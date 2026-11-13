// The Regulations page: the list, checking your car and the room against one, sharing, and adding shared ones.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		private static readonly string[] RegulationSections = { "Regulations", "Make or edit", "Shared with you" };

		private int regulationsView_;
		private Vector2 regulationsScroll_;
		private string regulationResult_ = "";
		private string checkedRegulation_ = "";
		private string sharingRegulation_ = "";
		private ulong shareRegulationTo_;

		private void DrawRegulationsTab() {
			GUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
			int shared = roomBoard_.SharedRegulations.Count;
			int view = SideNav(RegulationSections, regulationsView_, new[] { regulations_.Count > 0 ? regulations_.Count.ToString(CultureInfo.InvariantCulture) : "", editingRegulation_ != null ? "editing" : "", shared > 0 ? $"{shared} new" : "" });
			if (view != regulationsView_) {
				regulationsView_ = view;
				regulationResult_ = "";
				if (view == 1 && editingRegulation_ == null) {
					StartEditingRegulation(new Regulation { Author = BattleMessage.PlainName(MyName()) });
				}
			}
			GUILayout.Space(10);
			GUILayout.BeginVertical(GUILayout.ExpandHeight(true));
			// Up and down only: nothing on these pages should need scrolling sideways.
			regulationsScroll_ = GUILayout.BeginScrollView(regulationsScroll_, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
			if (regulationResult_.Length > 0) {
				GUILayout.Label(regulationResult_, Theme.Body);
			}
			switch (regulationsView_) {
				case 0: DrawRegulationList(); break;
				case 1: DrawRegulationEditor(); break;
				default: DrawSharedRegulations(); break;
			}
			GUILayout.EndScrollView();
			GUILayout.EndVertical();
			GUILayout.EndHorizontal();
		}

		private void DrawRegulationList() {
			GUILayout.Label("Regulations keep tunes balanced without everyone running the same tune: a most power, tyre widths, no swaps, which setup. " +
				"Make one, share it, and run 1v1 battles, races and tournaments to it. Each driver's mod checks their own car.", Theme.Small);
			if (regulations_.Count == 0) {
				GUILayout.BeginVertical(Theme.Card);
				GUILayout.Label("No regulations yet. Make one (Make or edit), or import one someone gave you below.", Theme.Small);
				GUILayout.EndVertical();
			}
			foreach (Regulation r in regulations_.ToList()) {
				GUILayout.BeginVertical(Theme.Card);
				GUILayout.Label($"<b>{r.Name}</b>  <color={Theme.MutedHex}>{r.Code}{(r.Author.Length > 0 ? $" · by {r.Author}" : "")}</color>", Theme.Body);
				GUILayout.Label(r.Summary(), Theme.Small);
				if (r.Notes.Length > 0) {
					GUILayout.Label(r.Notes, Theme.Small);
				}
				GUILayout.BeginHorizontal();
				if (NavButton(checkedRegulation_ == r.Code ? "Hide check" : "Check my car", Theme.ButtonPrimary)) {
					checkedRegulation_ = checkedRegulation_ == r.Code ? "" : r.Code;
					myFactsUntil_ = 0;
				}
				if (NavButton("Edit", Theme.Button)) {
					StartEditingRegulation(r);
					regulationsView_ = 1;
				}
				if (NavButton(sharingRegulation_ == r.Code ? "Done sharing" : "Share...", Theme.Button)) {
					sharingRegulation_ = sharingRegulation_ == r.Code ? "" : r.Code;
				}
				if (NavButton("Copy for Discord", Theme.Button)) {
					GUIUtility.systemCopyBuffer = $"{r.Name} ({r.Code}): {r.ShareText()}";
					regulationResult_ = $"Copied {r.Name}. Paste it in Discord: anyone with TRC Tune Loader imports it here with Paste.";
				}
				GUILayout.FlexibleSpace();
				if (NavButton("Delete", Theme.Button)) {
					DeleteRegulation(r);
				}
				GUILayout.EndHorizontal();
				if (checkedRegulation_ == r.Code) {
					DrawRegulationCheck(r);
				}
				if (sharingRegulation_ == r.Code) {
					DrawShareRegulation(r);
				}
				GUILayout.EndVertical();
			}

			GUILayout.Label("Import", Theme.SectionLabel);
			GUILayout.BeginVertical(Theme.Card);
			GUILayout.Label("Paste a regulation someone copied for Discord (the line starting TRCREG1.), or a whole .trcreg file's text.", Theme.Small);
			GUILayout.BeginHorizontal();
			if (NavButton("Paste from clipboard", Theme.ButtonPrimary)) {
				ImportRegulation(GUIUtility.systemCopyBuffer);
			}
			if (NavButton("Open the folder", Theme.Button)) {
				Directory.CreateDirectory(RegulationsFolder);
				Application.OpenURL("file:///" + RegulationsFolder.Replace('\\', '/'));
			}
			if (NavButton("Reload", Theme.Button)) {
				LoadRegulations();
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.EndVertical();
		}

		/// <summary>The player's car against a regulation, and everyone in the room.</summary>
		private void DrawRegulationCheck(Regulation r) {
			CarFacts mine = MyCarFacts();
			RegulationResult result = r.Check(mine);
			string car = mine.CarName.Length > 0 ? $"{mine.CarName} ({Regulation.SetupLabel(mine.Setup)})" : "your car";
			string power = mine.Hp > 0 ? $" · {mine.Hp:0} hp" : "";
			string weight = mine.Mass > 0 ? $" · {mine.Mass:0} kg{(mine.Hp > 0 ? $" · {mine.Hp / mine.Mass:0.000} hp/kg" : "")}" : "";
			GUILayout.Label($"{car}{power}{weight}", Theme.Small);
			if (result.Passes) {
				GUILayout.Label($"Your car meets {r.Name}.", Theme.PillGood);
			} else {
				GUILayout.Label($"Your car doesn't meet {r.Name}:", Theme.PillBad);
				foreach (string broken in result.Broken) {
					GUILayout.Label($"<color={Theme.BadHex}>· {broken}</color>", Theme.Small);
				}
			}
			if (result.Unknown.Count > 0) {
				GUILayout.Label($"<color={Theme.WarnHex}>Couldn't check: {string.Join(", ", result.Unknown.Distinct())}. Drive onto a map, or open the garage, and check again.</color>", Theme.Small);
			}
			RefreshPlayersIfDue();
			// Worked out every 2 s, not every frame: each player's numbers come from CarX's data.
			if (roomCheckFor_ != r.Code || Time.unscaledTime >= roomCheckUntil_) {
				roomCheckFor_ = r.Code;
				roomCheckUntil_ = Time.unscaledTime + 2f;
				roomChecks_ = players_.Where(p => !p.Player.IsLocal).Select(p => (p, r.Check(FactsFor(p.Player)))).ToList();
			}
			if (roomChecks_.Count > 0) {
				GUILayout.Label("In this room", Theme.SectionLabel);
				foreach (var (p, theirs) in roomChecks_) {
					string state = theirs.Passes ? $"<color={Theme.GoodHex}>meets it</color>" : $"<color={Theme.BadHex}>{theirs.Broken[0]}</color>";
					string unknown = theirs.Unknown.Count > 0 ? $" <color={Theme.WarnHex}>(couldn't check {theirs.Unknown.Count} rule(s))</color>" : "";
					GUILayout.Label($"<b>{p.Player.Name}</b>{(p.Player.IsConsole ? $" ({p.Player.Platform})" : "")}  <color={Theme.MutedHex}>{p.Player.CarName}</color> · {state}{unknown}", Theme.Small);
				}
			}
		}

		private void DrawShareRegulation(Regulation r) {
			List<RoomMessage> players = roomBoard_.Fresh(BattleNow).Where(s => s.From != MySteamId).ToList();
			if (shareRegulationTo_ != 0 && !players.Any(p => p.From == shareRegulationTo_)) {
				shareRegulationTo_ = 0;
			}
			GUILayout.Label("Share with", Theme.SectionLabel);
			var who = new List<string> { "Everyone in the room" };
			who.AddRange(players.Select(p => BattleMessage.PlainName(p.Name)));
			int picked = Choice(who.ToArray(), shareRegulationTo_ == 0 ? 0 : players.FindIndex(p => p.From == shareRegulationTo_) + 1);
			shareRegulationTo_ = picked <= 0 ? 0 : players[picked - 1].From;
			GUILayout.BeginHorizontal();
			bool enabled = GUI.enabled;
			GUI.enabled = enabled && roomSync_.IsAvailable;
			string target = shareRegulationTo_ == 0 ? "the room" : who[picked];
			if (NavButton($"Send to {target}", Theme.ButtonPrimary)) {
				var m = new RoomMessage { Kind = RoomKind.RegulationShare, From = MySteamId, Name = MyName(), To = shareRegulationTo_, Regulation = r };
				int pieces = roomSync_.Send(m);
				regulationResult_ = pieces > 0 ? $"Sent {r.Name} to {target}. They need TRC Tune Loader 0.16 or newer, and save it in Regulations, Shared with you." : "Couldn't send it: is the room's connection all right?";
				TrcLog.Info($"Shared the regulation {r.Name} ({r.Code}) with {target}: {pieces} packet(s).");
			}
			GUI.enabled = enabled;
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
		}

		private void DrawSharedRegulations() {
			if (roomBoard_.SharedRegulations.Count == 0) {
				GUILayout.Label("Nothing shared with you right now. When someone in the room shares a regulation, it shows here to save.", Theme.Small);
				return;
			}
			foreach (RoomBoard.SharedItem item in roomBoard_.SharedRegulations.ToList()) {
				Regulation r = item.Regulation;
				GUILayout.BeginVertical(Theme.Card);
				GUILayout.Label($"<b>{r.Name}</b>  <color={Theme.MutedHex}>{r.Code} · from {item.FromName}{(item.JustForMe ? ", just for you" : "")}</color>", Theme.Body);
				GUILayout.Label(r.Summary(), Theme.Small);
				GUILayout.BeginHorizontal();
				bool have = RegulationByCode(r.Code) != null;
				if (NavButton(have ? "Already saved" : "Save it", Theme.ButtonPrimary) && !have) {
					SaveRegulation(r.Copy(), $"Saved {r.Name}.");
					roomBoard_.SharedRegulations.Remove(item);
				}
				if (NavButton("Dismiss", Theme.Button)) {
					roomBoard_.SharedRegulations.Remove(item);
				}
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
				GUILayout.EndVertical();
			}
		}

		private void ImportRegulation(string text) {
			Regulation r = Regulation.FromPasted(text);
			if (r == null) {
				regulationResult_ = "That isn't a regulation: copy the whole TRCREG1. line (or a .trcreg file's text) and try again.";
				return;
			}
			Regulation same = RegulationByCode(r.Code);
			if (same != null) {
				regulationResult_ = $"You already have it: {same.Name} ({same.Code}).";
				return;
			}
			if (r.Name.Length == 0) {
				r.Name = "Imported regulation";
			}
			SaveRegulation(r, $"Imported {r.Name} ({r.Code}).");
		}

		private void SaveRegulation(Regulation r, string done) {
			try {
				r.Save(RegulationsFolder);
				LoadRegulations();
				regulationResult_ = done;
				TrcLog.Info($"Regulation saved: {r.Name} ({r.Code}) at {r.FilePath}.");
			} catch (Exception e) {
				regulationResult_ = $"Couldn't save it: {e.Message}";
				TrcLog.Error(e);
			}
		}

		private void DeleteRegulation(Regulation r) {
			try {
				if (r.FilePath.Length > 0 && File.Exists(r.FilePath)) {
					File.Delete(r.FilePath);
				}
				LoadRegulations();
				regulationResult_ = $"Deleted {r.Name}.";
			} catch (Exception e) {
				regulationResult_ = $"Couldn't delete it: {e.Message}";
			}
		}
	}
}
