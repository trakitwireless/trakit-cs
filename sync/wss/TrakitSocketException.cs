using System;
using System.Net.WebSockets;

namespace Trakit.Wss {
	/// <summary>
	/// 
	/// </summary>
	public class TrakitSocketException : Exception {
		/// <summary>
		/// After an exception of this kind is caught, the connection is terminated.
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