// A finished lap of a run (time, sectors, whether it counts, trace for the live delta), and a sector just finished.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>One lap of a run (a point-to-point run is one lap): its time, its sectors, and whether it counts.</summary>
	public sealed class LapTime {
		/// <summary>1 for the first lap.</summary>
		public int Number;
		public double Time;

		/// <summary>Seconds for each sector of the lap, in order.</summary>
		public double[] Sectors = new double[0];

		/// <summary>False when the lap was spoiled (practice only: a shortcut, leaving the road...), with why.</summary>
		public bool Valid = true;
		public string Invalid = "";

		/// <summary>The lap began at a standing start (the first lap of a standing run), so it's slower than a flying lap.</summary>
		public bool Standing;

		/// <summary>Lap time at every <see cref="TimeAttackRun.TraceStep"/> metres along the lap: what the live delta compares with.</summary>
		public float[] Trace = new float[0];

		/// <summary>Where the car was, every <see cref="TimeAttackRun.LineStep"/> m of the lap, and its speed there (km/h): the driving line.</summary>
		public List<Vector3> Line = new List<Vector3>();
		public List<float> LineKmh = new List<float>();
	}

	/// <summary>A sector just finished, to flash on screen.</summary>
	public struct SectorSplit {
		/// <summary>0 for the first sector.</summary>
		public int Index;
		public int Lap;

		/// <summary>Seconds for the sector itself.</summary>
		public double Time;

		/// <summary>The lap so far against the <see cref="TimeAttackRun.Reference"/> lap at the same point: negative ahead, positive behind; null without one.</summary>
		public double? Delta;

		/// <summary>The same against <see cref="TimeAttackRun.CarReference"/> (your best in this car), or null.</summary>
		public double? CarDelta;
	}
}
