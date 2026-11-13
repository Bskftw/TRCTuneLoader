// Room games run by the host: cops and runners, tag, infected, king of the hill, keep-up.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	public sealed partial class RaceSession {
		/// <summary>Room games start this long after the host presses start, wherever everyone is.</summary>
		private const double GameStartSeconds = 5;

		/// <summary>Cops and runners: a runner slower than this within this distance of a cop for this long is busted.</summary>
		public const float BustMetres = 8f;
		public const float BustKmh = 25f;
		public const double BustSeconds = 3;
		private const int BustPoints = 100;

		/// <summary>Tag and infected: this close is a touch. After a tag, nobody can be tagged for a moment.</summary>
		public const float TouchMetres = 4.5f;
		private const double TagCooldownSeconds = 4;
		private const int SurvivorBonus = 1000;

		/// <summary>Keep-up: further than the gap from the leader for this long and you're out.</summary>
		public const double KeepUpSeconds = 10;

		private readonly Dictionary<ulong, double> gameSeconds_ = new Dictionary<ulong, double>();
		private double lastGameTick_ = -1;
		private double tagReadyAt_;

		private void StartGame(double now) {
			RaceNumber++;
			var random = new System.Random((int)(Id ^ (uint)(RaceNumber * 7919)));
			List<RaceDriver> players = Drivers.ToList();
			foreach (RaceDriver d in players) {
				d.Racer = true;
				d.Role = RoomRole.None;
				d.Points = 0;
				d.Out = false;
				d.RuleSince = -1;
				d.State = RacerState.Racing;
				d.Reason = "";
				d.Incidents = 0;
				d.IncidentLog.Clear();
				d.Score = 0;
			}
			gameSeconds_.Clear();
			List<RaceDriver> shuffled = players.OrderBy(_ => random.Next()).ToList();
			switch (Settings.Mode) {
				case RoomMode.CopsAndRunners: {
					int cops = Mathf.Clamp(Settings.Cops, 1, Math.Max(1, players.Count - 1));
					for (int i = 0; i < shuffled.Count; i++) {
						shuffled[i].Role = i < cops ? RoomRole.Cop : RoomRole.Runner;
					}
					break;
				}
				case RoomMode.Tag:
					shuffled[0].Role = RoomRole.It;
					break;
				case RoomMode.Infected:
					shuffled[0].Role = RoomRole.Infected;
					break;
				case RoomMode.KeepUp:
				case RoomMode.Convoy:
					players[0].Role = RoomRole.Leader;
					break;
				case RoomMode.KingOfTheHill:
					Zone = myPosition_;
					ZoneRadius = HillRadius;
					break;
			}
			myScore_ = 0;
			myIncidents_ = 0;
			countdownId_++;
			goHostTime_ = now + GameStartSeconds;
			endAtHost_ = Settings.Mode == RoomMode.Convoy ? -1 : goHostTime_ + Settings.RoundMinutes * 60.0;
			EndAt = endAtHost_;
			Results = null;
			ResultsFor = -1;
			Phase = RacePhase.Countdown;
			lastGameTick_ = -1;
			tagReadyAt_ = 0;
			lobbyDirty_ = true;
			lastHostBeat_ = double.MinValue;
		}

		private static float Gap(RaceDriver a, RaceDriver b) => Vector3.Distance(a.Position, b.Position);

		private void AddSeconds(RaceDriver d, double seconds) {
			gameSeconds_.TryGetValue(d.Id, out double had);
			gameSeconds_[d.Id] = had + seconds;
			d.Points = (int)(had + seconds);
		}

		/// <summary>Host, every frame of a room game: who's caught, tagged, holding the hill or dropped, the points, and the end.</summary>
		private void HostGame(double now) {
			double dt = lastGameTick_ < 0 ? 0 : Math.Min(0.5, now - lastGameTick_);
			lastGameTick_ = now;
			List<RaceDriver> live = Racers.Where(d => d.HasPosition && !d.Out && d.State != RacerState.Out).ToList();
			bool over = endAtHost_ >= 0 && now >= endAtHost_;
			switch (Settings.Mode) {
				case RoomMode.CopsAndRunners:
					foreach (RaceDriver runner in live.Where(d => d.Role == RoomRole.Runner).ToList()) {
						RaceDriver cop = live.Where(c => c.Role == RoomRole.Cop).OrderBy(c => Gap(c, runner)).FirstOrDefault();
						bool caught = cop != null && Gap(cop, runner) <= BustMetres && runner.Kmh < BustKmh;
						if (!caught) {
							runner.RuleSince = -1;
							AddSeconds(runner, dt);
							continue;
						}
						if (runner.RuleSince < 0) {
							runner.RuleSince = now;
						} else if (now - runner.RuleSince >= BustSeconds) {
							// Busted: they join the cops. The cop who caught them scores.
							runner.Role = RoomRole.Cop;
							runner.RuleSince = -1;
							runner.Reason = $"busted by {cop.Name}";
							AddSeconds(cop, BustPoints);
							lobbyDirty_ = true;
						}
					}
					over |= !live.Any(d => d.Role == RoomRole.Runner);
					break;

				case RoomMode.Tag: {
					RaceDriver it = live.FirstOrDefault(d => d.Role == RoomRole.It);
					foreach (RaceDriver d in live.Where(d => d.Role != RoomRole.It)) {
						AddSeconds(d, dt);
					}
					if (it != null && now >= tagReadyAt_) {
						RaceDriver touched = live.Where(d => d != it && Gap(d, it) <= TouchMetres).OrderBy(d => Gap(d, it)).FirstOrDefault();
						if (touched != null) {
							it.Role = RoomRole.None;
							touched.Role = RoomRole.It;
							tagReadyAt_ = now + TagCooldownSeconds;
							lobbyDirty_ = true;
						}
					} else if (it == null && live.Count > 0) {
						live[0].Role = RoomRole.It;
						lobbyDirty_ = true;
					}
					break;
				}

				case RoomMode.Infected: {
					List<RaceDriver> infected = live.Where(d => d.Role == RoomRole.Infected).ToList();
					foreach (RaceDriver d in live.Where(d => d.Role != RoomRole.Infected)) {
						if (infected.Any(i => Gap(i, d) <= TouchMetres)) {
							d.Role = RoomRole.Infected;
							lobbyDirty_ = true;
						} else {
							AddSeconds(d, dt);
						}
					}
					over |= live.Count(d => d.Role != RoomRole.Infected) <= (live.Count > 1 ? 1 : 0);
					if (over) {
						// The survivors win: a bonus puts them above everyone caught, however long those lasted.
						foreach (RaceDriver d in live.Where(d => d.Role != RoomRole.Infected)) {
							AddSeconds(d, SurvivorBonus);
						}
					}
					break;
				}

				case RoomMode.KingOfTheHill: {
					List<RaceDriver> inside = live.Where(d => Vector3.Distance(d.Position, Zone) <= ZoneRadius).ToList();
					if (inside.Count == 1) {
						AddSeconds(inside[0], dt);
						over |= gameSeconds_[inside[0].Id] >= Settings.HillSeconds;
					}
					break;
				}

				case RoomMode.KeepUp: {
					RaceDriver leader = live.FirstOrDefault(d => d.Role == RoomRole.Leader);
					if (leader == null) {
						over = true;
						break;
					}
					AddSeconds(leader, 0);
					foreach (RaceDriver d in live.Where(d => d != leader)) {
						if (Gap(d, leader) <= Settings.KeepUpMetres) {
							d.RuleSince = -1;
							AddSeconds(d, dt);
						} else if (d.RuleSince < 0) {
							d.RuleSince = now;
						} else if (now - d.RuleSince >= KeepUpSeconds) {
							d.Out = true;
							d.Reason = $"fell more than {Settings.KeepUpMetres} m behind {leader.Name}";
							lobbyDirty_ = true;
						}
					}
					over |= !live.Any(d => d != leader && !d.Out);
					break;
				}

				case RoomMode.CutUp:
					foreach (RaceDriver d in live) {
						d.Points = (int)d.Score;
					}
					break;
			}
			if (over) {
				FinishRace(now);
			}
		}

		private string GameOutReason(RaceDriver d) => d.Reason.Length > 0 ? d.Reason : "out";

		/// <summary>The room game's rule for this driver right now, in a few words.</summary>
		public string GameLine(double now) {
			RaceDriver me = MyDriver;
			if (me == null) {
				return "";
			}
			string points = $"{me.Points} pts";
			switch (Settings.Mode) {
				case RoomMode.CopsAndRunners:
					int runners = Racers.Count(d => d.Role == RoomRole.Runner);
					return me.Role == RoomRole.Cop ? $"You're a cop: catch the runners ({runners} left). Box them in and stop them for {BustSeconds:0} s"
						: $"You're a runner: don't get stopped near a cop. {points}";
				case RoomMode.Tag:
					RaceDriver it = Racers.FirstOrDefault(d => d.Role == RoomRole.It);
					return me.Role == RoomRole.It ? "You're it! Touch someone" : $"{it?.Name ?? "Someone"} is it: stay away. {points}";
				case RoomMode.Infected:
					int left = Racers.Count(d => d.Role != RoomRole.Infected);
					return me.Role == RoomRole.Infected ? $"You're infected: catch the others ({left} left)" : $"Don't get touched by the infected ({left} left). {points}";
				case RoomMode.KingOfTheHill: {
					float away = Vector3.Distance(myPosition_, Zone);
					return away <= ZoneRadius ? $"On the hill: {me.Points}/{Settings.HillSeconds} s (only counts when you're alone on it)"
						: $"The hill is {away:0} m away. {me.Points}/{Settings.HillSeconds} s";
				}
				case RoomMode.KeepUp: {
					RaceDriver leader = Racers.FirstOrDefault(d => d.Role == RoomRole.Leader);
					if (me.Role == RoomRole.Leader) {
						return $"You lead: lose them. {Racers.Count(d => d.Role != RoomRole.Leader && !d.Out)} still with you";
					}
					return me.Out ? $"Out: {me.Reason}" : $"Stay within {Settings.KeepUpMetres} m of {leader?.Name}: {(leader != null ? Gap(me, leader) : 0):0} m. {points}";
				}
				case RoomMode.Convoy: {
					int index = Racers.IndexOf(me);
					RaceDriver ahead = index > 0 ? Racers[index - 1] : null;
					return ahead == null ? "You lead the convoy" : $"Follow {ahead.Name}: {Gap(me, ahead):0} m";
				}
				case RoomMode.CutUp:
					return $"Cut-up: pass cars close and fast. {me.Points} pts";
			}
			return "";
		}

		private void UpdateMessage(double now) {
			RaceDriver me = MyDriver;
			if (!IsHost && !Joined) {
				return;
			}
			switch (Phase) {
				case RacePhase.Lobby:
					Message = Course == null ? "Getting the course from the host..." : IsHost ? $"{Drivers.Count} driver(s) in. Start when you're ready" : "Waiting for the host to start";
					break;
				case RacePhase.Qualifying: {
					string left = QualifyingEnds >= 0 ? $" ({Math.Max(0, QualifyingEnds - now) / 60.0:0.0} min left)" : "";
					string best = myBestQualifying_ > 0 ? $"your best {TimeAttackRun.FormatTime(myBestQualifying_)}" : "no time yet";
					Message = $"Qualifying{left}: {best}. {(Run?.Active == true ? Run.Message : "")}".Trim();
					break;
				}
				case RacePhase.Gridding:
					if (me == null || !me.Racer) {
						Message = "Watching this race";
					} else if (Run != null && Run.State == TimeAttackRun.Phase.ToStart && Run.ReadyToStart && myCar_ != null && !CarAllowed(myCar_)) {
						Message = Settings.Regulation != null ? $"Your car doesn't meet the race's regulation ({Settings.Regulation.Name}): see Regulations"
							: "Your car isn't TRC spec, and this is a TRC spec race";
					} else if (Run != null && !Run.ReadyToStart) {
						Message = Run.Message;
					} else {
						int waiting = Racers.Count(r => !r.Ready);
						string recalled = Recalls > 0 && RecallBy.Length > 0 ? $"Start recalled: {RecallBy} jumped. " : "";
						Message = recalled + (waiting > 0 ? $"Ready. Waiting for {waiting} driver(s)" : "Everyone's ready");
					}
					break;
				case RacePhase.Countdown:
					Message = IsGame ? $"Starts in {Math.Max(0, goHostTime_ - ClockOffset - now):0} s" : Run?.Message ?? "Get ready";
					break;
				case RacePhase.Running when IsGame:
					Message = GameLine(now);
					break;
				case RacePhase.Running:
					Message = me?.Racer != true ? "Watching" : me.State == RacerState.Racing ? (EndAt >= 0 ? $"The race ends in {Math.Max(0, EndAt - now):0} s" : "")
						: me.State == RacerState.Finished ? $"Finished: {TimeAttackRun.FormatTime(me.Time)}{(me.Penalty > 0 ? $" (with +{me.Penalty:0} s jump start)" : "")}. Waiting for the others"
						: $"Out: {me.Reason}";
					break;
				case RacePhase.Finished:
					RaceResultRow mine = Results?.FirstOrDefault(r => r.Id == Me);
					Message = mine == null ? "Race finished" : mine.State == RacerState.Finished ? $"You finished P{mine.Position}" : $"Race finished: {mine.Reason}";
					break;
			}
		}
	}
}
