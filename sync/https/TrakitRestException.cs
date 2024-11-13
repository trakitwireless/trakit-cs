using System;
using System.Net;

namespace Trakit.Https {
	/// <summary>
	/// Represents a communication error with the underlying Trak-iT RESTful service.
	/// </summary>
	public class TrakitRestException : Exception {
		/// <summary>
		/// Details about the request that threw the exception.
		/// </summary>
		public class Input {
			/// <summary>
			/// The absolute path of the request.
			/// </summary>
			public string Route { get; set; }
			/// <summary>
			/// Contents of the request body.
			/// </summary>
			public string Body { get; set; }

			public Input(string route, string body) {
				this.Route = route;
				this.Body = body;
			}
		}
		/// <summary>
		/// Details about the response from the Trak-iT RESTful service.
		/// </summary>
		public class Output {
			/// <summary>
			/// Status code of the response.
			/// </summary>
			public HttpStatusCode StatusCode { get; set; }
			/// <summary>
			/// Response status message.
			/// </summary>
			public string StatusMessage { get; set; }
			/// <summary>
			/// Contents of the response body.
			/// </summary>
			public string Body { get; set; }

			public Output(HttpStatusCode code, string message, string body) {
				this.StatusCode = code;
				this.StatusMessage = message;
				this.Body = body;
			}
		}
		
		/// <summary>
		/// Details about the request that threw this exception.
		/// </summary>
		public Input Request;
		/// <summary>
		/// Details about the response that threw this exception.
		/// </summary>
		public Output Response;

		public TrakitRestException(
			string message,
			Input request,
			Output response
		) : base(message) {
			this.Request = request;
			this.Response = response;
		}
		public TrakitRestException(
			string message,
			Input request,
			Output response,
			Exception innerException
		) : base(message, innerException) {
			this.Request = request;
			this.Response = response;
		}
	}
}