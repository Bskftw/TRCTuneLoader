// The host every frame: the countdown, eliminations, finishing the race, results, standings and the grid order.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	public sealed partial class RaceSession {
		private void HostTick(double now) {
			RaceDriver me = MyDriver;
			if (me != null) {
				me.LastHeard = now;
				me.BestQualifying = myBestQualifying_;
				me.QualifyingRuns = myQualifyingRuns_;
				me.PersonalBest = personalBest_?.Invoke() ?? 0;
				me.Spec = myCar_?.Spec ?? me.Spec;
				me.TuneSet = myCar_?.TuneSet ?? me.TuneSet;
				me.RaceNumber = RaceNumber;
			}

			TickConsoles(now);

			// Drivers who stopped answering (a console player: no longer in the room).
			foreach (RaceDriver d in Drivers.Where(d => d.Id != Me && now - d.LastHeard > TimeoutSeconds).ToList()) {
				if ((Phase == RacePhase.Running || Phase == RacePhase.Countdown) && d.Racer) {
					if (d.State == RacerState.Racing) {
						d.State = RacerState.Out;
						d.Reason = "stopped answering";
						lobbyDirty_ = true;
					}
				} else {
					Drivers.Remove(d);
					lobbyDirty_ = true;
				}
			}

			switch (Phase) {
				case RacePhase.Qualifying:
					if (now >= qualifyingEndsHost_ && (Run == null || Run.State != TimeAttackRun.Phase.Running)) {
						Phase = RacePhase.Lobby;
						qualifyingEndsHost_ = -1;
						QualifyingEnds = -1;
						lobbyDirty_ = true;
					}
					break;

				case RacePhase.Gridding when endAtHost_ >= 0 && now >= endAtHost_:
				case RacePhase.Countdown when endAtHost_ >= 0 && now >= endAtHost_ && !IsGame:
					// Called off before GO: nobody races.
					foreach (RaceDriver d in Racers.Where(d => d.State == RacerState.Racing)) {
						d.State = RacerState.Out;
						d.Reason = "the race was called off";
					}
					FinishRace(now);
					break;

				case RacePhase.Gridding: {
					List<RaceDriver> racers = Racers;
					bool sameSet = !Settings.IsTrcSpec || racers.Select(r => r.TuneSet).Distinct().Count() == 1;
					bool rules = Settings.Regulation == null || racers.All(r => r.Id == Me || r.Console || r.RuleOk);
					if (racers.Count >= 1 && racers.All(r => r.Ready && r.RaceNumber == RaceNumber) && sameSet && rules) {
						if (bothReadySince_ < 0) {
							bothReadySince_ = now;
						} else if (now - bothReadySince_ >= ReadyHoldSeconds) {
							StartCountdown(now);
						}
					} else {
						bothReadySince_ = -1;
					}
					break;
				}

				case RacePhase.Countdown:
					if (now >= goHostTime_) {
						Phase = RacePhase.Running;
						lobbyDirty_ = true;
					}
					break;

				case RacePhase.Running when IsGame:
					HostGame(now);
					break;

				case RacePhase.Running: {
					if (Settings.Elimination && Course.IsCircuit) {
						Eliminate(now);
					}
					if (endAtHost_ >= 0 && now >= endAtHost_) {
						foreach (RaceDriver d in Racers.Where(d => d.State == RacerState.Racing)) {
							d.State = RacerState.Out;
							d.Reason = "the race was ended";
						}
					}
					bool allDone = Racers.All(d => d.State != RacerState.Racing);
					if (allDone) {
						FinishRace(now);
					}
					break;
				}
			}

			if (lobbyDirty_ || now - lastLobby_ >= LobbySeconds) {
				SendLobby(now);
			}
			bool trapsToSend = Settings.SpeedTrapScoring && Phase == RacePhase.Lobby && Drivers.Any(d => d.Id != Me);
			if (Course != null && ((!Course.Official && Drivers.Any(d => d.Id != Me && !d.HasCourse)) || trapsToSend) && now - lastCourseSent_ >= CourseResendSeconds) {
				lastCourseSent_ = now;
				var course = New(RaceKind.Course);
				course.Course = Course;
				outbox_.Add(course);
			}
			if (resultsSentAt_ >= 0 && resultsSends_ < 3 && now - resultsSentAt_ >= ResultsRepeatSeconds / 3 * resultsSends_) {
				resultsSends_++;
				SendResults();
			}
			HostBeat(now);
		}

		private void StartCountdown(double now) {
			countdownId_++;
			goHostTime_ = now + CountdownLeadSeconds + TimeAttackRun.CountdownSeconds;
			Phase = RacePhase.Countdown;
			bothReadySince_ = -1;
			lastHostBeat_ = double.MinValue;
			lobbyDirty_ = true;
		}

		/// <summary>Elimination: once all but one still in have done lap k, the one who hasn't is out (laps before the last only).</summary>
		private void Eliminate(double now) {
			while (true) {
				int lap = eliminations_ + 1;
				if (lap >= Settings.Laps) {
					return;
				}
				List<RaceDriver> still = Racers.Where(d => d.State == RacerState.Racing || d.State == RacerState.Finished).ToList();
				if (still.Count <= 1) {
					return;
				}
				List<RaceDriver> behind = still.Where(d => d.LapsDone < lap).ToList();
				if (behind.Count != 1) {
					// Someone dropped out, which counts as this lap's elimination; otherwise wait for the lap to finish.
					if (behind.Count == 0) {
						eliminations_++;
						continue;
					}
					return;
				}
				RaceDriver last = behind[0];
				last.State = RacerState.Eliminated;
				last.EliminatedOnLap = lap;
				last.Reason = $"eliminated on lap {lap}";
				eliminations_++;
				lobbyDirty_ = true;
			}
		}

		private void FinishRace(double now) {
			Results = ResultsFromDrivers();
			ResultsFor = RaceNumber;
			Phase = RacePhase.Finished;
			resultsSentAt_ = now;
			resultsSends_ = 0;
			lobbyDirty_ = true;
			Message = "Race finished";
		}

		/// <summary>The results as the drivers' own mods reported them: finishers by time, then those knocked out later, then the rest.</summary>
		public List<RaceResultRow> ResultsFromDrivers() {
			var rows = new List<RaceResultRow>();
			if (IsGame) {
				int place = 1;
				foreach (RaceDriver d in Racers.OrderByDescending(x => x.Points).ThenBy(x => x.Out ? 1 : 0)) {
					rows.Add(new RaceResultRow {
						Id = d.Id, Name = d.Name, Position = place++, State = d.Out ? RacerState.Out : RacerState.Finished, Points = d.Points,
						Reason = d.Out ? GameOutReason(d) : "", Incidents = d.Incidents,
					});
				}
				return rows;
			}
			RacerState StateOf(RaceDriver d) => d.HostDisqualified ? RacerState.Disqualified : d.State == RacerState.Racing ? RacerState.Out : d.State;
			bool traps = Settings.SpeedTrapScoring && Course != null && Course.Traps.Count > 0;
			IEnumerable<RaceDriver> order = Racers
				.OrderBy(d => StateOf(d) == RacerState.Finished ? 0 : StateOf(d) == RacerState.Eliminated ? 1 : StateOf(d) == RacerState.Disqualified ? 3 : 2)
				.ThenBy(d => StateOf(d) == RacerState.Finished ? (traps ? -d.TrapTotal : d.Time + d.HostPenalty) : 0)
				.ThenByDescending(d => d.State == RacerState.Eliminated ? d.EliminatedOnLap : 0)
				.ThenByDescending(d => d.Progress);
			int position = 1;
			foreach (RaceDriver d in order) {
				rows.Add(new RaceResultRow {
					Id = d.Id, Name = d.Name, Position = position++, State = StateOf(d),
					Time = d.Time + d.HostPenalty, Penalty = d.Penalty + d.HostPenalty,
					Reason = d.HostDisqualified ? "disqualified by the host" : d.Reason, BestLap = d.BestLap, TrapTotal = d.TrapTotal, Incidents = d.Incidents,
				});
			}
			return rows;
		}

		/// <summary>Everyone racing, in their place right now (finished drivers by time, then by how far along each is).</summary>
		public List<RaceStanding> Standings(double now) {
			var list = new List<RaceStanding>();
			foreach (RaceDriver d in Racers) {
				float progress = d.Progress;
				if (d.Id != Me && d.State == RacerState.Racing && d.ProgressHeardAt >= 0) {
					// Every driver has their own clock, so their progress is aged by when it arrived here.
					double age = Math.Max(0, Math.Min(1, now - d.ProgressHeardAt));
					progress += (float)(d.Speed * age);
				}
				list.Add(new RaceStanding { Driver = d, Progress = progress });
			}
			if (IsGame) {
				list = list.OrderByDescending(s => s.Driver.Points).ToList();
				for (int i = 0; i < list.Count; i++) {
					list[i].Position = i + 1;
				}
				return list;
			}
			list = list
				.OrderBy(s => s.Driver.State == RacerState.Finished ? 0 : s.Driver.State == RacerState.Racing ? 1 : s.Driver.State == RacerState.Eliminated ? 2 : 3)
				.ThenBy(s => s.Driver.State == RacerState.Finished ? s.Driver.Time : 0)
				.ThenByDescending(s => s.Driver.State == RacerState.Eliminated ? s.Driver.EliminatedOnLap : 0)
				.ThenByDescending(s => s.Progress)
				.ToList();
			for (int i = 0; i < list.Count; i++) {
				list[i].Position = i + 1;
			}
			return list;
		}

		/// <summary>The grid: the host's order when given, else by the race's grid order (with a stable tie-break by join order).</summary>
		private List<RaceDriver> GridOrderOf(List<RaceDriver> racers, IList<ulong> order) {
			if (order != null) {
				var given = order.Select(id => racers.FirstOrDefault(r => r.Id == id)).Where(r => r != null).ToList();
				given.AddRange(racers.Where(r => !given.Contains(r)));
				return given;
			}
			switch (Settings.Order) {
				case GridOrder.Random: {
					var random = new System.Random((int)(Id ^ (uint)RaceNumber));
					return racers.OrderBy(_ => random.Next()).ToList();
				}
				case GridOrder.BestTime:
					return racers.OrderBy(r => r.PersonalBest > 0 ? r.PersonalBest : double.MaxValue).ThenBy(r => racers.IndexOf(r)).ToList();
				case GridOrder.Qualifying:
					return racers.OrderBy(r => r.BestQualifying > 0 ? r.BestQualifying : double.MaxValue).ThenBy(r => racers.IndexOf(r)).ToList();
				default:
					return racers.ToList();
			}
		}
	}
}
