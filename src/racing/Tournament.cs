// A tournament, kept by the host's mod: a points series, heats and a final, or a shootout, and its standings for everyone to see.
using System;
using System.Collections.Generic;
using System.Linq;

namespace TRC.TuneLoader {
	/// <summary>
	/// A tournament, kept by the host's mod: a points series over several races, a knockout bracket of 1v1 battles,
	/// heats and a final, or a time attack shootout. It says what's next and keeps the standings; the races
	/// themselves are run by the race lobby, and knockout battles by the two drivers' battle system.
	/// Nothing here knows about CarX or KSL.
	/// </summary>
	public sealed partial class Tournament {
		public TournamentSettings Settings { get; }
		public List<(ulong Id, string Name)> Seeds { get; }
		public bool Done { get; private set; }
		public string Winner { get; private set; } = "";

		// Points series
		public List<TournamentStanding> Table { get; } = new List<TournamentStanding>();
		public int RoundIndex { get; private set; }
		private List<RaceResultRow> lastRace_;

		// Knockout
		public List<KnockoutMatch> Matches { get; } = new List<KnockoutMatch>();
		public int KnockoutRounds { get; private set; }

		// Heats
		public List<List<ulong>> Heats { get; } = new List<List<ulong>>();
		public int HeatIndex { get; private set; }
		public List<RaceResultRow> FinalGrid { get; } = new List<RaceResultRow>();
		public bool InFinal { get; private set; }
		private readonly List<List<RaceResultRow>> heatResults_ = new List<List<RaceResultRow>>();

		// Shootout
		public List<(ulong Id, string Name, double Best)> ShootoutResults { get; private set; } = new List<(ulong, string, double)>();

		/// <param name="seeds">The drivers, best first (qualifying, best times, or the order they joined).</param>
		public Tournament(TournamentSettings settings, List<(ulong Id, string Name)> seeds) {
			Settings = settings;
			Seeds = seeds.Take(RaceSettings.MaxDrivers).ToList();
			switch (settings.Kind) {
				case TournamentKind.Points:
					foreach (var (id, name) in Seeds) {
						Table.Add(new TournamentStanding { Id = id, Name = name });
					}
					break;
				case TournamentKind.Knockout:
					BuildBracket();
					break;
				case TournamentKind.Heats:
					BuildHeats();
					break;
			}
		}

		private string NameOf(ulong id) => Seeds.FirstOrDefault(s => s.Id == id).Name ?? Table.FirstOrDefault(t => t.Id == id)?.Name ?? "?";

		// ---- Points series ----

		public TournamentRound CurrentRound => Settings.Kind == TournamentKind.Points && RoundIndex < Settings.Rounds.Count ? Settings.Rounds[RoundIndex] : null;

		/// <summary>The grid for the round about to run: the last race's results reversed on reverse-grid rounds, else null (the race's own order).</summary>
		public IList<ulong> NextGrid() {
			TournamentRound round = CurrentRound;
			if (round == null || !round.ReverseGrid || lastRace_ == null) {
				return null;
			}
			return lastRace_.OrderByDescending(r => r.Position).Select(r => r.Id).ToList();
		}

		/// <summary>Adds a finished race of the series: points by position (finishers only), +1 for the fastest lap, then the next round.</summary>
		public void AddRaceResult(List<RaceResultRow> rows) {
			if (Settings.Kind != TournamentKind.Points || Done) {
				return;
			}
			RaceResultRow fastest = Settings.FastestLapPoint
				? rows.Where(r => r.State == RacerState.Finished && r.BestLap > 0).OrderBy(r => r.BestLap).FirstOrDefault()
				: null;
			foreach (RaceResultRow row in rows) {
				TournamentStanding s = Table.FirstOrDefault(t => t.Id == row.Id);
				if (s == null) {
					Table.Add(s = new TournamentStanding { Id = row.Id, Name = row.Name });
				}
				bool finished = row.State == RacerState.Finished;
				if (finished && row.Position - 1 < Settings.Points.Length) {
					s.Points += Settings.Points[row.Position - 1];
				}
				if (row == fastest) {
					s.Points += 1;
				}
				if (finished && row.Position == 1) {
					s.Wins++;
				}
				s.LastFinish = row.Position;
				s.Finishes.Add(finished ? row.Position : 0);
			}
			lastRace_ = rows;
			RoundIndex++;
			if (RoundIndex >= Settings.Rounds.Count) {
				Done = true;
				Winner = Ranked().FirstOrDefault()?.Name ?? "";
			}
		}

		/// <summary>The points table: most points, then most wins, then the better finish in the last race.</summary>
		public List<TournamentStanding> Ranked() =>
			Table.OrderByDescending(t => t.Points).ThenByDescending(t => t.Wins).ThenBy(t => t.LastFinish).ToList();

		// ---- Heats and final ----

		/// <summary>Heats of up to <see cref="TournamentSettings.HeatSize"/>, seeds spread over them in a snake (1 2 3 3 2 1...), so each heat is even.</summary>
		private void BuildHeats() {
			int count = Math.Max(1, (int)Math.Ceiling(Seeds.Count / (double)Math.Max(2, Settings.HeatSize)));
			for (int i = 0; i < count; i++) {
				Heats.Add(new List<ulong>());
			}
			for (int i = 0; i < Seeds.Count; i++) {
				int lap = i % (2 * count);
				int heat = lap < count ? lap : 2 * count - 1 - lap;
				Heats[heat].Add(Seeds[i].Id);
			}
		}

