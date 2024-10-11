using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
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

		#region Sending - Requests
		// internally handles sending requests and returns awaitable response from Trak-iT's RESTful API
		Task<HttpResponseMessage> _request(HttpMethod method, string path, JObject body = default) {
			_reqId++; // always
			var request = new HttpRequestMessage(method, path);
			path = $"{this.baseAddress.ToString().TrimEnd('/')}/{path.TrimStart('/')}";
			if (body != default) {
				// request has a body
				body["reqId"] = _reqId;
				request.Content = new StringContent(
					this.serializer.serialize(body),
					Encoding.UTF8,
					"text/json"
				);
			} else {
				// no body, so add reqId to query-string
				path += $"{(!path.Contains("?") ? "?" : "&")}reqId={_reqId}";
			}
			if (_machine?.secret?.Length != 0) {
				// use machine auth
				request.RequestUri = new Uri(path);
				signatures.addHmacHeader(request, _machine);
			} else if (_sessionId != default) {
				// user session in query-string
				request.RequestUri = new Uri(path + $"{(!path.Contains("?") ? "?" : "&")}ghostId={_sessionId}");
			}
			return this.client.SendAsync(request);
		}

		/// <summary>
		/// Sends the given request to Trak-iT's RESTful API and awaits a task whose result is both the HTTP response, and deserialized <see cref="Response"/>.
		/// </summary>
		/// <typeparam name="TReq">The <see cref="Request"/>for the given request.</typeparam>
		/// <typeparam name="TResp">The <see cref="Response"/> for the given request.</typeparam>
		/// <param name="message">Request message details.</param>
		/// <returns>A Task whose result contains the HTTP and Trak-iT API responses.</returns>
		public async Task<TrakitRestfulResponse<TResp>> request<TReq, TResp>(
			TrakitRestfulRequest<TReq> message
		) where TReq : Request where TResp : Response {
			var response = await _request(
				message.method,
				message.path,
				this.serializer.convert<Request, JObject>(message.parameters)
			);
			return new TrakitRestfulResponse<TResp>(
				response,
				this.serializer.deserialize<TResp>(await response.Content.ReadAsStringAsync())
			);
		}
		/// <summary>
		/// Sends the given request to Trak-iT's RESTful API and awaits a task whose result is both the HTTP response, and deserialized <see cref="Response"/>.
		/// </summary>
		/// <typeparam name="TReq">The <see cref="Request"/>for the given request.</typeparam>
		/// <typeparam name="TResp">The <see cref="Response"/> for the given request.</typeparam>
		/// <param name="method"><see cref="HttpMethod"/> for this request.</param>
		/// <param name="path">The relative path from the <see cref="baseAddress"/> for this request.</param>
		/// <param name="parms">Optional request parameters.</param>
		/// <returns>A Task whose result contains the HTTP and Trak-iT API responses.</returns>
		public Task<TrakitRestfulResponse<TResp>> request<TReq, TResp>(
			HttpMethod method,
			string path,
			TReq parms = default
		) where TReq : Request where TResp : Response
			=> this.request<TReq, TResp>(new TrakitRestfulRequest<TReq>(
				method,
				path,
				parms
			));
		/// <summary>
		/// Sends a raw JSON request to the Trak-iT RESTful API and returns a task whose result is also JSON.
		/// </summary>
		/// <param name="method"><see cref="HttpMethod"/> for this request.</param>
		/// <param name="path">The relative path from the <see cref="baseAddress"/> for this request.</param>
		/// <param name="parms">Optional request parameters.</param>
		/// <returns>The JSON which appears in the body of the response.</returns>
		public async Task<TJson> request<TJson>(HttpMethod method, string path, JObject parms = default) where TJson : JObject {
			var response = await _request(method, path, parms);
			return this.serializer.deserialize<TJson>(await response.Content.ReadAsStringAsync());
		}
		#endregion Sending - Requests

		#region Self
		/// <summary>
		/// Sends a login command, and if successful, saves the <see cref="RespSelfDetails.ghostId"/> as the authentication mechanism for all further requests.
		/// </summary>
		/// <param name="username">Your email address.</param>
		/// <param name="password">Your password.</param>
		/// <param name="userAgent">Optional string to identify this software.</param>
		/// <returns>The <see cref="RespSelfDetails"/>, which contains a <see cref="SelfUser"/> when successful.</returns>
		public async Task<TrakitRestfulResponse<RespSelfDetails>> login(string username, string password, string userAgent = default) {
			var body = new ReqLogin() {
				username = username,
				password = password,
			};
			if (userAgent != default) body.userAgent = userAgent;
			var response = await this.request<ReqLogin, RespSelfDetails>(
				HttpMethod.Post,
				"self/login",
				body
			);
			this.session = response.body;
			if (response.body.errorCode == ErrorCode.success && Guid.TryParse(response.body.ghostId, out Guid sessionId)) {
				this.setAuth(sessionId);
			}
			return response;
		}
		/// <summary>
		/// Sends a logout command, and if successful, removes the current session using <see cref="setAuth()"/>.
		/// </summary>
		/// <returns></returns>
		public async Task<TrakitRestfulResponse<RespBlank>> logout() {
			var response = await this.request<ReqBlank, RespBlank>(HttpMethod.Post, "self/logout");
			switch (response.body.errorCode) {
				case ErrorCode.success:
				case ErrorCode.sessionExpired:
				case ErrorCode.sessionNotFound:
					this.setAuth();
					break;
			}
			return response;
		}
		#endregion Self
	}
}