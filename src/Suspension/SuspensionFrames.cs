// What suspension logging keeps: one wheel at one moment, a frame of all four, a corner's setup, an event, a wheel's report.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>One wheel at one moment: how far the suspension is compressed, the tyre's load and whether it touches the road.</summary>
	public struct WheelFrame {
		/// <summary>0 fully extended (drooped), 1 fully compressed.</summary>
		public float Compression;

		/// <summary>Vertical load on the tyre (N).</summary>
		public float Load;
		public bool Contact;
		public float SlipAngle;
		public float SlipRatio;
	}

	/// <summary>All four wheels at one moment (front left, front right, rear left, rear right, CarX's order).</summary>
	public sealed class SuspensionFrame {
		public double Time;
		public Vector3 Position;
		public float Kmh;
		public WheelFrame[] Wheels = new WheelFrame[4];
	}

	/// <summary>One corner's suspension from the tune, as CarX built it.</summary>
	public sealed class CornerSetup {
		/// <summary>Full spring travel (m).</summary>
		public float Travel;

		/// <summary>Spring rate (N/m) and the four damper rates (N per m/s, if CarX's units are real ones).</summary>
		public float Spring;
		public float SlowBump;
		public float FastBump;
		public float SlowRebound;
		public float FastRebound;

		/// <summary>Damper speeds (m/s) where slow damping ends and fast damping starts, for bump and rebound.</summary>
		public float BumpSlowLimit = 0.1f;
		public float BumpFastLimit = 1f;
		public float ReboundSlowLimit = 0.1f;
		public float ReboundFastLimit = 1f;

		/// <summary>The car's weight on this corner (kg), from the weight and its balance.</summary>
		public float CornerMass;

		/// <summary>The spring's natural frequency (Hz): how fast the corner bounces on its spring.</summary>
		public float Frequency => Spring > 0 && CornerMass > 0 ? Mathf.Sqrt(Spring / CornerMass) / (2f * Mathf.PI) : 0f;

		/// <summary>A damper rate as a share of critical damping (0.7 settles fast without bouncing; 1 never overshoots).</summary>
		public float Ratio(float damping) => Spring > 0 && CornerMass > 0 ? damping / (2f * Mathf.Sqrt(Spring * CornerMass)) : 0f;
	}

	/// <summary>Something that happened to a wheel, where it happened.</summary>
	public sealed class SuspensionEvent {
		public enum Kind { ContactLost, Packing, Bottomed, LoadSpike }

		public Kind What;
		public int Wheel;
		public double Time;
		public Vector3 Position;
		public float Kmh;

		/// <summary>Contact lost: right after a hard compression. Packing: compression from and to.</summary>
		public bool AfterCompression;
		public float From;
		public float To;

		/// <summary>Contact lost: how long the tyre was off the road (s).</summary>
		public double Seconds;

		/// <summary>Where on the course (m along it), when a course was being driven; -1 otherwise.</summary>
		public float Along = -1f;
	}

	/// <summary>One wheel's numbers over a session or a run.</summary>
	public sealed class WheelReport {
		/// <summary>Seconds at each damper speed: bins of <see cref="SuspensionLog.BinMmS"/> mm/s from -<see cref="SuspensionLog.MaxMmS"/> (rebound) to +MaxMmS (bump); the ends hold everything faster.</summary>
		public double[] Histogram = new double[SuspensionLog.Bins];

		/// <summary>Seconds at each 5% of travel (0 extended to 100 compressed).</summary>
		public double[] TravelTime = new double[20];

		public float MinCompression = 1f;
		public float MaxCompression;
		public double Seconds;
		public double BumpSeconds;
		public double ReboundSeconds;
		public double FastBumpSeconds;
		public double FastReboundSeconds;
		public float FastestBumpMmS;
		public float FastestReboundMmS;
		public int ContactLost;
		public int ContactLostAfterCompression;
		public int Bottomed;
		public int Packing;
		public int LoadSpikes;
		public float MeanLoad;
		public float PeakLoad;

		/// <summary>How much of the time the damper moved in bump rather than rebound (beyond the slow range), 0 to 1.</summary>
		public float BumpShare => BumpSeconds + ReboundSeconds > 0 ? (float)(BumpSeconds / (BumpSeconds + ReboundSeconds)) : 0.5f;
	}
}
