// A race message read back from bytes, or null when they aren't one (older copies' messages still read).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace TRC.TuneLoader {
	public sealed partial class RaceMessage {
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
									Role = (RoomRole)r.ReadByte(), Points = r.ReadInt32(), Out = r.ReadBoolean(),
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
							if (r.BaseStream.Position < r.BaseStream.Length) {
								m.Settings.Fun = r.ReadBoolean();
								m.Settings.HostDrives = r.ReadBoolean();
								m.Settings.LightStart = r.ReadBoolean();
								m.Settings.NoPenalties = r.ReadBoolean();
								string regulation = r.ReadString();
								m.Settings.Regulation = regulation.Length > 0 && regulation.Length <= MaxRegulationText ? Regulation.Parse(regulation) : null;
								int consoles = r.ReadByte();
								for (int i = 0; i < consoles; i++) {
									ulong id = r.ReadUInt64();
									string platform = Clip(r.ReadString());
									LobbyEntry e = m.Entries.FirstOrDefault(x => x.Id == id);
									if (e != null) {
										e.Console = true;
										e.Platform = platform;
									}
								}
								m.Recalls = r.ReadInt32();
								m.RecallBy = Clip(r.ReadString());
								if (r.BaseStream.Position < r.BaseStream.Length) {
									m.Settings.SingleFile = r.ReadBoolean();
								}
							}
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
							if (r.BaseStream.Position < r.BaseStream.Length) {
								int traps = r.ReadByte();
								for (int i = 0; i < traps && i < MaxTraps; i++) {
									var gate = new CourseGate {
										Center = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
										Forward = CourseGeometry.Level(new Vector3(r.ReadSingle(), 0f, r.ReadSingle())),
										Width = r.ReadSingle(),
									};
									if (gate.Forward != Vector3.zero && gate.Width > 0.5f && gate.Width < 100f) {
										course.Traps.Add(gate);
									}
								}
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
							m.Position = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
							m.Heading = new Vector3(r.ReadSingle(), 0f, r.ReadSingle());
							m.Speed = r.ReadSingle();
							m.TrapTotal = r.ReadSingle();
							m.Incidents = r.ReadUInt16();
							m.LastIncident = Clip(r.ReadString());
							m.Score = r.ReadSingle();
							if (r.BaseStream.Position < r.BaseStream.Length) {
								m.HasRuleOk = true;
								m.RuleOk = r.ReadBoolean();
								m.Elo = r.ReadSingle();
								m.JumpedCountdown = r.ReadInt32();
							}
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
							m.Zone = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
							m.ZoneRadius = r.ReadSingle();
							if (r.BaseStream.Position < r.BaseStream.Length) {
								m.Recalls = r.ReadInt32();
							}
							break;
						}
						case RaceKind.Results: {
							int count = r.ReadByte();
							for (int i = 0; i < count; i++) {
								m.Rows.Add(new RaceResultRow {
									Id = r.ReadUInt64(), Name = Clip(r.ReadString()), Position = r.ReadByte(), State = (RacerState)r.ReadByte(),
									Time = r.ReadDouble(), Penalty = r.ReadSingle(), Reason = Clip(r.ReadString()), BestLap = r.ReadDouble(),
									TrapTotal = r.ReadSingle(), Points = r.ReadInt32(), Incidents = r.ReadUInt16(),
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
	}
}
