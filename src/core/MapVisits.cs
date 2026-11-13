// One visit to a map: when you leave, what happened there goes on Home and in the log.
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
		/// <summary>This visit to a map (null in the garage), and the last finished one's summary for Home (null: none, or hidden).</summary>
		private SessionStats visit_;
		private List<string> lastSession_;

		/// <summary>Leaving a map: what happened there goes on Home and in the log, when it was more than a look around.</summary>
		private void EndSession() {
			SessionStats ended = visit_;
			visit_ = null;
			if (ended == null || !ended.Worth) {
				return;
			}
			try {
				lastSession_ = ended.Lines(Time.realtimeSinceStartupAsDouble, TimeAttackRun.FormatTime);
				TrcLog.Info($"Session: {string.Join("; ", lastSession_)}.");
			} catch (Exception e) {
				TrcLog.Error(e);
			}
		}
	}
}
