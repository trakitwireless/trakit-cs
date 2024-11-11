using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using trakit.commands;
using trakit.hmac;
using trakit.tools;

namespace trakit.wss {
	/// <summary>
	/// A wrapper for Trak-iT's <see cref="WebSocket"/> service, including service specific idiosyncrasies.
	/// </summary>
	public sealed class TrakitSocket : Commander, IDisposable {
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
		/// <see cref="Uri"/> of the Trak-iT WebSocket service.
		/// </summary>
		public Uri baseAddress { get; private set; }
		/// <summary>
		/// The underlying connection.
		/// </summary>
		public ClientWebSocket client { get; private set; }
		/// <summary>
		/// This <see cref="WebSocket"/> wrapper's current connection status.
		/// </summary>
		/// <remarks>
		/// Does not exactly overlap the <see cref="WebSocketState"/> values.
		/// </remarks>
		public TrakitSocketStatus status { get; private set; } = TrakitSocketStatus.closed;

		public TrakitSocket() : this(new Uri(URI_PROD)) { }
		public TrakitSocket(Uri baseAddress) {
			this.baseAddress = baseAddress;
		}
		/// <summary>
		/// Disposes of the status setting task.
		/// </summary>
		public void Dispose() {
			var wss = this.client;
			this.client = null;
			wss?.Abort();
			wss?.Dispose();
			wss = null;
		}

		#region Events
		// and Waldorf
		object _statler = new { };
		// changes the status and raises the appropriate events
		void _onStatus(
			TrakitSocketStatus status,
			string message = BYEBYE,
			WebSocketCloseStatus reason = WebSocketCloseStatus.Empty,
			bool silent = false
		) {
			lock (_statler) {
				if (this.status != status) {
					this.status = status;
					this.StatusChanged?.Invoke(this);
					switch (status) {
						case TrakitSocketStatus.opened:
							this.Connected?.Invoke(this);
							break;
						case TrakitSocketStatus.closed:
							if (!silent) this.Disconnected?.Invoke(this, message, reason);
							break;
					}
				}
			}
		}

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
		#region Connection/Disconnection
		// token source for managing connecting, and incoming/outgoing messaging
		CancellationTokenSource _sauce;
		// an awaitable task which completes upon disconnection
		Task _connecting() {
			var sauce = new TaskCompletionSource<TrakitSocketStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
			void handler(TrakitSocket sender) {
				this.StatusChanged -= handler;
				if (
					this.status != TrakitSocketStatus.opened
					|| !sauce.TrySetResult(this.status)
				) {
					sauce.SetCanceled();
				}
			}
			this.StatusChanged += handler;
			return sauce.Task;
		}
		// generic disconnect message
		const string BYEBYE = "Goodbye!";
		// flag for setting only one disconnect handler
		object _shutlock = new { };
		// the task handling the disconnect
		Task _shutter;
		// this is called when either the client or server (not the user) initiates a disconnection
		void _shutdown(string closeMessage, WebSocketCloseStatus closeReason) {
			lock (_shutlock) {
				// it may be possible that this assignment happens twice, which is why the lock object is used.
				_shutter = _shutter ?? Task.Run(async () => await _shutting(closeMessage, closeReason).ConfigureAwait(false));
			}
		}
		// handles the disconnect, disposes of resources, and awaits tasks doing send/receive
		async Task _shutting(string closeMessage, WebSocketCloseStatus closeReason) {
			_sauce.Cancel();
			_outgoing.CompleteAdding();
			try { await _sender; } catch { _sender = null; } finally { _sender?.Dispose(); }
			try { await _receiver; } catch { _receiver = null; } finally { _receiver?.Dispose(); }
			var wss = this.client;
			this.client = null;
			_outgoing.Dispose();
			_outgoing = null;
			_sauce.Dispose();
			_sauce = null;
			wss.Abort();
			wss.Dispose();
			_sender =
			_receiver = null;

			_onStatus(TrakitSocketStatus.closed, closeMessage, closeReason);
		}
		// an awaitable task which completes upon disconnection
		Task _disconnecting() {
			var sauce = new TaskCompletionSource<TrakitSocketStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
			void handler(TrakitSocket sender) {
				// do nothing and return (do not unbind the handler)
				// this is a normal part of the disconnection routine
				if (this.status == TrakitSocketStatus.closing) return;

				this.StatusChanged -= handler;
				if (
					this.status != TrakitSocketStatus.closed
					|| !sauce.TrySetResult(this.status)
				) {
					sauce.SetCanceled();
				}
			}
			this.StatusChanged += handler;
			if (this.status == TrakitSocketStatus.closed) sauce.TrySetResult(this.status);
			return sauce.Task;
		}

