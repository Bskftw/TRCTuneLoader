// Short notices near the bottom of the screen (a shared course, what a button did).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		private string notice_ = "";
		private float noticeUntil_;

		private void ShowNotice(string text, float seconds = 6f) {
			notice_ = text;
			noticeUntil_ = Time.unscaledTime + seconds;
		}

		private bool NoticeVisible => Time.unscaledTime < noticeUntil_ && notice_.Length > 0;

		/// <summary>A short notice near the bottom of the screen (a shared course, what the wheel button did).</summary>
		private void DrawNotice(float screenWidth, float screenHeight) {
			if (!NoticeVisible || Event.current.type != EventType.Repaint) {
				return;
			}
			var content = new GUIContent(notice_);
			float width = Mathf.Min(360f, screenWidth - 2 * HudMargin);
			float height = Theme.HudPill.CalcHeight(content, width);
			GUI.Label(NoticeRect(width, height, screenWidth, screenHeight), content, Theme.HudPill);
		}
	}
}
