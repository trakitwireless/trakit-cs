using System.Net.Http;
using trakit.objects;

namespace trakit.https {
	/// <summary>
	/// The response from one of Trak-iT's RESTful APIs.
	/// </summary>
	public class TrakitRestfulResponse<T> where T : ResponseType {
		/// <summary>
		/// The actual <see cref="HttpResponseMessage"/> from the <see cref="TrakitRestfulRequest"/>.
		/// </summary>
		public readonly HttpResponseMessage http;
		/// <summary>
		/// The deserialized API <see cref="ResponseType"/>.
		/// </summary>
		public readonly T body;

		public TrakitRestfulResponse(HttpResponseMessage http, T body) {
			this.http = http;
			this.body = body;
		}
	}
}