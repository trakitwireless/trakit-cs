using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;

namespace trakit.wss {
	/// <summary>
	/// All Trak-iT WebSocket messages follow the format of "<c>name body</c>".
	/// The <see cref="name"/> portion is formatted in lower-camel-case, and the <see cref="body"/> portion is JSON.
	/// The server uses <see cref="Encoding.UTF8"/> for all <see cref="WebSocketMessageType.Text"/> messages.
	/// </summary>
	public class TrakitSocketMessage {
		// a single space character which divides the name and body
		const byte SEPARATOR = (byte)' ';

		/// <summary>
		/// The name portion of this message.
		/// </summary>
		public readonly string name;
		/// <summary>
		/// The body of this message.
		/// </summary>
		public readonly string body;
		/// <summary>
		/// The raw message body.
		/// </summary>
		public readonly byte[] content;
		/// <summary>
		/// A timestamp from when the <see cref="ClientWebSocket"/> began receiving or sending this message.
		/// </summary>
		public readonly DateTime created = DateTime.UtcNow;
		/// <summary>
		/// When true, this message was received from the server.
		/// </summary>
		public readonly bool incoming;
		/// <summary>
		/// When specified, will close the socket after sending the message with this reason.
		/// </summary>
		public WebSocketCloseStatus reason;

		/// <summary>
		/// Creates an incoming message from the received bytes.
		/// </summary>
		/// <param name="received"></param>
		public TrakitSocketMessage(IEnumerable<byte> received) {
			this.incoming = true;
			this.content = received?.ToArray() ?? new byte[0];

			int space = Array.IndexOf(this.content, SEPARATOR);
			if (space == -1) {
				this.name = this.ToString();
				this.body = string.Empty;
			} else {
				this.name = this.decode(0, space);
				this.body = this.decode(space + 1, this.content.Length);
			}
			this.reason = WebSocketCloseStatus.Empty;
		}
		/// <summary>
		/// Creates an outgoing message from the given <c>name</c> and <c>body</c>.
		/// </summary>
		/// <param name="name"></param>
		/// <param name="body"></param>
		/// <param name="reason"></param>
		public TrakitSocketMessage(string name, string body, WebSocketCloseStatus reason = WebSocketCloseStatus.Empty) {
			this.incoming = false;
			this.name = name;
			this.body = body;
			this.content = Encoding.UTF8.GetBytes($"{name} {body}");
			this.reason = reason;
		}

		/// <summary>
		/// Returns a portion of the <see cref="content"/> based on the given start <c>index</c>, and <c>count</c> length.
		/// </summary>
		/// <param name="index"></param>
		/// <param name="count"></param>
		/// <returns></returns>
		public string decode(int index, int count) => Encoding.UTF8.GetString(this.content, index, count - index);
		/// <summary>
		/// Returns a string that represents the message in "<see cref="name"/> <see cref="body"/>" format.
		/// </summary>
		/// <returns></returns>
		public override string ToString() => this.decode(0, this.content.Length);
	}
}