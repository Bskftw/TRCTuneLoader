using System;

namespace TRC.TuneLoader {
	/// <summary>How one run of a battle ended. Both mods work it out from the same two results, so they always agree.</summary>
	public sealed class BattleRun {
		public int Index;

		/// <summary>Cat and mouse: who led this run.</summary>
		public ulong Leader;
		public RunResult Mine;
		public RunResult Theirs;

		/// <summary>Who won the run (0 = nobody: run again).</summary>
		public ulong Winner;
		public string Text = "";
	}

	/// <summary>
	/// The battle rules, as TRC decided them. Times are each driver's own, from their own GO (both GOs are at the
	/// same moment), to the finish line.
	///
	/// Side-by-side race: the faster time wins. Closer than <see cref="DeadHeatSeconds"/> is a dead heat, run again.
	///
	/// Cat and mouse: the chaser wins the run by passing the leader and staying ahead to the finish (finishing
	/// first). The leader wins by finishing <see cref="LeaderGapFor"/> or more ahead: 1.5 s while both drivers
	/// have their first lead, 1 s for their second, then 0.5 s (TRC, Oct 23). Anything in between has no winner:
	/// swap places and go again. The first run with a winner wins the battle.
	///
	/// In both, a driver who doesn't finish (shortcut, off the road, reset...) loses the run to one who does.
	/// </summary>
	public static class BattleRules {
		public const double DeadHeatSeconds = 0.1;

		/// <summary>The chaser must finish at least this much ahead to have passed: closer is too close to call over the network.</summary>
		public const double PassMarginSeconds = 0.05;
		public const double LeaderGapSeconds = 1.5;
		private const double LeaderGapStep = 0.5;
		private const double LeaderGapMin = 0.5;

		/// <summary>The lead the leader needs in run <paramref name="index"/> (0-based): less after each pair of runs without a winner.</summary>
		public static double LeaderGapFor(int index) => Math.Max(LeaderGapMin, LeaderGapSeconds - LeaderGapStep * (index / 2));

		public static BattleRun Decide(BattleFormat format, int index, ulong leader, ulong me, ulong peer, string peerName, RunResult mine, RunResult theirs) {
			var run = new BattleRun { Index = index, Leader = leader, Mine = mine, Theirs = theirs };
			string Name(ulong id) => id == me ? "You" : peerName;

			if (format == BattleFormat.Race) {
				if (mine.Finished && theirs.Finished) {
					double gap = Math.Abs(mine.Time - theirs.Time);
					if (gap < DeadHeatSeconds) {
						run.Text = $"Dead heat ({TimeAttackRun.FormatTime(gap)} s): run it again";
					} else {
						run.Winner = mine.Time < theirs.Time ? me : peer;
						run.Text = run.Winner == me ? $"You win by {TimeAttackRun.FormatTime(gap)} s" : $"{peerName} wins by {TimeAttackRun.FormatTime(gap)} s";
					}
				} else if (mine.Finished) {
					run.Winner = me;
					run.Text = $"You win: {peerName} didn't finish ({theirs.Reason.TrimEnd('.')})";
				} else if (theirs.Finished) {
					run.Winner = peer;
					run.Text = $"{peerName} wins: you didn't finish ({mine.Reason.TrimEnd('.')})";
				} else {
					run.Text = "Neither of you finished: run it again";
				}
				return run;
			}

			bool iLead = leader == me;
			RunResult led = iLead ? mine : theirs;
			RunResult chased = iLead ? theirs : mine;
			ulong chaser = iLead ? peer : me;

			if (led.Finished && chased.Finished) {
				double gap = chased.Time - led.Time;
				double needed = LeaderGapFor(index);
				if (gap < -PassMarginSeconds) {
					run.Winner = chaser;
					run.Text = chaser == me ? "You passed and stayed ahead to the finish: you win" : $"{peerName} passed and stayed ahead to the finish: {peerName} wins";
				} else if (gap >= needed) {
					run.Winner = leader;
					run.Text = $"{Name(leader)} pulled {TimeAttackRun.FormatTime(gap)} s away: {(leader == me ? "you win" : $"{peerName} wins")}";
				} else {
					run.Text = gap < PassMarginSeconds
						? "Too close to call at the finish: no winner, swap places"
						: $"Gap {TimeAttackRun.FormatTime(gap)} s, under {needed:0.0} s: no winner, swap places";
				}
			} else if (led.Finished) {
				run.Winner = leader;
				run.Text = $"{Name(leader)} win{(leader == me ? "" : "s")}: the chaser didn't finish ({chased.Reason.TrimEnd('.')})";
			} else if (chased.Finished) {
				run.Winner = chaser;
				run.Text = $"{Name(chaser)} win{(chaser == me ? "" : "s")}: the leader didn't finish ({led.Reason.TrimEnd('.')})";
			} else {
				run.Text = "Neither of you finished: run it again";
			}
			return run;
		}
	}
}
