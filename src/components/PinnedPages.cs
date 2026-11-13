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
		private const int MostPinned = 8;
		private readonly List<string> pinnedKeys_ = new List<string>();
		private readonly List<NavPage> pinnedShown_ = new List<NavPage>();

		private void LoadPins() {
			pinnedKeys_.Clear();
			foreach (string key in (Kino.Prefs.Get(PinnedPref, "") ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)) {
				if (pinnedKeys_.Count < MostPinned && !pinnedKeys_.Contains(key)) {
					pinnedKeys_.Add(key);
				}
			}
		}

		private void TogglePin(NavPage page) {
			if (!pinnedKeys_.Remove(page.Key) && pinnedKeys_.Count < MostPinned) {
				pinnedKeys_.Add(page.Key);
			}
			SavePref(PinnedPref, string.Join(";", pinnedKeys_));
		}
	}
}
