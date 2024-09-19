using System;
using System.Net.Http;
using System.Net.WebSockets;

namespace trakit.wss {
	/// <summary>
	/// 
	/// </summary>
	public enum TrakitSocketEventType : byte {
		/// <summary>
		/// A new <see cref="TrakitSocketMessage"/> has been received.
		/// </summary>
		received,
		/// <summary>
		/// A <see cref="TrakitSocketMessage"/> was sent to the server.
		/// </summary>
		sent,
	}
}