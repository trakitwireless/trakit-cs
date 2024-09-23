using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using trakit.hmac;
using trakit.objects;

namespace trakit.wss {
	/// <summary>
	/// A wrapper for Trak-iT's <see cref="WebSocket"/> service, including service specific idiosyncrasies.
	/// </summary>
	public class TrakitSocket : IDisposable {
		#region Statics
		static Regex WHITESPACE = new Regex(@"[\r\n\s\t]+", RegexOptions.Compiled);
		/// <summary>
		/// Replaces all white-space sequences with a single space character, and trims the result.
		/// If the result is an empty string, it will instead return null.
		/// </summary>
		/// <param name="value"></param>
		/// <returns></returns>
		internal static string errorToReason(string value) {
			value = WHITESPACE.Replace(value ?? "", " ").Trim();
			return value == string.Empty ? null : value;
		}
		#endregion Statics

		/// <summary>
		/// The underlying connection.
		/// </summary>
		public ClientWebSocket client;
		/// <summary>
		/// 
		/// </summary>
		public TrakitSocketStatus status { get; private set; } = TrakitSocketStatus.closed;
		/// <summary>
		/// 
		/// </summary>
		/// <remarks>
		/// Prod - wss://socket.trakit.ca/
		/// Beta - wss://kraken.trakit.ca/
		/// </remarks>
		public Uri address;

		public TrakitSocket() {

		}
		public void Dispose() {
			var client = this.client;
			this.client = null;
			_sauce?.Cancel();
			client?.Abort();
			client?.Dispose();
		}

