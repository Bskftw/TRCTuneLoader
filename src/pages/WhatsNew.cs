// What's new: the lines shown once after an update, and the notice that points to them.
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
		private const string WhatsNewPref = "whats_new_seen";

		/// <summary>Raise with each version that has news worth a pop-up.</summary>
		private const string WhatsNewVersion = "0.18.4";

		private static readonly string[] WhatsNewLines = {
			"<b>Your bests carry over between cars</b>: in a car with no time on a track yet, the live delta and the sector pop-ups compare with your best in any car. A bar under the timer shows the delta, with your predicted time.",
			"<b>Timing detail</b> and <b>telemetry</b> in Options, While driving: Detailed adds this car's best and each sector's gaps; telemetry shows your inputs, g-force and wheel slip (each off until you turn it on).",
			"<b>Search</b> and <b>pins</b> on Home, <b>Compare tunes</b> in TRC spec, and faster loading on maps with lots of tracks.",
			"<b>Traffic lab</b> (very early testing), in Tools: AI traffic on Project Dream's highways and on any track's path. In a room, one player starts it and the others press Join: everyone sees the same cars and crashes.",
		};

		private bool whatsNewPending_;

		/// <summary>Once on the garage screen after an update: a notice says what's new is on Home. The window never opens by
		/// itself (bskftw, Nov 9: "let players open it themselves").</summary>
		private void OpenWhatsNewWhenReady() {
			RefreshCarIfDue();
			if (car_ == null || !car_.InGarage || car_.InDyno) {
				garageSince_ = -1f;
				return;
			}
			if (garageSince_ < 0f) {
				garageSince_ = Time.unscaledTime;
			}
			if (Time.unscaledTime - garageSince_ >= TourGarageDelaySeconds) {
				tab_ = HomeTab;
				ShowNotice($"<b>TRC Tune Loader {WhatsNewVersion} is installed</b>\n<color={Theme.MutedHex}>Press {shortcut_} to see what's new (Home)</color>", 10f);
				garageSince_ = -1f;
				// Once per start of the game.
				whatsNewShownThisSession_ = true;
			}
		}

		private bool whatsNewShownThisSession_;

		private void DismissWhatsNew() {
			whatsNewPending_ = false;
			SavePref(WhatsNewPref, WhatsNewVersion);
		}
	}
}
