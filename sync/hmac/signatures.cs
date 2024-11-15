using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Trakit.Objects;

namespace Trakit.Hmac {
	/// <summary>
	/// 
	/// </summary>
	public static class Signatures {
		/// <summary>
		/// Common name for session ID used by all systems.
		/// </summary>
		public const string SESSION_ID = "ghostId";
		/// <summary>
		/// Common name for authorization token used by all systems.
		/// </summary>
		public const string AUTH_TOKEN = "shadowKey";
		/// <summary>
		/// Common name for authorization signature when using HMAC.
		/// </summary>
		public const string AUTH_SIGNATURE = "shadowSig";

		/// <summary>
		/// Creates a signature for a given input using the given secret.
		/// </summary>
		/// <param name="secretBase64"></param>
		/// <param name="utf8Input"></param>
		/// <returns></returns>
		public static string HmacSignInput(string secretBase64, string utf8Input)
			=> Signatures.HmacSignInput(
				Convert.FromBase64String(secretBase64),
				Encoding.UTF8.GetBytes(utf8Input)
			);
		/// <summary>
		/// Creates a signature for a given input using the given secret.
		/// </summary>
		/// <param name="secretBase64"></param>
		/// <param name="utf8Input"></param>
		/// <returns></returns>
		public static string HmacSignInput(byte[] secret, string utf8Input)
			=> Signatures.HmacSignInput(
				secret,
				Encoding.UTF8.GetBytes(utf8Input)
			);
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
		/// <param name="secretBase64"></param>
		/// <param name="date"></param>
		/// <param name="method"></param>
		/// <param name="absoluteUri"></param>
		/// <param name="requestLength"></param>
		/// <returns></returns>
		public static string CreateHmacSignedInput(
			string apiKey,
			string secretBase64,
			DateTime date,
			HttpMethod method,
			Uri absoluteUri,
			long requestLength
		) => Signatures.CreateHmacSignedInput(
			apiKey,
			Convert.FromBase64String(secretBase64),
			date,
			method,
			absoluteUri,
			requestLength
		);
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
		public static string CreateHmacSignedInput(
			string apiKey,
			byte[] apiSecret,
			DateTime date,
			HttpMethod method,
			Uri absoluteUri,
			long requestLength
		) => Signatures.HmacSignInput(apiSecret, string.Join("\n", new[] {
			apiKey,
			date.ToString("yyyyMMddHHmmss"),
			method.ToString(),
			absoluteUri.GetSanitizedUri(),
			requestLength.ToString()
		}));

		/// <summary>
		/// Returns the URI with the session/machine keys removed from the <see cref="Uri.Query"/>.
		/// </summary>
		/// <param name="uri"></param>
		/// <returns></returns>
		public static string GetSanitizedUri(this Uri uri) {
			UriBuilder sanitized = new UriBuilder(uri);
			if (!string.IsNullOrEmpty(uri.Query)) {
				sanitized.Query = string.Join(
					"&",
					uri.Query.Substring(1)
							.Split('&')
							.Select(
								s => s.StartsWith(Signatures.SESSION_ID + "=")
									|| s.StartsWith(Signatures.AUTH_TOKEN + "=")
									|| s.StartsWith(Signatures.AUTH_SIGNATURE + "=")
										? string.Empty
										: s
							)
							.Where(s => s != string.Empty)
				);
			}
			return sanitized.ToString();
		}

		/// <summary>
		/// Adds the appropriate <c>Date</c> and <c>Authentication</c> headers to the <paramref name="request"/> for the given <see cref="Machine"/>.
		/// </summary>
		/// <param name="request"></param>
		/// <param name="machine"></param>
		/// <returns></returns>
		public static AuthenticationHeaderValue AddHmacHeader(HttpRequestMessage request, Machine machine)
			=> request.Headers.Authorization = Signatures.CreateHmacHeader(
				machine.key,
				machine.secret,
				(request.Headers.Date ?? DateTimeOffset.UtcNow).UtcDateTime,
				request.Method,
				request.RequestUri,
				request.Content?.Headers?.ContentLength ?? 0
			);
		/// <summary>
		/// Adds the appropriate <c>Date</c> and <c>Authentication</c> headers to the <paramref name="request"/> for the given values.
		/// </summary>
		/// <param name="apiKey"></param>
		/// <param name="secretBase64"></param>
		/// <param name="date"></param>
		/// <param name="method"></param>
		/// <param name="absoluteUri"></param>
		/// <param name="requestLength"></param>
		/// <returns></returns>
		public static AuthenticationHeaderValue CreateHmacHeader(
			string apiKey,
			string secretBase64,
			DateTime date,
			HttpMethod method,
			Uri absoluteUri,
			long requestLength
		) => new AuthenticationHeaderValue(
			"HMAC256",
			Convert.ToBase64String(Encoding.UTF8.GetBytes(
				apiKey
				+ ":"
				+ Signatures.CreateHmacSignedInput(
					apiKey,
					secretBase64,
					date,
					method,
					absoluteUri,
					requestLength
				)
			))
		);
	}
}