		/// <summary>
		/// Initiates a new <see cref="WebSocket"/> connection.
		/// </summary>
		/// <param name="headers"></param>
		/// <param name="ct"></param>
		/// <returns></returns>
		/// <exception cref="InvalidOperationException"></exception>
		public async Task connect(IEnumerable<KeyValuePair<string, string>> headers = null, CancellationToken? ct = null) {
			if (this.status != TrakitSocketStatus.closed) throw new InvalidOperationException($"connection is {this.status}.");

			_waitingForConnResp = true;
			_closer = null;
			_shutter = null;
			_sauce = new CancellationTokenSource();
			_outgoing = new BlockingCollection<TrakitSocketMessage>();
			this.client = new ClientWebSocket();
			if (headers?.Count() > 0) {
				foreach (var pair in headers) {
					this.client.Options.SetRequestHeader(pair.Key, pair.Value);
				}
			}
			var uri = $"{this.baseAddress.AbsoluteUri.TrimEnd('/')}/";
			if (_machine != default) {
				this.client.Options.SetRequestHeader(
					"Authorization",
					"HMAC256 " + Convert.ToBase64String(Encoding.UTF8.GetBytes(
						_machine.key
						+ ":"
						+ signatures.createHmacSignedInput(
							_machine.key,
							_machine.secret,
							DateTime.UtcNow,
							HttpMethod.Get,
							new Uri(uri),
							0
						)
					))
				);
			} else {
				uri += $"{(uri.Contains("?") ? "&" : "?")}ghostId={_sessionId}";
			}
			var source = ct.HasValue
					? CancellationTokenSource.CreateLinkedTokenSource(_sauce.Token, ct.Value)
					: _sauce;
			try {
				_onStatus(TrakitSocketStatus.opening);
				await this.client.ConnectAsync(new Uri(uri), source.Token);
				var conn = _connecting();
				_receiver = Task.Run(_receiving, _sauce.Token);
				_sender = Task.Run(_sending, _sauce.Token);
				await conn.ConfigureAwait(false);
			} catch {
				source.Cancel();
				source.Dispose();
				_onStatus(TrakitSocketStatus.closed, silent: true);
				throw;
			}
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
			if (this.status != TrakitSocketStatus.opened) throw new InvalidOperationException($"connection is {this.status}.");

			_closer = _closer ?? new TrakitSocketMessage(message, string.Empty, reason);
			_outgoing.TryAdd(_closer, -1, _sauce.Token);

			return _disconnecting();
		}
		#endregion Connection/Disconnection

