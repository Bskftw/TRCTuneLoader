using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// The Suspension tab and overlay (0.16, bskftw: suspension data logging "like in real life", its own tab, a nice
	/// overlay, and switches so it doesn't run all the time). Off by default: while it's off nothing is read or drawn.
	/// On, it reads the four wheels every frame on track (only during runs, if picked), keeps a log for the session
	/// and one for the last run, and the overlay shows the car from above with each tyre filling up as it compresses.
	/// The analysis is in src/Suspension/SuspensionLog.cs.
	/// </summary>
	public partial class TRCTuneLoaderMod {
		private const int SuspensionTab = 8;
		private const string SuspensionPref = "suspension_log";
		private const string SuspensionOverlayPref = "suspension_overlay";
		private const string SuspensionScopePref = "suspension_scope";
		private const string SuspensionPositionPref = "suspension_position";
		private const string SuspensionFolderName = "Suspension";
		private static readonly string[] SuspensionSections = { "Live", "Last run", "Session", "Setup sheet" };
		private static readonly string[] SuspensionScopes = { "Whenever I'm driving", "Only during runs" };

		/// <summary>The four wheels' colours in the trace (front left, front right, rear left, rear right).</summary>
		private static readonly Color[] WheelColors = { new Color(0.45f, 0.75f, 1f), new Color(1f, 0.5f, 0.75f), new Color(0.55f, 0.9f, 0.5f), new Color(1f, 0.78f, 0.35f) };

		private bool suspensionOn_;
		private bool suspensionOverlay_ = true;
		private int suspensionScope_;
		private Vector2 suspensionPosition_ = new Vector2(-1f, -1f);
		private int suspensionView_;
		private Vector2 suspensionScroll_;
		private string suspensionResult_ = "";

		private SuspensionLog session_;
		private SuspensionLog runLog_;
		private SuspensionLog lastRunLog_;
		private SuspensionLog baseline_;
		private TimeAttackRun loggedRun_;
		private SuspensionFrame liveFrame_;
		private CornerSetup[] corners_;
		private float nextSetupRead_;
		private int setupCar_;
		private bool suspensionDragging_;
		private Vector2 suspensionDragFrom_;

		private string SuspensionFolder => Path.Combine(modFolder_, SuspensionFolderName);

		private void LoadSuspensionOptions() {
			suspensionOn_ = Kino.Prefs.Get(SuspensionPref, false);
			suspensionOverlay_ = Kino.Prefs.Get(SuspensionOverlayPref, true);
			suspensionScope_ = Mathf.Clamp(Kino.Prefs.Get(SuspensionScopePref, 0), 0, 1);
			suspensionPosition_ = Kino.Prefs.Get(SuspensionPositionPref, new Vector2(-1f, -1f));
		}

		private void SetSuspensionOn(bool on) {
			suspensionOn_ = on;
			SavePref(SuspensionPref, on);
			if (!on) {
				// Off means off: nothing kept being read, and the memory goes.
				runLog_ = null;
				loggedRun_ = null;
				liveFrame_ = null;
			}
			TrcLog.Info($"Suspension logging {(on ? "on" : "off")}.");
		}

		/// <summary>Called from Update every frame. Does nothing at all while logging is off.</summary>
		private void UpdateSuspension() {
			if (!suspensionOn_ || !hasPose_) {
				return;
			}
			TimeAttackRun run = ActiveRun;
			bool running = run != null && run.State == TimeAttackRun.Phase.Running;
			if (suspensionScope_ == 1 && !running && runLog_ == null) {
				liveFrame_ = null;
				return;
			}
			if (Time.unscaledTime >= nextSetupRead_ || setupCar_ != pose_.CarId) {
				nextSetupRead_ = Time.unscaledTime + 5f;
				setupCar_ = pose_.CarId;
				if (adapter_.TryGetSuspensionSetup(out CornerSetup[] corners)) {
					corners_ = corners;
				}
			}
			var frame = new SuspensionFrame { Time = Time.timeAsDouble };
			if (!adapter_.TryGetSuspension(frame)) {
				liveFrame_ = null;
				return;
			}
			liveFrame_ = frame;
			if (session_ == null) {
				session_ = NewSuspensionLog(map_?.Name ?? "Free roam", null);
			}
			session_.Setup = corners_ ?? session_.Setup;
			session_.Add(frame);

			// The run being driven gets a log of its own; when it ends, that's "Last run".
			if (running && loggedRun_ != run) {
				loggedRun_ = run;
				runLog_ = NewSuspensionLog(run.Course.Name, run.Course);
			}
			if (runLog_ != null) {
				runLog_.Setup = corners_ ?? runLog_.Setup;
				runLog_.Add(frame);
				if (!running) {
					runLog_.Finish();
					if (runLog_.Seconds >= 3) {
						lastRunLog_ = runLog_;
						TrcLog.Info($"Suspension: logged {runLog_.Label}, {runLog_.Seconds:0} s, {runLog_.Events.Count} event(s).");
					}
					runLog_ = null;
					loggedRun_ = null;
				}
			}
		}

		private SuspensionLog NewSuspensionLog(string label, Course course) {
			var log = new SuspensionLog { Label = label, Started = DateTime.Now, Setup = corners_ ?? new CornerSetup[4] };
			if (course != null && course.HasPath) {
				log.AlongCourse = p => CourseGeometry.Project(course.Path, course.Distances, p, 0, course.Path.Count).Progress;
			}
			return log;
		}

		// ---- The overlay ----

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
		private void HandleSuspensionDrag(Rect rect) {
			Event e = Event.current;
			if (!windowOpen_) {
				suspensionDragging_ = false;
				return;
			}
			if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition) && !windowRect_.Contains(e.mousePosition)) {
				suspensionDragging_ = true;
				suspensionDragFrom_ = e.mousePosition - rect.position;
				e.Use();
			} else if (e.type == EventType.MouseDrag && suspensionDragging_) {
				suspensionPosition_ = e.mousePosition - suspensionDragFrom_;
				e.Use();
			} else if (e.type == EventType.MouseUp && suspensionDragging_) {
				suspensionDragging_ = false;
				SavePref(SuspensionPositionPref, suspensionPosition_);
				e.Use();
			}
		}

		// ---- The tab ----

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
