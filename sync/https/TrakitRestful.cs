using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using trakit.objects;
using trakit.tools;
using trakit.wss;

namespace trakit.https {
	/// <summary>
	/// A helper for accessing Trak-iT's RESTful service.
	/// </summary>
	public class TrakitRestful : IDisposable {
		/// <summary>
		/// Production RESTful service URL.
		/// This service is covered by the SLA and should be used for serices and code running in your own production environment.
		/// </summary>
		public const string URI_PROD = "https://rest.trakit.ca";
		/// <summary>
		/// Testing or beta RESTful service URL.
		/// This service is not covered by the SLA and should be used to test your own code before deployment.
		/// Throttling of connections and commands is tighter to help you diagnose issues before switching to production.
		/// </summary>
		/// <remarks>
		/// Both services access the same dataset, so be careful making changes as they will be reflected in production as well.
		/// </remarks>
		public const string URI_BETA = "https://mindflayer.trakit.ca";

		/// <summary>
		/// Used to correlate requests and responses.
		/// </summary>
		int _reqId;
		/// <summary>
		/// <see cref="Uri"/> of the Trak-iT RESTful service.
		/// </summary>
		public Uri baseAddress { get; private set; }
		/// <summary>
		/// The underlying client making HTTPS requests.
		/// </summary>
		public HttpClient client;
		/// <summary>
		/// Details of the <see cref="User"/> or <see cref="Machine"/> whose <see cref="Session"/> is connected to the <see cref="client"/>.
		/// </summary>
		public RespSelfDetails session { get; private set; }
		/// <summary>
		/// 
		/// </summary>
		public Serializer serializer { get; private set; }

		public TrakitRestful() : this(new Uri(URI_PROD)) { }
		public TrakitRestful(Uri baseAddress) {
			this.baseAddress = baseAddress;
		}
		public void Dispose() {
			var client = this.client;
			this.client = null;
			client?.CancelPendingRequests();
			client?.Dispose();
		}



		public Task<T> get<T>(ulong id) where T : Subscribable {

		}
		public Task<T> list<T>(ulong id) where T : Subscribable {

		}
		public Task<T> merge<T>(ulong id, JObject json) where T : Subscribable {

		}
		public Task<T> delete<T>(ulong id) where T : Subscribable, IDeletable {

		}
		public Task<T> restore<T>(ulong id) where T : Subscribable, IDeletable {

		}
		public Task<T> suspend<T>(ulong id) where T : Subscribable, ISuspendable {

		}
		public Task<T> revive<T>(ulong id) where T : Subscribable, ISuspendable {

		}

		public Task<T> revive<T>(ulong id) where T : Subscribable, ISuspendable {

		}
		public Task<T> revive<T>(ulong id) where T : Subscribable, ISuspendable {

		}
	}
}