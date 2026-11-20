using System;

namespace TRC.TuneLoader.CodeCheck {
	/// <summary>Nov 18: runs thrown out quietly for no-clip or a game clock that doesn't keep real time.</summary>
	internal static partial class SelfTest {
		private static void HonestyTests() {
			var h = new RunHonesty();
			h.NoClip("the car's colliders are off");
			Expect(h.Clean, "honesty: one odd check (a reset's frame) doesn't throw a run out");
			h.NoClip("");
			h.NoClip("the car's colliders are off");
			Expect(h.Clean, "honesty: signs must be in a row");
			h.NoClip("the car's colliders are off");
			Expect(!h.Clean && h.ThrownOut.Contains("no-clip"), $"honesty: no-clip for half a second throws the run out ({h.ThrownOut})");
			h.NoClip("");
			Expect(!h.Clean, "honesty: once thrown out, it stays out for the run");
			h.Reset();
			Expect(h.Clean, "honesty: a new run starts clean");

			// Moving without speed (KINO's no-clip): 100 m in 0.25 s at "0 km/h" is a sign, driving at 200 km/h isn't, a reset's one jump isn't twice.
			var m = new RunHonesty();
			m.MovedWithoutSpeed(UnityEngine.Vector3.zero, 0, 0);
			string fly = m.MovedWithoutSpeed(new UnityEngine.Vector3(0, 0, 100), 0.25, 0);
			string drive = m.MovedWithoutSpeed(new UnityEngine.Vector3(0, 0, 113.9f), 0.5, 200);
			m.MovedWithoutSpeed(new UnityEngine.Vector3(0, 0, 500), 0.75, 0);
			string after = m.MovedWithoutSpeed(new UnityEngine.Vector3(0, 0, 500), 1.0, 0);
			Expect(fly.Length > 0 && drive.Length == 0 && after.Length == 0, $"honesty: moving without speed is a sign ({fly}), driving and a reset's jump aren't");

			// Pinned pages saved before the regulations merge still open the same page.
			var pins = PageKeys.Migrate(new[] { "7:0", "7:1", "7:2", "3:2" }, 1);
			Expect(string.Join(";", pins) == "7:0;7:1;3:2", $"pins: old Make or edit goes to Regulations, Shared with you to its new view ({string.Join(";", pins)})");
			Expect(string.Join(";", PageKeys.Migrate(new[] { "7:1" }, PageKeys.Layout)) == "7:1", "pins: keys saved in today's layout are left alone");

			// The clock: real speed passes, 1.5x and 0.5x don't, a freeze doesn't count, the PC's clock being set is ignored.
			DateTime t0 = new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);
			bool Runs(double rate, double freezeAt = -1) {
				var c = new RunHonesty();
				double game = 100;
				for (int i = 0; i <= 120; i++) {
					double step = i == (int)freezeAt ? 3.0 : 0.25;
					game += step * rate;
					c.Clock(game, t0.AddSeconds(i * 0.25 + (freezeAt >= 0 && i >= freezeAt ? 2.75 : 0)));
				}
				return c.Clean;
			}
			Expect(Runs(1.0), "honesty: a clock at real speed counts");
			Expect(Runs(1.0, 40), "honesty: a 3 s freeze isn't a speed hack");
			Expect(!Runs(1.5) && !Runs(0.5), "honesty: a game clock at 1.5x or 0.5x throws the run out");
			var set = new RunHonesty();
			set.Clock(100, t0);
			set.Clock(101, t0.AddHours(-1));
			set.Clock(106, t0.AddHours(-1).AddSeconds(5));
			Expect(set.Clean, "honesty: the PC's clock being set back starts the comparison again");
		}
	}
}
