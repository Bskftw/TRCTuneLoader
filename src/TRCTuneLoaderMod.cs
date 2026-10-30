using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// Entry point. KSL finds this class through the KSLMeta attribute.
	/// BaseMod is a Unity MonoBehaviour, so Start() runs once when the mod loads.
	///
	/// This file holds the logic (checks, apply, players, backups). The TRC window that shows it
	/// is drawn in TRCTuneLoaderMod.Window.cs; the KSL menu page is just a launcher.
	///
	/// Keep <see cref="Version"/> in step with the GitHub release tag (v0.5.0 -> "0.5.0"):
	/// the KSL updater compares them.
	///
	/// KSL builds the mod's GUID as "Author.Name" and rejects spaces, so the name is one word.
	/// The name must match the KSL control panel registration, and the author must be the
	/// registered developer (bskftw): with any other author, maykr's build request is denied.
	/// </summary>
	[KSLMeta("TRCTuneLoader", Version, "bskftw")]
	public partial class TRCTuneLoaderMod : BaseMod {
		public const string Version = "0.15.0";

		private const string BackupsFolderName = "Backups";
		private const float CarRefreshSeconds = 1.0f;
		private const float PlayersRefreshSeconds = 2.0f;
		private const float ReportRequestSeconds = 15.0f;
		private const float ReferenceCheckSeconds = 2.0f;

		/// <summary>How long the car is on the main garage screen before its live reference is recorded on its own.</summary>
		private const float GarageReferenceSeconds = 2.0f;

		private readonly OfficialTunes official_ = new OfficialTunes();
		private readonly ModCheck modCheck_ = new ModCheck();
		private ICarTuneAdapter adapter_;
		private TrcSync sync_;
		private string modFolder_ = "";

		private CarInfo car_;
		private string carError_ = "";
		private float nextCarRefresh_;

		private string lastResult_ = "";
		private List<string> lastDiffs_ = new List<string>();

		private float nextReferenceCheck_;
		private string referencePlace_ = "";
		private float referenceSince_;
		private string lastReferenceResult_ = "";

		/// <summary>The tune set the player picked to apply (the main set unless they pick another).</summary>
		private string chosenSet_ = TuneSets.Main.Id;

		/// <summary>The set shown on the Tune sets tab.</summary>
		private string listSet_ = TuneSets.Main.Id;

		private List<PlayerRow> players_ = new List<PlayerRow>();
		private string playersError_ = "";
		private float nextPlayersRefresh_;
		private float nextReportRequest_;
		private PlayerRow selectedPlayer_;

		private List<TuneFile> backups_ = new List<TuneFile>();
		private string backupResult_ = "";
		private Vector2 backupScroll_;

		private string BackupsFolder => Path.Combine(modFolder_, BackupsFolderName);

		/// <summary>One player in the room with the results of checking them.</summary>
		private sealed class PlayerRow {
			public RoomPlayer Player;
			public TuneFile Official;
			public TuneCheck.Result Setup;
			public string SetupText = "";
			public TrcSync.Report Report;
		}

		/// <summary>The result of checking the player's own car.</summary>
		private sealed class OwnCheck {
			/// <summary>The official tune the saved setup matches, or (when none matches) the one it was compared with.</summary>
			public TuneFile Official;
			public bool Matched;
			public TuneCheck.Result Saved;
			public bool LiveChecked;
			public List<string> LiveDifferences = new List<string>();
			public string LiveNote = "";

			/// <summary>How body parts (widebody kits) change the stance compared with the stock body.</summary>
			public List<string> StanceDifferences = new List<string>();

			/// <summary>Steam Workshop bodies on the car.</summary>
			public List<WorkshopItem> Workshop = new List<WorkshopItem>();

			/// <summary>Engine, gearbox and drivetrain against CarX's own data (null when it couldn't be checked).</summary>
			public DrivetrainCheck Drivetrain;
			public string Error = "";

			public bool DrivetrainChanged => Drivetrain != null && !Drivetrain.Stock;
		}

		private void Start() {
			modFolder_ = GetModFolder();
			// Files that older versions kept next to a loose .ksm come into the mod's folder first, logs included.
			List<string> moved = new List<string>();
			string moveError = "";
			try {
				moved = DataFolder.MoveIn(string.IsNullOrEmpty(Location) ? null : Path.GetDirectoryName(Location), modFolder_);
			} catch (Exception e) {
				moveError = e.Message;
			}
			TrcLog.Start(modFolder_, $"TRC Tune Loader {Version} log, started {DateTime.Now:yyyy-MM-dd HH:mm:ss}. KSL's own log is kino/output.log.");
			TrcLog.Info($"Running from {Location}; files in {modFolder_}.");
			foreach (string line in moved) {
				TrcLog.Info($"Tidied up: {line}.");
			}
			if (moveError.Length > 0) {
				TrcLog.Warning($"Couldn't move all the old files into {modFolder_}: {moveError}");
			}
			FindOtherCopies();
			adapter_ = new CarXTuneAdapter();

			official_.Load();
			string perSet = string.Join(", ", official_.Sets.Select(s => $"{TuneSets.NameOf(s)}: {official_.Tunes.Count(t => t.SetId == s)}"));
			TrcLog.Info($"TRC Tune Loader {Version} started with {official_.Tunes.Count} official tune(s) ({perSet}).");
			foreach (string error in official_.Errors) {
				TrcLog.Warning($"Official tune problem: {error}");
			}

			sync_ = new TrcSync(BuildOwnReport);
			sync_.Start();
			TrcLog.Info(sync_.Status);

			try {
				SetupCourses();
				SetupBattles();
				SetupRatings();
				SetupRaces();
			} catch (Exception e) {
				TrcLog.Error(e);
			}
			SetupWindow();
			try {
				SetupHome();
			} catch (Exception e) {
				TrcLog.Error(e);
			}
		}

		private void Update() {
			sync_?.Tick();
			UpdateWindowInput();
			if (windowOpen_) {
				NameCars();
			}
			if (!tourSeen_) {
				try {
					StartTourWhenReady();
				} catch (Exception e) {
					tourSeen_ = true;
					TrcLog.Error(e);
				}
			}
			try {
				UpdateLiveReference();
			} catch (Exception e) {
				nextReferenceCheck_ = Time.unscaledTime + 30f;
				TrcLog.Error(e);
			}
			try {
				UpdateTrackStatus();
			} catch (Exception e) {
				showBadge_ = false;
				autoCheck_ = false;
				TrcLog.Error(e);
			}
			try {
				UpdateCourses();
			} catch (Exception e) {
				if (run_ != null) {
					run_.Cancel($"Time attack problem: {e.Message}");
				}
				hasPose_ = false;
				// It runs every frame: only the first few errors go to the log.
				if (courseErrors_++ < 3) {
					TrcLog.Error(e);
				}
			}
			try {
				UpdateRaces();
			} catch (Exception e) {
				if (courseErrors_++ < 6) {
					TrcLog.Error(e);
				}
			}
			try {
				UpdateBattles();
			} catch (Exception e) {
				if (battle_ != null && battle_.Active) {
					battle_.Leave(BattleNow);
					battleResult_ = $"Battle problem: {e.Message}";
				}
				if (courseErrors_++ < 6) {
					TrcLog.Error(e);
				}
			}
			try {
				UpdateControl();
			} catch (Exception e) {
				if (courseErrors_++ < 6) {
					TrcLog.Error(e);
				}
			}
			try {
				UpdateHome();
				UpdateLook();
			} catch (Exception e) {
				if (courseErrors_++ < 6) {
					TrcLog.Error(e);
				}
			}
		}

		private void OnDestroy() {
			CloseWindow();
			CleanUpCourses();
		}

		/// <summary>The KSL menu page: a launcher for the TRC window.</summary>
		public override void OnUIDraw() {
			if (adapter_ == null) {
				Kino.UI.Label("Starting...");
				return;
			}

			NameCars();
			RefreshCarIfDue();

			Kino.UI.GroupLabel("TRC Tune Loader");
			Kino.UI.Label(Theme.Tagline);
			Kino.UI.Label(car_ != null
				? $"Your car: {car_.Describe()}, {(car_.InGarage ? "in the garage" : "on track")}"
				: carError_);
			foreach (string error in official_.Errors) {
				Kino.UI.Label($"Error: {error}");
			}

			if (Kino.UI.Button(windowOpen_ ? "Close the TRC Tune Loader window" : "Open the TRC Tune Loader window")) {
				ToggleWindow();
			}
			Kino.UI.Label($"Shortcut: {shortcut_}. You can change it in the window's Options tab.");
		}

		public override void OnAdditionalAboutUIDraw() {
			Kino.UI.Label($"{Theme.Tagline}. Made for TRC (Touge Racing Community): the official TRC spec tunes and checks, plus time attack, a course builder, battles, races and tournaments.");
			Kino.UI.Label($"Version {Version}, {official_.Tunes.Count} official tune(s) built in: {string.Join(", ", official_.Sets.Select(TuneSets.NameOf))}.");
		}

		public override Texture2D GetIcon() {
			var assembly = Assembly.GetExecutingAssembly();
			return Kino.Utils.LoadEmbeddedTexture(assembly, "TRC.TuneLoader.Resources.icon.png");
		}

		// ---- Your car ----

		private void ApplyOfficial(TuneFile tune) {
			lastDiffs_ = new List<string>();
			try {
				if (!RefreshCar()) {
					lastResult_ = $"Couldn't apply: {carError_}";
					return;
				}

				bool intoDyno = car_.InDyno;
				if (!ApplyWithBackup(tune, $"Your tune before applying {tune.DisplayName}", out string error, out string backupNote)) {
					lastResult_ = $"Couldn't {(intoDyno ? "load" : "apply")}: {error}";
					return;
				}

				if (intoDyno) {
					lastResult_ = $"Loaded {tune.DisplayName} into CarX's tuning screen. To keep it, leave the tuning screen and choose Yes when CarX asks to save. {backupNote}";
					TrcLog.Info($"Loaded {tune.DisplayName} ({tune.Fingerprint}) into the dyno for {car_.Describe()}. {backupNote}");
					return;
				}

				TrcLog.Info($"Applied {tune.DisplayName} ({tune.Fingerprint}) to {car_.Describe()}. {backupNote}");
				CheckOwnCarFromMenu();
				lastResult_ = $"Applied {tune.DisplayName}. {backupNote} Check: {lastResult_}";
			} catch (Exception e) {
				lastResult_ = $"Couldn't apply: {e.Message}";
				TrcLog.Error(e);
			}
		}

		/// <summary>
		/// Backs up the player's saved tune (unless it's already official or already backed up), then applies
		/// it on the main garage screen, or loads it into CarX's tuning screen when that's open (also in rooms).
		/// </summary>
		private bool ApplyWithBackup(TuneFile tune, string backupNote, out string error, out string backupResult) {
			backupResult = "";
			bool intoDyno = car_.InDyno;
			error = "";
			if (!intoDyno && !adapter_.CanApply(car_, out error)) {
				return false;
			}
			if (!tune.IsFor(car_.CarId, car_.SlotId)) {
				error = $"This tune is for car {tune.CarId}, setup {tune.SlotId}. You're in {car_.Describe()}, car {car_.CarId}, setup {car_.SlotId}.";
				return false;
			}

			if (!adapter_.TryReadCurrentTune(car_, out var current, out error)) {
				error = $"couldn't back up your current tune, so nothing was changed ({error})";
				return false;
			}

			TuneFile latest = TuneBackups.ForCar(BackupsFolder, car_).FirstOrDefault();
			TuneFile alreadyOfficial = MatchingOfficial(current);

			if (alreadyOfficial != null) {
				backupResult = $"Your car had the official {alreadyOfficial.Name} tune, so no backup was needed.";
			} else if (latest != null && TuneBackups.SameValues(latest.Values, current)) {
				backupResult = "Your previous tune was already in Backups.";
			} else {
				string path = TuneBackups.Save(BackupsFolder, car_, current, backupNote, adapter_);
				backupResult = "Your previous tune is in Backups.";
				TrcLog.Info($"Backed up {car_.Describe()} to {path}");
				PruneBackups();
			}

			return intoDyno ? adapter_.TryLoadIntoDyno(car_, tune, out error) : adapter_.TryApplyTune(car_, tune, out error);
		}

		/// <summary>The official tune (any set) these values match on the player's car, or null.</summary>
		private TuneFile MatchingOfficial(IDictionary<string, float> values) {
			if (!adapter_.TryGetDefaults(car_.SlotId, out var defaults, out _)) {
				return null;
			}
			return official_.ForCar(car_.CarId, car_.SlotId).FirstOrDefault(t =>
				TuneCheck.Compare(car_.CarIdText, car_.SlotIdText, t.Fingerprint, t.Values, values, defaults).Matches);
		}

		private void CheckOwnCarFromMenu() {
			try {
				if (!RefreshCar()) {
					lastResult_ = $"Couldn't check: {carError_}";
					lastDiffs_ = new List<string>();
					return;
				}

				OwnCheck check = CheckOwnCar(recordInGarage: true);
				lastDiffs_ = new List<string>();
				if (check.Saved != null) {
					lastDiffs_.AddRange(check.Saved.Differences);
				}
				lastDiffs_.AddRange(check.StanceDifferences);
				lastDiffs_.AddRange(check.Workshop.Select(w => w.Describe()));
				if (check.Drivetrain != null) {
					lastDiffs_.Add($"Engine & drivetrain: {check.Drivetrain.Summary()}");
					lastDiffs_.AddRange(check.Drivetrain.Differences);
					if (check.Drivetrain.Differences.Count > 0 && car_.InGarage) {
						// Seen in game: KINO leaves its clutch value behind after its swap is set back to stock.
						lastDiffs_.Add("Just took a KINO swap off? It can leave values behind: press Apply to put CarX's own values back.");
					}
				}
				lastDiffs_.AddRange(check.LiveDifferences);
				lastResult_ = Summarize(check);

				TrcLog.Info($"Check on {car_.Describe()}: {lastResult_}");
				foreach (string diff in lastDiffs_) {
					TrcLog.Info($"  {diff}");
				}
				if (check.Drivetrain != null) {
					TrcLog.Info("  Physics engine / car description:");
					foreach (string line in check.Drivetrain.PhysicsLines) {
						TrcLog.Info($"    {line}");
					}
				}
			} catch (Exception e) {
				lastResult_ = $"Check failed: {e.Message}";
				TrcLog.Error(e);
			}
		}

		/// <summary>
		/// Checks the saved setup against the official tunes, then the live car against the reference CarX
		/// produced for it. In the garage, with an official saved setup, the reference is (re)recorded first.
		/// </summary>
		private OwnCheck CheckOwnCar(bool recordInGarage) {
			var check = new OwnCheck();
			List<TuneFile> tunes = official_.ForCar(car_.CarId, car_.SlotId);
			if (tunes.Count == 0) {
				check.Error = $"There's no TRC tune for this car on its {car_.SlotName} setup. The Tune sets tab lists the cars that have one.";
				return check;
			}

			if (!adapter_.TryReadCurrentTune(car_, out var current, out string error) ||
				!adapter_.TryGetDefaults(car_.SlotId, out var defaults, out error)) {
				check.Error = $"Couldn't read your tune: {error}";
				return check;
			}

			// Which official set (if any) is on the car? If none, compare with the set the player picked.
			int slotId = car_.SlotId;
			Func<TuneFile, TuneCheck.Result> compare = t => TuneCheck.Compare(car_.CarIdText, car_.SlotIdText, t.Fingerprint, t.Values, current, defaults,
				(key, value) => DescribeValue(slotId, key, value));
			foreach (TuneFile tune in tunes) {
				TuneCheck.Result result = compare(tune);
				if (result.Matches) {
					check.Official = tune;
					check.Matched = true;
					check.Saved = result;
					break;
				}
			}
			if (!check.Matched) {
				check.Official = tunes.Find(t => t.SetId == chosenSet_) ?? tunes[0];
				check.Saved = compare(check.Official);
			}

			if (adapter_.TryCheckStance(car_, out var stance, out _)) {
				check.StanceDifferences = stance;
			}
			if (adapter_.TryGetWorkshopItems(car_, out var workshop, out _)) {
				// Your car is built with its Workshop body, so the live stance check is what counts.
				workshop.ForEach(w => w.MovesWheels &= check.StanceDifferences.Count > 0);
				check.Workshop = workshop;
			}
			if (adapter_.TryCheckDrivetrain(car_, current, out var drivetrain, out _)) {
				check.Drivetrain = drivetrain;
			}

			if (recordInGarage && car_.InGarage && check.Saved.Matches &&
				!adapter_.TryRecordReference(car_, check.Official.Fingerprint, out string recordError)) {
				check.LiveNote = recordError;
			}

			check.LiveChecked = adapter_.TryCheckLive(car_, check.Official.Fingerprint, out var liveDifferences, out string note);
			check.LiveDifferences = liveDifferences;
			if (!check.LiveChecked && string.IsNullOrEmpty(check.LiveNote)) {
				check.LiveNote = note;
			}
			return check;
		}

		/// <summary>
		/// Records the live check's reference without the player pressing Check, every time a car with an official
		/// saved setup has none yet this session: on the main garage screen the way Check does (CarX puts the saved
		/// setup back on the car first), or, for a car that went straight to the track (joining a room, swapping car
		/// in one), from the car CarX built there, a few seconds after it arrives.
		/// </summary>
		private void UpdateLiveReference() {
			if (Time.unscaledTime < nextReferenceCheck_) {
				return;
			}
			nextReferenceCheck_ = Time.unscaledTime + ReferenceCheckSeconds;
			if (!RefreshCar() || car_.InDyno) {
				referencePlace_ = "";
				return;
			}

			// Wait until the car has been in the same place (garage or track) for a moment, so CarX has finished building it.
			string place = $"{car_.CarId}:{car_.SlotId}:{(car_.InGarage ? "garage" : "track")}";
			if (place != referencePlace_) {
				referencePlace_ = place;
				referenceSince_ = Time.unscaledTime;
				return;
			}
			if (Time.unscaledTime - referenceSince_ < (car_.InGarage ? GarageReferenceSeconds : ArrivalCheckSeconds)) {
				return;
			}

			if (!adapter_.TryReadCurrentTune(car_, out var current, out _)) {
				return;
			}
			TuneFile official = MatchingOfficial(current);
			if (official == null || adapter_.HasReference(car_, official.Fingerprint)) {
				return;
			}

			bool recorded = car_.InGarage
				? adapter_.TryRecordReference(car_, official.Fingerprint, out string error)
				: adapter_.TryRecordReferenceOnTrack(car_, official.Fingerprint, out error);
			string result = recorded
				? $"Live check reference recorded {(car_.InGarage ? "in the garage" : "when the car reached the track")} for {car_.Describe()} ({official.Name}, {official.Fingerprint})."
				: $"Live check reference for {car_.Describe()} not recorded yet: {error}";
			// A garage screen that isn't the main one fails every few seconds; say so once.
			if (result != lastReferenceResult_) {
				lastReferenceResult_ = result;
				TrcLog.Info(result);
			}
			if (recorded) {
				nextStatusRefresh_ = 0;
			}
		}

		/// <summary>"TRC Revival", or "TRC Revival (same tune as TRC V2.0)" when other sets have the identical tune.</summary>
		private string SetLabel(TuneFile tune) {
			if (!int.TryParse(tune.CarId, out int carId) || !int.TryParse(tune.SlotId, out int slotId)) {
				return tune.Name;
			}
			List<string> same = official_.ForCar(carId, slotId)
				.Where(t => t != tune && t.Fingerprint == tune.Fingerprint)
				.Select(t => t.Name)
				.ToList();
			return same.Count == 0 ? tune.Name : $"{tune.Name} (same tune as {string.Join(", ", same)})";
		}

		private string Summarize(OwnCheck check) {
			if (check.Saved == null) {
				return check.Error;
			}

			string code = check.Saved.Code;
			string set = check.Matched ? SetLabel(check.Official) : check.Official.Name;
			string extra = check.LiveDifferences.Count > 0 ? $" The live car differs in {check.LiveDifferences.Count} place(s)." : "";

			if (!check.Saved.Matches) {
				return $"Not TRC spec: your saved setup matches no official set; {check.Saved.Differences.Count} difference(s) from {set}.{extra} Your code: {code}";
			}
			if (check.StanceDifferences.Count > 0) {
				return $"Not TRC spec: your saved setup is {set} spec, but the car's stance (ride height, track width or axles) isn't stock (see below).{extra} Code of the saved setup: {code}";
			}
			if (check.DrivetrainChanged) {
				return $"Not TRC spec: your saved setup is {set} spec, but {DrivetrainProblem(check.Drivetrain)} (see below).{extra} Code of the saved setup: {code}";
			}
			if (!check.LiveChecked) {
				return $"Saved setup is {set} spec (code {code}). Live car not checked: {check.LiveNote}";
			}
			return check.LiveDifferences.Count == 0
				? $"{set} spec: saved setup and live car. Your code: {code}"
				: $"Saved setup is {set} spec, but the live car differs in {check.LiveDifferences.Count} place(s), so this isn't TRC spec. Code of the saved setup: {code}";
		}

		private static string DrivetrainProblem(DrivetrainCheck drivetrain) => drivetrain.EngineSwap
			? "a CarX engine swap is installed"
			: "the engine, gearbox, drivetrain or suspension was changed outside CarX's tuning (a KINO swap or air suspension, or another mod)";

		private string DescribeValue(int slotId, string key, float value) {
			var single = new Dictionary<string, float> { [key] = value };
			return adapter_.TryDescribe(slotId, single, out var text, out _) && text.TryGetValue(key, out string shown)
				? shown
				: TuneFingerprint.FormatValue(value);
		}

		/// <summary>Every setting of a tune the way CarX's tuning screen shows it, grouped ("---" lines are headings).</summary>
		private List<string> ValueLines(TuneFile tune) {
			var lines = new List<string>();
			try {
				int slotId = int.TryParse(tune.SlotId, out int id) ? id : 0;
				lines.Add($"{tune.Slot} setup · code {tune.Fingerprint} · slider positions run from 0 to 1");
				adapter_.TryDescribe(slotId, tune.Values, out var shown, out string error);

				string group = null;
				foreach (string key in TuneLabels.InDisplayOrder(tune.Values.Keys)) {
					if (TuneLabels.Group(key) != group) {
						group = TuneLabels.Group(key);
						lines.Add($"--- {group} ---");
					}
					string real = shown.TryGetValue(key, out string text) ? text : "?";
					lines.Add($"{TuneLabels.Label(key)}: {real}   <color={Theme.MutedHex}>(slider {TuneFingerprint.FormatValue(tune.Values[key])})</color>");
				}

				if (adapter_.TryGetDefaults(slotId, out var defaults, out _)) {
					var notListed = defaults.Keys.Where(k => !tune.Values.ContainsKey(k)).ToList();
					if (notListed.Count > 0) {
						lines.Add("--- Not in this tune: must stay at the car's default ---");
						lines.AddRange(TuneLabels.InDisplayOrder(notListed).Select(TuneLabels.Label));
					}
				}

				if (shown.Count == 0) {
					lines.Insert(1, $"Real numbers aren't available ({error}). Slider positions are shown instead.");
				}
			} catch (Exception e) {
				lines.Add($"Couldn't read the real numbers: {e.Message}");
				TrcLog.Error(e);
			}
			return lines;
		}

		// ---- Players in the room ----

		private void RefreshPlayersIfDue() {
			if (Time.unscaledTime >= nextReportRequest_) {
				nextReportRequest_ = Time.unscaledTime + ReportRequestSeconds;
				sync_.RequestReports();
			}

			if (Time.unscaledTime < nextPlayersRefresh_) {
				return;
			}
			nextPlayersRefresh_ = Time.unscaledTime + PlayersRefreshSeconds;

			try {
				if (!adapter_.TryGetRoomPlayers(out var players, out string error)) {
					playersError_ = error;
					players_ = new List<PlayerRow>();
					return;
				}

				playersError_ = "";
				players_ = players.Select(CheckPlayer).ToList();
			} catch (Exception e) {
				playersError_ = $"Couldn't read the room: {e.Message}";
				TrcLog.Error(e);
			}
		}

		private PlayerRow CheckPlayer(RoomPlayer player) {
			var row = new PlayerRow {
				Player = player,
				Report = sync_.ReportFrom(player.SteamId),
			};

			List<TuneFile> tunes = official_.ForCar(player.CarId, player.SlotId);
			if (tunes.Count == 0) {
				row.SetupText = "No TRC tune for this car and setup";
			} else if (player.EngineId > 0) {
				row.SetupText = player.Drivetrain != null && player.Drivetrain.EngineSwap ? $"{player.Drivetrain.Engine}: not TRC spec" : "CarX engine swap: not TRC spec";
			} else if (adapter_.TryGetDefaults(player.SlotId, out var defaults, out _)) {
				int slotId = player.SlotId;
				Func<TuneFile, TuneCheck.Result> compare = t => TuneCheck.Compare(player.CarIdText, player.SlotIdText, t.Fingerprint, t.Values, player.SharedTune, defaults,
					(key, value) => DescribeValue(slotId, key, value));

				// Which official set (if any) is this player on? If none, show the differences from the main set.
				foreach (TuneFile tune in tunes) {
					TuneCheck.Result result = compare(tune);
					if (result.Matches) {
						row.Official = tune;
						row.Setup = result;
						break;
					}
				}
				if (row.Official == null) {
					row.Official = tunes[0];
					row.Setup = compare(row.Official);
				}

				row.SetupText = row.Setup.Matches
					? $"Setup: {SetLabel(row.Official)} spec"
					: $"Setup modified ({row.Setup.Differences.Count} from {row.Official.Name})";
			} else {
				row.SetupText = "Setup couldn't be checked";
			}

			if (DrivetrainChanged(player)) {
				row.SetupText += " · engine, gearbox or suspension changed outside CarX";
			}
			if (player.StanceDifferences.Count > 0) {
				row.SetupText += player.Workshop.Exists(w => w.MovesWheels) ? " · Workshop body changes the stance" : " · stance not stock";
			} else if (UnknownWorkshop(player)) {
				row.SetupText += " · Workshop body, stance unknown";
			} else if (player.Workshop.Count > 0) {
				row.SetupText += " · Workshop body (looks only)";
			}
			return row;
		}

		/// <summary>A Workshop body this PC doesn't have, so whether it moves the wheels can't be told here.</summary>
		private static bool UnknownWorkshop(RoomPlayer player) => player.Workshop.Exists(w => !w.Installed);

		/// <summary>Their engine, gearbox or drivetrain differs from CarX's own data (a CarX engine swap is flagged on its own).</summary>
		private static bool DrivetrainChanged(RoomPlayer player) => player.Drivetrain != null && player.Drivetrain.Differences.Count > 0;

		/// <summary>Anything that makes the player's car not TRC spec apart from the setup itself.</summary>
		private static bool CarNotSpec(RoomPlayer player) => player.StanceDifferences.Count > 0 || DrivetrainChanged(player);

		private static string LiveText(TrcSync.Report report) {
			switch (report.LiveStatus) {
				case 1: return "matches";
				case 2: return $"differs ({report.LiveDifferences})";
				default: return "not checked yet";
			}
		}

		/// <summary>What this player's TRC Tune Loader tells others in the room (see TrcSync).</summary>
		private TrcSync.Report BuildOwnReport() {
			try {
				RefreshCar();
				modCheck_.Refresh();

				var report = new TrcSync.Report {
					SteamId = Kino.Info.SteamId,
					LoaderVersion = Version,
					ProtectedMode = modCheck_.ProtectedMode,
					AllVerified = modCheck_.AllVerified,
					Mods = modCheck_.Entries.ToList(),
				};

				if (car_ != null) {
					report.CarId = car_.CarId;
					report.SlotId = car_.SlotId;
					OwnCheck check = CheckOwnCar(recordInGarage: false);
					if (check.Saved != null) {
						report.SetupCode = check.Saved.Code;
						report.SetupOfficial = check.Saved.Matches;
						report.SetupSet = check.Matched ? SetLabel(check.Official) : "";
						report.SetupDifferences = check.Saved.Differences.Count;
					}
					report.LiveStatus = (byte)(!check.LiveChecked ? 0 : check.LiveDifferences.Count == 0 ? 1 : 2);
					report.LiveDifferences = check.LiveDifferences.Count;
					report.StanceDifferences = check.StanceDifferences.Count;
					if (check.Drivetrain != null) {
						report.Engine = check.Drivetrain.Engine;
						report.Drivetrain = check.Drivetrain.Drivetrain;
						report.DrivetrainDifferences = check.Drivetrain.Differences.Count;
					}
				}
				report.ShareValues = shareValues_;
				if (ratings_ != null) {
					report.EloTouge = (float)ratings_.Elo[0];
					report.EloHighway = (float)ratings_.Elo[1];
					report.EloCircuit = (float)ratings_.Elo[2];
					report.Safety = (float)ratings_.Safety;
					report.License = (byte)ratings_.License;
				}

				return report;
			} catch (Exception e) {
				TrcLog.Error(e);
				return null;
			}
		}

		// ---- Backups ----

		private void RefreshBackups() {
			try {
				RefreshCar();
				backups_ = TuneBackups.ForCar(BackupsFolder, car_);
			} catch (Exception e) {
				backups_ = new List<TuneFile>();
				backupResult_ = $"Couldn't read backups: {e.Message}";
				TrcLog.Error(e);
			}
		}

		/// <summary>With a backup limit set in Options, deletes this car and setup's backups over the limit (oldest first).</summary>
		private void PruneBackups() {
			if (backupsKeep_ <= 0) {
				return;
			}
			try {
				List<TuneFile> old = TuneBackups.OverLimit(BackupsFolder, backupsKeep_).Where(b => b.IsFor(car_.CarId, car_.SlotId)).ToList();
				int deleted = TuneBackups.Delete(old);
				if (deleted > 0) {
					TrcLog.Info($"Deleted {deleted} old backup(s) of {car_.Describe()} (keeping the last {backupsKeep_}).");
				}
			} catch (Exception e) {
				TrcLog.Error(e);
			}
		}

		private void RestoreBackup(TuneFile backup) {
			try {
				if (!RefreshCar()) {
					backupResult_ = $"Couldn't restore: {carError_}";
					return;
				}

				bool intoDyno = car_.InDyno;
				backupResult_ = !ApplyWithBackup(backup, $"Your tune before restoring {backup.Name}", out string error, out string backupNote)
					? $"Couldn't restore: {error}"
					: intoDyno
						? $"Loaded {backup.Name} into CarX's tuning screen. Leave it and choose Yes to save. {backupNote}"
						: $"Restored {backup.Name}. {backupNote}";
				TrcLog.Info($"Restore {backup.Name} on {car_.Describe()}: {backupResult_}");
				RefreshBackups();
			} catch (Exception e) {
				backupResult_ = $"Couldn't restore: {e.Message}";
				TrcLog.Error(e);
			}
		}

		// ---- Helpers ----

		private void NameCars() {
			try {
				official_.NameCars(adapter_);
			} catch (Exception e) {
				TrcLog.Error(e);
			}
		}

		private void RefreshCarIfDue() {
			if (Time.unscaledTime >= nextCarRefresh_) {
				RefreshCar();
			}
		}

		private bool RefreshCar() {
			nextCarRefresh_ = Time.unscaledTime + CarRefreshSeconds;
			try {
				if (adapter_.TryGetCurrentCar(out var car, out string error)) {
					car_ = car;
					carError_ = "";
					return true;
				}
				car_ = null;
				carError_ = error;
			} catch (Exception e) {
				car_ = null;
				carError_ = $"CarX connection problem: {e.Message}";
			}
			return false;
		}

		/// <summary>
		/// The mod's files always live in kino/mods/TRCTuneLoader, wherever the .ksm is (KSL's mod list can put it loose
		/// in kino/mods). Only when KSL doesn't say where its mods are, next to the .ksm.
		/// </summary>
		private string GetModFolder() {
			string mods = "";
			try {
				mods = Kino.Paths.Mods;
			} catch (Exception) {
				// Below.
			}
			if (!string.IsNullOrEmpty(mods)) {
				return DataFolder.For(mods);
			}
			string folder = string.IsNullOrEmpty(Location) ? null : Path.GetDirectoryName(Location);
			return string.IsNullOrEmpty(folder) ? DataFolder.Name : folder;
		}
	}
}
