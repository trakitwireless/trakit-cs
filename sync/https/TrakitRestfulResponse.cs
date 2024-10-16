using System.Net.Http;
using trakit.commands;

namespace trakit.https {
	/// <summary>
	/// The response from one of Trak-iT's RESTful APIs.
	/// </summary>
	public class TrakitRestfulResponse<T> where T : Response {
		/// <summary>
		/// The actual <see cref="HttpResponseMessage"/> from the <see cref="TrakitRestfulRequest"/>.
		/// </summary>
		public readonly HttpResponseMessage http;
		/// <summary>
		/// Raw response body contents (for debugging).
		/// </summary>
		public readonly string content;
		/// <summary>
		/// The deserialized API <see cref="Response"/>.
		/// </summary>
		public readonly T body;

		public TrakitRestfulResponse(HttpResponseMessage http, string content, T body) {
			this.http = http;
			this.content = content;
			this.body = body;
		}
	}
}