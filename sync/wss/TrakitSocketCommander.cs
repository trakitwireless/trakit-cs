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
using System.Web;
using Newtonsoft.Json.Linq;
using Trakit.Commands;
using Trakit.Hmac;
using Trakit.Tools;
using Timer = System.Timers.Timer;

namespace Trakit.Wss {
	/// <summary>
	/// A wrapper for Trak-iT's <see cref="WebSocket"/> service, including service specific idiosyncrasies.
	/// </summary>
	public sealed class TrakitSocketCommander : TrakitCommander, IDisposable {
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
			return value == string.Empty ? default : value;
		}
		#endregion Statics

		/// <summary>
		/// The underlying connection.
		/// </summary>
		public ClientWebSocket Client { get; private set; }
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

		public TrakitSocketCommander() : this(new Uri(URI_PROD)) { }
		public TrakitSocketCommander(Uri baseAddress) {
			this.BaseAddress = baseAddress;
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
				if (this.Status != status) {
					this.Status = status;
					this.StatusChanged?.Invoke(this);
					switch (status) {
						case TrakitSocketStatus.Opened:
							_noop.Enabled = this.NoopKeepAlive.TotalMilliseconds > _noopDefault;
							this.Connected?.Invoke(this);
							break;
						case TrakitSocketStatus.Closed:
							_noop.Enabled = false;
							if (!silent) this.Disconnected?.Invoke(this, message, reason);
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
		#region Connection/Disconnection
		// token source for managing connecting, and incoming/outgoing messaging
		CancellationTokenSource _sauce;
		// an awaitable task which completes upon disconnection
		Task _connecting() {
			var source = new TaskCompletionSource<TrakitSocketStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
			void handler(TrakitSocketCommander sender) {
				this.StatusChanged -= handler;
				if (
					this.Status == TrakitSocketStatus.Opened
					&& source.TrySetResult(this.Status)
				) {
					_sender = Task.Run(_sending, _sauce.Token);
				} else {
					source.TrySetCanceled();
				}
			}
			this.StatusChanged += handler;
			_receiver = Task.Run(_receiving, _sauce.Token);
			return source.Task;
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
			try { await _sender; } catch { _sender = default; } finally { _sender?.Dispose(); }
			try { await _receiver; } catch { _receiver = default; } finally { _receiver?.Dispose(); }
			var wss = this.Client;
			this.Client = default;
			_outgoing.Dispose();
			_outgoing = default;
			_sauce.Dispose();
			_sauce = default;
			wss.Abort();
			wss.Dispose();
			_sender =
			_receiver = default;

			_onStatus(TrakitSocketStatus.Closed, closeMessage, closeReason);
		}
		// an awaitable task which completes upon disconnection
		Task _disconnecting() {
			var source = new TaskCompletionSource<TrakitSocketStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
			void handler(TrakitSocketCommander sender) {
				// do nothing and return (do not unbind the handler)
				// this is a normal part of the disconnection routine
				if (this.Status == TrakitSocketStatus.Closing) return;

				this.StatusChanged -= handler;
				if (
					this.Status != TrakitSocketStatus.Closed
					|| !source.TrySetResult(this.Status)
				) {
					source.TrySetCanceled();
				}
			}
			this.StatusChanged += handler;
			return source.Task;
		}

		/// <summary>
		/// Initiates a new <see cref="WebSocket"/> connection.
		/// </summary>
		/// <param name="ct"></param>
		/// <param name="query"></param>
		/// <param name="headers"></param>
		/// <returns></returns>
		/// <exception cref="InvalidOperationException"></exception>
		public async Task Connect(
			CancellationToken? ct = default,
			IDictionary<string, string> query = default,
			IDictionary<string, string> headers = default
		) {
			if (this.Status != TrakitSocketStatus.Closed) throw new InvalidOperationException($"Connection is {this.Status}.");

			_closer = default;
			_shutter = default;
			_sauce = new CancellationTokenSource();
			_outgoing = new BlockingCollection<TrakitSocketMessage>();
			this.Client = new ClientWebSocket();

			var source = ct.HasValue
					? CancellationTokenSource.CreateLinkedTokenSource(_sauce.Token, ct.Value)
					: _sauce;
			var endpoint = new UriBuilder(this.BaseAddress);
			if (query?.Count() > 0) {
				endpoint.Query += "&" + string.Join("&", query.Select(p => HttpUtility.UrlEncode(p.Key) + "=" + HttpUtility.UrlEncode(p.Value)));
			}
			if (headers?.Count() > 0) {
				foreach (var pair in headers) {
					this.Client.Options.SetRequestHeader(pair.Key, pair.Value);
				}
			}
			if (_machine != default) {
				if (_machine.secret?.Length > 0) {
					this.Client.Options.SetRequestHeader(
						"Authorization",
						"HMAC256 " + Convert.ToBase64String(Encoding.UTF8.GetBytes(
							_machine.key
							+ ":"
							+ Signatures.CreateHmacSignedInput(
								_machine.key,
								_machine.secret,
								DateTime.UtcNow,
								HttpMethod.Get,
								endpoint.Uri,
								0
							)
						))
					);
				} else {
					endpoint.Query += $"&shadowKey={HttpUtility.UrlEncode(_machine.key)}";
				}
			} else if (_sessionId != default) {
				endpoint.Query += $"&ghostId={_sessionId}";
			}
			if (endpoint.Query.Length > 1 && endpoint.Query[1] == '&') {
				endpoint.Query = endpoint.Query.Substring(2);
			}
			try {
				_onStatus(TrakitSocketStatus.Opening);
				await this.Client.ConnectAsync(endpoint.Uri, source.Token);
				await _connecting().ConfigureAwait(false);
			} catch {
				source.Cancel();
				source.Dispose();
				_onStatus(TrakitSocketStatus.Closed, silent: true);
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
		public Task Disconnect(
			WebSocketCloseStatus reason = WebSocketCloseStatus.NormalClosure,
			string message = BYEBYE
		) {
			if (this.Status != TrakitSocketStatus.Opened) throw new InvalidOperationException($"Connection is {this.Status}.");

			var disconn = _disconnecting();
			_closer = _closer ?? new TrakitSocketMessage(message, string.Empty, reason);
			_outgoing.TryAdd(_closer, -1, _sauce.Token);
			return disconn;
		}
		#endregion Connection/Disconnection
		#region Messages - Receiving
		// 1mb buffer for receiving; way more than enough
		const int BUFFER = 1024 * 1024;
		// task to handle incoming messages and server-side disconnections
		Task _receiver;
		// handles incoming messages and server initiated disconnections.
		async Task _receiving() {
			var ct = _sauce.Token;
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
									this.LastConnected = this.LastReceived;
									this.Self = this.Serializer.Deserialize<RespSelfDetails>(msg.body);
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
				_onStatus(TrakitSocketStatus.Closing);
				var reason = ex is TrakitSocketException tse
						? tse.reason
						: WebSocketCloseStatus.ProtocolError;
				closeMessage = ex.Message;
				closeReason = reason;
				await (this.Client?.CloseOutputAsync(
					reason,
					closeMessage,
					ct
				) ?? Task.CompletedTask);
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
			_onStatus(TrakitSocketStatus.Closing);
			return (this.Client?.CloseOutputAsync(
				reason,
				TrakitSocketCommander.errorToReason(message) ?? reason.ToString(),
				ct
			) ?? Task.CompletedTask);
		}
		#endregion Messages - Sending
		#region Messages - Keep-alive
		// the timer itself (reset every time .Interval is set)
		Timer _noop = new Timer() {
			Enabled = false,
			AutoReset = true,
		};
		// almost 30 seconds
		const int _noopDefault = (30 * 1000) - 1;
		// default timeout for noop command
		TimeSpan _noopTimeout = TimeSpan.FromMilliseconds(_noopDefault);
		// add outgoing message (don't use .Command because we don't need to await)
		void _noopElapsed(object sender, ElapsedEventArgs e) {
			if (!_outgoing.TryAdd(new TrakitSocketMessage("noop", $"{{\"reqId\":{++_reqId}}}"), -1, _sauce.Token)) {
				_noop.Enabled = false;
			}
		}
		#endregion Messages - Keep-alive

		#region Commands
		// command name reply suffix
		const string RESPONSE_SUFFIX = "Response";
		// converts the Request type into a WebSocket command name
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
				if (received.name == outbound.name + RESPONSE_SUFFIX) {
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
			var ct = _sauce.Token;
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
				subscriptionTypes = subscriptions.ToArray()
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
				subscriptionTypes = subscriptions.ToArray()
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