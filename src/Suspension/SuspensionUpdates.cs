// Suspension logging in game: its options, switching it on and off, and reading the four wheels each frame into the logs.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// The Suspension tab and overlay (0.16, bskftw: suspension data logging "like in real life", its own tab, a nice
	/// overlay, and switches so it doesn't run all the time). Off by default: while it's off nothing is read or drawn.
	/// On, it reads the four wheels every frame on track (only during runs, if picked), keeps a log for the session
	/// and one for the last run, and the overlay shows the car from above with each tyre filling up as it compresses.
	/// The analysis is in src/suspension/SuspensionLog.cs; the overlay and the page are in src/pages/.
	/// </summary>
	public partial class TRCTuneLoaderMod {
		private const int SuspensionTab = 8;
		private const string SuspensionPref = "suspension_log";
		private const string SuspensionOverlayPref = "suspension_overlay";
		private const string SuspensionScopePref = "suspension_scope";
		private const string SuspensionPositionPref = "suspension_position";
		private const string SuspensionFolderName = "Suspension";

		private bool suspensionOn_;
		private bool suspensionOverlay_ = true;
		private int suspensionScope_;

		private SuspensionLog session_;
		private SuspensionLog runLog_;
		private SuspensionLog lastRunLog_;
		private SuspensionLog baseline_;
		private TimeAttackRun loggedRun_;
		private SuspensionFrame liveFrame_;
		private CornerSetup[] corners_;
		private float nextSetupRead_;
		private int setupCar_;

		private string SuspensionFolder => Path.Combine(modFolder_, SuspensionFolderName);

		private void LoadSuspensionOptions() {
			suspensionOn_ = Kino.Prefs.Get(SuspensionPref, false);
			suspensionOverlay_ = Kino.Prefs.Get(SuspensionOverlayPref, true);
			suspensionScope_ = Mathf.Clamp(Kino.Prefs.Get(SuspensionScopePref, 0), 0, 1);
			suspensionPosition_ = Kino.Prefs.Get(SuspensionPositionPref, new Vector2(-1f, -1f));
		}

		private void SetSuspensionOn(bool on) {
			suspensionOn_ = on;
			SavePref(SuspensionPref, on);
			if (!on) {
				// Off means off: nothing kept being read, and the memory goes.
				runLog_ = null;
				loggedRun_ = null;
				liveFrame_ = null;
			}
			TrcLog.Info($"Suspension logging {(on ? "on" : "off")}.");
		}

		/// <summary>Called from Update every frame. Does nothing at all while logging is off.</summary>
		private void UpdateSuspension() {
			if (!suspensionOn_ || !hasPose_) {
				return;
			}
			TimeAttackRun run = ActiveRun;
			bool running = run != null && run.State == TimeAttackRun.Phase.Running;
			if (suspensionScope_ == 1 && !running && runLog_ == null) {
				liveFrame_ = null;
				return;
			}
			if (Time.unscaledTime >= nextSetupRead_ || setupCar_ != pose_.CarId) {
				nextSetupRead_ = Time.unscaledTime + 5f;
				setupCar_ = pose_.CarId;
				if (adapter_.TryGetSuspensionSetup(out CornerSetup[] corners)) {
					corners_ = corners;
				}
			}
			var frame = new SuspensionFrame { Time = Time.timeAsDouble };
			if (!adapter_.TryGetSuspension(frame)) {
				liveFrame_ = null;
				return;
			}
			liveFrame_ = frame;
			if (session_ == null) {
				session_ = NewSuspensionLog(map_?.Name ?? "Free roam", null);
			}
			session_.Setup = corners_ ?? session_.Setup;
			session_.Add(frame);

			// The run being driven gets a log of its own; when it ends, that's "Last run".
			if (running && loggedRun_ != run) {
				loggedRun_ = run;
				runLog_ = NewSuspensionLog(run.Course.Name, run.Course);
			}
			if (runLog_ != null) {
				runLog_.Setup = corners_ ?? runLog_.Setup;
				runLog_.Add(frame);
				if (!running) {
					runLog_.Finish();
					if (runLog_.Seconds >= 3) {
						lastRunLog_ = runLog_;
						TrcLog.Info($"Suspension: logged {runLog_.Label}, {runLog_.Seconds:0} s, {runLog_.Events.Count} event(s).");
					}
					runLog_ = null;
					loggedRun_ = null;
				}
			}
		}

		private SuspensionLog NewSuspensionLog(string label, Course course) {
			var log = new SuspensionLog { Label = label, Started = DateTime.Now, Setup = corners_ ?? new CornerSetup[4] };
			if (course != null && course.HasPath) {
				log.AlongCourse = p => CourseGeometry.Project(course.Path, course.Distances, p, 0, course.Path.Count).Progress;
			}
			return log;
		}
	}
}
