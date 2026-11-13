// The kinds of course (touge, highway, circuit), grid layouts, a grid spot, and where a course came from.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>Touge and highway run from a start line to a finish line; a circuit is a loop whose finish line is its start line.</summary>
	public enum CourseKind { Touge, Highway, Circuit }

	/// <summary>How cars line up for a race: two-wide and staggered, or one behind the other.</summary>
	public enum GridLayout : byte { Grid, LineUp }

	/// <summary>Where one car lines up for a race, and the way it faces.</summary>
	public struct GridSpot {
		public Vector3 Position;
		public Vector3 Forward;
	}

	/// <summary>
	/// Where a course comes from. Official courses are built into the mod (only bskftw ships them);
	/// map courses come from TRC-named objects in the map itself; custom courses were made with the
	/// placement tool on this PC and only count for practice.
	/// </summary>
	public enum CourseSource { Official, Map, Custom }
}
