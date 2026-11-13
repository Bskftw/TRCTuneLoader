using System;
using System.Collections.Generic;
using System.Linq;

namespace TRC.TuneLoader.CodeCheck {
	/// <summary>
	/// Every message arriving twice (IMPROVEMENT_PLAN.md N8: a big message sent again, like a challenge every 3 s, can be
	/// put together and delivered twice): whole battles and races must end exactly as they do when each arrives once.
	/// </summary>
	internal static partial class SelfTest {
		private static void MessagesTwiceTests() {
			BattlesWithEveryMessageTwice();
			RaceWithEveryMessageTwice();
		}

		private static void BattlesWithEveryMessageTwice() {
			Course course = StraightCourse();
			foreach (BattleFormat format in new[] { BattleFormat.Race, BattleFormat.CatAndMouse }) {
				var (onceHost, onceGuest) = PlayBattle(course, format, 41, open: false, setUp: (h, g) => g.Acceleration = run => 4.8);
				var (twiceHost, twiceGuest) = PlayBattle(course, format, 41, open: false, setUp: (h, g) => g.Acceleration = run => 4.8, again: 0.4);
				BattleSession hs = twiceHost.Session, gs = twiceGuest.Session;
				Expect(hs.State == onceHost.Session.State && gs.State == onceGuest.Session.State,
					$"{format}: with every message twice the battle ends the same way ({hs.State}: {hs.Message} / {gs.State}: {gs.Message})");
				Expect(hs.Winner == onceHost.Session.Winner && gs.Winner == onceGuest.Session.Winner && hs.Winner == gs.Winner,
					$"{format}: the same winner, on both sides ({hs.Winner} / {gs.Winner}, once: {onceHost.Session.Winner})");
				Expect(hs.Runs.Count == onceHost.Session.Runs.Count && gs.Runs.Count == onceGuest.Session.Runs.Count,
					$"{format}: no run is counted twice ({hs.Runs.Count} runs, once: {onceHost.Session.Runs.Count})");
				bool sameTimes = hs.Runs.Zip(onceHost.Session.Runs, (a, b) => Math.Abs(a.Mine.Time - b.Mine.Time) < 0.01 && Math.Abs(a.Theirs.Time - b.Theirs.Time) < 0.01).All(x => x);
				Expect(sameTimes, $"{format}: the same times in every run");
			}
		}

		private static void RaceWithEveryMessageTwice() {
			Course course = RaceCourse();
			var settings = new RaceSettings { Name = "Twice", Open = false, Order = GridOrder.Joined };
			Action<Racer, int> speeds = (r, i) => r.Accel = i == 2 ? 6.0 : 4.0 + 0.2 * i;
			List<Racer> once = RunRoom(course, settings, 5, StartWhenAllIn(5), setUp: speeds);
			List<Racer> twice = RunRoom(course, settings, 5, StartWhenAllIn(5), setUp: speeds, again: 0.4);
			RaceSession host = twice[0].Session;
			Expect(host.Phase == RacePhase.Finished && host.Results?.Count == 5, $"with every message twice a 5-car race still finishes ({host.Phase}: {host.Message})");
			List<ulong> order = once[0].Session.Results?.Select(r => r.Id).ToList() ?? new List<ulong>();
			bool agree = twice.All(r => r.Session.Results != null && r.Session.Results.Select(x => x.Id).SequenceEqual(order));
			Expect(agree, "every driver gets the same results as when each message came once");
			Expect(twice.All(r => r.Session.Drivers.Count == 5), "nobody joins twice");
		}
	}
}
