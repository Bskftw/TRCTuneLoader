// One driver in a race lobby as this mod knows them, and a driver's place in the standings.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>One driver in a race lobby, as this mod knows them (from the host's lobby and their own beats).</summary>
	public sealed class RaceDriver {
		public ulong Id;
		public string Name = "";

		/// <summary>In the race being run (heats and knockouts run only some of the lobby).</summary>
		public bool Racer = true;
		public int Slot = -1;
		public int EliminatedOnLap;

		public bool Ready;
		public bool Spec;
		public string TuneSet = "";
		public string Car = "";
		public bool HasCourse;
		public float Progress;
		public int LapsDone;
		public RacerState State;
		public double Time;
		public double Penalty;
		public string Reason = "";
		public double BestLap;
		public double BestQualifying;
		public int QualifyingRuns;
		public double PersonalBest;

		/// <summary>The race number their beats are about, when they last said.</summary>
		public int RaceNumber = -1;
		public double LastHeard;
		public double SentAt;
		public double SentHeardAt = -1;

		/// <summary>
		/// When they measured <see cref="Progress"/> (their clock: for working out their speed), when it got here (this
		/// clock: for how far they've gone since), and how fast it was growing.
		/// </summary>
		public double ProgressAt = -1;
		public double ProgressHeardAt = -1;
		public double Speed;

		/// <summary>Where their car is on the map, the way it points and its speed (km/h), as their mod last said.</summary>
		public Vector3 Position;
		public Vector3 Heading;
		public float Kmh;
		public bool HasPosition;

		public float TrapTotal;
		public int Incidents;
		public string LastIncident = "";

		/// <summary>Host only: every incident they reported in this race, with when.</summary>
		public List<string> IncidentLog = new List<string>();

		/// <summary>Host only: a penalty the host gave after the race (seconds), or disqualified.</summary>
		public double HostPenalty;
		public bool HostDisqualified;

		/// <summary>Room games: their part, the points the host keeps for them, whether they're out, and their own score (cut-up).</summary>
		public RoomRole Role;
		public int Points;
		public bool Out;
		public float Score;

		/// <summary>Host only: how long a room game's rule has held for them (busted, too far from the leader).</summary>
		public double RuleSince = -1;

		/// <summary>0.16: a console player the host added; the host's mod times them from where CarX shows their car.</summary>
		public bool Console;
		public string Platform = "";

		/// <summary>0.16: their car meets the race's regulation (their mod said so; false until a 0.16 beat came), and their Elo for this race.</summary>
		public bool RuleOk;
		public bool SaidRuleOk;
		public float Elo;
	}

	/// <summary>Where a driver is in the race right now: their place, and metres along it.</summary>
	public sealed class RaceStanding {
		public RaceDriver Driver;
		public int Position;
		public float Progress;
	}
}
