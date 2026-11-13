// A race's messages going out: joining, the lobby, results, and the beats.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	public sealed partial class RaceSession {
		private void SendJoin(double now) {
			lastJoin_ = now;
			RaceMessage join = New(RaceKind.Join);
			join.Name = myName_;
			join.HasCourse = Course != null;
			outbox_.Add(join);
		}

		private void SendLobby(double now) {
			lastLobby_ = now;
			lobbyDirty_ = false;
			RaceMessage lobby = New(RaceKind.Lobby);
			lobby.HostName = HostName;
			lobby.Settings = Settings;
			lobby.CourseCode = CourseCode;
			lobby.Phase = Phase;
			lobby.Kicked = kicked_.ToList();
			lobby.Lines = Lines;
			lobby.MatchId = MatchId;
			lobby.MatchA = MatchA;
			lobby.MatchB = MatchB;
			lobby.MatchFormat = MatchFormat;
			lobby.MatchGame = MatchGame;
			lobby.MatchLaps = MatchLaps;
			foreach (RaceDriver d in Drivers) {
				lobby.Entries.Add(new LobbyEntry {
					Id = d.Id, Name = d.Name, Racer = d.Racer, Slot = d.Slot, EliminatedOnLap = d.EliminatedOnLap, Role = d.Role, Points = d.Points, Out = d.Out,
					Console = d.Console, Platform = d.Platform,
				});
			}
			lobby.Recalls = Recalls;
			lobby.RecallBy = RecallBy;
			outbox_.Add(lobby);
		}

		private void SendResults() {
			RaceMessage results = New(RaceKind.Results);
			results.Rows = Results ?? new List<RaceResultRow>();
			outbox_.Add(results);
		}

		private double BeatSeconds(double now) {
			if (IsGame && (Phase == RacePhase.Running || Phase == RacePhase.Countdown)) {
				return Racers.Count <= SlowAbove ? FastSeconds : SlowSeconds;
			}
			switch (Phase) {
				case RacePhase.Lobby:
				case RacePhase.Finished:
					return PresenceSeconds;
				case RacePhase.Qualifying:
				case RacePhase.Gridding:
					return SlowSeconds;
				case RacePhase.Countdown:
					return FastSeconds;
			}
			bool many = Racers.Count > RaceSettings.ManyDrivers;
			if (Racers.Count <= SlowAbove || Run == null || Course == null) {
				return Racers.Count <= SlowAbove ? FastSeconds : many ? SlowestSeconds : SlowSeconds;
			}
			float distance = Course.Length * Math.Max(1, Run.Laps);
			bool nearFinish = Run.TotalProgress > distance - FastNearFinishMetres;
			bool justStarted = Run.State == TimeAttackRun.Phase.Running && Run.Elapsed < FastAfterGoSeconds;
			// Past 16 drivers (custom servers), less often, so a big room doesn't flood the channel.
			return nearFinish || justStarted ? (many ? SlowSeconds : FastSeconds) : many ? SlowestSeconds : SlowSeconds;
		}

		private void Beat(double now) {
			RacerState state = MyDriver?.State ?? RacerState.Racing;
			if (state != lastBeatState_) {
				// A result goes out at once.
				lastBeatState_ = state;
				lastBeat_ = double.MinValue;
			}
			if (now - lastBeat_ < BeatSeconds(now)) {
				return;
			}
			lastBeat_ = now;
			RaceMessage beat = New(RaceKind.Beat);
			RaceDriver me = MyDriver;
			beat.SentAt = now;
			beat.Ready = MyReady;
			beat.Spec = myCar_?.Spec ?? false;
			beat.TuneSet = myCar_?.TuneSet ?? "";
			beat.Car = myCar_?.Car ?? "";
			beat.HasCourse = Course != null;
			beat.Progress = me?.Racer == true && Run != null && !qualifyingRun_ ? Run.TotalProgress : 0f;
			beat.LapsDone = Run != null && !qualifyingRun_ ? Run.LapTimes.Count : 0;
			beat.State = me?.State ?? RacerState.Racing;
			beat.Time = me?.Time ?? 0;
			beat.Penalty = me?.Penalty ?? 0;
			beat.Reason = me?.Reason ?? "";
			beat.BestLap = me?.BestLap ?? 0;
			beat.BestQualifying = myBestQualifying_;
			beat.QualifyingRuns = myQualifyingRuns_;
			beat.PersonalBest = personalBest_?.Invoke() ?? 0;
			beat.Position = myPosition_;
			beat.Heading = myForward_;
			beat.Speed = myKmh_;
			beat.TrapTotal = Run != null && !qualifyingRun_ ? Run.TrapTotal : 0f;
			beat.Incidents = myIncidents_;
			beat.LastIncident = myLastIncident_;
			beat.Score = myScore_;
			beat.RuleOk = myCar_?.RuleOk ?? false;
			beat.Elo = MyElo;
			beat.JumpedCountdown = myJumpedCountdown_;
			outbox_.Add(beat);
			if (IsHost) {
				ConsoleBeats(now);
			}
		}

		private void HostBeat(double now) {
			if (now - lastHostBeat_ < BeatSeconds(now)) {
				return;
			}
			lastHostBeat_ = now;
			RaceMessage beat = New(RaceKind.HostBeat);
			beat.SentAt = now;
			beat.Phase = Phase;
			beat.GoHostTime = goHostTime_;
			beat.CountdownId = countdownId_;
			beat.EndAtHostTime = endAtHost_;
			beat.QualifyingEndsHostTime = qualifyingEndsHost_;
			foreach (RaceDriver d in Drivers.Where(d => d.Id != Me && d.SentHeardAt >= 0)) {
				beat.Echoes.Add((d.Id, d.SentAt, now - d.SentHeardAt));
			}
			beat.Zone = Zone;
			beat.ZoneRadius = ZoneRadius;
			beat.Recalls = Recalls;
			outbox_.Add(beat);
		}

		private RaceMessage New(RaceKind kind) => new RaceMessage { Kind = kind, RaceId = Id, From = Me, RaceNumber = RaceNumber };
	}
}
