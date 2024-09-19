using System;
using System.Net.Http;
using System.Net.WebSockets;

namespace trakit.wss {
	/// <summary>
	/// 
	/// </summary>
	public enum TrakitSocketStatus : byte {
		/// <summary>
		/// A connection is being established and is awaiting the innitial <c>connectionResponse</c> message.
		/// </summary>
		opening = 1,
		/// <summary>
		/// A connection is established and the <c>connectionResponse</c> message has been received.
		/// </summary>
		open,
		/// <summary>
		/// Either the client or the server has initiated a disconnection.
		/// </summary>
		closing,
		/// <summary>
		/// The underlying <see cref="WebSocket"/> connection has been terminated.
		/// </summary>
		closed,
	}
}