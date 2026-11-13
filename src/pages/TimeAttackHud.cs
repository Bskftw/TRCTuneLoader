// Time attack over the track: the timer (and dragging it), the start and finish lines' labels, and the FINISH and sector pop-ups.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		/// <summary>Start and finish lines further than this from the camera (metres) aren't drawn.</summary>
		private const float MarkerRange = 100f;

		private bool draggingHud_;
		private Vector2 hudGrab_;

		/// <summary>Where the timer or message went this OnGUI pass (empty when nothing has the spot), so a challenge can show beside it.</summary>
		private Rect hudRect_;

		/// <summary>Time attack or the course builder is showing something over the track.</summary>
		private bool CourseHudVisible => hasPose_ && (BattleOnTrack || run_ != null || pathRecorder_ != null || recorder_.State != CourseRecorder.Phase.Idle || invites_.Count > 0 ||
			RaceHudVisible || RaceToShow() != null || gridRecorder_ != null || TrapFlashVisible || SectorFlashVisible || FinishFlashVisible);

		/// <summary>Called from OnGUI while time attack or the course builder runs: the lines on the road and the panel at the top.</summary>
		private void DrawCourseHud(float screenWidth, float screenHeight, float scale) {
			// The lines are 3D objects (CourseMarkers); drawn over the screen only when no shader for those was found.
			if (Event.current.type == EventType.Repaint && !CourseMarkers.Available) {
				Camera camera = HudCamera();
				Course course = (BattleOnTrack ? battle_.Course : null) ?? run_?.Course ?? pathRecorder_?.Course;
				if (camera != null) {
					DrawGate(camera, course != null ? course.Start : recorder_.Start, Theme.StartLine, "START", Theme.MarkerStart, scale);
					DrawGate(camera, course != null ? course.Finish : recorder_.Finish, Theme.FinishLine, "FINISH", Theme.MarkerFinish, scale);
				}
			}

			hudRect_ = Rect.zero;
			if (RaceHudVisible) {
				DrawRaceHud(screenWidth, screenHeight);
			} else if (BattleOnTrack) {
				DrawBattleHud(screenWidth, screenHeight);
			} else if (gridRecorder_ != null) {
				DrawHudPill(screenWidth, screenHeight, $"<b>Lay out grid · {gridRecorder_.Course.Name}</b> · {TimeAttackRun.Distance(gridRecorder_.Recorded)}\n<color={Theme.MutedHex}>{gridRecorder_.Message}</color>");
			} else if (run_ != null) {
				DrawRunHud(screenWidth, screenHeight);
			} else if (pathRecorder_ != null) {
				string recorded = pathRecorder_.State == PathRecorder.Phase.ToStart ? "" : $" · {TimeAttackRun.Distance(pathRecorder_.Recorded)}";
				DrawHudPill(screenWidth, screenHeight, $"<b>Record path · {pathRecorder_.Course.Name}</b>{recorded}\n<color={Theme.MutedHex}>{pathRecorder_.Message}</color>");
			} else if (recorder_.State != CourseRecorder.Phase.Idle) {
				string next = !recorder_.Circuit ? $"Stop on the finish line, then {shortcut_}: Set finish here"
					: recorder_.Recorded < CourseGeometry.CircuitMinLength ? "Drive once round the loop"
					: $"Back through the start line closes the loop: {TimeAttackRun.Distance(recorder_.DistanceToStart)}";
				DrawHudPill(screenWidth, screenHeight, recorder_.State == CourseRecorder.Phase.Recording
					? $"<b>Recording the {(recorder_.Circuit ? "circuit" : "course")}</b> · {TimeAttackRun.Distance(recorder_.Recorded)}\n<color={Theme.MutedHex}>{next}</color>"
					: $"<b>{(recorder_.Circuit ? "Loop closed" : "Course recorded")}</b>\n<color={Theme.MutedHex}>{shortcut_} to name and save it</color>");
			}
			// A challenge always shows, even during a time attack or a recording; so does a race someone just opened.
			if (invites_.Count > 0) {
				DrawInviteHud(screenWidth, screenHeight);
			} else if (RaceToShow() != null) {
				DrawRaceInviteHud(screenWidth, screenHeight);
			}
			DrawTrapFlash(screenWidth, screenHeight);
			DrawSectorFlash(screenWidth, screenHeight);
			DrawFinishFlash(screenWidth, screenHeight);
		}

		private void DrawRunHud(float screenWidth, float screenHeight) {
			double now = Time.timeAsDouble;
			string big = run_.State == TimeAttackRun.Phase.Countdown ? run_.CountdownNumber.ToString(CultureInfo.InvariantCulture)
				: run_.ShowGo(now) ? "GO!"
				: null;
			if (big != null) {
				GUI.Label(new Rect(0f, screenHeight * 0.3f, screenWidth, 160f), big, Theme.HudBig);
			}

			switch (run_.State) {
				case TimeAttackRun.Phase.Running:
					DrawRunTimer(screenWidth, screenHeight);
					break;
				case TimeAttackRun.Phase.Finished:
				case TimeAttackRun.Phase.Cancelled:
					DrawHudPill(screenWidth, screenHeight, $"<b>{run_.Course.Name}</b>\n{runResultText_}");
					break;
				default:
					DrawHudPill(screenWidth, screenHeight, $"<b>{run_.Course.Name}</b>\n<color={Theme.MutedHex}>{run_.Message}</color>");
					break;
			}
		}

		/// <summary>
		/// The running clock: the time (<paramref name="main"/>, else the run's), a thin line under it for how far
		/// along the course the car is, and short lines under that, like "Ahead by 12 m" or "Lap 2/5".
		/// </summary>
		private static readonly GUIContent TimerSample = new GUIContent("00:00.000");
		private GUIStyle timerWidthStyle_;
		private int timerWidthSize_ = -1;
		private Font timerWidthFont_;
		private float timerWidth_;

		/// <summary>How wide "00:00.000" is in the timer's text: measured again only when the style or its size changes (item 6).</summary>
		private float TimerTextWidth() {
			GUIStyle style = Theme.HudTimer;
			if (!ReferenceEquals(style, timerWidthStyle_) || style.fontSize != timerWidthSize_ || style.font != timerWidthFont_) {
				timerWidthStyle_ = style;
				timerWidthSize_ = style.fontSize;
				timerWidthFont_ = style.font;
				timerWidth_ = style.CalcSize(TimerSample).x;
			}
			return timerWidth_;
		}

		/// <summary>The delta bar is full at this many seconds ahead or behind.</summary>
		private const float DeltaBarSeconds = 1f;

		/// <param name="deltaBar">Time attacks: the live delta, drawn as a bar under the time (green to the right when ahead, red to the left when behind).</param>
		private void DrawTimer(float screenWidth, float screenHeight, TimeAttackRun run, string below = null, double? main = null, double? deltaBar = null) {
			int lines = string.IsNullOrEmpty(below) ? 0 : below.Split('\n').Length;
			float width = Mathf.Max(lines > 0 ? 240f : 150f, TimerTextWidth() + 32f);
			bool extra = lines > 0;
			float barSpace = deltaBar.HasValue ? 10f : 0f;
			Rect rect = HudRect(screenWidth, screenHeight, width, 50f + 18f * lines + barSpace);
			hudRect_ = rect;
			HandleHudDrag(rect, screenWidth, screenHeight);
			if (Event.current.type != EventType.Repaint) {
				return;
			}

			GUI.Box(rect, GUIContent.none, Theme.HudPanel);
			GUI.Label(new Rect(rect.x, rect.y + 3f, rect.width, 36f), TimeAttackRun.FormatTime(main ?? run.Elapsed), Theme.HudTimer);
			if (deltaBar is double delta) {
				DrawDeltaBar(new Rect(rect.x + 14f, rect.y + 40f, rect.width - 28f, 5f), delta);
			}
			if (extra) {
				GUI.Label(new Rect(rect.x, rect.y + 38f + barSpace, rect.width, 18f * lines), below, Theme.HudSmall);
			}
			var bar = new Rect(rect.x + 14f, rect.yMax - 9f, rect.width - 28f, 3f);
			float done = run.Course.Length > 0 ? Mathf.Clamp01(run.Progress / run.Course.Length) : 0f;
			GUI.DrawTexture(bar, Theme.ProgressBack);
			GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * done, bar.height), Theme.AccentLine);
			DrawMoveHint(rect);
		}

		/// <summary>The live delta as a bar from the middle: green to the right when ahead, red to the left when behind (full at 1 s).</summary>
		private static void DrawDeltaBar(Rect bar, double delta) {
			GUI.DrawTexture(bar, Theme.ProgressBack);
			float half = bar.width / 2f;
			float middle = bar.x + half;
			float size = Mathf.Clamp01((float)Math.Abs(delta) / DeltaBarSeconds) * half;
			if (delta <= 0) {
				GUI.DrawTexture(new Rect(middle, bar.y, size, bar.height), Theme.GoodFill);
			} else {
				GUI.DrawTexture(new Rect(middle - size, bar.y, size, bar.height), Theme.BadFill);
			}
			GUI.DrawTexture(new Rect(middle - 1f, bar.y - 2f, 2f, bar.height + 4f), Theme.Divider);
		}

		/// <summary>A short message in the timer's spot: where to go before the start, the result after the finish.</summary>
		private void DrawHudPill(float screenWidth, float screenHeight, string text) {
			var content = new GUIContent(text);
			float width = Mathf.Min(340f, screenWidth - 2 * HudMargin);
			Rect rect = HudRect(screenWidth, screenHeight, width, Theme.HudPill.CalcHeight(content, width));
			hudRect_ = rect;
			HandleHudDrag(rect, screenWidth, screenHeight);
			if (Event.current.type == EventType.Repaint) {
				GUI.Label(rect, content, Theme.HudPill);
				DrawMoveHint(rect);
			}
		}

		/// <summary>While the TRC window is open (so the mouse is free), the timer can be dragged anywhere. The place is saved.</summary>
		private void HandleHudDrag(Rect rect, float screenWidth, float screenHeight) {
			if (!windowOpen_) {
				draggingHud_ = false;
				return;
			}
			Event e = Event.current;
			switch (e.type) {
				case EventType.MouseDown:
					if (e.button == 0 && rect.Contains(e.mousePosition)) {
						draggingHud_ = true;
						hudGrab_ = e.mousePosition - new Vector2(rect.center.x, rect.y);
						e.Use();
					}
					break;
				case EventType.MouseDrag:
					if (draggingHud_) {
						Vector2 at = e.mousePosition - hudGrab_;
						hudPosition_ = new Vector2(Mathf.Clamp01(at.x / screenWidth), Mathf.Clamp01(at.y / screenHeight));
						e.Use();
					}
					break;
				case EventType.MouseUp:
					if (draggingHud_) {
						draggingHud_ = false;
						SavePref(HudPositionPref, hudPosition_);
						e.Use();
					}
					break;
			}
		}

		/// <summary>An outline and "drag to move" under the timer while the window is open.</summary>
		private void DrawMoveHint(Rect rect) {
			if (!windowOpen_) {
				return;
			}
			GUI.Box(rect, GUIContent.none, Theme.HudOutline);
			GUI.Label(new Rect(rect.x, rect.yMax + 2f, rect.width, 18f), "drag to move", Theme.HudSmall);
		}

		/// <summary>A start or finish line drawn on the road, with its name above the middle.</summary>
		private static void DrawGate(Camera camera, CourseGate gate, Texture2D line, string label, GUIStyle labelStyle, float scale) {
			if (gate == null || Vector3.Distance(camera.transform.position, gate.Center) > MarkerRange) {
				return;
			}
			Vector3 lift = Vector3.up * 0.3f;
			if (ToGui(camera, gate.LeftEnd + lift, scale, out Vector2 left) && ToGui(camera, gate.RightEnd + lift, scale, out Vector2 right)) {
				DrawLine(left, right, 4f, line);
			}
			if (ToGui(camera, gate.Center + Vector3.up * 2.5f, scale, out Vector2 top)) {
				var content = new GUIContent(label);
				Vector2 size = labelStyle.CalcSize(content);
				GUI.Label(new Rect(top.x - size.x / 2f, top.y - size.y, size.x, size.y), content, labelStyle);
			}
		}

		/// <summary>Where a point in the world is in the window's (scaled) coordinates. False when it's behind the camera.</summary>
		private static bool ToGui(Camera camera, Vector3 world, float scale, out Vector2 gui) {
			Vector3 viewport = camera.WorldToViewportPoint(world);
			gui = new Vector2(viewport.x * Screen.width / scale, (1f - viewport.y) * Screen.height / scale);
			return viewport.z > 0.5f;
		}

		private static void DrawLine(Vector2 from, Vector2 to, float width, Texture2D texture) {
			Matrix4x4 saved = GUI.matrix;
			float angle = Mathf.Atan2(to.y - from.y, to.x - from.x) * Mathf.Rad2Deg;
			GUIUtility.RotateAroundPivot(angle, from);
			GUI.DrawTexture(new Rect(from.x, from.y - width / 2f, (to - from).magnitude, width), texture);
			GUI.matrix = saved;
		}

		/// <summary>The camera the player sees the track through.</summary>
		private static Camera HudCamera() {
			Camera main = Camera.main;
			if (main != null && main.isActiveAndEnabled) {
				return main;
			}
			Camera best = null;
			foreach (Camera camera in Camera.allCameras) {
				if (camera.targetTexture == null && (best == null || camera.depth > best.depth)) {
					best = camera;
				}
			}
			return best;
		}

		private string finishFlash_ = "";
		private float finishFlashUntil_;
		private const float FinishFlashSeconds = 3f;

		private bool FinishFlashVisible => hasPose_ && Time.unscaledTime < finishFlashUntil_;

		/// <summary>FINISH across the screen where GO was, with the time (and place in a race, and a new PB) under it.</summary>
		private void DrawFinishFlash(float screenWidth, float screenHeight) {
			if (!FinishFlashVisible || Event.current.type != EventType.Repaint) {
				return;
			}
			GUI.Label(new Rect(0f, screenHeight * 0.3f, screenWidth, 160f), "FINISH", Theme.HudBig);
			var content = new GUIContent(finishFlash_);
			float width = Mathf.Min(360f, screenWidth - 2 * HudMargin);
			float height = Theme.HudPill.CalcHeight(content, width);
			GUI.Label(new Rect((screenWidth - width) / 2f, screenHeight * 0.3f + 150f, width, height), content, Theme.HudPill);
		}

		// ---- The sector pop-up (bskftw, Nov 3: like the speed trap one, with the sector's delta) ----

		private string sectorFlash_ = "";
		private float sectorFlashUntil_;
		private const float SectorFlashSeconds = 3.5f;

		private bool SectorFlashVisible => hasPose_ && Time.unscaledTime < sectorFlashUntil_;

		/// <summary>A sector just ended: its time in its pace colour (time attacks) and how far ahead of or behind the PB the lap is there.</summary>
		private void FlashSector(TimeAttackRun run, SectorSplit split) {
			string colour = "#FFFFFF";
			if (run == run_) {
				long ms = (long)Math.Round(split.Time * 1000.0);
				long? all = split.Index < sectorBestAll_.Length ? sectorBestAll_[split.Index] : null;
				long? mine = split.Index < sectorBestMine_.Length ? sectorBestMine_[split.Index] : null;
				colour = PaceColour(ms, all, mine);
			}
			string gap;
			if (split.Delta is double delta) {
				double rounded = Math.Round(delta, 3);
				string amount = Math.Abs(rounded).ToString("0.000", CultureInfo.InvariantCulture);
				gap = rounded < 0 ? $"<color={Theme.GoodHex}><b>-{amount}</b> ahead of PB</color>"
					: rounded > 0 ? $"<color={Theme.WarnHex}><b>+{amount}</b> behind PB</color>"
					: "level with PB";
			} else {
				gap = $"<color={Theme.MutedHex}>no PB to compare with yet</color>";
			}
			if (run == run_ && timingDetailed_ && bestAnyCarName_.Length > 0) {
				gap += split.CarDelta is double car
					? $"\n<color={Theme.MutedHex}>{TimeAttackRun.FormatGap(car)} on this car's best · PB in {ShortCar(bestAnyCarName_)}</color>"
					: $"\n<color={Theme.MutedHex}>No time in this car yet · PB in {ShortCar(bestAnyCarName_)}</color>";
			}
			sectorFlash_ = $"<b>SECTOR {split.Index + 1}</b>  <color={colour}><b>{TimeAttackRun.FormatTime(split.Time)}</b></color>\n{gap}";
			sectorFlashUntil_ = Time.unscaledTime + SectorFlashSeconds;
		}

		private void DrawSectorFlash(float screenWidth, float screenHeight) {
			if (Time.unscaledTime >= sectorFlashUntil_ || sectorFlash_.Length == 0) {
				return;
			}
			var content = new GUIContent(sectorFlash_);
			float width = Mathf.Min(300f, screenWidth - 2 * HudMargin);
			float height = Theme.HudPill.CalcHeight(content, width);
			if (Event.current.type == EventType.Repaint) {
				GUI.Label(SectorFlashRect(width, height, screenWidth, screenHeight), content, Theme.HudPill);
			}
		}
	}
}
