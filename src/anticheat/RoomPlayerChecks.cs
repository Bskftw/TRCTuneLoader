// Checking the players in the room: their cars as CarX shares them against the official tunes, and this player's own report for them.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod : BaseMod {
		private List<PlayerRow> players_ = new List<PlayerRow>();
		private string playersError_ = "";
		private float nextPlayersRefresh_;
		private float nextReportRequest_;
		private PlayerRow selectedPlayer_;

		/// <summary>One player in the room with the results of checking them.</summary>
		private sealed class PlayerRow {
			public RoomPlayer Player;
			public TuneFile Official;
			public TuneCheck.Result Setup;
			public string SetupText = "";
			public TrcSync.Report Report;

			/// <summary>This PC's specs that have a tune for their car, checked against what CarX shares of their setup.</summary>
			public List<(PlayerSpec Spec, TuneCheck.Result Result, bool On)> Specs = new List<(PlayerSpec, TuneCheck.Result, bool)>();

			/// <summary>This PC's regulations, checked against their car as CarX shares it.</summary>
			public List<(Regulation Regulation, RegulationResult Result)> Regulations = new List<(Regulation, RegulationResult)>();
		}

		private void RefreshPlayersIfDue() {
			if (Time.unscaledTime >= nextReportRequest_) {
				nextReportRequest_ = Time.unscaledTime + ReportRequestSeconds;
				sync_.RequestReports();
			}

			if (Time.unscaledTime < nextPlayersRefresh_) {
				return;
			}
			nextPlayersRefresh_ = Time.unscaledTime + PlayersRefreshSeconds;

			var watch = System.Diagnostics.Stopwatch.StartNew();
			try {
				if (!adapter_.TryGetRoomPlayers(out var players, out string error)) {
					playersError_ = error;
					players_ = new List<PlayerRow>();
					return;
				}

				playersError_ = "";
				players_ = players.Select(CheckPlayer).ToList();
				sync_.KeepOnly(new HashSet<ulong>(players.Select(p => p.SteamId)));
				// Slow refreshes are logged (at most once a minute), split into reading the room and checking the players (Nov 8).
				double total = watch.Elapsed.TotalMilliseconds;
				if (total > 8.0 && Time.unscaledTime - roomSlowLoggedAt_ > 60f) {
					roomSlowLoggedAt_ = Time.unscaledTime;
					var carx = adapter_ as CarXTuneAdapter;
					TrcLog.Warning($"Room players took {total:0.0} ms: reading the room {(carx != null ? carx.LastRoomMs : 0):0.0} ms ({(carx != null ? carx.LastRoomRechecked : 0)} of {players.Count} checked again), setups and rules {total - (carx != null ? carx.LastRoomMs : 0):0.0} ms.");
				}
			} catch (Exception e) {
				playersError_ = $"Couldn't read the room: {e.Message}";
				TrcLog.Error(e);
			}
		}

		private float roomSlowLoggedAt_ = -100f;

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

			CheckPlayerRules(row);
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
				report.Specs = MySpecs().Take(TrcSync.MaxRules).ToList();
				CarFacts facts = MyCarFacts();
				report.Regulations = regulations_.Take(TrcSync.MaxRules)
					.Select(r => new TrcSync.RuleClaim { Code = r.Code, Name = r.Name, Ok = r.Check(facts).Passes }).ToList();
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
	}
}
