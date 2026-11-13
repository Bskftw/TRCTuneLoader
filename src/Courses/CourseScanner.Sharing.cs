// Map pieces named without a direction, shared by every course on that road; numbering copies of a speed trap.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using UnityEngine;

namespace TRC.TuneLoader {
	public static partial class CourseScanner {
		/// <summary>
		/// bskftw, Oct 30: only the start and finish should need "Downhill" or "Uphill"; the path and the sectors are the
		/// same road either way. A path or sectors named without a direction ("TRC Path Akina", "TRC Akina Sector 1")
		/// go to every course whose name starts with that name ("Akina Downhill", "Akina Uphill") and has none of its
		/// own; then they're not listed as a course of their own. The path is read from each course's start line,
		/// and sectors are put in order along each course's path, so both directions get them the right way round.
		/// </summary>
		private static void ShareUndirected(Dictionary<string, Parts> found) {
			List<string> undirected = found.Where(p => p.Value.Start == null && p.Value.Finish == null && p.Value.Grid.Count == 0 && p.Value.Traps.Count == 0 &&
				(p.Value.PathPieces.Count > 0 || p.Value.Sectors.Count > 0)).Select(p => p.Key).ToList();
			var used = new HashSet<string>(StringComparer.Ordinal);
			List<string> courseKeys = found.Where(p => p.Value.Start != null || p.Value.Finish != null).Select(p => p.Key).ToList();
			foreach (var pair in found.Where(p => p.Value.Start != null || p.Value.Finish != null)) {
				string key = SharedFor(pair.Key, undirected, courseKeys);
				if (key == null) {
					continue;
				}
				Parts shared = found[key];
				if (pair.Value.PathPieces.Count == 0 && shared.PathPieces.Count > 0) {
					pair.Value.PathPieces.AddRange(shared.PathPieces);
					used.Add(key);
				}
				if (pair.Value.Sectors.Count == 0 && shared.Sectors.Count > 0) {
					foreach (var sector in shared.Sectors) {
						pair.Value.Sectors[sector.Key] = sector.Value;
					}
					used.Add(key);
				}
			}
			foreach (string key in used) {
				found.Remove(key);
			}
		}

		/// <summary>
		/// Free-roam speed traps that are copies of one ("TRC Speed Trap Akina Bridge", ".001", ".003") are numbered in
		/// order, skipped numbers and all (bskftw, Nov 2: ".001 is 1 and .003 is 2"): the unnumbered one keeps the plain
		/// name, the copies after it are 2, 3...; without an unnumbered one, the first copy is 1.
		/// </summary>
		public static void NumberCopies(List<(string Name, int Copy, SpeedTrap Trap)> traps) {
			foreach (var group in traps.GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)) {
				var inOrder = group.OrderBy(t => t.Copy).ToList();
				if (inOrder.Count == 1 && inOrder[0].Copy == 0) {
					continue;
				}
				bool plainFirst = inOrder[0].Copy == 0;
				for (int i = 0; i < inOrder.Count; i++) {
					if (i == 0 && plainFirst) {
						continue;
					}
					inOrder[i].Trap.Name = $"{inOrder[i].Name} {i + 1}";
				}
			}
		}

		/// <summary>
		/// The undirected name whose pieces a course uses: the longest of <paramref name="keys"/> that the course's
		/// name starts with ("akina lake" before "akina" for "akina lake downhill"), or null.
		///
		/// A circuit and a downhill on the same road don't share a path (bskftw, Nov 2: Okutama has a downhill and two
		/// circuits, so "TRC Path Okutama" is the downhill's road, not the circuits'). So a circuit takes a name without
		/// "circuit" only when no course in <paramref name="courses"/> that isn't a circuit uses it too (Lake Akina, only a
		/// circuit: "TRC Path Lake Akina" is its loop); a course that isn't a circuit never takes one that says "circuit".
		/// </summary>
		public static string SharedFor(string courseKey, IEnumerable<string> keys, IEnumerable<string> courses = null) {
			bool circuit = HasWord(courseKey, CircuitWord);
			bool ForThisKind(string k) {
				if (HasWord(k, CircuitWord)) {
					return circuit;
				}
				return !circuit || courses == null ||
					!courses.Any(c => c != courseKey && !HasWord(c, CircuitWord) && c.StartsWith(k + " ", StringComparison.Ordinal));
			}
			return keys.Where(k => k != courseKey && courseKey.StartsWith(k + " ", StringComparison.Ordinal) && ForThisKind(k))
				.OrderByDescending(k => k.Length)
				.FirstOrDefault();
		}

		private static bool HasWord(string key, string word) => (" " + key + " ").IndexOf(" " + word + " ", StringComparison.Ordinal) >= 0;
	}
}
