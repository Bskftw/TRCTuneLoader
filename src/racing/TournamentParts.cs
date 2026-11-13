// What a tournament is made of: its kind, a round, its settings, a driver's standing, and a knockout match.
using System;
using System.Collections.Generic;
using System.Linq;

namespace TRC.TuneLoader {
	public enum TournamentKind : byte { Points, Knockout, Heats, Shootout }

	/// <summary>One race of a points series: where, how many laps, and whether the grid is the last race's results reversed.</summary>
	public sealed class TournamentRound {
		public string CourseCode = "";
		public string CourseName = "";
		public int Laps = 1;
		public bool ReverseGrid;
	}

	public sealed class TournamentSettings {
		/// <summary>Points for 1st to 16th (TRC, Oct 25: F1's spread, stretched to 16 places).</summary>
		public static readonly int[] DefaultPoints = { 25, 20, 16, 13, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1, 1 };

		public TournamentKind Kind;
		public string Name = "";

		// Points series
		public List<TournamentRound> Rounds = new List<TournamentRound>();
		public int[] Points = DefaultPoints;
		public bool FastestLapPoint = true;

		// Knockout
		public BattleFormat Format = BattleFormat.Race;
		public BattleFormat FinalFormat = BattleFormat.CatAndMouse;
		public int BestOf = 3;
		public int FinalBestOf = 3;
		public bool ThirdPlace = true;
		public int Laps = 1;

		// Heats and final
		public int HeatSize = 6;
		public int Advance = 3;
	}

	/// <summary>A driver's line in a points table.</summary>
	public sealed class TournamentStanding {
		public ulong Id;
		public string Name = "";
		public int Points;
		public int Wins;
		public int LastFinish = int.MaxValue;
		public List<int> Finishes = new List<int>();
	}

	/// <summary>One knockout battle: two drivers, first to win most of <see cref="BestOf"/> games goes on.</summary>
	public sealed class KnockoutMatch {
		public uint Id;
		public int Round;
		public int Index;
		public ulong A;
		public ulong B;
		public int WinsA;
		public int WinsB;
		public int Games;
		public ulong Winner;
		public int BestOf = 1;
		public BattleFormat Format;
		public bool Final;
		public bool ThirdPlace;

		public bool Ready => A != 0 && B != 0 && Winner == 0;
		public ulong Loser => Winner == 0 ? 0 : Winner == A ? B : A;
	}
}
