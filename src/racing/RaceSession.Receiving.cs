// A race's messages coming in: the lobby, a driver joining, drivers' beats and the host's beats.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	public sealed partial class RaceSession {
		public void Receive(RaceMessage m, double now) {
			if (m == null || m.RaceId != Id || m.From == Me || Ended) {
				return;
			}
			if (m.From == HostId) {
				lastHeardHost_ = now;
				HostGone = false;
			}
			switch (m.Kind) {
				case RaceKind.Lobby:
					if (!IsHost && m.From == HostId) {
						ApplyLobby(m, now);
					}
					break;

				case RaceKind.Join:
					if (IsHost) {
						OnJoin(m, now);
					}
					break;

				case RaceKind.Leave:
					if (IsHost) {
						RaceDriver gone = Drivers.FirstOrDefault(d => d.Id == m.From);
						if (gone != null) {
							if (Phase == RacePhase.Running && gone.Racer && gone.State == RacerState.Racing) {
								gone.State = RacerState.Out;
								gone.Reason = "left the race";
							} else {
								Drivers.Remove(gone);
							}
							lobbyDirty_ = true;
						}
					}
					break;

				case RaceKind.Course:
					if (!IsHost && Course == null && m.From == HostId && m.Course != null && m.Course.Code == CourseCode) {
						Course = m.Course;
					} else if (!IsHost && Course != null && Settings.SpeedTrapScoring && m.From == HostId && m.Course?.Code == CourseCode && m.Course.Traps.Count > 0) {
						// A speed trap race counts the host's traps, even on a course this driver has with other traps (or none).
						Course = Course.WithTraps(m.Course.Traps);
					}
					break;

				case RaceKind.Beat:
					OnBeat(m, now);
					break;

				case RaceKind.HostBeat:
					if (!IsHost && m.From == HostId) {
						OnHostBeat(m, now);
					}
					break;

				case RaceKind.Results:
					if (!IsHost && m.From == HostId && m.RaceNumber == RaceNumber) {
						Results = m.Rows;
						ResultsFor = m.RaceNumber;
						if (Phase == RacePhase.Running || Phase == RacePhase.Countdown || Phase == RacePhase.Gridding) {
							Phase = RacePhase.Finished;
						}
					}
					break;

				case RaceKind.MatchReport:
					if (IsHost) {
						Reports.Add(m);
					}
					break;
			}
		}

		private void ApplyLobby(RaceMessage m, double now) {
			HostName = BattleMessage.PlainName(m.HostName);
			Settings = m.Settings;
			if (m.CourseCode != CourseCode) {
				CourseCode = m.CourseCode;
				Course = null;
			}
			Lines = m.Lines;
			MatchId = m.MatchId;
			MatchA = m.MatchA;
			MatchB = m.MatchB;
			MatchFormat = m.MatchFormat;
			MatchGame = m.MatchGame;
			MatchLaps = m.MatchLaps;
			if (m.Kicked.Contains(Me)) {
				Ended = true;
				Run?.Cancel("Removed from the race by the host.");
				Message = "The host removed you from the race";
				return;
			}
			if (m.Phase == RacePhase.Closed) {
				Ended = true;
				Run?.Cancel("The host closed the race.");
				Message = "The host closed the race";
				return;
			}
			if (m.RaceNumber != RaceNumber) {
				RaceNumber = m.RaceNumber;
				Results = null;
			}
			if (!(Phase == RacePhase.Finished && Results != null && m.Phase == RacePhase.Running)) {
				Phase = m.Phase;
			}

			// The host's list decides who's in; what each driver said about themselves stays.
			var keep = Drivers.ToDictionary(d => d.Id);
			Drivers.Clear();
			foreach (LobbyEntry e in m.Entries) {
				if (!keep.TryGetValue(e.Id, out RaceDriver d)) {
					d = new RaceDriver { Id = e.Id, LastHeard = now };
				}
				d.Name = BattleMessage.PlainName(e.Name);
				d.Racer = e.Racer;
				d.Slot = e.Slot;
				d.EliminatedOnLap = e.EliminatedOnLap;
				d.Role = e.Role;
				d.Points = e.Points;
				d.Out = e.Out;
				d.Console = e.Console;
				d.Platform = e.Platform;
				Drivers.Add(d);
			}
			Recalls = Math.Max(Recalls, m.Recalls);
			if (m.RecallBy.Length > 0) {
				RecallBy = m.RecallBy;
			}
			if (!Joined && m.Entries.Count >= m.Settings.MaxPlayers) {
				Message = "The race is full";
			}
		}

		private void OnJoin(RaceMessage m, double now) {
			if (kicked_.Contains(m.From)) {
				return;
			}
			RaceDriver d = Drivers.FirstOrDefault(x => x.Id == m.From);
			if (d == null) {
				if (Drivers.Count >= Settings.MaxPlayers) {
					return;
				}
				bool racing = Phase == RacePhase.Gridding || Phase == RacePhase.Countdown || Phase == RacePhase.Running;
				d = new RaceDriver { Id = m.From, Racer = !racing, LastHeard = now };
				Drivers.Add(d);
			}
			d.Name = BattleMessage.PlainName(m.Name);
			d.HasCourse = m.HasCourse;
			d.LastHeard = now;
			lobbyDirty_ = true;
		}

		private void OnBeat(RaceMessage m, double now) {
			RaceDriver d = Drivers.FirstOrDefault(x => x.Id == m.From);
			if (d == null || (IsHost && d.Console)) {
				return;
			}
			d.LastHeard = now;
			d.SentAt = m.SentAt;
			d.SentHeardAt = now;
			d.Spec = m.Spec;
			d.TuneSet = m.TuneSet;
			d.Car = m.Car;
			d.HasCourse = m.HasCourse;
			d.Position = m.Position;
			d.Heading = m.Heading;
			d.Kmh = m.Speed;
			d.HasPosition = true;
			d.Score = m.Score;
			d.BestQualifying = m.BestQualifying;
			d.QualifyingRuns = m.QualifyingRuns;
			d.PersonalBest = m.PersonalBest;
			if (m.RaceNumber != RaceNumber) {
				return;
			}
			d.RaceNumber = m.RaceNumber;
			d.Ready = m.Ready;
			if (d.ProgressAt >= 0 && m.SentAt > d.ProgressAt && m.SentAt - d.ProgressAt < 1.5) {
				double speed = Math.Max(0, (m.Progress - d.Progress) / (m.SentAt - d.ProgressAt));
				d.Speed += (speed - d.Speed) * 0.5;
			}
			if (d.ProgressAt < 0 || m.SentAt > d.ProgressAt) {
				d.Progress = m.Progress;
				d.ProgressAt = m.SentAt;
				d.ProgressHeardAt = now;
			}
			d.LapsDone = m.LapsDone;
			d.TrapTotal = m.TrapTotal;
			if (m.HasRuleOk) {
				d.SaidRuleOk = true;
				d.RuleOk = m.RuleOk;
				d.Elo = m.Elo;
				if (IsHost && Settings.Jump == JumpRule.Recall && m.JumpedCountdown > 0 && m.JumpedCountdown == countdownId_) {
					Recall(now, d.Name);
				}
			}
			if (m.Incidents > d.Incidents && IsHost) {
				int at = (int)Math.Max(0, now - goHostTime_);
				d.IncidentLog.Add($"{at / 60}:{at % 60:00} {m.LastIncident}");
			}
			d.Incidents = Math.Max(d.Incidents, m.Incidents);
			d.LastIncident = m.LastIncident;
			// The host decides who's eliminated or removed; anything else comes from the driver's own mod.
			if (d.State != RacerState.Eliminated && !(IsHost && d.State == RacerState.Out && m.State == RacerState.Racing)) {
				d.State = m.State;
			}
			d.Time = m.Time;
			d.Penalty = m.Penalty;
			d.Reason = m.Reason;
			d.BestLap = m.BestLap;
		}

		private void OnHostBeat(RaceMessage m, double now) {
			foreach (var (id, echo, hold) in m.Echoes) {
				if (id != Me) {
					continue;
				}
				double roundTrip = now - echo - hold;
				if (roundTrip >= 0 && roundTrip < 5) {
					offsets_.Add(m.SentAt + roundTrip / 2 - now);
					if (offsets_.Count > OffsetSamples) {
						offsets_.RemoveAt(0);
					}
					ClockOffset = offsets_.OrderBy(o => o).ElementAt(offsets_.Count / 2);
					RoundTrip = roundTrip;
				}
			}
			if (m.RaceNumber != RaceNumber) {
				return;
			}
			Recalls = Math.Max(Recalls, m.Recalls);
			if (m.Phase == RacePhase.Countdown || m.Phase == RacePhase.Running) {
				goHostTime_ = m.GoHostTime;
				countdownId_ = m.CountdownId;
			}
			if (!(Phase == RacePhase.Finished && Results != null && m.Phase == RacePhase.Running)) {
				Phase = m.Phase;
			}
			endAtHost_ = m.EndAtHostTime;
			EndAt = endAtHost_ >= 0 ? endAtHost_ - ClockOffset : -1;
			Zone = m.Zone;
			ZoneRadius = m.ZoneRadius;
			qualifyingEndsHost_ = m.QualifyingEndsHostTime;
			QualifyingEnds = qualifyingEndsHost_ >= 0 ? qualifyingEndsHost_ - ClockOffset : -1;
		}
	}
}
