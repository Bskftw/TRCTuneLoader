using System;
using System.Collections.Generic;

namespace TRC.TuneLoader.CodeCheck {
	/// <summary>Stage 1 of IMPROVEMENT_PLAN.md: who's in the room (RoomGate), the limits on messages (RateLimit), and unfinished messages kept.</summary>
	internal static partial class SelfTest {
		private static void Stage1Tests() {
			RoomGateTests();
			RateLimitTests();
			ChunkCapTests();
			SessionTests();
		}

		private static void SessionTests() {
			var quiet = new SessionStats("Akina", 0);
			quiet.Drove(0.4, 60f);
			Expect(!quiet.Worth, "a look around a map isn't worth a summary");

			var s = new SessionStats("Project Dream", 100);
			for (int i = 0; i < 300; i++) {
				s.Drove(0.1, 80f + i % 90);
			}
			s.Drove(0, 9000f); // a teleport's "speed"
			s.Run("Sadamine Downhill", 250.5);
			s.Run("Sadamine Downhill", 243.14);
			s.Run("Akina Downhill", 300);
			s.Trap("Trap 5", 176.7f);
			s.Trap("Trap 1", 96.7f);
			s.Battle(true);
			s.Battle(false);
			s.Race(2, 6);
			List<string> lines = s.Lines(100 + 25 * 60, t => t.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture));
			Expect(s.Worth && lines.Count == 5, $"a session with runs, traps, battles and a race has a line for each ({lines.Count})");
			Expect(lines[0] == "Project Dream: 25 min, 30.0 km driven, top speed 169 km/h", $"the first line is the map, time, distance and top speed: {lines[0]}");
			Expect(lines[1] == "3 time attack runs finished, best 243.140 on Sadamine Downhill", $"the best run is the quickest: {lines[1]}");
			Expect(lines[2] == "2 speed trap passes, fastest 176.7 km/h at Trap 5", $"the fastest trap: {lines[2]}");
			Expect(lines[3] == "2 battles, 1 won" && lines[4] == "1 race, the first one 2nd of 6", $"battles and races: {lines[3]} / {lines[4]}");
		}

		private static void RoomGateTests() {
			const ulong me = 76561198000000001UL, friend = 76561198000000002UL, late = 76561198000000003UL, stranger = 76561198000000099UL;
			ulong console = RaceSession.ConsoleId("123|PS4");
			var room = new List<ulong> { me, friend, console };
			bool readable = true;
			int reads = 0;
			double clock = 100;
			Func<ICollection<ulong>, bool> oldRead = RoomGate.ReadRoom;
			Func<ulong> oldMe = RoomGate.Me;
			Func<double> oldClock = RoomGate.Clock;
			try {
				RoomGate.Reset();
				RoomGate.ReadRoom = null;
				Expect(RoomGate.Allows(stranger), "with no room reader the gate is off: everyone passes");

				RoomGate.ReadRoom = ids => {
					reads++;
					if (!readable) {
						return false;
					}
					foreach (ulong id in room) {
						ids.Add(id);
					}
					return true;
				};
				RoomGate.Me = () => me;
				RoomGate.Clock = () => clock;

				Expect(RoomGate.Allows(friend), "a player in the room passes");
				Expect(RoomGate.Allows(console), "a console player's race ID passes (the host sends their messages under it)");
				Expect(!RoomGate.Allows(stranger), "a sender CarX doesn't list is refused");
				Expect(RoomGate.Ignored == 1, "a refused message is counted");

				// Someone joins between two reads of the room: their first message makes the gate look again.
				clock += 0.5;
				room.Add(late);
				Expect(RoomGate.Allows(late), "someone who just joined passes (the room is read again for a sender it doesn't know)");

				// A flood from made-up senders doesn't make it read the game for every message.
				int before = reads;
				for (int i = 0; i < 1000; i++) {
					RoomGate.Allows(stranger + (ulong)i);
				}
				Expect(reads - before <= 1, $"a flood in one moment reads the room once at most ({reads - before} reads)");

				// Not in a room, or the list can't be read: nothing is refused.
				clock += 5;
				readable = false;
				Expect(RoomGate.Allows(stranger), "when the room can't be read, nothing is refused");

				// The list doesn't have this player in it (still loading, or its IDs don't read like ours): nothing is refused.
				clock += 5;
				readable = true;
				room.Remove(me);
				Expect(RoomGate.Allows(stranger), "when this player isn't in the list, nothing is refused");
				room.Add(me);
				clock += 5;
				Expect(!RoomGate.Allows(stranger), "and refused again once the list is right");

				// This player's ID isn't known yet.
				RoomGate.Me = () => 0;
				clock += 5;
				Expect(RoomGate.Allows(stranger), "when this player's own ID isn't known, nothing is refused");

				// Someone leaves: within a second their messages stop counting.
				RoomGate.Me = () => me;
				clock += 5;
				Expect(RoomGate.Allows(friend), "still here");
				room.Remove(friend);
				clock += 1.5;
				Expect(!RoomGate.Allows(friend), "a player who left is refused once the room is read again");
			} finally {
				RoomGate.ReadRoom = oldRead;
				RoomGate.Me = oldMe;
				RoomGate.Clock = oldClock;
				RoomGate.Reset();
			}
		}

