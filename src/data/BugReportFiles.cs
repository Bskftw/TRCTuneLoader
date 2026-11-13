// Makes the bug report: collects the logs, the note and the summary into one zip on the Desktop.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		private void MakeBugReport() {
			DateTime when = DateTime.Now;
			try {
				TrcLog.Info($"Bug report made{(TuneRules.Trim(bugNote_).Length > 0 ? $": {TuneRules.Trim(bugNote_)}" : ".")}");
				TrcLog.Flush();
				var parts = new List<BugReport.Part> {
					new BugReport.Part { Name = "what-happened.txt", Text = TuneRules.Trim(bugNote_).Length > 0 ? bugNote_ : "(nothing written)" },
					new BugReport.Part { Name = "summary.txt", Text = BugSummary(when) },
					BugReport.LogPart(TrcLog.FileName, Path.Combine(modFolder_, TrcLog.FileName)),
					BugReport.LogPart(TrcLog.PreviousFileName, Path.Combine(modFolder_, TrcLog.PreviousFileName)),
				};
				string kino = Directory.GetParent(Kino.Paths.Mods)?.FullName ?? "";
				if (kino.Length > 0) {
					parts.Add(BugReport.LogPart("ksl-output.log", Path.Combine(kino, "output.log")));
				}
				string desktop = "";
				try {
					desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
				} catch (Exception) {
					// No Desktop: the mod's folder instead.
				}
				string folder = desktop.Length > 0 && Directory.Exists(desktop) ? desktop : Path.Combine(modFolder_, "bug-reports");
				lastBugReport_ = BugReport.Write(folder, when, parts);
				homeResult_ = $"Bug report saved: {Path.GetFileName(lastBugReport_)} ({(folder == desktop ? "on your Desktop" : folder)}). Send it to bskftw.";
				optionsResult_ = homeResult_;
				bugNote_ = "";
				TrcLog.Info($"Bug report saved: {lastBugReport_}");
			} catch (Exception e) {
				homeResult_ = $"Couldn't make the bug report: {e.Message}";
				optionsResult_ = homeResult_;
				TrcLog.Error(e);
			}
		}
	}
}
