// Seals the ratings file so it can't be changed in Notepad: scrambled, fingerprinted, and tied to the SteamID.
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace TRC.TuneLoader {
	/// <summary>
	/// Seals the ratings file (0.15.2, bskftw: "so people can't easily change their elo or licence via notepad"):
	/// the text is scrambled (AES) and fingerprinted (HMAC-SHA256) with keys made from a private key and the
	/// player's SteamID. Changing a single character, or using another player's file, breaks the fingerprint.
	///
	/// The private key isn't in the public code: the player build embeds it from ratings.key next to the project
	/// (git-ignored, like the build key), and KSL encrypts the .ksm it's in. Without it (a build from the public
	/// code) a development key is used, so those files don't open in the real mod and the other way round.
	///
	/// It stops Notepad, not a determined cheater: deleting the file, going back to an older copy, or reading the
	/// key out of a running game are only stopped once the TRC server holds the ratings.
	/// </summary>
	public static class RatingSeal {
		/// <summary>The first line of a sealed file; the rest is base64 of IV (16 bytes), the scrambled text, then the fingerprint (32 bytes).</summary>
		public const string Header = "TRC-RATINGS-1";

		/// <summary>The embedded private key's resource name (TRCTuneLoader.csproj).</summary>
		public const string KeyResource = "TRC.TuneLoader.RatingsKey";

		private const string DevelopmentKey = "TRC Tune Loader development key: not the player build's";

		private static byte[] secret_ = Encoding.UTF8.GetBytes(DevelopmentKey);

		/// <summary>True once the player build's private key was loaded (else the development key is used).</summary>
		public static bool HasPrivateKey { get; private set; }

		/// <summary>Uses <paramref name="key"/> (the text of ratings.key) instead of the development key.</summary>
		public static void UseKey(string key) {
			key = (key ?? "").Trim();
			if (key.Length >= 32) {
				secret_ = Encoding.UTF8.GetBytes(key);
				HasPrivateKey = true;
			}
		}

		/// <summary>Loads the embedded private key, when this build has one.</summary>
		public static void LoadEmbeddedKey() {
			using (Stream stream = typeof(RatingSeal).Assembly.GetManifestResourceStream(KeyResource)) {
				if (stream != null) {
					using (var reader = new StreamReader(stream, Encoding.UTF8)) {
						UseKey(reader.ReadToEnd());
					}
				}
			}
		}

		private static byte[] KeyFor(string use, ulong steamId) {
			using (var hmac = new HMACSHA256(secret_)) {
				return hmac.ComputeHash(Encoding.UTF8.GetBytes($"{use}|{steamId}"));
			}
		}

		public static string Seal(string text, ulong steamId) {
			byte[] plain = Encoding.UTF8.GetBytes(text);
			byte[] iv;
			byte[] scrambled;
			using (Aes aes = Aes.Create()) {
				aes.Key = KeyFor("scramble", steamId);
				aes.GenerateIV();
				iv = aes.IV;
				using (ICryptoTransform encryptor = aes.CreateEncryptor()) {
					scrambled = encryptor.TransformFinalBlock(plain, 0, plain.Length);
				}
			}
			byte[] body = iv.Concat(scrambled).ToArray();
			byte[] mark = Fingerprint(body, steamId);
			return Header + "\n" + Convert.ToBase64String(body.Concat(mark).ToArray()) + "\n";
		}

		/// <summary>The text in a sealed file, or false when it isn't one, was changed, or belongs to another SteamID.</summary>
		public static bool TryOpen(string file, ulong steamId, out string text) {
			text = "";
			try {
				string[] lines = (file ?? "").Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
				if (lines.Length != 2 || lines[0].Trim() != Header) {
					return false;
				}
				byte[] all = Convert.FromBase64String(lines[1].Trim());
				if (all.Length < 16 + 16 + 32) {
					return false;
				}
				byte[] body = all.Take(all.Length - 32).ToArray();
				byte[] mark = all.Skip(all.Length - 32).ToArray();
				if (!SameBytes(mark, Fingerprint(body, steamId))) {
					return false;
				}
				using (Aes aes = Aes.Create()) {
					aes.Key = KeyFor("scramble", steamId);
					aes.IV = body.Take(16).ToArray();
					using (ICryptoTransform decryptor = aes.CreateDecryptor()) {
						text = Encoding.UTF8.GetString(decryptor.TransformFinalBlock(body, 16, body.Length - 16));
					}
				}
				return true;
			} catch (Exception) {
				return false;
			}
		}

		private static byte[] Fingerprint(byte[] body, ulong steamId) {
			using (var hmac = new HMACSHA256(KeyFor("fingerprint", steamId))) {
				return hmac.ComputeHash(Encoding.UTF8.GetBytes(Header).Concat(body).ToArray());
			}
		}

		/// <summary>Compares every byte, so how long it takes says nothing about where they differ.</summary>
		private static bool SameBytes(byte[] a, byte[] b) {
			if (a.Length != b.Length) {
				return false;
			}
			int diff = 0;
			for (int i = 0; i < a.Length; i++) {
				diff |= a[i] ^ b[i];
			}
			return diff == 0;
		}
	}
}
