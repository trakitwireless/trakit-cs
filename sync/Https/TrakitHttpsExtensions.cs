using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using Trakit.Objects;

namespace Trakit.Https.Extensions {
	/// <summary>
	/// Extension methods to assist with communication.
	/// </summary>
	public static class TrakitHttpsExtensions {
		/// <summary>
		/// Common name for session ID used by all systems.
		/// </summary>
		public const string SESSION_ID = "ghostId";

		/// <summary>
		/// Returns the URI with the session/machine keys removed from the <see cref="Uri.Query"/>.
		/// </summary>
		/// <param name="uri"><see cref="Uri"/> being sanitized for the HMAC signature.</param>
		/// <returns>A new <see cref="Uri"/> without the <see cref="SESSION_ID"/> or <see cref="MACHINE_KEY"/> values in the <see cref="Uri.Query"/>.</returns>
		public static string GetSanitizedUri(this Uri uri) {
			UriBuilder built = new UriBuilder(uri);
			if (!string.IsNullOrEmpty(uri.Query)) {
				built.Query = string.Join(
					"&",
					uri.Query.Substring(1)
							.Split('&')
							.Select(s => s.StartsWith(SESSION_ID + "=") ? "" : s)
							.Where(s => s != "")
				);
			}
			return built.Uri.ToString();
		}
		/// <summary>
		/// Modifies the given request with <c>Date</c> and <c>Authorization</c> headers using an HMAC256 signature.
		/// </summary>
		/// <remarks>
		/// This call should be after all other content and payload is added to ensure the signature is correct.
		/// </remarks>
		/// <param name="machine"><see cref="Machine"/> signing the <paramref name="request"/>.</param>
		/// <param name="request">The request being sent to the Trak-iT API service.</param>
		/// <param name="date">Timestamp for the request.  If no value is given, then <see cref="DateTimeOffset.UtcNow"/> is used. Trak-iT APIs only allow requests up to <c>15 seconds</c> old.</param>
		public static void AuthorizeRequest(this Machine machine, HttpRequestMessage request, DateTimeOffset? date = default) {
			request.Headers.Date = date
							?? request.Headers.Date
							?? DateTimeOffset.UtcNow;
			request.Headers.Authorization = machine.secret?.Length > 0
				? new AuthenticationHeaderValue(
					"HMAC256",
					machine.CreateHmacCreateSignature(
						request.Headers.Date.Value,
						request.Method,
						request.RequestUri,
						request.Content?.Headers?.ContentLength ?? 0
					)
				)
				: new AuthenticationHeaderValue(
					"Machine",
					Convert.ToBase64String(Encoding.UTF8.GetBytes(machine.key))
				);
		}
		/// <summary>
		/// Creates an HMAC256 signed input for use in <see cref="HttpRequestHeaders"/>s and <see cref="ClientWebSocketOptions"/>.
		/// </summary>
		/// <remarks>
		/// The output of this function is used as the value of the <c>Authorization</c> header.</remarks>
		/// <param name="machine"><see cref="Machine"/> creating the signature.</param>
		/// <param name="date">Timestamp for when the request is sent.</param>
		/// <param name="method">HTTP verb of the request.</param>
		/// <param name="absoluteUri">Full <see cref="Uri"/> of the request.</param>
		/// <param name="contentLength">Request content body length.</param>
		/// <returns>A Base64 encoded signature from the given request details.</returns>
		public static string CreateHmacCreateSignature(
			this Machine machine,
			DateTimeOffset date,
			HttpMethod method,
			Uri absoluteUri,
			long contentLength
		) {
			using (var hmac = new HMACSHA256(Convert.FromBase64String(machine.secret))) {
				return Convert.ToBase64String(Encoding.UTF8.GetBytes(
					machine.key
					+ ":"
					+ Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", new[] {
						machine.key,
						date.UtcDateTime.ToString("yyyyMMddHHmmss"),
						method.ToString(),
						absoluteUri.GetSanitizedUri(),
						contentLength.ToString()
					}))))
				));
			}
		}
	}
}