// A battle message read back from bytes, or null when they aren't one.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace TRC.TuneLoader {
	public sealed partial class BattleMessage {
		/// <summary>The message in these bytes, or null when they aren't one (another protocol version, or damaged).</summary>
		public static BattleMessage Decode(byte[] data) {
			try {
				using (var reader = new BinaryReader(new MemoryStream(data), Encoding.UTF8)) {
					byte protocol = reader.ReadByte();
					if (protocol != Protocol && protocol != TrapProtocol && protocol != RuleProtocol) {
						return null;
					}
					var message = new BattleMessage {
						Kind = (BattleKind)reader.ReadByte(),
						BattleId = reader.ReadUInt32(),
						From = reader.ReadUInt64(),
						To = reader.ReadUInt64(),
					};
					switch (message.Kind) {
						case BattleKind.Challenge:
							message.FromName = Clip(reader.ReadString());
							message.Format = (BattleFormat)reader.ReadByte();
							message.Open = reader.ReadBoolean();
							message.Laps = Math.Max(1, (int)reader.ReadByte());
							message.MatchId = reader.ReadUInt32();
							message.MatchGame = reader.ReadByte();
							message.CourseCode = Clip(reader.ReadString());
							if (reader.ReadBoolean()) {
								message.Course = ReadCourse(reader);
							}
							if (message.Format == BattleFormat.SpeedTraps) {
								int traps = reader.ReadByte();
								for (int i = 0; i < traps; i++) {
									message.Traps.Add(ReadGate(reader));
								}
							}
							if (message.Format > BattleFormat.SpeedTraps) {
								return null;
							}
							if (reader.BaseStream.Position < reader.BaseStream.Length) {
								message.Fun = reader.ReadBoolean();
								string regulation = reader.ReadString();
								message.Regulation = regulation.Length > 0 && regulation.Length <= MaxRegulationText ? Regulation.Parse(regulation) : null;
								if (reader.BaseStream.Position < reader.BaseStream.Length) {
									message.SingleFile = reader.ReadBoolean();
								}
							}
							if (protocol == RuleProtocol && message.Regulation == null) {
								return null;
							}
							break;
						case BattleKind.Answer:
						case BattleKind.Leave:
						case BattleKind.Seen:
							message.Accept = reader.ReadBoolean();
							message.Reason = Clip(reader.ReadString());
							break;
						case BattleKind.Heartbeat:
							message.SentAt = reader.ReadDouble();
							message.Echo = reader.ReadDouble();
							message.EchoHold = reader.ReadDouble();
							message.Ready = reader.ReadBoolean();
							message.Spec = reader.ReadBoolean();
							message.TuneSet = Clip(reader.ReadString());
							message.TuneCode = Clip(reader.ReadString());
							message.Car = Clip(reader.ReadString());
							message.Progress = reader.ReadSingle();
							message.RunIndex = reader.ReadInt32();
							message.Phase = reader.ReadByte();
							message.Position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
							message.Heading = new Vector3(reader.ReadSingle(), 0f, reader.ReadSingle());
							message.Kmh = reader.ReadSingle();
							message.Sp = reader.ReadSingle();
							if (reader.ReadBoolean()) {
								message.Result = new RunResult {
									Run = reader.ReadInt32(),
									Finished = reader.ReadBoolean(),
									Time = reader.ReadDouble(),
									Reason = Clip(reader.ReadString()),
									SpecAtEnd = reader.ReadBoolean(),
								};
							}
							if (reader.BaseStream.Length - reader.BaseStream.Position >= 8) {
								message.TrapTotal = reader.ReadSingle();
								float resultTraps = reader.ReadSingle();
								if (message.Result != null) {
									message.Result.TrapTotal = resultTraps;
								}
							}
							if (reader.BaseStream.Length - reader.BaseStream.Position >= 5) {
								message.HasRuleOk = true;
								message.RuleOk = reader.ReadBoolean();
								message.Elo = reader.ReadSingle();
							}
							break;
						case BattleKind.Countdown:
							message.CountdownId = reader.ReadInt32();
							message.RunIndex = reader.ReadInt32();
							message.GoHostTime = reader.ReadDouble();
							break;
						case BattleKind.Abort:
							message.CountdownId = reader.ReadInt32();
							message.Reason = Clip(reader.ReadString());
							break;
						default:
							return null;
					}
					return message;
				}
			} catch (Exception) {
				return null;
			}
		}

		private const int MaxText = 160;
		private const int MaxPoints = 50000;
	}
}
