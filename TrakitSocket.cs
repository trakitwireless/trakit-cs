using System;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Threading.Tasks;

namespace trakit.wss {
	/// <summary>
	/// A wrapper for Trak-iT's <see cref="WebSocket"/> service, including service specific idiosyncrasies.
	/// </summary>
	public class TrakitSocket {
		/// <summary>
		/// The underlying connection.
		/// </summary>
		public ClientWebSocket client;
		/// <summary>
		/// 
		/// </summary>
		public TrakitSocketStatus status;


		public async Task connect() {

			this.Status?.Invoke(
				this,
				TrakitSocketStatus.opening
			);
		}
		public async Task disconnect() {

		}
		public async Task command() {

		}

		#region Events
		/// <summary>
		/// Delegate for connection and disconnection events.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="type"></param>
		/// <param name="client"></param>
		public delegate void ConnectionHandler(
			TrakitSocket sender,
			TrakitSocketStatus type
		);
		/// <summary>
		/// Delegate for incoming and outgoing message events.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="type"></param>
		/// <param name="client"></param>
		/// <param name="message"></param>
		public delegate void MessageHandler(
			TrakitSocket sender,
			TrakitSocketEventType type,
			TrakitSocketMessage message
		);

		/// <summary>
		/// Raised for each phase of the connection and disconnection lifetime.
		/// </summary>
		public event ConnectionHandler Status;
		/// <summary>
		/// Raised when a connection is successfully established.
		/// </summary>
		public event ConnectionHandler Connected;
		/// <summary>
		/// Raised when the <see cref="client"/> is disconnected.
		/// </summary>
		public event ConnectionHandler Disconnected;
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