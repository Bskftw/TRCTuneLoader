// A race message turned into bytes (each kind's fields in a fixed order; new fields only ever go at the end).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace TRC.TuneLoader {
	public sealed partial class RaceMessage {
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
						w.Write(Clip(BattleMessage.PlainName(HostName)));
						Settings.Write(w);
						w.Write(Clip(CourseCode));
						w.Write((byte)Phase);
						w.Write((byte)Math.Min(Entries.Count, RaceSettings.MaxDrivers));
						for (int i = 0; i < Entries.Count && i < RaceSettings.MaxDrivers; i++) {
							LobbyEntry e = Entries[i];
							w.Write(e.Id);
							w.Write(Clip(BattleMessage.PlainName(e.Name)));
							w.Write(e.Racer);
							w.Write((sbyte)Mathf.Clamp(e.Slot, -1, 100));
							w.Write((byte)Mathf.Clamp(e.EliminatedOnLap, 0, 255));
							w.Write((byte)e.Role);
							w.Write(e.Points);
							w.Write(e.Out);
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
						// 0.16: read only when there.
						w.Write(Settings.Fun);
						w.Write(Settings.HostDrives);
						w.Write(Settings.LightStart);
						w.Write(Settings.NoPenalties);
						string regulation = Settings.Regulation?.ToText() ?? "";
						w.Write(regulation.Length <= MaxRegulationText ? regulation : "");
						var consoles = Entries.Take(RaceSettings.MaxDrivers).Where(e => e.Console).ToList();
						w.Write((byte)consoles.Count);
						foreach (LobbyEntry e in consoles) {
							w.Write(e.Id);
							w.Write(Clip(e.Platform));
						}
						w.Write(Recalls);
						w.Write(Clip(BattleMessage.PlainName(RecallBy)));
						w.Write(Settings.SingleFile);
						break;
					case RaceKind.Join:
						w.Write(Clip(BattleMessage.PlainName(Name)));
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
						// Since 0.15.0, at the end (older mods stop reading before it): the course's speed traps, so a
						// speed trap race scores the same traps for everyone.
						w.Write((byte)Math.Min(Course.Traps.Count, MaxTraps));
						for (int i = 0; i < Course.Traps.Count && i < MaxTraps; i++) {
							CourseGate t = Course.Traps[i];
							w.Write(t.Center.x);
							w.Write(t.Center.y);
							w.Write(t.Center.z);
							w.Write(t.Forward.x);
							w.Write(t.Forward.z);
							w.Write(t.Width);
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
						w.Write(Position.x);
						w.Write(Position.y);
						w.Write(Position.z);
						w.Write(Heading.x);
						w.Write(Heading.z);
						w.Write(Speed);
						w.Write(TrapTotal);
						w.Write((ushort)Mathf.Clamp(Incidents, 0, 65535));
						w.Write(Clip(LastIncident));
						w.Write(Score);
						// 0.16: read only when there.
						w.Write(RuleOk);
						w.Write(Elo);
						w.Write(JumpedCountdown);
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
						w.Write(Zone.x);
						w.Write(Zone.y);
						w.Write(Zone.z);
						w.Write(ZoneRadius);
						// 0.16: read only when there.
						w.Write(Recalls);
						break;
					case RaceKind.Results:
						w.Write((byte)Math.Min(Rows.Count, RaceSettings.MaxDrivers));
						for (int i = 0; i < Rows.Count && i < RaceSettings.MaxDrivers; i++) {
							RaceResultRow row = Rows[i];
							w.Write(row.Id);
							w.Write(Clip(BattleMessage.PlainName(row.Name)));
							w.Write((byte)row.Position);
							w.Write((byte)row.State);
							w.Write(row.Time);
							w.Write((float)row.Penalty);
							w.Write(Clip(row.Reason));
							w.Write(row.BestLap);
							w.Write(row.TrapTotal);
							w.Write(row.Points);
							w.Write((ushort)Mathf.Clamp(row.Incidents, 0, 65535));
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
	}
}
