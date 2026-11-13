// Console players in a race, timed by the host's mod: adding them, their car and pose, and their runs.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	public sealed partial class RaceSession {
		/// <summary>The host's runs for its console players, by their ID (made when they line up for a race).</summary>
		private readonly Dictionary<ulong, TimeAttackRun> consoleRuns_ = new Dictionary<ulong, TimeAttackRun>();
		private readonly Dictionary<ulong, int> consoleCountdowns_ = new Dictionary<ulong, int>();
		private readonly Dictionary<ulong, BattleCar> consoleCars_ = new Dictionary<ulong, BattleCar>();
		private readonly Dictionary<ulong, (Vector3 Position, Vector3 Forward, float Kmh)> consolePoses_ = new Dictionary<ulong, (Vector3, Vector3, float)>();
		private double lastConsoleBeat_ = double.MinValue;

		/// <summary>
		/// Host: adds a console player (bskftw, Nov 1: console players can't run the mod, so a PC host adds them and
		/// its mod times them from where CarX shows their car). <paramref name="id"/> is any number that isn't a
		/// SteamID, the same for them all session (<see cref="ConsoleId"/>).
		/// </summary>
		public bool AddConsole(ulong id, string name, string platform, double now) {
			if (!IsHost || Drivers.Any(d => d.Id == id) || Drivers.Count >= Settings.MaxPlayers) {
				return false;
			}
			bool racing = Phase == RacePhase.Gridding || Phase == RacePhase.Countdown || Phase == RacePhase.Running;
			Drivers.Add(new RaceDriver { Id = id, Name = BattleMessage.PlainName(name), Console = true, Platform = platform ?? "", Racer = !racing, HasCourse = true, LastHeard = now });
			lobbyDirty_ = true;
			return true;
		}

		/// <summary>An ID for a console player from what CarX calls them ("123|PS4"): the top bits set, so it's never a SteamID.</summary>
		public static ulong ConsoleId(string playerKey) {
			ulong hash = 14695981039346656037UL;
			foreach (char c in playerKey ?? "") {
				hash = (hash ^ c) * 1099511628211UL;
			}
			return 0xC000000000000000UL | (hash & 0x3FFFFFFFFFFFFFFFUL);
		}

		public static bool IsConsoleId(ulong id) => (id & 0xC000000000000000UL) == 0xC000000000000000UL;

		/// <summary>Host, every frame: where a console player's car is, as CarX shows it here.</summary>
		public void SetConsolePose(ulong id, Vector3 position, Vector3 forward, float kmh, double now) {
			RaceDriver d = Drivers.FirstOrDefault(x => x.Id == id && x.Console);
			if (d == null) {
				return;
			}
			d.LastHeard = now;
			consolePoses_[id] = (position, CourseGeometry.Level(forward), Mathf.Abs(kmh));
		}

		/// <summary>Host: a console player's car, checked from what CarX shares (TRC spec, the regulation).</summary>
		public void SetConsoleCar(ulong id, BattleCar car) {
			if (car != null) {
				consoleCars_[id] = car;
			}
		}

		/// <summary>Host: times every console player's race like a driver's own mod would.</summary>
		private void TickConsoles(double now) {
			foreach (RaceDriver d in Drivers.Where(x => x.Console).ToList()) {
				if (!consolePoses_.TryGetValue(d.Id, out var pose)) {
					continue;
				}
				d.Position = pose.Position;
				d.Heading = pose.Forward;
				d.Kmh = pose.Kmh;
				d.HasPosition = true;
				d.HasCourse = true;
				d.RaceNumber = RaceNumber;
				consoleCars_.TryGetValue(d.Id, out BattleCar car);
				d.Spec = car?.Spec ?? false;
				d.TuneSet = car?.TuneSet ?? "";
				d.Car = car?.Car ?? d.Car;
				d.RuleOk = car?.RuleOk ?? false;
				d.SaidRuleOk = car != null;
				bool racing = Phase == RacePhase.Gridding || Phase == RacePhase.Countdown || Phase == RacePhase.Running;
				if (IsGame || !racing || !d.Racer || Course == null || d.Slot < 0) {
					continue;
				}
				if (!consoleRuns_.TryGetValue(d.Id, out TimeAttackRun run)) {
					List<GridSpot> spots = Course.GridSpots(d.Slot + 1, Settings.PoleLeft, Settings.GridShape);
					run = new TimeAttackRun(Course, Settings.Laps) {
						ExternalStart = true, AllowResets = true, KeepGoing = true, OnJump = RunJumpRule(Settings.Jump), Spot = spots[d.Slot],
						ClockFromLine = Settings.SingleFile,
					};
					consoleRuns_[d.Id] = run;
					consoleCountdowns_[d.Id] = 0;
					d.State = RacerState.Racing;
				}
				if ((Phase == RacePhase.Countdown || Phase == RacePhase.Running) && countdownId_ > 0 && consoleCountdowns_[d.Id] != countdownId_ && run.State == TimeAttackRun.Phase.ToStart) {
					consoleCountdowns_[d.Id] = countdownId_;
					run.StartCountdown(goHostTime_);
				}
				int jumps = run.JumpStarts;
				TimeAttackRun.Phase before = run.State;
				if (d.State == RacerState.Racing) {
					run.Tick(now, pose.Position, pose.Forward, pose.Kmh, 1f, null);
				}
				if (run.JumpStarts > jumps && Settings.Jump == JumpRule.Recall) {
					Recall(now, d.Name);
					continue;
				}
				if (EndAt >= 0 && now >= EndAt && d.State == RacerState.Racing) {
					run.Cancel("The race was ended.");
					d.State = RacerState.Out;
					d.Reason = "the race was ended";
				}
				if (d.EliminatedOnLap > 0 && run.Active) {
					run.Cancel($"Eliminated on lap {d.EliminatedOnLap}.");
				}
				if (d.State == RacerState.Racing && before != run.State && !run.Active) {
					if (run.State == TimeAttackRun.Phase.Finished) {
						d.State = RacerState.Finished;
						d.Penalty = run.JumpStarts > 0 && Settings.Jump == JumpRule.Penalty && !Settings.NoPenalties ? Settings.JumpPenaltySeconds : 0;
						d.Time = run.Result + d.Penalty;
					} else if (run.JumpStarts > 0 && Settings.Jump == JumpRule.Disqualify) {
						d.State = RacerState.Disqualified;
						d.Reason = "jump start";
					} else {
						d.State = RacerState.Out;
						d.Reason = run.Message.TrimEnd('.');
					}
				}
				d.Progress = run.TotalProgress;
				d.ProgressAt = now;
				d.ProgressHeardAt = now;
				d.Speed = 0;
				d.LapsDone = run.LapTimes.Count;
				d.BestLap = run.BestLap?.Time ?? 0;
				d.TrapTotal = run.TrapTotal;
				d.Ready = run.State == TimeAttackRun.Phase.ToStart && run.ReadyToStart && car != null && CarAllowed(car);
			}
		}

		/// <summary>Host: the console players' beats, sent for them (so every other mod sees them like any driver).</summary>
		private void ConsoleBeats(double now) {
			if (now - lastConsoleBeat_ < BeatSeconds(now)) {
				return;
			}
			lastConsoleBeat_ = now;
			foreach (RaceDriver d in Drivers.Where(x => x.Console && x.HasPosition)) {
				outbox_.Add(new RaceMessage {
					Kind = RaceKind.Beat, RaceId = Id, From = d.Id, RaceNumber = RaceNumber, SentAt = now, Ready = d.Ready, Spec = d.Spec, TuneSet = d.TuneSet,
					Car = d.Car, HasCourse = true, Progress = d.Racer ? d.Progress : 0f, LapsDone = d.LapsDone, State = d.State, Time = d.Time, Penalty = d.Penalty,
					Reason = d.Reason, BestLap = d.BestLap, Position = d.Position, Heading = d.Heading, Speed = d.Kmh, TrapTotal = d.TrapTotal, RuleOk = d.RuleOk,
					Elo = (float)RatingBook.StartElo,
				});
			}
		}

		/// <summary>The host's run for a console player (for the HUD and tests), or null.</summary>
		public TimeAttackRun ConsoleRun(ulong id) => consoleRuns_.TryGetValue(id, out TimeAttackRun run) ? run : null;
	}
}
