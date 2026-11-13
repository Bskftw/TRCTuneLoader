// A knockout tournament: the bracket, the next match, reporting a match, and moving winners on.
using System;
using System.Collections.Generic;
using System.Linq;

namespace TRC.TuneLoader {
	public sealed partial class Tournament {
		/// <summary>The bracket for the seeds: 1 v 16, 8 v 9... (the standard order), byes for the top seeds when the field isn't full.</summary>
		private void BuildBracket() {
			int size = 2;
			while (size < Seeds.Count) {
				size *= 2;
			}
			var order = new List<int> { 1, 2 };
			while (order.Count < size) {
				var next = new List<int>();
				foreach (int seed in order) {
					next.Add(seed);
					next.Add(order.Count * 2 + 1 - seed);
				}
				order = next;
			}
			KnockoutRounds = 0;
			for (int n = size; n > 1; n /= 2) {
				KnockoutRounds++;
			}
			uint id = 1;
			for (int round = 0; round < KnockoutRounds; round++) {
				int count = size >> (round + 1);
				for (int i = 0; i < count; i++) {
					bool final = round == KnockoutRounds - 1;
					Matches.Add(new KnockoutMatch {
						Id = id++, Round = round, Index = i, Final = final,
						Format = final ? Settings.FinalFormat : Settings.Format,
						BestOf = Math.Max(1, final ? Settings.FinalBestOf : Settings.BestOf),
					});
				}
			}
			if (Settings.ThirdPlace && KnockoutRounds >= 2) {
				Matches.Add(new KnockoutMatch {
					Id = id, Round = KnockoutRounds - 1, Index = 1, ThirdPlace = true,
					Format = Settings.FinalFormat, BestOf = Math.Max(1, Settings.FinalBestOf),
				});
			}
			for (int i = 0; i < size / 2; i++) {
				KnockoutMatch m = Match(0, i);
				m.A = SeedId(order[i * 2]);
				m.B = SeedId(order[i * 2 + 1]);
			}
			// Byes: a driver with nobody to race goes straight on.
			foreach (KnockoutMatch m in Matches.Where(m => m.Round == 0).ToList()) {
				if (m.A == 0 || m.B == 0) {
					m.Winner = m.A != 0 ? m.A : m.B;
					if (m.Winner != 0) {
						Advance(m);
					}
				}
			}
		}

		private ulong SeedId(int seed) => seed <= Seeds.Count ? Seeds[seed - 1].Id : 0;

		private KnockoutMatch Match(int round, int index) => Matches.First(m => m.Round == round && m.Index == index && !m.ThirdPlace);

		/// <summary>The next battle to call: the earliest round first, or null when there's none to run now.</summary>
		public KnockoutMatch NextMatch() =>
			Matches.Where(m => m.Ready).OrderBy(m => m.Round).ThenBy(m => m.ThirdPlace ? 1 : 0).ThenBy(m => m.Index).FirstOrDefault();

		/// <summary>A game of a match was won (0: no winner, it's run again). True when it counted.</summary>
		public bool Report(uint matchId, ulong winner) {
			KnockoutMatch m = Matches.FirstOrDefault(x => x.Id == matchId);
			if (m == null || !m.Ready || (winner != m.A && winner != m.B && winner != 0)) {
				return false;
			}
			if (winner == 0) {
				return true;
			}
			m.Games++;
			if (winner == m.A) {
				m.WinsA++;
			} else {
				m.WinsB++;
			}
			int needed = m.BestOf / 2 + 1;
			if (m.WinsA >= needed || m.WinsB >= needed) {
				m.Winner = m.WinsA >= needed ? m.A : m.B;
				Advance(m);
			}
			return true;
		}

		private void Advance(KnockoutMatch m) {
			if (m.ThirdPlace) {
				CheckKnockoutDone();
				return;
			}
			if (m.Final) {
				Winner = NameOf(m.Winner);
				CheckKnockoutDone();
				return;
			}
			KnockoutMatch next = Match(m.Round + 1, m.Index / 2);
			if (m.Index % 2 == 0) {
				next.A = m.Winner;
			} else {
				next.B = m.Winner;
			}
			// Semifinal losers race for third.
			KnockoutMatch third = Matches.FirstOrDefault(x => x.ThirdPlace);
			if (third != null && m.Round == KnockoutRounds - 2 && m.Loser != 0) {
				if (m.Index % 2 == 0) {
					third.A = m.Loser;
				} else {
					third.B = m.Loser;
				}
			}
		}

		private void CheckKnockoutDone() {
			KnockoutMatch final = Matches.FirstOrDefault(x => x.Final);
			KnockoutMatch third = Matches.FirstOrDefault(x => x.ThirdPlace);
			Done = final != null && final.Winner != 0 && (third == null || third.Winner != 0 || third.A == 0 || third.B == 0);
		}

		public string RoundName(int round) {
			int left = KnockoutRounds - round;
			return left == 1 ? "Final" : left == 2 ? "Semifinals" : left == 3 ? "Quarterfinals" : $"Round of {1 << left}";
		}
	}
}
