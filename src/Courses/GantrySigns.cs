// What the start and finish gantries' signs show as a run goes: STAGE START, the lamps, laps, checkpoints, STAGE CLEAR.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		private TimeAttackRun signRun_;
		private TimeAttackRun.Phase signPhase_;
		private float signTopKmh_;
		private GantrySign? signClear_;

		/// <summary>Set by FinishRun for the finish sign: the time's place among this car's times on the course, and whether it's a new best.</summary>
		private int signRank_;
		private bool signBest_;

		/// <summary>This lap's sector splits, by sector, for the checkpoint signs.</summary>
		private readonly Dictionary<int, SectorSplit> checkpointSplits_ = new Dictionary<int, SectorSplit>();
		private int checkpointLap_;

		/// <summary>
		/// The start sign follows the countdown (lamps at 3, 2, 1, GO), a circuit's shows the lap, and the finish sign
		/// shows STAGE CLEAR with the time, top speed and rank once this driver finishes, until the next go.
		/// </summary>
		private void UpdateGantrySigns(Course course, TimeAttackRun run) {
			if (run != signRun_) {
				signRun_ = run;
				signPhase_ = run?.State ?? TimeAttackRun.Phase.Cancelled;
				signClear_ = null;
				signTopKmh_ = 0f;
				checkpointSplits_.Clear();
			}
			if (run != null) {
				// The checkpoints show this lap's splits: a new go or a new lap starts them again.
				if (run.State != TimeAttackRun.Phase.Running && run.State != TimeAttackRun.Phase.Finished || run.Lap != checkpointLap_) {
					if (run.State != TimeAttackRun.Phase.Finished) {
						checkpointSplits_.Clear();
					}
					checkpointLap_ = run.Lap;
				}
				if (run.State == TimeAttackRun.Phase.Running && run.LastSplit is SectorSplit split && split.Lap == run.Lap) {
					if (!checkpointSplits_.TryGetValue(split.Index, out SectorSplit seen) || seen.Time != split.Time) {
						FlashSector(run, split);
					}
					checkpointSplits_[split.Index] = split;
				}
			}
			markers_.ShowCheckpointSigns(CheckpointSign);
			if (run != null) {
				TimeAttackRun.Phase phase = run.State;
				if (phase == TimeAttackRun.Phase.Countdown && signPhase_ != TimeAttackRun.Phase.Countdown) {
					signClear_ = null;
					signTopKmh_ = 0f;
				}
				if (phase == TimeAttackRun.Phase.Running && !run.BeforeLine && hasPose_) {
					signTopKmh_ = Mathf.Max(signTopKmh_, Mathf.Abs(pose_.SpeedKmh));
				}
				if (signPhase_ == TimeAttackRun.Phase.Running && phase == TimeAttackRun.Phase.Finished) {
					string rank = "";
					bool best = false;
					if (run == run_) {
						rank = signRank_ > 0 ? $"#{signRank_}" : "";
						best = signBest_;
					} else if (InRace && run == race_.Run) {
						RaceStanding me = race_.Standings(BattleNow).FirstOrDefault(s => s.Driver.Id == race_.Me);
						rank = me != null ? $"#{me.Position}" : "";
					}
					signClear_ = new GantrySign {
						Kind = GantrySign.Page.Clear, Text = TimeAttackRun.FormatTime(run.Result),
						TopSpeed = signTopKmh_ > 0f ? $"{signTopKmh_:0} KM/H" : "", Rank = rank, PersonalBest = best,
					};
					// The same moment on screen: FINISH, big, like GO at the start (bskftw, Nov 3).
					string place = InRace && run == race_.Run && rank.Length > 0 ? $"  ·  P{rank.TrimStart('#')}" : "";
					finishFlash_ = $"<b>{TimeAttackRun.FormatTime(run.Result)}</b>{place}{(best ? $"  ·  <color={Theme.PurpleHex}><b>new PB</b></color>" : "")}";
					finishFlashUntil_ = Time.unscaledTime + FinishFlashSeconds;
				}
				signPhase_ = phase;
			}

			var finishSign = signClear_ ?? new GantrySign { Kind = GantrySign.Page.Finish, Text = course.Name };
			GantrySign startSign;
			if (course.IsCircuit && signClear_ != null) {
				startSign = signClear_.Value;
			} else if (course.IsCircuit && run != null && run.State == TimeAttackRun.Phase.Running && !run.BeforeLine && (run.Flying || run.Elapsed >= 1.5)) {
				LapTime last = run.LapTimes.Count > 0 ? run.LapTimes[run.LapTimes.Count - 1] : null;
				startSign = new GantrySign { Kind = GantrySign.Page.Lap, Step = run.Lap, Laps = run.Laps, Text = last != null ? TimeAttackRun.FormatTime(last.Time) : "" };
			} else {
				int step = run == null ? SignPages.Idle
					: run.State == TimeAttackRun.Phase.Countdown ? Mathf.Clamp(run.CountdownNumber, 0, 3)
					: run.State == TimeAttackRun.Phase.Running && !run.Flying && run.Elapsed < 1.5 ? SignPages.Go
					: SignPages.Idle;
				startSign = new GantrySign { Kind = GantrySign.Page.Start, Step = step };
			}
			markers_.ShowSigns(startSign, finishSign);
		}

		/// <summary>
		/// Checkpoint <paramref name="index"/>'s sign: the gap to the PB when it was passed this lap (or the sector's time
		/// without a PB to compare with, as in races and battles), else dashes.
		/// </summary>
		private GantrySign CheckpointSign(int index) {
			var sign = new GantrySign { Kind = GantrySign.Page.Checkpoint, Step = index + 1, Text = "", Under = "" };
			if (!checkpointSplits_.TryGetValue(index, out SectorSplit split)) {
				return sign;
			}
			if (split.Delta is double delta) {
				double rounded = Math.Round(delta, 3);
				sign.Text = (rounded < 0 ? "-" : "+") + Math.Abs(rounded).ToString("0.000", CultureInfo.InvariantCulture);
				sign.Under = rounded < 0 ? "AHEAD OF PB" : rounded > 0 ? "BEHIND PB" : "LEVEL WITH PB";
				sign.Behind = rounded > 0;
			} else {
				sign.Text = TimeAttackRun.FormatTime(split.Time);
				sign.Under = $"SECTOR {index + 1} TIME";
			}
			return sign;
		}
	}
}
