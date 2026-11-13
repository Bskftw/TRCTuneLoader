// A message about an open race between TRC Tune Loader users in a room: its kinds and fields. Its bytes never change between versions.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace TRC.TuneLoader {
	public enum RaceKind : byte {
		/// <summary>The host opens the race, someone joins: Join, Leave.</summary>
		Lobby = 1, Join = 2, Leave = 3,

		/// <summary>Host to all: the course (with its grid), for drivers who don't have it.</summary>
		Course = 4,

		/// <summary>Everyone to all, several times a second: car, ready, progress, result.</summary>
		Beat = 5,

		/// <summary>Host to all, with the beats: clock sync for each driver, GO, the end of the race.</summary>
		HostBeat = 6,

		/// <summary>Host to all: the final results of a race.</summary>
		Results = 7,

		/// <summary>Driver to host: who won a knockout battle.</summary>
		MatchReport = 8,
	}

	/// <summary>
	/// A message about an open race between TRC Tune Loader users in a room. Like battle messages, every message goes
	/// to the whole room, says who it's from, and is ignored by mods that aren't in that race.
	/// </summary>
	public sealed partial class RaceMessage {
		/// <summary>2 since 0.13.0: positions, incidents, speed traps and room games. 0.16 adds to the ends of messages only.</summary>
		public const byte Protocol = 2;

		/// <summary>A regulation's text in the lobby, at most.</summary>
		private const int MaxRegulationText = 12000;

		/// <summary>Speed traps sent with a race's course, at most.</summary>
		public const int MaxTraps = 64;
		private const int MaxText = 160;
		private const int MaxLines = 24;

		public RaceKind Kind;
		public uint RaceId;
		public ulong From;

		/// <summary>Which race of the lobby this is about (a lobby runs several: heats, tournament rounds, rematches).</summary>
		public int RaceNumber;

		// Lobby
		public string HostName = "";
		public RaceSettings Settings = new RaceSettings();
		public string CourseCode = "";
		public RacePhase Phase;
		public List<LobbyEntry> Entries = new List<LobbyEntry>();
		public List<ulong> Kicked = new List<ulong>();

		/// <summary>Short lines about the tournament (standings, the next race or match), shown to everyone.</summary>
		public List<string> Lines = new List<string>();

		/// <summary>Knockout: the battle being called now (0 = none), between whom, how, and which game of the match.</summary>
		public uint MatchId;
		public ulong MatchA;
		public ulong MatchB;
		public BattleFormat MatchFormat;
		public int MatchGame;
		public int MatchLaps = 1;

		// Join
		public string Name = "";
		public bool HasCourse;

		// Course
		public Course Course;

		// Beat
		public double SentAt;
		public bool Ready;
		public bool Spec;
		public string TuneSet = "";
		public string Car = "";
		public float Progress;
		public int LapsDone;
		public RacerState State;
		public double Time;
		public double Penalty;
		public string Reason = "";
		public double BestLap;

		/// <summary>Qualifying: best time so far (0 = none) and runs done. Best time on this course from before (0 = none), for the grid.</summary>
		public double BestQualifying;
		public int QualifyingRuns;
		public double PersonalBest;

		/// <summary>Where the car is on the map, the way it points, and its speed (km/h): for room games, flags and contact.</summary>
		public Vector3 Position;
		public Vector3 Heading;
		public float Speed;
		public float TrapTotal;
		public int Incidents;
		public string LastIncident = "";

		/// <summary>Room games: this driver's own points (cut-up), or seconds (king of the hill) as their mod counts them.</summary>
		public float Score;

		// Beat, 0.16 (at the end): the car meets the race's regulation; the driver's Elo for this race's ratings; a jump start in this countdown.
		public bool HasRuleOk;
		public bool RuleOk;
		public float Elo;
		public int JumpedCountdown;

		// Lobby and HostBeat, 0.16 (at the end): how many times the start was recalled, and who jumped.
		public int Recalls;
		public string RecallBy = "";

		// HostBeat (room games)
		/// <summary>King of the hill: where the hill is, and how far it reaches.</summary>
		public Vector3 Zone;
		public float ZoneRadius;
		// HostBeat
		public double GoHostTime;
		public int CountdownId;
		public double EndAtHostTime = -1;
		public double QualifyingEndsHostTime = -1;
		public List<(ulong Id, double Echo, double Hold)> Echoes = new List<(ulong, double, double)>();

		// Results
		public List<RaceResultRow> Rows = new List<RaceResultRow>();

		// MatchReport
		public ulong Winner;

		public static string Clip(string text) => MessageText.Clip(text, MaxText);
	}
}
