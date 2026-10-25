using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace TRC.TuneLoader {
	public enum RacePhase : byte { Lobby, Qualifying, Gridding, Countdown, Running, Finished, Closed }

	/// <summary>How the grid is ordered: by who joined first, at random, by each driver's best time, by qualifying, or set by a tournament.</summary>
	public enum GridOrder : byte { Joined, Random, BestTime, Qualifying, Set }

	/// <summary>Where a driver stands in a race.</summary>
	public enum RacerState : byte { Racing, Finished, Out, Disqualified, Eliminated }

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

	/// <summary>The race's rules, picked by the host.</summary>
	public sealed class RaceSettings {
		public const int MaxDrivers = 16;

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
		};

		public RaceSettings Copy() => (RaceSettings)MemberwiseClone();
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
	}

	/// <summary>
	/// A message about an open race between TRC Tune Loader users in a room. Like battle messages, every message goes
	/// to the whole room, says who it's from, and is ignored by mods that aren't in that race.
	/// </summary>
	public sealed class RaceMessage {
		public const byte Protocol = 1;
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

		public byte[] Encode() {
			using (var memory = new MemoryStream())
			using (var w = new BinaryWriter(memory, Encoding.UTF8)) {
				w.Write(Protocol);
				w.Write((byte)Kind);
				w.Write(RaceId);
				w.Write(From);
				w.Write((ushort)RaceNumber);
				switch (Kind) {
					case RaceKind.Lobby:
						w.Write(Clip(HostName));
						Settings.Write(w);
						w.Write(Clip(CourseCode));
						w.Write((byte)Phase);
						w.Write((byte)Math.Min(Entries.Count, RaceSettings.MaxDrivers));
						for (int i = 0; i < Entries.Count && i < RaceSettings.MaxDrivers; i++) {
							LobbyEntry e = Entries[i];
							w.Write(e.Id);
							w.Write(Clip(e.Name));
							w.Write(e.Racer);
							w.Write((sbyte)Mathf.Clamp(e.Slot, -1, 100));
							w.Write((byte)Mathf.Clamp(e.EliminatedOnLap, 0, 255));
						}
						w.Write((byte)Math.Min(Kicked.Count, 32));
						for (int i = 0; i < Kicked.Count && i < 32; i++) {
							w.Write(Kicked[i]);
						}
						w.Write((byte)Math.Min(Lines.Count, MaxLines));
						for (int i = 0; i < Lines.Count && i < MaxLines; i++) {
							w.Write(Clip(Lines[i]));
						}
						w.Write(MatchId);
						w.Write(MatchA);
						w.Write(MatchB);
						w.Write((byte)MatchFormat);
						w.Write((byte)MatchGame);
						w.Write((byte)Math.Max(1, Math.Min(255, MatchLaps)));
						break;
					case RaceKind.Join:
						w.Write(Clip(Name));
						w.Write(HasCourse);
						break;
					case RaceKind.Leave:
						w.Write(Clip(Reason));
						break;
					case RaceKind.Course:
						BattleMessage.WriteCourse(w, Course);
						w.Write((byte)Course.GridLayout);
						w.Write(Course.GridPath.Count >= 2);
						if (Course.GridPath.Count >= 2) {
							w.Write(Course.GridPath.Count);
							BattleMessage.WritePath(w, Course.GridPath);
						}
						w.Write((byte)Math.Min(Course.GridObjects.Count, RaceSettings.MaxDrivers));
						for (int i = 0; i < Course.GridObjects.Count && i < RaceSettings.MaxDrivers; i++) {
							GridSpot s = Course.GridObjects[i];
							w.Write(s.Position.x);
							w.Write(s.Position.y);
							w.Write(s.Position.z);
							w.Write(s.Forward.x);
							w.Write(s.Forward.z);
						}
						break;
					case RaceKind.Beat:
						w.Write(SentAt);
						w.Write(Ready);
						w.Write(Spec);
						w.Write(Clip(TuneSet));
						w.Write(Clip(Car));
						w.Write(HasCourse);
						w.Write(Progress);
						w.Write((byte)Mathf.Clamp(LapsDone, 0, 255));
						w.Write((byte)State);
						w.Write(Time);
						w.Write((float)Penalty);
						w.Write(Clip(Reason));
						w.Write(BestLap);
						w.Write(BestQualifying);
						w.Write((byte)Mathf.Clamp(QualifyingRuns, 0, 255));
						w.Write(PersonalBest);
						break;
					case RaceKind.HostBeat:
						w.Write(SentAt);
						w.Write((byte)Phase);
						w.Write(GoHostTime);
						w.Write(CountdownId);
						w.Write(EndAtHostTime);
						w.Write(QualifyingEndsHostTime);
						w.Write((byte)Math.Min(Echoes.Count, RaceSettings.MaxDrivers));
						for (int i = 0; i < Echoes.Count && i < RaceSettings.MaxDrivers; i++) {
							w.Write(Echoes[i].Id);
							w.Write(Echoes[i].Echo);
							w.Write((float)Echoes[i].Hold);
						}
						break;
					case RaceKind.Results:
						w.Write((byte)Math.Min(Rows.Count, RaceSettings.MaxDrivers));
						for (int i = 0; i < Rows.Count && i < RaceSettings.MaxDrivers; i++) {
							RaceResultRow row = Rows[i];
							w.Write(row.Id);
							w.Write(Clip(row.Name));
							w.Write((byte)row.Position);
							w.Write((byte)row.State);
							w.Write(row.Time);
							w.Write((float)row.Penalty);
							w.Write(Clip(row.Reason));
							w.Write(row.BestLap);
						}
						break;
					case RaceKind.MatchReport:
						w.Write(MatchId);
						w.Write((byte)MatchGame);
						w.Write(Winner);
						break;
				}
				w.Flush();
				return memory.ToArray();
			}
		}

		/// <summary>The message in these bytes, or null when they aren't one.</summary>
		public static RaceMessage Decode(byte[] data) {
			try {
				using (var r = new BinaryReader(new MemoryStream(data), Encoding.UTF8)) {
					if (r.ReadByte() != Protocol) {
						return null;
					}
					var m = new RaceMessage {
						Kind = (RaceKind)r.ReadByte(),
						RaceId = r.ReadUInt32(),
						From = r.ReadUInt64(),
						RaceNumber = r.ReadUInt16(),
					};
					switch (m.Kind) {
						case RaceKind.Lobby: {
							m.HostName = Clip(r.ReadString());
							m.Settings = RaceSettings.Read(r);
							m.CourseCode = Clip(r.ReadString());
							m.Phase = (RacePhase)r.ReadByte();
							int count = r.ReadByte();
							for (int i = 0; i < count; i++) {
								m.Entries.Add(new LobbyEntry {
									Id = r.ReadUInt64(), Name = Clip(r.ReadString()), Racer = r.ReadBoolean(), Slot = r.ReadSByte(), EliminatedOnLap = r.ReadByte(),
								});
							}
							int kicked = r.ReadByte();
							for (int i = 0; i < kicked; i++) {
								m.Kicked.Add(r.ReadUInt64());
							}
							int lines = r.ReadByte();
							for (int i = 0; i < lines; i++) {
								m.Lines.Add(Clip(r.ReadString()));
							}
							m.MatchId = r.ReadUInt32();
							m.MatchA = r.ReadUInt64();
							m.MatchB = r.ReadUInt64();
							m.MatchFormat = (BattleFormat)r.ReadByte();
							m.MatchGame = r.ReadByte();
							m.MatchLaps = Math.Max(1, (int)r.ReadByte());
							break;
						}
						case RaceKind.Join:
							m.Name = Clip(r.ReadString());
							m.HasCourse = r.ReadBoolean();
							break;
						case RaceKind.Leave:
							m.Reason = Clip(r.ReadString());
							break;
						case RaceKind.Course: {
							Course course = BattleMessage.ReadCourse(r);
							course.GridLayout = (GridLayout)r.ReadByte();
							if (r.ReadBoolean()) {
								int count = r.ReadInt32();
								if (count < 2 || count > 5000) {
									return null;
								}
								course.GridPath = BattleMessage.ReadPath(r, count);
							}
							int spots = r.ReadByte();
							for (int i = 0; i < spots; i++) {
								course.GridObjects.Add(new GridSpot {
									Position = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
									Forward = new Vector3(r.ReadSingle(), 0f, r.ReadSingle()),
								});
							}
							m.Course = course;
							break;
						}
						case RaceKind.Beat:
							m.SentAt = r.ReadDouble();
							m.Ready = r.ReadBoolean();
							m.Spec = r.ReadBoolean();
							m.TuneSet = Clip(r.ReadString());
							m.Car = Clip(r.ReadString());
							m.HasCourse = r.ReadBoolean();
							m.Progress = r.ReadSingle();
							m.LapsDone = r.ReadByte();
							m.State = (RacerState)r.ReadByte();
							m.Time = r.ReadDouble();
							m.Penalty = r.ReadSingle();
							m.Reason = Clip(r.ReadString());
							m.BestLap = r.ReadDouble();
							m.BestQualifying = r.ReadDouble();
							m.QualifyingRuns = r.ReadByte();
							m.PersonalBest = r.ReadDouble();
							break;
						case RaceKind.HostBeat: {
							m.SentAt = r.ReadDouble();
							m.Phase = (RacePhase)r.ReadByte();
							m.GoHostTime = r.ReadDouble();
							m.CountdownId = r.ReadInt32();
							m.EndAtHostTime = r.ReadDouble();
							m.QualifyingEndsHostTime = r.ReadDouble();
							int count = r.ReadByte();
							for (int i = 0; i < count; i++) {
								m.Echoes.Add((r.ReadUInt64(), r.ReadDouble(), r.ReadSingle()));
							}
							break;
						}
						case RaceKind.Results: {
							int count = r.ReadByte();
							for (int i = 0; i < count; i++) {
								m.Rows.Add(new RaceResultRow {
									Id = r.ReadUInt64(), Name = Clip(r.ReadString()), Position = r.ReadByte(), State = (RacerState)r.ReadByte(),
									Time = r.ReadDouble(), Penalty = r.ReadSingle(), Reason = Clip(r.ReadString()), BestLap = r.ReadDouble(),
								});
							}
							break;
						}
						case RaceKind.MatchReport:
							m.MatchId = r.ReadUInt32();
							m.MatchGame = r.ReadByte();
							m.Winner = r.ReadUInt64();
							break;
						default:
							return null;
					}
					return m;
				}
			} catch (Exception) {
				return null;
			}
		}

		public static string Clip(string text) {
			text = text ?? "";
			return text.Length <= MaxText ? text : text.Substring(0, MaxText);
		}
	}
}
