// The Suspension page: live wheels, the last run and session reports, histograms, and the setup sheet.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		private static readonly string[] SuspensionSections = { "Live", "Last run", "Session", "Setup sheet" };
		private static readonly string[] SuspensionScopes = { "Whenever I'm driving", "Only during runs" };

		private int suspensionView_;
		private Vector2 suspensionScroll_;
		private string suspensionResult_ = "";

		private void DrawSuspensionTab() {
			GUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
			int view = SideNav(SuspensionSections, suspensionView_, new[] { suspensionOn_ ? "on" : "off", lastRunLog_ != null ? "1" : "", "", "" });
			if (view != suspensionView_) {
				suspensionView_ = view;
				suspensionResult_ = "";
			}
			GUILayout.Space(10);
			GUILayout.BeginVertical(GUILayout.ExpandHeight(true));
			suspensionScroll_ = GUILayout.BeginScrollView(suspensionScroll_, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
			if (suspensionResult_.Length > 0) {
				GUILayout.Label(suspensionResult_, Theme.Body);
			}
			switch (suspensionView_) {
				case 0: DrawSuspensionLive(); break;
				case 1: DrawSuspensionReport(lastRunLog_, "Drive a time attack, battle or race with logging on: its suspension shows here when it ends."); break;
				case 2: DrawSuspensionReport(session_, "Turn logging on and drive: everything since then shows here."); break;
				default: DrawSetupSheet(); break;
			}
			GUILayout.EndScrollView();
			GUILayout.EndVertical();
			GUILayout.EndHorizontal();
		}

		private void DrawSuspensionLive() {
			GUILayout.Label("See what the suspension does instead of guessing from the feel: how far each wheel compresses, how hard the tyre is pressed into the road, " +
				"when it leaves it, and whether it packs down over bumps. Your own car only; it only reads, it changes nothing.", Theme.Small);
			GUILayout.BeginVertical(Theme.Card);
			GUILayout.BeginHorizontal();
			GUILayout.Label("<b>Logging</b>", Theme.Body, GUILayout.Width(150));
			bool on = Choice(new[] { "On", "Off" }, suspensionOn_ ? 0 : 1) == 0;
			GUILayout.EndHorizontal();
			if (on != suspensionOn_) {
				SetSuspensionOn(on);
			}
			GUILayout.Label("Off by default, and off it costs nothing. On, it reads the four wheels every frame while you drive.", Theme.Small);
			GUI.enabled = suspensionOn_;
			GUILayout.BeginHorizontal();
			GUILayout.Label("<b>Overlay</b>", Theme.Body, GUILayout.Width(150));
			bool overlay = Choice(new[] { "Show", "Hide" }, suspensionOverlay_ ? 0 : 1) == 0;
			GUILayout.EndHorizontal();
			if (overlay != suspensionOverlay_) {
				suspensionOverlay_ = overlay;
				SavePref(SuspensionOverlayPref, overlay);
			}
			GUILayout.Label("The car from above: each tyre fills up as it compresses (green, amber getting deep, red at the stops, grey off the road), with its travel, load and damper speed beside it, and the last 4 seconds traced underneath. Drag it anywhere while this window is open.", Theme.Small);
			GUILayout.BeginHorizontal();
			GUILayout.Label("<b>Log</b>", Theme.Body, GUILayout.Width(150));
			int scope = Choice(SuspensionScopes, suspensionScope_);
			GUILayout.EndHorizontal();
			if (scope != suspensionScope_) {
				suspensionScope_ = scope;
				SavePref(SuspensionScopePref, scope);
			}
			GUILayout.BeginHorizontal();
			if (NavButton("Start a new session", Theme.Button)) {
				session_ = null;
				suspensionResult_ = "The session starts again from here.";
			}
			if (suspensionPosition_.x >= 0 && NavButton("Put the overlay back", Theme.Button)) {
				suspensionPosition_ = new Vector2(-1f, -1f);
				SavePref(SuspensionPositionPref, suspensionPosition_);
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUI.enabled = true;
			GUILayout.EndVertical();

			if (!suspensionOn_) {
				return;
			}
			GUILayout.Label("Right now", Theme.SectionLabel);
			GUILayout.BeginVertical(Theme.Card);
			if (liveFrame_ == null) {
				GUILayout.Label(!hasPose_ ? "Drive onto a map: the wheels are read while you drive." : suspensionScope_ == 1 ? "Waiting for a run to start." : "Waiting for the car.", Theme.Small);
			} else {
				SuspensionLog log = runLog_ ?? session_;
				for (int w = 0; w < 4; w++) {
					WheelFrame wf = liveFrame_.Wheels[w];
					float speed = (log?.LastSpeedMs[w] ?? 0f) * 1000f;
					GUILayout.Label($"<b>{SuspensionLog.WheelNames[w]}</b>  {wf.Compression * 100:0}% compressed · {wf.Load / 1000f:0.00} kN · " +
						$"{(wf.Contact ? "on the road" : $"<color={Theme.BadHex}>off the road</color>")} · damper {speed:+0;-0;0} mm/s · slip angle {wf.SlipAngle:0.00}", Theme.Small);
				}
				GUILayout.Label("Standing still on flat ground, each wheel should sit part of the way in (often 30-60%). If they all read near 0% or 100% standing still, tell bskftw: the reading may be the wrong way round.", Theme.Small);
			}
			GUILayout.EndVertical();
		}

		private void DrawSuspensionReport(SuspensionLog log, string empty) {
			if (log == null || log.Seconds < 1) {
				GUILayout.Label(empty, Theme.Small);
				return;
			}
			log.Finish();
			GUILayout.BeginVertical(Theme.Guide);
			GUILayout.Label($"{log.Label}", Theme.Heading);
			GUILayout.Label($"{log.Started.ToString("MMM d HH:mm", CultureInfo.InvariantCulture)} · {log.Seconds:0} s logged · {log.Events.Count} event(s)", Theme.Small);
			GUILayout.BeginHorizontal();
			if (NavButton("Export CSV", Theme.Button)) {
				try {
					string path = log.SaveCsv(SuspensionFolder);
					suspensionResult_ = $"Saved {log.Frames.Count} frames to {path}. It opens in a spreadsheet or a telemetry tool.";
				} catch (Exception e) {
					suspensionResult_ = $"Couldn't save it: {e.Message}";
				}
			}
			if (NavButton(baseline_ == log ? "This is the baseline" : "Compare against this later", Theme.Button)) {
				baseline_ = log;
				suspensionResult_ = "Saved as the baseline: change the tune, drive the same road, and the next report shows what changed.";
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.EndVertical();

			GUILayout.Label("What it points to", Theme.SectionLabel);
			GUILayout.BeginVertical(Theme.Card);
			foreach (string note in log.Advice()) {
				GUILayout.Label($"· {note}", Theme.Small);
			}
			GUILayout.EndVertical();

			GUILayout.Label("Each wheel", Theme.SectionLabel);
			SuspensionLog compare = baseline_ != null && baseline_ != log ? baseline_ : null;
			for (int w = 0; w < 4; w++) {
				WheelReport r = log.Wheels[w];
				WheelReport b = compare?.Wheels[w];
				string Delta(int now, int then) => b == null ? "" : now == then ? " (same)" : $" ({(now > then ? "+" : "")}{now - then} vs baseline)";
				GUILayout.BeginVertical(Theme.Card);
				GUILayout.Label($"<b><color=#{ColorUtility.ToHtmlStringRGB(WheelColors[w])}>{SuspensionLog.WheelNames[w]}</color></b>", Theme.Body);
				GUILayout.Label($"Travel used {r.MinCompression * 100:0}% to {r.MaxCompression * 100:0}% · bottomed {r.Bottomed}{Delta(r.Bottomed, b?.Bottomed ?? 0)} · " +
					$"packed down {r.Packing}{Delta(r.Packing, b?.Packing ?? 0)} · left the road {r.ContactLost}{Delta(r.ContactLost, b?.ContactLost ?? 0)} " +
					$"({r.ContactLostAfterCompression} right after a compression) · load spikes {r.LoadSpikes}", Theme.Small);
				GUILayout.Label($"Damper: bump {r.BumpShare * 100:0}% / rebound {100 - r.BumpShare * 100:0}% of the moving time · fastest bump {r.FastestBumpMmS:0} mm/s, rebound {r.FastestReboundMmS:0} mm/s · " +
					$"average load {r.MeanLoad / 1000f:0.0} kN, peak {r.PeakLoad / 1000f:0.0} kN", Theme.Small);
				DrawHistogram(r, log.Setup[w], b);
				GUILayout.EndVertical();
			}

			if (log.Events.Count > 0) {
				GUILayout.Label("Where it happened (latest 40)", Theme.SectionLabel);
				GUILayout.BeginVertical(Theme.Card);
				foreach (SuspensionEvent e in Enumerable.Reverse(log.Events).Where(e => e.What != SuspensionEvent.Kind.LoadSpike).Take(40)) {
					GUILayout.Label(SuspensionLog.EventText(e), Theme.Small);
				}
				GUILayout.EndVertical();
			}
		}

		/// <summary>
		/// The damper speed histogram, the way data engineers read it: rebound on the left, bump on the right, the bar
		/// height the time spent at that speed, with CarX's slow/fast limits marked. A baseline shows as outlines.
		/// </summary>
		private void DrawHistogram(WheelReport r, CornerSetup setup, WheelReport baseline) {
			Rect area = GUILayoutUtility.GetRect(10f, 90f, GUILayout.ExpandWidth(true));
			GUILayout.Label($"Damper speed, rebound on the left and bump on the right, out to {SuspensionLog.MaxMmS} mm/s each way (faster goes in the end bars). " +
				$"Faint lines: where slow damping ends and fast starts.{(baseline != null ? " White marks: the baseline." : "")}", Theme.Small);
			if (Event.current.type != EventType.Repaint) {
				return;
			}
			Fill(area, new Color(0f, 0f, 0f, 0.25f));
			double most = Math.Max(1e-6, Math.Max(r.Histogram.Max(), baseline?.Histogram.Max() ?? 0));
			float barWidth = area.width / SuspensionLog.Bins;
			for (int i = 0; i < SuspensionLog.Bins; i++) {
				bool bump = i >= SuspensionLog.Bins / 2;
				float h = (float)(r.Histogram[i] / most) * (area.height - 4f);
				Fill(new Rect(area.x + i * barWidth + 1f, area.yMax - 2f - h, barWidth - 2f, h), bump ? new Color(1f, 0.72f, 0.3f, 0.85f) : new Color(0.45f, 0.65f, 1f, 0.85f));
				if (baseline != null) {
					float bh = (float)(baseline.Histogram[i] / most) * (area.height - 4f);
					Fill(new Rect(area.x + i * barWidth + 1f, area.yMax - 2f - bh, barWidth - 2f, 1f), new Color(1f, 1f, 1f, 0.7f));
				}
			}
			float X(float mms) => area.x + area.width / 2f + mms / (2f * SuspensionLog.MaxMmS + 2f * SuspensionLog.BinMmS) * area.width;
			Fill(new Rect(X(0), area.y, 1f, area.height), new Color(1f, 1f, 1f, 0.35f));
			if (setup != null) {
				foreach (float knee in new[] { setup.BumpSlowLimit * 1000f, -setup.ReboundSlowLimit * 1000f, setup.BumpFastLimit * 1000f, -setup.ReboundFastLimit * 1000f }) {
					if (Mathf.Abs(knee) <= SuspensionLog.MaxMmS) {
						Fill(new Rect(X(knee), area.y, 1f, area.height), new Color(1f, 1f, 1f, 0.18f));
					}
				}
			}
			GUI.Label(new Rect(area.x + 4f, area.y + 2f, 200f, 16f), "rebound", suspensionLeft_ ?? Theme.HudSmall);
			GUI.Label(new Rect(area.xMax - 204f, area.y + 2f, 200f, 16f), "bump", suspensionRight_ ?? Theme.HudSmall);
		}

		/// <summary>The suspension from the tune, per corner: what real engineers set up from.</summary>
		private void DrawSetupSheet() {
			if (hasPose_ && adapter_.TryGetSuspensionSetup(out CornerSetup[] corners)) {
				corners_ = corners;
			}
			if (corners_ == null) {
				GUILayout.Label("Drive onto a map: the suspension is read from the car you're driving.", Theme.Small);
				return;
			}
			GUILayout.Label("Worked out from your tune. Frequency is how fast each corner bounces on its spring (higher is stiffer). Damping is a share of critical damping: " +
				"about 0.6-0.8 settles quickly without bouncing, 1.0 never overshoots, low values bounce. These use CarX's own numbers as real units; compare front with rear and one tune with another.", Theme.Small);
			for (int w = 0; w < 4; w++) {
				CornerSetup c = corners_[w];
				GUILayout.BeginVertical(Theme.Card);
				GUILayout.Label($"<b>{SuspensionLog.WheelNames[w]}</b>  <color={Theme.MutedHex}>{c.CornerMass:0} kg on this corner · travel {c.Travel * 1000f:0} mm</color>", Theme.Body);
				GUILayout.Label($"Spring {c.Spring / 1000f:0.0} N/mm · {c.Frequency:0.00} Hz", Theme.Small);
				GUILayout.Label($"Bump: slow {c.Ratio(c.SlowBump):0.00}, fast {c.Ratio(c.FastBump):0.00} (slow up to {c.BumpSlowLimit * 1000f:0} mm/s, fast from {c.BumpFastLimit * 1000f:0} mm/s)", Theme.Small);
				GUILayout.Label($"Rebound: slow {c.Ratio(c.SlowRebound):0.00}, fast {c.Ratio(c.FastRebound):0.00} (slow up to {c.ReboundSlowLimit * 1000f:0} mm/s, fast from {c.ReboundFastLimit * 1000f:0} mm/s)", Theme.Small);
				GUILayout.EndVertical();
			}
		}
	}
}
