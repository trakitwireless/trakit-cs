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
using System.Timers;
using Newtonsoft.Json.Linq;
using Trakit.Commands;
using Trakit.Https.Extensions;
using Trakit.Objects;
using Trakit.Tools;
using Timer = System.Timers.Timer;

namespace Trakit.Socket {
	/// <summary>
	/// Uses Trak-iT's <see cref="WebSocket"/> service to access and manipulate all <see cref="Component">Trak-iT API Objects</see>.
	/// </summary>
	public sealed class TrakitSocketCommander : TrakitObjectCommander<ClientWebSocket>, IDisposable {
		/// <summary>
		/// Production <see cref="WebSocket"/> service URL.
		/// </summary>
		/// <remarks>
		/// This service is covered by the SLA and should be used for serices and code running in your own production environment.
		/// Both services access the same data-set, so be careful making changes as they will be reflected in production as well.
		/// </remarks>
		public const string URI_PROD = "wss://socket.trakit.ca";
		/// <summary>
		/// Testing or beta <see cref="WebSocket"/> service URL.
		/// </summary>
		/// <remarks>
		/// This service is not covered by the SLA and should be used to test your own code before deployment.
		/// Throttling of connections and commands is tighter to help you diagnose issues before switching to production.
		/// Both services access the same data-set, so be careful making changes as they will be reflected in production as well.
		/// </remarks>
		public const string URI_BETA = "wss://kraken.trakit.ca";
		#region Statics
		// sequential white space of all kinds
		static Regex WHITESPACE = new Regex(@"[\r\n\s\t]+", RegexOptions.Compiled);
		// maximum close reason phrase length (who chose this?)
		const int CLOSE_REASON_LIMIT = 123;
		/// <summary>
		/// Replaces all white-space sequences with a single space character, trims the result, and limits the length to 123 characters.
		/// If the result is an empty string, it will instead return null.
		/// </summary>
		/// <remarks>
		/// The actual reason phrase may contain fewer than 123 characters because the phrase is UTF-8 encoded,
		/// so some non-ASCII characters may be corrupted if they occur at the end of the limit.
		/// For more information on close reason phrases, see https://developer.mozilla.org/en-US/docs/Web/API/WebSocket/close
		/// </remarks>
		/// <param name="value"></param>
		/// <returns></returns>
		internal static string errorToReason(string value) {
			byte[] bytes = Encoding.UTF8.GetBytes(WHITESPACE.Replace(value ?? "", " ").Trim());
			if (bytes.Length > 0) {
				string reason = Encoding.UTF8.GetString(bytes.Take(CLOSE_REASON_LIMIT).ToArray());
				return value.StartsWith(reason)    // GetString could suffix the reason with `?` making it 125 bytes long
					? reason
					: reason.Substring(0, reason.Length - 1);
			}
			return default;
		}
		#endregion Statics

