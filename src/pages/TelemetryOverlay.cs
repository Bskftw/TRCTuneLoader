// The telemetry overlay over the track: input bars with gear and RPM, the g-force dot and each wheel's slip, and dragging it.
using System;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		private const float TelemetryPad = 10f;
		private const float TelemetryHeader = 20f;
		private const float TelemetrySection = 96f;
		private const float TelemetryGap = 12f;
		private const float InputsWidth = 100f;
		private const float GBoxSize = 84f;
		private const float SlipWidth = 76f;

		/// <summary>The g-force box's edge is this many g.</summary>
		private const float GBoxEdge = 1.5f;

		private bool telemetryDragging_;
		private Vector2 telemetryDragFrom_;
		private GUIStyle telemetryRight_;
		private int telemetryStylesVersion_ = -1;

		private static readonly Color ThrottleColor = new Color(0.45f, 0.85f, 0.5f);
		private static readonly Color BrakeColor = new Color(0.95f, 0.35f, 0.35f);
		private static readonly Color HandbrakeColor = new Color(1f, 0.72f, 0.25f);
		private static readonly Color ClutchColor = new Color(0.45f, 0.7f, 1f);
		private static readonly Color TrackColor = new Color(1f, 1f, 1f, 0.1f);

		/// <summary>The panel's size for the parts switched on (or all three while arranging the screen with none on).</summary>
		private Vector2 TelemetrySize() {
			bool inputs = telemetryInputs_, g = telemetryG_, slip = telemetrySlip_;
			if (!inputs && !g && !slip) {
				inputs = g = slip = true;
			}
			float width = 0f;
			int parts = 0;
			foreach (var (on, w) in new[] { (inputs, InputsWidth), (g, GBoxSize), (slip, SlipWidth) }) {
				if (on) {
					width += w;
					parts++;
				}
			}
			width = Mathf.Max(150f, 2 * TelemetryPad + width + TelemetryGap * (parts - 1));
			return new Vector2(width, TelemetryPad + TelemetryHeader + (inputs ? 10f : 0f) + TelemetrySection + 6f);
		}

		/// <summary>Where the panel goes: where it was dragged, or bottom right.</summary>
		private Rect TelemetryRect(float screenWidth, float screenHeight) {
			Vector2 size = TelemetrySize();
			Vector2 at = telemetryPosition_.x >= 0 ? telemetryPosition_ : new Vector2(screenWidth - size.x - 16f, screenHeight - size.y - 16f);
			return new Rect(Mathf.Clamp(at.x, 0f, Mathf.Max(0f, screenWidth - size.x)), Mathf.Clamp(at.y, 0f, Mathf.Max(0f, screenHeight - size.y)), size.x, size.y);
		}

		private void DrawTelemetryOverlay(float screenWidth, float screenHeight) {
			Rect rect = TelemetryRect(screenWidth, screenHeight);
			DragOverlay(rect, ref telemetryDragging_, ref telemetryDragFrom_, ref telemetryPosition_, TelemetryPositionPref);
			TelemetryFrame f = telemetry_;
			if (Event.current.type != EventType.Repaint || f == null) {
				return;
			}
			if (telemetryStylesVersion_ != Theme.Version || telemetryRight_ == null) {
				telemetryStylesVersion_ = Theme.Version;
				telemetryRight_ = new GUIStyle(Theme.HudSmall) { alignment = TextAnchor.UpperRight, richText = true };
			}
			GUI.Box(rect, GUIContent.none, Theme.HudPanel);
			float x = rect.x + TelemetryPad;
			float y = rect.y + TelemetryPad - 4f;
			GUI.Label(new Rect(x, y, 120f, 18f), "<b>TELEMETRY</b>", Theme.HudSmall);
			y += TelemetryHeader;
			if (telemetryInputs_) {
				GUI.Label(new Rect(rect.xMax - TelemetryPad - 150f, rect.y + TelemetryPad - 4f, 150f, 18f),
					$"{f.Rpm:0} rpm  <b>{TelemetryFrame.GearText(f.Gear)}</b>", telemetryRight_);
				float rev = f.RevLimit > 100f ? Mathf.Clamp01(f.Rpm / f.RevLimit) : 0f;
				var bar = new Rect(x, y, rect.width - 2 * TelemetryPad, 5f);
				Fill(bar, TrackColor);
				Fill(new Rect(bar.x, bar.y, bar.width * rev, bar.height), rev >= 0.95f ? BrakeColor : rev >= 0.85f ? HandbrakeColor : ThrottleColor);
				y += 10f;
			}
			if (telemetryInputs_) {
				DrawInputs(new Rect(x, y, InputsWidth, TelemetrySection), f);
				x += InputsWidth + TelemetryGap;
			}
			if (telemetryG_) {
				DrawGBox(new Rect(x, y, GBoxSize, TelemetrySection), f);
				x += GBoxSize + TelemetryGap;
			}
			if (telemetrySlip_) {
				DrawSlip(new Rect(x, y, SlipWidth, TelemetrySection), f);
			}
		}

		/// <summary>Throttle, brake, handbrake and clutch as bars filling upwards, and the steering as a bar from the middle.</summary>
		private static void DrawInputs(Rect area, TelemetryFrame f) {
			float[] values = { f.Throttle, f.Brake, f.Handbrake, f.Clutch };
			Color[] colors = { ThrottleColor, BrakeColor, HandbrakeColor, ClutchColor };
			string[] names = { "T", "B", "H", "C" };
			const float barWidth = 14f;
			const float barHeight = 62f;
			float step = (area.width - barWidth) / 3f;
			for (int i = 0; i < 4; i++) {
				var bar = new Rect(area.x + i * step, area.y, barWidth, barHeight);
				Fill(bar, TrackColor);
				float fill = Mathf.Clamp01(values[i]) * barHeight;
				Fill(new Rect(bar.x, bar.yMax - fill, bar.width, fill), colors[i]);
				GUI.Label(new Rect(bar.x - 4f, bar.yMax, barWidth + 8f, 16f), names[i], Theme.HudSmall);
			}
			var steer = new Rect(area.x, area.y + barHeight + 22f, area.width, 5f);
			Fill(steer, TrackColor);
			float half = steer.width / 2f;
			float size = Mathf.Clamp(f.Steer, -1f, 1f) * half;
			Fill(size >= 0 ? new Rect(steer.x + half, steer.y, size, steer.height) : new Rect(steer.x + half + size, steer.y, -size, steer.height), Color.white);
			Fill(new Rect(steer.x + half - 1f, steer.y - 2f, 2f, steer.height + 4f), new Color(1f, 1f, 1f, 0.35f));
		}

		/// <summary>The g-force dot in a box (its edge is 1.5 g; the ring is 1 g), and the total under it.</summary>
		private void DrawGBox(Rect area, TelemetryFrame f) {
			var box = new Rect(area.x, area.y, GBoxSize, GBoxSize - 12f);
			box.x += (area.width - box.width) / 2f;
			Fill(box, new Color(0f, 0f, 0f, 0.3f));
			Vector2 centre = box.center;
			float scale = box.height / 2f / GBoxEdge;
			Fill(new Rect(box.x, centre.y, box.width, 1f), TrackColor);
			Fill(new Rect(centre.x, box.y, 1f, box.height), TrackColor);
			// 1 g as four ticks round the middle.
			float ring = scale;
			Fill(new Rect(centre.x - ring, centre.y - 3f, 1f, 6f), TrackColor);
			Fill(new Rect(centre.x + ring, centre.y - 3f, 1f, 6f), TrackColor);
			Fill(new Rect(centre.x - 3f, centre.y - ring, 6f, 1f), TrackColor);
			Fill(new Rect(centre.x - 3f, centre.y + ring, 6f, 1f), TrackColor);
			// Accelerating moves the dot down (pushed back into the seat), braking up; right turns push it left.
			Vector2 dot = telemetryDot_;
			float dx = Mathf.Clamp(-dot.x * scale, -box.width / 2f + 4f, box.width / 2f - 4f);
			float dy = Mathf.Clamp(dot.y * scale, -box.height / 2f + 4f, box.height / 2f - 4f);
			Round(new Rect(centre.x + dx - 5f, centre.y + dy - 5f, 10f, 10f), HandbrakeColor);
			GUI.Label(new Rect(area.x, box.yMax + 2f, area.width, 16f), $"{dot.magnitude:0.00} g", Theme.HudSmall);
		}

		/// <summary>The four wheels where they sit on the car, each filling with its slip: green gripping, amber past its best grip, red well past it, grey in the air.</summary>
		private static void DrawSlip(Rect area, TelemetryFrame f) {
			var body = new Rect(area.x + 22f, area.y + 2f, area.width - 44f, 72f);
			Round(body, new Color(1f, 1f, 1f, 0.08f));
			for (int w = 0; w < 4; w++) {
				bool left = w % 2 == 0;
				bool front = w < 2;
				var tyre = new Rect(left ? body.x - 16f : body.xMax + 4f, front ? body.y + 4f : body.yMax - 32f, 12f, 28f);
				Fill(tyre, new Color(0f, 0f, 0f, 0.55f));
				float slip = f.Slip[w];
				Color color = !f.Contact[w] ? new Color(0.55f, 0.55f, 0.6f) : slip >= 1.5f ? BrakeColor : slip >= 1f ? HandbrakeColor : ThrottleColor;
				float fill = f.Contact[w] ? Mathf.Clamp01(slip / 2f) * (tyre.height - 2f) : tyre.height - 2f;
				Fill(new Rect(tyre.x + 1f, tyre.yMax - 1f - fill, tyre.width - 2f, fill), f.Contact[w] ? color : new Color(color.r, color.g, color.b, 0.3f));
			}
			GUI.Label(new Rect(area.x - 6f, body.yMax + 4f, area.width + 12f, 16f), "slip", Theme.HudSmall);
		}

		/// <summary>An overlay can be dragged anywhere while the window is open (not through the window); the place is saved.</summary>
		private void DragOverlay(Rect rect, ref bool dragging, ref Vector2 grab, ref Vector2 position, string pref) {
			Event e = Event.current;
			if (!windowOpen_) {
				dragging = false;
				return;
			}
			if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition) && !windowRect_.Contains(e.mousePosition)) {
				dragging = true;
				grab = e.mousePosition - rect.position;
				e.Use();
			} else if (e.type == EventType.MouseDrag && dragging) {
				position = e.mousePosition - grab;
				e.Use();
			} else if (e.type == EventType.MouseUp && dragging) {
				dragging = false;
				SavePref(pref, position);
				e.Use();
			}
		}
	}
}
