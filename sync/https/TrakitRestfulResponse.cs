using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using trakit.objects;

namespace trakit.https {
	/// <summary>
	/// 
	/// </summary>
	public class TrakitRestfulResponse {
		/// <summary>
		/// 
		/// </summary>
		public readonly HttpResponseMessage response;
		/// <summary>
		/// 
		/// </summary>
		public readonly ResponseType result;

		public TrakitRestfulResponse(HttpResponseMessage response, ResponseType result) {
			this.response = response;
			this.result = result;
		}
	}
}