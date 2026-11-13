// Network bytes snapshot (refactor step 14): every message kind turned into bytes, compared with the bytes recorded on Nov 11 2026.
// While the code is reorganized, the bytes must stay exactly the same, so players on 0.18.3 keep understanding the new build.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;

namespace TRC.TuneLoader.CodeCheck {
	internal static partial class SelfTest {
		/// <summary>
		/// Each message's length and the start of its SHA-256, as today's code makes it. A name missing here makes the
		/// test fail and print the line to add, so new message kinds get recorded on purpose. TRC reports (TrcSync) and
		/// the traffic's room messages are written straight into KSL's packets, which this offline test can't load;
		/// tools/compare_builds.py covers those (the code that writes them must stay the same).
		/// </summary>
		private static readonly Dictionary<string, string> WireExpected = new Dictionary<string, string>(StringComparer.Ordinal) {
			{ "battle challenge, course with path", "1068 4258892E52CDEA14" },
			{ "battle challenge, speed traps", "1081 C13FDF3184CCEB25" },
			{ "battle challenge, regulation", "453 491FB95835DBFA3E" },
			{ "battle answer", "24 D92533CB6CD40A39" },
			{ "battle heartbeat", "149 16CD4775B2777E90" },
			{ "battle countdown", "38 7F9E4228FD239784" },
			{ "battle abort", "37 C1B82E6793230BB9" },
			{ "battle leave", "24 1604FDA2F4E53314" },
			{ "battle seen", "54 86C2662510FAC1A8" },
			{ "race lobby", "574 6F93480BDCCEF82E" },
			{ "race join", "22 289528CAC6ACDFE2" },
			{ "race leave", "17 2A00E7A0C2D803E4" },
			{ "race course with traps", "1045 EABFD1E5BE621CC5" },
			{ "race beat", "145 B16220784E25315C" },
			{ "race host beat", "74 9AA3AC648FD505F4" },
			{ "race results", "121 113D1FEF3D97AF84" },
			{ "race match report", "29 C802C9209E81A5A0" },
			{ "room status", "65 516157BF99D9B110" },
			{ "room course share", "1026 A3F106F49DADEF29" },
			{ "room trap share", "107 F337441A770D33EF" },
			{ "room regulation share", "423 8F575CDEA0003825" },
			{ "room run share", "473 B9D482C298A97890" },
			{ "chunk 1 of 3", "512 BB3EEB8A805CA88A" },
			{ "chunk 2 of 3", "512 98D6DA4744FB3D21" },
			{ "chunk 3 of 3", "80 0A1DD66C381DF40D" },
		};

		private static void WireSnapshotTests() {
			var bytes = new List<(string Name, byte[] Data)>();
			foreach ((string name, Func<byte[]> make) in WireMessages()) {
				bytes.Add((name, make()));
			}
			// A big message split into pieces for the packets (each piece's header and its share of the bytes).
			byte[] big = bytes.First(b => b.Name == "battle challenge, course with path").Data;
			foreach (BattleChunks.Piece piece in BattleChunks.Split(big)) {
				bytes.Add(($"chunk {piece.Index + 1} of {piece.Count}", PieceBytes(piece)));
			}

			foreach ((string name, byte[] data) in bytes) {
				string got = $"{data.Length} {Fingerprint(data)}";
				if (!WireExpected.TryGetValue(name, out string expected)) {
					Console.WriteLine($"WIRE not recorded yet: {{ \"{name}\", \"{got}\" }},");
					Expect(false, $"network bytes of \"{name}\" are recorded");
					continue;
				}
				Expect(got == expected, $"network bytes of \"{name}\" are the same as recorded ({got}, recorded {expected})");
			}
		}

		private static string Fingerprint(byte[] data) {
			using (var sha = SHA256.Create()) {
				return BitConverter.ToString(sha.ComputeHash(data), 0, 8).Replace("-", "");
			}
		}

		private static byte[] PieceBytes(BattleChunks.Piece piece) {
			var all = new List<byte>();
			all.AddRange(BitConverter.GetBytes(piece.MessageId));
			all.AddRange(BitConverter.GetBytes(piece.Index));
			all.AddRange(BitConverter.GetBytes(piece.Count));
			all.AddRange(piece.Data);
			return all.ToArray();
		}