		private static void RateLimitTests() {
			var limit = new RateLimit(perSecond: 10, burst: 20, mostKeys: 4);
			int passed = 0;
			for (int i = 0; i < 50; i++) {
				if (limit.Allow(1, 0)) {
					passed++;
				}
			}
			Expect(passed == 20, $"a burst passes, the rest in the same moment doesn't ({passed} of 50)");
			Expect(limit.Allow(2, 0), "another sender has its own allowance");
			Expect(!limit.Allow(1, 0.05), "half a message later, still nothing");
			Expect(limit.Allow(1, 0.11), "a tenth of a second gives one more");
			passed = 0;
			for (int i = 0; i < 50; i++) {
				if (limit.Allow(1, 100)) {
					passed++;
				}
			}
			Expect(passed == 20, "after a long quiet it's a burst again, not more");

			// More senders than it keeps: the ones over share one allowance, so made-up senders can't each bring their own.
			var few = new RateLimit(perSecond: 1, burst: 5, mostKeys: 3);
			for (ulong k = 1; k <= 3; k++) {
				few.Allow(k, 0);
			}
			passed = 0;
			for (ulong k = 100; k < 200; k++) {
				if (few.Allow(k, 0)) {
					passed++;
				}
			}
			Expect(passed == 5, $"a hundred new senders at once share one allowance ({passed} passed)");

			// The mod's own busiest sending never touches a channel's limit: a traffic host at 20 packets a second for a minute.
			var channel = new ChannelLimit("test", 100, 400, 400, 1600);
			bool allPassed = true;
			for (int i = 0; i < 1200; i++) {
				allPassed &= channel.Allow(7, i / 20.0);
			}
			Expect(allPassed, "20 packets a second from one sender all pass");
			// And 12 seconds' worth arriving at once after a freeze (a map loading).
			allPassed = true;
			for (int i = 0; i < 240; i++) {
				allPassed &= channel.Allow(8, 1000);
			}
			Expect(allPassed, "what piled up during a 12 s freeze passes");
			// A flood doesn't.
			passed = 0;
			for (int i = 0; i < 5000; i++) {
				if (channel.Allow(9, 2000)) {
					passed++;
				}
			}
			Expect(passed == 400 && channel.Cut == 4600, $"a flood from one sender is cut to its burst ({passed} passed, {channel.Cut} cut)");
			passed = 0;
			for (int i = 0; i < 5000; i++) {
				if (channel.Allow(1000 + (ulong)i, 3000)) {
					passed++;
				}
			}
			// 256 senders get an allowance each; the rest share one (400): never more than the channel's own burst.
			Expect(passed == 656, $"a flood from made-up senders is cut ({passed} of 5000 passed)");
		}

		private static void ChunkCapTests() {
			var chunks = new BattleChunks();
			DateTime now = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);
			// A real two-piece message whose first piece arrives first...
			byte[] real = new byte[BattleChunks.ChunkBytes + 10];
			for (int i = 0; i < real.Length; i++) {
				real[i] = (byte)(i * 7);
			}
			List<BattleChunks.Piece> pieces = BattleChunks.Split(real);
			Expect(chunks.Add(5, pieces[0], now) == null, "half a message isn't a message");
			// ...then a few first pieces of other messages: the real one is still finished.
			for (uint i = 0; i < 10; i++) {
				chunks.Add(6, new BattleChunks.Piece { MessageId = 1000 + i, Index = 0, Count = 3, Data = new byte[4] }, now.AddSeconds(1));
			}
			byte[] whole = chunks.Add(5, pieces[1], now.AddSeconds(2));
			Expect(whole != null && whole.Length == real.Length, "a message still comes together with other unfinished ones about");

			// A flood of first pieces: only so many are kept, the newest ones.
			var flooded = new BattleChunks();
			flooded.Add(5, pieces[0], now);
			for (uint i = 0; i < 500; i++) {
				flooded.Add(6, new BattleChunks.Piece { MessageId = 5000 + i, Index = 0, Count = 3, Data = new byte[4] }, now.AddSeconds(1 + i * 0.001));
			}
			byte[] last = new byte[4];
			var newest = new BattleChunks.Piece { MessageId = 5499, Index = 1, Count = 3, Data = last };
			// Its first piece is still kept (adding the second finishes nothing yet, but isn't a new start either): a third makes it whole or not by its hash, never a crash.
			Expect(flooded.Add(6, newest, now.AddSeconds(2)) == null, "the flood's pieces don't make a message");
			Expect(flooded.Add(5, pieces[1], now.AddSeconds(3)) == null, "the oldest unfinished message made way for the flood (it's sent again in real use)");
		}
	}
}
