using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace trakit.wss {
	/// <summary>
	/// A wrapper for Trak-iT's <see cref="WebSocket"/> service, including service specific idiosyncrasies.
	/// </summary>
	public class TrakitSocket {
		/// <summary>
		/// The underlying connection.
		/// </summary>
		public ClientWebSocket? client;
		/// <summary>
		/// 
		/// </summary>
		public TrakitSocketStatus status = TrakitSocketStatus.closed;
		/// <summary>
		/// 
		/// </summary>
		public Uri address;


		CancellationTokenSource _status,
							_comms;
		Task _receiver;
		async Task _receiving() {
			CancellationToken ct = _comms.Token;
			string closeMessage = "Goodbye!";
			WebSocketCloseStatus closeReason = WebSocketCloseStatus.NormalClosure;
			while (!ct.IsCancellationRequested && this.client?.State == WebSocketState.Open) {
				try {
					byte[] buffer = new byte[1024 * 1024];
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
				this.status = TrakitSocketStatus.open;
			}
		}

		public async Task connect(IEnumerable<KeyValuePair<string, string>>? headers = null) {
			this.client = new ClientWebSocket();
			_status = new CancellationTokenSource();
			_comms = new CancellationTokenSource();
			if (headers?.Count() > 0) {
				foreach (var pair in headers) {
					this.client.Options.SetRequestHeader(pair.Key, pair.Value);
				}
			}
			this.MessageReceived += _connectionResponse;
			await this.client.ConnectAsync(this.address, _status.Token);
			this.status = TrakitSocketStatus.opening;
			_receiver = Task.Run(_receiving);
		}
		public async Task disconnect() {
			var client = this.client;
			this.client = null;
			client?.Abort();
			client?.Dispose();
			try { _receiver?.Wait(); } catch { _receiver = null; } finally { _receiver?.Dispose(); }
			_status.Dispose();
			_comms.Dispose();

		}
		public async Task command() {

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
			WebSocketCloseStatus? reason = null
		) {
			this.status = status;
			switch (status) {
				case TrakitSocketStatus.open:
					this.Connected?.Invoke(this);
					break;
				case TrakitSocketStatus.closed:
					this.Disconnected?.Invoke(this, message, reason.Value);
					break;
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