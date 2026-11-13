// Running a time attack: starting and stopping it, recording a map course's path, finishing a run, and teleporting to the start.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		/// <summary>Teleporting puts the car this far (metres) behind the middle of the start line.</summary>
		private const float StartGridMetres = 4f;
		private const float TeleportCooldownSeconds = 3f;

		/// <summary>A flying start on a circuit teleports the car this far (metres) before the line, for a run-up.</summary>
		private const float FlyingRunUpMetres = 150f;

		private float teleportReadyAt_;

		/// <summary>Record path on a map course, while it runs (and for a few seconds after it's saved).</summary>
		private PathRecorder pathRecorder_;
		private float pathDoneUntil_;

		/// <summary>A battle is lined up or running, so time attack, Record path and the course builder wait.</summary>
		private bool InBattle => battle_ != null && battle_.Started && battle_.Active;

		private void StartTimeAttack(Course course) {
			if (InBattle || InRace) {
				courseResult_ = InRace ? "Finish or leave your race first." : "Finish or leave your battle first.";
				return;
			}
			gridRecorder_ = null;
			if (recorder_.State != CourseRecorder.Phase.Idle) {
				courseResult_ = "Finish or clear the course builder first.";
				return;
			}
			pathRecorder_ = null;
			pathShownFor_ = null;
			run_ = new TimeAttackRun(course, course.IsCircuit ? PickedLaps : 1, PickedFlying);
			runRule_ = TimeAttackRule;
			lastStartedCode_ = course.Code;
			runStart_ = null;
			runResultUntil_ = 0;
			ResetRunExtras();
			courseResult_ = "";
			string laps = course.IsCircuit ? (run_.Laps == 0 ? ", unlimited laps" : $", {run_.Laps} lap(s)") : "";
			TrcLog.Info($"Time attack ready on {course.Name} ({course.Code}, {course.SourceName}, {(run_.Flying ? "flying" : "standing")} start{laps}, {course.SectorEnds.Length} sectors).");
			// The window stays open (bskftw: it closed, then had to be opened again to teleport): Teleport to start closes it.
			courseResult_ = $"Time attack ready on {course.Name}: drive to the start, or Teleport to start.";
		}

		private void StopTimeAttack() {
			courseResult_ = "";
			if (run_ != null) {
				TrcLog.Info($"Time attack on {run_.Course.Name} stopped by the player.");
				EndCircuitRunEarly("stopped by the player");
			}
			run_ = null;
		}

		// ---- Record path ----

		/// <summary>Starts recording a path for a map course: drive it from its start line to its finish line once.</summary>
		private void StartRecordPath(Course course) {
			if (recorder_.State != CourseRecorder.Phase.Idle || run_ != null || InBattle) {
				courseResult_ = "Stop the time attack and clear the course builder first.";
				return;
			}
			pathRecorder_ = new PathRecorder(course);
			courseResult_ = "";
			TrcLog.Info($"Record path started on {course.Name}.");
		}

		private void TickPathRecorder() {
			if (pathRecorder_.State == PathRecorder.Phase.Done) {
				if (Time.unscaledTime >= pathDoneUntil_) {
					pathRecorder_ = null;
				}
				return;
			}
			pathRecorder_.Tick(pose_.Position);
			if (pathRecorder_.State == PathRecorder.Phase.Done) {
				pathDoneUntil_ = Time.unscaledTime + ResultSeconds;
				SaveRecordedPath();
			}
		}

		/// <summary>
		/// Saves the recorded path as a course file with the map course's own lines (replacing an older recording
		/// of the same course), then looks through the map again so the map course takes it.
		/// </summary>
		private void SaveRecordedPath() {
			try {
				Course recorded = pathRecorder_.Build($"steam:{Kino.Info.SteamId.ToString(CultureInfo.InvariantCulture)}");
				foreach (Course old in courses_.Custom.Where(c => c.IsCircuit == recorded.IsCircuit && c.SameLines(recorded)).ToList()) {
					CourseLibrary.DeleteCustom(old, out _);
				}
				string path = CourseLibrary.SaveCustom(CoursesFolder, recorded);
				courses_.LoadCustom(CoursesFolder);
				MapCoursesFromFilesAgain();
				courseResult_ = $"Path recorded for {recorded.Name}: {TimeAttackRun.Distance(recorded.Length)}. Recorded paths are for practice; official ones come with the mod.";
				TrcLog.Info($"Path recorded for {recorded.Name}: {path} ({recorded.Length:0} m, code {recorded.Code}).");
			} catch (Exception e) {
				courseResult_ = $"Couldn't save the path: {e.Message}";
				TrcLog.Error(e);
			}
		}

		private void TickRun() {
			// On a map course with TRC road objects: is any part of the car over them (null without road objects)?
			bool? onRoad = null;
			HashSet<int> road = run_.Course.RoadColliders;
			if (road.Count > 0 && run_.State == TimeAttackRun.Phase.Running && adapter_.TryCheckOverRoad(road, out bool overRoad)) {
				onRoad = overRoad;
			}

			TimeAttackRun.Phase before = run_.State;
			run_.Tick(Time.timeAsDouble, pose_.Position, pose_.Forward, pose_.SpeedKmh, Time.timeScale, onRoad);
			TimeAttackRun.Phase now = run_.State;

			if (before == TimeAttackRun.Phase.ToStart && now == TimeAttackRun.Phase.Countdown) {
				// Checked while the car stands still, so it doesn't cost a frame at GO.
				runStart_ = CheckRunCar();
			}
			if (run_.Flying && now == TimeAttackRun.Phase.ToStart && (runStart_ == null || runStart_.CarId != pose_.CarId)) {
				// A flying start has no standstill: the car is checked on the way to the line instead.
				runStart_ = CheckRunCar();
			}
			if (now == TimeAttackRun.Phase.Running && runStart_ != null && pose_.CarId != runStart_.CarId) {
				run_.Cancel("You changed car, so the run was stopped.");
				now = run_.State;
			}

			HandleRunProgress();
			if (before == TimeAttackRun.Phase.Running && now == TimeAttackRun.Phase.Finished) {
				FinishRun();
				RateRun(run_);
			} else if (before != TimeAttackRun.Phase.Cancelled && now == TimeAttackRun.Phase.Cancelled) {
				RateRun(run_);
				TrcLog.Info($"Time attack on {run_.Course.Name} stopped: {run_.Message}");
				string laps = LapSummary(run_);
				runResultText_ = laps.Length > 0 ? $"{run_.Message}\n<color={Theme.MutedHex}>{laps}</color>" : run_.Message;
				runResultUntil_ = Time.unscaledTime + ResultSeconds;
			}

			if (!run_.Active && Time.unscaledTime >= runResultUntil_) {
				run_.Restart();
				runStart_ = null;
				ResetRunExtras();
			}
		}

		private RunCheck CheckRunCar() {
			var check = new RunCheck { CarId = pose_.CarId };
			try {
				modCheck_.Refresh();
				check.Flagged = modCheck_.Flags.Count;
				if (RefreshCar()) {
					check.Car = car_.CarName;
					check.Setup = car_.SlotName;
					OwnCheck own = CheckOwnCar(recordInGarage: false);
					carStatus_ = own;
					check.State = StateOf(own);
					if (own.Saved != null) {
						check.Code = own.Saved.Code;
						check.Set = own.Matched ? own.Official.Name : "";
					}
				}
			} catch (Exception e) {
				TrcLog.Error(e);
			}
			return check;
		}

		private void FinishRun() {
			RunCheck end = CheckRunCar();
			RunCheck start = runStart_ ?? end;
			Course course = run_.Course;

			TimeAttackRecord record = RecordFor(start, end.State, (long)Math.Round(run_.Result * 1000.0, MidpointRounding.AwayFromZero));
			record.FlaggedMods = Math.Max(start.Flagged, end.Flagged);
			record.Flying = run_.Flying;
			record.Laps = run_.Laps;
			record.TrapKmh = run_.TrapSpeeds.ToArray();
			record.Ghost = GhostShowingFor(course);
			SetRules(record);
			LapTime only = !course.IsCircuit && run_.LapTimes.Count == 1 ? run_.LapTimes[0] : null;
			if (only != null) {
				record.SectorMs = only.Sectors.Select(s => (long)Math.Round(s * 1000.0, MidpointRounding.AwayFromZero)).ToArray();
			}

			// Compared with the same car on the same TRC tune set, started the same way: each has its own times.
			TimeKind kind = RunKind(run_);
			TimeAttackRecord best = TimeAttackLog.BestFor(times_, course.Code, record.CarId, record.TuneSet, record.IsSpec, kind);
			SaveRecord(record);
			// For the finish gantry: this time's place among the car's times on the course, started the same way.
			signRank_ = 1 + times_.Count(r => r != record && !r.IsLap && r.CourseCode == course.Code && r.CarId == record.CarId && r.TuneSet == record.TuneSet &&
				r.Flying == record.Flying && r.Laps == record.Laps && r.Rule == record.Rule && r.TimeMs < record.TimeMs);
			signBest_ = best == null || record.TimeMs < best.TimeMs;
			if (only != null) {
				string key = TimeAttackTraces.Key(course.Code, record.CarId, record.TuneSet, kind);
				if (TimeAttackTraces.SaveIfFaster(TimesFolder, key, only.Trace, record.TimeMs)) {
					TimeAttackTraces.SaveLine(TimesFolder, key, only.Line, only.LineKmh, record.TimeMs);
				}
			}

			string firstInCar = bestAnyCarMs_ > 0 && bestAnyCarName_.Length > 0
				? $" · {TimeAttackRun.FormatGap((record.TimeMs - bestAnyCarMs_) / 1000.0)} on your best ({ShortCar(bestAnyCarName_)})" : "";
			string compare = best == null ? $"first time in this car{(record.TuneSet.Length > 0 ? $" on {record.TuneSet}" : "")}{firstInCar}"
				: record.TimeMs < best.TimeMs ? $"new best by {TimeAttackRun.FormatTime((best.TimeMs - record.TimeMs) / 1000.0)}"
				: $"+{TimeAttackRun.FormatTime((record.TimeMs - best.TimeMs) / 1000.0)} on your best";
			string status = record.Rule == TimeAttackLog.OpenRule ? "Open (any setup)"
				: record.Rule.Length > 0 ? (record.RuleOk ? $"<color={Theme.GoodHex}>{RuleName(record.Rule)}</color>" : $"<color={Theme.BadHex}>doesn't meet {RuleName(record.Rule)}</color>")
				: !record.IsSpec ? $"<color={Theme.BadHex}>{DoesntCount(record)}</color>"
				: course.Official ? $"<color={Theme.GoodHex}>{record.TuneSet} spec</color>"
				: $"{record.TuneSet} spec · practice course";
			string laps = LapSummary(run_);
			string sectors = showSectors_ && only != null && only.Sectors.Length > 1 ? $"\n{ResultSectors(only)}" : "";
			if (record.TrapKmh.Length > 0) {
				sectors += $"\n<color={Theme.WarnHex}>Speed traps: {string.Join(" · ", record.TrapKmh.Select(k => $"{k:0}"))} km/h (total {record.TrapTotal:0})</color>";
			}
			sectors += GhostResultText(run_.Result, only?.Sectors);
			runResultText_ = $"<b>{TimeAttackRun.FormatTime(record.Seconds)}</b>\n{compare} · {status}{(laps.Length > 0 ? $"\n<color={Theme.MutedHex}>{laps}</color>" : "")}{sectors}";
			runResultUntil_ = Time.unscaledTime + ResultSeconds;
			visit_?.Run(course.Name, record.Seconds);
			TrcLog.Info($"Time attack on {course.Name} ({course.Code}): {TimeAttackRun.FormatTime(record.Seconds)} in {record.Car}, " +
				$"tune {record.TuneCode} {record.TuneSet}, {record.Spec}, flagged mods {record.FlaggedMods}, {(run_.Flying ? "flying" : "standing")} start, " +
				$"jump starts {run_.JumpStarts}{(laps.Length > 0 ? $", {laps}" : "")}{(only != null ? $", sectors {string.Join(" / ", only.Sectors.Select(s => TimeAttackRun.FormatTime(s)))}" : "")}.");
		}

		/// <summary>"spec" only when the car was TRC spec at both the start and the finish.</summary>
		private static string SpecWord(SpecState start, SpecState end) {
			if (start == SpecState.Spec && end == SpecState.Spec) {
				return TimeAttackLog.SpecWord;
			}
			if (start == SpecState.NotSpec || end == SpecState.NotSpec) {
				return "not spec";
			}
			return start == SpecState.None || end == SpecState.None ? "no TRC tune" : "not fully checked";
		}

		private static string DoesntCount(TimeAttackRecord record) =>
			record.FlaggedMods > 0 ? $"{record.FlaggedMods} flagged mod(s): doesn't count"
			: record.Spec == "not fully checked" ? "the live car wasn't checked yet: doesn't count"
			: record.Spec == "no TRC tune" ? "no TRC tune for this car: doesn't count"
			: "not TRC spec: doesn't count";

		/// <summary>Puts the car just behind the start line with CarX's own placement, and gets the run ready to go again.</summary>
		private void TeleportToStart() {
			Course course = run_?.Course ?? pathRecorder_?.Course;
			if (course == null) {
				return;
			}
			if (Time.unscaledTime < teleportReadyAt_) {
				courseResult_ = "Wait a moment before teleporting again.";
				return;
			}
			CourseGate start = course.Start;
			Vector3 spot = start.Center - start.Forward * StartGridMetres;
			Vector3 facing = start.Forward;
			if (run_ != null && run_.Flying && course.IsCircuit && course.HasPath) {
				// A flying lap needs a run-up: back along the end of the loop.
				int at = Array.FindIndex(course.Distances, d => d >= course.Length - FlyingRunUpMetres);
				if (at > 0 && at < course.Path.Count - 1) {
					spot = course.Path[at];
					facing = CourseGeometry.Level(course.Path[at + 1] - course.Path[at]);
				}
			}
			if (!adapter_.TryMoveCarTo(spot, facing, out string error)) {
				courseResult_ = $"Couldn't teleport: {error}";
				return;
			}
			// Restarted in the same frame, so the move isn't taken for a reset in the middle of a run.
			teleportReadyAt_ = Time.unscaledTime + TeleportCooldownSeconds;
			if (run_ != null) {
				EndCircuitRunEarly("restarted");
				run_.Restart();
				runStart_ = null;
				runResultUntil_ = 0;
				ResetRunExtras();
			} else {
				pathRecorder_ = new PathRecorder(course);
			}
			courseResult_ = "";
			TrcLog.Info($"Teleported to the start of {course.Name}.");
			CloseWindow();
		}
	}
}
