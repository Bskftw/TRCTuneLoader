using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace TRC.TuneLoader.CodeCheck {
	/// <summary>0.15.2: the ratings file is sealed, tied to the SteamID, and starts again when it was changed; speed traps can be shared.</summary>
	internal static partial class SelfTest {
		private const ulong SealOwner = 76561198000000001;
		private const ulong SealOther = 76561198000000002;

		/// <summary>bskftw, Oct 30: "TRC Path Akina" and "TRC Akina Sector 1" serve every "Akina ..." course; only start and finish need the direction.</summary>
		private static void UndirectedPiecesMatch() {
			var keys = new[] { "akina", "akina lake", "tsukuba" };
			Expect(CourseScanner.SharedFor("akina downhill", keys) == "akina" && CourseScanner.SharedFor("akina uphill", keys) == "akina",
				"a path named without the direction goes to both directions");
			Expect(CourseScanner.SharedFor("akina lake downhill", keys) == "akina lake", "the longest matching name wins (Akina Lake before Akina)");
			Expect(CourseScanner.SharedFor("akinasan downhill", keys) == null && CourseScanner.SharedFor("akina", keys) == null,
				"only whole words match, and a name doesn't match itself");
		}

		/// <summary>
		/// bskftw, Oct 30: a map's path that runs on past the start line gives the race grid its run-up (no grid objects),
		/// and past the finish, the uphill twin's. The course itself still runs from the start line to the finish.
		/// </summary>
		private static void PathRunsPastTheLines() {
			// A strip 0.3 m tall (extruded up) down a straight road from z = -60 to z = 560; start at 0, finish at 500.
			var vertices = new List<Vector3>();
			for (float z = -60; z <= 560; z += 3f) {
				vertices.Add(new Vector3(0, 0, z));
				vertices.Add(new Vector3(0, 0.3f, z));
			}
			CourseGate.TryMake(new Vector3(0, 0, 0), Vector3.forward, 12f, out CourseGate start);
			CourseGate.TryMake(new Vector3(0, 0, 500), Vector3.forward, 12f, out CourseGate finish);
			List<Vector3> ordered = CourseGeometry.OrderPoints(vertices, start.Center, start.Forward, 1.2f, 25f, out int unused, out List<Vector3> behind);
			Expect(unused == 0 && behind.Count > 10 && behind.Last().z < -55f, $"a strip extruded upwards is read both ways from the start line ({unused} unused, {behind.Count} behind)");

			var line = Enumerable.Range(0, 63).Select(i => new Vector3(0, 0, -60 + i * 10f)).ToList();
			List<Vector3> course = CourseGeometry.SplitAtLines(line, start, finish, out List<Vector3> runUp, out List<Vector3> runOut);
			Expect(Math.Abs(course.First().z) < 0.01f && Math.Abs(course.Last().z - 500f) < 0.01f, "the course still runs from the start line to the finish line");
			Expect(runUp.Count >= 2 && Math.Abs(runUp.First().z) < 0.01f && runUp.Last().z < -55f, "the path before the start line is the grid run-up, from the line backwards");
			Expect(runOut.Count >= 2 && Math.Abs(runOut.First().z - 500f) < 0.01f && runOut.Last().z > 555f, "the path past the finish line is kept for the reverse direction");

			List<Vector3> plain = CourseGeometry.SplitAtLines(line.Where(p => p.z >= 0 && p.z <= 500).ToList(), start, finish, out List<Vector3> noUp, out List<Vector3> noOut);
			Expect(noUp.Count == 0 && noOut.Count == 0 && plain.Count == 51, "a path that stops at the lines works as before");

			var map = new Course { Name = "Akina Downhill", MapKey = "test:1", Source = CourseSource.Map, Start = start, Finish = finish, HasPath = true, Path = course, GridPath = runUp, RunOut = runOut };
			map.Build(resample: false);
			Course uphill = map.ReversedAs("Akina Uphill");
			List<GridSpot> grid = uphill.GridSpots(4, poleLeft: true, GridLayout.LineUp);
			Expect(grid.All(g => g.Position.z > 500f) && grid.All(g => g.Forward.z < -0.9f) && grid[3].Position.z > grid[0].Position.z,
				$"the uphill twin lines up past the old finish, facing uphill ({string.Join(", ", grid.Select(g => g.Position.z.ToString("0")))})");
		}

		/// <summary>
		/// bskftw, Oct 30: a path may be the road's centre faces duplicated (flat, any width) or a line extruded up or down;
		/// and "TRC Path Akina.001" is the second piece of "TRC Path Akina".
		/// </summary>
		private static void PathFromFacesAndCopies() {
			CourseGate.TryMake(new Vector3(0, 0, 0), Vector3.forward, 12f, out CourseGate start);
			// A flat strip of quads 4 m wide down a gentle S-bend, two triangles each, vertices split at every quad like Unity does at seams.
			foreach (float width in new[] { 4f, 0.3f }) {
				var vertices = new List<Vector3>();
				var triangles = new List<int>();
				for (int i = 0; i < 60; i++) {
					Vector3 Centre(int k) => new Vector3(Mathf.Sin(k * 0.05f) * 20f, 0, k * 6f);
					Vector3 Across(int k) => Vector3.Cross(Vector3.up, (Centre(k + 1) - Centre(k - 1)).normalized) * (width / 2f);
					Vector3 a = Centre(i), b = Centre(i + 1);
					int at = vertices.Count;
					vertices.AddRange(new[] { a - Across(i), a + Across(i), b + Across(i + 1), b - Across(i + 1) });
					triangles.AddRange(new[] { at, at + 1, at + 2, at, at + 2, at + 3 });
				}
				List<Vector3> centre = CourseGeometry.StripCentre(vertices, triangles);
				float worst = centre.Max(p => Mathf.Abs(p.x - Mathf.Sin(p.z / 6f * 0.05f) * 20f));
				List<Vector3> ordered = CourseGeometry.OrderPoints(centre, start.Center, start.Forward, 1.2f, 25f, out int unused);
				Expect(centre.Count > 100 && worst < 0.2f && unused == 0 && ordered.Count > 100 && ordered.Last().z > 340f,
					$"a flat strip of faces {width} m wide gives the road's centre line ({centre.Count} points, {worst:0.00} m off at worst, {unused} unused)");
			}
			// Standing up: a line extruded 0.3 m upwards.
			var fence = new List<Vector3>();
			var fenceTriangles = new List<int>();
			for (int i = 0; i <= 40; i++) {
				fence.Add(new Vector3(0, 0, i * 5f));
				fence.Add(new Vector3(0, 0.3f, i * 5f));
				if (i > 0) {
					int at = fence.Count - 4;
					fenceTriangles.AddRange(new[] { at, at + 1, at + 3, at, at + 3, at + 2 });
				}
			}
			List<Vector3> middle = CourseGeometry.StripCentre(fence, fenceTriangles);
			Expect(middle.Count >= 79 && middle.All(p => Mathf.Abs(p.x) < 0.01f && Mathf.Abs(p.y - 0.15f) < 0.01f), $"a line extruded upwards gives its centre line ({middle.Count} points)");
			Expect(CourseGeometry.StripCentre(fence, new List<int>()).Count == 0, "a mesh with no faces gives nothing, so its vertices are used instead");

			Expect(CourseScanner.WithoutCopyNumber("TRC Path Akina.001", out int one) == "TRC Path Akina" && one == 1 &&
				CourseScanner.WithoutCopyNumber("TRC Mainroad Akina (2)", out int two) == "TRC Mainroad Akina" && two == 2 &&
				CourseScanner.WithoutCopyNumber("TRC Path Akina", out int none) == "TRC Path Akina" && none == 0,
				"'.001' (Blender) and ' (1)' (Unity) are copy numbers; without one it's the first");
			Expect(CourseScanner.WithoutCopyNumber("TRC Akina Sector 2", out int sector) == "TRC Akina Sector 2" && sector == 0 &&
				CourseScanner.WithoutCopyNumber("TRC Start Akina(3)", out int glued) == "TRC Start Akina(3)" && glued == 0,
				"sector and grid numbers aren't copy numbers");
		}

		private static void TrapSharingTests() {
			SpeedTrap Trap(string name, float z) => new SpeedTrap { Name = name, MapKey = "workshop:1", Source = CourseSource.Custom,
				Gate = new CourseGate { Center = new Vector3(5, 1, z), Forward = Vector3.forward, Width = 10f } };
			var m = new RoomMessage { Kind = RoomKind.TrapShare, From = SealOther, Name = "<color=red>Frog</color>", To = SealOwner, MapKey = "workshop:1", MapName = "Project Dream", Label = "Tsubaki" };
			m.Traps.Add(Trap("Tsubaki Speed Trap 1", 100));
			m.Traps.Add(Trap("Tsubaki Speed Trap 2", 300));
			RoomMessage back = RoomMessage.Decode(m.Encode());
			Expect(back != null && back.Kind == RoomKind.TrapShare && back.To == SealOwner && back.Traps.Count == 2 && back.Traps[1].Name == "Tsubaki Speed Trap 2" &&
				Vector3.Distance(back.Traps[1].Gate.Center, new Vector3(5, 1, 300)) < 0.01f && Math.Abs(back.Traps[0].Gate.Width - 10f) < 0.01f,
				"shared speed traps arrive with their names, places and widths");

			var board = new RoomBoard();
			Expect(board.ReceiveTraps(back, 0, SealOther + 5) == null, "traps shared with one player don't show for anyone else");
			RoomBoard.SharedTraps shared = board.ReceiveTraps(back, 0, SealOwner);
			Expect(shared != null && shared.JustForMe && shared.FromName == "Frog" && board.SharedTrapSets.Count == 1, "traps shared with you wait in your list");
			var everyone = RoomMessage.Decode(new RoomMessage { Kind = RoomKind.TrapShare, From = SealOther, Name = "Frog", MapKey = "workshop:1", Label = "All their traps", Traps = m.Traps }.Encode());
			Expect(board.ReceiveTraps(everyone, 3, SealOwner) == null, "one player can't flood the list: shares come at most every few seconds");
			Expect(board.ReceiveTraps(everyone, 20, SealOwner + 9) != null, "traps shared with the room show for everyone");

			var mine = new List<SpeedTrap> { Trap("My trap", 101) };
			int added = RoomBoard.MergeTraps(mine, shared.Traps);
			Expect(added == 1 && mine.Count == 2 && mine[1].Name == "Tsubaki Speed Trap 2", "adding shared traps skips one sitting where you already have a trap");
		}

		private static void RatingSealTests() {
			string folder = Path.Combine(Path.GetTempPath(), "trc_seal_" + Guid.NewGuid().ToString("N"));
			try {
				var book = new RatingBook { License = License.B, Safety = 3.4 };
				book.Elo[0] = 1620;
				book.Save(folder, SealOwner);
				string path = Path.Combine(folder, RatingBook.FileFor(SealOwner));
				string sealedText = File.ReadAllText(path);
				Expect(!sealedText.Contains("1620") && !sealedText.Contains("elo") && sealedText.StartsWith(RatingSeal.Header, StringComparison.Ordinal),
					"the ratings file is scrambled: Notepad shows no Elo or license");
				RatingBook back = RatingBook.Load(folder, SealOwner);
				Expect(!back.WasChanged && Math.Abs(back.Elo[0] - 1620) < 0.01 && back.License == License.B, "a sealed ratings file loads back for its owner");

				// One character changed, as Notepad would.
				char[] chars = sealedText.ToCharArray();
				int at = RatingSeal.Header.Length + 10;
				chars[at] = chars[at] == 'A' ? 'B' : 'A';
				File.WriteAllText(path, new string(chars));
				RatingBook edited = RatingBook.Load(folder, SealOwner);
				Expect(edited.WasChanged && Math.Abs(edited.Elo[0] - RatingBook.StartElo) < 0.01 && edited.License == License.Rookie &&
					File.Exists(Path.Combine(folder, RatingBook.ChangedFileFor(SealOwner))),
					"a changed ratings file starts again from the starting values, and the changed file is kept aside");
				Expect(!RatingBook.Load(folder, SealOwner).WasChanged, "after starting again, the new file is sealed and loads normally");

				// Someone else's file, renamed to yours.
				book.Save(folder, SealOther);
				File.Copy(Path.Combine(folder, RatingBook.FileFor(SealOther)), path, true);
				RatingBook copied = RatingBook.Load(folder, SealOwner);
				Expect(copied.WasChanged && Math.Abs(copied.Elo[0] - RatingBook.StartElo) < 0.01, "another player's ratings file doesn't work for you");

				// Up to 0.15.1 the file was plain text: carried over once, sealed, removed.
				string fresh = Path.Combine(folder, "old");
				Directory.CreateDirectory(fresh);
				File.WriteAllText(Path.Combine(fresh, RatingBook.OldFileName), "version = 2\nelo_touge = 1555\nsafety = 3.1\nlicense = C\nevents = 9\n", new UTF8Encoding(false));
				RatingBook moved = RatingBook.Load(fresh, SealOwner);
				Expect(!moved.WasChanged && Math.Abs(moved.Elo[0] - 1555) < 0.01 && moved.License == License.C &&
					!File.Exists(Path.Combine(fresh, RatingBook.OldFileName)) && File.Exists(Path.Combine(fresh, RatingBook.FileFor(SealOwner))),
					"a 0.15.1 ratings file is carried over once, then sealed and the plain one removed");
			} finally {
				try {
					Directory.Delete(folder, true);
				} catch (Exception) {
				}
			}
		}
	}
}
