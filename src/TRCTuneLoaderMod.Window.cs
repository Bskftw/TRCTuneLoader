using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// The TRC Tune Loader window: its own IMGUI window with the TRC look (see Theme), opened from the
	/// KSL menu or with the shortcut. This file only draws; the logic is in TRCTuneLoaderMod.cs, and the
	/// Options tab, shortcut and tour are in TRCTuneLoaderMod.Options.cs.
	/// </summary>
	public partial class TRCTuneLoaderMod {
		private const int WindowId = 0x7C1A11;
		private const float WindowWidth = 780f;
		private const float WindowHeight = 640f;
		private const float StatusRefreshSeconds = 2.0f;
		private static readonly string[] TabNames = { "Home", "My car", "Tune sets", "Players", "Mod check", "Backups", "Time attack", "Options", "About" };

		// UGUI's EventSystem lives in UnityEngine.UI; found by name so the mod doesn't need that DLL.
		private static readonly PropertyInfo CurrentEventSystem =
			Type.GetType("UnityEngine.EventSystems.EventSystem, UnityEngine.UI")?.GetProperty("current", BindingFlags.Public | BindingFlags.Static);

		private const int HomeTab = 0;
		private const int MyCarTab = 1;
		private const int TuneSetsTab = 2;
		private const int PlayersTab = 3;
		private const int ModCheckTab = 4;
		private const int BackupsTab = 5;

		private bool windowOpen_;
		private Rect windowRect_;
		private int tab_;
		private bool mouseOverWindow_;
		private Behaviour eventSystemWeDisabled_;
		private bool savedCursorVisible_;
		private CursorLockMode savedCursorLock_;
		private Texture2D logo_;

		private OwnCheck carStatus_;
		private float nextStatusRefresh_;
		private bool showNumbers_;
		private List<string> carNumbers_ = new List<string>();
		private Vector2 carScroll_;

		private TuneFile setTune_;
		private List<string> setNumbers_ = new List<string>();
		private Vector2 setListScroll_;
		private Vector2 setNumbersScroll_;

		private Vector2 playerListScroll_;
		private Vector2 playerDetailScroll_;
		private Vector2 modScroll_;

		private static readonly GUIStyle ContentArea = new GUIStyle { padding = new RectOffset(14, 14, 10, 14) };
		private static readonly GUIStyle HeaderArea = new GUIStyle { padding = new RectOffset(12, 8, 10, 10) };
		private static readonly GUIStyle TabArea = new GUIStyle { padding = new RectOffset(8, 8, 0, 0) };

		private void SetupWindow() {
			try {
				logo_ = Kino.Utils.LoadEmbeddedTexture(Assembly.GetExecutingAssembly(), "TRC.TuneLoader.Resources.trc_logo.png");
			} catch (Exception e) {
				TrcLog.Warning($"Couldn't load the TRC logo: {e.Message}");
			}
			LoadOptions();
		}

		private void ToggleWindow() {
			if (windowOpen_) {
				CloseWindow();
			} else {
				OpenWindow();
			}
		}

		private void OpenWindow() {
			if (windowOpen_) {
				return;
			}

			windowOpen_ = true;
			savedCursorVisible_ = Cursor.visible;
			savedCursorLock_ = Cursor.lockState;
			nextStatusRefresh_ = 0;
			RefreshCar();
			// A challenge is waiting: open right where it's answered.
			if (invites_.Count > 0 && tourSeen_) {
				tab_ = CoursesTab;
				coursesView_ = 1;
			}
			OnTabOpened(tab_);
			if (!tourSeen_ && tourStep_ < 0) {
				StartTour();
			}
		}

		private void CloseWindow() {
			if (!windowOpen_) {
				return;
			}

			windowOpen_ = false;
			mouseOverWindow_ = false;
			capturingShortcut_ = false;
			capturingRestart_ = false;
			if (tourStep_ >= 0) {
				EndTour();
			}
			RestoreEventSystem();
			Cursor.visible = savedCursorVisible_;
			Cursor.lockState = savedCursorLock_;
		}

		/// <summary>Called from Update: keeps the cursor usable and stops clicks reaching CarX's menus under the window.</summary>
		private void UpdateWindowInput() {
			if (!windowOpen_) {
				return;
			}

			Cursor.visible = true;
			Cursor.lockState = CursorLockMode.None;

			try {
				if (mouseOverWindow_) {
					if (eventSystemWeDisabled_ == null && CurrentEventSystem?.GetValue(null, null) is Behaviour eventSystem && eventSystem.enabled) {
						eventSystem.enabled = false;
						eventSystemWeDisabled_ = eventSystem;
					}
				} else {
					RestoreEventSystem();
				}
			} catch (Exception e) {
				TrcLog.Warning($"Window input: {e.Message}");
			}
		}

		private void RestoreEventSystem() {
			if (eventSystemWeDisabled_ != null) {
				eventSystemWeDisabled_.enabled = true;
				eventSystemWeDisabled_ = null;
			}
		}

		private void OnGUI() {
			try {
				HandleShortcutEvent(Event.current);
			} catch (Exception e) {
				TrcLog.Error(e);
			}
			bool hud = !windowOpen_ && OnTrack && (showBadge_ || autoCheck_);
			bool courseHud = CourseHudVisible || NoticeVisible;
			if (!windowOpen_ && !hud && !courseHud) {
				return;
			}

			Theme.Ensure();
			GUISkin previousSkin = GUI.skin;
			Matrix4x4 previousMatrix = GUI.matrix;
			try {
				GUI.skin = Theme.Skin;
				GUI.depth = -100;

				float scale = (Kino.UI.Scale > 0.1f ? Kino.UI.Scale : 1f) * windowScale_;
				GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
				float screenWidth = Screen.width / scale;
				float screenHeight = Screen.height / scale;

				if (courseHud) {
					DrawCourseHud(screenWidth, screenHeight, scale);
				}
				if (hud) {
					DrawHud(screenWidth, screenHeight);
				}
				DrawNotice(screenWidth, screenHeight);
				if (!windowOpen_) {
					return;
				}

				if (windowRect_.width <= 0) {
					windowRect_ = new Rect((screenWidth - WindowWidth) / 2f, (screenHeight - WindowHeight) / 2f, WindowWidth, WindowHeight);
				}
				windowRect_.width = WindowWidth;
				windowRect_.height = Mathf.Min(WindowHeight, screenHeight - 20f);
				windowRect_.x = Mathf.Clamp(windowRect_.x, 0f, Mathf.Max(0f, screenWidth - windowRect_.width));
				windowRect_.y = Mathf.Clamp(windowRect_.y, 0f, Mathf.Max(0f, screenHeight - windowRect_.height));

				windowRect_ = GUI.Window(WindowId, windowRect_, DrawWindow, GUIContent.none, Theme.Window);
				mouseOverWindow_ = windowRect_.Contains(Event.current.mousePosition);
			} catch (Exception e) {
				TrcLog.Error(e);
			} finally {
				GUI.skin = previousSkin;
				GUI.matrix = previousMatrix;
			}
		}

		private void DrawWindow(int id) {
			DrawHeader();
			DrawTabs();

			GUILayout.BeginVertical(ContentArea, GUILayout.ExpandHeight(true));
			bool showTab = tourStep_ < 0 || DrawTourCard();
			if (!showTab) {
				GUILayout.FlexibleSpace();
			} else {
				switch (tab_) {
					case HomeTab: DrawHomeTab(); break;
					case MyCarTab: DrawMyCarTab(); break;
					case TuneSetsTab: DrawTuneSetsTab(); break;
					case PlayersTab: DrawPlayersTab(); break;
					case ModCheckTab: DrawModCheckTab(); break;
					case BackupsTab: DrawBackupsTab(); break;
					case CoursesTab: DrawCoursesTab(); break;
					case OptionsTab: DrawOptionsTab(); break;
					default: DrawAboutTab(); break;
				}
			}
			GUILayout.EndVertical();

			GUI.DragWindow(new Rect(0, 0, WindowWidth, 64));
		}

		private void DrawHeader() {
			GUILayout.BeginHorizontal(HeaderArea);
			if (logo_ != null) {
				Rect logoRect = GUILayoutUtility.GetRect(44, 44, GUILayout.Width(44), GUILayout.Height(44));
				GUI.DrawTexture(logoRect, logo_, ScaleMode.ScaleToFit);
				GUILayout.Space(10);
			}

			GUILayout.BeginVertical();
			GUILayout.Label("TRC Tune Loader", Theme.Title);
			GUILayout.Label($"Touge Racing Community · v{Version}", Theme.Subtitle);
			GUILayout.EndVertical();

			GUILayout.FlexibleSpace();
			if (GUILayout.Button("×", Theme.Close)) {
				CloseWindow();
			}
			GUILayout.EndHorizontal();
			DividerLine();
		}

		private void DrawTabs() {
			GUILayout.BeginHorizontal(TabArea);
			for (int i = 0; i < TabNames.Length; i++) {
				string label = i == PlayersTab && players_.Count > 0 ? $"Players ({players_.Count})"
					: i == CoursesTab && invites_.Count > 0 ? $"{TabNames[i]} ({invites_.Count})"
					: i == HomeTab && whatsNewPending_ ? "Home (new)"
					: TabNames[i];
				if (GUILayout.Button(label, i == tab_ ? Theme.TabActive : Theme.Tab)) {
					tab_ = i;
					OnTabOpened(i);
				}
				if (i == tab_ && Event.current.type == EventType.Repaint) {
					Rect tab = GUILayoutUtility.GetLastRect();
					GUI.DrawTexture(new Rect(tab.x, tab.yMax - 2, tab.width, 2), Theme.AccentLine);
				}
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			DividerLine();
		}

		private void OnTabOpened(int tab) {
			switch (tab) {
				case HomeTab: nextStatusRefresh_ = 0; nextPlayersRefresh_ = 0; break;
				case MyCarTab: nextStatusRefresh_ = 0; break;
				case PlayersTab: nextPlayersRefresh_ = 0; nextReportRequest_ = 0; break;
				case ModCheckTab: modCheck_.Refresh(); break;
				case BackupsTab: backupResult_ = ""; RefreshBackups(); break;
				case CoursesTab: deletePending_ = null; break;
			}
		}

		// ---- My car ----

		private void DrawMyCarTab() {
			RefreshCarIfDue();
			if (car_ == null) {
				GUILayout.Label(carError_, Theme.PillNeutral);
				return;
			}

			GUILayout.Label(car_.CarName, Theme.Heading);
			string where = car_.InDyno ? "in CarX's tuning screen" : car_.InGarage ? "in the garage" : "on track";
			GUILayout.Label($"{car_.SlotName} setup · {where}", Theme.Small);
			GUILayout.Space(6);

			RefreshStatusIfDue();
			DrawStatusPill(carStatus_);
			if (carStatus_ != null) {
				if (carStatus_.Drivetrain != null) {
					GUILayout.Label(carStatus_.Drivetrain.Summary(), Theme.Small);
				}
				foreach (WorkshopItem item in carStatus_.Workshop) {
					GUILayout.Label(item.Describe(), Theme.Small);
				}
			}

			List<TuneFile> tunes = official_.ForCar(car_.CarId, car_.SlotId);
			if (tunes.Count == 0) {
				return;
			}

			TuneFile tune = tunes.Find(t => t.SetId == chosenSet_) ?? tunes[0];
			GUILayout.Label("Tune set", Theme.SectionLabel);
			GUILayout.BeginHorizontal();
			foreach (TuneFile option in tunes) {
				if (GUILayout.Button(TuneSets.NameOf(option.SetId), option == tune ? Theme.ChipActive : Theme.Chip)) {
					chosenSet_ = option.SetId;
					showNumbers_ = false;
					lastResult_ = "";
					lastDiffs_ = new List<string>();
				}
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.Space(6);

			GUILayout.BeginHorizontal();
			if (GUILayout.Button(car_.InDyno ? $"Load {tune.Name} into the dyno" : $"Apply {tune.Name}", Theme.ButtonPrimary)) {
				showNumbers_ = false;
				ApplyOfficial(tune);
				nextStatusRefresh_ = 0;
			}
			if (GUILayout.Button("Check my tune", Theme.Button)) {
				showNumbers_ = false;
				CheckOwnCarFromMenu();
				nextStatusRefresh_ = 0;
			}
			if (GUILayout.Button(showNumbers_ ? "Hide numbers" : "Real numbers", Theme.Button)) {
				showNumbers_ = !showNumbers_;
				if (showNumbers_) {
					carNumbers_ = ValueLines(tune);
				}
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();

			GUILayout.Label(car_.InDyno
				? $"Code {tune.Fingerprint} · loads onto CarX's tuning screen; leave it and choose Yes to save (in a room, CarX shares it with everyone) · your own tune is backed up first"
				: $"Code {tune.Fingerprint} · your own tune is backed up first · Apply works on the main garage screen; in a room, open CarX's tuning screen and load it there", Theme.Small);

			if (showNumbers_) {
				DrawLines(ref carScroll_, carNumbers_);
				return;
			}

			if (!string.IsNullOrEmpty(lastResult_)) {
				GUILayout.Space(4);
				GUILayout.Label(lastResult_, Theme.Body);
			}
			if (lastDiffs_.Count > 0) {
				DrawLines(ref carScroll_, lastDiffs_);
			}
		}

		private void RefreshStatusIfDue() {
			if (Time.unscaledTime < nextStatusRefresh_) {
				return;
			}

			nextStatusRefresh_ = Time.unscaledTime + StatusRefreshSeconds;
			try {
				carStatus_ = car_ != null ? CheckOwnCar(recordInGarage: false) : null;
			} catch (Exception e) {
				carStatus_ = null;
				TrcLog.Error(e);
			}
		}

		private void DrawStatusPill(OwnCheck status) {
			if (status == null) {
				return;
			}

			if (status.Saved == null) {
				GUILayout.Label(status.Error, Theme.PillNeutral);
			} else if (!status.Saved.Matches) {
				GUILayout.Label($"Not TRC spec: {status.Saved.Differences.Count} difference(s) from {status.Official.Name}", Theme.PillBad);
			} else if (status.StanceDifferences.Count > 0) {
				GUILayout.Label($"Not TRC spec: {SetLabel(status.Official)} saved, but the car's stance (ride height, track width or axles) isn't stock. Press Check my tune for details.", Theme.PillBad);
			} else if (status.DrivetrainChanged) {
				GUILayout.Label($"Not TRC spec: {SetLabel(status.Official)} saved, but {DrivetrainProblem(status.Drivetrain)}. Press Check my tune for details.", Theme.PillBad);
			} else if (!status.LiveChecked) {
				GUILayout.Label($"{SetLabel(status.Official)} spec saved. The live car is checked a few seconds after it reaches the garage or the track.", Theme.PillWarn);
			} else if (status.LiveDifferences.Count > 0) {
				GUILayout.Label($"{SetLabel(status.Official)} saved, but the live car differs in {status.LiveDifferences.Count} place(s)", Theme.PillBad);
			} else {
				GUILayout.Label($"{SetLabel(status.Official)} spec: saved setup and live car", Theme.PillGood);
			}
		}

		// ---- Tune sets ----

		private void DrawTuneSetsTab() {
			GUILayout.BeginHorizontal();
			foreach (string set in official_.Sets) {
				if (GUILayout.Button(TuneSets.NameOf(set), set == listSet_ ? Theme.ChipActive : Theme.Chip)) {
					listSet_ = set;
					setTune_ = null;
				}
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();

			List<TuneFile> inSet = official_.Tunes.Where(t => t.SetId == listSet_).ToList();
			GUILayout.Label($"{TuneSets.NameOf(listSet_)}: {inSet.Count} cars. Pick one to see its numbers.", Theme.Small);

			GUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
			GUILayout.BeginVertical(Theme.Card, GUILayout.Width(250), GUILayout.ExpandHeight(true));
			setListScroll_ = GUILayout.BeginScrollView(setListScroll_);
			foreach (TuneFile tune in inSet) {
				bool mine = car_ != null && tune.IsFor(car_.CarId, car_.SlotId);
				string text = (string.IsNullOrEmpty(tune.Car) ? tune.DisplayName : tune.Car) + (mine ? $" <color={Theme.AccentHex}>(your car)</color>" : "");
				if (GUILayout.Button(text, tune == setTune_ ? Theme.RowSelected : Theme.Row)) {
					setTune_ = tune;
					setNumbers_ = ValueLines(tune);
				}
			}
			GUILayout.EndScrollView();
			GUILayout.EndVertical();

			GUILayout.Space(10);
			GUILayout.BeginVertical(GUILayout.ExpandHeight(true));
			if (setTune_ == null) {
				GUILayout.Label("Pick a car on the left to see every setting the way CarX's tuning screen shows it.", Theme.Small);
			} else {
				GUILayout.Label(setTune_.DisplayName, Theme.Heading);
				DrawLines(ref setNumbersScroll_, setNumbers_);
			}
			GUILayout.EndVertical();
			GUILayout.EndHorizontal();
		}

		// ---- Players ----

		private void DrawPlayersTab() {
			RefreshPlayersIfDue();
			GUILayout.Label(sync_.IsAvailable
				? "Setups are checked from what CarX shares with everyone. Mod lists and live checks come from players who also run TRC Tune Loader (self-reported)."
				: $"Setups are checked from what CarX shares with everyone. {sync_.Status}", Theme.Small);

			if (!string.IsNullOrEmpty(playersError_)) {
				GUILayout.Label(playersError_, Theme.PillNeutral);
				return;
			}

			List<PlayerRow> problems = players_.Where(HasProblem).ToList();
			int filter = Choice(new[] { $"Everyone ({players_.Count})", $"Only problems ({problems.Count})" }, problemsOnly_ ? 1 : 0);
			if ((filter == 1) != problemsOnly_) {
				problemsOnly_ = filter == 1;
				SavePref(ProblemsOnlyPref, problemsOnly_);
			}
			List<PlayerRow> shown = problemsOnly_ ? problems : players_;

			GUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
			GUILayout.BeginVertical(Theme.Card, GUILayout.Width(290), GUILayout.ExpandHeight(true));
			playerListScroll_ = GUILayout.BeginScrollView(playerListScroll_);
			if (shown.Count == 0 && problemsOnly_) {
				GUILayout.Label("No problems: everyone here is TRC spec.", Theme.Small);
			}
			foreach (PlayerRow row in shown) {
				bool selected = selectedPlayer_ != null && SamePlayer(row, selectedPlayer_);
				if (GUILayout.Button(PlayerRowText(row), selected ? Theme.RowSelected : Theme.Row)) {
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

			if (GUILayout.Button("Refresh", Theme.Button, GUILayout.Width(100))) {
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
			string loader = row.Report == null
				? $"<color={Theme.MutedHex}>{(row.Player.IsLocal ? "You" : "No TRC Tune Loader")}</color>"
				: flagged > 0
					? $"<color={Theme.BadHex}>TRC Tune Loader {row.Report.LoaderVersion} · {flagged} flagged mod(s)</color>"
					: $"<color={Theme.AccentHex}>TRC Tune Loader {row.Report.LoaderVersion}</color>";
			return $"<b>{row.Player.Name}{(row.Player.IsLocal ? " (you)" : "")}</b>\n" +
				$"<color={Theme.MutedHex}>{row.Player.CarName} ({row.Player.SlotName})</color>\n" +
				$"<color={color}>{row.SetupText}</color>\n{loader}";
		}

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
				lines.Add($"Version {report.LoaderVersion}, reported at {report.ReceivedAt:HH:mm:ss}. Self-reported: a player who edits the game could fake it.");
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

		// ---- Mod check ----

		private void DrawModCheckTab() {
			GUILayout.Label(modCheck_.AllVerified ? "All mods are verified by KSL" : "Some mods aren't verified by KSL", modCheck_.AllVerified ? Theme.PillGood : Theme.PillWarn);
			GUILayout.Label(modCheck_.ProtectedMode ? "KSL protected mode is on" : "KSL protected mode is off", modCheck_.ProtectedMode ? Theme.PillGood : Theme.PillNeutral);
			foreach (string flag in modCheck_.Flags) {
				GUILayout.Label(flag, Theme.PillBad);
			}

			GUILayout.Label($"Loaded mods ({modCheck_.Entries.Count})", Theme.SectionLabel);
			DrawLines(ref modScroll_, modCheck_.Mods);

			if (GUILayout.Button("Refresh", Theme.Button, GUILayout.Width(100))) {
				modCheck_.Refresh();
			}
		}

		// ---- Backups ----

		private void DrawBackupsTab() {
			if (car_ == null) {
				GUILayout.Label(carError_, Theme.PillNeutral);
				return;
			}

			GUILayout.Label($"Backups for {car_.Describe()}: {backups_.Count}. Restoring one backs up your current tune first.", Theme.Small);
			if (!string.IsNullOrEmpty(backupResult_)) {
				GUILayout.Label(backupResult_, Theme.Body);
			}

			GUILayout.BeginVertical(Theme.Card, GUILayout.ExpandHeight(true));
			backupScroll_ = GUILayout.BeginScrollView(backupScroll_);
			if (backups_.Count == 0) {
				GUILayout.Label("No backups yet. One is saved each time a TRC tune replaces your own tune.", Theme.Small);
			}
			foreach (TuneFile backup in backups_) {
				GUILayout.BeginHorizontal();
				GUILayout.Label($"<b>{backup.Name}</b>\n<color={Theme.MutedHex}>{backup.Notes}</color>", Theme.Body);
				GUILayout.FlexibleSpace();
				if (GUILayout.Button("Restore", Theme.Button, GUILayout.Width(90))) {
					RestoreBackup(backup);
				}
				GUILayout.EndHorizontal();
			}
			GUILayout.EndScrollView();
			GUILayout.EndVertical();
		}

		// ---- Shared pieces ----

		/// <summary>A scrolling card of text lines; lines starting with "---" become small pink headings.</summary>
		private static void DrawLines(ref Vector2 scroll, List<string> lines) {
			GUILayout.BeginVertical(Theme.Card, GUILayout.ExpandHeight(true));
			scroll = GUILayout.BeginScrollView(scroll);
			foreach (string line in lines) {
				if (line.StartsWith("---", StringComparison.Ordinal)) {
					GUILayout.Label(line.Trim('-', ' '), Theme.Group);
				} else {
					GUILayout.Label(line, Theme.Body);
				}
			}
			GUILayout.EndScrollView();
			GUILayout.EndVertical();
		}

		private static void DividerLine() {
			Rect line = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true));
			if (Event.current.type == EventType.Repaint) {
				GUI.DrawTexture(line, Theme.Divider);
			}
		}
	}
}
