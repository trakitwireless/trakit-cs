using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Mail;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using trakit.commands;
using trakit.hmac;
using trakit.objects;
using trakit.tools;

namespace trakit.wss {
	/// <summary>
	/// A wrapper for Trak-iT's <see cref="WebSocket"/> service, including service specific idiosyncrasies.
	/// </summary>
	public class TrakitSocket : IDisposable {
		/// <summary>
		/// Production <see cref="WebSocket"/> service URL.
		/// This service is covered by the SLA and should be used for serices and code running in your own production environment.
		/// </summary>
		public const string URI_PROD = "wss://socket.trakit.ca";
		/// <summary>
		/// Testing or beta <see cref="WebSocket"/> service URL.
		/// This service is not covered by the SLA and should be used to test your own code before deployment.
		/// Throttling of connections and commands is tighter to help you diagnose issues before switching to production.
		/// </summary>
		/// <remarks>
		/// Both services access the same dataset, so be careful making changes as they will be reflected in production as well.
		/// </remarks>
		public const string URI_BETA = "wss://kraken.trakit.ca";

		#region Statics
		//sequential white space of all kinds
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
		/// Used to correlate requests and responses.
		/// </summary>
		int _reqId;
		/// <summary>
		/// <see cref="Uri"/> of the Trak-iT WebSocket service.
		/// </summary>
		public Uri baseAddress { get; private set; }
		/// <summary>
		/// This <see cref="WebSocket"/> wrapper's current connection status.
		/// </summary>
		/// <remarks>
		/// Does not exactly overlap the <see cref="WebSocketState"/> values.
		/// </remarks>
		public TrakitSocketStatus status { get; private set; } = TrakitSocketStatus.closed;

		/// <summary>
		/// The underlying connection.
		/// </summary>
		public ClientWebSocket client { get; private set; }
		/// <summary>
		/// Details of the <see cref="User"/> or <see cref="Machine"/> whose <see cref="Session"/> is connected to the <see cref="client"/>.
		/// </summary>
		public RespSelfDetails session { get; private set; }
		/// <summary>
		/// 
		/// </summary>
		public Serializer serializer { get; private set; }

		public TrakitSocket() : this(new Uri(URI_PROD)) { }
		public TrakitSocket(Uri baseAddress) {
			this.baseAddress = baseAddress;
		}
		public void Dispose() {
			var client = this.client;
			_sauce?.Cancel();
			this.client = null;
			client?.Abort();
			client?.Dispose();
		}

		#region Handling Disconnect
		// generic disconnect message
		const string BYEBYE = "Goodbye!";
		// token source for managing connecting, and incoming/outgoing messaging
		CancellationTokenSource _sauce;
		// flag for setting only one disconnect handler
		object _shutlock = new { };
		// this is called when either the client or server (not the user) initiates a disconnection
		void _shutdown(string closeMessage, WebSocketCloseStatus closeReason) {
			lock (_shutlock) {
				// it may be possible that this assignment happens twice, which is why the lock object is used.
				_shutter = _shutter ?? _shutting(closeMessage, closeReason);
			}
		}
		// the task handling the disconnect
		Task _shutter;
		// handles the disconnect, disposes of resources, and awaits tasks doing send/receive
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
		#endregion Handling Disconnect
		#region Receiving Messages
		// 1mb buffer for receiving; way more than enough
		const int BUFFER = 1024 * 1024;
		// task to handle incoming messages and server-side disconnections
		Task _receiver;
		// handles incoming messages and server initiated disconnections.
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
		// before we receive the connectionResponse message, the socket is in an unstable state
		// and can end the session if a command is sent
		// so we only mark this wrapper as "open" when the underlying connection is open, and we've received this message.
		void _receivingFirstMessage(TrakitSocket sender, TrakitSocketMessage message) {
			if (message.name == "connectionResponse") {
				this.MessageReceived -= _receivingFirstMessage;
				this.session = this.serializer.deserialize<RespSelfDetails>(message.body);
				_onStatus(TrakitSocketStatus.open);
			}
		}
		#endregion Receiving Messages
		#region Sending Messages
		// task to handle outgoing messages and client-side disconnections
		Task _sender;
		// list of outgoing messages
		BlockingCollection<TrakitSocketMessage> _outgoing;
		// a specific message to close the underlying connection immediately instead of waiting for the outgoing queue to clear
		TrakitSocketMessage _closer;
		// handles sending messages to the server, and initiating client requested disconnections
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
						message = _closer;
						closeMessage = message.name;
						closeReason = message.reason;
						await _sendingClose(
							closeReason,
							closeMessage,
							ct
						);
						break;
					}
					this.MessageSent?.Invoke(this, message);
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
		// sends the client requested close message with the reason and goodbye message
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
		// returns a clean Trak-iT socket address
		string _connectUri() {
			var endpoint = this.baseAddress.AbsoluteUri.TrimEnd('?', '/', '&');
			return endpoint + (endpoint.Contains("?") ? "&" : "?");
		}
		// instantiates a new ClientWebSocket, and associated class needed to run processes
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
		// an awaitable task which completes upon disconnection
		Task _connecting() {
			var sauce = new TaskCompletionSource<bool>();
			void handler(TrakitSocket sender) {
				this.StatusChanged -= handler;
				if (this.status == TrakitSocketStatus.open) {
					sauce.SetResult(true);
				} else {
					sauce.SetCanceled();
				}
			};
			this.StatusChanged += handler;
			return sauce.Task;
		}
		// sends the connection async, changes status to "opening", and starts sending/receiving tasks
		async Task _connectSend(string uri) {
			try {
				var conn = this.client.ConnectAsync(new Uri(uri), _sauce.Token);
				_onStatus(TrakitSocketStatus.opening);
				await conn;
			} catch {
				_onStatus(TrakitSocketStatus.closed);
				throw;
			}
			_receiver = _receiving(_sauce.Token);
			_sender = _sending(_sauce.Token);
			await _connecting();
		}

