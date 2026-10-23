using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader.CodeCheck {
	/// <summary>
	/// Battles between two simulated players: each has its own clock and its own car, and messages go through
	/// a fake network with a different delay each way (and through the real encoding), like two PCs in a room.
	/// </summary>
	internal static partial class SelfTest {
		private const ulong HostId = 76561190000000001;
		private const ulong GuestId = 76561190000000002;

		private sealed class Player {
			public string Name;
			public ulong Id;
			public double ClockOrigin;
			public BattleSession Session;
			public Vector3 Position;
			public double Go = double.NaN;
			public int RunSeen = -1;
			public BattleCar Car = new BattleCar { Spec = true, TuneSet = "TRC Revival", TuneCode = "TRC-0000-0000", Car = "Test car" };

			/// <summary>Acceleration (m/s²) for each run, so a test can make either driver faster.</summary>
			public Func<int, double> Acceleration = run => 5;

			/// <summary>Moves 1 m forward once during this countdown (a jump start), when set.</summary>
			public int JumpInCountdown = -1;
			public bool Jumped;
			public double Clock(double t) => t + ClockOrigin;
		}

		private sealed class Wire {
			public double DeliverAt;
			public byte[] Data;
			public Player To;
		}

		private static void BattleTests() {
			BattleMessageRoundTrip();
			RaceIsWonByTheFasterDriver();
			CatAndMouseRules();
			CatAndMouseBattle();
			JumpStartCallsOffTheCountdown();
			DifferentSetsDontStart();
			SilentOpponentHasLeft();
		}

		private static Course StraightCourse() => MakeCourse(new List<Vector3> { new Vector3(0, 0, 0), new Vector3(0, 0, 500) });

		private static void BattleMessageRoundTrip() {
			Course course = MakeCourse(Hairpin(300, 5, 1.3f));
			var challenge = new BattleMessage {
				Kind = BattleKind.Challenge, BattleId = 7, From = HostId, To = GuestId, FromName = "Host, \"the\" driver",
				Format = BattleFormat.CatAndMouse, Course = course, CourseCode = course.Code,
			};
			BattleMessage back = BattleMessage.Decode(challenge.Encode());
			Expect(back != null && back.Course != null && back.Course.Code == course.Code && back.FromName == challenge.FromName && back.Format == BattleFormat.CatAndMouse,
				"a challenge with its course comes through the encoding unchanged");

			var beat = new BattleMessage {
				Kind = BattleKind.Heartbeat, BattleId = 7, From = GuestId, To = HostId, SentAt = 123.25, Echo = 99.5, EchoHold = 0.01, Ready = true,
				TuneSet = "TRC V3.0", Progress = 812.5f, RunIndex = 2, Phase = 4, Result = new RunResult { Run = 1, Finished = true, Time = 181.234, SpecAtEnd = true },
			};
			BattleMessage beatBack = BattleMessage.Decode(beat.Encode());
			Expect(beatBack != null && beatBack.Result != null && beatBack.Result.Time == 181.234 && beatBack.RunIndex == 2 && beatBack.TuneSet == "TRC V3.0" && beatBack.Phase == 4,
				"a heartbeat comes through the encoding unchanged");
			Expect(BattleMessage.Decode(new byte[] { 9, 1, 2 }) == null, "bytes from another protocol version are ignored");
		}

		/// <summary>
		/// Plays a battle to the end (or <paramref name="seconds"/>): the host challenges, the guest accepts, both
		/// drive their own car from their own GO. Returns both players.
		/// </summary>
		private static (Player host, Player guest) PlayBattle(Course course, BattleFormat format, uint id, bool open,
			Action<Player, Player> setUp = null, double seconds = 400, double hostToGuest = 0.06, double guestToHost = 0.09, Func<double, bool> networkUp = null) {
			var host = new Player { Name = "Host", Id = HostId, ClockOrigin = 100 };
			var guest = new Player { Name = "Guest", Id = GuestId, ClockOrigin = 5000 };
			setUp?.Invoke(host, guest);
			var wires = new List<Wire>();

			host.Session = BattleSession.Challenge(id, HostId, "Host", GuestId, "Guest", course, format, open, () => host.Car, host.Clock(0));
			host.Position = course.Start.Center - course.Start.Forward * 200f;
			guest.Position = host.Position;

			for (double t = 0; t < seconds; t += Frame) {
				// Deliver what has arrived.
				foreach (Wire wire in wires.Where(w => w.DeliverAt <= t).ToList()) {
					wires.Remove(wire);
					BattleMessage message = BattleMessage.Decode(wire.Data);
					Player to = wire.To;
					if (message.Kind == BattleKind.Challenge && to.Session == null) {
						to.Session = BattleSession.FromChallenge(message, to.Id, to.Name, message.Course ?? course, () => to.Car, to.Clock(t));
						to.Session.Accept(to.Clock(t));
					} else {
						to.Session?.Receive(message, to.Clock(t));
					}
				}

				foreach (Player p in new[] { host, guest }) {
					BattleSession s = p.Session;
					if (s == null) {
						continue;
					}
					double now = p.Clock(t);

					// Lined up on their spot at the start of each run (as if they teleported there).
					if (s.State == BattleSession.Phase.Gridding && s.RunIndex != p.RunSeen) {
						p.RunSeen = s.RunIndex;
						p.Position = s.MySpot();
						p.Go = double.NaN;
					}
					float speed = 0f;
					Vector3 forward = course.Start.Forward;
					if (s.State == BattleSession.Phase.Countdown && s.Run.State == TimeAttackRun.Phase.Countdown && p.JumpInCountdown == s.RunIndex && !p.Jumped) {
						p.Jumped = true;
						p.Position += forward;
						speed = 10f;
					} else if (s.Run != null && s.Run.State == TimeAttackRun.Phase.Running) {
						if (double.IsNaN(p.Go)) {
							p.Go = s.Run.GoTime;
						}
						double since = now - p.Go;
						double a = p.Acceleration(s.RunIndex);
						p.Position = s.MySpot() + forward * (float)(0.5 * a * since * since);
						speed = (float)(a * since * 3.6);
					}

					s.Tick(now, p.Position, forward, speed, 1f, null);
					foreach (BattleMessage message in s.TakeOutbox()) {
						Player other = p == host ? guest : host;
						if (networkUp == null || networkUp(t)) {
							wires.Add(new Wire { DeliverAt = t + (p == host ? hostToGuest : guestToHost), Data = message.Encode(), To = other });
						}
					}
				}

				if (host.Session != null && guest.Session != null && !host.Session.Active && !guest.Session.Active) {
					break;
				}
			}
			return (host, guest);
		}

		private static void RaceIsWonByTheFasterDriver() {
			Course course = StraightCourse();
			var (host, guest) = PlayBattle(course, BattleFormat.Race, 4, open: false, setUp: (h, g) => g.Acceleration = run => 4.8);
			BattleSession hs = host.Session, gs = guest.Session;
			Expect(hs.State == BattleSession.Phase.Finished && gs.State == BattleSession.Phase.Finished, $"a race battle finishes on both sides ({hs.State}: {hs.Message} / {gs.State}: {gs.Message})");
			Expect(hs.Winner == HostId && gs.Winner == HostId, $"both sides agree the faster driver won ({hs.Message} / {gs.Message})");

			// Each from 4 m behind the line to 500 m past it: 504 m at 5 and 4.8 m/s².
			double hostTime = Math.Sqrt(2 * 504 / 5.0), guestTime = Math.Sqrt(2 * 504 / 4.8);
			BattleRun run = hs.Runs.LastOrDefault();
			Expect(run != null && Math.Abs(run.Mine.Time - hostTime) < 0.01 && Math.Abs(run.Theirs.Time - guestTime) < 0.01,
				$"both race times are right ({run?.Mine.Time:0.000} / {run?.Theirs.Time:0.000}, expected {hostTime:0.000} / {guestTime:0.000})");
			Expect(hs.Counted && gs.Counted, "a TRC spec battle between two spec cars counts");

			// The two GOs, on one shared clock: the guest's offset estimate should put them within the network's unevenness.
			double hostGo = host.Go - host.ClockOrigin, guestGo = guest.Go - guest.ClockOrigin;
			Expect(Math.Abs(hostGo - guestGo) < 0.03, $"GO lands at the same moment on both screens ({Math.Abs(hostGo - guestGo) * 1000:0} ms apart)");
		}

		private static void CatAndMouseRules() {
			RunResult Finish(double time) => new RunResult { Finished = true, Time = time };
			RunResult Dnf(string why) => new RunResult { Reason = why };
			const ulong me = 1, peer = 2;

			BattleRun passed = BattleRules.Decide(BattleFormat.CatAndMouse, 0, leader: peer, me, peer, "Rival", Finish(60.0), Finish(60.4));
			Expect(passed.Winner == me, "cat and mouse: the chaser who finishes first wins");
			BattleRun away = BattleRules.Decide(BattleFormat.CatAndMouse, 0, leader: me, me, peer, "Rival", Finish(60.0), Finish(61.6));
			Expect(away.Winner == me, "cat and mouse: the leader 1.5 s or more ahead wins");
			BattleRun close = BattleRules.Decide(BattleFormat.CatAndMouse, 0, leader: me, me, peer, "Rival", Finish(60.0), Finish(60.9));
			Expect(close.Winner == 0, "cat and mouse: under 1.5 s with no pass has no winner");
			BattleRun tooClose = BattleRules.Decide(BattleFormat.CatAndMouse, 0, leader: me, me, peer, "Rival", Finish(60.0), Finish(59.98));
			Expect(tooClose.Winner == 0, "cat and mouse: a finish too close to call has no winner");
			BattleRun crashed = BattleRules.Decide(BattleFormat.CatAndMouse, 0, leader: me, me, peer, "Rival", Dnf("Shortcut"), Finish(65.0));
			Expect(crashed.Winner == peer, "cat and mouse: a leader who doesn't finish loses the run");

			BattleRun deadHeat = BattleRules.Decide(BattleFormat.Race, 0, leader: me, me, peer, "Rival", Finish(60.0), Finish(60.05));
			Expect(deadHeat.Winner == 0, "race: under 0.1 s is a dead heat");
			BattleRun raceWin = BattleRules.Decide(BattleFormat.Race, 0, leader: me, me, peer, "Rival", Finish(61.0), Finish(60.5));
			Expect(raceWin.Winner == peer, "race: the faster time wins");
		}

		private static void CatAndMouseBattle() {
			// Battle 2 is even: the host leads run 1. Same speed, so the chaser (16 m back) can't pass and isn't 1.5 s
			// behind: no winner, swap. In run 2 the guest leads and the host (now chasing) is slow: the guest wins.
			Course course = StraightCourse();
			var (host, guest) = PlayBattle(course, BattleFormat.CatAndMouse, 2, open: true,
				setUp: (h, g) => h.Acceleration = run => run == 0 ? 5 : 3);
			BattleSession hs = host.Session, gs = guest.Session;
			Expect(hs.Runs.Count == 2 && gs.Runs.Count == 2, $"cat and mouse goes to a second run after a run with no winner ({hs.Runs.Count} / {gs.Runs.Count} runs: {hs.Message})");
			Expect(hs.Runs.Count == 2 && hs.Runs[0].Leader == HostId && hs.Runs[1].Leader == GuestId, "cat and mouse swaps who leads");
			Expect(hs.Winner == GuestId && gs.Winner == GuestId, $"both sides agree who won the cat and mouse ({hs.Message} / {gs.Message})");
			Expect(!hs.Counted, "an Open battle doesn't count");
		}

		private static void JumpStartCallsOffTheCountdown() {
			Course course = StraightCourse();
			var (host, guest) = PlayBattle(course, BattleFormat.Race, 6, open: true, setUp: (h, g) => {
				g.JumpInCountdown = 0;
				g.Acceleration = run => 4.8;
			});
			Expect(guest.Jumped, "the guest jumped the start");
			Expect(host.Session.State == BattleSession.Phase.Finished && host.Session.Runs.Count == 1,
				$"after a jump start both line up again and the run still happens ({host.Session.State}: {host.Session.Message})");
			Expect(host.Session.Run.JumpStarts == 0 && guest.Session.Runs.Count == 1, "the jump start didn't count as a run");
		}

		private static void DifferentSetsDontStart() {
			Course course = StraightCourse();
			var (host, guest) = PlayBattle(course, BattleFormat.Race, 8, open: false, seconds: 30,
				setUp: (h, g) => g.Car = new BattleCar { Spec = true, TuneSet = "TRC V3.0" });
			Expect(host.Session.State == BattleSession.Phase.Gridding && host.Session.Message.Contains("Different tune sets"),
				$"a TRC spec battle doesn't start with two different tune sets ({host.Session.State}: {host.Session.Message})");
		}

		private static void SilentOpponentHasLeft() {
			Course course = StraightCourse();
			var (host, _) = PlayBattle(course, BattleFormat.Race, 10, open: true, seconds: 60, networkUp: t => t < 8);
			Expect(host.Session.State == BattleSession.Phase.Ended && host.Session.PeerLeft,
				$"an opponent who stops answering has left the battle ({host.Session.State}: {host.Session.Message})");
		}
	}
}