		#region Handling Disconnect
		// generic disconnect message
		const string BYEBYE = "Goodbye!";
		// token source for managing connecting, and incoming/outgoing messaging
		CancellationTokenSource _sauce;
		//
		object _shutlock = new { };
		//
		Task _shutter;
		//
		async Task _shutting(string closeMessage, WebSocketCloseStatus closeReason) {
			this.MessageReceived -= _receivingFirstMessage;
			_sauce.Cancel();
			_outgoing.CompleteAdding();
			try { await _sender; } catch { } finally { _sender.Dispose(); }
			try { await _receiver; } catch { } finally { _receiver.Dispose(); }
			_outgoing.Dispose();
			_outgoing = null;
			_sauce.Dispose();
			_sauce = null;
			var client = this.client;
			this.client = null;
			client.Abort();
			client.Dispose();
			_receiver =
			_sender =
			_shutter = null;

			_onStatus(TrakitSocketStatus.closed, closeMessage, closeReason);
		}
		//
		void _shutdown(string closeMessage, WebSocketCloseStatus closeReason) {
			lock (_shutlock) {
				_shutter = _shutter ?? _shutting(closeMessage, closeReason);
			}
		}
		#endregion Handling Disconnect
		#region Receiving Messages
		// 1mb buffer for receiving; way more than enough
		const int BUFFER = 1024 * 1024;
		// task to handle incoming messages and server-side disconnections
		Task _receiver;
		//
		async Task _receiving(CancellationToken ct) {
			string closeMessage = BYEBYE;
			WebSocketCloseStatus closeReason = WebSocketCloseStatus.NormalClosure;
			while (!ct.IsCancellationRequested && this.client?.State == WebSocketState.Open) {
				try {
					byte[] buffer = new byte[BUFFER];
					List<byte> message = new List<byte>();
					WebSocketReceiveResult received;
					do {
						received = await this.client.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
						message.AddRange(buffer.Take(received.Count));
					} while (!received.EndOfMessage);

					switch (received.MessageType) {
						case WebSocketMessageType.Text:
							this.MessageReceived?.Invoke(
								this,
								new TrakitSocketMessage(message)
							);
							break;
						case WebSocketMessageType.Close:
							_onStatus(TrakitSocketStatus.closing);
							await this.client.CloseOutputAsync(
								WebSocketCloseStatus.NormalClosure,
								closeMessage,
								ct
							);
							break;
						default:
							throw new TrakitSocketException(
								$"{received.MessageType} messages not supported",
								WebSocketCloseStatus.InvalidMessageType
							);
					}
				} catch (OperationCanceledException) {
					// CancellationToken cancelled
					_onStatus(TrakitSocketStatus.closing);
				} catch (Exception ex) {
					_onStatus(TrakitSocketStatus.closing);
					var reason = ex is TrakitSocketException tse
							? tse.reason
							: WebSocketCloseStatus.ProtocolError;
					closeMessage = ex.Message;
					closeReason = reason;
					await this.client.CloseOutputAsync(
						reason,
						closeMessage,
						ct
					);
				}
			}
			_shutdown(closeMessage, closeReason);
		}
		//
		void _receivingFirstMessage(TrakitSocket sender, TrakitSocketMessage message) {
			if (message.name == "connectionResponse") {
				this.MessageReceived -= _receivingFirstMessage;
				_onStatus(TrakitSocketStatus.open);
				// get resp-self
			}
		}
		#endregion Receiving Messages
		#region Sending Messages
		// task to handle outgoing messages and client-side disconnections
		Task _sender;
		// list of outgoing messages
		BlockingCollection<TrakitSocketMessage> _outgoing;
		//
		TrakitSocketMessage _closer;
		//
		async Task _sending(CancellationToken ct) {
			string closeMessage = BYEBYE;
			WebSocketCloseStatus closeReason = WebSocketCloseStatus.NormalClosure;
			try {
				while (_outgoing.TryTake(out TrakitSocketMessage message, -1, ct)) {
					if (_closer == null) {
						for (int offset = 0; offset < message.content.Length; offset += BUFFER) {
							int length = Math.Min(message.content.Length - offset, BUFFER);
							await (this.client?.SendAsync(
								new ArraySegment<byte>(message.content, offset, length),
								WebSocketMessageType.Text,
								offset + length == message.content.Length,
								ct
							) ?? Task.FromCanceled(ct));
						}
					} else {
						closeMessage = _closer.name;
						closeReason = _closer.reason;
						await _sendingClose(
							closeReason,
							closeMessage,
							ct
						);
						break;
					}
				}
			} catch (WebSocketException ex) {
				// socket disconnect
				closeMessage = ex.Message;
				if (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely) {
					closeReason = WebSocketCloseStatus.EndpointUnavailable;
					_onStatus(TrakitSocketStatus.closing);
				} else {
					closeReason = WebSocketCloseStatus.ProtocolError;
					await _sendingClose(
						closeReason,
						ex.Message,
						ct
					);
				}
			} catch (Exception ex) {
				closeMessage = ex.Message;
				closeReason = ex is TrakitSocketException tsx
						? tsx.reason
						: WebSocketCloseStatus.ProtocolError;
				await _sendingClose(
					closeReason,
					closeMessage,
					ct
				);
			}
			_shutdown(closeMessage, closeReason);
		}
		//
		async Task _sendingClose(WebSocketCloseStatus reason, string message, CancellationToken ct) {
			_onStatus(TrakitSocketStatus.closing);
			await (this.client?.CloseOutputAsync(
				reason,
				TrakitSocket.errorToReason(message) ?? reason.ToString(),
				ct
			) ?? Task.CompletedTask);
		}
		#endregion Sending Messages

