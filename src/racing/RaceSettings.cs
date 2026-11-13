// A race's own types: its phase, grid order, a racer's state, room game modes and roles, the race's rules, a lobby entry and a result row.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace TRC.TuneLoader {
	public enum RacePhase : byte { Lobby, Qualifying, Gridding, Countdown, Running, Finished, Closed }

	/// <summary>How the grid is ordered: by who joined first, at random, by each driver's best time, by qualifying, or set by a tournament.</summary>
	public enum GridOrder : byte { Joined, Random, BestTime, Qualifying, Set }

	/// <summary>Where a driver stands in a race.</summary>
	public enum RacerState : byte { Racing, Finished, Out, Disqualified, Eliminated }

	/// <summary>What the lobby plays: a race, or one of the room games played anywhere on the map.</summary>
	public enum RoomMode : byte { Race, CopsAndRunners, Tag, Infected, KingOfTheHill, KeepUp, Convoy, CutUp }

	/// <summary>A driver's part in a room game.</summary>
	public enum RoomRole : byte { None, Cop, Runner, It, Infected, Leader }

	/// <summary>The race's rules, picked by the host.</summary>
	public sealed class RaceSettings {
		/// <summary>
		/// No real limit since 0.16 (bskftw: Jeefrect's custom servers take more than 16 players). Past
		/// <see cref="ManyDrivers"/>, beats go out less often. 0.15 copies show at most 16 of the list.
		/// </summary>
		public const int MaxDrivers = 100;
		public const int ManyDrivers = 16;

		public string Name = "";
		public int MaxPlayers = MaxDrivers;
		public int Laps = 1;
		public bool Open;
		public GridOrder Order;
		public bool PoleLeft = true;
		public GridLayout Layout;
		public bool LayoutFromCourse = true;
		public JumpRule Jump = JumpRule.Penalty;
		public double JumpPenaltySeconds = 5;

		/// <summary>Circuits: the last car each lap is out, until one is left.</summary>
		public bool Elimination;

		/// <summary>Qualifying: how long (minutes) and how many runs each (0 = as many as fit).</summary>
		public int QualifyingMinutes = 10;
		public int QualifyingRuns;

		/// <summary>A speed trap race: finishers are ranked on the total of their speeds at the course's traps, not their time.</summary>
		public bool SpeedTrapScoring;

		/// <summary>The room game, and its rules: how long a round lasts (minutes), how many cops, the hill's time to win, keep-up's gap.</summary>
		public RoomMode Mode;
		public int RoundMinutes = 10;
		public int Cops = 1;
		public int HillSeconds = 60;
		public int KeepUpMetres = 250;

		// 0.16, sent at the end of the lobby message (0.15 copies stop reading before it).

		/// <summary>Just for fun: no ratings change, whatever the rules (bskftw: TRC spec too).</summary>
		public bool Fun;

		/// <summary>Run to a regulation (its whole text travels with the lobby), or null. <see cref="Open"/> is set too, so 0.15 copies don't take it for TRC spec.</summary>
		public Regulation Regulation;

		/// <summary>The host drives in it; false: the host only runs it (console players' races, a race director).</summary>
		public bool HostDrives = true;

		/// <summary>The host's headlights show the start (3, 2, 1 flashes, off at GO), for console players.</summary>
		public bool LightStart;

		/// <summary>Tournaments: no time penalties from the host (jump starts follow <see cref="Jump"/>).</summary>
		public bool NoPenalties;

		/// <summary>
		/// The road at the start is too narrow for side by side (<see cref="Course.SingleFile"/>, measured by the host):
		/// everyone lines up in a line and each driver's clock starts at the start line.
		/// </summary>
		public bool SingleFile;

		/// <summary>The grid's shape: single file on a narrow road, else the host's pick (null: the course's own).</summary>
		public GridLayout? GridShape => SingleFile ? GridLayout.LineUp : LayoutFromCourse ? (GridLayout?)null : Layout;

		public bool IsTrcSpec => !Open && Regulation == null;

		public void Write(BinaryWriter w) {
			w.Write(RaceMessage.Clip(Name));
			w.Write((byte)MaxPlayers);
			w.Write((byte)Laps);
			w.Write(Open);
			w.Write((byte)Order);
			w.Write(PoleLeft);
			w.Write((byte)Layout);
			w.Write(LayoutFromCourse);
			w.Write((byte)Jump);
			w.Write((float)JumpPenaltySeconds);
			w.Write(Elimination);
			w.Write((byte)Mathf.Clamp(QualifyingMinutes, 1, 255));
			w.Write((byte)Mathf.Clamp(QualifyingRuns, 0, 255));
			w.Write(SpeedTrapScoring);
			w.Write((byte)Mode);
			w.Write((byte)Mathf.Clamp(RoundMinutes, 1, 255));
			w.Write((byte)Mathf.Clamp(Cops, 1, 15));
			w.Write((ushort)Mathf.Clamp(HillSeconds, 5, 3600));
			w.Write((ushort)Mathf.Clamp(KeepUpMetres, 20, 5000));
		}

		public static RaceSettings Read(BinaryReader r) => new RaceSettings {
			Name = RaceMessage.Clip(r.ReadString()),
			MaxPlayers = Mathf.Clamp(r.ReadByte(), 2, MaxDrivers),
			Laps = Math.Max(1, (int)r.ReadByte()),
			Open = r.ReadBoolean(),
			Order = (GridOrder)r.ReadByte(),
			PoleLeft = r.ReadBoolean(),
			Layout = (GridLayout)r.ReadByte(),
			LayoutFromCourse = r.ReadBoolean(),
			Jump = (JumpRule)r.ReadByte(),
			JumpPenaltySeconds = r.ReadSingle(),
			Elimination = r.ReadBoolean(),
			QualifyingMinutes = Math.Max(1, (int)r.ReadByte()),
			QualifyingRuns = r.ReadByte(),
			SpeedTrapScoring = r.ReadBoolean(),
			Mode = (RoomMode)r.ReadByte(),
			RoundMinutes = Math.Max(1, (int)r.ReadByte()),
			Cops = Math.Max(1, (int)r.ReadByte()),
			HillSeconds = r.ReadUInt16(),
			KeepUpMetres = r.ReadUInt16(),
		};

		public RaceSettings Copy() {
			var copy = (RaceSettings)MemberwiseClone();
			copy.Regulation = Regulation?.Copy();
			return copy;
		}
	}

	/// <summary>One driver in the lobby, as the host lists them.</summary>
	public sealed class LobbyEntry {
		public ulong Id;
		public string Name = "";

		/// <summary>In the race being run (heats and knockouts run only some of the lobby).</summary>
		public bool Racer = true;

		/// <summary>Place on the grid (0 = pole), or -1 before the grid is set.</summary>
		public int Slot = -1;

		/// <summary>Elimination races: the lap they were knocked out on (0 = still in).</summary>
		public int EliminatedOnLap;

		/// <summary>Room games: their part, their points (as the host keeps them), and whether they're out.</summary>
		public RoomRole Role;
		public int Points;
		public bool Out;

		/// <summary>0.16: a console player the host added and times from where CarX shows their car ("PS4", "Xbox"...).</summary>
		public bool Console;
		public string Platform = "";
	}

	/// <summary>A driver's place in a race's results.</summary>
	public sealed class RaceResultRow {
		public ulong Id;
		public string Name = "";
		public int Position;
		public RacerState State;

		/// <summary>Seconds from GO to the finish, with any penalty in it.</summary>
		public double Time;
		public double Penalty;
		public string Reason = "";
		public double BestLap;

		/// <summary>Speed trap races: the total of their speeds at the traps. Room games: their points.</summary>
		public float TrapTotal;
		public int Points;

		/// <summary>Incidents the driver's own mod saw in the race (resets, off the road, possible contact...).</summary>
		public int Incidents;
	}
}
