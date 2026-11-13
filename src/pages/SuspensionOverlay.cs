// The suspension overlay over the track: the car from above, each tyre filling with its compression, and dragging it.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		/// <summary>The four wheels' colours in the trace (front left, front right, rear left, rear right).</summary>
		private static readonly Color[] WheelColors = { new Color(0.45f, 0.75f, 1f), new Color(1f, 0.5f, 0.75f), new Color(0.55f, 0.9f, 0.5f), new Color(1f, 0.78f, 0.35f) };

		private Vector2 suspensionPosition_ = new Vector2(-1f, -1f);

		private bool suspensionDragging_;
		private Vector2 suspensionDragFrom_;

		private bool SuspensionOverlayVisible => suspensionOn_ && suspensionOverlay_ && liveFrame_ != null && hasPose_;

		private static Texture2D roundTexture_;
		private static GUIStyle roundStyle_;

		/// <summary>A rounded box in one colour (a white rounded texture, tinted).</summary>
		private static void Round(Rect rect, Color color) {
			if (roundTexture_ == null || roundStyle_ == null) {
				roundTexture_ = Theme.Rounded(Color.white, Color.white, 5);
				roundStyle_ = new GUIStyle { normal = { background = roundTexture_ }, border = new RectOffset(5, 5, 5, 5) };
			}
			Color was = GUI.color;
			GUI.color = color;
			roundStyle_.Draw(rect, false, false, false, false);
			GUI.color = was;
		}

		private static void Fill(Rect rect, Color color) {
			Color was = GUI.color;
			GUI.color = color;
			GUI.DrawTexture(rect, Texture2D.whiteTexture);
			GUI.color = was;
		}

		/// <summary>How a tyre is coloured: green in the normal range, amber getting deep, red at the stops, grey off the road.</summary>
		private static Color TravelColor(float compression, bool contact) =>
			!contact ? new Color(0.55f, 0.55f, 0.6f) : compression >= SuspensionLog.BottomedAt ? new Color(0.95f, 0.3f, 0.3f)
			: compression >= 0.8f ? new Color(1f, 0.72f, 0.25f) : new Color(0.4f, 0.85f, 0.5f);

		/// <summary>The car from above: each tyre fills with its compression, its numbers beside it, and the last 4 s traced underneath.</summary>
		private void DrawSuspensionOverlay(float screenWidth, float screenHeight) {
			const float width = 300f;
			const float height = 262f;
			if (suspensionPosition_.x < 0) {
				suspensionPosition_ = new Vector2(16f, screenHeight - height - 16f);
			}
			var rect = new Rect(Mathf.Clamp(suspensionPosition_.x, 0f, Mathf.Max(0f, screenWidth - width)),
				Mathf.Clamp(suspensionPosition_.y, 0f, Mathf.Max(0f, screenHeight - height)), width, height);
			HandleSuspensionDrag(rect);
			if (Event.current.type != EventType.Repaint) {
				return;
			}
			GUI.Box(rect, GUIContent.none, Theme.HudPanel);
			SuspensionFrame frame = liveFrame_;
			GUI.Label(new Rect(rect.x + 12f, rect.y + 6f, 150f, 18f), "<b>SUSPENSION</b>", Theme.HudSmall);
			SuspensionLog log = runLog_ ?? session_;
			string state = runLog_ != null ? $"logging {runLog_.Label}" : $"session {log?.Seconds ?? 0:0} s";
			GUI.Label(new Rect(rect.xMax - 160f, rect.y + 6f, 150f, 18f), state, Theme.HudSmall);

			// The car body, then each tyre at its corner.
			float cx = rect.center.x;
			float top = rect.y + 30f;
			var body = new Rect(cx - 30f, top + 4f, 60f, 132f);
			Round(body, new Color(1f, 1f, 1f, 0.08f));
			Round(new Rect(body.x + 8f, body.y + 26f, body.width - 16f, 22f), new Color(1f, 1f, 1f, 0.06f));
			Round(new Rect(body.x + 8f, body.y + 92f, body.width - 16f, 18f), new Color(1f, 1f, 1f, 0.05f));
			for (int w = 0; w < 4; w++) {
				bool left = w % 2 == 0;
				bool front = w < 2;
				var tyre = new Rect(left ? body.x - 26f : body.xMax + 4f, front ? top + 14f : top + 86f, 22f, 44f);
				WheelFrame wf = frame.Wheels[w];
				Color color = TravelColor(wf.Compression, wf.Contact);
				Round(tyre, new Color(0f, 0f, 0f, 0.55f));
				if (wf.Contact) {
					float fill = Mathf.Clamp01(wf.Compression) * (tyre.height - 4f);
					Round(new Rect(tyre.x + 2f, tyre.yMax - 2f - fill, tyre.width - 4f, Mathf.Max(3f, fill)), color);
				} else {
					Round(new Rect(tyre.x + 2f, tyre.y + 2f, tyre.width - 4f, tyre.height - 4f), new Color(color.r, color.g, color.b, 0.25f));
				}
				// The tyre's load against twice its share of the weight, as a thin bar under it.
				float share = corners_ != null && corners_[w] != null ? corners_[w].CornerMass * 9.81f : 0f;
				if (share > 0) {
					var bar = new Rect(tyre.x, tyre.yMax + 3f, tyre.width, 3f);
					Fill(bar, new Color(1f, 1f, 1f, 0.12f));
					Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(wf.Load / (2f * share)), bar.height), WheelColors[w]);
				}
				float speed = SuspensionLog.TraceSeconds > 0 && log != null ? log.LastSpeedMs[w] * 1000f : 0f;
				string damper = Mathf.Abs(speed) < 20f ? "" : speed > 0 ? $"<color=#FAC775>bump {speed:0}</color>" : $"<color=#8FB8FF>reb {-speed:0}</color>";
				string text = wf.Contact ? $"<b>{wf.Compression * 100:0}%</b>\n{wf.Load / 1000f:0.0} kN\n{damper}" : "<b><color=#F7C1C1>OFF</color></b>\nthe road";
				var label = new Rect(left ? rect.x + 8f : tyre.xMax + 8f, tyre.y, left ? tyre.x - rect.x - 14f : rect.xMax - tyre.xMax - 14f, 54f);
				if (suspensionStylesVersion_ != Theme.Version) {
					// The look changed: made again in the new text colour and size.
					suspensionStylesVersion_ = Theme.Version;
					suspensionLeft_ = suspensionRight_ = null;
				}
				GUIStyle style = left ? suspensionRight_ ?? (suspensionRight_ = new GUIStyle(Theme.HudSmall) { alignment = TextAnchor.UpperRight, richText = true })
					: suspensionLeft_ ?? (suspensionLeft_ = new GUIStyle(Theme.HudSmall) { alignment = TextAnchor.UpperLeft, richText = true });
				GUI.Label(label, text, style);
			}

			// The last 4 seconds: each wheel's compression, bottom of the box extended, top compressed.
			var graph = new Rect(rect.x + 12f, rect.y + 180f, rect.width - 24f, 56f);
			Fill(graph, new Color(0f, 0f, 0f, 0.3f));
			Fill(new Rect(graph.x, graph.y + graph.height * (1f - 0.8f), graph.width, 1f), new Color(1f, 0.72f, 0.25f, 0.25f));
			if (log != null && log.Trace.Count > 1) {
				double end = log.Trace[log.Trace.Count - 1].Time;
				foreach (var (time, compression) in log.Trace) {
					float x = graph.x + graph.width * (float)(1 - (end - time) / SuspensionLog.TraceSeconds);
					for (int w = 0; w < 4; w++) {
						float y = graph.yMax - 2f - (graph.height - 4f) * Mathf.Clamp01(compression[w]);
						Fill(new Rect(x, y, 2f, 2f), WheelColors[w]);
					}
				}
			}
			for (int w = 0; w < 4; w++) {
				GUI.Label(new Rect(graph.x + w * 70f, graph.yMax + 2f, 70f, 16f), $"<b><color=#{ColorUtility.ToHtmlStringRGB(WheelColors[w])}>{SuspensionLog.WheelShort[w]}</color></b>", suspensionLeft_ ?? Theme.HudSmall);
			}
		}

		private GUIStyle suspensionLeft_;
		private int suspensionStylesVersion_ = -1;
		private GUIStyle suspensionRight_;

		/// <summary>The overlay can be dragged anywhere while the window is open, like the timer.</summary>
		private void HandleSuspensionDrag(Rect rect) =>
			DragOverlay(rect, ref suspensionDragging_, ref suspensionDragFrom_, ref suspensionPosition_, SuspensionPositionPref);
	}
}