		/// <summary>
		/// This <see cref="WebSocket"/> wrapper's current connection status.
		/// </summary>
		/// <remarks>
		/// Does not exactly overlap the <see cref="WebSocketState"/> values.
		/// </remarks>
		public TrakitSocketStatus Status { get; private set; } = TrakitSocketStatus.Closed;
		/// <summary>
		/// Timestamp recorded right after establishing a connection and receiving the <c>connectionResponse</c> message.
		/// </summary>
		public DateTime LastConnected { get; private set; }
		/// <summary>
		/// Timestamp recorded right after sending the most recent <see cref="WebSocketMessageType.Text"/> message was completed.
		/// </summary>
		public DateTime LastSent { get; private set; }
		/// <summary>
		/// Timestamp recorded right after receiving the most recent <see cref="WebSocketMessageType.Text"/> message.
		/// </summary>
		public DateTime LastReceived { get; private set; }
		/// <summary>
		/// When set to a value of 30 seconds or more, will send a <c>noop {}</c> message to the server.
		/// </summary>
		/// <remarks>
		/// In some firewalled environments, the built-in <see cref="WebSocket.DefaultKeepAliveInterval"/> is
		/// insufficient. Enabling this value will still not guaruntee your connection will remain open, but
		/// should make it more stable, or provide more immediate notice of severed connections. If you use
		/// this option, we recommend setting 5 minutes or more.
		/// </remarks>
		public TimeSpan NoopKeepAlive {
			get => _noopTimeout;
			set {
				_noopTimeout = value;
				_noop.Interval = Math.Max(_noopTimeout.TotalMilliseconds, _noopDefault);
				_noop.Enabled = _noop.Interval > _noopDefault;    // resets timer
				if (_noop.Enabled && this.Status == TrakitSocketStatus.Opened) {
					// if the connection is already open
					// trigger noop right awway
					_noopElapsed(_noop, default);
				}
			}
		}
		/// <summary>
		/// When set to true, will automatically reconnect to the <see cref="ClientWebSocket"/>.
		/// </summary>
		public bool ReconnectEnabled {
			get => !(_reconSauce?.IsCancellationRequested ?? true);
			set {
				if (value != this.ReconnectEnabled) _reconSauce?.Cancel();
				_reconSauce = value
						? new CancellationTokenSource()
						: default;
			}
		}
		/// <summary>
		/// Amount of time to wait after disconnection to automatically re-establish the connection.
		/// </summary>
		public TimeSpan ReconnectDelay => TimeSpan.FromMilliseconds(_reconDelay);

		public TrakitSocketCommander() : this(new Uri(URI_PROD)) { }
		public TrakitSocketCommander(Uri baseAddress) {
			this.BaseAddress = baseAddress;
			this.Client = new ClientWebSocket();
			_noop.Elapsed += _noopElapsed;
		}
		/// <summary>
		/// Disposes of the status setting task.
		/// </summary>
		public void Dispose() {
			var wss = this.Client;
			this.Client = default;
			wss?.Abort();
			wss?.Dispose();
			wss = default;
		}

		#region Events
		/// when true, will trigger the <see cref="Disconnected"/> event?
		bool _wasOpen;
		/// and Waldorf
		object _statler = new { };
		/// changes the status and raises the appropriate events
		void _onStatus(
			TrakitSocketStatus status,
			string message = BYEBYE,
			WebSocketCloseStatus reason = WebSocketCloseStatus.Empty
		) {
			lock (_statler) {
				if (this.Status != status) {
					this.Status = status;
					this.StatusChanged?.Invoke(this);
					switch (status) {
						case TrakitSocketStatus.Opened:
							_wasOpen = true;
							_reconDelay = _reconMin;
							_noop.Enabled = _noop.Interval > _noopDefault;
							this.LastConnected = this.LastReceived;
							this.Connected?.Invoke(this);
							break;
						case TrakitSocketStatus.Closed:
							_noop.Enabled = false;
							if (this.ReconnectEnabled) _reconnecter = _reconnecter ?? Task.Run(_reconnecting, _reconSauce.Token);
							if (_wasOpen) this.Disconnected?.Invoke(this, message, reason);
							_wasOpen = false;
							break;
					}
				}
			}
		}

		/// <summary>
		/// Delegate for connection events.
		/// </summary>
		/// <param name="socket"></param>
		public delegate void ConnectionHandler(TrakitSocketCommander socket);
		/// <summary>
		/// Delegate for disconnection events.
		/// </summary>
		/// <param name="socket"></param>
		/// <param name="message"></param>
		/// <param name="reason"></param>
		public delegate void DisconnectionHandler(TrakitSocketCommander socket, string message, WebSocketCloseStatus reason);
		/// <summary>
		/// Delegate for incoming and outgoing message events.
		/// </summary>
		/// <param name="socket"></param>
		/// <param name="message"></param>
		/// <param name="type"></param>
		public delegate void MessageHandler(TrakitSocketCommander socket, TrakitSocketMessage message);

