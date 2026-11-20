// Pinned pages: the pages a player pins from their titles, saved in the pinned_pages pref.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		private const string PinnedPref = "pinned_pages";
		private const string PinnedLayoutPref = "pinned_pages_layout";
		private const int MostPinned = 8;
		private readonly List<string> pinnedKeys_ = new List<string>();
		private readonly List<NavPage> pinnedShown_ = new List<NavPage>();

		private void LoadPins() {
			pinnedKeys_.Clear();
			string[] saved = (Kino.Prefs.Get(PinnedPref, "") ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
			int.TryParse(Kino.Prefs.Get(PinnedLayoutPref, "1"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int layout);
			// Pages renumbered since they were pinned (src/data/PageKeys.cs) still open the same page.
			foreach (string key in PageKeys.Migrate(saved, layout)) {
				if (pinnedKeys_.Count < MostPinned) {
					pinnedKeys_.Add(key);
				}
			}
			if (layout != PageKeys.Layout && saved.Length > 0) {
				SavePins();
			}
		}

		private void TogglePin(NavPage page) {
			if (!pinnedKeys_.Remove(page.Key) && pinnedKeys_.Count < MostPinned) {
				pinnedKeys_.Add(page.Key);
			}
			SavePins();
		}

		private void SavePins() {
			SavePref(PinnedPref, string.Join(";", pinnedKeys_));
			SavePref(PinnedLayoutPref, PageKeys.Layout.ToString(CultureInfo.InvariantCulture));
		}
	}
}
