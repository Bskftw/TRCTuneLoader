// How many messages a sender may send each second, per sender and per channel.
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace TRC.TuneLoader {
	/// <summary>
	/// How many times a second something may happen, per key (a sender's Steam ID): a bucket of tokens for each, filled
	/// at <c>perSecond</c> up to <c>burst</c>. Used on messages from the room (IMPROVEMENT_PLAN.md N7), with limits far above
	/// anything the mod sends, so only a flood is cut. Safe from any thread (KSL may deliver packets off the main thread).
	/// Nothing here knows about the game, so the self-test can drive it with its own clock.
	/// </summary>
	public sealed class RateLimit {
		private struct Bucket {
			public double Tokens, At;
		}

		/// <summary>The bucket every key shares once there are too many keys to keep one each (a flood of made-up senders).</summary>
		private const ulong Shared = ulong.MaxValue;

		private readonly double perSecond_, burst_;
		private readonly int mostKeys_;
		private readonly Dictionary<ulong, Bucket> buckets_ = new Dictionary<ulong, Bucket>();
		private readonly List<ulong> full_ = new List<ulong>();
		private readonly object lock_ = new object();

		public int Refused { get; private set; }

		public RateLimit(double perSecond, double burst, int mostKeys = 256) {
			perSecond_ = Math.Max(0.001, perSecond);
			burst_ = Math.Max(1, burst);
			mostKeys_ = Math.Max(1, mostKeys);
		}

		/// <summary>Seconds on a clock that only goes forward, the same from any thread.</summary>
		public static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

		public bool Allow(ulong key) => Allow(key, Now);

		public bool Allow(ulong key, double now) {
			lock (lock_) {
				if (!buckets_.TryGetValue(key, out Bucket b)) {
					if (buckets_.Count >= mostKeys_) {
						Tidy(now);
					}
					if (buckets_.Count >= mostKeys_) {
						key = Shared;
						if (!buckets_.TryGetValue(key, out b)) {
							b = new Bucket { Tokens = burst_, At = now };
						}
					} else {
						b = new Bucket { Tokens = burst_, At = now };
					}
				}
				b.Tokens = Math.Min(burst_, b.Tokens + Math.Max(0, now - b.At) * perSecond_);
				b.At = now;
				bool ok = b.Tokens >= 1;
				if (ok) {
					b.Tokens -= 1;
				} else {
					Refused++;
				}
				buckets_[key] = b;
				return ok;
			}
		}

		/// <summary>Forgets the keys whose buckets would be full again by now (nothing is lost by making them anew).</summary>
		private void Tidy(double now) {
			full_.Clear();
			foreach (KeyValuePair<ulong, Bucket> pair in buckets_) {
				if (pair.Key != Shared && pair.Value.Tokens + (now - pair.Value.At) * perSecond_ >= burst_) {
					full_.Add(pair.Key);
				}
			}
			foreach (ulong key in full_) {
				buckets_.Remove(key);
			}
		}
	}

	/// <summary>
	/// The limits on one room channel: so many packets a second from each sender, and so many from everyone together
	/// (made-up senders each get their own bucket, so the whole channel has one too). What's cut is counted and said in
	/// the log at most once a minute.
	/// </summary>
	public sealed class ChannelLimit {
		/// <summary>Where a cut is reported (the mod sets it to its log); null: nowhere.</summary>
		public static Action<string> Log;

		private readonly string name_;
		private readonly RateLimit each_, all_;
		private double loggedAt_ = -1000;
		private int cut_, cutLogged_;
		private readonly object lock_ = new object();

		public int Cut => cut_;

		public ChannelLimit(string name, double eachPerSecond, double eachBurst, double allPerSecond, double allBurst) {
			name_ = name;
			each_ = new RateLimit(eachPerSecond, eachBurst);
			all_ = new RateLimit(allPerSecond, allBurst, 1);
		}

		public bool Allow(ulong sender) => Allow(sender, RateLimit.Now);

		public bool Allow(ulong sender, double now) {
			if (all_.Allow(0, now) && each_.Allow(sender, now)) {
				return true;
			}
			lock (lock_) {
				cut_++;
				if (now - loggedAt_ >= 60) {
					loggedAt_ = now;
					int since = cut_ - cutLogged_;
					cutLogged_ = cut_;
					Log?.Invoke($"Ignored {since} {name_} message(s): far more a second than the mod ever sends (the last from {sender}).");
				}
			}
			return false;
		}
	}
}
