// Staff passes: a note signed by bskftw that lets one Steam account run with flagged mods (like Unity Explorer). Every car check still applies.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace TRC.TuneLoader {
	/// <summary>
	/// A pass is one line: "1|SteamID|name|issued yyyy-MM-dd|signature". bskftw makes it with tools/staffpass, signing
	/// with staff.key, which never leaves their PC. The mod only holds the public half below, so it can check a pass but
	/// never make one. Passes never expire (bskftw, Nov 19): to take one away, add the Steam ID to <see cref="Revoked"/>
	/// and release. Like every check on the player's PC, someone who edits the game could get past it; it's there so
	/// honest staff with Unity Explorer loaded still have their runs and battles count.
	/// </summary>
	public static class StaffPass {
		public const string FileName = "staff.pass";
		public const int MaxLength = 1024;

		/// <summary>Steam IDs whose passes no longer count.</summary>
		public static readonly ulong[] Revoked = { };

		/// <summary>The public half of staff.key (RSA 2048, base64), printed by `tools/staffpass keys`. Empty = no pass is valid.</summary>
		private const string PublicModulus = "xs6wAltgMxvfD0vIbI7gftPYC+Qifwg6T5zTWgEdadEvCMmoM0p1aIITWhYArC5IgEm3MHq6niqETeNUbnlUUKh47WhAjfB/xxibtGb7jN5QpTI61pG026QZ1wiIpMSRTk2OELQj4u5U8QZ6BsZjIowr21EbKP74ncV+PsnTFMxFnkh8FYrWrEP7kEV/NKojELQrd43SNF5LLK5SeaaD+g29SUx9f8KcI+0JLZl4wUVh2BiWZ19JYXNuYC7ea3RF0y+kpNMjqJUfH8kN5y4x5aw+2fqx0BA+zOZnDH2mjJezezxnQOjXwwgCmFgOct8Rty8NV4sQPDx+EbGraWRs6Q==";
		private const string PublicExponent = "AQAB";

		private static readonly Dictionary<string, bool> checkedPasses_ = new Dictionary<string, bool>(StringComparer.Ordinal);

		/// <summary>The text that's signed.</summary>
		public static string Body(ulong steamId, string name, string issued) => $"trc-staff-pass|1|{steamId.ToString(CultureInfo.InvariantCulture)}|{name}|{issued}";

		public static string Make(ulong steamId, string name, string issued, byte[] signature) => $"1|{steamId.ToString(CultureInfo.InvariantCulture)}|{name}|{issued}|{Convert.ToBase64String(signature)}";

		/// <summary>The name on a pass ("" when it isn't one).</summary>
		public static string NameOf(string pass) => Split(pass)?[2] ?? "";

		/// <summary>Whether <paramref name="pass"/> is a pass bskftw signed for <paramref name="steamId"/>, not taken back. Checked once per pass.</summary>
		public static bool IsValid(string pass, ulong steamId) {
			if (PublicModulus.Length == 0 || string.IsNullOrEmpty(pass)) {
				return false;
			}
			string key = $"{steamId}|{pass}";
			lock (checkedPasses_) {
				if (checkedPasses_.TryGetValue(key, out bool known)) {
					return known;
				}
			}
			bool valid = IsValid(pass, steamId, new RSAParameters { Modulus = Convert.FromBase64String(PublicModulus), Exponent = Convert.FromBase64String(PublicExponent) });
			lock (checkedPasses_) {
				if (checkedPasses_.Count > 64) {
					checkedPasses_.Clear();
				}
				checkedPasses_[key] = valid;
			}
			return valid;
		}

		/// <summary>The same check against any public key (the self-test uses its own).</summary>
		public static bool IsValid(string pass, ulong steamId, RSAParameters publicKey) {
			string[] parts = Split(pass);
			if (parts == null || steamId == 0 || Revoked.Contains(steamId) || parts[1] != steamId.ToString(CultureInfo.InvariantCulture)) {
				return false;
			}
			try {
				using (RSA rsa = RSA.Create()) {
					rsa.ImportParameters(publicKey);
					byte[] body = Encoding.UTF8.GetBytes(Body(steamId, parts[2], parts[3]));
					return rsa.VerifyData(body, Convert.FromBase64String(parts[4]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
				}
			} catch (Exception) {
				return false;
			}
		}

		private static string[] Split(string pass) {
			if (string.IsNullOrEmpty(pass) || pass.Length > MaxLength) {
				return null;
			}
			string[] parts = pass.Trim().Split('|');
			return parts.Length == 5 && parts[0] == "1" ? parts : null;
		}
	}
}
