using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Trakit.Objects;

namespace Trakit.Https {
	/// <summary>
	/// 
	/// </summary>
	public static class TrakitExtensions {
		/// <summary>
		/// Common name for session ID used by all systems.
		/// </summary>
		public const string SESSION_ID = "ghostId";
		/// <summary>
		/// Common name for authorization token used by all systems.
		/// </summary>
		public const string MACHINE_KEY = "shadowKey";

		/// <summary>
		/// Creates a signature for a given input using the given secret.
		/// </summary>
		/// <param name="secret"></param>
		/// <param name="input"></param>
		/// <returns></returns>
		public static string HmacSignInput(byte[] secret, byte[] input) {
			using (var hmac = new HMACSHA256(secret)) {
				return Convert.ToBase64String(hmac.ComputeHash(input));
			}
		}

		/// <summary>
		/// Creates an HMAC256 signed input for use in <see cref="HttpRequestMessage"/>s.
		/// </summary>
		/// <param name="apiKey"></param>
		/// <param name="apiSecret"></param>
		/// <param name="date"></param>
		/// <param name="method"></param>
		/// <param name="absoluteUri"></param>
		/// <param name="requestLength"></param>
		/// <returns></returns>
		public static string HmacCreateSignature(
			string apiKey,
			string apiSecret,
			DateTimeOffset date,
			HttpMethod method,
			Uri absoluteUri,
			long requestLength
		) => Convert.ToBase64String(Encoding.UTF8.GetBytes(
			apiKey
			+ ":"
			+ TrakitExtensions.HmacSignInput(
				Convert.FromBase64String(apiSecret), 
				Encoding.UTF8.GetBytes(string.Join("\n", new[] {
					apiKey,
					date.UtcDateTime.ToString("yyyyMMddHHmmss"),
					method.ToString(),
					absoluteUri.GetSanitizedUri(),
					requestLength.ToString()
				}))
			)
		));

		/// <summary>
		/// Returns the URI with the session/machine keys removed from the <see cref="Uri.Query"/>.
		/// </summary>
		/// <param name="uri"></param>
		/// <returns></returns>
		public static string GetSanitizedUri(this Uri uri) {
			UriBuilder built = new UriBuilder(uri);
			if (!string.IsNullOrEmpty(uri.Query)) {
				built.Query = string.Join(
					"&",
					uri.Query.Substring(1)
							.Split('&')
							.Select(s => s.StartsWith(SESSION_ID + "=") || s.StartsWith(MACHINE_KEY + "=") ? "" : s)
							.Where(s => s != "")
				);
			}
			return built.Uri.ToString();
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="machine"></param>
		/// <param name="request"></param>
		/// <param name="date">Timestamp for the request.  If no value is given, then <see cref="DateTimeOffset.UtcNow"/> is used. Trak-iT APIs only allow requests up to <c>15 seconds</c> old.</param>
		public static void AuthorizeRequest(this Machine machine, HttpRequestMessage request, DateTimeOffset? date = default) {
			request.Headers.Date = date 
							?? request.Headers.Date 
							?? DateTimeOffset.UtcNow;
			request.Headers.Authorization = new AuthenticationHeaderValue(
				"HMAC256",
				TrakitExtensions.HmacCreateSignature(
					machine.key,
					machine.secret,
					request.Headers.Date.Value,
					request.Method,
					request.RequestUri,
					request.Content?.Headers?.ContentLength ?? 0
				)
			);
		}
	}
}