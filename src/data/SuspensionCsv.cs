// A suspension log as a CSV file (every frame), saved in the Suspension folder.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace TRC.TuneLoader {
	public sealed partial class SuspensionLog {
		/// <summary>Every frame as a CSV (time, speed, position, and each wheel's compression, damper speed, load, contact and slip), for a spreadsheet or a telemetry tool.</summary>
		public string ToCsv() {
			var text = new StringBuilder("time_s,kmh,x,y,z");
			foreach (string w in WheelShort) {
				text.Append($",{w}_compression_pct,{w}_damper_mm_s,{w}_load_n,{w}_contact,{w}_slip_angle,{w}_slip_ratio");
			}
			text.Append('\n');
			string F(double v, string format = "0.###") => v.ToString(format, CultureInfo.InvariantCulture);
			double start = Frames.Count > 0 ? Frames[0].Time : 0;
			for (int i = 0; i < Frames.Count; i++) {
				SuspensionFrame f = Frames[i];
				SuspensionFrame p = i > 0 ? Frames[i - 1] : f;
				double dt = f.Time - p.Time;
				text.Append(F(f.Time - start, "0.0000")).Append(',').Append(F(f.Kmh, "0.0")).Append(',')
					.Append(F(f.Position.x, "0.00")).Append(',').Append(F(f.Position.y, "0.00")).Append(',').Append(F(f.Position.z, "0.00"));
				for (int w = 0; w < 4; w++) {
					WheelFrame wf = f.Wheels[w];
					float travel = Setup[w]?.Travel > 0 ? Setup[w].Travel : 0.15f;
					double speed = dt > 0 ? (wf.Compression - p.Wheels[w].Compression) * travel / dt * 1000 : 0;
					text.Append(',').Append(F(wf.Compression * 100, "0.0")).Append(',').Append(F(speed, "0")).Append(',').Append(F(wf.Load, "0"))
						.Append(',').Append(wf.Contact ? "1" : "0").Append(',').Append(F(wf.SlipAngle, "0.000")).Append(',').Append(F(wf.SlipRatio, "0.000"));
				}
				text.Append('\n');
			}
			return text.ToString();
		}

		public string SaveCsv(string folder) {
			Directory.CreateDirectory(folder);
			string name = new string($"{Started.ToString("yyyy-MM-dd HHmmss", CultureInfo.InvariantCulture)} {Label}".Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim();
			string path = Path.Combine(folder, name + ".csv");
			File.WriteAllText(path, ToCsv(), new UTF8Encoding(false));
			return path;
		}
	}
}
