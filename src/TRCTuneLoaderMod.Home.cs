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
	/// The Home tab: what's new, quick buttons to every part of the mod, what's waiting for the player, the room
	/// board (what every TRC Tune Loader player in the room is doing, from the "trc_room" channel), courses
	/// shared with the room, and Report a bug. Also the wheel/controller buttons, and the server-ready event
	/// ledger (EventLedger) fed from runs, battles and races.
	/// </summary>
	public partial class TRCTuneLoaderMod {
		private const string WhatsNewPref = "whats_new_seen";
		private const string WheelActionPref = "wheel_action";
		private const string WheelWindowPref = "wheel_window";

		/// <summary>Raise with each version that has news worth a pop-up.</summary>
		private const string WhatsNewVersion = "0.15.0";

		private static readonly string[] WhatsNewLines = {
			"<b>Tidier tabs</b>: Time attack has Tracks, Speed traps, My times and Course builder down the left; <b>Battles</b> is its own tab with 1v1 battles, Races & tournaments, Fun games and My ratings. Backups are in My car.",
			"<b>Speed traps</b>: name them, pick how wide they are, and they're grouped by track (\"Tsubaki Speed Trap 1, 2...\"). A floating SPEED CHECK sign shows your last speed; the line on the road only shows while you place one. They work during time attacks, battles and races too.",
			"<b>Speed trap races</b> (Races & tournaments, Race for: Speed traps): the highest total of every trap on the course wins, like Most Wanted.",
			"<b>Your own colours and text size</b> (Options, Look).",
			"<b>Delete times</b> in My times, and the <b>Course builder</b> can make the reverse of your course as you save it.",
			"<b>Fixes</b>: highway battles where only one driver started, challenges on long courses that never arrived, the delta jumping in hairpins, and colour codes in names.",
			"<b>Ratings are being tested</b>: the Safety Rating no longer counts light touches or time attacks, and was reset once. Elo and Safety Rating will be reset again before they count.",
		};

		private readonly RoomSync roomSync_ = new RoomSync();
		private readonly RoomBoard roomBoard_ = new RoomBoard();
		private readonly WheelButtons wheel_ = new WheelButtons();
		private EventLedger ledger_;

		private bool whatsNewPending_;

		/// <summary>Other TRCTuneLoader.ksm files than the one running (an old install or update left them).</summary>
		private List<string> otherCopies_ = new List<string>();
		private bool upForBattle_;
		private string homeResult_ = "";
		private string bugNote_ = "";
		private string lastBugReport_ = "";
		private Vector2 homeScroll_;
		private string notice_ = "";
		private float noticeUntil_;

		private WheelButtons.Binding wheelAction_ = WheelButtons.Binding.None;
		private WheelButtons.Binding wheelWindow_ = WheelButtons.Binding.None;

		/// <summary>0 = not picking, 1 = picking the action button, 2 = the window button.</summary>
		private int capturingWheel_;
		private float captureWheelUntil_;

		private void SetupHome() {
			roomSync_.Start(MySteamId);
			TrcLog.Info(roomSync_.Status);
			try {
				whatsNewPending_ = tourSeen_ && Kino.Prefs.Get(WhatsNewPref, "") != WhatsNewVersion;
				wheelAction_ = WheelButtons.Binding.Parse(Kino.Prefs.Get(WheelActionPref, ""));
				wheelWindow_ = WheelButtons.Binding.Parse(Kino.Prefs.Get(WheelWindowPref, ""));
			} catch (Exception e) {
				TrcLog.Warning($"Couldn't read the Home options: {e.Message}");
			}
		}

		private EventLedger Ledger => ledger_ ??= new EventLedger(TimesFolder);

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
				RoomBoard.Shared shared = roomBoard_.Receive(m, now);
				if (shared != null) {
					ShowNotice($"<b>{shared.FromName} shared a course</b>\n<color={Theme.MutedHex}>{shared.Course.Name} · open the window (Home) to save it</color>");
					TrcLog.Info($"{shared.FromName} shared the course {shared.Course.Name} ({shared.Course.Code}, {shared.Course.MapKey}).");
				}
			}
			if (roomSync_.IsAvailable) {
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

		private void FindOtherCopies() {
			try {
				otherCopies_ = DataFolder.OtherCopies(Location, Kino.Paths.Mods, Kino.Paths.GameRoot);
			} catch (Exception e) {
				TrcLog.Warning($"Couldn't look for other copies of the mod: {e.Message}");
				return;
			}
			foreach (string copy in otherCopies_) {
				TrcLog.Warning($"Another copy of TRC Tune Loader is at {copy}. The one running is {Location}: delete the other one.");
			}
			if (otherCopies_.Count > 0) {
				ShowNotice($"<b>TRC Tune Loader is installed twice</b>\n<color={Theme.MutedHex}>Open the window (Home) to see which file to delete</color>", 15f);
			}
		}

		private void DrawOtherCopies() {
			if (otherCopies_.Count == 0) {
				return;
			}
			GUILayout.BeginVertical(Theme.Guide);
			GUILayout.Label("TRC Tune Loader is installed twice", Theme.Heading);
			GUILayout.Label($"The one running is <b>{Location}</b>. Close CarX and delete:", Theme.Body);
			foreach (string copy in otherCopies_) {
				GUILayout.Label($"• {copy}", Theme.Body);
			}
			GUILayout.Label("An old version's update put the mod in a second place. Your times, courses and backups are safe: they're in kino\\mods\\TRCTuneLoader.", Theme.Small);
			GUILayout.EndVertical();
		}

		/// <summary>Someone in the room runs a newer version: this one didn't update (KSL can treat an update as optional).</summary>
		private void DrawNewerVersion() {
			if (newerVersion_.Length == 0 || !RoomBoard.IsNewer(newerVersion_, Version)) {
				return;
			}
			GUILayout.BeginVertical(Theme.Guide);
			GUILayout.Label($"TRC Tune Loader {newerVersion_} is out (you have {Version})", Theme.Heading);
			GUILayout.Label("Someone in this room already has it. Turn on automatic updates for TRC Tune Loader in KSL, then restart CarX: KSL installs it as the game starts. " +
				"Battles, races and the room board work best when everyone's on the same version.", Theme.Body);
			GUILayout.EndVertical();
		}

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
			GUI.Label(new Rect((screenWidth - width) / 2f, screenHeight - height - 90f, width, height), content, Theme.HudPill);
		}

		/// <summary>The window opens by itself once on the garage screen after an update, on Home with What's new.</summary>
		private void OpenWhatsNewWhenReady() {
			RefreshCarIfDue();
			if (car_ == null || !car_.InGarage || car_.InDyno) {
				garageSince_ = -1f;
				return;
			}
			if (garageSince_ < 0f) {
				garageSince_ = Time.unscaledTime;
			}
			if (Time.unscaledTime - garageSince_ >= TourGarageDelaySeconds) {
				tab_ = HomeTab;
				OpenWindow();
				garageSince_ = -1f;
				// Once per start of the game, even if they close it without reading.
				whatsNewShownThisSession_ = true;
			}
		}

		private bool whatsNewShownThisSession_;

		private void DismissWhatsNew() {
			whatsNewPending_ = false;
			SavePref(WhatsNewPref, WhatsNewVersion);
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

		private void DrawRoomBoard() {
			GUILayout.Label("In this room", Theme.SectionLabel);
			GUILayout.BeginVertical(Theme.Card);
			if (!roomSync_.IsAvailable) {
				GUILayout.Label(roomSync_.Status, Theme.Small);
				GUILayout.EndVertical();
				return;
			}
			double now = BattleNow;
			GUILayout.BeginHorizontal();
			GUILayout.Label($"<b>You</b>  <color={Theme.MutedHex}>{RoomBoard.ActivityText(MyStatus())}</color>", Theme.Body);
			GUILayout.FlexibleSpace();
			bool up = GUILayout.Toggle(upForBattle_, " I'm up for a battle");
			if (up != upForBattle_) {
				upForBattle_ = up;
			}
			GUILayout.EndHorizontal();

			List<RoomMessage> others = roomBoard_.Fresh(now).Where(s => s.From != MySteamId).ToList();
			if (others.Count == 0) {
				GUILayout.Label("Nobody else here runs TRC Tune Loader 0.14 or newer yet. Players on older versions show on the Players tab.", Theme.Small);
			}
			foreach (RoomMessage s in others) {
				var parts = new List<string> { RoomBoard.ActivityText(s) };
				if (s.Car.Length > 0) {
					parts.Add(s.Car);
				}
				string rating = RatingOf(s.From, CourseKind.Touge);
				if (rating.Length > 0) {
					parts.Add(rating);
				}
				if (map_ != null && s.MapKey.Length > 0 && s.MapKey != map_.Key) {
					parts.Add("another map");
				}
				GUILayout.BeginHorizontal();
				string up4 = s.UpForBattle ? $"  <color={Theme.GoodHex}>up for a battle</color>" : "";
				GUILayout.Label($"<b>{BattleMessage.PlainName(s.Name)}</b>{up4}  <color={Theme.MutedHex}>{string.Join(" · ", parts)}</color>", Theme.Body);
				GUILayout.FlexibleSpace();
				bool enabled = GUI.enabled;
				if (s.OpenRaceId != 0 && openRaces_.TryGetValue(s.OpenRaceId, out var open) && race_ == null) {
					if (GUILayout.Button("Join", Theme.ButtonPrimary)) {
						JoinRace(open.Lobby);
						GoToBattle(open.Lobby.Settings.Mode == RoomMode.Race ? 1 : 2);
					}
				}
				bool canBattle = RoomBoard.Free(s) && System.Version.TryParse(s.Version, out System.Version theirs) && theirs >= BattleVersion;
				GUI.enabled = enabled && canBattle && (battle_ == null || !battle_.Active) && !InRace;
				if (GUILayout.Button("Challenge", Theme.Button)) {
					pickedOpponent_ = s.From;
					GoToBattle(0);
				}
				GUI.enabled = enabled;
				GUILayout.EndHorizontal();
			}
			GUILayout.EndVertical();
		}

		private void GoToTimeAttack(int view) {
			tab_ = CoursesTab;
			OnTabOpened(CoursesTab);
			coursesView_ = view;
		}

		/// <summary>The Battles tab at a section: 0 1v1 battles, 1 races, 2 fun games, 3 ratings.</summary>
		private void GoToBattle(int section) {
			tab_ = BattlesTab;
			OnTabOpened(BattlesTab);
			battleViewMode_ = section;
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

		private void DrawSharedCourses() {
			if (roomBoard_.SharedCourses.Count == 0) {
				return;
			}
			GUILayout.Label("Courses shared with the room", Theme.SectionLabel);
			GUILayout.BeginVertical(Theme.Card);
			foreach (RoomBoard.Shared shared in roomBoard_.SharedCourses.ToList()) {
				Course c = shared.Course;
				bool have = courses_.ByCode(c.Code) != null;
				GUILayout.BeginHorizontal();
				string map = c.MapName.Length > 0 ? c.MapName : c.MapKey;
				GUILayout.Label($"<b>{c.Name}</b>  <color={Theme.MutedHex}>from {shared.FromName} · {c.KindName} · {TimeAttackRun.Distance(c.Length)} · {map}</color>", Theme.Body);
				GUILayout.FlexibleSpace();
				bool enabled = GUI.enabled;
				GUI.enabled = enabled && !have;
				if (GUILayout.Button(have ? "Saved" : "Save", Theme.ButtonPrimary)) {
					SaveSharedCourse(shared);
				}
				GUI.enabled = enabled;
				if (GUILayout.Button("Dismiss", Theme.Button)) {
					roomBoard_.SharedCourses.Remove(shared);
				}
				GUILayout.EndHorizontal();
			}
			GUILayout.EndVertical();
		}

		private void SaveSharedCourse(RoomBoard.Shared shared) {
			try {
				Course copy = Copy(shared.Course, c => c.Author = $"{shared.FromName} (shared)");
				string path = CourseLibrary.SaveCustom(CoursesFolder, copy);
				courses_.LoadCustom(CoursesFolder);
				RefreshTraps();
				homeResult_ = $"Saved {copy.Name} to your courses: it's in Time attack, Tracks, when you're on {(copy.MapName.Length > 0 ? copy.MapName : "its map")}.";
				TrcLog.Info($"Saved the shared course {copy.Name} ({copy.Code}) from {shared.FromName}: {path}");
			} catch (Exception e) {
				homeResult_ = $"Couldn't save the course: {e.Message}";
				TrcLog.Error(e);
			}
		}

		// ---- Wheel and controller buttons ----

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
			pickedMode_ = b.Open ? 1 : 0;
			pickedBattleLaps_ = Math.Max(0, Array.IndexOf(BattleLapChoices, b.Laps));
			ChallengePlayer(peer, b.IsHighway ? Course.FreeRoam(map_?.Key ?? b.Course.MapKey) : b.Course);
		}

		private void DrawWheelOptions() {
			GUILayout.Label("Wheel and controller buttons", Theme.SectionLabel);
			GUILayout.BeginVertical(Theme.Card);
			GUILayout.Label("Pick buttons CarX doesn't use: CarX still sees every press. The <b>TRC action</b> button accepts a challenge, joins a race that just opened, " +
				"runs the last battle or race back, or restarts your time attack, whichever fits.", Theme.Small);
			void Row(string label, ref WheelButtons.Binding binding, int which, string pref) {
				GUILayout.BeginHorizontal();
				GUILayout.Label($"<b>{label}</b>  <color={Theme.MutedHex}>{(capturingWheel_ == which ? "press a button on your wheel or controller…" : binding.ToString())}</color>", Theme.Body);
				GUILayout.FlexibleSpace();
				if (GUILayout.Button(capturingWheel_ == which ? "Cancel" : "Pick a button", Theme.Button)) {
					if (capturingWheel_ == which) {
						capturingWheel_ = 0;
					} else {
						capturingWheel_ = which;
						captureWheelUntil_ = Time.unscaledTime + 10f;
						wheel_.Poll();
						optionsResult_ = "";
					}
				}
				if (binding.IsSet && GUILayout.Button("Clear", Theme.Button)) {
					binding = WheelButtons.Binding.None;
					SavePref(pref, "");
				}
				GUILayout.EndHorizontal();
			}
			Row("TRC action", ref wheelAction_, 1, WheelActionPref);
			Row("Open the window", ref wheelWindow_, 2, WheelWindowPref);
			if (wheel_.Problem.Length > 0) {
				GUILayout.Label(wheel_.Problem, Theme.PillWarn);
			} else if (capturingWheel_ != 0 && wheel_.Devices.Count == 0) {
				GUILayout.Label("CarX doesn't see a wheel or controller right now.", Theme.Small);
			}
			GUILayout.EndVertical();
		}

		// ---- Report a bug ----

		private void DrawBugReport() {
			GUILayout.Label("Report a bug", Theme.SectionLabel);
			GUILayout.BeginVertical(Theme.Card);
			GUILayout.Label("Something didn't work? Say what happened (optional), then press Make a bug report. A zip lands on your Desktop with the mod's logs, " +
				"what you were doing, your options and your mods. Send it to bskftw on Discord. It has the names of players you raced, and no tune values of anyone.", Theme.Small);
			bugNote_ = GUILayout.TextArea(bugNote_ ?? "", 1000, Theme.Field, GUILayout.MinHeight(48));
			GUILayout.BeginHorizontal();
			if (GUILayout.Button("Make a bug report", Theme.ButtonPrimary)) {
				MakeBugReport();
			}
			if (lastBugReport_.Length > 0 && GUILayout.Button("Show the file", Theme.Button)) {
				try {
					Application.OpenURL("file:///" + (Path.GetDirectoryName(lastBugReport_) ?? "").Replace('\\', '/'));
				} catch (Exception e) {
					homeResult_ = $"Couldn't open the folder: {e.Message}";
				}
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			if (lastBugReport_.Length > 0) {
				GUILayout.Label($"Saved: {lastBugReport_}", Theme.Small);
			}
			GUILayout.EndVertical();
		}

		private void MakeBugReport() {
			DateTime when = DateTime.Now;
			try {
				TrcLog.Info($"Bug report made{(TuneRules.Trim(bugNote_).Length > 0 ? $": {TuneRules.Trim(bugNote_)}" : ".")}");
				var parts = new List<BugReport.Part> {
					new BugReport.Part { Name = "what-happened.txt", Text = TuneRules.Trim(bugNote_).Length > 0 ? bugNote_ : "(nothing written)" },
					new BugReport.Part { Name = "summary.txt", Text = BugSummary(when) },
					BugReport.LogPart(TrcLog.FileName, Path.Combine(modFolder_, TrcLog.FileName)),
					BugReport.LogPart(TrcLog.PreviousFileName, Path.Combine(modFolder_, TrcLog.PreviousFileName)),
				};
				string kino = Directory.GetParent(Kino.Paths.Mods)?.FullName ?? "";
				if (kino.Length > 0) {
					parts.Add(BugReport.LogPart("ksl-output.log", Path.Combine(kino, "output.log")));
				}
				string desktop = "";
				try {
					desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
				} catch (Exception) {
					// No Desktop: the mod's folder instead.
				}
				string folder = desktop.Length > 0 && Directory.Exists(desktop) ? desktop : Path.Combine(modFolder_, "bug-reports");
				lastBugReport_ = BugReport.Write(folder, when, parts);
				homeResult_ = $"Bug report saved: {Path.GetFileName(lastBugReport_)} ({(folder == desktop ? "on your Desktop" : folder)}). Send it to bskftw.";
				optionsResult_ = homeResult_;
				bugNote_ = "";
				TrcLog.Info($"Bug report saved: {lastBugReport_}");
			} catch (Exception e) {
				homeResult_ = $"Couldn't make the bug report: {e.Message}";
				optionsResult_ = homeResult_;
				TrcLog.Error(e);
			}
		}

		private string BugSummary(DateTime when) {
			var b = new StringBuilder();
			void Line(string name, object value) => b.Append(name).Append(": ").Append(Convert.ToString(value, CultureInfo.InvariantCulture)).Append('\n');
			void Safe(string name, Func<object> value) {
				try {
					Line(name, value());
				} catch (Exception e) {
					Line(name, $"(couldn't read: {e.Message})");
				}
			}
			Line("TRC Tune Loader", Version);
			Line("Running from", Location);
			Line("Files in", modFolder_);
			Line("Other copies", otherCopies_.Count == 0 ? "none" : string.Join(", ", otherCopies_));
			Line("Made", when.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
			Safe("Unity", () => Application.unityVersion);
			Safe("System", () => SystemInfo.operatingSystem);
			Safe("Screen", () => $"{Screen.width}x{Screen.height}, UI scale {Kino.UI.Scale:0.##}, window size {windowScale_:0.##}");
			Safe("KSL", () => $"protected mode {Kino.Loader.IsProtectedModeActive()}, all mods verified {Kino.Loader.IsAllModsVerified()}");
			Safe("Map", () => map_ == null ? "(none)" : $"{map_.Name} ({map_.Key})");
			Safe("Car", () => car_ == null ? "(none)" : $"{car_.Describe()}, id {car_.CarId}, {(car_.InGarage ? "garage" : "on track")}{(car_.InDyno ? ", tuning screen" : "")}");
			Safe("Doing", () => RoomBoard.ActivityText(MyStatus()));
			Safe("Time attack", () => run_ == null ? "(none)" : $"{run_.Course.Name} ({run_.Course.Code}), {run_.State}, {run_.Message}");
			Safe("Battle", () => battle_ == null ? "(none)" : $"{battle_.Id} vs {battle_.PeerName}, {BattleFormats[(int)battle_.Format]}, {battle_.State}, {battle_.Message}");
			Safe("Race", () => race_ == null ? "(none)" : $"{race_.Id} {race_.Settings.Name}, {race_.Phase}, {race_.Drivers.Count} driver(s), host {race_.IsHost}, {race_.Message}");
			Safe("Sharing", () => sync_?.Status);
			Safe("Battles channel", () => $"{battleSync_.Status} sent {battleSync_.Sent}, refused {battleSync_.Refused}, received {battleSync_.Received}");
			Safe("Races channel", () => $"{raceSync_.Status} sent {raceSync_.Sent}, refused {raceSync_.Refused}, received {raceSync_.Received}");
			Safe("Room channel", () => $"{roomSync_.Status} sent {roomSync_.Sent}, refused {roomSync_.Refused}, received {roomSync_.Received}, {roomBoard_.Fresh(BattleNow).Count} player(s) on the board");
			Safe("Players in the room", () => players_.Count);
			Safe("Options", () => $"shortcut {shortcut_}, restart {restartShortcut_}, badge {showBadge_}, track check {autoCheck_}, sectors {showSectors_}, best line {bestLineMode_}, " +
				$"share values {shareValues_}, free-roam traps {freeRoamTraps_}, wheel action {wheelAction_}, wheel window {wheelWindow_}");
			Safe("Server events", () => {
				string file = Ledger.FilePath;
				if (!File.Exists(file)) {
					return "none yet";
				}
				string problem = EventLedger.Verify(File.ReadLines(file, Encoding.UTF8));
				return $"{Ledger.Count}, {(problem.Length == 0 ? "chain OK" : problem)}";
			});
			b.Append("\nMods:\n");
			try {
				foreach (TrcSync.ModEntry mod in BuildOwnReport()?.Mods ?? new List<TrcSync.ModEntry>()) {
					b.Append($"  {mod.Name} {mod.Version} by {mod.Author} ({mod.Type})\n");
				}
			} catch (Exception e) {
				b.Append($"  (couldn't list: {e.Message})\n");
			}
			return b.ToString();
		}

		// ---- Server events ----

		/// <summary>Adds an event to the server-ready ledger; never stops what called it.</summary>
		private void LedgerEvent(string type, List<EventLedger.Field> fields) {
			try {
				Ledger.Append(type, MySteamId, Version, DateTime.UtcNow, fields);
			} catch (Exception e) {
				TrcLog.Warning($"Couldn't add the {type} to the server events: {e.Message}");
			}
		}

		// ---- The tab ----

		private void DrawHomeTab() {
			RefreshCarIfDue();
			RefreshPlayersIfDue();
			homeScroll_ = GUILayout.BeginScrollView(homeScroll_);
			DrawOtherCopies();
			DrawNewerVersion();
			if (whatsNewPending_) {
				GUILayout.BeginVertical(Theme.Guide);
				GUILayout.Label($"What's new in {WhatsNewVersion}", Theme.Heading);
				foreach (string line in WhatsNewLines) {
					GUILayout.Label($"• {line}", Theme.Body);
				}
				GUILayout.BeginHorizontal();
				if (GUILayout.Button("Got it", Theme.ButtonPrimary)) {
					DismissWhatsNew();
				}
				if (GUILayout.Button("Take the new tour", Theme.Button)) {
					DismissWhatsNew();
					StartTour();
				}
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
				GUILayout.EndVertical();
			}
			if (homeResult_.Length > 0) {
				GUILayout.Label(homeResult_, Theme.Body);
			}

			GUILayout.Label("Right now", Theme.SectionLabel);
			GUILayout.Label(map_ == null ? "Not on a map." : $"On <b>{map_.Name}</b>{(car_ != null ? $" in the {car_.CarName}" : "")}", Theme.Body);
			DrawStatusPill(carStatus_);

			// The four parts of the mod, in their colours.
			GUILayout.Label("Go to", Theme.SectionLabel);
			GUILayout.BeginVertical(Theme.Card);
			for (int pillar = 0; pillar < Theme.Pillars.Length; pillar++) {
				GUILayout.BeginHorizontal();
				GUILayout.Label($"<b><color={Theme.PillarHex[pillar]}>{Theme.Pillars[pillar]}</color></b>", Theme.Body, GUILayout.Width(250));
				switch (pillar) {
					case 0:
						if (GUILayout.Button("Put a TRC tune on", Theme.Button)) {
							tab_ = MyCarTab;
							OnTabOpened(MyCarTab);
						}
						if (GUILayout.Button("Tune sets", Theme.Button)) {
							tab_ = TuneSetsTab;
							OnTabOpened(TuneSetsTab);
						}
						if (GUILayout.Button("Players here", Theme.Button)) {
							tab_ = PlayersTab;
							OnTabOpened(PlayersTab);
						}
						break;
					case 1:
						if (GUILayout.Button("Tracks", Theme.Button)) {
							GoToTimeAttack(0);
						}
						if (GUILayout.Button("Make a course", Theme.Button)) {
							GoToTimeAttack(3);
						}
						if (GUILayout.Button("My times", Theme.Button)) {
							timesView_ = 0;
							GoToTimeAttack(2);
						}
						break;
					case 2:
						if (GUILayout.Button("Challenge someone", Theme.Button)) {
							GoToBattle(0);
						}
						if (GUILayout.Button("Races", Theme.Button)) {
							GoToBattle(1);
						}
						if (GUILayout.Button("Fun games", Theme.Button)) {
							GoToBattle(2);
						}
						break;
					default:
						if (GUILayout.Button("Speed traps", Theme.Button)) {
							GoToTimeAttack(1);
						}
						if (GUILayout.Button("My ratings", Theme.Button)) {
							GoToBattle(3);
						}
						break;
				}
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
			}
			GUILayout.EndVertical();

			// What's waiting: challenges and open races.
			List<RaceMessage> races = race_ == null ? openRaces_.Values.Select(v => v.Lobby).Where(l => l.Phase == RacePhase.Lobby || l.Phase == RacePhase.Finished).ToList() : new List<RaceMessage>();
			if (invites_.Count > 0 || races.Count > 0) {
				GUILayout.Label("Waiting for you", Theme.SectionLabel);
				GUILayout.BeginVertical(Theme.Guide);
				foreach (var (challenge, _) in invites_.ToList()) {
					GUILayout.BeginHorizontal();
					GUILayout.Label($"<b>{BattleMessage.PlainName(challenge.FromName)} challenges you</b>  <color={Theme.MutedHex}>{BattleFormats[(int)challenge.Format]}</color>", Theme.Body);
					GUILayout.FlexibleSpace();
					if (GUILayout.Button("Answer", Theme.ButtonPrimary)) {
						GoToBattle(0);
					}
					GUILayout.EndHorizontal();
				}
				foreach (RaceMessage lobby in races) {
					Course course = lobby.CourseCode.Length > 0 ? courses_.ByCode(lobby.CourseCode) : null;
					string where = lobby.Settings.Mode != RoomMode.Race ? RoomModeNames[(int)lobby.Settings.Mode] : course?.Name ?? lobby.CourseCode;
					GUILayout.BeginHorizontal();
					GUILayout.Label($"<b>{BattleMessage.PlainName(lobby.HostName)}'s {(lobby.Settings.Mode != RoomMode.Race ? "game" : "race")}</b>  <color={Theme.MutedHex}>{where} · {lobby.Entries.Count}/{lobby.Settings.MaxPlayers}</color>", Theme.Body);
					GUILayout.FlexibleSpace();
					bool enabled = GUI.enabled;
					GUI.enabled = enabled && lobby.Entries.Count < lobby.Settings.MaxPlayers;
					if (GUILayout.Button("Join", Theme.ButtonPrimary)) {
						JoinRace(lobby);
						GoToBattle(lobby.Settings.Mode == RoomMode.Race ? 1 : 2);
					}
					GUI.enabled = enabled;
					GUILayout.EndHorizontal();
				}
				GUILayout.EndVertical();
			}

			DrawRoomBoard();
			DrawSharedCourses();
			DrawBugReport();
			GUILayout.EndScrollView();
		}
	}
}
