// Where the on-screen boxes sit (timer, speed trap flash, sector pop-up, notices, TRC spec badge): moved by the player or in their usual place.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
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

		private void LoadLayout() {
			trapFlashPosition_ = Kino.Prefs.Get(TrapFlashPositionPref, Unmoved);
			noticePosition_ = Kino.Prefs.Get(NoticePositionPref, Unmoved);
			badgePosition_ = Kino.Prefs.Get(BadgePositionPref, Unmoved);
			sectorFlashPosition_ = Kino.Prefs.Get(SectorFlashPositionPref, Unmoved);
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

		private const string HudPositionPref = "timer_position";

		/// <summary>The timer's top centre, as fractions of the screen: top middle.</summary>
		private static readonly Vector2 DefaultHudPosition = new Vector2(0.5f, 0.08f);

		private Vector2 hudPosition_ = DefaultHudPosition;

		private const float HudGap = 8f;

		/// <summary>The timer's place: its top centre sits at <see cref="hudPosition_"/> (fractions of the screen), kept on screen.</summary>
		private Rect HudRect(float screenWidth, float screenHeight, float width, float height) {
			float x = Mathf.Clamp(hudPosition_.x * screenWidth - width / 2f, 0f, Mathf.Max(0f, screenWidth - width));
			float y = Mathf.Clamp(hudPosition_.y * screenHeight, 0f, Mathf.Max(0f, screenHeight - height));
			return new Rect(x, y, width, height);
		}
	}
}
