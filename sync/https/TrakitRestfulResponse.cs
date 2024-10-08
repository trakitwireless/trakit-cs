using System.Net.Http;
using trakit.objects;

namespace trakit.https {
	/// <summary>
	/// 
	/// </summary>
	public class TrakitRestfulResponse<TResponse> where TResponse : ResponseType {
		/// <summary>
		/// 
		/// </summary>
		public readonly HttpResponseMessage response;
		/// <summary>
		/// 
		/// </summary>
		public readonly TResponse result;

		public TrakitRestfulResponse(HttpResponseMessage response, TResponse result) {
			this.response = response;
			this.result = result;
		}
	}
}