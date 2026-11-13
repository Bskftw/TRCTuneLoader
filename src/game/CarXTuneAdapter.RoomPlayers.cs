// Players in the room: their cars and setups as CarX shares them, checked here, their platforms and SteamIDs, and where their cars are.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using CarProfile;
using DB;
using DB.Meta;
using DI;
using GameOverlay;
using LocalCaching.Manager;
using PrefsModels;
using SyncMultiplayer;
using UnityEngine;
using ParameterType = Db.Meta.CarProfile.CarProfileParameterType;

namespace TRC.TuneLoader {
	public sealed partial class CarXTuneAdapter : ICarTuneAdapter {
		/// <summary>
		/// A room player's stance, Workshop and drivetrain checks, kept while their car, setup and parts stay the same (and
		/// redone every <see cref="RoomCheckSeconds"/> anyway). bskftw, Nov 8: pages showing room players lagged (1v1 battles
		/// 35 ms a frame): every 2 s each player's checks were all redone, inside the page's drawing.
		/// </summary>
		private sealed class RoomCheck {
			public string Signature = "";
			public float At;
			public List<string> Stance;
			public List<WorkshopItem> Workshop;
			public DrivetrainCheck Drivetrain;
		}

		private const float RoomCheckSeconds = 15f;
		private readonly Dictionary<string, RoomCheck> roomChecks_ = new Dictionary<string, RoomCheck>();

		/// <summary>The time the last <see cref="TryGetRoomPlayers"/> took, and how many players were checked again (for the log).</summary>
		public double LastRoomMs { get; private set; }
		public int LastRoomRechecked { get; private set; }

		private static string RoomSignature(SmartfoxPlayerProperties shared, bool builtHere, Dictionary<string, float> tune) {
			var sb = new System.Text.StringBuilder();
			sb.Append(shared.CarId).Append('|').Append(shared.ProfileId).Append('|').Append(shared.SwapId).Append('|').Append(builtHere ? 1 : 0).Append('|');
			HashSet<int> parts = shared.VisualSettings?.parts;
			if (parts != null) {
				foreach (int part in parts.OrderBy(x => x)) {
					sb.Append(part).Append(',');
				}
			}
			sb.Append('|');
			Dictionary<ulong, HashSet<int>> network = shared.VisualSettings?.networkparts;
			if (network != null) {
				foreach (var item in network.OrderBy(k => k.Key)) {
					sb.Append(item.Key).Append(':');
					if (item.Value != null) {
						foreach (int part in item.Value.OrderBy(x => x)) {
							sb.Append(part).Append(',');
						}
					}
					sb.Append(';');
				}
			}
			sb.Append('|');
			foreach (var pair in tune.OrderBy(k => k.Key, StringComparer.Ordinal)) {
				sb.Append(pair.Key).Append('=').Append(pair.Value.ToString("R", CultureInfo.InvariantCulture)).Append(';');
			}
			return sb.ToString();
		}

		public bool TryGetRoomKeys(List<(ulong SteamId, string Key)> members) {
			try {
				NetworkGame game = NetworkController.InstanceGame;
				if (!NetworkController.IsActive || game?.Players == null) {
					return false;
				}
				foreach (NetworkPlayer player in game.Players.ToArray()) {
					SmartfoxPlayerProperties shared = player?.PlayerProperties;
					if (shared != null) {
						members.Add((SteamIdOf(shared.PlayerID), shared.PlayerID.ToString()));
					}
				}
				return true;
			} catch (Exception e) {
				// A CarX update changed something: the room isn't known, so nothing is refused (RoomGate).
				TrcLog.Once("room keys", $"Couldn't read who's in the room ({e.Message}), so messages aren't checked against it; said once.");
				return false;
			}
		}

