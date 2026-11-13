// Starting a run: waiting at the line or in a grid spot, a flying start, the countdown, jump starts and GO.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	public sealed partial class TimeAttackRun {
		private bool jumped_;

		private void WaitAtStart(double now, Vector3 position, Vector3 forward, float speedKmh) {
			if (Spot.HasValue) {
				WaitInSpot(now, position, forward, speedKmh, Spot.Value);
				return;
			}
			CourseGate gate = Course.Start;
			float ahead = gate.Ahead(position);
			float across = gate.Across(position);
			float up = position.y - gate.Center.y;
			DistanceToStart = Vector3.Distance(position, gate.Center);
			bool between = Mathf.Abs(across) <= gate.Width / 2f && Mathf.Abs(up) <= GridHeight;
			bool facing = Vector3.Dot(CourseGeometry.Level(forward), gate.Forward) > 0.5f;
			lastSeen_ = position;
			ReadyToStart = false;

			string hint;
			if (!between || ahead > -GridFront || ahead < -GridBack) {
				stillSince_ = -1;
				hint = DistanceToStart > GridBack + gate.Width ? $"Drive to the start: {Distance(DistanceToStart)}"
					: between && ahead > 0f ? "Back up behind the start line"
					: between && ahead > -GridFront ? $"Line up further back: at least {GridFront:0} m behind the start line"
					: between && ahead < -GridBack ? $"Move up: within {GridBack:0} m of the start line"
					: "Line up behind the start line, between its ends";
			} else if (!facing) {
				stillSince_ = -1;
				hint = "Turn around: face the way the course goes";
			} else if (speedKmh > StoppedKmh) {
				stillSince_ = -1;
				hint = "Stop behind the line";
			} else {
				if (stillSince_ < 0) {
					stillSince_ = now;
				}
				if (ExternalStart) {
					ReadyToStart = now - stillSince_ >= StillSeconds;
					hint = ReadyToStart ? "Ready" : "Hold still...";
				} else if (now - stillSince_ >= StillSeconds) {
					State = Phase.Countdown;
					countdownStart_ = now;
					anchor_ = position;
					CountdownNumber = 3;
					Message = "Get ready";
					return;
				} else {
					hint = "Hold still...";
				}
			}
			Message = now < jumpUntil_ ? "Jump start! Stop behind the line to try again" : hint;
		}

		/// <summary>Races: lined up when stopped in the car's own grid spot, facing the way the grid faces.</summary>
		private void WaitInSpot(double now, Vector3 position, Vector3 forward, float speedKmh, GridSpot spot) {
			lastSeen_ = position;
			lastPosition_ = position;
			ReadyToStart = false;
			float away = Vector3.Distance(new Vector3(position.x, spot.Position.y, position.z), spot.Position);
			DistanceToStart = away;
			bool facing = Vector3.Dot(CourseGeometry.Level(forward), spot.Forward) > 0.5f;
			string hint;
			if (away > SpotRadius || Mathf.Abs(position.y - spot.Position.y) > GridHeight) {
				stillSince_ = -1;
				hint = $"Drive to your grid spot: {Distance(away)}";
			} else if (!facing) {
				stillSince_ = -1;
				hint = "Turn around: face the way the grid faces";
			} else if (speedKmh > StoppedKmh) {
				stillSince_ = -1;
				hint = "Stop in your spot";
			} else {
				if (stillSince_ < 0) {
					stillSince_ = now;
				}
				ReadyToStart = now - stillSince_ >= StillSeconds;
				hint = ReadyToStart ? "Ready" : "Hold still...";
			}
			Message = hint;
		}

		/// <summary>Flying start: the clock starts at the moment the car crosses the start line (worked out between frames).</summary>
		private void WaitForFlyingStart(double now, Vector3 position, float speedKmh) {
			DistanceToStart = Vector3.Distance(position, Course.Start.Center);
			if (hasLast_ && Vector3.Distance(position, lastPosition_) <= ResetMetres &&
				CourseGeometry.Crossed(Course.Start, lastPosition_, position, out float fraction)) {
				goTime_ = lastTime_ + fraction * (now - lastTime_);
				BeginRunning(now, position, speedKmh);
				return;
			}
			hasLast_ = true;
			lastPosition_ = position;
			lastTime_ = now;
			Message = DistanceToStart > 60f
				? $"Drive through the start line to start the clock: {Distance(DistanceToStart)}"
				: "Drive through the start line to start the clock";
		}

		private void CountDown(double now, Vector3 position, float speedKmh) {
			bool moved = speedKmh > JumpKmh || Vector3.Distance(position, anchor_) > JumpMetres;
			if (moved && OnJump == JumpRule.Disqualify) {
				JumpStarts++;
				Cancel("Jump start: disqualified.");
				return;
			}
			if (moved && OnJump == JumpRule.Penalty) {
				// The countdown carries on; the penalty goes on the time at the finish.
				if (!jumped_) {
					jumped_ = true;
					JumpStarts++;
				}
				moved = false;
			}
			if (moved) {
				State = Phase.ToStart;
				stillSince_ = -1;
				CountdownNumber = 0;
				jumpUntil_ = now + 3;
				JumpStarts++;
				Message = "Jump start! Stop behind the line to try again";
				return;
			}

			if (now < countdownStart_) {
				// A battle's countdown starts a moment after both are ready, so it reaches both players in time.
				CountdownNumber = 0;
				Message = "Get ready";
				return;
			}

			double left = CountdownSeconds - (now - countdownStart_);
			if (left > 0) {
				CountdownNumber = Mathf.Clamp((int)Math.Ceiling(left), 1, 3);
				Message = jumped_ ? "Jump start! Time penalty" : "Get ready";
				return;
			}

			goTime_ = countdownStart_ + CountdownSeconds;
			BeginRunning(now, position, speedKmh);
		}

		private void BeginRunning(double now, Vector3 position, float speedKmh) {
			State = Phase.Running;
			CountdownNumber = 0;
			Elapsed = now - goTime_;
			lastPosition_ = position;
			lastTime_ = now;
			lastSpeed_ = speedKmh;
			PathPosition at = CourseGeometry.Project(Course.Path, Course.Distances, position, 0, LookAhead);
			segment_ = at.Segment;
			lastProgress_ = Progress = at.Progress;
			offSince_ = -1;
			offRoadSince_ = -1;
			Lap = 1;
			BeginLap(0);
			// Well behind the line (the back of a race grid): nothing counts until the car crosses it.
			BeforeLine = !Flying && Course.Start.Ahead(position) < (ClockFromLine ? 0f : -GridDepth);
			if (BeforeLine) {
				lastProgress_ = Progress = 0f;
				segment_ = 0;
			}
			Message = "";
		}

		/// <summary>A new lap starting <paramref name="at"/> seconds after GO.</summary>
		private void BeginLap(double at) {
			lapStart_ = at;
			refSegment_ = 0;
			lineRefTime_ = refLine_ != null ? 0 : -1;
			LapValid = true;
			LapInvalid = "";
			Sector = 0;
			splits_.Clear();
			trace_.Clear();
			trace_.Add(0f);
			trapsThisLap_.Clear();
			line_.Clear();
			lineKmh_.Clear();
		}
	}
}
