// Specs > Players: the room against your specs and regulations.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		private Vector2 specPlayersScroll_;
		private Vector2 specPlayerDetailScroll_;

		/// <summary>Something worth a look for specs and regulations: a claim that doesn't hold, or a regulation broken.</summary>
		private static bool RulesProblem(PlayerRow row) => RulesClaimWrong(row) || row.Regulations.Any(r => !r.Result.Passes);

		/// <summary>Specs > Players: every player, the spec they're on and the regulations they meet, checked here; their claims beside it.</summary>
		private void DrawSpecPlayersPage() {
			RefreshPlayersIfDue();
			GUILayout.Label("Checked here from what CarX shares of each car, against your specs and regulations, so it doesn't rest on what their mod says. " +
				"A spec you don't have can't be checked: install it from My specs.", Theme.Small);
			if (!string.IsNullOrEmpty(playersError_)) {
				GUILayout.Label(playersError_, Theme.PillNeutral);
				return;
			}
			List<PlayerRow> shown = problemsOnly_ ? players_.Where(RulesProblem).ToList() : players_;
			GUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
			GUILayout.BeginVertical(Theme.Card, GUILayout.Width(290), GUILayout.ExpandHeight(true));
			specPlayersScroll_ = GUILayout.BeginScrollView(specPlayersScroll_);
			if (shown.Count == 0) {
				GUILayout.Label(problemsOnly_ ? "Nothing to look at: no broken regulations or false claims." : "Nobody in the room yet.", Theme.Small);
			}
			foreach (PlayerRow row in shown) {
				bool selected = selectedPlayer_ != null && SamePlayer(row, selectedPlayer_);
				if (NavButton(RulesPlayerRowText(row), selected ? Theme.RowSelected : Theme.Row)) {
					selectedPlayer_ = row;
				}
			}
			GUILayout.EndScrollView();
			GUILayout.EndVertical();
			GUILayout.Space(10);
			GUILayout.BeginVertical(GUILayout.ExpandHeight(true));
			PlayerRow picked = selectedPlayer_ == null ? null : players_.FirstOrDefault(r => SamePlayer(r, selectedPlayer_));
			if (picked == null) {
				GUILayout.Label(selectedPlayer_ == null ? "Pick a player on the left." : "This player has left the room.", Theme.Small);
			} else {
				GUILayout.Label($"{picked.Player.Name}{(picked.Player.IsLocal ? " (you)" : "")}", Theme.Heading);
				GUILayout.Label($"{picked.Player.CarName} · {picked.Player.SlotName} setup", Theme.Small);
				bool values = picked.Player.IsLocal || picked.Report?.ShareValues == true;
				List<string> lines = RulesDetailLines(picked, values ? (Func<string, string>)(l => l) : TuneCheck.WithoutValues);
				if (lines.Count == 0) {
					lines.Add(specs_.Count == 0 && regulations_.Count == 0
						? "You have no specs or regulations to check against yet."
						: "None of your specs has a tune for their car, you have no regulations, and their mod didn't say it's on any.");
				}
				if (picked.Report == null && !picked.Player.IsLocal) {
					lines.Add($"<color={Theme.MutedHex}>No TRC Tune Loader report from them: only what's checked here shows.</color>");
				}
				DrawLines(ref specPlayerDetailScroll_, lines);
			}
			GUILayout.EndVertical();
			GUILayout.EndHorizontal();
			if (NavButton("Refresh", Theme.Button, GUILayout.Width(100))) {
				nextPlayersRefresh_ = 0;
				nextReportRequest_ = 0;
			}
		}

		/// <summary>A player in Specs > Players: name, car, the spec they're on, the regulations they meet, and any false claim.</summary>
		private static string RulesPlayerRowText(PlayerRow row) {
			string spec = RulesRowText(row);
			int meets = row.Regulations.Count(r => r.Result.Passes);
			string rules = row.Regulations.Count == 0 ? ""
				: $"\n<color={(meets == row.Regulations.Count ? Theme.GoodHex : Theme.BadHex)}>Meets {meets} of {row.Regulations.Count} regulation(s)</color>";
			string wrong = RulesClaimWrong(row) ? $"\n<color={Theme.BadHex}>Says it's on a spec or regulation it isn't</color>" : "";
			return $"<b>{row.Player.Name}{(row.Player.IsLocal ? " (you)" : "")}</b>\n" +
				$"<color={Theme.MutedHex}>{row.Player.CarName} ({row.Player.SlotName})</color>\n" +
				(spec.Length > 0 ? spec : $"<color={Theme.MutedHex}>No spec</color>") + rules + wrong;
		}
	}
}
