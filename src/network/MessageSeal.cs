// Signed room messages (docs/archive/IMPROVEMENT_PLAN.md A2, Stage 5): a fingerprint made with the mod's built-in key on the end of each packet.
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace TRC.TuneLoader {
	/// <summary>
	/// Each battle, race, room and report packet ends with a seal: when it was sent, a number that goes up with every packet,
	/// and an HMAC-SHA256 fingerprint (16 bytes) of the packet and those two, made with a key built into the mod (from
	/// ratings.key, like <see cref="RatingSeal"/>). So a packet can't be changed on the way, made by a script that isn't
	/// the mod, or sent again later (<see cref="Guard"/>). It doesn't stop someone who digs the key out of the mod, and it
	/// never proves the sender's game is honest: that's the opponent check and the season bot (docs/archive/IMPROVEMENT_PLAN.md A2).
	/// The seal goes on the end, so copies from before 0.19 read the packet as always; for now a packet without one (from
	/// those copies) is still taken, and the sender is noted as an older version.
	/// </summary>
	public static partial class MessageSeal {
		/// <summary>"TRS1", last on a sealed packet.</summary>
		public static readonly byte[] Magic = { 0x54, 0x52, 0x53, 0x31 };
		public const int TagBytes = 16;

		/// <summary>A packet more than this far from this PC's clock is refused (clocks may be minutes apart; a replay is caught by its number anyway).</summary>
		public static readonly TimeSpan MostOff = TimeSpan.FromMinutes(30);

		public enum Result { Unsigned, Good, Bad, Replayed, Stale }

		private static byte[] key_ = Derive(Encoding.UTF8.GetBytes("TRC Tune Loader development key: not the player build's"));
		private static long counter_;

		/// <summary>Before the first packet: the mod's private key, if this build has one (it's loaded once).</summary>
		public static void EnsureKey() {
			if (!RatingSeal.HasPrivateKey) {
				RatingSeal.LoadEmbeddedKey();
			}
		}

		/// <summary>Reads a seal off the end of a packet (after its last field): null tag when there's none.</summary>
		public static (long Ticks, uint Counter, byte[] Tag) ReadSeal(Func<long> readLong, Func<uint> readUInt, Func<int, byte[]> readBytes) {
			try {
				long ticks = readLong();
				uint counter = readUInt();
				byte[] tag = readBytes(TagBytes);
				byte[] magic = readBytes(Magic.Length);
				if (tag != null && tag.Length == TagBytes && magic != null && magic.Length == Magic.Length && Same(magic, Magic)) {
					return (ticks, counter, tag);
				}
			} catch (Exception) {
				// Nothing after the last field: an older copy's packet.
			}
			return (0, 0, null);
		}

		/// <summary>The messages' own key, made from the mod's private key (so ratings.key itself never signs a message).</summary>
		public static void UseSecret(byte[] secret) {
			key_ = Derive(secret);
		}

		private static byte[] Derive(byte[] secret) {
			using (var hmac = new HMACSHA256(secret)) {
				return hmac.ComputeHash(Encoding.UTF8.GetBytes("trc room messages 1"));
			}
		}

		/// <summary>The fingerprint of a packet's bytes on a channel, sent at <paramref name="ticks"/> with number <paramref name="counter"/>.</summary>
		public static byte[] Tag(string channel, byte[] body, long ticks, uint counter) {
			using (var hmac = new HMACSHA256(key_)) {
				var data = new MemoryStream();
				using (var w = new BinaryWriter(data, Encoding.UTF8)) {
					w.Write(channel ?? "");
					w.Write(body?.Length ?? 0);
					w.Write(body ?? new byte[0]);
					w.Write(ticks);
					w.Write(counter);
				}
				byte[] full = hmac.ComputeHash(data.ToArray());
				var tag = new byte[TagBytes];
				Array.Copy(full, tag, TagBytes);
				return tag;
			}
		}

		/// <summary>A new seal for a packet now: when, its number, and the fingerprint.</summary>
		public static (long Ticks, uint Counter, byte[] Tag) Make(string channel, byte[] body) {
			long ticks = DateTime.UtcNow.Ticks;
			uint counter = unchecked((uint)System.Threading.Interlocked.Increment(ref counter_));
			return (ticks, counter, Tag(channel, body, ticks, counter));
		}

		/// <summary>Remembers each sender's recent packet numbers, so one sent again is caught.</summary>
		public sealed class Guard {
			private const int Keep = 4096;
			private readonly Dictionary<ulong, (HashSet<uint> Seen, Queue<uint> Order)> senders_ = new Dictionary<ulong, (HashSet<uint>, Queue<uint>)>();
			private readonly object lock_ = new object();

			/// <summary>True the first time a sender's number is seen; false when it was seen before.</summary>
			public bool First(ulong from, uint counter) {
				lock (lock_) {
					if (!senders_.TryGetValue(from, out var s)) {
						if (senders_.Count >= 256) {
							senders_.Clear();
						}
						s = (new HashSet<uint>(), new Queue<uint>());
						senders_[from] = s;
					}
					if (!s.Seen.Add(counter)) {
						return false;
					}
					s.Order.Enqueue(counter);
					if (s.Order.Count > Keep) {
						s.Seen.Remove(s.Order.Dequeue());
					}
					return true;
				}
			}
		}

		/// <summary>Checks a packet's seal (<paramref name="tag"/> null: it had none).</summary>
		public static Result Check(string channel, byte[] body, long ticks, uint counter, byte[] tag, ulong from, Guard guard, long nowTicks) {
			if (tag == null) {
				return Result.Unsigned;
			}
			if (tag.Length != TagBytes || !Same(tag, Tag(channel, body, ticks, counter))) {
				return Result.Bad;
			}
			if (Math.Abs(nowTicks - ticks) > MostOff.Ticks) {
				return Result.Stale;
			}
			return guard == null || guard.First(from, counter) ? Result.Good : Result.Replayed;
		}

		private static bool Same(byte[] a, byte[] b) {
			int diff = a.Length ^ b.Length;
			for (int i = 0; i < a.Length && i < b.Length; i++) {
				diff |= a[i] ^ b[i];
			}
			return diff == 0;
		}

		/// <summary>The bytes that identify a SyncChannel piece (what its seal covers).</summary>
		public static byte[] PieceBody(byte protocol, ulong from, uint messageId, int index, int count, byte[] data) {
			var body = new MemoryStream();
			using (var w = new BinaryWriter(body)) {
				w.Write(protocol);
				w.Write(from);
				w.Write(messageId);
				w.Write(index);
				w.Write(count);
				w.Write(data ?? new byte[0]);
			}
			return body.ToArray();
		}

		/// <summary>Whether CarX's runtime has ECDSA, for a key pair per player later (A2's second step): only looked at and logged.</summary>
		public static string KeyPairCheck() {
			try {
				using (ECDsa ecdsa = ECDsa.Create()) {
					if (ecdsa == null) {
						return "no ECDSA in this runtime";
					}
					byte[] data = Encoding.UTF8.GetBytes("check");
					byte[] signature = ecdsa.SignData(data, HashAlgorithmName.SHA256);
					return ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256) ? $"ECDSA works ({ecdsa.KeySize}-bit)" : "ECDSA signs but doesn't verify";
				}
			} catch (Exception e) {
				return $"no ECDSA ({e.GetType().Name}: {e.Message})";
			}
		}
	}
}
