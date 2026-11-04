using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// Ghosts and shared runs (0.16, bskftw): every run (time attack, battle, race) is recorded as a ghost while it's
	/// driven; a time attack's best is saved by itself, and any run can be saved or shared with the room or one player.
	/// A ghost raced in a time attack shows as a see-through car body with no collider or physics (KINO's ghost could
	/// be used for a slipstream; this one can't touch anything), and the live delta and sectors compare with it. The
	/// ghost logic and file are in src/Courses/Ghost.cs.
	/// </summary>
	public partial class TRCTuneLoaderMod {
		private const string GhostsFolderName = "Ghosts";
		private const int GhostsKeptPerCar = 3;
		private const string GhostsPref = "ghost_record";

		/// <summary>Options (bskftw): keep the 3 best time attacks per car and course (the default), keep every finished run, or record nothing.</summary>
		private static readonly string[] GhostModes = { "Keep my 3 best", "Keep every run", "Don't record" };
		private const int KeepBestGhosts = 0;
		private const int KeepEveryGhost = 1;
		private const int NoGhosts = 2;
		private int ghostMode_ = KeepBestGhosts;

		/// <summary>Options: how see-through ghosts are (bskftw, testing: more see-through).</summary>
		private const string GhostOpacityPref = "ghost_opacity";
		private static readonly string[] GhostOpacities = { "Faint", "Light", "Medium", "Solid" };
		private static readonly float[] GhostOpacityValues = { 0.15f, 0.25f, 0.4f, 0.75f };
		private int ghostOpacity_ = 1;

		// Renaming a ghost in the list.
		private Ghost renamingGhost_;
		private string ghostName_ = "";

		private string GhostsFolder => Path.Combine(modFolder_, GhostsFolderName);

		// Recording the run being driven, and the last one finished.
		private Ghost recording_;
		private TimeAttackRun recordingRun_;
		private Ghost lastGhost_;

		// The ghost raced in a time attack, and its car body on the track.
		private Ghost racingGhost_;
		private GameObject ghostBody_;
		private TimeAttackRun ghostReferenceFor_;
		private bool ghostBodyFailed_;

		// The Tracks list: which course's ghosts are open, and each course's saved ghosts (read when opened).
		private string ghostsOpenFor_ = "";
		private readonly Dictionary<string, List<Ghost>> savedGhosts_ = new Dictionary<string, List<Ghost>>(StringComparer.Ordinal);
		private Ghost sharingGhost_;
		private ulong shareGhostTo_;

		private List<Ghost> SavedGhostsFor(string courseCode) {
			if (!savedGhosts_.TryGetValue(courseCode, out List<Ghost> list)) {
				try {
					list = Ghost.LoadFor(GhostsFolder, courseCode);
				} catch (Exception e) {
					TrcLog.Error(e);
					list = new List<Ghost>();
				}
				savedGhosts_[courseCode] = list;
			}
			return list;
		}

		/// <summary>Called from Update every frame: records the run being driven, and moves the raced ghost.</summary>
		private void UpdateGhosts() {
			RecordGhost();
			ShowGhost();
		}

		private void RecordGhost() {
			if (ghostMode_ == NoGhosts) {
				recording_ = null;
				recordingRun_ = null;
				return;
			}
			TimeAttackRun run = hasPose_ ? ActiveRun : null;
			if (run != null && run.State == TimeAttackRun.Phase.Running && !run.BeforeLine) {
				if (recordingRun_ != run || recording_ == null) {
					recordingRun_ = run;
					recording_ = NewGhost(run);
				}
				recording_.Record(run.Elapsed, pose_.Position, pose_.Rotation, pose_.SpeedKmh);
				return;
			}
			if (recording_ == null || recordingRun_ == null) {
				return;
			}
			if (recordingRun_.State == TimeAttackRun.Phase.Finished && recordingRun_.Result > 0) {
				recording_.Time = recordingRun_.Result;
				recording_.Sectors = recordingRun_.Course.IsCircuit ? new List<double>() : recordingRun_.CurrentSectors;
				recording_.TrapTotal = recordingRun_.TrapTotal;
				recording_.Record(recordingRun_.Result, pose_.Position, pose_.Rotation, pose_.SpeedKmh);
				lastGhost_ = recording_;
				if (recording_.Samples.Count >= 2) {
					if (ghostMode_ == KeepEveryGhost) {
						SaveGhost(recording_, quiet: true);
					} else if (recordingRun_ == run_) {
						SaveIfBestGhost(recording_);
					}
				}
			}
			if (recordingRun_.State != TimeAttackRun.Phase.Running) {
				recording_ = null;
				recordingRun_ = null;
			}
		}

		private Ghost NewGhost(TimeAttackRun run) {
			string kind = run == run_ ? "time attack" : InRace ? "race" : "battle";
			string rules = run == run_ ? (runStart_ != null && runStart_.Set.Length > 0 ? runStart_.Set : "not spec")
				: InRace ? RuleText(race_.Settings.Open, race_.Settings.Regulation, false)
				: RuleText(battle_.Open, battle_.Regulation, false);
			return new Ghost {
				CourseCode = run.Course.Code, CourseName = run.Course.Name, MapKey = run.Course.MapKey, DriverId = MySteamId, Driver = BattleMessage.PlainName(MyName()),
				CarId = pose_.CarId, Car = car_ != null && car_.CarId == pose_.CarId ? car_.CarName : adapter_.TryGetCarName(pose_.CarId, out string name) ? name : "",
				Rules = rules, Kind = kind, When = DateTime.Now,
			};
		}

		/// <summary>A time attack's ghost is kept when it's this car's best on the course (the 3 best per car stay).</summary>
		private void SaveIfBestGhost(Ghost ghost) {
			List<Ghost> saved = SavedGhostsFor(ghost.CourseCode);
			List<Ghost> mine = saved.Where(g => g.DriverId == ghost.DriverId && g.CarId == ghost.CarId && g.Kind == "time attack").ToList();
			if (mine.Count >= GhostsKeptPerCar && mine.All(g => g.Time <= ghost.Time)) {
				return;
			}
			try {
				ghost.Save(GhostsFolder);
				saved.Add(ghost);
				mine.Add(ghost);
				foreach (Ghost slow in mine.OrderBy(g => g.Time).Skip(GhostsKeptPerCar).ToList()) {
					DeleteGhost(slow);
				}
				savedGhosts_[ghost.CourseCode] = saved.OrderBy(g => g.Time).ToList();
				TrcLog.Info($"Ghost saved: {ghost.CourseName}, {TimeAttackRun.FormatTime(ghost.Time)} ({ghost.Samples.Count} points) at {ghost.FilePath}.");
			} catch (Exception e) {
				TrcLog.Error(e);
			}
		}

		private void DeleteGhost(Ghost ghost) {
			try {
				if (ghost.FilePath.Length > 0 && File.Exists(ghost.FilePath)) {
					File.Delete(ghost.FilePath);
				}
			} catch (Exception e) {
				TrcLog.Error(e);
			}
			if (savedGhosts_.TryGetValue(ghost.CourseCode, out List<Ghost> list)) {
				list.Remove(ghost);
			}
			if (racingGhost_ == ghost) {
				racingGhost_ = null;
			}
		}

		/// <summary>Whether a ghost is showing on this course right now (its driver's name, for the run log), or "".</summary>
		private string GhostShowingFor(Course course) =>
			racingGhost_ != null && course != null && racingGhost_.CourseCode == course.Code ? $"{racingGhost_.Driver} {TimeAttackRun.FormatTime(racingGhost_.Time)}" : "";

		/// <summary>The raced ghost: placed where it was at this moment of its run (at its start before GO), hidden otherwise.</summary>
		private void ShowGhost() {
			Ghost ghost = racingGhost_;
			bool show = ghost != null && hasPose_ && run_ != null && run_.Course.Code == ghost.CourseCode && !InRace && !InBattle;
			if (!show) {
				if (ghostBody_ != null) {
					ghostBody_.SetActive(false);
				}
				return;
			}
			if (ghostBody_ == null && !ghostBodyFailed_) {
				ghostBody_ = adapter_.BuildGhostBody(CourseMarkers.GhostMaterial);
				ghostBodyFailed_ = ghostBody_ == null;
				TrcLog.Info(ghostBody_ != null ? $"Ghost: {ghost.Title}, {ghostBody_.transform.childCount} parts, no collider, drawn {CourseMarkers.GhostLook}."
					: "Ghost: couldn't make a car body to show it.");
			}
			if (ghostBody_ == null) {
				return;
			}
			if (ghostReferenceFor_ != run_ && referenceLoaded_) {
				// Racing their ghost: the live delta compares with their run, not your own best.
				ghostReferenceFor_ = run_;
				float[] trace = ghost.TraceOn(run_.Course);
				if (trace != null) {
					run_.Reference = trace;
				}
			}
			double t = run_.State == TimeAttackRun.Phase.Running ? run_.Elapsed : 0;
			if (ghost.PoseAt(t, out GhostSample pose)) {
				ghostBody_.SetActive(true);
				ghostBody_.transform.SetPositionAndRotation(pose.Position, pose.Rotation);
			} else {
				ghostBody_.SetActive(false);
			}
		}

		/// <summary>Stops showing a ghost (and lets the next one build its body from the car driven then).</summary>
		private void StopGhost() {
			racingGhost_ = null;
			if (ghostBody_ != null) {
				UnityEngine.Object.Destroy(ghostBody_);
				ghostBody_ = null;
			}
			ghostBodyFailed_ = false;
			ghostReferenceFor_ = null;
		}

		private void RaceGhost(Ghost ghost, Course course) {
			StopGhost();
			racingGhost_ = ghost;
			TrcLog.Info($"Racing the ghost of {ghost.Title} on {course.Name}.");
			StartTimeAttack(course);
		}

		/// <summary>The line after a time attack's time: how it went against the ghost, overall and sector by sector.</summary>
		private string GhostResultText(double time, IList<double> sectors) {
			Ghost g = racingGhost_;
			if (g == null) {
				return "";
			}
			string text = $"\n<color=#8FB8FF>vs {g.Driver}'s ghost: {TimeAttackRun.FormatGap(time - g.Time)}";
			if (sectors != null && g.Sectors.Count == sectors.Count && sectors.Count > 1) {
				text += " · sectors " + string.Join(" ", sectors.Select((s, i) => TimeAttackRun.FormatGap(s - g.Sectors[i])));
			}
			return text + "</color>";
		}

		// ---- Tracks: the ghosts of a course ----

		/// <summary>The ghosts button on a track's row, and its list when open.</summary>
		private void DrawGhostsButton(Course course) {
			int count = SavedGhostsFor(course.Code).Count + roomBoard_.SharedRuns.Count(r => r.Ghost.CourseCode == course.Code) +
				(lastGhost_ != null && lastGhost_.CourseCode == course.Code && lastGhost_.FilePath.Length == 0 ? 1 : 0);
			string label = racingGhost_ != null && racingGhost_.CourseCode == course.Code ? "Ghost on" : count > 0 ? $"Ghosts ({count})" : "Ghosts";
			if (NavButton(ghostsOpenFor_ == course.Code ? "Hide ghosts" : label, Theme.Button)) {
				ghostsOpenFor_ = ghostsOpenFor_ == course.Code ? "" : course.Code;
			}
		}

		private void DrawGhostList(Course course) {
			if (ghostsOpenFor_ != course.Code) {
				return;
			}
			GUILayout.BeginVertical(Theme.Guide);
			GUILayout.Label("Ghosts", Theme.Heading);
			GUILayout.Label("Race a ghost in a time attack: it's a see-through car with no collider or physics, so it can't push, tow or slipstream you. " +
				"The live delta and your sectors compare with it. " + (ghostMode_ == KeepEveryGhost ? "Every run you finish is kept." : ghostMode_ == KeepBestGhosts
					? "Your 3 best time attacks on each car are kept by themselves; Save keeps any other." : "Recording ghosts is off (Options)."), Theme.Small);
			if (racingGhost_ != null && racingGhost_.CourseCode == course.Code) {
				GUILayout.BeginHorizontal();
				GUILayout.Label($"Racing: {racingGhost_.Title}", Theme.PillGood);
				GUILayout.FlexibleSpace();
				if (NavButton("Ghost off", Theme.Button)) {
					StopGhost();
				}
				GUILayout.EndHorizontal();
			}
			var rows = new List<(Ghost Ghost, string From, RoomBoard.SharedItem Shared)>();
			if (lastGhost_ != null && lastGhost_.CourseCode == course.Code && lastGhost_.FilePath.Length == 0) {
				rows.Add((lastGhost_, $"your last {lastGhost_.Kind}", null));
			}
			rows.AddRange(roomBoard_.SharedRuns.Where(r => r.Ghost.CourseCode == course.Code).Select(r => (r.Ghost, $"shared by {r.FromName}", r)));
			rows.AddRange(SavedGhostsFor(course.Code).Select(g => (g, g.DriverId == MySteamId ? "yours, saved" : "saved", (RoomBoard.SharedItem)null)));
			if (rows.Count == 0) {
				GUILayout.Label("No ghosts on this course yet: drive a time attack, or ask someone to share a run.", Theme.Small);
			}
			foreach (var (ghost, from, shared) in rows) {
				GUILayout.BeginHorizontal();
				string sectors = ghost.Sectors.Count > 1 ? $" · sectors {string.Join(" ", ghost.Sectors.Select(s => TimeAttackRun.FormatTime(s)))}" : "";
				string name = ghost.Name.Length > 0 ? $"<b>{ghost.Name}</b>  " : "";
				GUILayout.Label($"{name}<b>{TimeAttackRun.FormatTime(ghost.Time)}</b> {ghost.Driver}  <color={Theme.MutedHex}>{ghost.Car} · {ghost.Rules} · {from} · " +
					$"{ghost.When.ToString("MMM d", CultureInfo.InvariantCulture)}{sectors}</color>", Theme.Small);
				GUILayout.FlexibleSpace();
				bool enabled = GUI.enabled;
				GUI.enabled = enabled && course.Usable && hasPose_ && !InRace && !InBattle;
				if (NavButton("Race it", Theme.ButtonPrimary)) {
					RaceGhost(ghost, course);
				}
				GUI.enabled = enabled;
				if (ghost.FilePath.Length == 0 && NavButton("Save", Theme.Button)) {
					SaveGhost(ghost);
					if (shared != null) {
						roomBoard_.SharedRuns.Remove(shared);
					}
				}
				if (NavButton(sharingGhost_ == ghost ? "Done" : "Share...", Theme.Button)) {
					sharingGhost_ = sharingGhost_ == ghost ? null : ghost;
				}
				if (shared != null && NavButton("Dismiss", Theme.Button)) {
					roomBoard_.SharedRuns.Remove(shared);
				}
				if (NavButton(renamingGhost_ == ghost ? "Cancel" : "Name", Theme.Button)) {
					renamingGhost_ = renamingGhost_ == ghost ? null : ghost;
					ghostName_ = ghost.Name;
				}
				if (ghost.FilePath.Length > 0 && NavButton("Delete", Theme.Button)) {
					DeleteGhost(ghost);
				}
				GUILayout.EndHorizontal();
				if (renamingGhost_ == ghost) {
					GUILayout.BeginHorizontal();
					ghostName_ = GUILayout.TextField(ghostName_ ?? "", 60, Theme.Field, GUILayout.Width(260));
					if (NavButton(ghost.FilePath.Length > 0 ? "Save name" : "Save with this name", Theme.ButtonPrimary)) {
						ghost.Name = ghostName_.Trim();
						SaveGhost(ghost);
						renamingGhost_ = null;
						if (shared != null) {
							roomBoard_.SharedRuns.Remove(shared);
						}
					}
					GUILayout.FlexibleSpace();
					GUILayout.EndHorizontal();
				}
				if (sharingGhost_ == ghost) {
					DrawShareGhost(ghost);
				}
			}
			GUILayout.EndVertical();
		}

		private void SaveGhost(Ghost ghost, bool quiet = false) {
			try {
				ghost.Save(GhostsFolder);
				List<Ghost> saved = SavedGhostsFor(ghost.CourseCode);
				if (!saved.Contains(ghost)) {
					saved.Add(ghost);
				}
				savedGhosts_[ghost.CourseCode] = saved.OrderBy(g => g.Time).ToList();
				if (!quiet) {
					courseResult_ = $"Ghost saved: {ghost.Title}.";
				}
			} catch (Exception e) {
				courseResult_ = $"Couldn't save the ghost: {e.Message}";
				TrcLog.Error(e);
			}
		}

		/// <summary>Shares a run (its time, sectors and ghost) with the room or one player.</summary>
		private void DrawShareGhost(Ghost ghost) {
			List<RoomMessage> players = roomBoard_.Fresh(BattleNow).Where(s => s.From != MySteamId).ToList();
			if (shareGhostTo_ != 0 && !players.Any(p => p.From == shareGhostTo_)) {
				shareGhostTo_ = 0;
			}
			var who = new List<string> { "Everyone in the room" };
			who.AddRange(players.Select(p => BattleMessage.PlainName(p.Name)));
			int picked = Choice(who.ToArray(), shareGhostTo_ == 0 ? 0 : players.FindIndex(p => p.From == shareGhostTo_) + 1);
			shareGhostTo_ = picked <= 0 ? 0 : players[picked - 1].From;
			GUILayout.BeginHorizontal();
			bool enabled = GUI.enabled;
			GUI.enabled = enabled && roomSync_.IsAvailable;
			string target = shareGhostTo_ == 0 ? "the room" : who[picked];
			if (NavButton($"Send to {target}", Theme.ButtonPrimary)) {
				byte[] bytes = ghost.Encode();
				if (bytes.Length > Ghost.MaxBytes) {
					courseResult_ = $"That run is too long to share ({bytes.Length / 1024} KB, {Ghost.MaxBytes / 1024} KB at most).";
				} else {
					Course course = courses_.ByCode(ghost.CourseCode);
					var m = new RoomMessage {
						Kind = RoomKind.RunShare, From = MySteamId, Name = MyName(), To = shareGhostTo_, Ghost = ghost,
						MapKey = ghost.MapKey, MapName = map_ != null && map_.Key == ghost.MapKey ? map_.Name : course?.MapName ?? "",
					};
					int pieces = roomSync_.Send(m);
					courseResult_ = pieces > 0
						? $"Sending your {TimeAttackRun.FormatTime(ghost.Time)} on {ghost.CourseName} to {target} ({bytes.Length / 1024 + 1} KB, a few seconds). They need TRC Tune Loader 0.16 or newer."
						: "Couldn't send it: is the room's connection all right?";
					TrcLog.Info($"Shared a run ({ghost.CourseName}, {TimeAttackRun.FormatTime(ghost.Time)}, {bytes.Length} bytes) with {target}: {pieces} packet(s).");
				}
				sharingGhost_ = null;
			}
			GUI.enabled = enabled;
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
		}

		/// <summary>Runs shared with you on courses that aren't on this map (the course's own list shows the rest).</summary>
		private void DrawSharedRunsElsewhere() {
			List<RoomBoard.SharedItem> elsewhere = roomBoard_.SharedRuns.Where(r => map_ == null || r.Ghost.MapKey != map_.Key).ToList();
			if (elsewhere.Count == 0) {
				return;
			}
			GUILayout.Label("Runs shared with you on other maps", Theme.SectionLabel);
			GUILayout.BeginVertical(Theme.Card);
			foreach (RoomBoard.SharedItem item in elsewhere) {
				GUILayout.BeginHorizontal();
				GUILayout.Label($"<b>{TimeAttackRun.FormatTime(item.Ghost.Time)}</b> {item.Ghost.Driver} on {item.Ghost.CourseName}  <color={Theme.MutedHex}>{(item.MapName.Length > 0 ? item.MapName : item.MapKey)}</color>", Theme.Small);
				GUILayout.FlexibleSpace();
				if (NavButton("Save", Theme.Button)) {
					SaveGhost(item.Ghost);
					roomBoard_.SharedRuns.Remove(item);
				}
				if (NavButton("Dismiss", Theme.Button)) {
					roomBoard_.SharedRuns.Remove(item);
				}
				GUILayout.EndHorizontal();
			}
			GUILayout.EndVertical();
		}
	}
}
