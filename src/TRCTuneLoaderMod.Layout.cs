using System;
using System.Collections.Generic;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// Arrange the screen (0.16, bskftw: "an option in the options tab to rearrange all the ui window positions ...
	/// you kinda have to start races and speed traps first to move them"). The window steps aside and every box the
	/// mod draws over the track shows at once with sample text, to drag where you want: the timer (time attacks,
	/// races and battles), the speed trap flash, pop-up notices, the TRC spec badge and the suspension overlay.
	/// </summary>
	public partial class TRCTuneLoaderMod {
		private const string TrapFlashPositionPref = "trap_flash_position";
		private const string NoticePositionPref = "notice_position";
		private const string BadgePositionPref = "badge_position";
		private const string SectorFlashPositionPref = "sector_flash_position";

		/// <summary>Not moved yet: the box stays where it always was.</summary>
		private static readonly Vector2 Unmoved = new Vector2(-1f, -1f);

		/// <summary>Where each box's top middle sits, in fractions of the screen (Unmoved: its usual place).</summary>
		private Vector2 trapFlashPosition_ = Unmoved;
		private Vector2 noticePosition_ = Unmoved;
		private Vector2 badgePosition_ = Unmoved;
		private Vector2 sectorFlashPosition_ = Unmoved;

		private bool arranging_;
		private int arrangeDrag_ = -1;
		private Vector2 arrangeGrab_;

		private void LoadLayout() {
			trapFlashPosition_ = Kino.Prefs.Get(TrapFlashPositionPref, Unmoved);
			noticePosition_ = Kino.Prefs.Get(NoticePositionPref, Unmoved);
			badgePosition_ = Kino.Prefs.Get(BadgePositionPref, Unmoved);
			sectorFlashPosition_ = Kino.Prefs.Get(SectorFlashPositionPref, Unmoved);
		}

		private void StartArranging() {
			arranging_ = true;
			arrangeDrag_ = -1;
			TrcLog.Info("Arranging the screen.");
		}

		private void StopArranging() {
			arranging_ = false;
			arrangeDrag_ = -1;
			SavePref(HudPositionPref, hudPosition_);
			SavePref(TrapFlashPositionPref, trapFlashPosition_);
			SavePref(NoticePositionPref, noticePosition_);
			SavePref(BadgePositionPref, badgePosition_);
			SavePref(SectorFlashPositionPref, sectorFlashPosition_);
			SavePref(SuspensionPositionPref, suspensionPosition_);
		}

		private void ResetLayout() {
			hudPosition_ = DefaultHudPosition;
			trapFlashPosition_ = Unmoved;
			noticePosition_ = Unmoved;
			badgePosition_ = Unmoved;
			sectorFlashPosition_ = Unmoved;
			suspensionPosition_ = new Vector2(-1f, -1f);
			StopArranging();
			arranging_ = false;
		}

		/// <summary>A box of this size whose top middle sits at <paramref name="at"/> (fractions of the screen), kept on screen.</summary>
		private static Rect PlacedRect(Vector2 at, float width, float height, float screenWidth, float screenHeight) {
			float x = Mathf.Clamp(at.x * screenWidth - width / 2f, 0f, Mathf.Max(0f, screenWidth - width));
			float y = Mathf.Clamp(at.y * screenHeight, 0f, Mathf.Max(0f, screenHeight - height));
			return new Rect(x, y, width, height);
		}

		/// <summary>The speed trap flash: where it was put, or a little under the timer (as before).</summary>
		private Rect TrapFlashRect(float width, float height, float screenWidth, float screenHeight) {
			if (trapFlashPosition_.x >= 0) {
				return PlacedRect(trapFlashPosition_, width, height, screenWidth, screenHeight);
			}
			Rect timer = HudRect(screenWidth, screenHeight, 300f, 70f);
			float y = Mathf.Min(screenHeight - height, timer.yMax + HudGap + 70f);
			return new Rect((screenWidth - width) / 2f, y, width, height);
		}

		/// <summary>The sector pop-up: where it was put, or under the speed trap flash's spot.</summary>
		private Rect SectorFlashRect(float width, float height, float screenWidth, float screenHeight) {
			if (sectorFlashPosition_.x >= 0) {
				return PlacedRect(sectorFlashPosition_, width, height, screenWidth, screenHeight);
			}
			Rect trap = TrapFlashRect(320f, 56f, screenWidth, screenHeight);
			return new Rect((screenWidth - width) / 2f, Mathf.Min(screenHeight - height, trap.yMax + HudGap), width, height);
		}

		/// <summary>Pop-up notices: where they were put, or near the bottom in the middle (as before).</summary>
		private Rect NoticeRect(float width, float height, float screenWidth, float screenHeight) =>
			noticePosition_.x >= 0 ? PlacedRect(noticePosition_, width, height, screenWidth, screenHeight)
				: new Rect((screenWidth - width) / 2f, screenHeight - height - 90f, width, height);

		/// <summary>The TRC spec badge: where it was put, or the corner picked in Options.</summary>
		private Rect BadgeRect(Vector2 size, float screenWidth, float screenHeight) {
			if (badgePosition_.x >= 0) {
				return PlacedRect(badgePosition_, size.x, size.y, screenWidth, screenHeight);
			}
			float x = badgeCorner_ % 2 == 0 ? HudMargin : screenWidth - size.x - HudMargin;
			float y = badgeCorner_ < 2 ? HudMargin : screenHeight - size.y - HudMargin;
			return new Rect(x, y, size.x, size.y);
		}

		private sealed class ArrangeBox {
			public string Name;
			public string Sample;
			public Rect Rect;
			public GUIStyle Style;

			/// <summary>Moves the box so its top-left is at this point.</summary>
			public Action<Vector2> MoveTo;
		}

		/// <summary>Every movable box at once, with sample text, to drag; and a bar to finish.</summary>
		private void DrawArranging(float screenWidth, float screenHeight) {
			var boxes = new List<ArrangeBox>();
			var timer = new GUIContent("<b>1:23.456</b>\n<color=#C0DD97>-0.412</color>  S1 S2 S3");
			Rect timerRect = HudRect(screenWidth, screenHeight, 300f, 70f);
			boxes.Add(new ArrangeBox {
				Name = "Timer: time attacks, races and battles", Sample = timer.text, Rect = timerRect, Style = Theme.HudPill,
				MoveTo = p => hudPosition_ = new Vector2(Mathf.Clamp01((p.x + 150f) / screenWidth), Mathf.Clamp01(p.y / screenHeight)),
			});
			string flash = "<b>SPEED TRAP · Tsubaki 1</b>\n<color=#C9A7FF><b>187 km/h</b></color>  new best";
			float flashHeight = Theme.HudPill.CalcHeight(new GUIContent(flash), 320f);
			boxes.Add(new ArrangeBox {
				Name = "Speed trap flash", Sample = flash, Rect = TrapFlashRect(320f, flashHeight, screenWidth, screenHeight), Style = Theme.HudPill,
				MoveTo = p => trapFlashPosition_ = new Vector2(Mathf.Clamp01((p.x + 160f) / screenWidth), Mathf.Clamp01(p.y / screenHeight)),
			});
			string sector = "<b>SECTOR 2</b>  <color=#C9A7FF><b>21.034</b></color>\n<color=#7FB843><b>-0.842</b> ahead of PB</color>";
			float sectorHeight = Theme.HudPill.CalcHeight(new GUIContent(sector), 300f);
			boxes.Add(new ArrangeBox {
				Name = "Sector pop-up", Sample = sector, Rect = SectorFlashRect(300f, sectorHeight, screenWidth, screenHeight), Style = Theme.HudPill,
				MoveTo = p => sectorFlashPosition_ = new Vector2(Mathf.Clamp01((p.x + 150f) / screenWidth), Mathf.Clamp01(p.y / screenHeight)),
			});
			string notice = "<b>Frog shared a course</b>\nAkina Downhill · open the window to save it";
			float noticeHeight = Theme.HudPill.CalcHeight(new GUIContent(notice), 360f);
			boxes.Add(new ArrangeBox {
				Name = "Pop-up notices", Sample = notice, Rect = NoticeRect(360f, noticeHeight, screenWidth, screenHeight), Style = Theme.HudPill,
				MoveTo = p => noticePosition_ = new Vector2(Mathf.Clamp01((p.x + 180f) / screenWidth), Mathf.Clamp01(p.y / screenHeight)),
			});
			var badge = new GUIContent("TRC SPEC · Revival");
			Vector2 badgeSize = Theme.BadgeGood.CalcSize(badge);
			boxes.Add(new ArrangeBox {
				Name = "TRC spec badge", Sample = badge.text, Rect = BadgeRect(badgeSize, screenWidth, screenHeight), Style = Theme.BadgeGood,
				MoveTo = p => badgePosition_ = new Vector2(Mathf.Clamp01((p.x + badgeSize.x / 2f) / screenWidth), Mathf.Clamp01(p.y / screenHeight)),
			});
			Vector2 sus = suspensionPosition_.x >= 0 ? suspensionPosition_ : new Vector2(16f, screenHeight - 262f - 16f);
			boxes.Add(new ArrangeBox {
				Name = "Suspension overlay", Sample = "<b>SUSPENSION</b>\n\nthe car from above, each tyre's travel,\nload and damper speed, and a 4 s trace",
				Rect = new Rect(Mathf.Clamp(sus.x, 0f, screenWidth - 300f), Mathf.Clamp(sus.y, 0f, screenHeight - 262f), 300f, 262f), Style = Theme.HudPill,
				MoveTo = p => suspensionPosition_ = p,
			});

			Event e = Event.current;
			if (e.type == EventType.MouseDown && e.button == 0) {
				for (int i = boxes.Count - 1; i >= 0; i--) {
					if (boxes[i].Rect.Contains(e.mousePosition)) {
						arrangeDrag_ = i;
						arrangeGrab_ = e.mousePosition - boxes[i].Rect.position;
						e.Use();
						break;
					}
				}
			} else if (e.type == EventType.MouseDrag && arrangeDrag_ >= 0 && arrangeDrag_ < boxes.Count) {
				ArrangeBox box = boxes[arrangeDrag_];
				Vector2 to = e.mousePosition - arrangeGrab_;
				to.x = Mathf.Clamp(to.x, 0f, Mathf.Max(0f, screenWidth - box.Rect.width));
				to.y = Mathf.Clamp(to.y, 0f, Mathf.Max(0f, screenHeight - box.Rect.height));
				box.MoveTo(to);
				e.Use();
			} else if (e.type == EventType.MouseUp && arrangeDrag_ >= 0) {
				arrangeDrag_ = -1;
				e.Use();
			}

			foreach (ArrangeBox box in boxes) {
				GUI.Label(box.Rect, box.Sample, box.Style);
				GUI.Box(box.Rect, GUIContent.none, Theme.HudOutline);
				GUI.Label(new Rect(box.Rect.x, box.Rect.yMax + 2f, Mathf.Max(box.Rect.width, 220f), 18f), box.Name, Theme.HudSmall);
			}

			// The bar to finish, at the top in the middle.
			var bar = new Rect((screenWidth - 520f) / 2f, 12f, 520f, 44f);
			GUI.Box(bar, GUIContent.none, Theme.HudPanel);
			GUI.Label(new Rect(bar.x + 14f, bar.y + 12f, 260f, 20f), "<b>Drag the boxes where you want them</b>", Theme.Body);
			if (GUI.Button(new Rect(bar.xMax - 230f, bar.y + 7f, 110f, 30f), "Put back", Theme.Button)) {
				ResetLayout();
				StartArranging();
			}
			if (GUI.Button(new Rect(bar.xMax - 112f, bar.y + 7f, 100f, 30f), "Done", Theme.ButtonPrimary)) {
				StopArranging();
			}
		}
	}
}
