// Result codes for the season bot: a finished battle or race's event line, signed so the bot can tell it came from the mod unchanged.
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace TRC.TuneLoader {
	/// <summary>
	/// docs/archive/IMPROVEMENT_PLAN.md A4 (bskftw, Nov 12: "alongside screenshots at first"): every finished battle and race gets a
	/// code to paste into the season bot's <c>/report-race</c>. The code is the event's line from the server events
	/// (EventLedger: course, cars, tune set, times, the opponent check) and an HMAC-SHA256 of it with a private key
	/// that only the player build and the bot have (results.key, git-ignored, never committed). Change one letter and
	/// the bot says so. Like the ratings seal it stops editing, not someone who digs the key out of the game.
	/// </summary>
	public static class ResultCode {
		public const string Prefix = "TRCR1";

		/// <summary>The embedded private key's resource name (TRCTuneLoader.csproj).</summary>
		public const string KeyResource = "TRC.TuneLoader.ResultsKey";

		private const string DevelopmentKey = "TRC Tune Loader development results key: the bot refuses these";

		private static byte[] secret_ = Encoding.UTF8.GetBytes(DevelopmentKey);

		public static bool HasPrivateKey { get; private set; }

		/// <summary>Uses <paramref name="key"/> (the text of results.key) instead of the development key.</summary>
		public static void UseKey(string key) {
			key = (key ?? "").Trim();
			if (key.Length >= 32) {
				secret_ = Encoding.UTF8.GetBytes(key);
				HasPrivateKey = true;
			}
		}

		public static void LoadEmbeddedKey() {
			using (Stream stream = typeof(ResultCode).Assembly.GetManifestResourceStream(KeyResource)) {
				if (stream != null) {
					using (var reader = new StreamReader(stream, Encoding.UTF8)) {
						UseKey(reader.ReadToEnd());
					}
				}
			}
		}

		/// <summary>The code for one event line: <c>TRCR1.</c>, the line in base64url, a dot, its signature in base64url.</summary>
		public static string Make(string line) {
			byte[] body = Encoding.UTF8.GetBytes(line ?? "");
			return $"{Prefix}.{Base64Url(body)}.{Base64Url(Sign(body))}";
		}

		/// <summary>The event line in a code, or false when it isn't one, was changed, or was signed with another key.</summary>
		public static bool TryRead(string code, out string line) {
			line = "";
			try {
				string[] parts = (code ?? "").Trim().Split('.');
				if (parts.Length != 3 || parts[0] != Prefix) {
					return false;
				}
				byte[] body = FromBase64Url(parts[1]);
				byte[] mark = FromBase64Url(parts[2]);
				byte[] expected = Sign(body);
				if (mark.Length != expected.Length) {
					return false;
				}
				int diff = 0;
				for (int i = 0; i < mark.Length; i++) {
					diff |= mark[i] ^ expected[i];
				}
				if (diff != 0) {
					return false;
				}
				line = Encoding.UTF8.GetString(body);
				return true;
			} catch (FormatException) {
				return false;
			}
		}

		private static byte[] Sign(byte[] body) {
			using (var hmac = new HMACSHA256(secret_)) {
				return hmac.ComputeHash(body);
			}
		}

		private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

		private static byte[] FromBase64Url(string text) {
			string s = text.Replace('-', '+').Replace('_', '/');
			return Convert.FromBase64String(s + new string('=', (4 - s.Length % 4) % 4));
		}
	}
}
