using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using trakit.objects;

namespace trakit.hmac {
	/// <summary>
	/// 
	/// </summary>
	public static class signatures {
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
		public static string hmacSignInput(string secretBase64, string utf8Input)
			=> signatures.hmacSignInput(
				Convert.FromBase64String(secretBase64),
				Encoding.UTF8.GetBytes(utf8Input)
			);
		/// <summary>
		/// Creates a signature for a given input using the given secret.
		/// </summary>
		/// <param name="secretBase64"></param>
		/// <param name="utf8Input"></param>
		/// <returns></returns>
		public static string hmacSignInput(byte[] secret, string utf8Input)
			=> signatures.hmacSignInput(
				secret,
				Encoding.UTF8.GetBytes(utf8Input)
			);
		/// <summary>
		/// Creates a signature for a given input using the given secret.
		/// </summary>
		/// <param name="secret"></param>
		/// <param name="input"></param>
		/// <returns></returns>
		public static string hmacSignInput(byte[] secret, byte[] input) {
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
		public static string createHmacSignedInput(
			string apiKey,
			string secretBase64,
			DateTime date,
			HttpMethod method,
			Uri absoluteUri,
			long requestLength
		) => signatures.createHmacSignedInput(
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
		public static string createHmacSignedInput(
			string apiKey,
			byte[] apiSecret,
			DateTime date,
			HttpMethod method,
			Uri absoluteUri,
			long requestLength
		) => signatures.hmacSignInput(apiSecret, string.Join("\n", new[] {
			apiKey,
			date.ToString("yyyyMMddHHmmss"),
			method.ToString(),
			absoluteUri.getSanitizedUri() ,
			requestLength.ToString()
		}));

		/// <summary>
		/// Returns the URI with the session/machine keys removed from the <see cref="Uri.Query"/>.
		/// </summary>
		/// <param name="uri"></param>
		/// <returns></returns>
		public static string getSanitizedUri(this Uri uri) {
			string sanitized = uri.AbsoluteUri.Substring(0, uri.AbsoluteUri.Length - uri.Query.Length - uri.Fragment.Length);
			if (!string.IsNullOrEmpty(uri.Query)) {
				var parts = uri.Query.Substring(1)
									.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries)
									.Select(
										s => s.StartsWith(signatures.SESSION_ID + "=")
											|| s.StartsWith(signatures.AUTH_TOKEN + "=")
											|| s.StartsWith(signatures.AUTH_SIGNATURE + "=")
												? string.Empty
												: s
									)
									.Where(s => s != string.Empty)
									.ToArray();
				if (parts.Length > 0) sanitized += "?" + string.Join("&", parts);
			}
			if (uri.Fragment != string.Empty) sanitized += uri.Fragment;
			return sanitized;
		}

		/// <summary>
		/// Adds the appropriate <c>Date</c> and <c>Authentication</c> headers to the <paramref name="request"/> for the given <see cref="Machine"/>.
		/// </summary>
		/// <param name="request"></param>
		/// <param name="machine"></param>
		/// <returns></returns>
		public static AuthenticationHeaderValue createHmacHeader(HttpRequestMessage request, Machine machine)
			=> request.Headers.Authorization = signatures.createHmacHeader(
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
		public static AuthenticationHeaderValue createHmacHeader(
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
				+ signatures.createHmacSignedInput(
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