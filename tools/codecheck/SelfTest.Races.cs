using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader.CodeCheck {
	/// <summary>
	/// Open races and tournaments between simulated players: each has its own clock, its own car and its own delay,
	/// and every message goes through the real encoding, like PCs in a room.
	/// </summary>
	internal static partial class SelfTest {
		private const ulong RaceHost = 76561190000000100;

		private sealed class Racer {
			public ulong Id;
			public string Name;
			public double ClockOrigin;
			public double Delay;
			public RaceSession Session;
			public Vector3 Position;
			public double Accel = 5;
			public bool JumpEarly;
			public bool Jumped;
			public double Go = double.NaN;
			public int RaceSeen = -1;
			public double PersonalBest;
			public bool Teleported;
			public BattleCar Car = new BattleCar { Spec = true, TuneSet = "TRC Revival", Car = "Test car" };
			public double Clock(double t) => t + ClockOrigin;
		}

		private sealed class RaceWire {
			public double DeliverAt;
			public byte[] Data;
			public Racer To;
		}

		private static void RaceTests() {
			RaceMessagesRoundTrip();
			GridSpotsLineUp();
			GridFileAndRecorder();
			TwelveCarRace();
			QualifyingSetsTheGrid();
			JumpStartRules();
			HostEndsTheRace();
			EliminationOnTheCircle();
			SixteenDriversSlowDown();
			PointsSeries();
			KnockoutBracket();
			HeatsAndFinal();
			Shootout();
		}

		// ---- Simulation ----

		/// <summary>
		/// A room of <paramref name="count"/> players: player 0 hosts on <paramref name="course"/>, the others join.
		/// <paramref name="step"/> runs every frame with the clock (seconds) so a test can press the host's buttons.
		/// </summary>
		private static List<Racer> RunRoom(Course course, RaceSettings settings, int count, Func<double, List<Racer>, bool> step,
			double seconds = 300, Action<Racer, int> setUp = null) {
			var racers = new List<Racer>();
			for (int i = 0; i < count; i++) {
				var r = new Racer { Id = RaceHost + (ulong)i, Name = $"Driver {i + 1}", ClockOrigin = 1000 * i + 17, Delay = 0.03 + 0.01 * (i % 5) };
				setUp?.Invoke(r, i);
				racers.Add(r);
			}
			Racer host = racers[0];
			host.Session = RaceSession.Host(777, host.Id, host.Name, course, settings, () => host.Car, () => host.PersonalBest, host.Clock(0));
			var wires = new List<RaceWire>();
			Vector3 park = course.Start.Center - course.Start.Forward * 200f;
			foreach (Racer r in racers) {
				r.Position = park + Vector3.right * (racers.IndexOf(r) * 3f);
			}

			for (double t = 0; t < seconds; t += Frame) {
				foreach (RaceWire wire in wires.Where(w => w.DeliverAt <= t).ToList()) {
					wires.Remove(wire);
					RaceMessage m = RaceMessage.Decode(wire.Data);
					Racer to = wire.To;
					if (m == null) {
						continue;
					}
					if (to.Session == null) {
						if (m.Kind == RaceKind.Lobby) {
							to.Session = RaceSession.Join(m, to.Id, to.Name, null, () => to.Car, () => to.PersonalBest, to.Clock(t));
						}
					} else {
						to.Session.Receive(m, to.Clock(t));
					}
				}

				foreach (Racer r in racers) {
					RaceSession s = r.Session;
					if (s == null) {
						continue;
					}
					double now = r.Clock(t);
					TimeAttackRun run = s.Run;
					float speed = 0f;
					Vector3 forward = course.Start.Forward;
					if (run != null && s.IAmRacing) {
						if (s.RaceNumber != r.RaceSeen) {
							r.RaceSeen = s.RaceNumber;
							r.Teleported = false;
							r.Go = double.NaN;
							r.Jumped = false;
						}
						GridSpot? spot = s.MySpot();
						if (!r.Teleported && spot.HasValue) {
							r.Teleported = true;
							r.Position = spot.Value.Position;
						}
						if (run.State == TimeAttackRun.Phase.Countdown && r.JumpEarly && !r.Jumped && run.CountdownNumber == 2) {
							r.Jumped = true;
							r.Position += forward * 1.5f;
							speed = 8f;
						} else if (run.State == TimeAttackRun.Phase.Running) {
							if (double.IsNaN(r.Go)) {
								r.Go = run.GoTime;
								r.Position = r.Position;
							}
							double since = now - r.Go;
							Vector3 from = spot?.Position ?? course.Start.Center;
							if (r.Jumped) {
								from += forward * 1.5f;
							}
							r.Position = from + forward * (float)(0.5 * r.Accel * since * since);
							speed = (float)(r.Accel * since * 3.6);
						}
					} else if (run != null && s.Phase == RacePhase.Qualifying) {
						// Qualifying: line up in the start box, then drive at this driver's pace.
						if (run.State == TimeAttackRun.Phase.ToStart || run.State == TimeAttackRun.Phase.Countdown) {
							r.Position = course.Start.Center - forward * 4f;
							r.Go = double.NaN;
						} else if (run.State == TimeAttackRun.Phase.Running) {
							if (double.IsNaN(r.Go)) {
								r.Go = run.GoTime;
							}
							double since = now - r.Go;
							r.Position = course.Start.Center - forward * 4f + forward * (float)(0.5 * r.Accel * since * since);
							speed = (float)(r.Accel * since * 3.6);
						}
					}
					s.Tick(now, r.Position, forward, speed, 1f, null);
					foreach (RaceMessage m in s.TakeOutbox()) {
						byte[] data = m.Encode();
						foreach (Racer other in racers.Where(o => o != r)) {
							wires.Add(new RaceWire { DeliverAt = t + r.Delay + other.Delay, Data = data, To = other });
						}
					}
				}
				if (step(t, racers)) {
					break;
				}
			}
			return racers;
		}

		private static Course RaceCourse() => MakeCourse(new List<Vector3> { new Vector3(0, 0, 0), new Vector3(0, 0, 500) });

		/// <summary>The host's button presses for a plain race: start once everyone's in, stop once it's finished.</summary>
		private static Func<double, List<Racer>, bool> StartWhenAllIn(int count, Action<double, RaceSession> extra = null) {
			bool started = false;
			return (t, racers) => {
				RaceSession host = racers[0].Session;
				double now = racers[0].Clock(t);
				if (!started && host.Drivers.Count == count && racers.All(r => r.Session != null && r.Session.Joined && r.Session.Course != null)) {
					started = true;
					host.StartRace(now);
				}
				extra?.Invoke(now, host);
				return started && host.Phase == RacePhase.Finished && racers.All(r => r.Session.Phase == RacePhase.Finished && r.Session.Results != null);
			};
		}

		// ---- Tests ----

		private static void RaceMessagesRoundTrip() {
			Course course = RaceCourse();
			course.GridPath = new List<Vector3> { course.Start.Center, course.Start.Center - Vector3.forward * 40f, course.Start.Center - Vector3.forward * 80f + Vector3.right * 10f };
			course.GridLayout = GridLayout.LineUp;
			var m = new RaceMessage { Kind = RaceKind.Course, RaceId = 5, From = RaceHost, Course = course };
			RaceMessage back = RaceMessage.Decode(m.Encode());
			Expect(back?.Course != null && back.Course.Code == course.Code && back.Course.GridPath.Count == 3 && back.Course.GridLayout == GridLayout.LineUp &&
				Vector3.Distance(back.Course.GridPath[2], course.GridPath[2]) < 0.01f, "a race's course comes through with its grid");

			var lobby = new RaceMessage {
				Kind = RaceKind.Lobby, RaceId = 5, From = RaceHost, RaceNumber = 3, HostName = "Host", CourseCode = course.Code, Phase = RacePhase.Gridding,
				Settings = new RaceSettings { Laps = 5, Order = GridOrder.Qualifying, Jump = JumpRule.Disqualify, Elimination = true, QualifyingMinutes = 7, QualifyingRuns = 2, PoleLeft = false },
				Lines = new List<string> { "Round 1 of 3", "1. Host 25 pts" },
				MatchId = 9, MatchA = 1, MatchB = 2, MatchFormat = BattleFormat.CatAndMouse, MatchGame = 2,
			};
			for (int i = 0; i < 16; i++) {
				lobby.Entries.Add(new LobbyEntry { Id = (ulong)(i + 1), Name = $"Driver {i + 1}", Slot = 15 - i, Racer = i % 3 != 0, EliminatedOnLap = i == 4 ? 2 : 0 });
			}
			RaceMessage lb = RaceMessage.Decode(lobby.Encode());
			Expect(lb != null && lb.Entries.Count == 16 && lb.Entries[0].Slot == 15 && !lb.Entries[0].Racer && lb.Entries[4].EliminatedOnLap == 2 &&
				lb.Settings.Laps == 5 && lb.Settings.Order == GridOrder.Qualifying && lb.Settings.Jump == JumpRule.Disqualify && lb.Settings.Elimination &&
				lb.Settings.QualifyingMinutes == 7 && lb.Settings.QualifyingRuns == 2 && !lb.Settings.PoleLeft && lb.Lines.Count == 2 && lb.MatchId == 9 && lb.MatchGame == 2,
				"a lobby with 16 drivers comes through the encoding unchanged");
			Expect(lobby.Encode().Length < 1500, $"a full lobby is a few small packets ({lobby.Encode().Length} bytes)");
		}

		private static void GridSpotsLineUp() {
			Course course = RaceCourse();
			List<GridSpot> grid = course.GridSpots(4, poleLeft: true, GridLayout.Grid);
			float side = Mathf.Clamp(course.Start.Width / 4f, 1.5f, 3f);
			Expect(Math.Abs(grid[0].Position.z + 4f) < 0.01f && Math.Abs(grid[1].Position.z + 8f) < 0.01f && Math.Abs(grid[3].Position.z + 16f) < 0.01f,
				"a two-wide grid staggers every 4 m behind the line");
			Expect(grid[0].Position.x < 0 && grid[1].Position.x > 0 && grid[2].Position.x < 0 && Math.Abs(Math.Abs(grid[0].Position.x) - side) < 0.01f,
				"pole is on the left when picked, the next car on the right, and so on");
			List<GridSpot> right = course.GridSpots(2, poleLeft: false, GridLayout.Grid);
			Expect(right[0].Position.x > 0, "pole can be on the right");
			List<GridSpot> line = course.GridSpots(16, true, GridLayout.LineUp);
			Expect(line.All(s => Math.Abs(s.Position.x) < 0.01f) && Math.Abs(line[15].Position.z + (4f + 15 * 8f)) < 0.01f && line.All(s => Vector3.Dot(s.Forward, Vector3.forward) > 0.99f),
				"a line-up is single file, 8 m apart, all facing the start");

			// A grid path that bends: spots follow it.
			course.GridPath = new List<Vector3> { new Vector3(0, 0, 0), new Vector3(0, 0, -20), new Vector3(-30, 0, -50) };
			List<GridSpot> bent = course.GridSpots(10, true, GridLayout.LineUp);
			Expect(bent[9].Position.x < -20f && Vector3.Dot(bent[9].Forward, new Vector3(1, 0, 1).normalized) > 0.99f, "spots follow a grid path round a bend");

			course.GridObjects = new List<GridSpot> { new GridSpot { Position = new Vector3(5, 0, -3), Forward = Vector3.forward }, new GridSpot { Position = new Vector3(-5, 0, -9), Forward = Vector3.forward } };
			List<GridSpot> objects = course.GridSpots(3, true);
			Expect(objects[0].Position == new Vector3(5, 0, -3) && objects[1].Position == new Vector3(-5, 0, -9) && objects[2].Position.z < -9f,
				"a map's grid objects come first, in number order, and more cars go behind the last one");
		}

		private static void GridFileAndRecorder() {
			Course course = RaceCourse();
			var recorder = new GridRecorder(course);
			for (float z = -120f; z < 3f; z += 0.5f) {
				recorder.Tick(new Vector3(0, 0, z));
			}
			Expect(recorder.State == GridRecorder.Phase.Done && recorder.Recorded > 100f, $"Lay out grid ends by itself through the start line ({recorder.State}, {recorder.Recorded:0} m)");
			List<Vector3> gridPath = recorder.GridPath();
			Expect(gridPath.Count > 30 && gridPath[0] == course.Start.Center && gridPath[gridPath.Count - 1].z < -100f, "the grid path runs from the start line backwards");
			course.GridPath = gridPath;
			course.GridLayout = GridLayout.LineUp;
			Course back = CourseFile.Parse(CourseFile.ToText(course), CourseSource.Custom);
			Expect(back.GridPath.Count == gridPath.Count && back.GridLayout == GridLayout.LineUp && back.Code == course.Code && back.Problems.Count == 0,
				"a course file keeps its grid, and the grid doesn't change the course code");
		}

		private static void TwelveCarRace() {
			Course course = RaceCourse();
			var settings = new RaceSettings { Name = "Test race", Open = false, Order = GridOrder.Joined };
			// Driver 7 is fastest, driver 1 (the host) slowest; everyone else in between.
			List<Racer> racers = RunRoom(course, settings, 12, StartWhenAllIn(12), setUp: (r, i) => r.Accel = i == 6 ? 6.5 : 4.0 + 0.1 * i);
			RaceSession host = racers[0].Session;
			Expect(host.Phase == RacePhase.Finished && host.Results?.Count == 12, $"a 12-car race finishes ({host.Phase}: {host.Message}, {host.Results?.Count} results)");
			Expect(host.Results != null && host.Results[0].Id == racers[6].Id && host.Results.All(r => r.State == RacerState.Finished),
				$"the fastest car wins and everyone finishes ({host.Results?.FirstOrDefault()?.Name})");
			bool agree = racers.All(r => r.Session.Results != null && r.Session.Results.Select(x => x.Id).SequenceEqual(host.Results.Select(x => x.Id)));
			Expect(agree, "every driver gets the same results");
			double go = racers[0].Go - racers[0].ClockOrigin;
			double spread = racers.Max(r => Math.Abs(r.Go - r.ClockOrigin - go));
			Expect(spread < 0.06, $"GO lands at the same moment on all 12 screens ({spread * 1000:0} ms apart)");
		}

		private static void QualifyingSetsTheGrid() {
			Course course = RaceCourse();
			var settings = new RaceSettings { Order = GridOrder.Qualifying, QualifyingMinutes = 1, QualifyingRuns = 1, Open = true };
			bool qualified = false, started = false;
			List<Racer> racers = RunRoom(course, settings, 4, (t, rs) => {
				RaceSession host = rs[0].Session;
				double now = rs[0].Clock(t);
				if (!qualified && host.Drivers.Count == 4 && rs.All(r => r.Session?.Course != null && r.Session.Joined)) {
					qualified = true;
					host.StartQualifying(now);
				}
				if (qualified && !started && host.Phase == RacePhase.Lobby && host.Drivers.All(d => d.BestQualifying > 0)) {
					started = true;
					host.StartRace(now);
				}
				return started && host.Phase == RacePhase.Gridding && rs.All(r => r.Session.MyDriver?.Slot >= 0);
			}, seconds: 200, setUp: (r, i) => r.Accel = new[] { 4.0, 6.0, 5.0, 4.5 }[i]);
			RaceSession h = racers[0].Session;
			Expect(started, $"after qualifying the host can start the race ({h.Phase}: {h.Message})");
			List<ulong> grid = h.Drivers.OrderBy(d => d.Slot).Select(d => d.Id).ToList();
			Expect(grid.SequenceEqual(new[] { racers[1].Id, racers[2].Id, racers[3].Id, racers[0].Id }),
				$"qualifying sets the grid, fastest on pole ({string.Join(", ", h.Drivers.OrderBy(d => d.Slot).Select(d => $"{d.Name} {d.BestQualifying:0.00}"))})");
		}

		private static void JumpStartRules() {
			Course course = RaceCourse();
			foreach (JumpRule rule in new[] { JumpRule.Penalty, JumpRule.Disqualify }) {
				var settings = new RaceSettings { Open = true, Jump = rule, JumpPenaltySeconds = 5 };
				// Driver 2 jumps the start and is the fastest by far.
				List<Racer> racers = RunRoom(course, settings, 3, StartWhenAllIn(3), setUp: (r, i) => {
					r.Accel = i == 1 ? 9 : 5;
					r.JumpEarly = i == 1;
				});
				List<RaceResultRow> results = racers[0].Session.Results;
				RaceResultRow jumper = results?.FirstOrDefault(r => r.Id == racers[1].Id);
				if (rule == JumpRule.Penalty) {
					Expect(jumper != null && jumper.State == RacerState.Finished && Math.Abs(jumper.Penalty - 5) < 0.01 && jumper.Position == 3,
						$"a jump start with the penalty rule finishes with +5 s and drops back ({jumper?.State}, +{jumper?.Penalty}, P{jumper?.Position})");
				} else {
					Expect(jumper != null && jumper.State == RacerState.Disqualified && jumper.Position == 3 && results.Take(2).All(r => r.State == RacerState.Finished),
						$"a jump start with the disqualify rule is out, and the race goes on for the rest ({jumper?.State})");
				}
			}
		}

		private static void HostEndsTheRace() {
			Course course = MakeCourse(new List<Vector3> { new Vector3(0, 0, 0), new Vector3(0, 0, 3000) });
			bool ended = false;
			// The leader finishes, then the host ends the race with the 60 s timer; the slowest driver can't make it.
			List<Racer> racers = RunRoom(course, new RaceSettings { Open = true }, 3, StartWhenAllIn(3, (now, host) => {
				if (!ended && host.Phase == RacePhase.Running && host.Drivers.Any(d => d.State == RacerState.Finished)) {
					ended = true;
					host.EndRace(now, 60);
				}
			}), seconds: 400, setUp: (r, i) => r.Accel = new[] { 3.0, 1.2, 0.35 }[i]);
			List<RaceResultRow> results = racers[0].Session.Results;
			Expect(ended && results != null && results[0].State == RacerState.Finished && results.Any(r => r.State == RacerState.Out && r.Reason.Contains("ended")),
				$"the host's 60 s end stops whoever hasn't finished ({string.Join(", ", results?.Select(r => $"{r.Name} {r.State}") ?? new string[0])})");
		}

		/// <summary>Elimination, with the other drivers' laps fed in as their beats would report them.</summary>
		private static void EliminationOnTheCircle() {
			Course ring = MakeCircuit();
			var settings = new RaceSettings { Open = true, Elimination = true };
			var host = RaceSession.Host(1, 1, "A", ring, settings, () => new BattleCar(), () => 0, 0);
			var ids = new ulong[] { 1, 2, 3, 4 };
			foreach (ulong id in ids.Skip(1)) {
				host.Receive(new RaceMessage { Kind = RaceKind.Join, RaceId = 1, From = id, Name = $"Driver {id}", HasCourse = true }, 0);
			}
			host.StartRace(0);
			Expect(host.Settings.Laps == 3, $"an elimination race has one lap per driver knocked out ({host.Settings.Laps} laps for 4)");
			// Lap counts as the others' beats report them: after lap 1, driver 4 is the only one not round yet.
			void Beat(ulong id, int laps, double at) => host.Receive(new RaceMessage {
				Kind = RaceKind.Beat, RaceId = 1, From = id, RaceNumber = host.RaceNumber, SentAt = at, LapsDone = laps, State = RacerState.Racing, Progress = laps * ring.Length,
			}, at);
			host.GoNow(0);
			for (double t = 0; t < 6; t += 0.1) {
				host.Tick(t, ring.Start.Center - ring.Start.Forward * 4f, ring.Start.Forward, 0f, 1f, null);
			}
			Beat(2, 1, 6); Beat(3, 1, 6); Beat(4, 0, 6);
			host.MyDriver.LapsDone = 1;
			host.Tick(6.1, ring.Start.Center, ring.Start.Forward, 0f, 1f, null);
			RaceDriver four = host.Drivers.First(d => d.Id == 4);
			Expect(four.State == RacerState.Eliminated && four.EliminatedOnLap == 1, $"the last car round lap 1 is eliminated ({four.State})");
		}

		private static void SixteenDriversSlowDown() {
			Course course = MakeCourse(new List<Vector3> { new Vector3(0, 0, 0), new Vector3(0, 0, 1500) });
			List<Racer> racers = RunRoom(course, new RaceSettings { Open = true, Layout = GridLayout.Grid }, 16, StartWhenAllIn(16), seconds: 400,
				setUp: (r, i) => r.Accel = 3 + 0.05 * i);
			Expect(racers[0].Session.Results?.Count == 16 && racers[0].Session.Results.All(r => r.State == RacerState.Finished),
				$"a full 16-car race finishes with everyone placed ({racers[0].Session.Results?.Count})");
			// Beats per second for one driver mid-race with 16 racers, counted straight from the session.
			var s = RaceSession.Host(2, 1, "A", course, new RaceSettings { Open = true }, () => new BattleCar { Spec = true }, () => 0, 0);
			for (ulong id = 2; id <= 16; id++) {
				s.Receive(new RaceMessage { Kind = RaceKind.Join, RaceId = 2, From = id, Name = $"D{id}", HasCourse = true }, 0);
			}
			s.StartRace(0);
			s.GoNow(0);
			Vector3 spot = s.MySpot().Value.Position;
			int beats = 0;
			for (double t = 0; t < 30; t += Frame) {
				double since = Math.Max(0, t - (TimeAttackRun.CountdownSeconds + 1.5));
				Vector3 at = spot + Vector3.forward * (float)(5 * since);
				s.Tick(t, at, Vector3.forward, since > 0 ? 18f : 0f, 1f, null);
				foreach (RaceMessage m in s.TakeOutbox()) {
					if (m.Kind == RaceKind.Beat && t > 10 && t < 20) {
						beats++;
					}
				}
				for (ulong id = 2; id <= 16; id++) {
					s.Receive(new RaceMessage { Kind = RaceKind.Beat, RaceId = 2, From = id, RaceNumber = s.RaceNumber, SentAt = t, State = RacerState.Racing, Ready = true, Spec = true, TuneSet = "" }, t);
				}
			}
			Expect(beats >= 18 && beats <= 22, $"with more than 8 racers, mid-race beats go twice a second ({beats} in 10 s)");
		}

		private static List<RaceResultRow> Finish(params ulong[] order) =>
			order.Select((id, i) => new RaceResultRow { Id = id, Name = $"D{id}", Position = i + 1, State = RacerState.Finished, Time = 60 + i, BestLap = 30 + (id == 3 ? -1 : i) }).ToList();

		private static void PointsSeries() {
			var settings = new TournamentSettings {
				Kind = TournamentKind.Points, Name = "Cup",
				Rounds = new List<TournamentRound> { new TournamentRound { CourseName = "A" }, new TournamentRound { CourseName = "B", ReverseGrid = true } },
			};
			var seeds = new List<(ulong, string)> { (1, "D1"), (2, "D2"), (3, "D3"), (4, "D4") };
			var cup = new Tournament(settings, seeds);
			cup.AddRaceResult(Finish(1, 2, 3, 4));
			Expect(cup.Ranked()[0].Id == 1 && cup.Ranked()[0].Points == 25 && cup.Table.First(t => t.Id == 3).Points == 16 + 1,
				"a points series gives 25, 20, 16... and +1 for the fastest lap");
			Expect(cup.NextGrid()?.SequenceEqual(new ulong[] { 4, 3, 2, 1 }) == true, "a reverse-grid round starts in reverse order of the last race");
			cup.AddRaceResult(Finish(2, 1, 4, 3));
			// D1 25+20 = 45, D2 20+25 = 45: tied on points and wins, D2 finished better in the last race.
			Expect(cup.Done && cup.Ranked()[0].Id == 2 && cup.Ranked()[1].Id == 1 && cup.Winner == "D2",
				$"ties go to the most wins, then the better finish in the last race ({string.Join(", ", cup.Ranked().Select(s => $"{s.Name} {s.Points}"))})");
			var dnf = new Tournament(settings, seeds);
			var rows = Finish(1, 2, 3, 4);
			rows[3].State = RacerState.Out;
			dnf.AddRaceResult(rows);
			Expect(dnf.Table.First(t => t.Id == 4).Points == 0, "a driver who doesn't finish scores nothing");
		}

		private static void KnockoutBracket() {
			var seeds = Enumerable.Range(1, 6).Select(i => ((ulong)i, $"D{i}")).ToList();
			var ko = new Tournament(new TournamentSettings { Kind = TournamentKind.Knockout, BestOf = 3, FinalBestOf = 3, ThirdPlace = true }, seeds);
			Expect(ko.KnockoutRounds == 3 && ko.Matches.Count(m => m.Round == 0) == 4, "6 drivers make an 8-place bracket");
			Expect(ko.Matches.Where(m => m.Round == 0).Count(m => m.Winner != 0) == 2 && ko.Matches.First(m => m.Round == 1 && m.Index == 0).A == 1,
				"the top two seeds get byes into the next round");
			KnockoutMatch first = ko.NextMatch();
			Expect(first != null && first.Round == 0 && new[] { first.A, first.B }.OrderBy(x => x).SequenceEqual(new ulong[] { 4, 5 }) == true || first?.A + first?.B == 3 + 6 || first?.A + first?.B == 4 + 5,
				$"the first battle is a seeded pair ({first?.A} v {first?.B})");
			// Play it out: the lower seed number always wins, 2-0 (or 2-1 once).
			int guard = 0;
			for (KnockoutMatch m = ko.NextMatch(); m != null && guard++ < 50; m = ko.NextMatch()) {
				ulong better = Math.Min(m.A, m.B), worse = Math.Max(m.A, m.B);
				ko.Report(m.Id, better);
				if (m.Final) {
					ko.Report(m.Id, worse);
				}
				ko.Report(m.Id, 0); // a draw is run again
				ko.Report(m.Id, better);
			}
			KnockoutMatch final = ko.Matches.First(m => m.Final);
			KnockoutMatch third = ko.Matches.First(m => m.ThirdPlace);
			Expect(ko.Done && ko.Winner == "D1" && final.WinsA + final.WinsB == 3 && final.Winner == 1, $"best of three: the final goes 2-1, seed 1 wins ({final.WinsA}-{final.WinsB})");
			Expect(third.Winner == 3, $"the semifinal losers race for third ({third.Winner})");
			Expect(final.Format == BattleFormat.CatAndMouse, "the final uses the final's format");
		}

		private static void HeatsAndFinal() {
			var seeds = Enumerable.Range(1, 10).Select(i => ((ulong)i, $"D{i}")).ToList();
			var heats = new Tournament(new TournamentSettings { Kind = TournamentKind.Heats, HeatSize = 5, Advance = 2 }, seeds);
			Expect(heats.Heats.Count == 2 && heats.Heats[0].SequenceEqual(new ulong[] { 1, 4, 5, 8, 9 }) && heats.Heats[1].SequenceEqual(new ulong[] { 2, 3, 6, 7, 10 }),
				"seeds snake over the heats so they're even");
			heats.AddHeatResult(Finish(1, 4, 5, 8, 9));
			heats.AddHeatResult(Finish(3, 2, 6, 7, 10));
			Expect(heats.InFinal && heats.NextHeatRacers().SequenceEqual(new ulong[] { 1, 3, 4, 2 }), $"the top 2 of each heat make the final, gridded by heat place ({string.Join(",", heats.NextHeatRacers())})");
			heats.AddHeatResult(Finish(4, 1, 3, 2));
			Expect(heats.Done && heats.Winner == "D4", "the final decides the winner");
		}

		private static void Shootout() {
			var t = new Tournament(new TournamentSettings { Kind = TournamentKind.Shootout }, new List<(ulong, string)> { (1, "A"), (2, "B"), (3, "C") });
			t.SetShootout(new[] { ((ulong)1, "A", 61.2), ((ulong)2, "B", 0.0), ((ulong)3, "C", 60.9) });
			Expect(t.Done && t.Winner == "C" && t.ShootoutResults.Last().Name == "B", "a shootout goes to the best time, no time last");
		}
	}
}
