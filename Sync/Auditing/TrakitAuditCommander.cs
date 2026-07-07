using System;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Trakit.Commands;
using Trakit.Https;
using Trakit.Https.Extensions;
using Trakit.Objects;
using Trakit.Tools;

namespace Trakit.Restful {
	/// <summary>
	/// Uses Trak-iT's RESTful service to access and manipulate all <see cref="Component">Trak-iT API Objects</see>.
	/// </summary>
	public sealed class TrakitAuditCommander : TrakitCommander<HttpClient>, IDisposable {
		/// <summary>
		/// Production RESTful service URL.
		/// </summary>
		/// <remarks>
		/// This service is covered by the SLA and should be used for serices and code running in your own production environment.
		/// Both services access the same data-set, so be careful making changes as they will be reflected in production as well.
		/// </remarks>
		public const string URI_PROD = "https://audit.trakit.ca";
		/// <summary>
		/// Testing or beta RESTful service URL.
		/// </summary>
		/// <remarks>
		/// This service is not covered by the SLA and should be used to test your own code before deployment.
		/// Throttling of connections and commands is tighter to help you diagnose issues before switching to production.
		/// Both services access the same data-set, so be careful making changes as they will be reflected in production as well.
		/// </remarks>
		public const string URI_BETA = "https://gloomhands.trakit.ca";

		public TrakitAuditCommander(
			Uri baseAddress,
			RepSelfGet account = default
		) : base(
			baseAddress ?? new Uri(URI_PROD),
			account
		) {
			this.Client = new HttpClient();
		}
		public TrakitAuditCommander(
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

		#region Commands
		// why does dotnet not have this as a static?
		static readonly HttpMethod HTTP_PATCH = new HttpMethod("PATCH");
		// used to split object names into paths
		static readonly Regex SPLITTER = new Regex("[A-Z][a-z]+", RegexOptions.Compiled);
		// outs the verb and path for the given request
		string _route(Payload request) {
			string route = default;
			string[] matches = request.GetNameParts();
			if (matches.Length > 1) {
				var objNames = SPLITTER.Split(matches[0]).Select(s => Text.Plural(s)).ToArray();
				route = string.Join("/", objNames);
				if (request is IPaySingle single) {
					route = objNames[0]
						+ single.GetKey()
						+ string.Join("/", objNames.Skip(1));
				}
			}
			if (string.IsNullOrEmpty(route)) {
				throw new NotImplementedException($"no verb and/or route supported for {request.GetType().Name}");
			}
			return route;
		}
		// internally handles sending requests and returns awaitable response from Trak-iT's RESTful API
		HttpRequestMessage _command(string path, JObject body) {
			var request = new HttpRequestMessage() {
				Method = HttpMethod.Get,
				RequestUri = this.CreateBaseUri(path).Uri,
			};
			// add headers
			foreach (var pair in this.Headers) {
				request.Headers.Add(pair.Key, pair.Value);
			}
			// add machine
			if (this.Account.machine != default) {
				this.Account.machine.AuthorizeRequest(request);
			} else if (Guid.TryParse(this.Account.ghostId, out Guid ghostId)) {
				request.Headers.Add("Authorization", $"Bearer {ghostId}");
			}
			return request;
		}
		/// <summary>
		/// Sends a raw JSON request to the Trak-iT RESTful API and returns a task whose result is also JSON.
		/// </summary>
		/// <param name="path">The relative path from the <see cref="BaseAddress"/> for this request.</param>
		/// <param name="method"><see cref="HttpMethod"/> for this request.</param>
		/// <param name="parms">Optional request parameters.</param>
		/// <returns>The JSON which appears in the body of the response.</returns>
		public async Task<JObject> Command(string path, JObject parms = default) {
			HttpRequestMessage request = default;
			HttpResponseMessage response = default;
			string content = default;
			try {
				request = _command(path, parms);
				response = await this.Client.SendAsync(request);
				content = await response.Content.ReadAsStringAsync();
				return this.Serializer.Deserialize<JObject>(content);
			} catch (Exception ex) {
				throw new TrakitHttpsException(
					ex.Message,
					new TrakitHttpsException.Input($"GET {request?.RequestUri}", ""),
					response == default
							? default
							: new TrakitHttpsException.Output(response.StatusCode, response.ReasonPhrase, content),
					ex
				);
			}
		}
		#endregion Commands

		/// <summary>
		/// Sends the given request to Trak-iT's RESTful API and awaits a task whose result is both the HTTP response, and deserialized <see cref="Reply"/>.
		/// </summary>
		/// <typeparam name="TReply">The <see cref="Reply"/> for the given request.</typeparam>
		/// <param name="payload">Request message details.</param>
		/// <returns>A Task whose result contains the HTTP and Trak-iT API responses.</returns>
		public override async Task<TReply> Command<TReply>(Payload payload) {
			return this.Serializer.ConvertFrom<TReply>(
				await this.Command(
					 _route(payload),
					this.Serializer.ConvertTo<JObject>(payload)
				)
			);
		}
	}
}