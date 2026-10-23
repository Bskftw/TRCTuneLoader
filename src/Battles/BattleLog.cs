using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace TRC.TuneLoader {
	/// <summary>
	/// Every battle that got as far as lining up, one line each in TimeAttack/battles.csv next to the mod.
	/// Lines are only added, so the file is the player's battle history (and, later, what ratings start from).
	/// </summary>
	public static class BattleLog {
		public const string FileName = "battles.csv";

		private static readonly string[] Columns = {
			"date", "battle_id", "course_code", "course", "map", "format", "mode", "counted", "opponent", "opponent_steam_id",
			"result", "runs", "my_times", "their_times", "my_car", "my_tune_set", "their_car", "their_tune_set", "loader_version",
		};

		/// <summary>"win", "loss", "draw", "opponent left" or "you left".</summary>
		public static string ResultOf(BattleSession battle) =>
			battle.Winner == battle.Me ? "win"
			: battle.Winner == battle.Peer ? "loss"
			: battle.PeerLeft ? "opponent left"
			: battle.ILeft ? "you left"
			: "draw";

		public static void Append(string folder, BattleSession battle, string myCar, string mySet, string version) {
			Directory.CreateDirectory(folder);
			string path = Path.Combine(folder, FileName);
			var text = new StringBuilder();
			if (!File.Exists(path)) {
				text.Append(string.Join(",", Columns)).Append('\n');
			}
			string[] fields = {
				DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
				battle.Id.ToString(CultureInfo.InvariantCulture),
				battle.Course.Code, battle.Course.Name, battle.Course.MapKey,
				battle.Format == BattleFormat.CatAndMouse ? "cat and mouse" : "race",
				battle.Open ? "open" : "TRC spec",
				battle.Counted && battle.Winner != 0 ? "yes" : "no",
				battle.PeerName, battle.Peer.ToString(CultureInfo.InvariantCulture),
				ResultOf(battle),
				battle.Runs.Count.ToString(CultureInfo.InvariantCulture),
				Times(battle.Runs.Select(r => r.Mine)),
				Times(battle.Runs.Select(r => r.Theirs)),
				myCar, mySet, battle.PeerCar.Car, battle.PeerCar.TuneSet,
				version,
			};
			text.Append(string.Join(",", fields.Select(Quote))).Append('\n');
			File.AppendAllText(path, text.ToString(), new UTF8Encoding(false));
		}

		/// <summary>Each run's time ("1:23.456"), or "DNF", separated by spaces.</summary>
		private static string Times(IEnumerable<RunResult> results) =>
			string.Join(" ", results.Select(r => r == null || !r.Finished ? "DNF" : TimeAttackRun.FormatTime(r.Time)));

		private static string Quote(string field) {
			field = (field ?? "").Replace("\r", " ").Replace("\n", " ");
			return field.IndexOfAny(new[] { ',', '"' }) >= 0 ? "\"" + field.Replace("\"", "\"\"") + "\"" : field;
		}
	}
}
