using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using Newtonsoft.Json.Linq;
using Trakit.Commands;
using Trakit.Hmac;
using Trakit.Https;
using Trakit.Tools;

namespace Trakit.Imaging {
	/// <summary>
	/// A helper for accessing Trak-iT's RESTful service.
	/// </summary>
	public sealed class TrakitImageCommander : TrakitCommander, IDisposable {
		/// <summary>
		/// Production RESTful service URL.
		/// This service is covered by the SLA and should be used for serices and code running in your own production environment.
		/// </summary>
		public const string URI_PROD = "https://img.trakit.ca";
		/// <summary>
		/// Testing or beta RESTful service URL.
		/// This service is not covered by the SLA and should be used to test your own code before deployment.
		/// Throttling of connections and commands is tighter to help you diagnose issues before switching to production.
		/// </summary>
		/// <remarks>
		/// Both services access the same dataset, so be careful making changes as they will be reflected in production as well.
		/// </remarks>
		public const string URI_BETA = "https://boogeyman.trakit.ca";

		/// <summary>
		/// The underlying client making HTTPS requests.
		/// </summary>
		public HttpClient Client { get; private set; } = new HttpClient();

		public TrakitImageCommander() : this(new Uri(URI_PROD)) { }
		public TrakitImageCommander(Uri baseAddress) {
			this.BaseAddress = baseAddress;
		}
		public void Dispose() {
			var http = this.Client;
			this.Client = default;
			http?.CancelPendingRequests();
			http?.Dispose();
		}

		/// <summary>
		/// 
		/// </summary>
		/// <typeparam name="TResponse"></typeparam>
		/// <param name="request"></param>
		/// <returns></returns>
		/// <exception cref="NotImplementedException"></exception>
		public override Task<TResponse> Command<TResponse>(Request request) => throw new NotImplementedException();
	}
}