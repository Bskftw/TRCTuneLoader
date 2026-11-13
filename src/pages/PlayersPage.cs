// The Players page: everyone in the room, their TRC spec state and one player's details.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		private Vector2 playerListScroll_;
		private Vector2 playerDetailScroll_;

		private void DrawPlayersTab() {
			RefreshPlayersIfDue();
			GUILayout.Label(sync_.IsAvailable
				? "Setups are checked from what CarX shares with everyone. Mod lists and live checks come from players who also run TRC Tune Loader (self-reported)."
				: $"Setups are checked from what CarX shares with everyone. {sync_.Status}", Theme.Small);

			if (!string.IsNullOrEmpty(playersError_)) {
				GUILayout.Label(playersError_, Theme.PillNeutral);
				return;
			}

			// Everyone / Only problems is beside the page's title (Nav.DrawPageOptions).
			List<PlayerRow> shown = problemsOnly_ ? players_.Where(HasProblem).ToList() : players_;

			GUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
			GUILayout.BeginVertical(Theme.Card, GUILayout.Width(290), GUILayout.ExpandHeight(true));
			playerListScroll_ = GUILayout.BeginScrollView(playerListScroll_);
			if (shown.Count == 0 && problemsOnly_) {
				GUILayout.Label("No problems: everyone here is TRC spec.", Theme.Small);
			}
			foreach (PlayerRow row in shown) {
				bool selected = selectedPlayer_ != null && SamePlayer(row, selectedPlayer_);
				if (NavButton(PlayerRowText(row), selected ? Theme.RowSelected : Theme.Row)) {
					selectedPlayer_ = row;
				}
			}
			GUILayout.EndScrollView();
			GUILayout.EndVertical();

			GUILayout.Space(10);
			GUILayout.BeginVertical(GUILayout.ExpandHeight(true));
			DrawPlayerDetails();
			GUILayout.EndVertical();
			GUILayout.EndHorizontal();

			if (NavButton("Refresh", Theme.Button, GUILayout.Width(100))) {
				nextPlayersRefresh_ = 0;
				nextReportRequest_ = 0;
			}
		}

		private enum RowState { Good, Warn, Bad, Unknown }

		/// <summary>The colour of a player: red when not TRC spec, amber when a Workshop body can't be checked here, grey when the setup can't be checked.</summary>
		private static RowState StateOf(PlayerRow row) {
			RoomPlayer player = row.Player;
			if (CarNotSpec(player) || player.EngineId > 0 || (row.Setup != null && !row.Setup.Matches)) {
				return RowState.Bad;
			}
			if (row.Setup == null) {
				return RowState.Unknown;
			}
			return UnknownWorkshop(player) ? RowState.Warn : RowState.Good;
		}

		private static int FlaggedMods(PlayerRow row) => row.Report == null ? 0 : ModCheck.FlagsFor(row.Report.Mods).Count;

		/// <summary>For the Only problems filter: not TRC spec or not checkable, or their own report flags something.</summary>
		private static bool HasProblem(PlayerRow row) =>
			StateOf(row) != RowState.Good || FlaggedMods(row) > 0 ||
			(row.Report != null && (row.Report.LiveStatus == 2 || row.Report.DrivetrainDifferences > 0));

		private static string PlayerRowText(PlayerRow row) {
			RowState state = StateOf(row);
			string color = state == RowState.Bad ? Theme.BadHex
				: state == RowState.Warn ? Theme.WarnHex
				: state == RowState.Unknown ? Theme.MutedHex
				: Theme.GoodHex;
			int flagged = FlaggedMods(row);
			// An old report is greyed and says its age, so a stale "spec" isn't read as current (IMPROVEMENT_PLAN.md A5).
			int age = row.Report == null ? 0 : ReportAge(row.Report);
			string loader = row.Report == null
				? $"<color={Theme.MutedHex}>{(row.Player.IsLocal ? "You" : "No TRC Tune Loader")}</color>"
				: age > StaleReportSeconds
					? $"<color={Theme.MutedHex}>TRC Tune Loader {row.Report.LoaderVersion} · report {AgeText(age)} old</color>"
					: flagged > 0
						? $"<color={Theme.BadHex}>TRC Tune Loader {row.Report.LoaderVersion} · {flagged} flagged mod(s)</color>"
						: $"<color={Theme.AccentHex}>TRC Tune Loader {row.Report.LoaderVersion}</color>";
			return $"<b>{row.Player.Name}{(row.Player.IsLocal ? " (you)" : "")}</b>\n" +
				$"<color={Theme.MutedHex}>{row.Player.CarName} ({row.Player.SlotName})</color>\n" +
				$"<color={color}>{row.SetupText}</color>\n{loader}";
		}

		/// <summary>A report older than this (s) is shown as out of date: they come every 15 s or so while a Players page is open.</summary>
		private const int StaleReportSeconds = 30;

		private static int ReportAge(TrcSync.Report report) => (int)Math.Max(0, (DateTime.Now - report.ReceivedAt).TotalSeconds);

		private static string AgeText(int seconds) => seconds < 90 ? $"{seconds} s" : $"{seconds / 60} min";

		private static bool SamePlayer(PlayerRow a, PlayerRow b) =>
			a.Player.Name == b.Player.Name && a.Player.SteamId == b.Player.SteamId;

		private void DrawPlayerDetails() {
			PlayerRow row = selectedPlayer_ == null ? null : players_.FirstOrDefault(r => SamePlayer(r, selectedPlayer_));
			if (row == null) {
				GUILayout.Label(selectedPlayer_ == null ? "Pick a player on the left." : "This player has left the room.", Theme.Small);
				return;
			}

			RoomPlayer player = row.Player;
			GUILayout.Label($"{player.Name}{(player.IsLocal ? " (you)" : "")}", Theme.Heading);
			GUILayout.Label($"{player.CarName} (car {player.CarId}) · {player.SlotName} setup {player.SlotId}{(player.EngineId > 0 ? $" · engine swap {player.EngineId}" : "")}", Theme.Small);
			RowState state = StateOf(row);
			GUIStyle pill = state == RowState.Bad ? Theme.PillBad
				: state == RowState.Warn ? Theme.PillWarn
				: state == RowState.Unknown ? Theme.PillNeutral
				: Theme.PillGood;
			GUILayout.Label(row.SetupText, pill);

			// Another player's own tune stays theirs: what's changed shows, not the values, unless they chose to share them
			// (Options; their TRC Tune Loader says so in its report). Your own car always shows its numbers.
			bool values = player.IsLocal || row.Report?.ShareValues == true;
			Func<string, string> show = values ? (Func<string, string>)(l => l) : TuneCheck.WithoutValues;
			var lines = new List<string>();
			if (row.Setup != null) {
				lines.Add($"Code of the shared setup: {row.Setup.Code} (official {row.Official.Fingerprint})");
				lines.AddRange(row.Setup.Differences.Select(show));
				if (!player.IsLocal && row.Setup.Differences.Count > 0) {
					lines.Add(values
						? $"<color={Theme.MutedHex}>{player.Name} shares their tune values (their Options), so the numbers show.</color>"
						: $"<color={Theme.MutedHex}>Only what's changed is shown, not the values: everyone's own tune stays theirs unless they share it in Options.</color>");
				}
			}
			lines.AddRange(player.StanceDifferences.Select(show));
			lines.AddRange(player.Workshop.Select(w => w.Describe()));
			if (UnknownWorkshop(player)) {
				lines.Add("Subscribe to it on the Steam Workshop and the tune loader can check whether it moves the wheels.");
			}

			lines.Add("--- Engine & drivetrain ---");
			if (player.Drivetrain == null) {
				lines.Add("Their car isn't on the track here yet.");
			} else {
				lines.Add(player.Drivetrain.Summary());
				lines.AddRange(player.Drivetrain.Differences.Select(show));
				if (!player.IsLocal) {
					lines.Add($"<color={Theme.MutedHex}>Another player's KINO engine or gearbox swap only shows here if you run KINO too.</color>");
				}
			}

			TrcSync.Report report = row.Report;
			lines.Add("--- TRC Tune Loader ---");
			if (report == null) {
				lines.Add(player.IsLocal ? "That's you: see the Mod check tab." : "No report: this player doesn't seem to run TRC Tune Loader, or hasn't answered yet.");
			} else {
				int age = ReportAge(report);
				lines.Add($"Version {report.LoaderVersion}, reported {AgeText(age)} ago. Self-reported: a player who edits the game could fake it.");
				if (age > StaleReportSeconds) {
					lines.Add($"<color={Theme.WarnHex}>That report is {AgeText(age)} old: their answers aren't arriving any more. Read what follows as out of date.</color>");
				}
				if (report.HasRatings) {
					lines.Add($"TRC ratings (kept on their PC for now): {report.Badge} license · Elo touge {report.EloTouge:0}, highway {report.EloHighway:0}, circuit {report.EloCircuit:0}");
				}
				lines.Add($"Their own check: setup {(report.SetupOfficial ? $"{report.SetupSet} spec" : $"modified ({report.SetupDifferences})")}, code {report.SetupCode}; " +
					$"stance {(report.StanceDifferences == 0 ? "stock" : $"changed by a body kit ({report.StanceDifferences})")}; live car {LiveText(report)}");
				if (!string.IsNullOrEmpty(report.Engine)) {
					string changed = report.DrivetrainDifferences == 0 ? "matches CarX's own data" : $"<color={Theme.BadHex}>{report.DrivetrainDifferences} value(s) changed outside CarX's tuning (a KINO swap or air suspension, or another mod)</color>";
					lines.Add($"Their engine & drivetrain: {report.Engine}, {report.Drivetrain}; {changed}");
				}
				lines.Add($"KSL: all mods verified {(report.AllVerified ? "yes" : "no")}, protected mode {(report.ProtectedMode ? "on" : "off")}");

				List<string> flags = ModCheck.FlagsFor(report.Mods);
				if (flags.Count > 0) {
					lines.Add("--- Flagged ---");
					lines.AddRange(flags.Select(f => $"<color={Theme.BadHex}>{f}</color>"));
				}
				lines.Add($"--- Their mods ({report.Mods.Count}) ---");
				lines.AddRange(report.Mods.Select(ModCheck.Line));
			}

			DrawLines(ref playerDetailScroll_, lines);
		}
	}
}
