using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader.CodeCheck {
	/// <summary>
	/// 0.16: regulations, just-for-fun events and regulation Elo, recalled starts and no penalties, races the host only
	/// runs, console players timed by the host, the headlight start, lobbies over 16, and ghosts and shared runs.
	/// </summary>
	internal static partial class SelfTest {
		private static void Version016Tests() {
			RegulationFile();
			RegulationChecks();
			RegulationMessages();
			RegulationElo();
			RecallTheStart();
			NoPenaltiesAndHostOnly();
			RegulatedRaceWaitsForRules();
			ConsolePlayerTimedByHost();
			HeadlightStart();
			TwentyDriverLobby();
			GhostsRecordAndPlay();
			SharedRunsAndRegulations();
		}

		private static Regulation GunmaRace() => new Regulation {
			Name = "Gunma race spec", Author = "Gunma", Notes = "For the touge cup",
			Setups = new List<string> { "Racing", "Ultimate" }, MaxHp = 600, MinKgPerHp = 2.0f,
			EngineSwaps = false, OutsideSwaps = false, Widebody = false,
			Limits = new List<ValueLimit> { new ValueLimit { Key = "rear_tyre_width", Max = 300 }, new ValueLimit { Key = "front_camber", Min = -6, Max = 0 } },
			Tyres = new List<TyreBracket> { new TyreBracket { UpToKg = 1200, MaxWidth = 265 }, new TyreBracket { UpToKg = 1400, MaxWidth = 285 } },
		};

		private static CarFacts GoodCar() => new CarFacts {
			CarId = 12, CarName = "Test car", Setup = "Racing", Hp = 450, Mass = 1250, EngineSwap = false, DrivetrainChanged = false,
			OutsideChanges = new List<string>(), Stance = new List<string>(),
			Numbers = new Dictionary<string, float> { ["rear_tyre_width"] = 275, ["front_tyre_width"] = 255, ["front_camber"] = -4 },
		};

		private static void RegulationFile() {
			Regulation r = GunmaRace();
			Regulation back = Regulation.Parse(r.ToText());
			Expect(back != null && back.Name == r.Name && back.Code == r.Code && back.MaxHp == 600 && back.Setups.SequenceEqual(new[] { "Racing", "Ultimate" }) &&
				!back.EngineSwaps && back.OutsideWeight && back.Limits.Count == 2 && back.Limits.First(l => l.Key == "front_camber").Min == -6 && back.Tyres.Count == 2,
				$"a regulation file reads back the same ({back?.Code} vs {r.Code})");
			Regulation renamed = r.Copy();
			renamed.Name = "Something else";
			renamed.Notes = "";
			Expect(renamed.Code == r.Code && r.Code.StartsWith("REG-", StringComparison.Ordinal), "renaming a regulation keeps its code (it's worked out from the rules)");
			Regulation stricter = r.Copy();
			stricter.MaxHp = 550;
			Expect(stricter.Code != r.Code, "changing a rule changes the code");
			string share = r.ShareText();
			Regulation pasted = Regulation.FromPasted($"Here's ours: Gunma race spec ({r.Code}): {share} have fun");
			Expect(share.StartsWith(Regulation.SharePrefix, StringComparison.Ordinal) && pasted != null && pasted.Code == r.Code && pasted.Name == r.Name,
				$"the Discord line pastes back to the same regulation, even with words around it ({share.Length} characters)");
			Expect(Regulation.FromPasted("TRCREG1.notbase64!!") == null && Regulation.FromPasted("hello") == null && Regulation.FromPasted(r.ToText())?.Code == r.Code,
				"broken pastes are refused; a whole file's text works too");
			string folder = Path.Combine(Path.GetTempPath(), "trc-regulations-" + Guid.NewGuid().ToString("N"));
			try {
				r.Save(folder);
				r.Copy().Save(folder);
				List<Regulation> loaded = Regulation.LoadAll(folder);
				Expect(loaded.Count == 2 && loaded.All(x => x.Code == r.Code) && loaded.Select(x => x.FilePath).Distinct().Count() == 2,
					"saving two with the same name keeps both files");
			} finally {
				Directory.Delete(folder, true);
			}
		}

		private static void RegulationChecks() {
			Regulation r = GunmaRace();
			Expect(r.Check(GoodCar()).Passes && r.Check(GoodCar()).Unknown.Count == 0, $"a car within the rules meets the regulation ({string.Join("; ", r.Check(GoodCar()).Broken)})");
			CarFacts strong = GoodCar();
			strong.Hp = 640;
			RegulationResult over = r.Check(strong);
			Expect(!over.Passes && over.Broken.Any(b => b.Contains("640 hp")) && over.Broken.Any(b => b.Contains("kg per hp")), $"too much power breaks the hp and power-to-weight rules ({string.Join("; ", over.Broken)})");
			CarFacts wrongSetup = GoodCar();
			wrongSetup.Setup = "UltimatePro";
			Expect(!r.Check(wrongSetup).Passes && r.Check(wrongSetup).Broken[0].Contains("Ultimate Pro"), "a setup that isn't allowed is named");
			CarFacts swapped = GoodCar();
			swapped.EngineSwap = true;
			swapped.OutsideChanges = new List<string> { "Live Engine max torque: 700 (expected 500)", "Live Weight: 1100 (expected 1250)" };
			RegulationResult swaps = r.Check(swapped);
			Expect(swaps.Broken.Any(b => b.Contains("CarX engine swaps")) && swaps.Broken.Any(b => b.Contains("KINO swap")) && !swaps.Broken.Any(b => b.Contains("weight menu")),
				$"engine swaps and KINO swaps are caught; KINO weight is allowed here ({string.Join("; ", swaps.Broken)})");
			Expect(Regulation.KindOf("Live Rear slow bump: 1 (expected 2)") == Regulation.OutsideKind.Suspension && Regulation.KindOf("Live Centre of gravity height: 0.4 (expected 0.5)") == Regulation.OutsideKind.Weight &&
				Regulation.KindOf("Live Gear 3 ratio: 1.2 (expected 1.3)") == Regulation.OutsideKind.Engine, "changes outside CarX are sorted into swaps, weight and suspension");
			CarFacts wide = GoodCar();
			wide.Stance = new List<string> { "Stance: rear track +40 mm" };
			Expect(r.Check(wide).Broken.Any(b => b.Contains("widebody")), "a widebody kit is caught when it isn't allowed");
			// Weight classes: a 1250 kg car is in the 1400 class (285 mm); at 1150 kg it's in the 1200 class (265 mm).
			CarFacts light = GoodCar();
			light.Mass = 1150;
			light.Hp = 400;
			RegulationResult tyres = r.Check(light);
			Expect(r.BracketFor(1250).MaxWidth == 285 && r.BracketFor(1700).MaxWidth == 285 && tyres.Broken.Count == 1 && tyres.Broken[0].Contains("1150 kg cars may run up to 265"),
				$"tyre widths follow the weight class ({string.Join("; ", tyres.Broken)})");
			CarFacts camber = GoodCar();
			camber.Numbers["front_camber"] = -7.5f;
			camber.Numbers["rear_tyre_width"] = 300.4f;
			Regulation noClasses = r.Copy();
			noClasses.Tyres.Clear();
			RegulationResult limits = noClasses.Check(camber);
			Expect(limits.Broken.Count == 1 && limits.Broken[0].Contains("Front camber"), $"tune limits have a least and a most; CarX's rounding is allowed ({string.Join("; ", limits.Broken)})");
			// Checked the way CarX shows it (bskftw, Nov 1): 600.4 hp is 600; a tyre stretch of 10.3 shows as 10.
			var power = new Regulation { MaxHp = 600 };
			Expect(power.Check(new CarFacts { Hp = 600.4f }).Passes && !power.Check(new CarFacts { Hp = 600.6f }).Passes && !power.Check(new CarFacts { Hp = 603f }).Passes,
				"power is checked to the whole hp: 600.4 passes a 600 hp limit, 600.6 and 603 don't");
			var stretch = new Regulation { Limits = new List<ValueLimit> { new ValueLimit { Key = "rear_tire_stretch", Max = 10 } } };
			CarFacts Stretch(float v, int? decimals) {
				var f = new CarFacts { Numbers = new Dictionary<string, float> { ["rear_tire_stretch"] = v } };
				if (decimals.HasValue) {
					f.Decimals["rear_tire_stretch"] = decimals.Value;
				}
				return f;
			}
			RegulationResult tooWide = stretch.Check(Stretch(10.6f, 0));
			Expect(stretch.Check(Stretch(10.3f, 0)).Passes && !tooWide.Passes && tooWide.Broken[0].Contains("10.6 (shows as 11)") &&
				!stretch.Check(Stretch(10.3f, 1)).Passes && stretch.Check(Stretch(10.04f, 1)).Passes,
				$"a tune value passes when it shows as the limit: 10.3 shown as 10 passes, 10.6 shown as 11 doesn't, and with one decimal shown 10.3 is over ({tooWide.Broken[0]})");
			var unknown = new CarFacts { Setup = "Racing" };
			RegulationResult notKnown = r.Check(unknown);
			Expect(notKnown.Passes && notKnown.Unknown.Contains("power") && notKnown.Unknown.Contains("engine swap"), "what couldn't be read is listed as unknown, not broken");
			Regulation cars = new Regulation { Cars = new List<int> { 5, 7 } };
			Expect(!cars.Check(GoodCar()).Passes && cars.Check(new CarFacts { CarId = 7 }).Passes, "a list of cars allows only those");
		}

		private static void RegulationMessages() {
			Regulation r = GunmaRace();
			var lobby = new RaceMessage {
				Kind = RaceKind.Lobby, RaceId = 5, From = RaceHost, HostName = "Host", CourseCode = "CRS-1",
				Settings = new RaceSettings { Open = true, Regulation = r, Fun = true, HostDrives = false, LightStart = true, NoPenalties = true, Jump = JumpRule.Recall, MaxPlayers = 40 },
				Recalls = 2, RecallBy = "Frog",
			};
			lobby.Entries.Add(new LobbyEntry { Id = RaceHost, Name = "Host" });
			lobby.Entries.Add(new LobbyEntry { Id = RaceSession.ConsoleId("123|PS4"), Name = "Pad", Console = true, Platform = "PlayStation" });
			RaceMessage back = RaceMessage.Decode(lobby.Encode());
			Expect(back != null && back.Settings.Regulation?.Code == r.Code && back.Settings.Fun && !back.Settings.HostDrives && back.Settings.LightStart && back.Settings.NoPenalties &&
				back.Settings.Jump == JumpRule.Recall && back.Settings.MaxPlayers == 40 && back.Entries[1].Console && back.Entries[1].Platform == "PlayStation" && !back.Entries[0].Console &&
				back.Recalls == 2 && back.RecallBy == "Frog", "a 0.16 lobby carries the regulation, fun, host-only, light start, no penalties, console players and recalls");
			// A 0.15 lobby (nothing at the end) reads with the old defaults.
			var old = new RaceMessage { Kind = RaceKind.Lobby, RaceId = 5, From = RaceHost, HostName = "Host", CourseCode = "CRS-1", Settings = new RaceSettings() };
			byte[] bytes = old.Encode();
			int trailing = 4 + 1 + 1 + 1 + 4 + 1 - 1; // fun, host drives, lights, no penalties; empty regulation; no consoles; recalls; empty name
			RaceMessage fromOld = RaceMessage.Decode(bytes.Take(bytes.Length - trailing).ToArray());
			Expect(fromOld != null && fromOld.Settings.Regulation == null && fromOld.Settings.HostDrives && !fromOld.Settings.Fun, "a lobby from 0.15 (without the new fields) still reads, as before");
			Expect(RaceSession.IsConsoleId(RaceSession.ConsoleId("123|PS4")) && !RaceSession.IsConsoleId(76561198000000001) && RaceSession.ConsoleId("a") != RaceSession.ConsoleId("b"),
				"console players' IDs never look like SteamIDs");

			var beat = new RaceMessage { Kind = RaceKind.Beat, RaceId = 5, From = 2, RuleOk = true, Elo = 1612, JumpedCountdown = 3 };
			RaceMessage beatBack = RaceMessage.Decode(beat.Encode());
			Expect(beatBack.HasRuleOk && beatBack.RuleOk && Math.Abs(beatBack.Elo - 1612) < 0.01 && beatBack.JumpedCountdown == 3, "a beat carries the rule, the Elo and a jump start");

			var challenge = new BattleMessage { Kind = BattleKind.Challenge, BattleId = 9, From = 1, To = 2, FromName = "A", CourseCode = "CRS-1", Open = true, Regulation = r, Fun = true };
			byte[] c = challenge.Encode();
			BattleMessage cBack = BattleMessage.Decode(c);
			Expect(c[0] == BattleMessage.RuleProtocol && cBack != null && cBack.Regulation?.Code == r.Code && cBack.Fun,
				"a regulation challenge goes out as protocol 7 (older copies never see it) and carries the regulation");
			var plain = new BattleMessage { Kind = BattleKind.Challenge, BattleId = 9, From = 1, To = 2, FromName = "A", CourseCode = "CRS-1", Fun = true };
			byte[] p = plain.Encode();
			Expect(p[0] == BattleMessage.Protocol && BattleMessage.Decode(p).Fun && BattleMessage.Decode(p).Regulation == null, "a just-for-fun TRC spec challenge stays protocol 5");
			var heart = new BattleMessage { Kind = BattleKind.Heartbeat, BattleId = 9, From = 1, To = 2, RuleOk = true, Elo = 1490 };
			BattleMessage hBack = BattleMessage.Decode(heart.Encode());
			Expect(hBack.HasRuleOk && hBack.RuleOk && Math.Abs(hBack.Elo - 1490) < 0.01, "a heartbeat carries the car's rule and Elo");

			// A regulated battle: each side lines up only meeting the rule, and it counts (for the regulation's Elo).
			var session = BattleSession.Challenge(9, 1, "A", 2, "B", RaceCourse(), BattleFormat.Race, false, () => new BattleCar { Spec = false, RuleOk = true }, 0, regulation: r);
			Expect(session.Open && session.Regulation != null && session.Counted && !session.IsTrcSpec && session.CarOk(new BattleCar { RuleOk = true }) && !session.CarOk(new BattleCar { Spec = true }),
				"a regulated battle needs cars that meet the regulation, not TRC spec, and counts");
		}

		private static void RegulationElo() {
			var book = new RatingBook();
			string code = GunmaRace().Code;
			book.RuleNames[code] = "Gunma race spec";
			book.Battle(CourseKind.Touge, 1500, 1, "B", DateTime.Now, code);
			Expect(Math.Abs(book.Elo[0] - RatingBook.StartElo) < 0.001 && book.EloFor(code, CourseKind.Touge) > RatingBook.StartElo && book.EloFor("REG-0000-0000", CourseKind.Touge) == RatingBook.StartElo,
				"a regulation battle moves the regulation's own Elo, not TRC spec Elo");
			string folder = Path.Combine(Path.GetTempPath(), "trc-ruleelo-" + Guid.NewGuid().ToString("N"));
			try {
				book.Save(folder, SealOwner);
				RatingBook back = RatingBook.Load(folder, SealOwner);
				Expect(!back.WasChanged && Math.Abs(back.EloFor(code, CourseKind.Touge) - book.EloFor(code, CourseKind.Touge)) < 0.01 && back.RuleNames[code] == "Gunma race spec",
					"regulation Elo is saved in the sealed ratings file and read back");
			} finally {
				Directory.Delete(folder, true);
			}
		}

		private static void RecallTheStart() {
			Course course = RaceCourse();
			var settings = new RaceSettings { Open = true, Jump = JumpRule.Recall };
			int recalledAt = -1;
			List<Racer> racers = RunRoom(course, settings, 3, StartWhenAllIn(3, (now, host) => {
				if (host.Recalls > 0 && recalledAt < 0) {
					recalledAt = host.Recalls;
				}
			}), setUp: (r, i) => {
				r.Accel = 5 + i;
				r.JumpEarly = i == 2;
			});
			RaceSession h = racers[0].Session;
			List<RaceResultRow> results = h.Results;
			Expect(h.Recalls == 1 && h.RecallBy == "Driver 3" && results != null && results.All(r => r.State == RacerState.Finished && r.Penalty == 0) && results[0].Id == racers[2].Id,
				$"a jump start recalls the start: everyone lines up again, nobody gets a penalty, and the race runs ({h.Recalls} recall(s) by {h.RecallBy}; {string.Join(", ", results?.Select(r => $"{r.Name} {r.State}") ?? new string[0])})");
			Expect(racers.All(r => r.Session.Recalls == 1), "every driver's mod knows the start was recalled");
		}

		private static void NoPenaltiesAndHostOnly() {
			Course course = RaceCourse();
			var settings = new RaceSettings { Open = true, Jump = JumpRule.None, HostDrives = false };
			List<Racer> racers = RunRoom(course, settings, 3, StartWhenAllIn(3), setUp: (r, i) => {
				r.Accel = 5 + i;
				r.JumpEarly = i == 2;
			});
			RaceSession h = racers[0].Session;
			List<RaceResultRow> results = h.Results;
			Expect(results != null && results.Count == 2 && results.All(r => r.Id != racers[0].Id && r.State == RacerState.Finished && r.Penalty == 0) && !h.MyDriver.Racer,
				$"a host who only runs the race isn't in it, and \"no penalty\" lets a jump start go ({results?.Count} result(s))");
			h.Penalize(racers[1].Id, 5, false, 0);
			Expect(h.Results.First(r => r.Id == racers[1].Id).Penalty == 5, "the host can still give a penalty by hand when penalties are on");
			RaceSettings none = h.Settings.Copy();
			none.NoPenalties = true;
			h.SetSettings(none);
			h.Penalize(racers[2].Id, 10, false, 0);
			Expect(h.Results.First(r => r.Id == racers[2].Id).Penalty == 0, "with no penalties, the host can't add any");
		}

		private static void RegulatedRaceWaitsForRules() {
			Course course = RaceCourse();
			Regulation r = GunmaRace();
			var settings = new RaceSettings { Open = true, Regulation = r };
			// Driver 2's car doesn't meet the regulation for the first 40 s, then it's fixed (in the dyno, say).
			double fixAt = 40;
			bool startedEarly = false;
			Func<double, List<Racer>, bool> start = StartWhenAllIn(3);
			List<Racer> racers = RunRoom(course, settings, 3, (t, rs) => {
				if (t >= fixAt) {
					rs[1].Car.RuleOk = true;
				}
				RaceSession host = rs[0].Session;
				if (t < fixAt && (host.Phase == RacePhase.Countdown || host.Phase == RacePhase.Running)) {
					startedEarly = true;
				}
				return start(t, rs);
			}, setUp: (racer, i) => racer.Car = new BattleCar { Spec = false, RuleOk = i != 1, Car = "Test car" });
			Expect(!startedEarly && racers[0].Session.Results?.Count == 3 && racers[0].Session.Results.All(x => x.State == RacerState.Finished),
				$"a regulated race doesn't start until every car meets the regulation; then it runs (early {startedEarly}, {racers[0].Session.Phase}, {racers[0].Session.Message}, " +
				$"{string.Join(", ", racers[0].Session.Drivers.Select(d => $"{d.Name} ready {d.Ready} rule {d.RuleOk}/{d.SaidRuleOk}"))}; results {racers[0].Session.Results?.Count})");
		}

		/// <summary>A console player in a race: the host's mod times them from where their car is, and everyone sees them.</summary>
		private static void ConsolePlayerTimedByHost() {
			Course course = RaceCourse();
			ulong pad = RaceSession.ConsoleId("555|PS4");
			var host = RaceSession.Host(31, RaceHost, "Host", course, new RaceSettings { Open = true }, () => new BattleCar { Spec = true }, () => 0, 0);
			var watcher = RaceSession.Join(RaceMessage.Decode(new RaceMessage { Kind = RaceKind.Lobby, RaceId = 31, From = RaceHost, HostName = "Host", CourseCode = course.Code, Settings = new RaceSettings { Open = true } }.Encode()),
				RaceHost + 1, "Watcher", course, () => new BattleCar { Spec = true }, () => 0, 0);
			Expect(host.AddConsole(pad, "Pad", "PlayStation", 0) && !host.AddConsole(pad, "Pad", "PlayStation", 0), "the host adds a console player once");
			host.SetConsoleCar(pad, new BattleCar { Spec = true, Car = "Console car" });
			Vector3 park = course.Start.Center - course.Start.Forward * 100f;
			Vector3 padAt = park;
			Vector3 hostAt = park + Vector3.right * 5f;
			double padGo = double.NaN;
			bool started = false;
			bool sawLights = false;
			for (double t = 0; t < 120; t += Frame) {
				if (!started && t > 1) {
					started = true;
					host.StartRace(t);
				}
				TimeAttackRun padRun = host.ConsoleRun(pad);
				if (padRun != null && padRun.Spot.HasValue && padRun.State == TimeAttackRun.Phase.ToStart && double.IsNaN(padGo)) {
					padAt = padRun.Spot.Value.Position;
				}
				float padKmh = 0;
				if (padRun != null && padRun.State == TimeAttackRun.Phase.Running) {
					if (double.IsNaN(padGo)) {
						padGo = t + 0.3; // they see the lights a moment late
					}
					double since = Math.Max(0, t - padGo);
					padAt = padRun.Spot.Value.Position + course.Start.Forward * (float)(0.5 * 6 * since * since);
					padKmh = (float)(6 * since * 3.6);
				}
				sawLights |= host.NeedsLightStart && host.GoAt > 0;
				host.SetConsolePose(pad, padAt, course.Start.Forward, padKmh, t);
				GridSpot? spot = host.MySpot();
				if (spot.HasValue && host.Run != null && host.Run.State != TimeAttackRun.Phase.Running) {
					hostAt = spot.Value.Position;
				} else if (host.Run != null && host.Run.State == TimeAttackRun.Phase.Running) {
					hostAt += course.Start.Forward * (float)(4 * Frame);
				}
				host.Tick(t, hostAt, course.Start.Forward, host.Run?.State == TimeAttackRun.Phase.Running ? 14f : 0f, 1f, null);
				foreach (RaceMessage m in host.TakeOutbox()) {
					watcher.Receive(RaceMessage.Decode(m.Encode()), t);
				}
				watcher.Tick(t, park + Vector3.left * 20f, course.Start.Forward, 0f, 1f, null);
				if (host.Drivers.First(d => d.Id == pad).State == RacerState.Finished) {
					break;
				}
			}
			RaceDriver padDriver = host.Drivers.First(d => d.Id == pad);
			RaceDriver seen = watcher.Drivers.FirstOrDefault(d => d.Id == pad);
			Expect(sawLights && padDriver.State == RacerState.Finished && padDriver.Time > 10 && padDriver.Time < 20,
				$"the host's mod times a console player from GO to the finish, and asks for the headlight start ({padDriver.State}, {padDriver.Time:0.00} s)");
			Expect(seen != null && seen.Console && seen.Progress > course.Length * 0.9f, $"other drivers see the console player as a driver, timed by the host ({seen?.Progress:0} m)");
			// A console player who moves before GO under the recall rule recalls the start.
			var strict = RaceSession.Host(32, RaceHost, "Host", course, new RaceSettings { Open = true, Jump = JumpRule.Recall, HostDrives = false }, () => new BattleCar { Spec = true }, () => 0, 0);
			strict.AddConsole(pad, "Pad", "PlayStation", 0);
			strict.SetConsoleCar(pad, new BattleCar { Spec = true });
			strict.StartRace(0);
			Vector3 at = Vector3.zero;
			bool moved = false;
			for (double t = 0; t < 20 && strict.Recalls == 0; t += Frame) {
				TimeAttackRun run = strict.ConsoleRun(pad);
				if (run != null && run.Spot.HasValue && !moved) {
					at = run.Spot.Value.Position;
				}
				if (run != null && run.State == TimeAttackRun.Phase.Countdown && run.CountdownNumber == 2) {
					moved = true;
					at += course.Start.Forward * 0.2f;
				}
				strict.SetConsolePose(pad, at, course.Start.Forward, moved ? 10f : 0f, t);
				strict.Tick(t, park, course.Start.Forward, 0f, 1f, null);
			}
			Expect(strict.Recalls == 1 && strict.Phase == RacePhase.Gridding, $"a console player's jump start recalls the start too ({strict.Recalls}, {strict.Phase})");
		}

		private static void HeadlightStart() {
			bool? At(double toGo) => LightStart.LightsAt(toGo);
			Expect(At(10) == null && At(3.8) == true && At(3.0) == false && At(2.7) == false && At(2.5) == true && At(1.9) == false && At(1.5) == true && At(0.8) == false &&
				At(0.3) == true && At(0) == false && At(-1) == false && At(-3) == null,
				"the headlight start: on, off at 3, 2 and 1, off at GO, then left alone");
		}

		private static void TwentyDriverLobby() {
			Course course = MakeCourse(new List<Vector3> { new Vector3(0, 0, 0), new Vector3(0, 0, 800) });
			List<Racer> racers = RunRoom(course, new RaceSettings { Open = true, MaxPlayers = RaceSettings.MaxDrivers, Layout = GridLayout.Grid, LayoutFromCourse = false }, 20, StartWhenAllIn(20),
				seconds: 400, setUp: (r, i) => r.Accel = 3 + 0.05 * i);
			List<RaceResultRow> results = racers[0].Session.Results;
			Expect(results?.Count == 20 && results.All(r => r.State == RacerState.Finished) && racers.All(r => r.Session.Drivers.Count == 20),
				$"a 20-car race (over the old 16) runs and finishes with everyone placed ({results?.Count})");
			var points = new Tournament(new TournamentSettings { Kind = TournamentKind.Points, Rounds = new List<TournamentRound> { new TournamentRound() } },
				Enumerable.Range(1, 20).Select(i => ((ulong)i, $"D{i}")).ToList());
			points.AddRaceResult(Enumerable.Range(1, 20).Select(i => new RaceResultRow { Id = (ulong)i, Name = $"D{i}", Position = i, State = RacerState.Finished, Time = 60 + i }).ToList());
			Expect(points.Table.Count == 20 && points.Table.First(s => s.Id == 20).Points == 0 && points.Table.First(s => s.Id == 1).Points >= 25,
				"a points series with 20 drivers scores the top 16 and gives the rest nothing");
		}

		private static void GhostsRecordAndPlay() {
			Course course = MakeCourse(new List<Vector3> { new Vector3(0, 0, 0), new Vector3(0, 0, 400) });
			var ghost = new Ghost { CourseCode = course.Code, CourseName = "Test", Driver = "Me", Car = "Test car", Time = 20, Name = "Clean run before the cup" };
			for (double t = 0; t <= 20.001; t += Frame) {
				ghost.Record(t, new Vector3(0, 0, (float)(t * 20)), new Quaternion(0f, Mathf.Sin((float)t * 0.01f), 0f, Mathf.Cos((float)t * 0.01f)), 72f);
			}
			Expect(ghost.Samples.Count >= 195 && ghost.Samples.Count <= 205, $"a ghost keeps 10 points a second ({ghost.Samples.Count} for 20 s)");
			Expect(ghost.PoseAt(10.05, out GhostSample mid) && Math.Abs(mid.Position.z - 201f) < 0.5f && !ghost.PoseAt(25, out _) && !ghost.PoseAt(-1, out _),
				$"a ghost is placed between its points, and gone before and after its run ({mid.Position.z:0.0} m at 10.05 s)");
			byte[] bytes = ghost.Encode();
			Ghost back = Ghost.Decode(bytes);
			Expect(back != null && back.Samples.Count == ghost.Samples.Count && Math.Abs(back.Time - 20) < 1e-9 && back.Driver == "Me" && back.Name == "Clean run before the cup" &&
				Math.Abs(back.Samples[100].Rotation.y - ghost.Samples[100].Rotation.y) < 0.001f && Math.Abs(back.Samples[100].Rotation.w - ghost.Samples[100].Rotation.w) < 0.001f && Math.Abs(back.Samples[100].Kmh - 72f) < 0.1f && bytes.Length < 8000,
				$"a ghost packs and reads back the same ({bytes.Length} bytes for 20 s)");
			float[] trace = ghost.TraceOn(course);
			Expect(trace != null && trace.Length >= 40 && Math.Abs(trace[20] - 10f) < 0.15f, $"a ghost gives the time along the course for the live delta ({trace?[20]:0.00} s at 200 m)");
			Expect(Ghost.Decode(new byte[] { 1, 2, 3 }) == null && Ghost.Decode(bytes.Take(bytes.Length / 2).ToArray()) == null, "a damaged ghost is refused");
			string folder = Path.Combine(Path.GetTempPath(), "trc-ghosts-" + Guid.NewGuid().ToString("N"));
			try {
				ghost.Save(folder);
				var faster = new Ghost { CourseCode = course.Code, Driver = "Fast", Time = 18, Samples = ghost.Samples };
				faster.Save(folder);
				List<Ghost> list = Ghost.LoadFor(folder, course.Code);
				Expect(list.Count == 2 && list[0].Driver == "Fast" && Ghost.LoadFor(folder, "CRS-NONE").Count == 0, "saved ghosts load fastest first, per course");
			} finally {
				Directory.Delete(folder, true);
			}
		}

		private static void SharedRunsAndRegulations() {
			Regulation r = GunmaRace();
			var share = new RoomMessage { Kind = RoomKind.RegulationShare, From = SealOther, Name = "Frog", To = 0, Regulation = r };
			RoomMessage back = RoomMessage.Decode(share.Encode());
			var board = new RoomBoard();
			RoomBoard.SharedItem item = board.ReceiveItem(back, 0, SealOwner);
			Expect(item?.Regulation?.Code == r.Code && board.SharedRegulations.Count == 1 && !item.JustForMe, "a regulation shared with the room waits in the list");
			Expect(board.ReceiveItem(back, 3, SealOwner) == null, "one player can't flood the list");

			var ghost = new Ghost { CourseCode = "CRS-1", CourseName = "Akina", MapKey = "workshop:1", Driver = "Frog", Time = 61.2 };
			for (double t = 0; t <= 61.2; t += 0.1) {
				ghost.Record(t, new Vector3((float)t, 0, (float)t), new Quaternion(0f, 0f, 0f, 1f), 80f);
			}
			var run = new RoomMessage { Kind = RoomKind.RunShare, From = SealOther, Name = "Frog", To = SealOwner, MapKey = "workshop:1", MapName = "Project Dream", Ghost = ghost };
			RoomMessage runBack = RoomMessage.Decode(run.Encode());
			RoomBoard.SharedItem runItem = board.ReceiveItem(runBack, 20, SealOwner);
			Expect(runItem?.Ghost != null && runItem.JustForMe && runItem.Ghost.Samples.Count == ghost.Samples.Count && runItem.Ghost.DriverId == SealOther && board.SharedRuns.Count == 1,
				$"a run shared with one player arrives with its ghost ({run.Encode().Length} bytes)");
			Expect(new RoomBoard().ReceiveItem(runBack, 0, SealOwner + 7) == null, "a run shared with one player doesn't show for anyone else");
		}
	}
}
