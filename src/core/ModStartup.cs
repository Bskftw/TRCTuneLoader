// The mod's entry point: KSL starts it here, Update runs everything each frame, and the KSL menu and about page hooks.
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
	/// This file starts the mod and runs Update; everything else is in the folders under src/ (PROJECT_MAP.md
	/// lists them). The TRC window is drawn in src/components/Window.cs; the KSL menu page is just a launcher.
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
		public const string Version = "0.18.4";

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
			SetupRoomGate();

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
				LoadRegulations();
				LoadSpecs();
				SetupBattles();
				SetupRatings();
				SetupRaces();
			} catch (Exception e) {
				TrcLog.Error(e);
			}
			SetupWindow();
			try {
				SetupHome();
				StartDevTools();
			} catch (Exception e) {
				TrcLog.Error(e);
			}
			// Last, so the room channels above keep their places for everyone.
			RegisterDevPackets();
		}

		/// <summary>Empty in the player mod: only a private build on bskftw's PC fills it in (map load timing).</summary>
		partial void StartDevTools();

		private readonly List<(ulong SteamId, string Key)> roomKeys_ = new List<(ulong, string)>();

		/// <summary>
		/// Messages from the room only count from someone CarX lists in it (RoomGate, IMPROVEMENT_PLAN.md N1). Every player is
		/// in under their Steam ID and under the ID a race host uses for a console player (RaceSession.ConsoleId: the host
		/// sends console drivers' messages for them under that ID, so it has to pass).
		/// </summary>
		private void SetupRoomGate() {
			ChannelLimit.Log = text => TrcLog.Warning(text);
			RoomGate.Log = text => TrcLog.Warning(text);
			RoomGate.Me = () => MySteamId;
			RoomGate.ReadRoom = ids => {
				roomKeys_.Clear();
				if (!adapter_.TryGetRoomKeys(roomKeys_)) {
					return false;
				}
				foreach (var (steamId, key) in roomKeys_) {
					if (steamId != 0) {
						ids.Add(steamId);
					}
					ids.Add(RaceSession.ConsoleId(key));
				}
				return true;
			};
		}

		/// <summary>The private builds' own room messages: KSL only takes new kinds of message while the mod starts.</summary>
		partial void RegisterDevPackets();

		private void Update() {
			TrcLog.FlushIfDue();
			PerfLog.BeginUpdate();
			try {
				UpdateMod();
			} finally {
				PerfLog.EndUpdate();
			}
		}

		private void UpdateMod() {
			sync_?.Tick();
			UpdateWindowInput();
			try {
				UpdatePadMenu();
			} catch (Exception e) {
				padMenuOn_ = false;
				ReleasePad();
				TrcLog.Error(e);
			}
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
				UpdateSuspension();
			} catch (Exception e) {
				liveFrame_ = null;
				if (courseErrors_++ < 6) {
					TrcLog.Error(e);
				}
			}
			try {
				UpdateTelemetry();
			} catch (Exception e) {
				telemetry_ = null;
				if (courseErrors_++ < 6) {
					TrcLog.Error(e);
				}
			}
			try {
				UpdateGhosts();
			} catch (Exception e) {
				StopGhost();
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
				LoadRatings();
			} catch (Exception e) {
				if (courseErrors_++ < 6) {
					TrcLog.Error(e);
				}
			}
		}

		private void OnDestroy() {
			CloseWindow();
			inputBlocker_.Destroy();
			StopGhost();
			CleanUpCourses();
			ForgetTrafficCars();
			TrcLog.Close();
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
