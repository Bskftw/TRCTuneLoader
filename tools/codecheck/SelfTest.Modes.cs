using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader.CodeCheck {
	/// <summary>Speed traps, the best line, race control, ratings, highway battles and the room games.</summary>
	internal static partial class SelfTest {
		private static void ModeTests() {
			SpeedTrapsCatchSpeed();
			SpeedTrapFiles();
			CourseTrapsInARun();
			BestLineIsRecorded();
			ContactIsNoticed();
			CutUpScoresPasses();
			FlagsShow();
			RatingsMove();
			HighwayBattle();
			RoomGames();
			PenaltiesAndTrapScoring();
			Fixes015Tests();
		}

		private static void SpeedTrapsCatchSpeed() {
			CourseGate.TryMake(new Vector3(0, 0, 100), Vector3.forward, 12f, out CourseGate gate);
			var traps = new List<SpeedTrap> { new SpeedTrap { Name = "Bridge", Gate = gate, MapKey = "test:1" } };
			var watcher = new SpeedTrapWatcher();
			SpeedTrapHit? hit = null;
			// Speeding up through it: 180 km/h just before, 200 just after: it's caught in between.
			for (int i = 0; i <= 120 && hit == null; i++) {
				float z = 90f + i * 0.2f;
				hit = watcher.Tick(i * Frame, new Vector3(0, 0, z), z < 100f ? 180f : 200f, traps);
			}
			Expect(hit.HasValue && hit.Value.Trap.Name == "Bridge" && hit.Value.Kmh >= 180f && hit.Value.Kmh <= 200f, $"a speed trap catches the speed as the car goes through ({hit?.Kmh:0.0} km/h)");
			SpeedTrapHit? again = watcher.Tick(3, new Vector3(0, 0, 99), 150f, traps) ?? watcher.Tick(3.1, new Vector3(0, 0, 101), 150f, traps);
			Expect(!again.HasValue, "the same trap doesn't count again straight away");
			var back = new SpeedTrapWatcher();
			back.Tick(10, new Vector3(0, 0, 101), 120f, traps);
			Expect(back.Tick(10.05, new Vector3(0, 0, 99), 120f, traps).HasValue, "a free-roam trap counts either way through");
			Expect(!new SpeedTrapWatcher().Tick(0, new Vector3(0, 0, 50), 100f, traps).HasValue, "nowhere near a trap, nothing");
			Expect(traps[0].Key == "test:1@0,0,100", $"a trap's key is its map and place ({traps[0].Key})");
		}

		private static void SpeedTrapFiles() {
			string folder = Path.Combine(Path.GetTempPath(), "trc_traps_" + Guid.NewGuid().ToString("N"));
			try {
				CourseGate.TryMake(new Vector3(12.5f, 3f, -40f), new Vector3(1, 0, 1), 16f, out CourseGate gate);
				string file = Path.Combine(folder, SpeedTrapFile.FileName);
				SpeedTrapFile.Save(file, new[] { new SpeedTrap { Name = "Tunnel exit", Gate = gate, MapKey = "test:1" } });
				List<SpeedTrap> back = SpeedTrapFile.Load(file, "test:1");
				Expect(back.Count == 1 && back[0].Name == "Tunnel exit" && Vector3.Distance(back[0].Gate.Center, gate.Center) < 0.01f &&
					Math.Abs(back[0].Gate.Width - 16f) < 0.01f && Vector3.Dot(back[0].Gate.Forward, gate.Forward) > 0.999f, "free-roam traps save and load");
				SpeedTrapLog.Append(folder, new SpeedTrapRecord { When = new DateTime(2026, 10, 25, 20, 0, 0), TrapKey = back[0].Key, TrapName = "Tunnel exit, west", Kmh = 251.4f, CarId = 7, Car = "AE86", Spec = true, Context = "freeroam" });
				List<SpeedTrapRecord> records = SpeedTrapLog.Load(folder);
				Expect(records.Count == 1 && Math.Abs(records[0].Kmh - 251.4f) < 0.05f && records[0].TrapName == "Tunnel exit, west" && records[0].Spec, "trap speeds are saved");

				Course course = RaceCourse();
				CourseGate.TryMake(new Vector3(0, 0, 250), Vector3.forward, 12f, out CourseGate trap);
				course.Traps.Add(trap);
				Course read = CourseFile.Parse(CourseFile.ToText(course), CourseSource.Custom);
				Expect(read.Traps.Count == 1 && Vector3.Distance(read.Traps[0].Center, trap.Center) < 0.01f && read.Code == course.Code && read.Problems.Count == 0,
					"a course file keeps its speed traps, and they don't change the course code");
				Course up = course.ReversedAs("Up");
				Expect(up.Traps.Count == 1 && Vector3.Dot(up.Traps[0].Forward, trap.Forward) < -0.99f, "a reversed course's traps face the other way");
			} finally {
				try {
					Directory.Delete(folder, true);
				} catch (Exception) {
				}
			}
		}

		private static void CourseTrapsInARun() {
			Course course = RaceCourse();
			CourseGate.TryMake(new Vector3(0, 0, 100), Vector3.forward, 12f, out CourseGate a);
			CourseGate.TryMake(new Vector3(0, 0, 400), Vector3.forward, 12f, out CourseGate b);
			course.Traps.AddRange(new[] { a, b });
			var run = new TimeAttackRun(course, flying: true);
			for (double now = 0; now < 40 && run.Active; now += Frame) {
				float speed = 20f + (float)now * 2f;
				run.Tick(now, new Vector3(0, 0, -20f + 20f * (float)now + (float)(now * now)), Vector3.forward, speed * 3.6f, 1f);
			}
			Expect(run.State == TimeAttackRun.Phase.Finished && run.TrapSpeeds.Count == 2 && run.TrapSpeeds[1] > run.TrapSpeeds[0],
				$"a run catches its speed at each of the course's traps ({string.Join(", ", run.TrapSpeeds.Select(s => s.ToString("0")))} km/h)");
			Expect(Math.Abs(run.TrapTotal - run.TrapSpeeds.Sum()) < 0.01f, "the trap total adds them up");
		}

		private static void BestLineIsRecorded() {
			Course course = RaceCourse();
			var run = new TimeAttackRun(course, flying: true);
			for (double now = 0; now < 40 && run.Active; now += Frame) {
				run.Tick(now, new Vector3(0, 0, -20f + 30f * (float)now), Vector3.forward, 108f, 1f);
			}
			LapTime lap = run.LapTimes.FirstOrDefault();
			Expect(lap != null && lap.Line.Count > 200 && lap.Line.Count == lap.LineKmh.Count && lap.LineKmh.All(k => Math.Abs(k - 108f) < 0.1f),
				$"a run keeps its line every 2 m with its speed ({lap?.Line.Count} points)");
			string folder = Path.Combine(Path.GetTempPath(), "trc_line_" + Guid.NewGuid().ToString("N"));
			try {
				TimeAttackTraces.SaveLine(folder, "k", lap.Line, lap.LineKmh, 16000);
				var back = TimeAttackTraces.LoadLine(folder, "k");
				Expect(back != null && back.Value.Line.Count == lap.Line.Count && Vector3.Distance(back.Value.Line[10], lap.Line[10]) < 0.01f,
					"the best line saves and loads");
			} finally {
				try {
					Directory.Delete(folder, true);
				} catch (Exception) {
				}
			}
		}

		private static void ContactIsNoticed() {
			var watcher = new ContactWatcher();
			var other = new NearbyCar { Id = 2, Name = "Rival", Position = new Vector3(2, 0, 0) };
			string hard = null;
			// Hard braking: 100 to 80 km/h in a second (about 0.6 g): not a hit.
			for (int i = 0; i <= 60; i++) {
				hard = hard ?? watcher.Tick(i * Frame, Vector3.zero, 100f - i / 3f, new[] { other });
			}
			Expect(hard == null, "hard braking isn't a hit");
			string hit = null;
			for (int i = 0; i < 10 && hit == null; i++) {
				hit = watcher.Tick(2 + i * Frame, Vector3.zero, i < 5 ? 120f : 95f, new[] { other });
			}
			Expect(hit == "light contact with Rival", $"losing 25 km/h next to another car is light contact, no points ({hit})");
			var close = new ContactWatcher();
			string hardHit = null;
			for (int i = 0; i < 10 && hardHit == null; i++) {
				hardHit = close.Tick(i * Frame, Vector3.zero, i < 5 ? 120f : 80f, new[] { other });
			}
			Expect(hardHit == "contact with Rival", $"losing 40 km/h at once next to another car is contact ({hardHit})");
			var alone = new ContactWatcher();
			string wall = null;
			for (int i = 0; i < 10 && wall == null; i++) {
				wall = alone.Tick(i * Frame, Vector3.zero, i < 5 ? 120f : 80f, new NearbyCar[0]);
			}
			Expect(wall == "hit something", "with nobody near, a hard stop is hit something");
			var drift = new ContactWatcher();
			string slide = null;
			for (int i = 0; i < 10 && slide == null; i++) {
				slide = drift.Tick(i * Frame, Vector3.zero, i < 5 ? 120f : 95f, new NearbyCar[0]);
			}
			Expect(slide == null, "losing 25 km/h alone (a big drift) isn't anything");
		}

		private static void CutUpScoresPasses() {
			var cutUp = new CutUpScorer();
			float total = 0;
			// Driving +z at 180 km/h; a car at 100 km/h, 2 m to the side, passed twice in a row, then a third car 20 m to the side.
			for (double t = 0; t < 4; t += Frame) {
				float me = 50f * (float)t;
				var cars = new[] {
					new NearbyCar { Id = 2, Name = "A", Position = new Vector3(2, 0, 30f + 27.8f * (float)t), Kmh = 100f },
					new NearbyCar { Id = 3, Name = "B", Position = new Vector3(-2.5f, 0, 60f + 27.8f * (float)t), Kmh = 100f },
					new NearbyCar { Id = 4, Name = "C", Position = new Vector3(20, 0, 20f + 27.8f * (float)t), Kmh = 100f },
				};
				total += cutUp.Tick(t, new Vector3(0, 0, me), Vector3.forward, 180f, cars);
			}
			Expect(cutUp.Points > 0 && Math.Abs(cutUp.Points - total) < 0.01f && cutUp.Combo == 3, $"close fast passes score, with a combo ({cutUp.Points:0} pts, x{cutUp.Combo})");
			float first = 100f * 1.8f * 1.8f * (1.5f - 2f / CutUpScorer.CloseMetres);
			Expect(cutUp.Points > first * 2.5f, "the second pass is worth more with the combo");
			cutUp.Break();
			Expect(cutUp.Combo == 1, "an incident loses the combo");
		}

		private static void FlagsShow() {
			var (yellow, blue) = RaceFlags.For(1000f, 0f, new[] { ("Slow", 1150f, 10f, false), ("Fast", 1100f, 150f, false) });
			Expect(yellow.Contains("Slow") && yellow.Contains("150") && blue.Length == 0, $"a slow car ahead shows a yellow flag ({yellow})");
			var (_, lapped) = RaceFlags.For(1500f, 2000f, new[] { ("Leader", 3460f, 160f, false) });
			Expect(lapped.Contains("Leader"), $"a car a lap ahead closing in shows a blue flag ({lapped})");
			var (none, noBlue) = RaceFlags.For(1500f, 2000f, new[] { ("Close", 1460f, 160f, false) });
			Expect(none.Length == 0 && noBlue.Length == 0, "a car just behind on the same lap shows nothing");
		}

		private static void RatingsMove() {
			var a = new RatingBook();
			double change = a.Battle(CourseKind.Touge, 1500, 1, "B", DateTime.Now);
			Expect(Math.Abs(change - 16) < 0.01 && Math.Abs(a.Elo[0] - 1516) < 0.01 && a.Elo[1] == 1500, "beating an even driver is +16 touge Elo, and only touge");
			var b = new RatingBook();
			b.Elo[0] = 1800;
			double upset = b.Battle(CourseKind.Touge, 1500, 0, "A", DateTime.Now);
			Expect(upset < -25, $"losing to a much lower rating costs a lot ({upset:0.0})");
			var c = new RatingBook();
			double race = c.Race(CourseKind.Circuit, 1, new List<(double, int)> { (1500, 2), (1500, 3), (1500, 4) }, DateTime.Now);
			Expect(Math.Abs(race - 16) < 0.01, $"winning a race against even drivers is +16 ({race:0.0})");

			var clean = new RatingBook();
			for (int i = 0; i < 3; i++) {
				clean.Drive(10, 0, "run", DateTime.Now);
			}
			Expect(clean.Safety > 3.0 && clean.License == License.D, $"clean driving raises the Safety Rating and earns the D license ({clean.Badge})");
			var close = new RatingBook();
			double hard = close.Drive(3, 40, "race with lots of contact", DateTime.Now);
			Expect(Math.Abs(hard + RatingBook.MaxDropPerEvent) < 0.001, $"one event takes at most {RatingBook.MaxDropPerEvent:0.00} away ({hard:0.00})");
			var messy = new RatingBook { License = License.C, Safety = 1.05 };
			messy.Drive(2, RatingBook.ContactPoints, "race", DateTime.Now);
			Expect(messy.License == License.D && Math.Abs(messy.Safety - RatingBook.StartSafety) < 0.01, $"dropping below 1.00 loses a license step ({messy.Badge})");
			string folder = Path.Combine(Path.GetTempPath(), "trc_rating_" + Guid.NewGuid().ToString("N"));
			try {
				clean.Save(folder);
				RatingBook back = RatingBook.Load(folder);
				Expect(back.License == clean.License && Math.Abs(back.Safety - clean.Safety) < 0.001 && back.Events == 3 && back.History.Count == clean.History.Count, "ratings save and load");
			} finally {
				try {
					Directory.Delete(folder, true);
				} catch (Exception) {
				}
			}
		}

		private static void HighwayBattle() {
			// Both roll side by side at 100 km/h; at GO the host speeds up and pulls away: the guest's SP drains to zero.
			var host = new Player { Name = "Host", Id = HostId, ClockOrigin = 100 };
			var guest = new Player { Name = "Guest", Id = GuestId, ClockOrigin = 5000 };
			Course free = Course.FreeRoam("test:1");
			host.Session = BattleSession.Challenge(30, HostId, "Host", GuestId, "Guest", free, BattleFormat.Highway, true, () => host.Car, host.Clock(0));
			var wires = new List<Wire>();
			float hostZ = 0f, guestZ = 0f;
			double goSeen = -1;
			for (double t = 0; t < 120; t += Frame) {
				foreach (Wire wire in wires.Where(w => w.DeliverAt <= t).ToList()) {
					wires.Remove(wire);
					BattleMessage m = BattleMessage.Decode(wire.Data);
					if (m.Kind == BattleKind.Challenge && wire.To.Session == null) {
						wire.To.Session = BattleSession.FromChallenge(m, wire.To.Id, wire.To.Name, free, () => wire.To.Car, wire.To.Clock(t));
						wire.To.Session.Accept(wire.To.Clock(t));
					} else {
						wire.To.Session?.Receive(m, wire.To.Clock(t));
					}
				}
				bool running = host.Session.State == BattleSession.Phase.Running;
				if (running && goSeen < 0) {
					goSeen = t;
				}
				float hostKmh = running ? 100f + 40f * (float)Math.Min(5, t - goSeen) : 100f;
				hostZ += hostKmh / 3.6f * (float)Frame;
				guestZ += 100f / 3.6f * (float)Frame;
				foreach (Player p in new[] { host, guest }) {
					if (p.Session == null) {
						continue;
					}
					bool isHost = p == host;
					p.Session.Tick(p.Clock(t), new Vector3(isHost ? -2f : 2f, 0, isHost ? hostZ : guestZ), Vector3.forward, isHost ? hostKmh : 100f, 1f, null);
					foreach (BattleMessage m in p.Session.TakeOutbox()) {
						wires.Add(new Wire { DeliverAt = t + 0.07, Data = m.Encode(), To = isHost ? guest : host });
					}
				}
				if (guest.Session != null && !host.Session.Active && !guest.Session.Active) {
					break;
				}
			}
			Expect(goSeen > 0, "a highway battle starts rolling once both are alongside at speed");
			Expect(host.Session.State == BattleSession.Phase.Finished && host.Session.Winner == HostId && guest.Session.Winner == HostId && guest.Session.MySp <= 0f,
				$"the car that pulls away gaps the other: both agree ({host.Session.Message} / {guest.Session?.Message})");
			Expect(host.Session.MySp > 99f, "the leader's SP doesn't drain");
			HighwayCountdownLost(dropAll: false);
			HighwayCountdownLost(dropAll: true);
		}

		/// <summary>
		/// Oct 29: the guest never got the countdown, the host raced alone and lost Elo for a battle the guest never
		/// drove. Now the countdown is sent again until the guest has it; when it never arrives, nobody wins.
		/// </summary>
		private static void HighwayCountdownLost(bool dropAll) {
			var host = new Player { Name = "Host", Id = HostId, ClockOrigin = 100 };
			var guest = new Player { Name = "Guest", Id = GuestId, ClockOrigin = 5000 };
			Course free = Course.FreeRoam("test:1");
			host.Session = BattleSession.Challenge(31, HostId, "Host", GuestId, "Guest", free, BattleFormat.Highway, true, () => host.Car, host.Clock(0));
			var wires = new List<Wire>();
			float hostZ = 0f, guestZ = 0f;
			int countdowns = 0;
			bool backToGrid = false;
			for (double t = 0; t < 90; t += Frame) {
				foreach (Wire wire in wires.Where(w => w.DeliverAt <= t).ToList()) {
					wires.Remove(wire);
					BattleMessage m = BattleMessage.Decode(wire.Data);
					if (m.Kind == BattleKind.Challenge && wire.To.Session == null) {
						wire.To.Session = BattleSession.FromChallenge(m, wire.To.Id, wire.To.Name, free, () => wire.To.Car, wire.To.Clock(t));
						wire.To.Session.Accept(wire.To.Clock(t));
					} else {
						wire.To.Session?.Receive(m, wire.To.Clock(t));
					}
				}
				bool running = host.Session.State == BattleSession.Phase.Running && (guest.Session?.State == BattleSession.Phase.Running);
				float hostKmh = running ? 140f : 100f;
				hostZ += hostKmh / 3.6f * (float)Frame;
				guestZ += 100f / 3.6f * (float)Frame;
				foreach (Player p in new[] { host, guest }) {
					if (p.Session == null) {
						continue;
					}
					bool isHost = p == host;
					p.Session.Tick(p.Clock(t), new Vector3(isHost ? -2f : 2f, 0, isHost ? hostZ : guestZ), Vector3.forward, isHost ? hostKmh : 100f, 1f, null);
					foreach (BattleMessage m in p.Session.TakeOutbox()) {
						// The first countdown is lost (or every one of them).
						if (m.Kind == BattleKind.Countdown && (dropAll || countdowns++ == 0)) {
							continue;
						}
						wires.Add(new Wire { DeliverAt = t + 0.07, Data = m.Encode(), To = isHost ? guest : host });
					}
				}
				backToGrid |= host.Session.Message.Contains("didn't reach both");
				if (guest.Session != null && !host.Session.Active && !guest.Session.Active) {
					break;
				}
			}
			if (dropAll) {
				Expect(host.Session.Winner == 0 && host.Session.State != BattleSession.Phase.Finished && backToGrid,
					$"a highway start that never reaches the other driver is called off, and nobody wins ({host.Session.Message})");
			} else {
				Expect(host.Session.State == BattleSession.Phase.Finished && host.Session.Winner == HostId && guest.Session.Winner == HostId,
					$"a lost highway countdown is sent again, and the battle is decided the same on both sides ({host.Session.Message} / {guest.Session?.Message})");
			}
		}

		private static void RoomGames() {
			RaceSession Game(RoomMode mode, int players, Action<RaceSettings> setUp = null) {
				var settings = new RaceSettings { Mode = mode, RoundMinutes = 5, Cops = 1, HillSeconds = 10, KeepUpMetres = 100 };
				setUp?.Invoke(settings);
				var s = RaceSession.Host(50 + (uint)mode, 1, "D1", null, settings, () => new BattleCar(), () => 0, 0);
				for (ulong id = 2; id <= (ulong)players; id++) {
					s.Receive(new RaceMessage { Kind = RaceKind.Join, RaceId = s.Id, From = id, Name = $"D{id}" }, 0);
				}
				s.StartRace(0);
				return s;
			}
			void Beat(RaceSession s, ulong id, Vector3 at, float kmh, double now) => s.Receive(new RaceMessage {
				Kind = RaceKind.Beat, RaceId = s.Id, From = id, RaceNumber = s.RaceNumber, SentAt = now, Position = at, Speed = kmh, Heading = Vector3.forward,
			}, now);
			void Run(RaceSession s, double from, double to, Func<ulong, double, (Vector3, float)> where) {
				for (double t = from; t < to; t += 0.1) {
					foreach (RaceDriver d in s.Drivers.Where(d => d.Id != 1).ToList()) {
						var (at, kmh) = where(d.Id, t);
						Beat(s, d.Id, at, kmh, t);
					}
					var (mine, myKmh) = where(1, t);
					s.Tick(t, mine, Vector3.forward, myKmh, 1f, null);
				}
			}

			// Cops: driver 1's cop/runner part is random, so find them; the cop parks next to a stopped runner for 3 s.
			RaceSession cops = Game(RoomMode.CopsAndRunners, 3);
			Expect(cops.Drivers.Count(d => d.Role == RoomRole.Cop) == 1 && cops.Drivers.Count(d => d.Role == RoomRole.Runner) == 2, "cops and runners: one cop, the rest run");
			ulong cop = cops.Drivers.First(d => d.Role == RoomRole.Cop).Id;
			ulong caught = cops.Drivers.First(d => d.Role == RoomRole.Runner).Id;
			Run(cops, 0, 12, (id, t) => id == cop ? (new Vector3(0, 0, 0), 0f) : id == caught ? (new Vector3(3, 0, 0), 0f) : (new Vector3(500, 0, (float)t * 20), 72f));
			RaceDriver busted = cops.Drivers.First(d => d.Id == caught);
			Expect(busted.Role == RoomRole.Cop && busted.Reason.Contains("busted") && cops.Drivers.First(d => d.Id == cop).Points >= 100,
				$"a runner stopped next to a cop for 3 s is busted, joins the cops, and the cop scores ({busted.Role}, {busted.Reason})");

			// Tag: whoever is it drives into driver 3.
			RaceSession tag = Game(RoomMode.Tag, 3);
			ulong it = tag.Drivers.First(d => d.Role == RoomRole.It).Id;
			ulong target = tag.Drivers.First(d => d.Id != it).Id;
			Run(tag, 0, 8, (id, t) => id == it ? (new Vector3(0, 0, Math.Min(98f, (float)t * 20)), 50f) : id == target ? (new Vector3(0, 0, 100), 10f) : (new Vector3(400, 0, 0), 50f));
			Expect(tag.Drivers.First(d => d.Id == target).Role == RoomRole.It && tag.Drivers.First(d => d.Id == it).Role == RoomRole.None, "tag: touching passes it on");

			// King of the hill: the host's spot at the start is the hill; driver 2 holds it alone for 10 s.
			RaceSession hill = Game(RoomMode.KingOfTheHill, 2);
			Run(hill, 0, 30, (id, t) => id == 2 ? (Vector3.zero, 0f) : (new Vector3(300, 0, 0), 0f));
			Expect(hill.Phase == RacePhase.Finished && hill.Results?.First().Id == 2, $"king of the hill: holding the hill alone wins ({hill.Phase})");

			// Keep-up: driver 1 leads; driver 3 drops 150 m back for 10 s and is out; driver 2 stays close.
			RaceSession keep = Game(RoomMode.KeepUp, 3);
			Run(keep, 0, 20, (id, t) => id == 1 ? (new Vector3(0, 0, (float)t * 30), 108f) : id == 2 ? (new Vector3(0, 0, (float)t * 30 - 20), 108f) : (new Vector3(0, 0, (float)t * 30 - 150), 108f));
			Expect(keep.Drivers.First(d => d.Id == 3).Out && !keep.Drivers.First(d => d.Id == 2).Out, "keep-up: falling too far behind the leader is out");

			// Infected: the infected drives through everyone.
			RaceSession inf = Game(RoomMode.Infected, 3);
			ulong zero = inf.Drivers.First(d => d.Role == RoomRole.Infected).Id;
			Run(inf, 0, 10, (id, t) => id == zero ? (new Vector3(0, 0, (float)t * 20), 72f) : (new Vector3(0, 0, id == 2 || zero == 2 && id == 3 ? 60 : 120), 0f));
			Expect(inf.Phase == RacePhase.Finished && inf.Drivers.All(d => d.Role == RoomRole.Infected || inf.Results.First().Id == d.Id),
				$"infected: touching spreads it, and the game ends with one survivor ({inf.Phase})");
		}

		private static void PenaltiesAndTrapScoring() {
			Course course = RaceCourse();
			CourseGate.TryMake(new Vector3(0, 0, 250), Vector3.forward, 12f, out CourseGate trap);
			course.Traps.Add(trap);
			var s = RaceSession.Host(90, 1, "D1", course, new RaceSettings { Open = true, SpeedTrapScoring = true }, () => new BattleCar(), () => 0, 0);
			s.Receive(new RaceMessage { Kind = RaceKind.Join, RaceId = 90, From = 2, Name = "D2", HasCourse = true }, 0);
			s.Receive(new RaceMessage { Kind = RaceKind.Join, RaceId = 90, From = 3, Name = "D3", HasCourse = true }, 0);
			s.StartRace(0);
			foreach (RaceDriver d in s.Drivers) {
				d.State = RacerState.Finished;
				d.Time = 60 + d.Id;
				d.TrapTotal = d.Id == 3 ? 260f : 200f + d.Id;
			}
			List<RaceResultRow> traps = s.ResultsFromDrivers();
			Expect(traps[0].Id == 3, "a speed trap race goes to the highest trap total, not the fastest time");
			// Drivers 2 and 3 finish (their mods say so); the host ends the race for itself.
			var timed = RaceSession.Host(91, 1, "D1", course, new RaceSettings { Open = true }, () => new BattleCar(), () => 0, 0);
			timed.Receive(new RaceMessage { Kind = RaceKind.Join, RaceId = 91, From = 2, Name = "D2", HasCourse = true }, 0);
			timed.Receive(new RaceMessage { Kind = RaceKind.Join, RaceId = 91, From = 3, Name = "D3", HasCourse = true }, 0);
			timed.StartRace(0);
			timed.GoNow(0);
			for (double t = 0; t < 6; t += 0.1) {
				timed.Tick(t, Vector3.zero, Vector3.forward, 0f, 1f, null);
			}
			foreach (ulong id in new ulong[] { 2, 3 }) {
				timed.Receive(new RaceMessage { Kind = RaceKind.Beat, RaceId = 91, From = id, RaceNumber = timed.RaceNumber, SentAt = 6, State = RacerState.Finished, Time = id == 2 ? 60 : 63 }, 6);
			}
			timed.EndRace(6, 0);
			timed.Tick(6.1, Vector3.zero, Vector3.forward, 0f, 1f, null);
			Expect(timed.Results != null && timed.Results[0].Id == 2 && timed.Results[1].Id == 3, "before penalties, the faster time wins");
			timed.Penalize(2, 5, false, 7);
			Expect(timed.Results[0].Id == 3 && timed.Results[1].Id == 2 && Math.Abs(timed.Results[1].Penalty - 5) < 0.01, "a host penalty of +5 s is added and the results change");
			timed.Penalize(3, 0, true, 8);
			Expect(timed.Results.Last().Id == 3 && timed.Results.Last().State == RacerState.Disqualified, "a disqualified driver goes to the bottom");

			var called = RaceSession.Host(92, 1, "D1", course, new RaceSettings { Open = true }, () => new BattleCar(), () => 0, 0);
			called.StartRace(0);
			called.EndRace(1, 0);
			called.Tick(1.1, Vector3.zero, Vector3.forward, 0f, 1f, null);
			Expect(called.Phase == RacePhase.Finished, $"calling a race off while lining up ends it ({called.Phase})");

			var lobby = new RaceMessage { Kind = RaceKind.Beat, RaceId = 1, From = 2, Position = new Vector3(1, 2, 3), Heading = Vector3.forward, Speed = 140f, Incidents = 3, LastIncident = "possible contact with D1", Score = 1234.5f, TrapTotal = 480f };
			RaceMessage back = RaceMessage.Decode(lobby.Encode());
			Expect(back != null && back.Position == new Vector3(1, 2, 3) && back.Incidents == 3 && back.LastIncident == lobby.LastIncident && Math.Abs(back.Score - 1234.5f) < 0.01f && back.TrapTotal == 480f,
				"a race beat carries the car's position, incidents, score and trap total");
		}
	}
}
