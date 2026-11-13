// My times > Ratings: your Elo and licence, and other players' ratings.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		private void DrawRatings() {
			RatingBook r = ratings_ ?? new RatingBook();
			GUILayout.Label(RatingBook.TestingNote, Theme.PillWarn);
			if (r.WasChanged) {
				GUILayout.Label("Your ratings file was changed outside the TRC Tune Loader (or was another player's), so your ratings started again. The file is sealed: editing it never counts.", Theme.PillBad);
			}
			GUILayout.Label("Kept on this PC until the TRC server is up: then they'll be checked and shared. Other TRC Tune Loader players see your license and Safety Rating.", Theme.Small);
			GUILayout.BeginVertical(Theme.Guide);
			GUILayout.Label($"<b>{RatingBook.LicenseName(r.License)} license</b> · Safety Rating {r.Safety:0.00} · {r.Events} event(s)", Theme.Heading);
			string next = r.License == License.A ? "Top license." : $"Next license: Safety Rating {RatingBook.PromoteSafety:0.00} or more and enough events driven.";
			GUILayout.Label($"{next} Below {RatingBook.DemoteSafety:0.00} you go down a license.", Theme.Small);
			GUILayout.EndVertical();
			GUILayout.BeginVertical(Theme.Card);
			for (int i = 0; i < 3; i++) {
				GUILayout.Label($"<b>{Course.KindNames[i]}</b>  Elo {r.Elo[i]:0}", Theme.Body);
			}
			foreach (var pool in r.RuleElo.OrderBy(p => p.Key, StringComparer.Ordinal)) {
				string name = r.RuleNames.TryGetValue(pool.Key, out string n) && n.Length > 0 ? n : pool.Key;
				GUILayout.Label($"<b>{name}</b> <color={Theme.MutedHex}>({pool.Key})</color>  touge {pool.Value[0]:0} · highway {pool.Value[1]:0} · circuit {pool.Value[2]:0}", Theme.Small);
			}
			GUILayout.Label("Elo moves only in TRC spec battles and races; events run to a regulation have their own Elo (above), and \"just for fun\" ones change nothing. " +
				"The Safety Rating moves in TRC spec and regulation battles and races: up with clean kilometres, " +
				$"down with hard contact, hard hits and resets (at most {RatingBook.MaxDropPerEvent:0.00} an event). Light touches in close racing, time attack, " +
				"leaving the road and jump starts don't count. No blame: like iRacing, both cars in a contact get it.", Theme.Small);
			GUILayout.EndVertical();
			GUILayout.Label("Recent", Theme.SectionLabel);
			GUILayout.BeginVertical(Theme.Card);
			if (r.History.Count == 0) {
				GUILayout.Label("Nothing yet: drive a time attack, a battle or a race.", Theme.Small);
			}
			foreach (string line in Enumerable.Reverse(r.History).Take(30)) {
				GUILayout.Label(line, Theme.Small);
			}
			GUILayout.EndVertical();
		}

		/// <summary>"B 3.12 · 1530": another player's license, Safety Rating and Elo for a kind of course, from their report ("" without).</summary>
		private string RatingOf(ulong id, CourseKind kind) {
			TrcSync.Report r = id == MySteamId ? null : sync_?.ReportFrom(id);
			if (id == MySteamId && ratings_ != null) {
				return $"{ratings_.Badge} · {ratings_.Elo[(int)kind]:0}";
			}
			return r != null && r.HasRatings ? $"{r.Badge} · {r.EloFor(kind):0}" : "";
		}
	}
}
