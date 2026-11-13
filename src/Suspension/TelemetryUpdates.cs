// The telemetry overlay's settings and its reading every frame: nothing is read while all its parts are off.
using System;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// Stage 2.2 (F1): a small panel over the track with your own car's inputs (with gear and RPM), g-force and wheel slip.
	/// bskftw, Nov 12: those three, "off until switched on". Each part has its own switch in Options > While driving;
	/// with all three off nothing is read at all. The drawing is in src/pages/TelemetryOverlay.cs.
	/// </summary>
	public partial class TRCTuneLoaderMod {
		private const string TelemetryInputsPref = "telemetry_inputs";
		private const string TelemetryGPref = "telemetry_g";
		private const string TelemetrySlipPref = "telemetry_slip";
		private const string TelemetryPositionPref = "telemetry_position";

		private bool telemetryInputs_, telemetryG_, telemetrySlip_;
		private Vector2 telemetryPosition_ = new Vector2(-1f, -1f);

		/// <summary>The last frame read (null while the overlay is off or the car can't be read), and the g-force dot as shown.</summary>
		private TelemetryFrame telemetry_;
		private Vector2 telemetryDot_;

		private bool TelemetryOn => telemetryInputs_ || telemetryG_ || telemetrySlip_;

		private bool TelemetryOverlayVisible => TelemetryOn && telemetry_ != null && hasPose_;

		private void LoadTelemetryOptions() {
			telemetryInputs_ = Kino.Prefs.Get(TelemetryInputsPref, false);
			telemetryG_ = Kino.Prefs.Get(TelemetryGPref, false);
			telemetrySlip_ = Kino.Prefs.Get(TelemetrySlipPref, false);
			telemetryPosition_ = Kino.Prefs.Get(TelemetryPositionPref, new Vector2(-1f, -1f));
		}

		/// <summary>Called from Update every frame. Does nothing while every part is off.</summary>
		private void UpdateTelemetry() {
			if (!TelemetryOn || !hasPose_) {
				telemetry_ = null;
				return;
			}
			var frame = telemetry_ ?? new TelemetryFrame();
			bool read;
			try {
				read = adapter_.TryGetTelemetry(frame);
			} catch (Exception) {
				// A CarX update can break the call itself; the adapter logs its own failures once.
				read = false;
			}
			if (!read) {
				telemetry_ = null;
				return;
			}
			telemetry_ = frame;
			telemetryDot_ = TelemetryFrame.Smooth(telemetryDot_, new Vector2(frame.SideG, frame.LongG), Time.unscaledDeltaTime);
		}

		/// <summary>Options: one part of the overlay switched on or off.</summary>
		private void SetTelemetryPart(ref bool part, bool on, string pref, string name) {
			if (part == on) {
				return;
			}
			part = on;
			SavePref(pref, on);
			if (!TelemetryOn) {
				telemetry_ = null;
			}
			TrcLog.Info($"Telemetry: {name} {(on ? "on" : "off")}.");
		}
	}
}