		#region Initiate Connection
		//
		void _connectInit(IEnumerable<KeyValuePair<string, string>>? headers) {
			if (this.status != TrakitSocketStatus.open) throw new InvalidOperationException($"connection is {this.status}.");

			this.client = new ClientWebSocket();
			_sauce = new CancellationTokenSource();
			_outgoing = new BlockingCollection<TrakitSocketMessage>();
			if (headers?.Count() > 0) {
				foreach (var pair in headers) {
					this.client.Options.SetRequestHeader(pair.Key, pair.Value);
				}
			}
			this.MessageReceived += _receivingFirstMessage;
		}
		//
		string _connectUri() {
			var endpoint = this.address.AbsoluteUri.TrimEnd('?', '/', '&');
			return endpoint + (endpoint.Contains("?") ? "&" : "?");
		}
		//
		async Task _connectSend(string uri) {
			var conn = this.client.ConnectAsync(new Uri(uri), _sauce.Token);
			_onStatus(TrakitSocketStatus.opening);
			await conn;
			_receiver = _receiving(_sauce.Token);
			_sender = _sending(_sauce.Token);
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="sessionId"></param>
		/// <param name="headers"></param>
		/// <returns></returns>
		public async Task connect(
			Guid sessionId,
			IEnumerable<KeyValuePair<string, string>>? headers = null
		) {
			_connectInit(headers);
			await _connectSend(
				_connectUri()
				+ "ghostId=" + sessionId
			);
		}
		/// <summary>
		/// 
		/// </summary>
		/// <param name="username"></param>
		/// <param name="password"></param>
		/// <param name="headers"></param>
		/// <returns></returns>
		public async Task connect(
			MailAddress username,
			string password,
			IEnumerable<KeyValuePair<string, string>>? headers = null
		) {
			_connectInit(headers);
			await _connectSend(
				_connectUri()
				+ "username=" + username.Address
				+ "&password=" + password
			);
		}
		/// <summary>
		/// 
		/// </summary>
		/// <param name="apiKey"></param>
		/// <param name="apiSecret"></param>
		/// <param name="headers"></param>
		/// <returns></returns>
		public async Task connect(
			string apiKey,
			string apiSecret,
			IEnumerable<KeyValuePair<string, string>>? headers = null
		) {
			_connectInit(headers);
			var uri = _connectUri();
			uri += "shadowKey=" + apiKey
				+ "&shadowSig="
				+ signatures.createHmacHeader(
					apiKey,
					apiSecret,
					DateTime.UtcNow,
					HttpMethod.Get,
					new Uri(uri),
					0
				);
			await _connectSend(uri);
		}
		#endregion Initiate Connection
		#region Initiate Disconnection
		//
		Task _disconnecting() {
			var sauce = new TaskCompletionSource<bool>();
			void handler(TrakitSocket sender, string message, WebSocketCloseStatus reason) {
				if (this.status == TrakitSocketStatus.closed) {
					this.Disconnected -= handler;
					sauce.SetResult(true);
				}
			};
			this.Disconnected += handler;
			return sauce.Task;
		}
		/// <summary>
		/// 
		/// </summary>
		/// <param name="reason"></param>
		/// <param name="message"></param>
		/// <param name="forceClose"></param>
		/// <returns></returns>
		/// <exception cref="InvalidOperationException"></exception>
		public async Task disconnect(
			WebSocketCloseStatus reason = WebSocketCloseStatus.NormalClosure,
			string message = BYEBYE,
			bool forceClose = false
		) {
			if (this.status != TrakitSocketStatus.open) throw new InvalidOperationException($"connection is {this.status}.");

			var close = new TrakitSocketMessage(message, string.Empty, reason);
			if (forceClose || reason != WebSocketCloseStatus.NormalClosure) _closer = close;
			_outgoing.TryAdd(close, -1, _sauce.Token);

			await _disconnecting();
		}
		#endregion Initiate Disconnection
		#region Commands
		public async Task command(string name, object parameters) {

		}
		#endregion Commands

		#region Events
		/// <summary>
		/// Delegate for connection events.
		/// </summary>
		/// <param name="sender"></param>
		public delegate void ConnectionHandler(TrakitSocket sender);
		/// <summary>
		/// Delegate for disconnection events.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="message"></param>
		/// <param name="reason"></param>
		public delegate void DisconnectionHandler(TrakitSocket sender, string message, WebSocketCloseStatus reason);
		/// <summary>
		/// Delegate for incoming and outgoing message events.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="message"></param>
		/// <param name="type"></param>
		public delegate void MessageHandler(TrakitSocket sender, TrakitSocketMessage message);

		void _onStatus(
			TrakitSocketStatus status,
			string? message = null,
			WebSocketCloseStatus reason = WebSocketCloseStatus.Empty
		) {
			if (this.status != status) {
				this.status = status;
				this.StatusChanged?.Invoke(this);
				switch (status) {
					case TrakitSocketStatus.open:
						this.Connected?.Invoke(this);
						break;
					case TrakitSocketStatus.closed:
						this.Disconnected?.Invoke(this, message, reason);
						break;
				}
			}
		}


		/// <summary>
		/// Raised for each phase of the connection lifetime.
		/// </summary>
		public event ConnectionHandler StatusChanged;
		/// <summary>
		/// Raised when a connection is successfully established.
		/// </summary>
		public event ConnectionHandler Connected;
		/// <summary>
		/// Raised when the <see cref="client"/> is disconnected.
		/// </summary>
		public event DisconnectionHandler Disconnected;
		/// <summary>
		/// Raised when a message is sent to the server.
		/// </summary>
		public event MessageHandler MessageSent;
		/// <summary>
		/// Raised when a message is received from the server.
		/// </summary>
		public event MessageHandler MessageReceived;
		#endregion Events
	}
}