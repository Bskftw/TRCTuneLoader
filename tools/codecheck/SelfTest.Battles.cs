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

			/// <summary>Metres added to where the car would be (a reset or a jump ahead), from the frame it's set.</summary>
			public float Skip;
			public double Clock(double t) => t + ClockOrigin;
		}

		private sealed class Wire {
			public double DeliverAt;
			public byte[] Data;
			public Player To;
		}

		private static void BattleTests() {
			BattleMessageRoundTrip();
			ChallengeWithALongCourse();
			PiecesFillInAcrossResends();
			ChallengeUntilSeen();
			CrossedChallengesBecomeOneBattle();
			RaceIsWonByTheFasterDriver();
			CatAndMouseRules();
			CatAndMouseBattle();
			LeaderNeedsLessEachRound();
			GapIsMeasuredAtTheSameMoment();
			OpponentOutIsToldAtOnce();
			RaceSpotsAndChaserBox();
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

			var seen = new BattleMessage { Kind = BattleKind.Seen, BattleId = 7, From = GuestId, To = HostId, Reason = "you're not on the course's map" };
			BattleMessage seenBack = BattleMessage.Decode(seen.Encode());
			Expect(seenBack != null && seenBack.Kind == BattleKind.Seen && !seenBack.Accept && seenBack.Reason == seen.Reason, "a Seen comes through the encoding unchanged");

			string colourful = "<color=#FFBF00>[</color><color=#FFBF00>P</color><color=#FFBF00>G</color><color=#FFBF00>]</color> <color=#9E00FF>a</color><color=#9E00FF>pok</color>";
			Expect(BattleMessage.PlainName(colourful) == "[PG] apok", $"a name coloured letter by letter comes out plain ({BattleMessage.PlainName(colourful)})");
			Expect(BattleMessage.PlainName("<<KING>> 3 < 4") == "<<KING>> 3 < 4", "other angle brackets in a name stay");
		}

		private static void ChallengeWithALongCourse() {
			// Like Akagi Downhill on Project Dream: 5.8 km of bends, 400 m down, far from the map's middle.
			var points = new List<Vector3>();
			for (int i = 0; i <= 2900; i++) {
				float s = i * 2f;
				points.Add(new Vector3(2825f - s * 0.37f + 40f * Mathf.Sin(s / 60f), 470.66f - s * 0.071f, 8085f - s * 0.12f + 35f * Mathf.Cos(s / 45f)));
			}
			Course course = MakeCourse(points);
			var challenge = new BattleMessage {
				Kind = BattleKind.Challenge, BattleId = 9, From = HostId, To = GuestId, FromName = "Host", Course = course, CourseCode = course.Code,
			};
			byte[] bytes = challenge.Encode();
			BattleMessage back = BattleMessage.Decode(bytes);
			Expect(back?.Course != null && back.Course.Code == course.Code && back.Course.Path.Count == course.Path.Count,
				$"a {TimeAttackRun.Distance(course.Length)} course with {course.Path.Count} points comes through with the same code");
			bool exact = back?.Course != null && back.Course.Path.Count == course.Path.Count &&
				course.Path.Select((p, i) => p.x == back.Course.Path[i].x && p.y == back.Course.Path[i].y && p.z == back.Course.Path[i].z).All(same => same);
			Expect(exact, "every point of the path arrives exactly as it was");
			Expect(bytes.Length < course.Path.Count * 4, $"the path goes in about a quarter of the bytes plain numbers took ({bytes.Length} bytes, was about {course.Path.Count * 12}, {BattleChunks.Split(bytes).Count} pieces)");

			// A step too long for centimetre steps (over 327 m): the path goes as plain numbers.
			var jump = new Course {
				Name = "Jump", MapKey = "test:1", Source = CourseSource.Custom, HasPath = true,
				Path = new List<Vector3> { new Vector3(10.5f, 2f, 0f), new Vector3(10.5f, 2f, 400f), new Vector3(10.5f, 2f, 800f) },
			};
			CourseGate.TryMake(jump.Path[0], Vector3.forward, 12f, out jump.Start);
			CourseGate.TryMake(jump.Path[2], Vector3.forward, 12f, out jump.Finish);
			jump.Build(resample: false);
			BattleMessage jumpBack = BattleMessage.Decode(new BattleMessage {
				Kind = BattleKind.Challenge, BattleId = 10, From = HostId, To = GuestId, Course = jump, CourseCode = jump.Code,
			}.Encode());
			Expect(jumpBack?.Course != null && jumpBack.Course.Code == jump.Code, "a path with a 400 m step still comes through with the same code");
		}

		private static void PiecesFillInAcrossResends() {
			var data = new byte[35000];
			new System.Random(5).NextBytes(data);
			List<BattleChunks.Piece> pieces = BattleChunks.Split(data);
			Expect(pieces.Count == 70 && pieces.All(p => p.Data.Length <= BattleChunks.ChunkBytes), $"a 35 KB message goes in 70 pieces of up to 500 bytes ({pieces.Count})");
			Expect(BattleChunks.Split(data)[0].MessageId == pieces[0].MessageId, "the same message gets the same ID every time it's sent");

			// Three in ten pieces lost every time: one send would never arrive whole (0.7^70), so the copies have to fill each other's gaps.
			var chunks = new BattleChunks();
			var random = new System.Random(11);
			var now = new DateTime(2026, 10, 23, 18, 0, 0, DateTimeKind.Utc);
			byte[] whole = null;
			int sends = 0;
			while (whole == null && sends < 20) {
				sends++;
				foreach (BattleChunks.Piece piece in pieces) {
					if (random.NextDouble() >= 0.3) {
						whole = chunks.Add(HostId, piece, now) ?? whole;
					}
				}
				now = now.AddSeconds(3);
			}
			Expect(whole != null && whole.SequenceEqual(data) && sends <= 8, $"with 30% of pieces lost, sending the same message again fills the gaps ({sends} sends)");

			var small = new byte[] { 1, 2, 3 };
			Expect(chunks.Add(HostId, BattleChunks.Split(small)[0], now)?.SequenceEqual(small) == true, "a message that fits in one piece comes straight through");
			Expect(!BattleChunks.Valid(3, 3, 10) && !BattleChunks.Valid(0, 0, 10) && !BattleChunks.Valid(0, 2, BattleChunks.ChunkBytes + 1) && BattleChunks.Valid(1, 2, 500),
				"pieces with impossible numbers are ignored");
		}

		private static void ChallengeUntilSeen() {
			Course course = StraightCourse();
			const double t = 1000;
			var host = BattleSession.Challenge(21, HostId, "Host", GuestId, "<color=#FFBF00>G</color><color=#FFBF00>uest</color>", course, BattleFormat.Race, true, () => new BattleCar(), t);
			int Challenges() => host.TakeOutbox().Count(m => m.Kind == BattleKind.Challenge);
			void Tick(double at) => host.Tick(at, Vector3.zero, Vector3.forward, 0f, 1f, null);

			Expect(host.PeerName == "Guest", $"the opponent's name is kept plain ({host.PeerName})");
			Expect(Challenges() == 1, "a challenge goes out straight away");
			Tick(t + 3.1);
			Expect(Challenges() == 1, "it goes again every few seconds while the other mod hasn't confirmed it");
			Tick(t + 7);
			Expect(host.Message.Contains("Still sending your course"), $"after a few seconds without a Seen, the challenger is told, and that a course takes longer ({host.Message})");
			host.TakeOutbox();

			host.Receive(new BattleMessage { Kind = BattleKind.Seen, BattleId = 21, From = GuestId, To = HostId, Accept = true }, t + 7.2);
			Expect(host.PeerSeen && host.Message.Contains("got your challenge"), $"a Seen tells the challenger it got there ({host.Message})");
			Tick(t + 10.5);
			Tick(t + 14);
			Expect(Challenges() == 0, "once the other mod has it, the challenge isn't sent again");
			host.Receive(new BattleMessage { Kind = BattleKind.Seen, BattleId = 21, From = GuestId, To = HostId, Reason = "you're not on the course's map" }, t + 15);
			Expect(host.Message.Contains("can't accept it: you're not on the course's map"), $"the challenger sees why the other driver can't accept ({host.Message})");
			Tick(t + 46);
			Expect(host.Active, "a challenge carrying its course gets more than 45 s to be answered");
			Tick(t + 91);
			Expect(host.State == BattleSession.Phase.Ended && host.Message.Contains("didn't answer"), $"a challenge that got there but wasn't answered runs out ({host.Message})");

			var lost = BattleSession.Challenge(22, HostId, "Host", GuestId, "Guest", course, BattleFormat.Race, true, () => new BattleCar(), t);
			lost.Tick(t + 91, Vector3.zero, Vector3.forward, 0f, 1f, null);
			Expect(lost.State == BattleSession.Phase.Ended && lost.Message.Contains("never got to"), $"a challenge that never got there says so ({lost.Message})");
			Expect(BattleMessage.PlainName("<color=#00FF00>C</color><color=#11FF11>r</color><color=#2") == "Cr", "a gradient name cut off in the middle of a tag comes out plain");
			Expect(BattleMessage.PlainName("<#00FF00>Crazy Frog") == "Crazy Frog", "short colour tags go too");
			Expect(BattleMessage.PlainName("[00D928]Crazy Frog") == "Crazy Frog" && BattleMessage.PlainName("[FF0000]C[-][00FF00]r[-][00FF") == "Cr",
				"KINO's [RRGGBB] name colours go, cut off or not");
			Expect(BattleMessage.PlainName("[TRC] Apok") == "[TRC] Apok", "a clan tag in brackets stays");
			Expect(RoomBoard.IsNewer("0.15.1", "0.15.0") && !RoomBoard.IsNewer("0.14.2", "0.15.0") && !RoomBoard.IsNewer("", "0.15.0"), "a newer version in the room is noticed");
		}

		private static void CrossedChallengesBecomeOneBattle() {
			// Each driver recorded their own path for the same map course: the same lines, different paths and codes.
			Course lowCourse = MakeCourse(new List<Vector3> { new Vector3(0, 0, 0), new Vector3(0, 0, 250), new Vector3(0, 0, 500) });
			Course highCourse = MakeCourse(new List<Vector3> { new Vector3(0, 0, 0), new Vector3(3, 0, 250), new Vector3(0, 0, 500) });
			Expect(lowCourse.Code != highCourse.Code, "two recordings of one course have different codes");

			const double t = 50;
			BattleCar Car() => new BattleCar { Spec = true, TuneSet = "TRC Revival" };
			BattleMessage Sent(BattleSession s) => BattleMessage.Decode(s.TakeOutbox().Single(m => m.Kind == BattleKind.Challenge).Encode());
			var low = BattleSession.Challenge(31, HostId, "Low", GuestId, "High", lowCourse, BattleFormat.Race, false, Car, t);
			var high = BattleSession.Challenge(32, GuestId, "High", HostId, "Low", highCourse, BattleFormat.Race, false, Car, t);
			BattleMessage fromLow = Sent(low), fromHigh = Sent(high);
			Expect(low.Crosses(fromHigh) && high.Crosses(fromLow), "two drivers challenging each other to the same battle see it's the same battle");
			Expect(high.YieldsTo(fromLow) && !low.YieldsTo(fromHigh), "exactly one of them gives way: the higher Steam ID accepts the lower one's challenge");

			var otherFormat = BattleSession.Challenge(33, GuestId, "High", HostId, "Low", highCourse, BattleFormat.CatAndMouse, false, Car, t);
			Expect(!low.Crosses(Sent(otherFormat)), "a challenge with another format isn't the same battle (both get shown instead)");

			// The one that gives way takes its own back and accepts: both line up on the lower Steam ID's course.
			high.Leave(t + 0.1);
			var accepted = BattleSession.FromChallenge(fromLow, GuestId, "High", fromLow.Course, Car, t + 0.1);
			accepted.Accept(t + 0.1);
			foreach (BattleMessage message in accepted.TakeOutbox()) {
				low.Receive(BattleMessage.Decode(message.Encode()), t + 0.2);
			}
			Expect(low.State == BattleSession.Phase.Gridding && accepted.State == BattleSession.Phase.Gridding && accepted.Course.Code == lowCourse.Code,
				$"crossed challenges become one battle ({low.State} / {accepted.State})");
		}

		/// <summary>
		/// Plays a battle to the end (or <paramref name="seconds"/>): the host challenges, the guest accepts, both
		/// drive their own car from their own GO. Returns both players.
		/// </summary>
		private static (Player host, Player guest) PlayBattle(Course course, BattleFormat format, uint id, bool open,
			Action<Player, Player> setUp = null, double seconds = 400, double hostToGuest = 0.06, double guestToHost = 0.09, Func<double, bool> networkUp = null,
			Action<double, Player, Player> onFrame = null) {
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
						p.Position = s.MySpot() + forward * (float)(0.5 * a * since * since + p.Skip);
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

				onFrame?.Invoke(t, host, guest);
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

		private static void LeaderNeedsLessEachRound() {
			Expect(BattleRules.LeaderGapFor(0) == 1.5 && BattleRules.LeaderGapFor(1) == 1.5 && BattleRules.LeaderGapFor(2) == 1.0 &&
				BattleRules.LeaderGapFor(3) == 1.0 && BattleRules.LeaderGapFor(4) == 0.5 && BattleRules.LeaderGapFor(9) == 0.5,
				"cat and mouse: the leader needs 1.5 s while both lead the first time, then 1 s, then 0.5 s");
			RunResult Finish(double time) => new RunResult { Finished = true, Time = time };
			BattleRun first = BattleRules.Decide(BattleFormat.CatAndMouse, 0, leader: 1, 1, 2, "Rival", Finish(60.0), Finish(61.2));
			BattleRun third = BattleRules.Decide(BattleFormat.CatAndMouse, 2, leader: 1, 1, 2, "Rival", Finish(60.0), Finish(61.2));
			BattleRun fifth = BattleRules.Decide(BattleFormat.CatAndMouse, 4, leader: 1, 1, 2, "Rival", Finish(60.0), Finish(60.6));
			Expect(first.Winner == 0 && third.Winner == 1 && fifth.Winner == 1, "a 1.2 s lead wins from the third run on, 0.6 s from the fifth");
		}

		private static void GapIsMeasuredAtTheSameMoment() {
			// Same car, side by side the whole way: the true gap is 0 m. Heartbeats come 5 times a second and late.
			Course course = StraightCourse();
			float worstNew = 0f, worstOld = 0f;
			PlayBattle(course, BattleFormat.Race, 12, open: true, seconds: 25, onFrame: (t, h, g) => {
				BattleSession s = h.Session;
				if (s?.State == BattleSession.Phase.Running && s.Run.Active && s.Run.Progress > 30f && g.Session?.Run?.Active == true) {
					worstNew = Math.Max(worstNew, Math.Abs(s.Run.Progress - s.PeerProgressAt(h.Clock(t))));
					worstOld = Math.Max(worstOld, Math.Abs(s.Run.Progress - s.PeerProgress));
				}
			});
			Expect(worstNew < 1.5f && worstOld > 3f, $"the gap compares both cars at the same moment (off by {worstNew:0.0} m at most, was {worstOld:0.0} m)");
		}

		private static void OpponentOutIsToldAtOnce() {
			// The guest's car is moved 200 m further along at 150 m (a teleport: since 0.15.1 a reset alone doesn't stop a battle run): its run stops. The host hears it at once and wins by finishing.
			Course course = StraightCourse();
			string toldWhileRunning = "";
			var (host, guest) = PlayBattle(course, BattleFormat.Race, 14, open: true, onFrame: (t, h, g) => {
				if (g.Session?.Run?.State == TimeAttackRun.Phase.Running && g.Session.Run.Progress > 150f && g.Skip == 0f) {
					g.Skip = 200f;
				}
				if (h.Session?.State == BattleSession.Phase.Running && h.Session.PeerOut.Length > 0 && toldWhileRunning.Length == 0) {
					toldWhileRunning = h.Session.PeerOut;
				}
			});
			Expect(toldWhileRunning.Contains("finish to win"), $"a driver hears straight away when the other driver's run stops ({toldWhileRunning})");
			Expect(host.Session.Winner == HostId && guest.Session.Winner == HostId, $"and wins by finishing ({host.Session.Message})");
		}

		private static void RaceSpotsAndChaserBox() {
			Course course = StraightCourse();
			var host = BattleSession.Challenge(15, HostId, "Host", GuestId, "Guest", course, BattleFormat.Race, true, () => new BattleCar(), 0);
			var guest = BattleSession.FromChallenge(BattleMessage.Decode(host.TakeOutbox()[0].Encode()), GuestId, "Guest", course, () => new BattleCar(), 0);
			float apart = Vector3.Distance(host.MySpot(), guest.MySpot());
			Expect(Math.Abs(apart - 4f) < 0.01f, $"race spots on a 12 m line are 4 m apart, 1 m closer each than before ({apart:0.00} m)");

			guest.Accept(0);
			Expect(guest.Run.GridFront == 0f || guest.Run.GridFront == 5f, "the guest's box is set when it accepts");
			var chase = BattleSession.Challenge(16, HostId, "Host", GuestId, "Guest", course, BattleFormat.CatAndMouse, true, () => new BattleCar(), 0);
			var mouse = BattleSession.FromChallenge(BattleMessage.Decode(chase.TakeOutbox()[0].Encode()), GuestId, "Guest", course, () => new BattleCar(), 0);
			mouse.Accept(0);
			BattleSession chaser = mouse.IAmLeader ? null : mouse;
			if (chaser == null) {
				foreach (BattleMessage m in mouse.TakeOutbox()) {
					chase.Receive(BattleMessage.Decode(m.Encode()), 0.1);
				}
				chaser = chase;
			}
			Expect(chaser.Run.GridFront == 5f && Vector3.Distance(chaser.MySpot(), course.Start.Center) == 10f,
				$"the chaser lines up from 5 m back, with its spot 10 m back ({chaser.Run.GridFront} m, spot {Vector3.Distance(chaser.MySpot(), course.Start.Center):0.0} m)");
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
