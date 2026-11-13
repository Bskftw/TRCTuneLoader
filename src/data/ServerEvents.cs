// Adds finished runs, battles and races to the server-ready event ledger (EventLedger).
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
		private EventLedger ledger_;

		private EventLedger Ledger => ledger_ ??= new EventLedger(TimesFolder);

		/// <summary>Adds an event to the server-ready ledger; never stops what called it.</summary>
		private void LedgerEvent(string type, List<EventLedger.Field> fields) {
			try {
				Ledger.Append(type, MySteamId, Version, DateTime.UtcNow, fields);
			} catch (Exception e) {
				TrcLog.Warning($"Couldn't add the {type} to the server events: {e.Message}");
			}
		}
	}
}
