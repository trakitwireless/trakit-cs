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
	/// 
	/// </summary>
	public sealed class TrakitHostingCommander : TrakitCommander, IDisposable {
		/// <summary>
		/// 
		/// </summary>
		/// <remarks>
		/// This service is covered by the SLA and should be used for serices and code running in your own production environment.
		/// Both services access the same data-set, so be careful making changes as they will be reflected in production as well.
		/// </remarks>
		public const string URI_PROD = "https://files.trakit.ca";
		/// <summary>
		/// 
		/// </summary>
		/// <remarks>
		/// This service is not covered by the SLA and should be used to test your own code before deployment.
		/// Throttling of connections and commands is tighter to help you diagnose issues before switching to production.
		/// Both services access the same data-set, so be careful making changes as they will be reflected in production as well.
		/// </remarks>
		public const string URI_BETA = "https://wanshitong.trakit.ca";

		/// <summary>
		/// The underlying client making HTTPS requests.
		/// </summary>
		public HttpClient Client { get; private set; } = new HttpClient();

		public TrakitHostingCommander() : this(new Uri(URI_PROD)) { }
		public TrakitHostingCommander(Uri baseAddress) {
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