		public bool TryGetRoomPlayers(out List<RoomPlayer> players, out string error) {
			players = new List<RoomPlayer>();
			var watch = System.Diagnostics.Stopwatch.StartNew();
			int rechecked = 0;
			var seen = new HashSet<string>();
			try {
				NetworkGame game = NetworkController.InstanceGame;
				if (!NetworkController.IsActive || game?.Players == null) {
					error = "Join an online room to see the other players.";
					return false;
				}

				foreach (NetworkPlayer player in game.Players.ToArray()) {
					SmartfoxPlayerProperties shared = player?.PlayerProperties;
					if (shared == null) {
						continue;
					}

					var info = new RoomPlayer {
						Name = BattleMessage.PlainName(string.IsNullOrEmpty(player.FilteredNickName) ? shared.Name ?? "" : player.FilteredNickName),
						SteamId = SteamIdOf(shared.PlayerID),
						Key = shared.PlayerID.ToString(),
						Platform = PlatformName(shared.PlayerID.platform),
						IsConsole = IsConsole(shared.PlayerID.platform),
						IsLocal = player.IsLocal,
						CarId = shared.CarId,
						CarName = Db.GetById<PlayerCar>(shared.CarId)?.name ?? $"car {shared.CarId}",
						SlotId = shared.ProfileId,
						SlotName = Db.GetById<PlayerCarProfile>(shared.ProfileId)?.name ?? "",
						EngineId = shared.SwapId,
					};

					if (shared.Profile?.controllers != null) {
						foreach (var pair in shared.Profile.controllers) {
							info.SharedTune[pair.Key] = pair.Value;
						}
					}

					bool builtHere = player.userCar != null;
					string signature = RoomSignature(shared, builtHere, info.SharedTune);
					seen.Add(info.Key);
					if (roomChecks_.TryGetValue(info.Key, out RoomCheck kept) && kept.Signature == signature && Time.unscaledTime - kept.At < RoomCheckSeconds) {
						info.StanceDifferences = kept.Stance;
						info.Workshop = kept.Workshop;
						info.Drivetrain = kept.Drivetrain;
						players.Add(info);
						continue;
					}
					rechecked++;

					if (builtHere) {
						info.StanceDifferences = StanceDifferences(player.userCar, shared.ProfileId, shared.VisualSettings?.parts);
					}

					info.Workshop = WorkshopItems(shared.CarId, shared.ProfileId, shared.VisualSettings?.parts, shared.VisualSettings?.networkparts, out var workshopStance);
					foreach (WorkshopItem item in info.Workshop) {
						if (builtHere && item.ShownHere) {
							// CarX built their car here with this body, so the live stance is what counts.
							item.MovesWheels &= info.StanceDifferences.Count > 0;
						}
					}
					if (info.StanceDifferences.Count == 0 && info.Workshop.Exists(w => w.MovesWheels)) {
						// The live car here doesn't have their Workshop body, so use the stance worked out from its parts.
						info.StanceDifferences = workshopStance.Select(d => $"{d}, worked out from their Workshop body's parts").ToList();
					}

					if (builtHere) {
						info.Drivetrain = CheckDrivetrain(player.userCar, shared.ProfileId, info.SharedTune, withPhysics: false);
					}
					roomChecks_[info.Key] = new RoomCheck {
						Signature = signature, At = Time.unscaledTime, Stance = info.StanceDifferences, Workshop = info.Workshop, Drivetrain = info.Drivetrain,
					};

					players.Add(info);
				}
				foreach (string gone in roomChecks_.Keys.Where(k => !seen.Contains(k)).ToList()) {
					roomChecks_.Remove(gone);
				}

				error = "";
				return true;
			} catch (Exception e) {
				return Failed(e, out error);
			} finally {
				LastRoomMs = watch.Elapsed.TotalMilliseconds;
				LastRoomRechecked = rechecked;
			}
		}

		private static string PlatformName(UserPlatform.Id platform) {
			switch (platform) {
				case UserPlatform.Id.PS4: return "PlayStation";
				case UserPlatform.Id.Xbox: return "Xbox";
				case UserPlatform.Id.Switch: return "Switch";
				case UserPlatform.Id.EpicGames: return "Epic";
				case UserPlatform.Id.MyGames: return "MY.GAMES";
				case UserPlatform.Id.Steam: return "Steam";
				default: return platform.ToString();
			}
		}

		private static bool IsConsole(UserPlatform.Id platform) =>
			platform == UserPlatform.Id.PS4 || platform == UserPlatform.Id.Xbox || platform == UserPlatform.Id.Switch;

		private bool posesFailureLogged_;

		public bool TryGetRoomCarPoses(List<RoomCarPose> poses) {
			poses.Clear();
			try {
				NetworkGame game = NetworkController.InstanceGame;
				if (!NetworkController.IsActive || game?.Players == null) {
					return false;
				}
				foreach (NetworkPlayer player in game.Players) {
					SmartfoxPlayerProperties shared = player?.PlayerProperties;
					RaceCar car = player?.userCar;
					if (shared == null || car == null || player.IsLocal) {
						continue;
					}
					Transform body = car.carX != null ? car.carX.transform : car.transform;
					poses.Add(new RoomCarPose {
						Key = shared.PlayerID.ToString(),
						Name = BattleMessage.PlainName(string.IsNullOrEmpty(player.FilteredNickName) ? shared.Name ?? "" : player.FilteredNickName),
						Platform = PlatformName(shared.PlayerID.platform),
						IsConsole = IsConsole(shared.PlayerID.platform),
						Pose = new CarPose {
							Position = body.position, Forward = body.forward, Rotation = body.rotation, CarId = shared.CarId,
							SpeedKmh = car.carX != null ? Mathf.Abs(car.carX.speedKMH) : 0f,
						},
					});
				}
				return true;
			} catch (Exception e) {
				if (!posesFailureLogged_) {
					posesFailureLogged_ = true;
					TrcLog.Error(e);
				}
				return false;
			}
		}

		private static ulong SteamIdOf(PlayerId id) {
			if (id.platform != UserPlatform.Id.Steam) {
				return 0;
			}

			// PlayerId prints as "<account id>|<platform>"; for Steam the account ID is the SteamID64.
			string text = id.ToString();
			int bar = text.IndexOf('|');
			return ulong.TryParse(bar >= 0 ? text.Substring(0, bar) : text, NumberStyles.None, CultureInfo.InvariantCulture, out ulong steamId)
				? steamId
				: 0;
		}
	}
}
