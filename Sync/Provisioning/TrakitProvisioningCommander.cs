using System;
using System.Net.Http;
using System.Threading.Tasks;
using Trakit.Commands;

namespace Trakit.Provisioning {
	/// <summary>
	/// Used to verify, authenticate, and configure hardware with the Trak-iT service.
	/// </summary>
	public sealed class TrakitProvisioningCommander : TrakitCommander<HttpClient>, IDisposable {
		/// <summary>
		/// 
		/// </summary>
		/// <remarks>
		/// This service is covered by the SLA and should be used for serices and code running in your own production environment.
		/// Both services access the same data-set, so be careful making changes as they will be reflected in production as well.
		/// </remarks>
		public const string URI_PROD = "https://modem.trakit.ca";
		/// <summary>
		/// 
		/// </summary>
		/// <remarks>
		/// This service is not covered by the SLA and should be used to test your own code before deployment.
		/// Throttling of connections and commands is tighter to help you diagnose issues before switching to production.
		/// Both services access the same data-set, so be careful making changes as they will be reflected in production as well.
		/// </remarks>
		public const string URI_BETA = "https://chimera.trakit.ca";

		public TrakitProvisioningCommander(
			Uri baseAddress,
			RepSelfGet account = default
		) : base(
			baseAddress ?? new Uri(URI_PROD),
			account
		) {
			this.Client = new HttpClient();
		}
		public TrakitProvisioningCommander(
			RepSelfGet account = default,
			Uri baseAddress = default
		) : this(
			baseAddress,
			account
		) { }
		public void Dispose() {
			var http = this.Client;
			this.Client = default;
			http?.CancelPendingRequests();
			http?.Dispose();
		}

		/// <summary>
		/// 
		/// </summary>
		/// <typeparam name="TReply"></typeparam>
		/// <param name="payload"></param>
		/// <returns></returns>
		/// <exception cref="NotImplementedException"></exception>
		public override Task<TReply> Command<TReply>(Payload payload) => throw new NotImplementedException();
	}
}