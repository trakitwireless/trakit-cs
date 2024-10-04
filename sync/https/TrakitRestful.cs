using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using trakit.hmac;
using trakit.objects;
using trakit.tools;

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
		public HttpClient client { get; private set; }
		/// <summary>
		/// Details of the <see cref="User"/> or <see cref="Machine"/> whose <see cref="Session"/> is connected to the <see cref="client"/>.
		/// </summary>
		public RespSelfDetails session { get; private set; }
		/// <summary>
		/// 
		/// </summary>
		public Serializer serializer { get; private set; } = new Serializer();

		public TrakitRestful() : this(new Uri(URI_PROD)) { }
		public TrakitRestful(Uri baseAddress) {
			this.baseAddress = baseAddress;
			this.client = new HttpClient();
		}
		public void Dispose() {
			var client = this.client;
			this.client = null;
			client?.CancelPendingRequests();
			client?.Dispose();
		}


		Machine _machine;
		Guid _sessionId;
		public void setMachine(Machine machine) {
			_machine = machine;
			_sessionId = default;
		}
		public void setSessionId(Guid sessionId) {
			_machine = default;
			_sessionId = sessionId;
		}

		/// <summary>
		/// 
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="method"></param>
		/// <param name="path"></param>
		/// <param name="body"></param>
		/// <returns></returns>
		async Task<TrakitRestfulResponse<T>> _send<T>(HttpMethod method, string path, JObject body = default) where T : ResponseType {
			_reqId++;
			path = this.baseAddress.ToString() + path;
			var request = new HttpRequestMessage(HttpMethod.Post, path);
			if (body != default) {
				if (!((IDictionary<string, JToken>)body).ContainsKey("reqId")) {
					body["reqId"] = _reqId;
				}
				request.Content = new StringContent(
					this.serializer.serialize(body),
					Encoding.UTF8,
					"text/json"
				);
			}
			if (_machine != default) {
				request.RequestUri = new Uri(path);
				signatures.createHmacHeader(request, _machine);
			} else if (_sessionId != default) {
				request.RequestUri = new Uri(path + $"{(!path.Contains("?") ? "?" : "&")}ghostId={_sessionId}");
			}
			var response = await this.client.SendAsync(request);
			return new TrakitRestfulResponse<T>(
				response,
				this.serializer.deserialize<T>(await response.Content.ReadAsStringAsync())
			);
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="username"></param>
		/// <param name="password"></param>
		/// <param name="userAgent"></param>
		/// <returns></returns>
		public async Task<TrakitRestfulResponse<RespSelfDetails>> login(string username, string password, string userAgent = default) {
			var body = new JObject(
				new JProperty("username", username),
				new JProperty("password", password)
			);
			if (userAgent != default) body["userAgent"] = userAgent;
			var response = await _send<RespSelfDetails>(
				HttpMethod.Post,
				"self/login",
				body
			);
			if (Guid.TryParse(response.result.ghostId, out Guid sessionId)) {
				this.setSessionId(sessionId);
			}
			return response;
		}




		//public Task<TrakitRestfulResponse> get<T>(string key) where T : IRequestable {
			
		//}
		//public Task<TrakitRestfulResponse> list<T>(string key) where T : IRequestable {

		//}
		//public Task<TrakitRestfulResponse> merge<T>(string key, JObject body) where T : IRequestable {

		//}
		//public Task<TrakitRestfulResponse> delete<T>(string key) where T : IRequestable, IDeletable {

		//}
		//public Task<TrakitRestfulResponse> restore<T>(string key) where T : IRequestable, IDeletable {

		//}
		//public Task<TrakitRestfulResponse> suspend<T>(string key) where T : IRequestable, ISuspendable {

		//}
		//public Task<TrakitRestfulResponse> revive<T>(string key) where T : IRequestable, ISuspendable {

		//}

	}
}