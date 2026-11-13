// The wheel and controller shortcut buttons: the TRC action button (does the obvious thing now) and the window button.
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
		private const string WheelActionPref = "wheel_action";
		private const string WheelWindowPref = "wheel_window";

		private readonly WheelButtons wheel_ = new WheelButtons();

		private WheelButtons.Binding wheelAction_ = WheelButtons.Binding.None;
		private WheelButtons.Binding wheelWindow_ = WheelButtons.Binding.None;

		/// <summary>0 = not picking, 1 = picking the action button, 2 = the window button.</summary>
		private int capturingWheel_;
		private float captureWheelUntil_;

		private void UpdateWheel() {
			if (capturingWheel_ == 0 && !wheelAction_.IsSet && !wheelWindow_.IsSet) {
				return;
			}
			wheel_.Poll();
			if (capturingWheel_ != 0) {
				if (Time.unscaledTime > captureWheelUntil_) {
					capturingWheel_ = 0;
					optionsResult_ = "No button pressed: nothing changed.";
					return;
				}
				if (wheel_.Pressed.Count > 0) {
					WheelButtons.Binding picked = wheel_.Pressed[0];
					if (capturingWheel_ == 1) {
						wheelAction_ = picked;
						SavePref(WheelActionPref, picked.Save());
						if (wheelWindow_.Save() == picked.Save()) {
							wheelWindow_ = WheelButtons.Binding.None;
							SavePref(WheelWindowPref, "");
						}
					} else {
						wheelWindow_ = picked;
						SavePref(WheelWindowPref, picked.Save());
						if (wheelAction_.Save() == picked.Save()) {
							wheelAction_ = WheelButtons.Binding.None;
							SavePref(WheelActionPref, "");
						}
					}
					optionsResult_ = $"{(capturingWheel_ == 1 ? "TRC action" : "Open the window")}: {picked}.";
					TrcLog.Info($"Wheel button set: {optionsResult_}");
					capturingWheel_ = 0;
				}
				return;
			}
			if (TypingInGame()) {
				return;
			}
			if (wheel_.WasPressed(wheelWindow_)) {
				ToggleWindow();
			}
			if (wheel_.WasPressed(wheelAction_)) {
				WheelAction();
			}
		}

		/// <summary>
		/// The TRC action button does the obvious thing right now: accept a challenge, join a race that just
		/// opened, run the last battle or race back, or restart the time attack.
		/// </summary>
		private void WheelAction() {
			if (invites_.Count > 0) {
				BattleMessage challenge = invites_[invites_.Count - 1].Challenge;
				AnswerChallenge(challenge, true);
				ShowNotice(battle_ != null && battle_.Active ? $"<b>Accepted {BattleMessage.PlainName(challenge.FromName)}'s challenge</b>" : $"<b>Couldn't accept</b>\n{battleResult_}", 3f);
				return;
			}
			RaceMessage lobby = RaceToShow();
			if (lobby != null) {
				JoinRace(lobby);
				ShowNotice($"<b>Joined {BattleMessage.PlainName(lobby.HostName)}'s {(lobby.Settings.Mode != RoomMode.Race ? RoomModeNames[(int)lobby.Settings.Mode].ToLowerInvariant() : "race")}</b>", 3f);
				return;
			}
			if (battle_ != null && !battle_.Active && battle_.Started && CanRunItBack(battle_)) {
				RunItBack(battle_);
				ShowNotice($"<b>Run it back</b>\nChallenged {BattleMessage.PlainName(battle_.PeerName)} again", 3f);
				return;
			}
			if (race_ != null && race_.IsHost && race_.Phase == RacePhase.Finished && tournament_ == null && hostTournament_ == 0) {
				race_.StartRace(BattleNow);
				ShowNotice("<b>Run it back</b>\nThe same race again, same drivers", 3f);
				return;
			}
			if (run_ != null && !InBattle) {
				RestartFromKey();
				return;
			}
			if (!InBattle && !InRace && TrackToStart() != null) {
				StartFromKey();
				return;
			}
			ShowNotice("Nothing for the TRC button to do right now.", 2f);
		}

		private bool CanRunItBack(BattleSession b) => players_.Any(p => p.Player.SteamId == b.Peer) && (b.Course.Usable || b.IsHighway);

		/// <summary>The same battle again: the same opponent, course, format, laps and mode.</summary>
		private void RunItBack(BattleSession b) {
			PlayerRow peer = players_.FirstOrDefault(p => p.Player.SteamId == b.Peer);
			if (peer == null) {
				battleResult_ = $"{b.PeerName} isn't in the room any more.";
				return;
			}
			pickedFormat_ = (int)b.Format;
			pickedMode_ = b.Regulation != null ? 1 : b.Open ? 2 : 0;
			pickedRule_ = b.Regulation?.Code ?? pickedRule_;
			pickedFun_ = b.Fun;
			pickedBattleLaps_ = Math.Max(0, Array.IndexOf(BattleLapChoices, b.Laps));
			ChallengePlayer(peer, b.IsHighway ? Course.FreeRoam(map_?.Key ?? b.Course.MapKey) : b.Course);
		}
	}
}
