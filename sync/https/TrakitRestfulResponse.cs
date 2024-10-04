using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using trakit.objects;

namespace trakit.https {
	/// <summary>
	/// 
	/// </summary>
	public class TrakitRestfulResponse<T> where T : ResponseType {
		/// <summary>
		/// 
		/// </summary>
		public readonly HttpResponseMessage response;
		/// <summary>
		/// 
		/// </summary>
		public readonly T result;

		public TrakitRestfulResponse(HttpResponseMessage response, T result) {
			this.response = response;
			this.result = result;
		}
	}
}