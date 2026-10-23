using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// On-screen messages while driving: the TRC spec badge in a corner, and the pop-up when the car
	/// reaches the track (or changes while driving) and isn't TRC spec. Both use the same check as the
	/// My car status bar, refreshed every few seconds while on track.
	/// </summary>
	public partial class TRCTuneLoaderMod {
		/// <summary>How long after reaching the track the car is checked, so CarX has finished putting it together.</summary>
		private const float ArrivalCheckSeconds = 4f;
		private const float PopupSeconds = 8f;
		private const float HudMargin = 12f;
		private const float PopupWidth = 520f;

		private enum SpecState { None, Spec, NotFullyChecked, NotSpec }

		private float trackSince_ = -1f;
		private bool arrivalChecked_;
		private SpecState lastTrackState_;
		private string popupText_ = "";
		private float popupUntil_;

		private bool OnTrack => car_ != null && !car_.InGarage && !car_.InDyno;

		/// <summary>Called from Update: keeps the status fresh on track for the badge and the pop-up.</summary>
		private void UpdateTrackStatus() {
			if (!showBadge_ && !autoCheck_) {
				return;
			}

			RefreshCarIfDue();
			if (!OnTrack) {
				trackSince_ = -1f;
				arrivalChecked_ = false;
				return;
			}
			if (trackSince_ < 0f) {
				trackSince_ = Time.unscaledTime;
			}
			if (Time.unscaledTime - trackSince_ < ArrivalCheckSeconds) {
				return;
			}

			if (!arrivalChecked_) {
				// A fresh check now that the car has settled, not one left over from the garage.
				arrivalChecked_ = true;
				nextStatusRefresh_ = 0;
				RefreshStatusIfDue();
				lastTrackState_ = SpecState.None;
			} else {
				RefreshStatusIfDue();
			}

			SpecState state = StateOf(carStatus_);
			if (autoCheck_ && state == SpecState.NotSpec && lastTrackState_ != SpecState.NotSpec) {
				popupText_ = $"Not TRC spec: {ProblemOf(carStatus_)}. Click here or press {shortcut_} for details.";
				popupUntil_ = Time.unscaledTime + PopupSeconds;
				TrcLog.Info($"On track: {popupText_}");
			}
			lastTrackState_ = state;
		}

		private static SpecState StateOf(OwnCheck check) {
			if (check?.Saved == null) {
				return SpecState.None;
			}
			if (!check.Saved.Matches || check.StanceDifferences.Count > 0 || check.DrivetrainChanged || check.LiveDifferences.Count > 0) {
				return SpecState.NotSpec;
			}
			return check.LiveChecked ? SpecState.Spec : SpecState.NotFullyChecked;
		}

		/// <summary>The first reason the car isn't TRC spec, in a few words.</summary>
		private static string ProblemOf(OwnCheck check) {
			if (!check.Saved.Matches) {
				return $"your setup differs from {check.Official.Name} in {check.Saved.Differences.Count} place(s)";
			}
			if (check.StanceDifferences.Count > 0) {
				return "the car's stance (ride height, track width or axles) isn't stock";
			}
			if (check.DrivetrainChanged) {
				return DrivetrainProblem(check.Drivetrain);
			}
			return $"the car you're driving differs from the tune in {check.LiveDifferences.Count} place(s)";
		}

		/// <summary>Called from OnGUI while the window is closed: the badge and the pop-up.</summary>
		private void DrawHud(float screenWidth, float screenHeight) {
			if (!OnTrack) {
				return;
			}

			if (autoCheck_ && Time.unscaledTime < popupUntil_ && !string.IsNullOrEmpty(popupText_)) {
				var content = new GUIContent(popupText_);
				float width = Mathf.Min(PopupWidth, screenWidth - 2 * HudMargin);
				float height = Theme.Popup.CalcHeight(content, width);
				var rect = new Rect((screenWidth - width) / 2f, HudMargin + 40f, width, height);
				if (GUI.Button(rect, content, Theme.Popup)) {
					popupUntil_ = 0;
					tab_ = 0;
					OpenWindow();
				}
			}

			if (!showBadge_ || !arrivalChecked_) {
				return;
			}
			SpecState state = StateOf(carStatus_);
			if (state == SpecState.None) {
				return;
			}

			string set = carStatus_.Official.Name;
			GUIStyle style;
			string text;
			switch (state) {
				case SpecState.Spec:
					style = Theme.BadgeGood;
					text = $"TRC SPEC · {set}";
					break;
				case SpecState.NotFullyChecked:
					style = Theme.BadgeWarn;
					text = $"{set} · not fully checked";
					break;
				default:
					style = Theme.BadgeBad;
					text = "NOT TRC SPEC";
					break;
			}

			var badge = new GUIContent(text);
			Vector2 size = style.CalcSize(badge);
			float x = badgeCorner_ % 2 == 0 ? HudMargin : screenWidth - size.x - HudMargin;
			float y = badgeCorner_ < 2 ? HudMargin : screenHeight - size.y - HudMargin;
			GUI.Label(new Rect(x, y, size.x, size.y), badge, style);
		}
	}
}
