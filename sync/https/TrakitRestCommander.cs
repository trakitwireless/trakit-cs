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
using Trakit.Tools;

namespace Trakit.Https {
	/// <summary>
	/// A helper for accessing Trak-iT's RESTful service.
	/// </summary>
	public sealed class TrakitRestCommander : TrakitCommander, IDisposable {
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
		/// The underlying client making HTTPS requests.
		/// </summary>
		public HttpClient Client { get; private set; } = new HttpClient();

		public TrakitRestCommander() : this(new Uri(URI_PROD)) { }
		public TrakitRestCommander(Uri baseAddress) {
			this.BaseAddress = baseAddress;
		}
		public void Dispose() {
			var http = this.Client;
			this.Client = default;
			http?.CancelPendingRequests();
			http?.Dispose();
		}

		#region Commands
		// why does dotnet not have this as a static?
		static readonly HttpMethod HTTP_PATCH = new HttpMethod("PATCH");
		// used to split object names into paths
		static readonly Regex SPLITTER = new Regex("[A-Z][a-z]+", RegexOptions.Compiled);
		// outs the verb and path for the given request
		void _commandHttp<TRequest>(TRequest request, out HttpMethod method, out string route) where TRequest : Request {
			method = default;
			route = default;
			string query = "";
			string[] matches = request.GetNameParts();
			if (matches.Length > 1) {
				switch (matches[0]) {
					case "Self":
						method = HttpMethod.Post;
						route = (matches[0] + "/" + matches[1]).ToLowerInvariant();
						return;
					case "Subscription":
						throw new NotImplementedException($"{matches[0]} only supported by TrakitSocketCommander");
				}

				var objNames = SPLITTER.Split(matches[0]).Select(s => Text.Plural(s)).ToArray();
				route = string.Join("/", objNames);
				switch (matches[1]) {
					case "Get":
						method = HttpMethod.Get;
						if (request is IReqSingle single) {
							route = objNames[0]
								+ single.getKey()
								+ string.Join("/", objNames.Skip(1));
						}
						break;
					case "List":
						method = HttpMethod.Get;
						if (request is IReqListByCompany byCompany) {
							route = $"companies/{byCompany.company.id}/{route}";
						}
						if (request is IReqListByLabels byLabels) {
							query += $"&labels={HttpUtility.UrlEncode(string.Join(",", byLabels.labels))}";
						}
						if (request is IReqListByReferences byRefs) {
							query += "&" + string.Join("&", byRefs.references.Select(p => $"{HttpUtility.UrlEncode(p.Key)}={HttpUtility.UrlEncode(p.Value)}"));
						}
						break;
					case "Restore":
						method = HTTP_PATCH;
						route += "/restore";
						break;
					case "BatchMerge":
						method = HTTP_PATCH;
						break;
					case "Suspend":
					case "BatchSuspend":
						method = HTTP_PATCH;
						route += "/suspend";
						break;
					case "Reactivate":
					case "BatchReactivate":
						method = HTTP_PATCH;
						route += "/revive";
						break;
					case "Delete":
					case "BatchDelete":
						method = HttpMethod.Delete;
						break;
					case "Merge":
						method = HttpMethod.Post;
						break;
				}
			}
			if (method == default || string.IsNullOrEmpty(route)) {
				throw new NotImplementedException($"no verb and/or route supported for {request.GetType().Name}");
			}
			if (query.Length > 0) route += "?" + query.Substring(1);
		}
		// internally handles sending requests and returns awaitable response from Trak-iT's RESTful API
		HttpRequestMessage _command(HttpMethod method, string path, JObject body, out string route, out string content) {
			_reqId++; // always
			var request = new HttpRequestMessage(method, path);
			path = $"{this.BaseAddress.ToString().TrimEnd('/')}/{path.TrimStart('/')}";
			if (body != default) {
				// request has a body
				body["reqId"] = _reqId;
				request.Content = new StringContent(
					content = this.Serializer.Serialize(body),
					Encoding.UTF8,
					"text/json"
				);
			} else {
				// no body, so add reqId to query-string
				path += $"{(!path.Contains("?") ? "?" : "&")}reqId={_reqId}";
				content = default;
			}
			if ((_machine?.secret?.Length ?? 0) != 0) {
				// use machine auth
				request.RequestUri = new Uri(path);
				Signatures.AddHmacHeader(request, _machine);
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
		/// <param name="path">The relative path from the <see cref="BaseAddress"/> for this request.</param>
		/// <param name="method"><see cref="HttpMethod"/> for this request.</param>
		/// <param name="parms">Optional request parameters.</param>
		/// <returns>The JSON which appears in the body of the response.</returns>
		public async Task<JObject> Command(string path, HttpMethod method = default, JObject parms = default) {
			HttpRequestMessage request = default;
			HttpResponseMessage response = default;
			method = method ?? HttpMethod.Get;
			string route = default;
			string body = default;
			string content = default;
			try {
				request = _command(method, path, parms, out route, out body);
				response = await this.Client.SendAsync(request);
				content = await response.Content.ReadAsStringAsync();
				return this.Serializer.Deserialize<JObject>(content);
			} catch (Exception ex) {
				throw new TrakitRestException(
					ex.Message,
					new TrakitRestException.Input($"{method} {route}", body),
					response == default
							? default
							: new TrakitRestException.Output(response.StatusCode, response.ReasonPhrase, content),
					ex
				);
			}
		}
		#endregion Commands

		/// <summary>
		/// Sends the given request to Trak-iT's RESTful API and awaits a task whose result is both the HTTP response, and deserialized <see cref="Response"/>.
		/// </summary>
		/// <typeparam name="TResp">The <see cref="Response"/> for the given request.</typeparam>
		/// <param name="request">Request message details.</param>
		/// <returns>A Task whose result contains the HTTP and Trak-iT API responses.</returns>
		public override async Task<TResp> Command<TResp>(Request request) {
			_commandHttp(request, out HttpMethod method, out string route);
			return this.Serializer.ConvertFrom<TResp>(
				await this.Command(
					route,
					method,
					this.Serializer.ConvertTo<JObject>(request)
				)
			);
		}
	}
}