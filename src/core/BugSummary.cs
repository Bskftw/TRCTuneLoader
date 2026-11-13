// The bug report's summary: the mod's version, options and what it was doing, as text.
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
			Safe("Room gate", () => RoomGate.Summary);
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
	}
}
