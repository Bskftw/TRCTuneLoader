// A battle's format (race, cat and mouse, highway, speed traps), and one driver's result for one run.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// Race and cat and mouse run on a course; a highway battle (Tokyo Xtreme Racer style) anywhere, on SP bars;
	/// speed traps (0.15.1, for fun) on a course, won on the total of every trap.
	/// </summary>
	public enum BattleFormat : byte { Race = 0, CatAndMouse = 1, Highway = 2, SpeedTraps = 3 }

	/// <summary>One driver's result for one run of a battle.</summary>
	public sealed class RunResult {
		public int Run;
		public bool Finished;

		/// <summary>Seconds from GO to the finish line (when finished).</summary>
		public double Time;

		/// <summary>Why the run was stopped, when it didn't finish.</summary>
		public string Reason = "";

		/// <summary>TRC spec at the finish (TRC spec battles only care).</summary>
		public bool SpecAtEnd;

		/// <summary>Every speed caught at the course's traps added up (km/h): what a speed trap battle is won on.</summary>
		public float TrapTotal;
	}
}
