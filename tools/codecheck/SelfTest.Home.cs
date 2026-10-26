using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace TRC.TuneLoader.CodeCheck {
	/// <summary>The room board and shared courses, the server event ledger, and the bug report.</summary>
	internal static partial class SelfTest {
		private static void HomeTests() {
			RoomStatusesTravel();
			SharedCoursesTravel();
			RoomBoardSendsWhenItShould();
			LedgerChains();
			BugReportsAreWritten();
		}

		private static void RoomStatusesTravel() {
			var status = new RoomMessage {
				Kind = RoomKind.Status, From = 76561198000000001UL, Name = "<color=red>Apok</color>", Version = "0.14.0",
				Activity = RoomActivity.RaceLobby, Detail = "Apok's race", UpForBattle = true, OpenRaceId = 1234567u, MapKey = "project-dream", Car = "Toyota AE86",
			};
			RoomMessage back = RoomMessage.Decode(status.Encode());
			Expect(back != null && back.Kind == RoomKind.Status && back.From == status.From && back.Name == status.Name && back.Version == "0.14.0" &&
				back.Activity == RoomActivity.RaceLobby && back.Detail == "Apok's race" && back.UpForBattle && back.OpenRaceId == 1234567u &&
				back.MapKey == "project-dream" && back.Car == "Toyota AE86", "a room status comes back the same after encoding");
			Expect(RoomBoard.ActivityText(back) == "Waiting in Apok's race" && !RoomBoard.Free(back), "a race lobby reads as waiting, not free to battle");
			Expect(RoomBoard.Free(new RoomMessage { Activity = RoomActivity.FreeRoam }) && !RoomBoard.Free(new RoomMessage { Activity = RoomActivity.Battle }), "free roam is free, a battle isn't");

			byte[] data = status.Encode();
			Expect(RoomMessage.Decode(data.Take(data.Length - 3).ToArray()) == null, "a cut-off room message is ignored");
			data[0] = 99;
			Expect(RoomMessage.Decode(data) == null, "a room message of a kind this version doesn't know is ignored");
			Expect(RoomMessage.Decode(new RoomMessage { Kind = RoomKind.Status, Detail = new string('x', 500) }.Encode()).Detail.Length == 80, "long text is clipped");
		}

		private static void SharedCoursesTravel() {
			Course course = MakeCourse(Hairpin(300f, 30f, 2f));
			course.Name = "Shared hairpin";
			course.MapName = "Test map";
			CourseGate.TryMake(course.Path[40], course.Path[41] - course.Path[40], 12f, out CourseGate trap);
			course.Traps.Add(trap);
			course.GridPath = Enumerable.Range(0, 30).Select(i => new Vector3(0, 0, -i * 2f)).ToList();
			course.GridLayout = GridLayout.LineUp;
			var share = new RoomMessage { Kind = RoomKind.CourseShare, From = 7, Name = "bskftw", Course = course };
			byte[] data = share.Encode();
			RoomMessage back = RoomMessage.Decode(data);
			Expect(back != null && back.Course != null && back.Course.Code == course.Code && back.Course.Name == "Shared hairpin" && back.Course.Path.Count == course.Path.Count,
				$"a shared course arrives with the same course code ({course.Code}, {data.Length} bytes)");
			Expect(back.Course.Traps.Count == 1 && Vector3.Distance(back.Course.Traps[0].Center, trap.Center) < 0.01f &&
				back.Course.GridPath.Count == 30 && back.Course.GridLayout == GridLayout.LineUp, "its speed traps and grid come with it");

			var board = new RoomBoard();
			RoomBoard.Shared got = board.Receive(back, 100);
			Expect(got != null && got.FromName == "bskftw" && board.SharedCourses.Count == 1, "a shared course goes on the board");
			Expect(board.Receive(back, 105) == null, $"the same sender can't share again within {RoomBoard.ShareGapSeconds:0} s");
			Expect(board.Receive(back, 120) != null && board.SharedCourses.Count == 1, "sharing the same course again doesn't list it twice");
			for (int i = 0; i < 15; i++) {
				Course other = MakeCourse(Hairpin(200f + i * 10f, 25f, 2f));
				board.Receive(new RoomMessage { Kind = RoomKind.CourseShare, From = (ulong)(100 + i), Name = $"p{i}", Course = other }, 200 + i);
			}
			Expect(board.SharedCourses.Count == RoomBoard.MaxShared && board.SharedCourses[0].FromName == "p14", $"the board keeps the newest {RoomBoard.MaxShared} shared courses");
		}

		private static void RoomBoardSendsWhenItShould() {
			var board = new RoomBoard();
			var mine = new RoomMessage { Kind = RoomKind.Status, From = 1, Activity = RoomActivity.FreeRoam };
			Expect(board.ShouldSend(mine, 0), "the first status goes out at once");
			Expect(!board.ShouldSend(mine, 5), "the same status isn't sent again straight away");
			mine.Activity = RoomActivity.TimeAttack;
			Expect(!board.ShouldSend(mine, 1), $"not more than once every {RoomBoard.MinGapSeconds:0} s");
			Expect(board.ShouldSend(mine, 6), "a changed status goes out");
			Expect(!board.ShouldSend(mine, 15) && board.ShouldSend(mine, 6 + RoomBoard.KeepAliveSeconds), $"an unchanged status goes out every {RoomBoard.KeepAliveSeconds:0} s");
			board.Receive(new RoomMessage { Kind = RoomKind.Status, From = 2, Name = "Newcomer", Activity = RoomActivity.Garage }, 25);
			Expect(board.ShouldSend(mine, 25), "someone new in the room gets our status straight away");
			Expect(board.Fresh(30).Count == 1 && board.Fresh(25 + RoomBoard.StaleSeconds + 1).Count == 0, "players not heard from for a while drop off the board");
			Expect(board.StatusOf(2, 30)?.Name == "Newcomer" && board.StatusOf(3, 30) == null, "a player's status can be looked up");
		}

		private static void LedgerChains() {
			string folder = Path.Combine(Path.GetTempPath(), "trc-ledger-" + Guid.NewGuid().ToString("N"));
			try {
				var ledger = new EventLedger(folder);
				var run = new TimeAttackRecord {
					CourseCode = "CRS-1111-2222", CourseName = "Akina \"Downhill\"\\test", MapKey = "project-dream", CarId = 12, Car = "AE86", TuneCode = "TRC-95F4-06D9",
					TuneSet = "TRC Revival", Spec = TimeAttackLog.SpecWord, TimeMs = 123456, SectorMs = new long[] { 40000, 41000, 42456 }, TrapKmh = new[] { 151.25f, 160f },
				};
				var when = new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
				string first = ledger.Append("run", 42, "0.14.0", when, EventLedger.RunFields(run));
				ledger.Append("lap", 42, "0.14.0", when.AddMinutes(1), EventLedger.RunFields(run));
				ledger.Append("custom", 42, "0.14.0", when.AddMinutes(2), new List<EventLedger.Field> {
					new EventLedger.Field("note", "line\nbreak"), new EventLedger.Field("ok", true), new EventLedger.Field("x", 1.23456),
				});
				Expect(first.StartsWith("{\"v\":1,\"seq\":1,", StringComparison.Ordinal) && first.Contains("\"time_ms\":123456") &&
					first.Contains("\"sectors_ms\":[40000,41000,42456]") && first.Contains("\"traps_kmh\":[151.25,160]") && first.Contains("\"prev\":\"\""),
					"a run becomes one JSON line with its time, sectors and trap speeds");
				Expect(first.Contains("\"course_name\":\"Akina \\\"Downhill\\\"\\\\test\""), "quotes and backslashes in names are escaped");
				Expect(first.Contains("\"at\":\"2026-10-26T12:00:00.000Z\"") && first.Contains("\"steam\":\"42\""), "the time is UTC and the Steam ID is text");

				string path = Path.Combine(folder, EventLedger.FolderName, EventLedger.FileName);
				List<string> lines = File.ReadAllLines(path, Encoding.UTF8).ToList();
				Expect(lines.Count == 3 && EventLedger.Verify(lines) == "", "three events make a good chain");
				Expect(lines[2].Contains("\"note\":\"line\\nbreak\"") && lines[2].Contains("\"x\":1.235"), "line breaks are escaped and numbers rounded to 3 places");

				var reopened = new EventLedger(folder);
				Expect(reopened.Count == 3 && reopened.Head == ledger.Head, "the ledger picks up where the file ends after a restart");
				reopened.Append("run", 42, "0.14.0", when.AddMinutes(3), EventLedger.RunFields(run));
				Expect(EventLedger.Verify(File.ReadLines(path, Encoding.UTF8)) == "", "the chain goes on after a restart");

				var changed = File.ReadAllLines(path, Encoding.UTF8).ToList();
				changed[0] = changed[0].Replace("123456", "103456");
				Expect(EventLedger.Verify(changed).StartsWith("line 1: changed", StringComparison.Ordinal), "a changed time is caught");
				var dropped = File.ReadAllLines(path, Encoding.UTF8).ToList();
				dropped.RemoveAt(1);
				Expect(EventLedger.Verify(dropped).StartsWith("line 2: chain broken", StringComparison.Ordinal), "a removed line is caught");
				Expect(EventLedger.Verify(new string[0]) == "", "an empty ledger is fine");
			} finally {
				try {
					Directory.Delete(folder, true);
				} catch (Exception) {
					// Temp files.
				}
			}
		}

		private static void BugReportsAreWritten() {
			string folder = Path.Combine(Path.GetTempPath(), "trc-bug-" + Guid.NewGuid().ToString("N"));
			try {
				Directory.CreateDirectory(folder);
				string log = Path.Combine(folder, "test.log");
				var big = new StringBuilder();
				while (big.Length < BugReport.MaxLogBytes + 100000) {
					big.Append("an old line nobody needs\n");
				}
				big.Append("THE END");
				File.WriteAllText(log, big.ToString());
				BugReport.Part part;
				// The log is still open for writing, like the mod's and KSL's are.
				using (new FileStream(log, FileMode.Open, FileAccess.Write, FileShare.ReadWrite)) {
					part = BugReport.LogPart("test.log", log);
				}
				Expect(part != null && part.Text.EndsWith("THE END", StringComparison.Ordinal) && part.Text.Length < BugReport.MaxLogBytes + 1000 && part.Text.StartsWith("[only the last", StringComparison.Ordinal),
					"a big log keeps its end, even while it's open");
				Expect(BugReport.LogPart("none.log", Path.Combine(folder, "none.log")) == null, "a log that isn't there is left out");

				var when = new DateTime(2026, 10, 26, 15, 30, 0);
				var parts = new List<BugReport.Part> { new BugReport.Part { Name = "what-happened.txt", Text = "The race didn't start" }, part, null };
				string zip = BugReport.Write(folder, when, parts);
				Expect(Path.GetFileName(zip) == "TRC-bug-report-2026-10-26-1530.zip", $"the report is a zip named for the time ({Path.GetFileName(zip)})");
				Dictionary<string, string> back = BugReport.Read(zip);
				Expect(back.Count == 2 && back["what-happened.txt"] == "The race didn't start" && back["test.log"].EndsWith("THE END", StringComparison.Ordinal), "the zip has every part");
				string second = BugReport.Write(folder, when, parts);
				Expect(Path.GetFileName(second) == "TRC-bug-report-2026-10-26-1530 (2).zip", "a second report in the same minute doesn't replace the first");
			} finally {
				try {
					Directory.Delete(folder, true);
				} catch (Exception) {
					// Temp files.
				}
			}
		}
	}
}