		#region Messages - Receiving
		// 1mb buffer for receiving; way more than enough
		const int BUFFER = 1024 * 1024;
		// task to handle incoming messages and server-side disconnections
		Task _receiver;
		// before we receive the connectionResponse message, the socket is in an unstable state
		// and can end the session if a command is sent
		// so we only mark this wrapper as "open" when the underlying connection is open, and we've received this message.
		bool _waitingForConnResp;
		// handles incoming messages and server initiated disconnections.
		async Task _receiving() {
			var ct = _sauce.Token;
			string closeMessage = BYEBYE;
			WebSocketCloseStatus closeReason = WebSocketCloseStatus.NormalClosure;
			try {
				while (!ct.IsCancellationRequested && this.client?.State == WebSocketState.Open) {
					byte[] buffer = new byte[BUFFER];
					List<byte> message = new List<byte>();
					WebSocketReceiveResult received;
					do {
						received = await this.client.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
						message.AddRange(buffer.Take(received.Count));
					} while (!received.EndOfMessage);

					switch (received.MessageType) {
						case WebSocketMessageType.Text:
							var msg = new TrakitSocketMessage(message);
							if (_waitingForConnResp && msg.name == "connectionResponse") {
								_waitingForConnResp = false;
								this.self = this.serializer.deserialize<RespSelfDetails>(msg.body);
								_onStatus(TrakitSocketStatus.opened);
							}
							this.MessageReceived?.Invoke(this, msg);
							break;
						case WebSocketMessageType.Close:
							_onStatus(TrakitSocketStatus.closing);
							await this.client.CloseOutputAsync(
								received.CloseStatus ?? WebSocketCloseStatus.NormalClosure,
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
			_shutdown(closeMessage, closeReason);
		}
		#endregion Messages - Receiving
		#region Messages - Sending
		// task to handle outgoing messages and client-side disconnections
		Task _sender;
		// list of outgoing messages
		BlockingCollection<TrakitSocketMessage> _outgoing;
		// a specific message to close the underlying connection immediately instead of waiting for the outgoing queue to clear
		TrakitSocketMessage _closer;
		// handles sending messages to the server, and initiating client requested disconnections
		async Task _sending() {
			var ct = _sauce.Token;
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
						await _close(
							closeReason,
							closeMessage,
							ct
						);
						break;
					}
					this.MessageSent?.Invoke(this, message);
				}
			} catch (OperationCanceledException) {
				// shutting down
			} catch (WebSocketException ex) {
				// socket disconnect
				closeMessage = ex.Message;
				if (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely) {
					closeReason = WebSocketCloseStatus.EndpointUnavailable;
					_onStatus(TrakitSocketStatus.closing);
				} else {
					closeReason = WebSocketCloseStatus.ProtocolError;
					await _close(
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
				await _close(
					closeReason,
					closeMessage,
					ct
				);
			}
			_shutdown(closeMessage, closeReason);
		}
		// sends the client requested close message with the reason and goodbye message
		Task _close(WebSocketCloseStatus reason, string message, CancellationToken ct) {
			_onStatus(TrakitSocketStatus.closing);
			return (this.client?.CloseOutputAsync(
				reason,
				TrakitSocket.errorToReason(message) ?? reason.ToString(),
				ct
			) ?? Task.CompletedTask);
		}
		#endregion Messages - Sending

		/// <summary>
		/// Sends a command to the Trak-iT <see cref="WebSocket"/> service, and returns a <see cref="Task"/> that completes when a reply is received.
		/// </summary>
		/// <typeparam name="TResponse"></typeparam>
		/// <param name="request"></param>
		/// <returns></returns>
		/// <exception cref="InvalidOperationException"></exception>
		public override async Task<TResponse> command<TResponse>(Request request)
			=> this.serializer.convertFrom<TResponse>(
				await this.command(
					_getCommandName(request),
					this.serializer.convertTo<JObject>(request)
				)
			);

		#region Commands
		// command name reply suffix
		const string RESPONSE_SUFFIX = "Response";
		// converts the Request type into a WebSocket command name
		static string _getCommandName<TRequest>(TRequest request) where TRequest : Request {
			var matches = request.getNameParts();
			if (matches.Length >= 2) {
				string objName = matches[0],
					cmdName = matches[1].ToLowerInvariant();
				switch (objName) {
					case "Subscription":
						switch (cmdName) {
							case "merge":
								return "subscribe";
							case "delete":
							case "remove":
								return "unsubscribe";
							case "list":
								return "getSubscriptionsList";
						}
						break;
				}
				switch (cmdName) {
					case "login":
					case "logout":
						return cmdName;

					case "get":
					case "merge":
					case "restore":
					case "suspend":
					case "revive":
					default:
						return cmdName + objName;
					case "delete":
					case "remove":
						return "remove" + objName;
					case "list":
						cmdName = "get" + text.plural(objName) + "List";
						if (matches.Length > 2 && matches[2] != "ByCompany") {
							cmdName += matches[2];
						}
						return cmdName;
				}
			}
			throw new NotImplementedException($"no command supported for {typeof(TRequest).Name}");
		}
		/// <summary>
		/// Sends a command to the Trak-iT <see cref="WebSocket"/> service, and returns a <see cref="Task"/> that completes when a reply is received.
		/// This command allows you to work with the API in raw JSON instead of relying no the Trak-iT API <see cref="Output"/> classes.
		/// </summary>
		/// <typeparam name="TJson"></typeparam>
		/// <param name="name"></param>
		/// <param name="parameters"></param>
		/// <returns></returns>
		/// <exception cref="InvalidOperationException"></exception>
		public Task<JObject> command(string name, JObject parameters) {
			if (this.status != TrakitSocketStatus.opened) throw new InvalidOperationException($"connection is {this.status}.");

			// let's track this request.
			parameters["reqId"] = ++_reqId;
			var outbound = new TrakitSocketMessage(name, this.serializer.serialize(parameters));

			var sauce = new TaskCompletionSource<JObject>();
			void handleMsg(TrakitSocket sender, TrakitSocketMessage received) {
				if (received.name == outbound.name + RESPONSE_SUFFIX) {
					var response = this.serializer.deserialize<JObject>(received.body);
					if (
						int.TryParse(response?["reqId"]?.ToString(), out int reqId)
						&& reqId == (int)parameters["reqId"]
					) {
						this.MessageReceived -= handleMsg;
						this.StatusChanged -= handleDis;
						if (!sauce.TrySetResult(response)) {
							sauce.SetCanceled();
						}
					}
				}
			}
			void handleDis(TrakitSocket sender) {
				this.MessageReceived -= handleMsg;
				this.StatusChanged -= handleDis;
				sauce.SetCanceled();
			}
			this.MessageReceived += handleMsg;
			this.StatusChanged += handleDis;

			// add to outgoing queue
			var ct = _sauce.Token;
			return _outgoing.TryAdd(outbound, -1, ct)
				? sauce.Task
				: Task.FromCanceled<JObject>(ct);
		}
		#endregion Commands
		#region Commands - Subscription
		/// <summary>
		/// Subscribes the <see cref="client"/> to receive notifications for merge/delete changes to objects.
		/// </summary>
		/// <param name="company"></param>
		/// <param name="subscriptions"></param>
		/// <returns></returns>
		public Task<RespSubscription> subscribe(ulong company, IEnumerable<SubscriptionType> subscriptions)
			=> this.command<RespSubscription>(new ReqSubscriptionMerge() {
				company = new ParamId() { id = company },
				subscriptionTypes = subscriptions.ToArray()
			});
		/// <summary>
		/// Unsubscribes the <see cref="client"/> to receive notifications for merge/delete changes to objects.
		/// </summary>
		/// <param name="company"></param>
		/// <param name="subscriptions"></param>
		/// <returns></returns>
		public Task<RespSubscription> unsubscribe(ulong company, IEnumerable<SubscriptionType> subscriptions)
			=> this.command<RespSubscription>(new ReqSubscriptionRemove() {
				company = new ParamId() { id = company },
				subscriptionTypes = subscriptions.ToArray()
			});
		/// <summary>
		/// Gets the list of current subscriptions for the <see cref="client"/>.
		/// </summary>
		/// <returns></returns>
		public Task<RespSubscriptionList> subscriptionList()
			=> this.command<RespSubscriptionList>(new ReqSubscriptionList());
		#endregion Commands - Subscription
	}
}