		/// <summary>
		/// Initiates a new <see cref="WebSocket"/> connection.
		/// </summary>
		/// <param name="sessionId"></param>
		/// <param name="headers"></param>
		/// <returns></returns>
		public Task connect(IEnumerable<KeyValuePair<string, string>>? headers = null) {
			_connectInit(headers);
			return _connectSend(_connectUri());
		}
		/// <summary>
		/// Resumes a <see cref="WebSocket"/> connection for an existing <see cref="Session"/>.
		/// </summary>
		/// <param name="sessionId"></param>
		/// <param name="headers"></param>
		/// <returns></returns>
		public Task connect(Guid sessionId, IEnumerable<KeyValuePair<string, string>>? headers = null) {
			_connectInit(headers);
			return _connectSend(_connectUri() + "ghostId=" + sessionId);
		}
		/// <summary>
		/// Initiates a <see cref="WebSocket"/> connection for a <see cref="User"/> account.
		/// </summary>
		/// <param name="username"></param>
		/// <param name="password"></param>
		/// <param name="headers"></param>
		/// <returns></returns>
		public Task connect(MailAddress username, string password, IEnumerable<KeyValuePair<string, string>>? headers = null) {
			_connectInit(headers);
			return _connectSend(_connectUri() + "username=" + username.Address + "&password=" + password);
		}
		/// <summary>
		/// Initiates a <see cref="WebSocket"/> connection for a <see cref="Machine"/> account.
		/// </summary>
		/// <param name="apiKey"><see cref="Machine.key"/></param>
		/// <param name="apiSecret"><see cref="Machine.secret"/> as bytes from <see cref="Encoding.UTF8"/></param>
		/// <param name="headers"></param>
		/// <returns></returns>
		public Task connect(string apiKey, byte[] apiSecret, IEnumerable<KeyValuePair<string, string>>? headers = null) {
			_connectInit(headers);
			var uri = _connectUri();
			uri += "shadowKey=" + apiKey
				+ "&shadowSig="
				+ signatures.createHmacSignedInput(
					apiKey,
					apiSecret,
					DateTime.UtcNow,
					HttpMethod.Get,
					new Uri(uri),
					0
				);
			return _connectSend(uri);
		}
		#endregion Initiate Connection
		#region Initiate Disconnection
		// an awaitable task which completes upon disconnection
		Task _disconnecting() {
			var sauce = new TaskCompletionSource<bool>();
			void handler(TrakitSocket sender) {
				switch (this.status) {
					case TrakitSocketStatus.closing:
						// do nothing, return instead of break so as to not unbind the handler
						return;
					case TrakitSocketStatus.closed:
						sauce.SetResult(true);
						break;
					default:
						sauce.SetCanceled();
						break;
				}
				this.StatusChanged -= handler;
			};
			this.StatusChanged += handler;
			return sauce.Task;
		}
		/// <summary>
		/// Initiates a disconnection of the Trak-iT <see cref="WebSocket"/> service.
		/// The disconnection will take place after all outbound messages are sent.
		/// However, if the <paramref name="reason"/> is anything other than <see cref="WebSocketCloseStatus.NormalClosure"/>,
		/// or the <paramref name="forceClose"/> is set to true, the sending process is interupted to send the close request first.
		/// </summary>
		/// <param name="reason">Reason for closing the connection.</param>
		/// <param name="message">Parting message.</param>
		/// <returns></returns>
		/// <exception cref="InvalidOperationException"></exception>
		public Task disconnect(
			WebSocketCloseStatus reason = WebSocketCloseStatus.NormalClosure,
			string message = BYEBYE
		) {
			if (this.status != TrakitSocketStatus.open) throw new InvalidOperationException($"connection is {this.status}.");

			_closer = new TrakitSocketMessage(message, string.Empty, reason);
			_outgoing.TryAdd(_closer, -1, _sauce.Token);

			return _disconnecting();
		}
		#endregion Initiate Disconnection
		#region Commands
		// command name reply suffix
		const string SUFFIX = "Response";
		/// <summary>
		/// Sends a command to the Trak-iT <see cref="WebSocket"/> service, and returns a <see cref="Task"/> that completes when a reply is received.
		/// </summary>
		/// <param name="name"></param>
		/// <param name="parameters"></param>
		/// <returns></returns>
		public Task<ResponseType> command(string name, ParameterType parameters) => this.command<ResponseType>(name, parameters);
		/// <summary>
		/// Sends a command to the Trak-iT <see cref="WebSocket"/> service, and returns a <see cref="Task"/> that completes when a reply is received.
		/// </summary>
		/// <typeparam name="TResponse"></typeparam>
		/// <param name="name"></param>
		/// <param name="parameters"></param>
		/// <returns></returns>
		/// <exception cref="InvalidOperationException"></exception>
		/// <exception cref="OperationCanceledException"></exception>
		public Task<TResponse> command<TResponse>(string name, ParameterType parameters) where TResponse : ResponseType {
			if (this.status != TrakitSocketStatus.open) throw new InvalidOperationException($"connection is {this.status}.");

			// let's track this request.
			parameters.reqId = ++_reqId;
			var message = new TrakitSocketMessage(name, this.serializer.serialize(parameters));

			var sauce = new TaskCompletionSource<TResponse>();
			void handleMsg(TrakitSocket sender, TrakitSocketMessage received) {
				if (received.name == message.name + SUFFIX) {
					var response = this.serializer.deserialize<TResponse>(message.body);
					if (response.reqId == parameters.reqId) {
						this.StatusChanged -= handleDis;
						this.MessageReceived -= handleMsg;
						sauce.SetResult(response);
					}
				}
			}
			void handleDis(TrakitSocket sender) {
				switch (this.status) {
					case TrakitSocketStatus.closing:
					case TrakitSocketStatus.closed:
						this.MessageReceived -= handleMsg;
						this.StatusChanged -= handleDis;
						sauce.SetCanceled();
						break;
				}
			};
			this.StatusChanged += handleDis;
			this.MessageReceived += handleMsg;

			// add to outgoing queue
			_outgoing.TryAdd(message, -1, _sauce.Token);
			return sauce.Task;
		}

