// The room board's channel: receiving players' statuses and shared items, sending this player's status.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// The room board's channel ("trc_room"): what every TRC Tune Loader player in the room is doing, and the
	/// courses, speed traps, regulations and runs they share. The Home page itself is src/pages/HomePage.cs.
	/// </summary>
	public partial class TRCTuneLoaderMod {
		private readonly RoomSync roomSync_ = new RoomSync();
		private readonly RoomBoard roomBoard_ = new RoomBoard();

		private void SetupHome() {
			roomSync_.Start(MySteamId);
			TrcLog.Info(roomSync_.Status);
			LoadHomeOptions();
		}

		/// <summary>The newest version another player in the room runs, when it's newer than this one ("" otherwise).</summary>
		private string newerVersion_ = "";

		/// <summary>Called from Update every frame.</summary>
		private void UpdateHome() {
			double now = BattleNow;
			roomSync_.Pump(now);
			foreach (RoomMessage m in roomSync_.Drain()) {
				if (m.Kind == RoomKind.Status && m.From != MySteamId && RoomBoard.IsNewer(m.Version, newerVersion_.Length > 0 ? newerVersion_ : Version)) {
					bool first = newerVersion_.Length == 0;
					newerVersion_ = m.Version;
					TrcLog.Info($"{BattleMessage.PlainName(m.Name)} runs TRC Tune Loader {m.Version}, newer than this one ({Version}).");
					if (first) {
						ShowNotice($"<b>TRC Tune Loader {m.Version} is out</b> (you have {Version})\n<color={Theme.MutedHex}>Turn on automatic updates for it in KSL, then restart CarX</color>", 15f);
					}
				}
				RoomBoard.SharedTraps traps = roomBoard_.ReceiveTraps(m, now, MySteamId);
				if (traps != null) {
					ShowNotice($"<b>{traps.FromName} shared {traps.Traps.Count} speed trap(s){(traps.JustForMe ? " with you" : "")}</b>\n" +
						$"<color={Theme.MutedHex}>{traps.Label} on {traps.MapName} · open the window (Time attack, Speed traps) to add them</color>");
					TrcLog.Info($"{traps.FromName} shared {traps.Traps.Count} speed trap(s) ({traps.Label}) on {traps.MapKey}{(traps.JustForMe ? " with you" : " with the room")}.");
				}
				RoomBoard.SharedItem item = roomBoard_.ReceiveItem(m, now, MySteamId);
				if (item?.Regulation != null) {
					ShowNotice($"<b>{item.FromName} shared a regulation{(item.JustForMe ? " with you" : "")}</b>\n<color={Theme.MutedHex}>{item.Regulation.Name} · open the window (Regulations) to save it</color>");
					TrcLog.Info($"{item.FromName} shared the regulation {item.Regulation.Name} ({item.Regulation.Code}).");
				} else if (item?.Ghost != null) {
					ShowNotice($"<b>{item.FromName} shared a run{(item.JustForMe ? " with you" : "")}</b>\n<color={Theme.MutedHex}>{TimeAttackRun.FormatTime(item.Ghost.Time)} on {item.Ghost.CourseName} · open the window (Time attack, Tracks) to race it</color>");
					TrcLog.Info($"{item.FromName} shared a run: {TimeAttackRun.FormatTime(item.Ghost.Time)} on {item.Ghost.CourseName} ({item.Ghost.CourseCode}), {item.Ghost.Samples.Count} ghost points.");
				}
				RoomBoard.Shared shared = roomBoard_.Receive(m, now);
				if (shared != null) {
					ShowNotice($"<b>{shared.FromName} shared a course</b>\n<color={Theme.MutedHex}>{shared.Course.Name} · open the window (Home) to save it</color>");
					TrcLog.Info($"{shared.FromName} shared the course {shared.Course.Name} ({shared.Course.Code}, {shared.Course.MapKey}).");
				}
			}
			// Built only when it could be sent (docs/archive/OPTIMIZATION_REPORT.md item 6): ShouldSend never sends within its gap anyway.
			if (roomSync_.IsAvailable && roomBoard_.PastGap(now)) {
				RoomMessage mine = MyStatus();
				if (roomBoard_.ShouldSend(mine, now)) {
					roomSync_.Send(mine);
				}
			}
			UpdateWheel();
			if (whatsNewPending_ && !windowOpen_ && !whatsNewShownThisSession_ && tourStep_ < 0) {
				OpenWhatsNewWhenReady();
			}
		}

		// ---- Room board ----

		/// <summary>What this player is doing, for the room board.</summary>
		private RoomMessage MyStatus() {
			var s = new RoomMessage {
				Kind = RoomKind.Status,
				From = MySteamId,
				Name = MyName(),
				Version = Version,
				UpForBattle = upForBattle_,
				MapKey = map_?.Key ?? "",
				Car = car_?.CarName ?? "",
			};
			if (race_ != null && !race_.Ended) {
				string where = race_.IsGame ? RoomModeNames[(int)race_.Settings.Mode] : race_.Course?.Name ?? race_.CourseCode;
				switch (race_.Phase) {
					case RacePhase.Lobby:
					case RacePhase.Finished:
						s.Activity = RoomActivity.RaceLobby;
						s.Detail = race_.Settings.Name;
						if (race_.Drivers.Count < race_.Settings.MaxPlayers) {
							s.OpenRaceId = race_.Id;
						}
						break;
					case RacePhase.Qualifying:
						s.Activity = RoomActivity.Qualifying;
						s.Detail = where;
						break;
					default:
						s.Activity = race_.IsGame ? RoomActivity.Game : RoomActivity.Race;
						s.Detail = where;
						break;
				}
			} else if (battle_ != null && battle_.Active && battle_.Started) {
				s.Activity = RoomActivity.Battle;
				s.Detail = $"{BattleMessage.PlainName(battle_.PeerName)}";
			} else if (run_ != null) {
				s.Activity = RoomActivity.TimeAttack;
				s.Detail = run_.Course.Name;
			} else if (recorder_.State != CourseRecorder.Phase.Idle || pathRecorder_ != null || gridRecorder_ != null) {
				s.Activity = RoomActivity.Building;
			} else if (car_ != null && !car_.InGarage) {
				s.Activity = RoomActivity.FreeRoam;
			} else {
				s.Activity = RoomActivity.Garage;
			}
			return s;
		}

		// ---- Shared courses ----

		/// <summary>Sends one of this player's courses to everyone in the room.</summary>
		private void ShareCourse(Course course) {
			if (!roomSync_.IsAvailable) {
				courseResult_ = roomSync_.Status;
				return;
			}
			var m = new RoomMessage { Kind = RoomKind.CourseShare, From = MySteamId, Name = MyName(), Course = course };
			int pieces = roomSync_.Send(m);
			courseResult_ = pieces > 0 ? $"Sent {course.Name} to everyone in the room with TRC Tune Loader 0.14 or newer." : "Couldn't send it: is the room's connection all right?";
			TrcLog.Info($"Shared the course {course.Name} ({course.Code}) with the room: {pieces} packet(s).");
		}
	}
}