		/// <summary>One message of each kind, with most of its fields filled in, built the same way every time.</summary>
		private static IEnumerable<(string, Func<byte[]>)> WireMessages() {
			Course course = MakeCourse(Hairpin(300, 5, 1.3f));
			CourseGate.TryMake(new Vector3(0, 0, 100), Vector3.forward, 10f, out CourseGate trap);
			Course trapped = course.WithTraps(new[] { trap });
			var regulation = new Regulation { Name = "Gunma touge", Author = "bskftw", MaxHp = 450f, MinWeight = 1100f, Setups = { "Drift" }, Cars = { 3, 7 } };
			regulation.Limits.Add(new ValueLimit { Key = "front_camber", Min = -5f, Max = 0f });
			regulation.Tyres.Add(new TyreBracket { UpToKg = 1200f, MaxWidth = 245f });
			var when = new DateTime(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);

			yield return ("battle challenge, course with path", () => new BattleMessage {
				Kind = BattleKind.Challenge, BattleId = 7, From = HostId, To = GuestId, FromName = "Host, \"the\" driver",
				Format = BattleFormat.CatAndMouse, Course = course, CourseCode = course.Code, Laps = 3, Open = true, MatchId = 4, MatchGame = 2,
			}.Encode());
			yield return ("battle challenge, speed traps", () => new BattleMessage {
				Kind = BattleKind.Challenge, BattleId = 8, From = HostId, To = GuestId, FromName = "bskftw", Format = BattleFormat.SpeedTraps,
				CourseCode = trapped.Code, Course = trapped, Traps = { trap },
			}.Encode());
			yield return ("battle challenge, regulation", () => new BattleMessage {
				Kind = BattleKind.Challenge, BattleId = 9, From = HostId, To = GuestId, FromName = "bskftw", Format = BattleFormat.Race,
				CourseCode = course.Code, Fun = true, Regulation = regulation, SingleFile = true,
			}.Encode());
			yield return ("battle answer", () => new BattleMessage { Kind = BattleKind.Answer, BattleId = 7, From = GuestId, To = HostId, Accept = true }.Encode());
			yield return ("battle heartbeat", () => new BattleMessage {
				Kind = BattleKind.Heartbeat, BattleId = 7, From = GuestId, To = HostId, SentAt = 123.25, Echo = 99.5, EchoHold = 0.01, Ready = true,
				Spec = true, TuneSet = "TRC V3.0", TuneCode = "TRC-91D4-D44C", Car = "Toyota AE86", Progress = 812.5f, RunIndex = 2, Phase = 4,
				Result = new RunResult { Run = 1, Finished = true, Time = 181.234, SpecAtEnd = true, TrapTotal = 455.5f },
				Position = new Vector3(1.5f, 2.25f, -3f), Heading = Vector3.forward, Kmh = 142.5f, Sp = 0.75f, TrapTotal = 300f,
				HasRuleOk = true, RuleOk = true, Elo = 1532.5f, CountdownId = 3, GoHostTime = 456.75,
			}.Encode());
			yield return ("battle countdown", () => new BattleMessage { Kind = BattleKind.Countdown, BattleId = 7, From = HostId, To = GuestId, CountdownId = 3, GoHostTime = 456.75 }.Encode());
			yield return ("battle abort", () => new BattleMessage { Kind = BattleKind.Abort, BattleId = 7, From = HostId, To = GuestId, Reason = "jump start" }.Encode());
			yield return ("battle leave", () => new BattleMessage { Kind = BattleKind.Leave, BattleId = 7, From = GuestId, To = HostId }.Encode());
			yield return ("battle seen", () => new BattleMessage { Kind = BattleKind.Seen, BattleId = 7, From = GuestId, To = HostId, Reason = "you're not on the course's map" }.Encode());

			var settings = new RaceSettings {
				Name = "Friday cup", MaxPlayers = 12, Laps = 3, Order = GridOrder.Random, PoleLeft = false, Jump = JumpRule.Disqualify, JumpPenaltySeconds = 3,
				Elimination = true, QualifyingMinutes = 5, QualifyingRuns = 2, SpeedTrapScoring = true, Fun = true, Regulation = regulation,
				HostDrives = false, LightStart = true, NoPenalties = true, SingleFile = true,
			};
			yield return ("race lobby", () => new RaceMessage {
				Kind = RaceKind.Lobby, RaceId = 9, From = HostId, RaceNumber = 2, HostName = "bskftw", Settings = settings, CourseCode = course.Code,
				Phase = RacePhase.Qualifying, Kicked = { 555UL },
				Entries = {
					new LobbyEntry { Id = HostId, Name = "bskftw", Slot = 0, Points = 25 },
					new LobbyEntry { Id = GuestId, Name = "Apok", Racer = true, Slot = 1, Role = RoomRole.Cop, Console = true, Platform = "PS5", EliminatedOnLap = 2 },
				},
			}.Encode());
			yield return ("race join", () => new RaceMessage { Kind = RaceKind.Join, RaceId = 9, From = GuestId, Name = "Apok" }.Encode());
			yield return ("race leave", () => new RaceMessage { Kind = RaceKind.Leave, RaceId = 9, From = GuestId }.Encode());
			yield return ("race course with traps", () => new RaceMessage { Kind = RaceKind.Course, RaceId = 9, From = HostId, Course = trapped }.Encode());
			yield return ("race beat", () => new RaceMessage {
				Kind = RaceKind.Beat, RaceId = 9, From = GuestId, SentAt = 77.5, Ready = true, Spec = true, TuneSet = "TRC Revival", Car = "Nissan S13",
				Progress = 1234.5f, LapsDone = 1, State = RacerState.Racing, Time = 61.25, Penalty = 5, BestLap = 59.5, BestQualifying = 58.25,
				QualifyingRuns = 2, PersonalBest = 57.75, Position = new Vector3(10f, 0.5f, 20f), Heading = Vector3.right, Speed = 120.5f, TrapTotal = 210f,
				Incidents = 1, LastIncident = "contact", Score = 3.5f, HasRuleOk = true, RuleOk = false, Elo = 1490f, JumpedCountdown = 2,
			}.Encode());
			yield return ("race host beat", () => new RaceMessage {
				Kind = RaceKind.HostBeat, RaceId = 9, From = HostId, SentAt = 78.25, Phase = RacePhase.Running, GoHostTime = 70.5, CountdownId = 4,
				EndAtHostTime = 900, QualifyingEndsHostTime = 300, Recalls = 1, RecallBy = "Apok", Zone = new Vector3(5f, 0f, 5f), ZoneRadius = 30f,
			}.Encode());
			yield return ("race results", () => new RaceMessage {
				Kind = RaceKind.Results, RaceId = 9, From = HostId, Winner = HostId, Lines = { "1. bskftw", "2. Apok" },
				Rows = {
					new RaceResultRow { Id = HostId, Name = "bskftw", Position = 1, State = RacerState.Finished, Time = 181.5, BestLap = 59.25, TrapTotal = 300f, Points = 25 },
					new RaceResultRow { Id = GuestId, Name = "Apok", Position = 2, State = RacerState.Disqualified, Penalty = 5, Reason = "jump start", Incidents = 2 },
				},
			}.Encode());
			yield return ("race match report", () => new RaceMessage {
				Kind = RaceKind.MatchReport, RaceId = 9, From = HostId, MatchId = 4, MatchA = HostId, MatchB = GuestId, MatchFormat = BattleFormat.CatAndMouse,
				MatchGame = 2, MatchLaps = 1, Winner = GuestId,
			}.Encode());

			yield return ("room status", () => new RoomMessage {
				Kind = RoomKind.Status, From = 76561198000000001UL, Name = "<color=red>Apok</color>", Version = "0.18.3", Activity = RoomActivity.RaceLobby,
				Detail = "Apok's race", UpForBattle = true, OpenRaceId = 1234567u, MapKey = "project-dream", Car = "Toyota AE86",
			}.Encode());
			yield return ("room course share", () => new RoomMessage { Kind = RoomKind.CourseShare, From = HostId, Name = "bskftw", Course = course, To = GuestId }.Encode());
			yield return ("room trap share", () => new RoomMessage {
				Kind = RoomKind.TrapShare, From = HostId, Name = "bskftw", Label = "Tsubaki", MapKey = "project-dream", MapName = "Project Dream",
				Traps = { new SpeedTrap { Name = "Tsubaki Speed Trap 1", Gate = trap, MapKey = "project-dream" } },
			}.Encode());
			yield return ("room regulation share", () => new RoomMessage { Kind = RoomKind.RegulationShare, From = HostId, Name = "bskftw", Regulation = regulation }.Encode());
			yield return ("room run share", () => {
				var ghost = new Ghost {
					CourseCode = course.Code, CourseName = "Akina Downhill", MapKey = "project-dream", DriverId = HostId, Driver = "bskftw", CarId = 3,
					Car = "Toyota AE86", Rules = "TRC Revival", Kind = "time attack", Name = "clean run", Time = 181.234, Sectors = { 60.5, 121.25 },
					TrapTotal = 300f, When = when,
				};
				for (int i = 0; i < 20; i++) {
					// A turn of i * 3 degrees around the up axis (Quaternion.Euler needs the game's engine, so it's built by hand).
					double half = i * 3 * Math.PI / 360;
					ghost.Record(i * Ghost.SampleSeconds, new Vector3(i, 0f, i * 2f), new Quaternion(0f, (float)Math.Sin(half), 0f, (float)Math.Cos(half)), 100f + i);
				}
				return new RoomMessage { Kind = RoomKind.RunShare, From = HostId, Name = "bskftw", To = GuestId, Ghost = ghost }.Encode();
			});
		}
	}
}