		/// <summary>
		/// Raised for each phase of the connection lifetime.
		/// </summary>
		public event ConnectionHandler StatusChanged;
		/// <summary>
		/// Raised when a connection is successfully established.
		/// </summary>
		public event ConnectionHandler Connected;
		/// <summary>
		/// Raised when the <see cref="Client"/> is disconnected.
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
		#region Connection
		/// is completed (or cancelled) after a (dis)connection process is completed
		TaskCompletionSource<TrakitSocketStatus> _connSauce;
		/// waits for and resets the (dis)connection process
		void _resetConn() {
			try { _connSauce?.Task.Wait(); } catch { }
			_connSauce = new TaskCompletionSource<TrakitSocketStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
		}
		/// token source for managing (dis)connection, and incoming/outgoing messaging
		CancellationTokenSource _runSauce;
		/// an awaitable task which completes after setting <see cref="TrakitObjectCommander{TClient}.Self"/>
		Task _connecting() {
			void handler(TrakitSocketCommander sender) {
				this.StatusChanged -= handler;
				if (
					this.Status == TrakitSocketStatus.Opened
					&& _connSauce.TrySetResult(this.Status)
				) {
					_sender = Task.Run(_sending, _runSauce.Token);
				} else {
					_connSauce.TrySetCanceled();
				}
			}
			this.StatusChanged += handler;
			_receiver = Task.Run(_receiving, _runSauce.Token);
			return _connSauce.Task;
		}

