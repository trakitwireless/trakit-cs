using System;
using System.Net;

namespace trakit.https {
	/// <summary>
	/// A request sent to one of Trak-iT's RESTful APIs.
	/// </summary>
	public class TrakitRestfulException : Exception {
		public class Input {
			/// <summary>
			/// 
			/// </summary>
			public string route { get; set; }
			/// <summary>
			/// 
			/// </summary>
			public string body { get; set; }

			public Input(string route, string body) {
				this.route = route;
				this.body = body;
			}
		}
		public class Output {
			/// <summary>
			/// 
			/// </summary>
			public HttpStatusCode status { get; set; }
			/// <summary>
			/// 
			/// </summary>
			public string message { get; set; }
			/// <summary>
			/// 
			/// </summary>
			public string body { get; set; }

			public Output(HttpStatusCode status, string message, string body) {
				this.status = status;
				this.message = message;
				this.body = body;
			}
		}
		/// <summary>
		/// 
		/// </summary>
		public Input request;
		/// <summary>
		/// 
		/// </summary>
		public Output response;

		public TrakitRestfulException(
			string message,
			Input request,
			Output response
		) : base(message) {
			this.request = request;
			this.response = response;
		}
		public TrakitRestfulException(
			string message,
			Input request,
			Output response,
			Exception innerException
		) : base(message, innerException) {
			this.request = request;
			this.response = response;
		}
	}
}