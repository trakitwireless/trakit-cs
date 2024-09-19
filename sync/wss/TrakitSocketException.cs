using System;
using System.Net.WebSockets;

namespace trakit.wss {
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
			Exception? inner = null
		) : base(message, inner) {
			this.reason = reason;
		}
	}
}