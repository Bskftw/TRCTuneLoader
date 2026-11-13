// Whether a message's sender is in the room: messages from anyone CarX doesn't list are dropped.
using System;
using System.Collections.Generic;

namespace TRC.TuneLoader {
	/// <summary>
	/// Whether a message's sender is in the room (IMPROVEMENT_PLAN.md N1). KSL doesn't say who sent a packet, so every
	/// message carries its sender's Steam ID, and a changed copy of the mod could put any number there. Messages from
	/// someone CarX doesn't list in the room are ignored: that stops made-up senders (it can't stop someone pretending to
	/// be a player who is in the room).
	///
	/// It never guesses. It only refuses when CarX's room list could be read just now and this player is in it (which
	/// shows the list is loaded and its IDs read the way senders write theirs); otherwise everything is let through, as
	/// before. A sender it doesn't know makes it read the room again before refusing, so someone who only just joined
	/// isn't cut. Call it on the main thread (reading the room reads the game). Nothing here knows about CarX: the mod
	/// gives it <see cref="ReadRoom"/>, and the self-test gives it its own.
	/// </summary>
	public static class RoomGate {
		/// <summary>Fills in the ID of everyone in the room (false: not in a room, or it can't be read). Null: the gate is off.</summary>
		public static Func<ICollection<ulong>, bool> ReadRoom;

		/// <summary>This player's own ID (0: not known yet).</summary>
		public static Func<ulong> Me;

		/// <summary>Seconds; the self-test sets its own.</summary>
		public static Func<double> Clock = () => RateLimit.Now;

		/// <summary>Where a refused sender is reported (the mod sets it to its log).</summary>
		public static Action<string> Log;

		/// <summary>The room is read again after this long, and at once (but not more often than <see cref="AgainSeconds"/>) for a sender it doesn't know.</summary>
		public const double FreshSeconds = 1.0, AgainSeconds = 0.1;

		private static HashSet<ulong> ids_ = new HashSet<ulong>(), scratch_ = new HashSet<ulong>();
		private static readonly HashSet<ulong> said_ = new HashSet<ulong>();
		private static bool known_;
		private static double readAt_ = -1000;

		/// <summary>Messages refused so far, for the bug report.</summary>
		public static int Ignored { get; private set; }

		/// <summary>The room's list is known right now (so senders outside it are refused).</summary>
		public static bool Known => known_;

		public static bool Allows(ulong sender) {
			if (ReadRoom == null) {
				return true;
			}
			double now = Clock();
			if (now - readAt_ > FreshSeconds) {
				Read(now);
			}
			if (!known_ || ids_.Contains(sender)) {
				return true;
			}
			if (now - readAt_ > AgainSeconds) {
				Read(now);
				if (!known_ || ids_.Contains(sender)) {
					return true;
				}
			}
			Ignored++;
			if (said_.Count < 8 && said_.Add(sender)) {
				Log?.Invoke($"Ignoring messages from {sender}: CarX doesn't list them in this room.");
			}
			return false;
		}

		private static void Read(double now) {
			readAt_ = now;
			scratch_.Clear();
			bool ok;
			try {
				ok = ReadRoom(scratch_);
			} catch (Exception) {
				ok = false;
			}
			ulong me = 0;
			try {
				me = Me != null ? Me() : 0;
			} catch (Exception) {
				me = 0;
			}
			known_ = ok && me != 0 && scratch_.Contains(me);
			(ids_, scratch_) = (scratch_, ids_);
		}

		/// <summary>For the bug report's summary.</summary>
		public static string Summary => ReadRoom == null ? "off" : $"{(known_ ? $"room list known ({ids_.Count} IDs)" : "room list not known, everything let through")}, {Ignored} message(s) ignored";

		/// <summary>Back to nothing known (the self-test, between cases).</summary>
		public static void Reset() {
			ids_.Clear();
			scratch_.Clear();
			said_.Clear();
			known_ = false;
			readAt_ = -1000;
			Ignored = 0;
		}
	}
}
