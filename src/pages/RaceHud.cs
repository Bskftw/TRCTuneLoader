// Races over the track: the race and room game HUD, and a race invite.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		private bool RaceHudVisible => hasPose_ && race_ != null && !race_.Ended &&
			(race_.IAmRacing || race_.Phase == RacePhase.Qualifying || (race_.Phase == RacePhase.Finished && race_.Results != null && race_.MyDriver?.Racer == true));

		/// <summary>The race over the track: where to line up, the countdown, your place and the gaps, then the result.</summary>
		private void DrawRaceHud(float screenWidth, float screenHeight) {
			RaceSession r = race_;
			TimeAttackRun run = r.Run;
			double now = BattleNow;
			if (run != null && run.State == TimeAttackRun.Phase.Countdown && run.CountdownNumber > 0) {
				GUI.Label(new Rect(0f, screenHeight * 0.3f, screenWidth, 160f), run.CountdownNumber.ToString(CultureInfo.InvariantCulture), Theme.HudBig);
			} else if (run != null && run.ShowGo(now)) {
				GUI.Label(new Rect(0f, screenHeight * 0.3f, screenWidth, 160f), "GO!", Theme.HudBig);
			}

			if (r.Phase == RacePhase.Qualifying && run != null && run.State == TimeAttackRun.Phase.Running) {
				string left = r.QualifyingEnds >= 0 ? $"{Math.Max(0, r.QualifyingEnds - now) / 60.0:0.0} min left" : "";
				DrawTimer(screenWidth, screenHeight, run, $"Qualifying · {left}{(r.MyDriver?.BestQualifying > 0 ? $" · best {TimeAttackRun.FormatTime(r.MyDriver.BestQualifying)}" : "")}");
				return;
			}
			if (r.IsGame && (r.Phase == RacePhase.Countdown || r.Phase == RacePhase.Running)) {
				DrawGameHud(screenWidth, screenHeight, r, now);
				return;
			}
			if (r.Phase == RacePhase.Running && run != null && run.State == TimeAttackRun.Phase.Running && r.MyDriver?.State == RacerState.Racing) {
				List<RaceStanding> standings = r.Standings(now);
				RaceStanding mine = standings.FirstOrDefault(s => s.Driver.Id == MySteamId);
				var lines = new List<string>();
				string lap = r.Course.IsCircuit ? $" · Lap {run.Lap}/{run.Laps}" : "";
				lines.Add($"<b>P{mine?.Position}/{standings.Count}</b>{lap}");
				if (mine != null) {
					RaceStanding ahead = standings.LastOrDefault(s => s.Position < mine.Position && s.Driver.State == RacerState.Racing);
					RaceStanding behind = standings.FirstOrDefault(s => s.Position > mine.Position && s.Driver.State == RacerState.Racing);
					var gaps = new List<string>();
					if (ahead != null) {
						gaps.Add($"{ahead.Progress - mine.Progress:0} m to P{ahead.Position}");
					}
					if (behind != null) {
						gaps.Add($"P{behind.Position} {mine.Progress - behind.Progress:0} m back");
					}
					if (gaps.Count > 0) {
						lines.Add(string.Join(" · ", gaps));
					}
				}
				if (mine != null) {
					var (yellow, blue) = RaceFlags.For(mine.Progress, r.Course.IsCircuit ? r.Course.Length : 0f,
						standings.Where(s => s != mine).Select(s => (s.Driver.Name, s.Progress, s.Driver.Kmh, s.Driver.State != RacerState.Racing && s.Driver.State != RacerState.Finished)));
					if (yellow.Length > 0) {
						lines.Add($"<color={Theme.WarnHex}>{yellow}</color>");
					}
					if (blue.Length > 0) {
						lines.Add($"<color=#8FB8FF>{blue}</color>");
					}
				}
				if (r.Settings.SpeedTrapScoring) {
					// Won on the trap totals, not the order on the road: the live totals, highest first.
					float TotalOf(RaceStanding s) => s.Driver.Id == MySteamId ? run.TrapTotal : s.Driver.TrapTotal;
					var byTraps = standings.Where(s => s.Driver.State == RacerState.Racing || s.Driver.State == RacerState.Finished)
						.OrderByDescending(TotalOf).ToList();
					int myRank = byTraps.FindIndex(s => s.Driver.Id == MySteamId) + 1;
					lines.Add($"<color={Theme.WarnHex}><b>Traps {TrapCountText(run)}: {run.TrapTotal:0} km/h · {Ordinal(myRank)} on traps</b></color>");
					lines.Add($"<color={Theme.MutedHex}>{string.Join("   ", byTraps.Take(3).Select((s, i) => $"{i + 1}. {(s.Driver.Id == MySteamId ? "You" : s.Driver.Name)} {TotalOf(s):0}"))}</color>");
				}
				if (r.EndAt >= 0) {
					lines.Add($"<color={Theme.WarnHex}>The race ends in {Math.Max(0, r.EndAt - now):0} s</color>");
				}
				DrawTimer(screenWidth, screenHeight, run, string.Join("\n", lines));
				return;
			}
			string text;
			if (r.Phase == RacePhase.Finished && r.Results != null) {
				RaceResultRow me = r.Results.FirstOrDefault(x => x.Id == MySteamId);
				string podium = string.Join("  ", r.Results.Take(3).Select(x => $"P{x.Position} {x.Name}"));
				text = $"<b>{(me != null ? $"P{me.Position} of {r.Results.Count}: {RowText(me)}" : "Race finished")}</b>\n<color={Theme.MutedHex}>{podium}</color>";
			} else {
				text = $"<b>{r.Settings.Name}</b> · {r.Course?.Name}\n<color={Theme.MutedHex}>{r.Message}</color>";
			}
			DrawHudPill(screenWidth, screenHeight, text);
		}

		/// <summary>A room game over the track: the start count, your part and points, the top three, the time left, and cut-up passes.</summary>
		private void DrawGameHud(float screenWidth, float screenHeight, RaceSession r, double now) {
			if (r.Phase == RacePhase.Countdown) {
				DrawHudPill(screenWidth, screenHeight, $"<b>{RoomModeNames[(int)r.Settings.Mode]}</b>\n<color={Theme.MutedHex}>{r.Message}</color>");
				return;
			}
			var lines = new List<string> { $"<b>{RoomModeNames[(int)r.Settings.Mode]}</b>{(r.EndAt >= 0 ? $" · {Math.Max(0, r.EndAt - now) / 60.0:0.0} min left" : "")}", r.Message };
			if (r.Settings.Mode != RoomMode.Convoy) {
				lines.Add($"<color={Theme.MutedHex}>{string.Join("   ", r.Standings(now).Take(3).Select(s => $"{s.Position}. {s.Driver.Name} {s.Driver.Points}"))}</color>");
			}
			if (CutUpFlashVisible) {
				lines.Add(cutUpFlash_);
			}
			DrawHudPill(screenWidth, screenHeight, string.Join("\n", lines));
		}

		/// <summary>An open race this player isn't in, for a while after it first appears.</summary>
		private RaceMessage RaceToShow() {
			if (race_ != null || !hasPose_) {
				return null;
			}
			foreach (var (lobby, _, first) in openRaces_.Values) {
				if (lobby.Phase == RacePhase.Lobby && lobby.Entries.Count < lobby.Settings.MaxPlayers && Time.unscaledTime - first < RaceInviteSeconds) {
					return lobby;
				}
			}
			return null;
		}

		private void DrawRaceInviteHud(float screenWidth, float screenHeight) {
			RaceMessage lobby = RaceToShow();
			if (lobby == null) {
				return;
			}
			Course course = courses_.ByCode(lobby.CourseCode);
			string what = lobby.Settings.Mode != RoomMode.Race ? RoomModeNames[(int)lobby.Settings.Mode].ToLowerInvariant() : lobby.Lines.Count > 0 ? "tournament" : "race";
			string text = $"<b>{BattleMessage.PlainName(lobby.HostName)} opened a {what}</b>\n" +
				$"<color={Theme.MutedHex}>{(lobby.Settings.Mode != RoomMode.Race ? "anywhere on the map" : course?.Name ?? "a course")} · {lobby.Entries.Count}/{lobby.Settings.MaxPlayers} · press {shortcut_} to join (Battle, Races)</color>";
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
