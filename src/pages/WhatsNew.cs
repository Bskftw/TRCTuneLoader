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
		private const string WhatsNewVersion = "0.19.0";

		private static readonly string[] WhatsNewLines = {
			"<b>A new look</b>: the parts down the left, a tile for each on Home, your last run with Go again, and new ready-made looks in Options, Look.",
			"<b>Challenges</b> (Time attack): a daily and a weekly track with bronze, silver and gold times. <b>Lap compare</b> on My times shows where you lost time.",
			"<b>Tools</b>: Host tools with Invite everyone, tandem aids, Driver stats, and the HUD page with presets. <b>Ctrl + Q</b> opens the quick menu while you drive.",
			"<b>Highway traffic together</b> uses far less data and carries on when its host leaves. Everyone in the room needs 0.19.0.",
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
