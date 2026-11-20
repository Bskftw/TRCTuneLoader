// Signed room messages: what the mod says in its log about packets without a seal or refused ones (not in the self-tests).
using System.Collections.Generic;

namespace TRC.TuneLoader {
	public static partial class MessageSeal {
		/// <summary>Senders whose packets came without a seal (copies before 0.19), each said once in the log.</summary>
		private static readonly HashSet<ulong> unsigned_ = new HashSet<ulong>();

		public static void NoteUnsigned(ulong from, string what) {
			lock (unsigned_) {
				if (!unsigned_.Add(from)) {
					return;
				}
			}
			TrcLog.Info($"{what} from {from} aren't signed: an older TRC Tune Loader (before 0.19). Taken for now.");
		}

		/// <summary>Whether this player's packets came without a seal (an older version).</summary>
		public static bool IsUnsigned(ulong from) {
			lock (unsigned_) {
				return unsigned_.Contains(from);
			}
		}

		/// <summary>A refused packet, said once per sender and reason.</summary>
		public static void NoteRefused(ulong from, string what, Result why) {
			TrcLog.Once($"seal {what} {from} {why}", $"{what} from {from} refused: {(why == Result.Bad ? "its seal doesn't match (changed on the way, or not made by the mod)" : why == Result.Replayed ? "it was sent before (a replay)" : "its time is far from this PC's clock")}. Said once.");
		}

	}
}
