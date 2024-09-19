using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
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
		public TrakitSocketStatus status = TrakitSocketStatus.closed;
		/// <summary>
		/// 
		/// </summary>
		public Uri address;

		public TrakitSocket() {

		}
		public void Dispose() {
			var client = this.client;
			this.client = null;
			client?.Dispose();
			_sauce?.Cancel();
			client?.Abort();
			client?.Dispose();
		}

		#region Sending and Receiving Messages
		const string BYEBYE = "Goodbye!";
		const int BUFFER = 1024 * 1024;
		CancellationTokenSource _sauce;
		Task _receiver, _sender;
		BlockingCollection<TrakitSocketMessage> _outgoing;
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
							await this.client.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, closeMessage, ct);
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
				} catch (WebSocketException ex) {
					// socket disconnect
					_onStatus(TrakitSocketStatus.closing);
					closeMessage = ex.Message;
					closeReason = WebSocketCloseStatus.ProtocolError;
					await this.client.CloseOutputAsync(WebSocketCloseStatus.ProtocolError, ex.Message, ct);
				} catch (Exception ex) {
					_onStatus(TrakitSocketStatus.closing);
					var reason = ex is TrakitSocketException tse
							? tse.reason
							: WebSocketCloseStatus.InternalServerError;
					closeMessage = ex.Message;
					closeReason = reason;
					await this.client.CloseOutputAsync(reason, ex.Message, ct);
				}
			}
			_onStatus(TrakitSocketStatus.closed, closeMessage, closeReason);
		}
		void _connectionResponse(TrakitSocket sender, TrakitSocketMessage message) {
			if (message.name == "connectionResponse") {
				this.MessageReceived -= _connectionResponse;
				_onStatus(TrakitSocketStatus.open);
				// get resp-self
			}
		}

		/// <summary>
		/// This message is a priority message that closes the socket.  It is only set for abnormal states
		/// where closing the connection is a priority before sending any other messages in the
		/// <see cref="outgoing"/> queue.
		/// </summary>
		TrakitSocketMessage _closer;
		async Task _sending(CancellationToken ct) {
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
						await _sendingClose(
							WebSocketCloseStatus.NormalClosure,
							message.name,
							ct
						);
						break;
					}
				}
			} catch (WebSocketException ex) {
				// socket disconnect
				if (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely) {
					_sauce?.Cancel();
				} else {
					await _sendingClose(
						WebSocketCloseStatus.ProtocolError,
						ex.Message,
						ct
					);
				}
			} catch (Exception ex) {
				await _sendingClose(
					ex is TrakitSocketException tsx
						? tsx.reason
						: WebSocketCloseStatus.ProtocolError,
					ex.Message,
					ct
				);
			}
			_outgoing.CompleteAdding();
		}
		async Task _sendingClose(WebSocketCloseStatus reason, string message, CancellationToken ct) {
			_onStatus(TrakitSocketStatus.closing);
			await (this.client?.CloseOutputAsync(
				reason,
				TrakitSocket.errorToReason(message) ?? reason.ToString(),
				ct
			) ?? Task.CompletedTask);
		}
		#endregion Sending and Receiving Messages

		public async Task connect(IEnumerable<KeyValuePair<string, string>>? headers = null) {
			this.client = new ClientWebSocket();
			_sauce = new CancellationTokenSource();
			_outgoing = new BlockingCollection<TrakitSocketMessage>();
			if (headers?.Count() > 0) {
				foreach (var pair in headers) {
					this.client.Options.SetRequestHeader(pair.Key, pair.Value);
				}
			}
			this.MessageReceived += _connectionResponse;
			await this.client.ConnectAsync(this.address, _sauce.Token);
			_onStatus(TrakitSocketStatus.opening);
			_receiver = _receiving(_sauce.Token);
			_sender = _sending(_sauce.Token);
		}
		public async Task disconnect(WebSocketCloseStatus reason = WebSocketCloseStatus.NormalClosure, string message = BYEBYE) {
			_closer = new TrakitSocketMessage(message, string.Empty, reason);
			_outgoing.TryAdd(_closer, -1, _sauce.Token);
			try { await _sender; } catch { } finally { _sender?.Dispose(); }
			try { await _receiver; } catch { } finally { _receiver?.Dispose(); }
			var client = this.client;
			this.client = null;
			client?.Dispose();
			_sauce?.Dispose();
			_sauce = null;
			_receiver =
				_sender = null;
		}
		public async Task command(string name, object parameters) {

		}

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
		public event ConnectionHandler Status;
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