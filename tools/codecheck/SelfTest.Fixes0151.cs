using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader.CodeCheck {
	/// <summary>0.15.1: races and battles keep going after a reset, speed traps counted live, and speed trap battles.</summary>
	internal static partial class SelfTest {
		private static void Fixes0151Tests() {
			RacesKeepGoing();
			TrapBattleRules();
			TrapBattleMessages();
		}

		/// <summary>A race run on a straight 500 m course with two traps: <paramref name="drive"/> moves the car each frame (z from z).</summary>
		private static TimeAttackRun RaceRun(Course course, bool keepGoing, Func<float, Vector3> drive) {
			var run = new TimeAttackRun(course) { AllowResets = true, ExternalStart = true, KeepGoing = keepGoing };
			double now = 0;
			for (; now < 3; now += Frame) {
				run.Tick(now, new Vector3(0, 0, -4), Vector3.forward, 0f, 1f);
			}
			run.StartCountdown(now + 3.5);
			float z = -4f;
			for (; now < 90 && run.Active; now += Frame) {
				if (run.State == TimeAttackRun.Phase.Running) {
					z += 25f * (float)Frame;
				}
				run.Tick(now, drive(z), Vector3.forward, run.State == TimeAttackRun.Phase.Running ? 90f : 0f, 1f);
			}
			return run;
		}

		/// <summary>
		/// bskftw, Oct 30: "races should not be canceled for anyone if they reset the car, at all." A reset ahead, and
		/// a few seconds off the course, now leave the run going to the finish, and the traps after it still count.
		/// </summary>
		private static void RacesKeepGoing() {
			Course course = MakeCourse(new List<Vector3> { new Vector3(0, 0, 0), new Vector3(0, 0, 500) });
			CourseGate.TryMake(new Vector3(0, 0, 100), Vector3.forward, 10f, out CourseGate early);
			CourseGate.TryMake(new Vector3(0, 0, 400), Vector3.forward, 10f, out CourseGate late);
			course = course.WithTraps(new[] { early, late });

			// A reset 60 m ahead at 200 m (stopped a battle before 0.15.1).
			bool jumped = false;
			Vector3 Reset(float z) {
				if (z > 200f && !jumped) {
					jumped = true;
				}
				return new Vector3(0, 0, jumped ? z + 60f : z);
			}
			TimeAttackRun reset = RaceRun(course, true, Reset);
			Expect(reset.State == TimeAttackRun.Phase.Finished && reset.Resets == 1 && reset.TrapSpeeds.Count == 2,
				$"in a race, a reset doesn't stop the run and the traps after it still count ({reset.State}: {reset.Message}, {reset.TrapSpeeds.Count} traps)");
			jumped = false;
			TimeAttackRun old = RaceRun(course, false, Reset);
			Expect(old.State == TimeAttackRun.Phase.Cancelled, $"without KeepGoing the same reset still stops a run ({old.State})");

			// 30 m to the side for 4 s (past the 2 s limit), then back.
			Vector3 Wide(float z) => new Vector3(z > 150f && z < 250f ? 30f : 0f, 0, z);
			TimeAttackRun wide = RaceRun(course, true, Wide);
			Expect(wide.State == TimeAttackRun.Phase.Finished && !wide.LapValid && wide.TrapSpeeds.Count == 2,
				$"in a race, leaving the course for a while doesn't stop the run, it only spoils the lap ({wide.State}: {wide.Message})");
			Expect(Math.Abs(wide.TrapTotal - 180f) < 1f, $"the trap total adds up live: 90 + 90 km/h ({wide.TrapTotal:0.0})");

			// Far off the course and back much further along: still a shortcut.
			Vector3 Cut(float z) => z > 100f && z < 140f ? new Vector3(200f, 0, z) : new Vector3(0, 0, z < 140f ? z : z + 250f);
			TimeAttackRun cut = RaceRun(course, true, Cut);
			Expect(cut.State == TimeAttackRun.Phase.Cancelled && cut.Message.Contains("further along"),
				$"in a race, a jump far further along the course (a teleport) still stops the run ({cut.State}: {cut.Message})");
		}

		private static void TrapBattleRules() {
			RunResult Done(float traps, double time = 60) => new RunResult { Finished = true, Time = time, TrapTotal = traps };
			BattleRun win = BattleRules.Decide(BattleFormat.SpeedTraps, 0, 1, 1, 2, "Frog", Done(900, 70), Done(850, 60));
			Expect(win.Winner == 1, $"a speed trap battle goes to the higher trap total, not the faster time ({win.Text})");
			BattleRun heat = BattleRules.Decide(BattleFormat.SpeedTraps, 0, 1, 1, 2, "Frog", Done(900.2f), Done(900f));
			Expect(heat.Winner == 0, $"trap totals under 0.5 km/h apart are run again ({heat.Text})");
			BattleRun dnf = BattleRules.Decide(BattleFormat.SpeedTraps, 0, 1, 1, 2, "Frog", new RunResult { Reason = "Shortcut" }, Done(100));
			Expect(dnf.Winner == 2, $"you have to finish for your traps to count ({dnf.Text})");
		}

		/// <summary>The challenge carries its traps as protocol 6 (0.15.0 skips it); heartbeats carry the totals where 0.15.0 stops reading.</summary>
		private static void TrapBattleMessages() {
			Course course = MakeCourse(new List<Vector3> { new Vector3(0, 0, 0), new Vector3(0, 0, 500) });
			CourseGate.TryMake(new Vector3(0, 0, 100), Vector3.forward, 8f, out CourseGate trap);
			var challenge = new BattleMessage { Kind = BattleKind.Challenge, BattleId = 5, From = 1, To = 2, FromName = "bskftw", Format = BattleFormat.SpeedTraps, CourseCode = course.Code, Course = course };
			challenge.Traps.Add(trap);
			byte[] bytes = challenge.Encode();
			BattleMessage back = BattleMessage.Decode(bytes);
			Expect(bytes[0] == BattleMessage.TrapProtocol && back != null && back.Format == BattleFormat.SpeedTraps && back.Traps.Count == 1 &&
				Vector3.Distance(back.Traps[0].Center, trap.Center) < 0.01f, "a speed trap challenge carries its traps, as protocol 6 so 0.15.0 ignores it");

			var race = new BattleMessage { Kind = BattleKind.Challenge, BattleId = 6, From = 1, To = 2, FromName = "bskftw", Format = BattleFormat.Race, CourseCode = course.Code };
			Expect(race.Encode()[0] == BattleMessage.Protocol, "other challenges stay protocol 5, so 0.15.0 still battles 0.15.1");

			var beat = new BattleMessage { Kind = BattleKind.Heartbeat, BattleId = 5, From = 1, To = 2, TrapTotal = 321f, Result = new RunResult { Finished = true, Time = 50, TrapTotal = 456f } };
			byte[] beatBytes = beat.Encode();
			BattleMessage heard = BattleMessage.Decode(beatBytes);
			Expect(heard != null && Math.Abs(heard.TrapTotal - 321f) < 0.01f && Math.Abs(heard.Result.TrapTotal - 456f) < 0.01f, "a heartbeat carries the live and final trap totals");
			BattleMessage older = BattleMessage.Decode(beatBytes.Take(beatBytes.Length - 8).ToArray());
			Expect(older != null && older.TrapTotal == 0f && older.Result != null && older.Result.Finished, "a 0.15.0 heartbeat, without the totals, still reads");
		}
	}
}
