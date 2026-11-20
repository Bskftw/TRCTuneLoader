// Clip marks (clips.csv next to the mod): a line for each moment the player marked, to find it later in a recording.
using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace TRC.TuneLoader {
	/// <summary>
	/// docs/archive/IMPROVEMENT_PLAN.md F8: "a key that writes 'moment worth clipping' with the time, course and speed to a small text
	/// file". Lines are only added. The clock time is the PC's, so it lines up with a recording's own clock.
	/// </summary>
	public static class ClipMarks {
		public const string FileName = "clips.csv";

		public static void Append(string folder, DateTime when, string map, string course, string runTime, string car, float kmh) {
			Directory.CreateDirectory(folder);
			string path = Path.Combine(folder, FileName);
			var text = new StringBuilder();
			if (!File.Exists(path)) {
				text.Append("date,map,course,run_time,car,kmh\n");
			}
			string[] fields = { when.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), map, course, runTime, car, kmh.ToString("0", CultureInfo.InvariantCulture) };
			text.Append(string.Join(",", Array.ConvertAll(fields, TimeAttackLog.Quote))).Append('\n');
			File.AppendAllText(path, text.ToString(), new UTF8Encoding(false));
		}
	}
}
