using System;
using System.Net.WebSockets;

namespace Trakit.Wss {
	/// <summary>
	/// Represents a communication error with the underlying Trak-iT <see cref="WebSocket"/> service.
	/// </summary>
	public class TrakitSocketException : Exception {
		/// <summary>
		/// After an exception of this kind is thrown, the connection is terminated.
		/// </summary>
		public WebSocketCloseStatus reason;

		public TrakitSocketException(
			string message,
			WebSocketCloseStatus reason,
			Exception inner = null
		) : base(message, inner) {
			this.reason = reason;
		}
	}
}