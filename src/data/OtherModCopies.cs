// Finds other TRCTuneLoader.ksm files than the one running, left by an old install.
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
		/// <summary>Other TRCTuneLoader.ksm files than the one running (an old install or update left them).</summary>
		private List<string> otherCopies_ = new List<string>();

		private void FindOtherCopies() {
			try {
				otherCopies_ = DataFolder.OtherCopies(Location, Kino.Paths.Mods, Kino.Paths.GameRoot);
			} catch (Exception e) {
				TrcLog.Warning($"Couldn't look for other copies of the mod: {e.Message}");
				return;
			}
			foreach (string copy in otherCopies_) {
				TrcLog.Warning($"Another copy of TRC Tune Loader is at {copy}. The one running is {Location}: delete the other one.");
			}
			if (otherCopies_.Count > 0) {
				ShowNotice($"<b>TRC Tune Loader is installed twice</b>\n<color={Theme.MutedHex}>Open the window (Home) to see which file to delete</color>", 15f);
			}
		}
	}
}