		/// <summary>Who races next: the current heat's drivers, or the finalists. Null when done.</summary>
		public List<ulong> NextHeatRacers() {
			if (Settings.Kind != TournamentKind.Heats || Done) {
				return null;
			}
			return InFinal ? FinalGrid.Select(r => r.Id).ToList() : Heats[HeatIndex];
		}

		/// <summary>A heat (or the final) finished. The top <see cref="TournamentSettings.Advance"/> of each heat go to the final, gridded by heat place, then time.</summary>
		public void AddHeatResult(List<RaceResultRow> rows) {
			if (Settings.Kind != TournamentKind.Heats || Done) {
				return;
			}
			if (InFinal) {
				Done = true;
				Winner = rows.FirstOrDefault(r => r.Position == 1 && r.State == RacerState.Finished)?.Name ?? "";
				lastRace_ = rows;
				return;
			}
			heatResults_.Add(rows);
			HeatIndex++;
			if (HeatIndex >= Heats.Count) {
				InFinal = true;
				FinalGrid.AddRange(heatResults_
					.SelectMany(h => h.Where(r => r.State == RacerState.Finished && r.Position <= Settings.Advance))
					.OrderBy(r => r.Position).ThenBy(r => r.Time));
			}
		}

		// ---- Shootout ----

		/// <summary>The shootout's result: everyone's best time from the session, fastest first (no time: last).</summary>
		public void SetShootout(IEnumerable<(ulong Id, string Name, double Best)> bests) {
			ShootoutResults = bests.OrderBy(b => b.Best > 0 ? b.Best : double.MaxValue).ToList();
			Done = true;
			Winner = ShootoutResults.FirstOrDefault(b => b.Best > 0).Name ?? "";
		}

		// ---- For everyone to see ----

		/// <summary>Short lines for the lobby: what's on and the standings.</summary>
		public List<string> Lines() {
			var lines = new List<string>();
			string name = Settings.Name.Length > 0 ? Settings.Name : "Tournament";
			switch (Settings.Kind) {
				case TournamentKind.Points: {
					TournamentRound round = CurrentRound;
					lines.Add(Done ? $"{name}: finished, {Winner} wins" : $"{name}: round {RoundIndex + 1} of {Settings.Rounds.Count}, {round?.CourseName}{(round?.Laps > 1 ? $" ({round.Laps} laps)" : "")}{(round?.ReverseGrid == true ? ", reverse grid" : "")}");
					int place = 1;
					foreach (TournamentStanding s in Ranked()) {
						lines.Add($"{place++}. {s.Name}  {s.Points} pts{(s.Wins > 0 ? $" ({s.Wins} win{(s.Wins == 1 ? "" : "s")})" : "")}");
					}
					break;
				}
				case TournamentKind.Knockout: {
					KnockoutMatch next = NextMatch();
					lines.Add(Done ? $"{name}: finished, {Winner} wins" : next != null
						? $"{name}: {(next.ThirdPlace ? "3rd place" : RoundName(next.Round))}, {NameOf(next.A)} v {NameOf(next.B)} (best of {next.BestOf}, {(next.Format == BattleFormat.CatAndMouse ? "cat and mouse" : "side by side")})"
						: $"{name}: waiting for battles to finish");
					foreach (KnockoutMatch m in Matches.Where(m => m.A != 0 || m.B != 0).OrderBy(m => m.Round).ThenBy(m => m.ThirdPlace ? 1 : 0).ThenBy(m => m.Index)) {
						if (m.A == 0 || m.B == 0) {
							continue;
						}
						string label = m.ThirdPlace ? "3rd" : RoundName(m.Round);
						string score = m.Games > 0 || m.Winner != 0 ? $" {m.WinsA}-{m.WinsB}" : "";
						lines.Add($"{label}: {NameOf(m.A)} v {NameOf(m.B)}{score}{(m.Winner != 0 ? $", {NameOf(m.Winner)} goes on" : "")}");
					}
					break;
				}
				case TournamentKind.Heats:
					if (Done) {
						lines.Add($"{name}: finished, {Winner} wins");
					} else if (InFinal) {
						lines.Add($"{name}: the final, {FinalGrid.Count} drivers");
						lines.AddRange(FinalGrid.Select((r, i) => $"{i + 1}. {r.Name}"));
					} else {
						lines.Add($"{name}: heat {HeatIndex + 1} of {Heats.Count}, top {Settings.Advance} of each go to the final");
						for (int h = 0; h < Heats.Count; h++) {
							lines.Add($"Heat {h + 1}: {string.Join(", ", Heats[h].Select(NameOf))}");
						}
					}
					break;
				case TournamentKind.Shootout:
					lines.Add(Done ? $"{name}: finished, {Winner} wins" : $"{name}: time attack shootout, best time wins");
					int p = 1;
					foreach (var (_, n, best) in ShootoutResults) {
						lines.Add($"{p++}. {n}  {(best > 0 ? TimeAttackRun.FormatTime(best) : "no time")}");
					}
					break;
			}
			return lines;
		}
	}
}
