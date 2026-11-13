// Times, gaps and distances as text: 1:23.456, +0.125, 4.2 km.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	public sealed partial class TimeAttackRun {
		/// <summary>"1:23.456", or "23.456" under a minute.</summary>
		public static string FormatTime(double seconds) {
			if (seconds < 0) {
				return "-" + FormatTime(-seconds);
			}
			long ms = (long)Math.Round(seconds * 1000.0, MidpointRounding.AwayFromZero);
			long minutes = ms / 60000;
			double rest = (ms % 60000) / 1000.0;
			return minutes > 0
				? $"{minutes.ToString(CultureInfo.InvariantCulture)}:{rest.ToString("00.000", CultureInfo.InvariantCulture)}"
				: rest.ToString("0.000", CultureInfo.InvariantCulture);
		}

		/// <summary>"+0.125" or "-1.200": a gap in seconds, with its sign.</summary>
		public static string FormatGap(double seconds) =>
			(seconds < 0 ? "-" : "+") + Math.Abs(seconds).ToString("0.000", CultureInfo.InvariantCulture);

		/// <summary>"320 m" or "4.2 km".</summary>
		public static string Distance(float metres) =>
			metres >= 1000f
				? (metres / 1000f).ToString("0.0", CultureInfo.InvariantCulture) + " km"
				: metres.ToString("0", CultureInfo.InvariantCulture) + " m";
	}
}