		/// <summary>
		/// Subscribes the <see cref="client"/> to receive notifications for merge/delete changes to objects.
		/// </summary>
		/// <param name="company"></param>
		/// <param name="subscriptions"></param>
		/// <returns></returns>
		public Task<SubscriptionResponse> subscribe(ulong company, IEnumerable<SubscriptionType> subscriptions)
			=> this.command<SubscriptionResponse>("subscribe", new SubscriptionRequest() {
				company = new ParamId() { id = company },
				subscriptionTypes = subscriptions.ToList()
			});
		/// <summary>
		/// Unsubscribes the <see cref="client"/> to receive notifications for merge/delete changes to objects.
		/// </summary>
		/// <param name="company"></param>
		/// <param name="subscriptions"></param>
		/// <returns></returns>
		public Task<SubscriptionResponse> unsubscribe(ulong company, IEnumerable<SubscriptionType> subscriptions)
			=> this.command<SubscriptionResponse>("unsubscribe", new SubscriptionRequest() {
				company = new ParamId() { id = company },
				subscriptionTypes = subscriptions.ToList()
			});
		/// <summary>
		/// Gets the list of current subscriptions for the <see cref="client"/>.
		/// </summary>
		/// <returns></returns>
		public Task<SubscriptionListResponse> subscriptionList()
			=> this.command<SubscriptionListResponse>("getSubscriptionsList", new RequestBlank());
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

		// flag for setting status
		object _statlock = new { };
		// changes the status and raises the appropriate events
		void _onStatus(
			TrakitSocketStatus status,
			string message = BYEBYE,
			WebSocketCloseStatus reason = WebSocketCloseStatus.Empty
		) {
			lock (_statlock) {
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