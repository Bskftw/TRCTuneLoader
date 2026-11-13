// Battles over the track: the battle and highway HUD, and a challenge invite.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		/// <summary>A highway battle: the rolling countdown, then both SP bars (yours and theirs) and the gap.</summary>
		private void DrawHighwayHud(float screenWidth, float screenHeight, BattleSession b) {
			if (b.State == BattleSession.Phase.Countdown && b.Message.Length > 0) {
				GUI.Label(new Rect(0f, screenHeight * 0.3f, screenWidth, 160f), b.Message, Theme.HudBig);
				return;
			}
			if (b.State != BattleSession.Phase.Running) {
				DrawHudPill(screenWidth, screenHeight, $"<b>Highway battle vs {b.PeerName}</b>\n<color={Theme.MutedHex}>{b.Message}</color>");
				return;
			}
			float width = Mathf.Min(340f, screenWidth - 2 * HudMargin);
			Rect rect = HudRect(screenWidth, screenHeight, width, 78f);
			hudRect_ = rect;
			HandleHudDrag(rect, screenWidth, screenHeight);
			if (Event.current.type != EventType.Repaint) {
				return;
			}
			GUI.Box(rect, GUIContent.none, Theme.HudPanel);
			void Bar(float y, string who, float sp, Texture2D fill) {
				GUI.Label(new Rect(rect.x + 12f, y, 90f, 18f), who, Theme.HudSmall);
				var back = new Rect(rect.x + 100f, y + 5f, rect.width - 150f, 8f);
				GUI.DrawTexture(back, Theme.ProgressBack);
				GUI.DrawTexture(new Rect(back.x, back.y, back.width * Mathf.Clamp01(sp / BattleSession.FullSp), back.height), fill);
				GUI.Label(new Rect(back.xMax + 6f, y, 40f, 18f), $"{sp:0}", Theme.HudSmall);
			}
			Bar(rect.y + 8f, "You", b.MySp, Theme.AccentLine);
			Bar(rect.y + 30f, b.PeerName, b.PeerSp, Theme.FinishLine);
			string gap = Mathf.Abs(b.HighwayGap) < 3f ? "Side by side" : b.HighwayGap > 0 ? $"Ahead by {b.HighwayGap:0} m" : $"Behind by {-b.HighwayGap:0} m: losing SP";
			GUI.Label(new Rect(rect.x, rect.y + 52f, rect.width, 20f), gap, Theme.HudSmall);
			DrawMoveHint(rect);
		}

		/// <summary>The battle over the track: the countdown, the timer with the gap, and messages.</summary>
		private void DrawBattleHud(float screenWidth, float screenHeight) {
			BattleSession b = battle_;
			if (b.IsHighway) {
				DrawHighwayHud(screenWidth, screenHeight, b);
				return;
			}
			TimeAttackRun run = b.Run;
			if (b.State == BattleSession.Phase.Countdown && run.CountdownNumber > 0) {
				GUI.Label(new Rect(0f, screenHeight * 0.3f, screenWidth, 160f), run.CountdownNumber.ToString(CultureInfo.InvariantCulture), Theme.HudBig);
			} else if (b.State == BattleSession.Phase.Running && run.ShowGo(BattleNow)) {
				GUI.Label(new Rect(0f, screenHeight * 0.3f, screenWidth, 160f), "GO!", Theme.HudBig);
			}

			string title = $"<b>vs {b.PeerName}</b> · {b.Course.Name}";
			if (b.Format == BattleFormat.CatAndMouse && b.Active) {
				title += $" · {(b.IAmLeader ? "you lead" : "you chase")}";
			}
			if (b.State == BattleSession.Phase.Running && run.Active) {
				float gap = shownGap_;
				string place = Mathf.Abs(gap) < SideBySideMetres ? "Side by side" : gap > 0 ? $"Ahead by {gap:0} m" : $"Behind by {-gap:0} m";
				if (b.Course.IsCircuit) {
					place = $"Lap {run.Lap}/{b.Laps} · {place}";
				}
				if (b.IsSpeedTraps) {
					float mine = run.TrapTotal;
					float theirs = b.PeerTrapTotal;
					string lead = Mathf.Abs(mine - theirs) < BattleRules.TrapDeadHeatKmh ? "level" : mine > theirs ? $"you lead by {mine - theirs:0}" : $"{b.PeerName} leads by {theirs - mine:0}";
					place += $"\n<color={Theme.WarnHex}>Traps {TrapCountText(run)}: you {mine:0} · {b.PeerName} {theirs:0} km/h ({lead})</color>";
				}
				if (b.PeerOut.Length > 0) {
					place += $"\n<color={Theme.GoodHex}>{b.PeerOut}</color>";
				}
				DrawTimer(screenWidth, screenHeight, run, place);
			} else {
				DrawHudPill(screenWidth, screenHeight, $"{title}\n<color={Theme.MutedHex}>{b.Message}</color>");
			}
		}

		/// <summary>
		/// A challenge waiting for an answer, while driving. When a time attack or a recording already has the
		/// timer's spot, the challenge shows just under it (or over it, near the bottom of the screen).
		/// </summary>
		private void DrawInviteHud(float screenWidth, float screenHeight) {
			BattleMessage challenge = invites_[invites_.Count - 1].Challenge;
			string text = $"<b>{BattleMessage.PlainName(challenge.FromName)} challenges you</b>\n<color={Theme.MutedHex}>{BattleFormats[(int)challenge.Format]} · press {shortcut_} to answer</color>";
			if (hudRect_.width <= 0f) {
				DrawHudPill(screenWidth, screenHeight, text);
				return;
			}
			var content = new GUIContent(text);
			float width = Mathf.Min(340f, screenWidth - 2 * HudMargin);
			float height = Theme.HudPill.CalcHeight(content, width);
			float x = Mathf.Clamp(hudRect_.center.x - width / 2f, 0f, Mathf.Max(0f, screenWidth - width));
			float y = hudRect_.yMax + HudGap + height <= screenHeight ? hudRect_.yMax + HudGap : Mathf.Max(0f, hudRect_.y - HudGap - height);
			if (Event.current.type == EventType.Repaint) {
				GUI.Label(new Rect(x, y, width, height), content, Theme.HudPill);
			}
		}
	}
}
