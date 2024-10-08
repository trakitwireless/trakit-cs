using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using trakit.commands;
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
		public HttpClient client { get; private set; } = new HttpClient();
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
		}
		public void Dispose() {
			var http = this.client;
			this.client = null;
			http?.CancelPendingRequests();
			http?.Dispose();
		}

		#region Authorization
		// saved API credentials when using a service account
		Machine _machine;
		// saved session identifier when using a user account
		Guid _sessionId;
		/// <summary>
		/// Saves the authentication mechanism as a <see cref="Machine"/>.
		/// </summary>
		/// <param name="machine"></param>
		public void setAuth(Machine machine) {
			this.setAuth();
			_machine = machine;
		}
		/// <summary>
		/// Saves the authentication mechanism as a <see cref="Session.id"/>.
		/// </summary>
		/// <param name="sessionId"></param>
		public void setAuth(Guid sessionId) {
			this.setAuth();
			_sessionId = sessionId;
		}
		/// <summary>
		/// Unsets the authentication mechanism so that requests are sent without any.
		/// </summary>
		public void setAuth() {
			_machine = default;
			_sessionId = default;
		}
		#endregion Authorization

		/// <summary>
		/// Sends an HTTPS <see cref="HttpRequestMessage"/> to the server and awaits a <see cref="HttpResponseMessage"/>.
		/// </summary>
		/// <typeparam name="T">The <see cref="ParameterType"/> for the given request.</typeparam>
		/// <param name="method"></param>
		/// <param name="path"></param>
		/// <param name="parms"></param>
		/// <returns></returns>
		public async Task<TrakitRestfulResponse<T>> send<T>(HttpMethod method, string path, ParameterType parms = default) where T : ResponseType {
			_reqId++;
			path = $"{this.baseAddress.ToString().TrimEnd('/')}/{path.TrimStart('/')}";
			var request = new HttpRequestMessage(method, path);
			if (parms != default) {
				parms.reqId = _reqId;
				request.Content = new StringContent(
					this.serializer.serialize(parms),
					Encoding.UTF8,
					"text/json"
				);
			} else {
				path += $"{(!path.Contains("?") ? "?" : "&")}reqId={_reqId}";
			}
			if (_machine?.secret?.Length != 0) {
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
		/// Sends a login command, and if successful, saves the <see cref="RespSelfDetails.ghostId"/> as the authentication mechanism for all further requests.
		/// </summary>
		/// <param name="username"></param>
		/// <param name="password"></param>
		/// <param name="userAgent"></param>
		/// <returns></returns>
		public async Task<TrakitRestfulResponse<RespSelfDetails>> login(string username, string password, string userAgent = default) {
			var body = new ReqLogin() {
				username = username,
				password = password,
			};
			if (userAgent != default) body.userAgent = userAgent;
			var response = await this.send<RespSelfDetails>(
				HttpMethod.Post,
				"self/login",
				body
			);
			if (Guid.TryParse(response.result.ghostId, out Guid sessionId)) {
				this.setAuth(sessionId);
			}
			return response;
		}
	}
}