		/// <summary>
		/// Initiates a new <see cref="WebSocket"/> connection.
		/// </summary>
		/// <param name="ct"></param>
		/// <returns></returns>
		/// <exception cref="InvalidOperationException"></exception>
		public async Task Connect(CancellationToken ct = default) {
			if (this.Status != TrakitSocketStatus.Closed) throw new InvalidOperationException($"Connection is {this.Status}.");
			_resetConn();

			_reconToken = ct;
			_closer = default;
			_shutter = default;
			_reconnecter = default;
			_runSauce = new CancellationTokenSource();
			_outgoing = new BlockingCollection<TrakitSocketMessage>();
			this.Client = new ClientWebSocket();

			var source = CancellationTokenSource.CreateLinkedTokenSource(_runSauce.Token, ct);
			var uri = this.CreateBaseUri().Uri;
			// add headers
			foreach (var pair in this.Headers) {
				this.Client.Options.SetRequestHeader(pair.Key, pair.Value);
			}
			// add machine
			if (_machine != default) {
				this.Client.Options.SetRequestHeader(
					"Authorization",
					_machine.secret?.Length > 0
						? "HMAC256 " + _machine.CreateHmacCreateSignature(
							DateTime.UtcNow,
							HttpMethod.Get,
							uri,
							0
						)
						: $"Machine " + Convert.ToBase64String(Encoding.UTF8.GetBytes(_machine.key))
				);
			}
			try {
				_onStatus(TrakitSocketStatus.Opening);
				await this.Client.ConnectAsync(uri, source.Token);
				await _connecting().ConfigureAwait(false);
			} catch (Exception ex) {
				_runSauce.Cancel();
				_onStatus(TrakitSocketStatus.Closed);
				_connSauce.TrySetException(ex);
				throw;
			}
		}
		#endregion Connection
		#region Disconnection
		/// generic disconnect message
		const string BYEBYE = "Goodbye!";
		/// the task handling the disconnect
		Task _shutter;
		/// this is called when either the client or server (not the user) initiates a disconnection
		void _shutdown(string message, WebSocketCloseStatus reason) {
			lock (this) {
				// it may be possible that this assignment happens twice, which is why the lock object is used.
				_shutter = _shutter ?? Task.Run(() => _shutting(message, reason));
			}
		}
		/// handles the disconnect, disposes of resources, and awaits tasks doing send/receive
		void _shutting(string message, WebSocketCloseStatus reason) {
			var wss = this.Client;
			_runSauce.Cancel();
			_outgoing.CompleteAdding();
			try { _sender?.Wait(); } catch { }
			try { _receiver?.Wait(); } catch { }
			this.Client = default;
			_outgoing.Dispose();
			_outgoing = default;
			wss?.Abort();
			wss?.Dispose();
			_sender =
			_receiver = default;

			_onStatus(TrakitSocketStatus.Closed, message, reason);
			_connSauce.TrySetResult(this.Status);
		}
		/// an awaitable task which completes upon disconnection
		Task _disconnecting() {
			void handler(TrakitSocketCommander sender) {
				// do nothing and return (do not unbind the handler)
				// this is a normal part of the disconnection routine
				if (this.Status == TrakitSocketStatus.Closing) return;

				this.StatusChanged -= handler;
				if (
					this.Status != TrakitSocketStatus.Closed
					|| !_connSauce.TrySetResult(this.Status)
				) {
					_connSauce.TrySetCanceled();
				}
			}
			this.StatusChanged += handler;
			return _connSauce.Task;
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
		public Task Disconnect(
			WebSocketCloseStatus reason = WebSocketCloseStatus.NormalClosure,
			string message = BYEBYE
		) {
			if (this.Status != TrakitSocketStatus.Opened) throw new InvalidOperationException($"Connection is {this.Status}.");
			_resetConn();

			var disco = _disconnecting();
			_closer = _closer ?? new TrakitSocketMessage(message, string.Empty, reason);
			_outgoing.TryAdd(_closer, -1, _runSauce.Token);
			return disco;
		}
		#endregion Disconnection
		#region Reconnection
		/// minimum and maximum wait times (in milliseconds) before trying to reconnect
		const int _reconMin = 1 * 1000,
				_reconMax = 5 * 60 * 1000;
		/// amount of time (in milliseconds) to wait before trying to reconnect
		int _reconDelay = _reconMin;
		/// a source for controlling the reconnection routine
		CancellationTokenSource _reconSauce;
		/// the token given in the first <see cref="Connect(CancellationToken)"/> call.
		CancellationToken _reconToken;
		/// the task that waits, and then attempts to reconnect
		Task _reconnecter;
		/// reset the wait timeout, then wait, then reconnect
		async Task _reconnecting() {
			_reconDelay = Math.Min(_reconDelay * 2, _reconMax);
			await Task.Delay(_reconDelay, _reconSauce.Token);
			await this.Connect(_reconToken);
		}
		#endregion Reconnection
		#region Messages - Receiving
		/// 1mb buffer for receiving; way more than enough
		const int BUFFER = 1024 * 1024;
		/// task to handle incoming messages and server-side disconnections
		Task _receiver;
		/// handles incoming messages and server initiated disconnections.
		async Task _receiving() {
			var ct = _runSauce.Token;
			string closeMessage = BYEBYE;
			WebSocketCloseStatus closeReason = WebSocketCloseStatus.NormalClosure;
			try {
				while (!ct.IsCancellationRequested && this.Client?.State == WebSocketState.Open) {
					byte[] buffer = new byte[BUFFER];
					List<byte> message = new List<byte>();
					WebSocketReceiveResult received;
					do {
						received = await this.Client.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
						_noop.Interval = _noopTimeout.TotalMilliseconds;
						message.AddRange(buffer.Take(received.Count));
					} while (!received.EndOfMessage);

					switch (received.MessageType) {
						case WebSocketMessageType.Text:
							this.LastReceived = DateTime.Now;
							var msg = new TrakitSocketMessage(message);
							switch (msg.name) {
								case "connectionResponse":
									this.Self = this.Serializer.Deserialize<RespSelfGet>(msg.body);
									if (
										(
											// machine was not authorized
											_machine != default
											&& this.Self.errorCode != ErrorCode.success
										) || (
											// user ok, or session expire, or not logged in
											this.Self.errorCode != ErrorCode.success
											&& this.Self.errorCode != ErrorCode.passwordExpired
											&& this.Self.errorCode != ErrorCode.sessionExpired
											&& this.Self.errorCode != ErrorCode.userNotLoggedIn
										)
									) {
										throw new TrakitSocketException(
											this.Self.message,
											WebSocketCloseStatus.PolicyViolation
										);
									}
									_onStatus(TrakitSocketStatus.Opened);
									break;
								case "sessionMachineMerged":
									this.Self.machine = this.Serializer.Deserialize<SelfMachine>(msg.body);
									break;
								case "sessionGeneralMerged":
									this.Self.user.General = this.Serializer.Deserialize<SelfUserGeneral>(msg.body);
									break;
								case "sessionAdvancedMerged":
									this.Self.user.Advanced = this.Serializer.Deserialize<SelfUserAdvanced>(msg.body);
									break;
							}
							this.MessageReceived?.Invoke(this, msg);
							break;
						case WebSocketMessageType.Close:
							_onStatus(TrakitSocketStatus.Closing);
							await this.Client.CloseOutputAsync(
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
				_onStatus(TrakitSocketStatus.Closing);
			} catch (Exception ex) {
				closeReason = ex is TrakitSocketException tse
						? tse.reason
						: WebSocketCloseStatus.ProtocolError;
				closeMessage = ex.Message;
				await _close(
					closeReason,
					closeMessage,
					ct
				);
			}
			_shutdown(closeMessage, closeReason);
		}
		#endregion Messages - Receiving
		#region Messages - Sending
		/// task to handle outgoing messages and client-side disconnections
		Task _sender;
		/// list of outgoing messages
		BlockingCollection<TrakitSocketMessage> _outgoing;
		/// a specific message to close the underlying connection immediately instead of waiting for the outgoing queue to clear
		TrakitSocketMessage _closer;
		/// handles sending messages to the server, and initiating client requested disconnections
		async Task _sending() {
			var ct = _runSauce.Token;
			string closeMessage = BYEBYE;
			WebSocketCloseStatus closeReason = WebSocketCloseStatus.NormalClosure;
			try {
				while (_outgoing.TryTake(out TrakitSocketMessage message, -1, ct)) {
					if (_closer == default) {
						for (int offset = 0; offset < message.content.Length; offset += BUFFER) {
							int length = Math.Min(message.content.Length - offset, BUFFER);
							await (this.Client?.SendAsync(
								new ArraySegment<byte>(message.content, offset, length),
								WebSocketMessageType.Text,
								offset + length == message.content.Length,
								ct
							) ?? Task.FromCanceled(ct));
							_noop.Interval = _noopTimeout.TotalMilliseconds;
						}
						this.LastSent = DateTime.Now;
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
				_onStatus(TrakitSocketStatus.Closing);
			} catch (WebSocketException ex) {
				// socket disconnect
				closeMessage = ex.Message;
				if (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely) {
					closeReason = WebSocketCloseStatus.EndpointUnavailable;
					_onStatus(TrakitSocketStatus.Closing);
				} else {
					closeReason = WebSocketCloseStatus.ProtocolError;
					await _close(
						closeReason,
						closeMessage,
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
		/// sends the client requested close message with the reason and goodbye message
		Task _close(WebSocketCloseStatus reason, string message, CancellationToken ct) {
			_onStatus(TrakitSocketStatus.Closing);
			return (this.Client?.CloseOutputAsync(
				reason,
				TrakitSocketCommander.errorToReason(message) ?? reason.ToString(),
				ct
			) ?? Task.CompletedTask);
		}
		#endregion Messages - Sending
		#region Messages - Keep-alive
		/// the timer itself (resets every time <see cref="Timer.Interval"/> setter is invoked)
		Timer _noop = new Timer() {
			Enabled = false,
			AutoReset = true,
		};
		/// almost 30 seconds
		const int _noopDefault = (30 * 1000) - 1;
		/// default timeout for noop command
		TimeSpan _noopTimeout = TimeSpan.FromMilliseconds(_noopDefault);
		/// add outgoing message (don't use <see cref="Command(string, JObject)"/> because we don't need to await)
		void _noopElapsed(object sender, ElapsedEventArgs e) {
			if (!_outgoing.TryAdd(new TrakitSocketMessage("noop", $"{{\"reqId\":{++_reqId}}}"), -1, _runSauce.Token)) {
				// if we can't send outgoing messages, it means the connection is being closed
				_noop.Enabled = false;
			}
		}
		#endregion Messages - Keep-alive

		#region Commands
		/// used to correlate requests and responses. <seealso cref="Request.reqId"/>
		int _reqId;
		/// command name reply suffix and unknown command response name
		const string RESPONSE_SUFFIX = "Response",
					UNKNOWN_COMMAND = "unknownCommand" + RESPONSE_SUFFIX;
		/// converts the <see cref="Request"/> type into a WebSocket command name
		static string _getCommandName<TRequest>(TRequest request) where TRequest : Request {
			var matches = request.GetNameParts();
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
					case "Self":
						if (cmdName == "get") return "getSessionDetails";
						break;
					case "Session":
						if (cmdName == "delete") return "killSession";
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
						cmdName = "get" + Text.Plural(objName) + "List";
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
		public Task<JObject> Command(string name, JObject parameters) {
			if (this.Status != TrakitSocketStatus.Opened) throw new InvalidOperationException($"Connection is {this.Status}.");

			// let's track this request.
			parameters["reqId"] = ++_reqId;
			var outbound = new TrakitSocketMessage(name, this.Serializer.Serialize(parameters));

			var source = new TaskCompletionSource<JObject>(TaskCreationOptions.RunContinuationsAsynchronously);
			void handleMsg(TrakitSocketCommander sender, TrakitSocketMessage received) {
				if (
					received.name == outbound.name + RESPONSE_SUFFIX
					|| received.name == UNKNOWN_COMMAND
				) {
					try {
						var response = this.Serializer.Deserialize<JObject>(received.body);
						if (
							int.TryParse(response["reqId"]?.ToString(), out int reqId)
							&& reqId == (int)parameters["reqId"]
						) {
							this.MessageReceived -= handleMsg;
							this.StatusChanged -= handleDis;
							if (!source.TrySetResult(response)) {
								source.TrySetCanceled();
							}
						}
					} catch (Exception ex) {
						source.TrySetException(ex);
					}
				}
			}
			void handleDis(TrakitSocketCommander sender) {
				this.MessageReceived -= handleMsg;
				this.StatusChanged -= handleDis;
				source.TrySetCanceled();
			}
			this.MessageReceived += handleMsg;
			this.StatusChanged += handleDis;

			// add to outgoing queue
			var ct = _runSauce.Token;
			return _outgoing.TryAdd(outbound, -1, ct)
				? source.Task
				: Task.FromCanceled<JObject>(ct);
		}
		/// <summary>
		/// Sends a command to the Trak-iT <see cref="WebSocket"/> service, and returns a <see cref="Task"/> that completes when a reply is received.
		/// </summary>
		/// <typeparam name="TResponse"></typeparam>
		/// <param name="request"></param>
		/// <returns></returns>
		/// <exception cref="InvalidOperationException"></exception>
		public override async Task<TResponse> Command<TResponse>(Request request)
			=> this.Serializer.ConvertFrom<TResponse>(
				await this.Command(
					_getCommandName(request),
					this.Serializer.ConvertTo<JObject>(request)
				)
			);
		#endregion Commands
		#region Commands - Subscription
		/// <summary>
		/// Subscribes the <see cref="Client"/> to receive notifications for merge/delete changes to objects.
		/// </summary>
		/// <param name="company"></param>
		/// <param name="subscriptions"></param>
		/// <returns></returns>
		public Task<RespSubscription> Subscribe(ulong company, IEnumerable<SubscriptionType> subscriptions)
			=> this.Command<RespSubscription>(new ReqSubscriptionMerge() {
				company = new ParamId() { id = company },
				subscriptionTypes = subscriptions.ToList()
			});
		/// <summary>
		/// Unsubscribes the <see cref="Client"/> to receive notifications for merge/delete changes to objects.
		/// </summary>
		/// <param name="company"></param>
		/// <param name="subscriptions"></param>
		/// <returns></returns>
		public Task<RespSubscription> Unsubscribe(ulong company, IEnumerable<SubscriptionType> subscriptions)
			=> this.Command<RespSubscription>(new ReqSubscriptionRemove() {
				company = new ParamId() { id = company },
				subscriptionTypes = subscriptions.ToList()
			});
		/// <summary>
		/// Gets the list of current subscriptions for the <see cref="Client"/>.
		/// </summary>
		/// <returns></returns>
		public Task<RespSubscriptionList> GetSubscriptionList()
			=> this.Command<RespSubscriptionList>(new ReqSubscriptionList());
		#endregion Commands - Subscription
	}
}