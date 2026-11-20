// The tandem aids box over the track (Tools > HUD): the car you're chasing or leading, the gap, both drift angles, how long it's been close.
using System.Collections.Generic;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		private const string TandemPref = "hud_tandem";
		private bool tandemOn_;
		private readonly TandemWatch tandem_ = new TandemWatch();
		private readonly List<RoomCarPose> tandemPoses_ = new List<RoomCarPose>();
		private string tandemWith_ = "";
		private string tandemKey_ = "";

		/// <summary>Another car is this close (m): the box shows.</summary>
		private const float TandemReach = 40f;

		private bool TandemVisible => tandemOn_ && tandemWith_.Length > 0 && hasPose_ && !windowOpen_;

		private void LoadTandemOption() {
			tandemOn_ = Kino.Prefs.Get(TandemPref, false);
		}

		/// <summary>From Update: the nearest other car within <see cref="TandemReach"/> m, measured against yours. Nothing runs when it's off.</summary>
		private void UpdateTandem() {
			if (!tandemOn_ || !hasPose_ || !adapter_.TryGetRoomCarPoses(tandemPoses_)) {
				tandemWith_ = "";
				return;
			}
			int nearest = -1;
			float best = TandemReach;
			for (int i = 0; i < tandemPoses_.Count; i++) {
				float d = Vector3.Distance(tandemPoses_[i].Pose.Position, pose_.Position);
				if (d < best) {
					best = d;
					nearest = i;
				}
			}
			if (nearest < 0) {
				tandemWith_ = "";
				tandem_.Reset();
				return;
			}
			RoomCarPose other = tandemPoses_[nearest];
			if (other.Key != tandemKey_) {
				tandemKey_ = other.Key;
				tandem_.Reset();
			}
			tandemWith_ = BattleMessage.PlainName(other.Name);
			tandem_.Tick(Time.deltaTime, pose_.Position, pose_.Forward, pose_.Body, other.Pose.Position, other.Pose.Forward, other.Pose.Body);
		}

		/// <summary>
		/// The gap as a bar (UI redesign Stage 5): green up to close, amber to twice that, red beyond, with a white marker
		/// where the gap is now (the bar's end is three times close).
		/// </summary>
		private static void DrawGapBar(Rect bar, float gap) {
			float third = bar.width / 3f;
			Color before = GUI.color;
			string[] hexes = { Theme.GoodHex, Theme.WarnHex, Theme.BadHex };
			for (int i = 0; i < 3; i++) {
				Theme.TryHex(hexes[i], out Color c);
				GUI.color = new Color(c.r, c.g, c.b, 0.85f);
				GUI.DrawTexture(new Rect(bar.x + i * third, bar.y, third, bar.height), Texture2D.whiteTexture);
			}
			float at = Mathf.Clamp01(gap / (TandemWatch.CloseMetres * 3f));
			GUI.color = Color.white;
			GUI.DrawTexture(new Rect(bar.x + bar.width * at - 1.5f, bar.y - 4f, 3f, bar.height + 8f), Texture2D.whiteTexture);
			GUI.color = before; // risk 8
		}

		private void DrawTandem(float screenWidth, float screenHeight) {
			if (Event.current.type != EventType.Repaint) {
				return;
			}
			const float width = 240f;
			var panel = new Rect(12f, screenHeight * 0.42f, width, 100f);
			GUI.Box(panel, GUIContent.none, Theme.HudPanel);
			string close = tandem_.Gap <= TandemWatch.CloseMetres ? Theme.GoodHex : Theme.MutedHex;
			GUI.Label(new Rect(panel.x + 10f, panel.y + 6f, width - 20f, 18f), $"<b>Tandem</b> with {tandemWith_}", Theme.HudSmall);
			GUI.Label(new Rect(panel.x + 10f, panel.y + 24f, width - 20f, 26f), $"<color={close}><b>{tandem_.Gap:0.0} m</b></color>", Theme.HudTimer);
			DrawGapBar(new Rect(panel.x + 14f, panel.y + 52f, width - 28f, 4f), tandem_.Gap);
			GUI.Label(new Rect(panel.x + 10f, panel.y + 58f, width - 20f, 18f), $"Angle: you {tandem_.MyAngle:0}°, them {tandem_.TheirAngle:0}°", Theme.HudSmall);
			GUI.Label(new Rect(panel.x + 10f, panel.y + 76f, width - 20f, 18f),
				$"Held close: {tandem_.Held:0.0} s  <color={Theme.MutedHex}>best {tandem_.BestHeld:0.0} s</color>", Theme.HudSmall);
		}
	}
}
