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
		HttpRequestMessage _request(HttpMethod method, string path, JObject body, out string route, out string content) {
			_reqId++; // always
			var request = new HttpRequestMessage(method, path);
			path = $"{this.baseAddress.ToString().TrimEnd('/')}/{path.TrimStart('/')}";
			if (body != default) {
				// request has a body
				body["reqId"] = _reqId;
				request.Content = new StringContent(
					content = this.serializer.serialize(body),
					Encoding.UTF8,
					"text/json"
				);
			} else {
				// no body, so add reqId to query-string
				path += $"{(!path.Contains("?") ? "?" : "&")}reqId={_reqId}";
				content = null;
			}
			if ((_machine?.secret?.Length ?? 0) != 0) {
				// use machine auth
				request.RequestUri = new Uri(path);
				signatures.addHmacHeader(request, _machine);
			} else if (_sessionId != default) {
				// user session in query-string
				request.RequestUri = new Uri(path + $"{(!path.Contains("?") ? "?" : "&")}ghostId={_sessionId}");
			}
			route = request.RequestUri.ToString();
			return request;
		}

		/// <summary>
		/// Sends a raw JSON request to the Trak-iT RESTful API and returns a task whose result is also JSON.
		/// </summary>
		/// <param name="method"><see cref="HttpMethod"/> for this request.</param>
		/// <param name="path">The relative path from the <see cref="baseAddress"/> for this request.</param>
		/// <param name="parms">Optional request parameters.</param>
		/// <returns>The JSON which appears in the body of the response.</returns>
		public async Task<TJson> request<TJson>(HttpMethod method, string path, JObject parms = default) where TJson : JObject {
			HttpRequestMessage request = null;
			HttpResponseMessage response = null;
			string route = null;
			string body = null;
			string content = null;
			try {
				request = _request(method, path, parms, out route, out body);
				response = await this.client.SendAsync(request);
				content = await response.Content.ReadAsStringAsync();
				return this.serializer.deserialize<TJson>(content);
			} catch (Exception ex) {
				throw new TrakitRestfulException(
					ex.Message,
					new TrakitRestfulException.Input($"{method} {route}", body),
					response == null
							? null
							: new TrakitRestfulException.Output(response.StatusCode, response.ReasonPhrase, content),
					ex
				);
			}
		}

		/// <summary>
		/// Sends the given request to Trak-iT's RESTful API and awaits a task whose result is both the HTTP response, and deserialized <see cref="Response"/>.
		/// </summary>
		/// <typeparam name="TResp">The <see cref="Response"/> for the given request.</typeparam>
		/// <param name="message">Request message details.</param>
		/// <returns>A Task whose result contains the HTTP and Trak-iT API responses.</returns>
		public async Task<TResp> request<TResp>(Request message) where TResp : Response {
			HttpRequestMessage request = null;
			HttpResponseMessage response = null;
			HttpMethod method = null;
			string route = null;
			string body = null;
			string content = null;
			try {
				request = _request(
					method = message.httpVerb,
					route = message.httpRoute,
					this.serializer.convert<JObject>(message),
					out route,
					out body
				);
				response = await this.client.SendAsync(request);
				content = await response.Content.ReadAsStringAsync();
				return this.serializer.deserialize<TResp>(content);
			} catch (Exception ex) {
				throw new TrakitRestfulException(
					ex.Message,
					new TrakitRestfulException.Input($"{method} {route}", body),
					response == null
							? null
							: new TrakitRestfulException.Output(response.StatusCode, response.ReasonPhrase, content),
					ex
				);
			}
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
		public async Task<RespSelfDetails> login(string username, string password, string userAgent = default) {
			var body = new ReqLogin() {
				username = username,
				password = password,
			};
			if (userAgent != default) body.userAgent = userAgent;
			this.session = await this.request<RespSelfDetails>(body);
			if (this.session.errorCode == ErrorCode.success && Guid.TryParse(this.session.ghostId, out Guid sessionId)) {
				this.setAuth(sessionId);
			}
			return this.session;
		}
		/// <summary>
		/// Sends a logout command, and if successful, removes the current session using <see cref="setAuth()"/>.
		/// </summary>
		/// <returns></returns>
		public async Task<RespLogout> logout() {
			var response = await this.request<RespLogout>(new ReqLogout());
			switch (response.errorCode) {
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