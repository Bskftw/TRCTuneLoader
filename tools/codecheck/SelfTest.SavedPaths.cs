using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TRC.TuneLoader.CodeCheck {
	/// <summary>Remembered course paths (IMPROVEMENT_PLAN.md P1): exact round trip, stale paths dropped, damaged files ignored.</summary>
	internal static partial class SelfTest {
		private static void SavedPathsTests() {
			string folder = Path.Combine(Path.GetTempPath(), "trc-paths-" + Guid.NewGuid().ToString("N"));
			string file = Path.Combine(folder, "project-dream", SavedPaths.FileName);
			try {
				// Points with awkward values: they must come back bit for bit, or a course's code could change.
				var akina = new List<Vector3> { new Vector3(-0f, 1e-7f, 123456.789f), new Vector3(float.Epsilon, -3.4e38f, 0.1f), new Vector3(1.2f, 2.3f, -4.5f) };
				var usui = new List<Vector3> { new Vector3(10f, 0f, 0f), new Vector3(20f, 0.5f, 5f) };
				var first = new SavedPaths("0.18.3");
				Expect(!first.TryGet("akina", out _) && first.Misses == 1, "an empty set has nothing");
				first.Put("akina", akina, new List<string> { "3 of the path's points weren't used" });
				first.Put("usui", usui, new List<string>());
				Expect(first.Save(file, out string error) && error == "" && File.Exists(file), $"new paths are saved ({error})");

				SavedPaths second = SavedPaths.Load(file, "0.18.3", out error);
				Expect(error == "" && second.ModVersion == "0.18.3", "the file reads back");
				Expect(second.TryGet("akina", out SavedPaths.Entry entry) && entry.Path.Count == 3 && entry.Problems.Count == 1 &&
					entry.Problems[0] == "3 of the path's points weren't used", "a path comes back with its problems");
				bool exact = true;
				for (int i = 0; i < akina.Count; i++) {
					exact &= BitConverter.ToInt32(BitConverter.GetBytes(entry.Path[i].x), 0) == BitConverter.ToInt32(BitConverter.GetBytes(akina[i].x), 0)
						&& BitConverter.ToInt32(BitConverter.GetBytes(entry.Path[i].y), 0) == BitConverter.ToInt32(BitConverter.GetBytes(akina[i].y), 0)
						&& BitConverter.ToInt32(BitConverter.GetBytes(entry.Path[i].z), 0) == BitConverter.ToInt32(BitConverter.GetBytes(akina[i].z), 0);
				}
				Expect(exact, "every point comes back with exactly the same bits (-0 and the tiniest float too)");
				entry.Path.Clear();
				Expect(second.TryGet("akina", out entry) && entry.Path.Count == 3, "what's handed out is a copy: changing it changes nothing saved");
				Expect(second.Hits == 2 && second.Misses == 0, "hits are counted");

				// Only Akina was used this time (Usui is gone from the map): saving drops it.
				Expect(second.Save(file, out error), "a path that wasn't used makes it save");
				SavedPaths third = SavedPaths.Load(file, "0.18.3", out error);
				Expect(third.TryGet("akina", out _) && !third.TryGet("usui", out _), "the unused path dropped out");
				Expect(!third.Save(file, out error) && error == "", "nothing new and nothing dropped: nothing is written");

				// A damaged or foreign file is ignored, so every path is read from the map again.
				File.WriteAllBytes(file, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
				SavedPaths damaged = SavedPaths.Load(file, "0.18.3", out error);
				Expect(error != "" && !damaged.TryGet("akina", out _), "a damaged file gives no paths and says why");
				SavedPaths missing = SavedPaths.Load(Path.Combine(folder, "nothing", SavedPaths.FileName), "0.18.3", out error);
				Expect(error == "" && !missing.TryGet("akina", out _), "no file is no paths, without an error");
			} finally {
				try {
					Directory.Delete(folder, true);
				} catch (IOException) {
				}
			}
		}
	}
}
