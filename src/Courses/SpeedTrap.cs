// A speed trap (a line across the road where the speed is caught), its name groups, and a car going through one.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// A speed trap: a line across the road where the car's speed is measured as it goes through, like the speed
	/// cameras in Need for Speed. Free-roam traps stand on their own (map objects "TRC Speed Trap &lt;name&gt;", or
	/// placed by the player); courses can have their own along the way (Course.Traps), counted in a run.
	/// </summary>
	public sealed class SpeedTrap {
		public string Name = "";
		public CourseGate Gate = new CourseGate();
		public string MapKey = "";
		public CourseSource Source;

		/// <summary>The same everywhere for the same trap: the map and where it is (to the metre).</summary>
		public string Key => $"{MapKey}@{Mathf.RoundToInt(Gate.Center.x)},{Mathf.RoundToInt(Gate.Center.y)},{Mathf.RoundToInt(Gate.Center.z)}";

		/// <summary>The group it's listed under: "Tsubaki Speed Trap 3" is in "Tsubaki". "" when the name has nothing but a number.</summary>
		public string Group => GroupOf(Name);

		private static readonly Regex TrailingNumber = new Regex(@"[\s#\-_:]*\d+\s*$", RegexOptions.Compiled);
		private static readonly Regex TrailingTrap = new Regex(@"[\s\-_:]*(speed\s*traps?|traps?|speed\s*checks?)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

		public static string GroupOf(string name) {
			string rest = TrailingNumber.Replace((name ?? "").Trim(), "");
			rest = TrailingTrap.Replace(rest, "").Trim(' ', '-', '_', ':');
			return rest;
		}

		/// <summary>The number at the end of a trap's name ("Tsubaki Speed Trap 3" is 3), or 0.</summary>
		public static int NumberOf(string name) {
			Match m = Regex.Match(name ?? "", @"(\d+)\s*$");
			return m.Success && int.TryParse(m.Groups[1].Value, out int n) ? n : 0;
		}

		/// <summary>The name for the next trap after <paramref name="last"/>: "Tsubaki Speed Trap 4" after "... 3".</summary>
		public static string NextName(string last, int count) {
			string group = GroupOf(last);
			return group.Length > 0 ? $"{group} Speed Trap {NumberOf(last) + 1}" : $"Speed trap {count + 1}";
		}
	}

	/// <summary>A car going through a speed trap.</summary>
	public struct SpeedTrapHit {
		public SpeedTrap Trap;
		public float Kmh;
	}
}
