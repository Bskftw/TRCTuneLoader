// What the race host does: the course and settings, qualifying, starting and ending the race, kicking, closing or leaving.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	public sealed partial class RaceSession {
		/// <summary>Host: a different course for the next race (a tournament round on another road). Only between races.</summary>
		public void SetCourse(Course course) {
			if (!IsHost || Phase == RacePhase.Gridding || Phase == RacePhase.Countdown || Phase == RacePhase.Running || Phase == RacePhase.Qualifying) {
				return;
			}
			Course = course;
			CourseCode = course.Code;
			foreach (RaceDriver d in Drivers.Where(d => d.Id != Me)) {
				d.HasCourse = false;
			}
			lobbyDirty_ = true;
			lastCourseSent_ = double.MinValue;
		}

		/// <summary>Host: changes the rules between races (laps, grid, jump starts...).</summary>
		public void SetSettings(RaceSettings settings) {
			if (!IsHost) {
				return;
			}
			Settings = settings.Copy();
			lobbyDirty_ = true;
		}

		/// <summary>Host: tournament lines everyone sees.</summary>
		public void SetLines(List<string> lines) {
			if (!IsHost) {
				return;
			}
			Lines = lines ?? new List<string>();
			lobbyDirty_ = true;
		}

		/// <summary>Host: calls a knockout battle between two drivers (0 to clear).</summary>
		public void CallMatch(uint matchId, ulong a, ulong b, BattleFormat format, int game, int laps) {
			if (!IsHost) {
				return;
			}
			MatchId = matchId;
			MatchA = a;
			MatchB = b;
			MatchFormat = format;
			MatchGame = game;
			MatchLaps = Math.Max(1, laps);
			lobbyDirty_ = true;
		}

		/// <summary>Host: everyone drives qualifying runs on their own for <see cref="RaceSettings.QualifyingMinutes"/>.</summary>
		public void StartQualifying(double now) {
			if (!IsHost || Phase != RacePhase.Lobby && Phase != RacePhase.Finished) {
				return;
			}
			foreach (RaceDriver d in Drivers) {
				d.BestQualifying = 0;
				d.QualifyingRuns = 0;
			}
			myBestQualifying_ = 0;
			myQualifyingRuns_ = 0;
			qualifyingEndsHost_ = now + Settings.QualifyingMinutes * 60.0;
			QualifyingEnds = qualifyingEndsHost_;
			Phase = RacePhase.Qualifying;
			lobbyDirty_ = true;
			lastHostBeat_ = double.MinValue;
			Message = "Qualifying";
		}

		/// <summary>Host: ends qualifying early.</summary>
		public void EndQualifying(double now) {
			if (IsHost && Phase == RacePhase.Qualifying) {
				qualifyingEndsHost_ = now;
				QualifyingEnds = now;
			}
		}

		/// <summary>
		/// Host: lines up a race. <paramref name="racers"/> picks who races (null: everyone in the lobby);
		/// <paramref name="order"/> sets the grid when the tournament decides it (else the race's grid order).
		/// </summary>
		public void StartRace(double now, IList<ulong> racers = null, IList<ulong> order = null) {
			if (IsHost && IsGame && Phase != RacePhase.Countdown && Phase != RacePhase.Running) {
				StartGame(now);
				return;
			}
			if (!IsHost || Course == null || Phase == RacePhase.Gridding || Phase == RacePhase.Countdown || Phase == RacePhase.Running) {
				return;
			}
			RaceNumber++;
			foreach (RaceDriver d in Drivers) {
				d.Racer = (racers == null || racers.Contains(d.Id)) && (d.Id != Me || Settings.HostDrives);
				d.Slot = -1;
				d.EliminatedOnLap = 0;
				d.State = RacerState.Racing;
				d.Ready = false;
				d.Progress = 0;
				d.LapsDone = 0;
				d.Time = 0;
				d.Penalty = 0;
				d.Reason = "";
				d.BestLap = 0;
				d.ProgressAt = -1;
				d.TrapTotal = 0;
				d.Incidents = 0;
				d.IncidentLog.Clear();
				d.HostPenalty = 0;
				d.HostDisqualified = false;
			}
			myIncidents_ = 0;
			myLastIncident_ = "";
			consoleRuns_.Clear();
			Recalls = 0;
			RecallBy = "";
			List<RaceDriver> grid = GridOrderOf(Racers, order);
			for (int i = 0; i < grid.Count; i++) {
				grid[i].Slot = i;
			}
			if (Settings.Elimination && Course.IsCircuit) {
				// One lap per driver knocked out, so the last lap is between two.
				Settings.Laps = Math.Max(1, grid.Count - 1);
			}
			eliminations_ = 0;
			Results = null;
			ResultsFor = -1;
			endAtHost_ = -1;
			EndAt = -1;
			bothReadySince_ = -1;
			Phase = RacePhase.Gridding;
			lobbyDirty_ = true;
			lastHostBeat_ = double.MinValue;
			Message = "Line up in your grid spot";
		}

		/// <summary>Host: starts the countdown now, whoever is lined up.</summary>
		public void GoNow(double now) {
			if (IsHost && Phase == RacePhase.Gridding) {
				StartCountdown(now);
			}
		}

		/// <summary>Host: ends the race in <paramref name="seconds"/> (0: now). Anyone still driving then doesn't finish.</summary>
		public void EndRace(double now, double seconds) {
			if (!IsHost || Phase != RacePhase.Running && Phase != RacePhase.Countdown && Phase != RacePhase.Gridding) {
				return;
			}
			if (Phase != RacePhase.Running || seconds <= 0) {
				endAtHost_ = now;
			} else {
				endAtHost_ = endAtHost_ >= 0 ? Math.Min(endAtHost_, now + seconds) : now + seconds;
			}
			EndAt = endAtHost_;
			lastHostBeat_ = double.MinValue;
		}

		/// <summary>Host: back to the lobby after a race (results stay until the next one).</summary>
		public void BackToLobby() {
			if (IsHost && (Phase == RacePhase.Finished || Phase == RacePhase.Qualifying)) {
				Phase = RacePhase.Lobby;
				lobbyDirty_ = true;
			}
		}

		/// <summary>Host: removes a driver from the lobby (they can't join again).</summary>
		public void Kick(ulong id, double now) {
			if (!IsHost || id == Me) {
				return;
			}
			RaceDriver d = Drivers.FirstOrDefault(x => x.Id == id);
			if (d == null) {
				return;
			}
			kicked_.Add(id);
			if (Phase == RacePhase.Running && d.Racer && d.State == RacerState.Racing) {
				d.State = RacerState.Out;
				d.Reason = "removed by the host";
			}
			Drivers.Remove(d);
			lobbyDirty_ = true;
		}

		/// <summary>Closes the race (host) or leaves it (driver).</summary>
		public void Leave(double now) {
			if (Ended) {
				return;
			}
			if (IsHost) {
				Phase = RacePhase.Closed;
				SendLobby(now);
				Message = "You closed the race";
			} else {
				outbox_.Add(New(RaceKind.Leave));
				Message = "You left the race";
			}
			Run?.Cancel("You left the race.");
			Ended = true;
		}
	}
}
