// A battle message turned into bytes (new fields only ever go at the end).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace TRC.TuneLoader {
	public sealed partial class BattleMessage {
		public byte[] Encode() {
			using (var memory = new MemoryStream())
			using (var writer = new BinaryWriter(memory, Encoding.UTF8)) {
				writer.Write(Kind == BattleKind.Challenge && Regulation != null ? RuleProtocol : Kind == BattleKind.Challenge && Format == BattleFormat.SpeedTraps ? TrapProtocol : Protocol);
				writer.Write((byte)Kind);
				writer.Write(BattleId);
				writer.Write(From);
				writer.Write(To);
				switch (Kind) {
					case BattleKind.Challenge:
						writer.Write(Clip(PlainName(FromName)));
						writer.Write((byte)Format);
						writer.Write(Open);
						writer.Write((byte)Math.Max(1, Math.Min(255, Laps)));
						writer.Write(MatchId);
						writer.Write((byte)Math.Max(0, Math.Min(255, MatchGame)));
						writer.Write(Clip(CourseCode));
						writer.Write(Course != null);
						if (Course != null) {
							WriteCourse(writer, Course);
						}
						if (Format == BattleFormat.SpeedTraps) {
							int traps = Math.Min(MaxTraps, Traps.Count);
							writer.Write((byte)traps);
							for (int i = 0; i < traps; i++) {
								WriteGate(writer, Traps[i]);
							}
						}
						// 0.16: read only when there.
						writer.Write(Fun);
						string regulation = Regulation?.ToText() ?? "";
						writer.Write(regulation.Length <= MaxRegulationText ? regulation : "");
						writer.Write(SingleFile);
						break;
					case BattleKind.Answer:
					case BattleKind.Leave:
					case BattleKind.Seen:
						writer.Write(Accept);
						writer.Write(Clip(Reason));
						break;
					case BattleKind.Heartbeat:
						writer.Write(SentAt);
						writer.Write(Echo);
						writer.Write(EchoHold);
						writer.Write(Ready);
						writer.Write(Spec);
						writer.Write(Clip(TuneSet));
						writer.Write(Clip(TuneCode));
						writer.Write(Clip(Car));
						writer.Write(Progress);
						writer.Write(RunIndex);
						writer.Write(Phase);
						writer.Write(Position.x);
						writer.Write(Position.y);
						writer.Write(Position.z);
						writer.Write(Heading.x);
						writer.Write(Heading.z);
						writer.Write(Kmh);
						writer.Write(Sp);
						writer.Write(Result != null);
						if (Result != null) {
							writer.Write(Result.Run);
							writer.Write(Result.Finished);
							writer.Write(Result.Time);
							writer.Write(Clip(Result.Reason));
							writer.Write(Result.SpecAtEnd);
						}
						// 0.15.1: read only when there.
						writer.Write(TrapTotal);
						writer.Write(Result?.TrapTotal ?? 0f);
						// 0.16: read only when there.
						writer.Write(RuleOk);
						writer.Write(Elo);
						break;
					case BattleKind.Countdown:
						writer.Write(CountdownId);
						writer.Write(RunIndex);
						writer.Write(GoHostTime);
						break;
					case BattleKind.Abort:
						writer.Write(CountdownId);
						writer.Write(Clip(Reason));
						break;
				}
				writer.Flush();
				return memory.ToArray();
			}
		}
	}
}
