// One side of a 1v1 battle (the host is the challenger): its state, challenging, answering and leaving. A driver's car as their own mod checked it.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>A driver's car as their own mod checked it, for a battle.</summary>
	public sealed class BattleCar {
		public bool Spec;
		public string TuneSet = "";
		public string TuneCode = "";
		public string Car = "";

		/// <summary>0.16: the car meets the regulation of the battle or race being driven (false when there's none).</summary>
		public bool RuleOk;
	}

	/// <summary>
	/// One battle between two TRC Tune Loader users, as one of them sees it. Both run the same logic on their
	/// own PC and talk through <see cref="BattleMessage"/>s; the mod passes messages in and out and feeds the car.
	///
	///  1. The host challenges until the guest's mod says it got the challenge (Seen); the guest accepts (both on
	///     the same course, the same map). Two drivers who challenge each other at once to the same battle get
	///     one battle (<see cref="YieldsTo"/>).
	///  2. Both drive to their start box (side by side for a race; the leader in front for cat and mouse) and
	///     stop. Each mod reports ready; in a TRC spec battle only with a TRC spec car, both on the same set.
	///  3. The host picks GO a moment ahead on its clock; the guest turns it into its own clock with the clock
	///     sync the heartbeats keep up, so 3-2-1-GO lands at the same moment on both screens. Moving before GO
	///     is a jump start: the countdown is called off for both.
	///  4. Each mod times its own car from its own GO to the finish (TimeAttackRun: shortcuts, leaving the road
	///     and resets stop it), and sends its result. With both results, both decide the run the same way
	///     (BattleRules): a winner ends the battle, otherwise they line up again.
	///
	/// A driver who stops answering for <see cref="TimeoutSeconds"/> has left. Nothing here knows about CarX or
	/// KSL, so the self-test can play a whole battle between two simulated players.
	/// </summary>
	public sealed partial class BattleSession {
		public enum Phase : byte { Inviting, Invited, Gridding, Countdown, Running, Finished, Ended }

		private const double HeartbeatSeconds = 0.2;
		public const double TimeoutSeconds = 10;
		private const double InviteSeconds = 45;

		/// <summary>A challenge that carries its course (dozens of packets for a long one) gets longer to arrive.</summary>
		private const double InviteWithCourseSeconds = 90;
		private const double ChallengeResendSeconds = 3;

		/// <summary>After this long without a Seen, the challenger is told it hasn't got there yet.</summary>
		private const double NotSeenSeconds = 6;

		/// <summary>Two challenges are the same battle when their lines are this close (each driver may have recorded their own path).</summary>
		private const float SameLineMetres = 1f;

		/// <summary>Both must stay ready this long before the host starts the countdown.</summary>
		private const double ReadyHoldSeconds = 1;

		/// <summary>The countdown starts this long after the host calls it, so the message reaches the guest first.</summary>
		private const double CountdownLeadSeconds = 1;

		/// <summary>If GO passed this long ago and the other side never started, the countdown is called off (a lost message).</summary>
		private const double MissedGoSeconds = 3;

		/// <summary>The host sends the countdown again this often until the other side's heartbeat shows it arrived.</summary>
		private const double CountdownResendSeconds = 0.3;

		/// <summary>How long to wait for the other driver's result once one is in.</summary>
		private const double ResultWaitSeconds = 180;

		/// <summary>
		/// After this many runs without a winner, the battle is a draw. Cat and mouse goes on "until someone wins"
		/// (the lead needed shrinks to 0.5 s), so this is only a backstop.
		/// </summary>
		public const int MaxRuns = 10;

		/// <summary>
		/// Start boxes, in metres behind the start line. In cat and mouse the leader's box ends where the chaser's
		/// begins (TRC, Oct 24: the chaser lines up from 5 m back), so the chaser can never be in front.
		/// </summary>
		private const float RaceBoxBack = 12f;
		private const float LeaderBoxBack = 5f;
		private const float ChaserBoxFront = 5f;
		private const float ChaserBoxBack = 30f;

		/// <summary>Teleport spots: metres behind the start line, and for a race how far to each side of the middle.</summary>
		public const float FrontSpot = 4f;
		public const float LeaderSpot = 2.5f;
		public const float ChaserSpot = 10f;

		private const int OffsetSamples = 15;

		public uint Id { get; }
		public bool IsHost { get; }
		public ulong Me { get; }
		public ulong Peer { get; }
		public string PeerName { get; }
		public BattleFormat Format { get; }

		/// <summary>Laps per run on a circuit (1 on any other course).</summary>
		public int Laps { get; }

		/// <summary>Highway battles: both drivers' SP (100 at the start; empty loses), and where the other car is.</summary>
		public float MySp { get; private set; } = FullSp;
		public float PeerSp { get; private set; } = FullSp;
		public Vector3 PeerPosition { get; private set; }
		public Vector3 PeerHeading { get; private set; }
		public float PeerKmh { get; private set; }
		public bool IsHighway => Format == BattleFormat.Highway;

		/// <summary>The gap (metres) to the other car along the way you're both going: plus when you're ahead.</summary>
		public float HighwayGap { get; private set; }

		public const float FullSp = 100f;

		/// <summary>Highway battles start rolling: both this close and at least this fast, for a moment.</summary>
		public const float HighwayStartMetres = 30f;
		public const float HighwayStartKmh = 50f;

		/// <summary>Behind by more than this (metres), SP drains; faster the bigger the gap, up to a limit. Further than the last, it's over.</summary>
		public const float DrainFromMetres = 5f;
		public const float DrainMetresPerSp = 8f;
		public const float MaxDrainPerSecond = 25f;
		public const float LostMetres = 600f;

		/// <summary>A hit (a wall or the other car) costs this much SP.</summary>
		public const float HitSp = 15f;
		private const double HighwayMaxSeconds = 600;

		private Vector3 myPosition_;
		private Vector3 myForward_;
		private float myKmh_;
		private double lastHighwayTick_ = -1;

		/// <summary>Highway battles: the mod saw this car hit something.</summary>
		public void Hit() {
			if (IsHighway && State == Phase.Running) {
				MySp = Math.Max(0f, MySp - HitSp);
			}
		}

		/// <summary>A knockout tournament's battle (0 = an ordinary one), and which game of the match.</summary>
		public uint MatchId { get; private set; }
		public int MatchGame { get; private set; }

		/// <summary>An Open battle: cars don't have to be TRC spec, and it never counts (unless it's run to a regulation).</summary>
		public bool Open { get; }
		public Course Course { get; }

		/// <summary>0.16: run to this regulation (each mod checks its own car against it), or null. Such a battle is Open too.</summary>
		public Regulation Regulation { get; private set; }

		/// <summary>0.16: just for fun: no ratings change.</summary>
		public bool Fun { get; private set; }

		/// <summary>
		/// The road at the start is too narrow for side by side (the challenger's mod measured it): the challenger lines
		/// up in front, the other driver behind, and each clock starts at the start line (cat and mouse is unchanged).
		/// </summary>
		public bool SingleFile { get; private set; }

		private bool LinedUp => SingleFile && Format != BattleFormat.CatAndMouse && !IsHighway;

		public bool IsTrcSpec => !Open && Regulation == null;

		/// <summary>This driver's Elo for the battle's ratings (the mod sets it), and the other driver's, from their heartbeats (0 until heard).</summary>
		public float MyElo { get; set; }
		public float PeerElo { get; private set; }

		/// <summary>The car may line up: TRC spec in a TRC spec battle, meeting the regulation in a regulated one, anything when Open.</summary>
		private string NotAllowedMessage => Regulation != null ? $"Your car doesn't meet the battle's regulation ({Regulation.Name}): see Regulations"
			: "Your car isn't TRC spec, and this is a TRC spec battle";

		public bool CarOk(BattleCar car) => car != null && (Regulation != null ? car.RuleOk : Open || car.Spec);

		public Phase State { get; private set; }

		/// <summary>What to tell the player right now.</summary>
		public string Message { get; private set; } = "";
		public int RunIndex { get; private set; }

		/// <summary>Cat and mouse: who leads the current run.</summary>
		public ulong Leader => LeaderFor(RunIndex);
		public bool IAmLeader => Leader == Me;

		/// <summary>This driver's own run (the current one).</summary>
		public TimeAttackRun Run { get; private set; }
		public List<BattleRun> Runs { get; } = new List<BattleRun>();

		/// <summary>Who won (0 = nobody: a draw, or the battle ended early).</summary>
		public ulong Winner { get; private set; }

		/// <summary>The battle ended because the other driver left or stopped answering.</summary>
		public bool PeerLeft { get; private set; }

		/// <summary>The battle ended because this driver left it.</summary>
		public bool ILeft { get; private set; }

		/// <summary>The challenge got to the other driver's mod (host only).</summary>
		public bool PeerSeen { get; private set; }

		/// <summary>Why the other driver can't accept the challenge, when their mod said so (host only).</summary>
		public string PeerProblem { get; private set; } = "";

		public bool MyReady { get; private set; }
		public bool PeerReady { get; private set; }
		public BattleCar MyCar { get; private set; }
		public BattleCar PeerCar { get; private set; } = new BattleCar();

		/// <summary>The other driver's run stopped before the finish (and why): this driver only has to finish to win it. "" otherwise.</summary>
		public string PeerOut { get; private set; } = "";

		/// <summary>The other driver's metres along the course in the current run, as of their last heartbeat.</summary>
		public float PeerProgress { get; private set; }

		/// <summary>The other driver's trap total so far in this run (speed trap battles).</summary>
		public float PeerTrapTotal { get; private set; }

		public bool IsSpeedTraps => Format == BattleFormat.SpeedTraps;

		/// <summary>
		/// Where the other driver is now: their last progress moved on by their speed for as long as it took to
		/// get here, so it can be compared with this driver's live position (heartbeats come 5 times a second,
		/// late by the connection).
		/// </summary>
		public float PeerProgressAt(double now) {
			if (peerProgressAt_ < 0 || offsets_.Count == 0) {
				return PeerProgress;
			}
			double age = Math.Max(0, Math.Min(1, now - (peerProgressAt_ - ClockOffset)));
			return PeerProgress + (float)(peerSpeed_ * age);
		}

		/// <summary>A TRC spec battle where both cars were TRC spec at every start and finish.</summary>
		public bool Counted { get; private set; }

		/// <summary>The other driver's clock minus this one's, in seconds (from the heartbeats).</summary>
		public double ClockOffset { get; private set; }

		/// <summary>Time for a message to go there and back, in seconds.</summary>
		public double RoundTrip { get; private set; }

		/// <summary>The battle is still going (not finished or ended).</summary>
		public bool Active => State != Phase.Finished && State != Phase.Ended;

		/// <summary>Both drivers agreed and lined up at least once: worth saving to the battle log.</summary>
		public bool Started { get; private set; }

		private readonly Func<BattleCar> checkMyCar_;
		private readonly List<BattleMessage> outbox_ = new List<BattleMessage>();
		private readonly List<double> offsets_ = new List<double>();
		private readonly string myName_;
		private readonly double createdAt_;
		private double lastHeartbeat_ = double.MinValue;
		private double lastHeard_;
		private double lastChallengeSent_;
		private double peerSentAt_;
		private double peerSentHeardAt_ = -1;
		private int countdownId_;
		private double goTime_;
		private double bothReadySince_ = -1;
		private RunResult myResult_;
		private RunResult peerResult_;
		private RunResult lastResult_;
		private double firstResultAt_ = -1;
		private double endedAt_ = -1;
		private Phase peerPhase_;
		private int peerRunIndex_ = -1;
		/// <summary>When the other driver measured <see cref="PeerProgress"/>, on their clock.</summary>
		private double peerProgressAt_ = -1;
		private double peerSpeed_;
		private double lastCountdownSent_ = -1;

		/// <summary>Highway battles: the other driver's heartbeats showed them running this run too (a result only counts then).</summary>
		private bool peerRan_;

		private BattleSession(uint id, bool host, ulong me, string myName, ulong peer, string peerName, Course course,
			BattleFormat format, bool open, int laps, Func<BattleCar> checkMyCar, double now) {
			Id = id;
			IsHost = host;
			Me = me;
			myName_ = BattleMessage.PlainName(myName);
			Peer = peer;
			peerName = BattleMessage.PlainName(peerName);
			PeerName = peerName.Length == 0 ? "the other driver" : peerName;
			Course = course;
			Format = format;
			Laps = course != null && course.IsCircuit ? Math.Max(1, laps) : 1;
			Open = open;
			Counted = !open;
			checkMyCar_ = checkMyCar;
			createdAt_ = now;
			lastHeard_ = now;
		}

		/// <summary>Challenges <paramref name="peer"/>. The course goes with the challenge unless it's official (they have it too).</summary>
		public static BattleSession Challenge(uint id, ulong me, string myName, ulong peer, string peerName, Course course,
			BattleFormat format, bool open, Func<BattleCar> checkMyCar, double now, int laps = 1, uint matchId = 0, int matchGame = 0,
			Regulation regulation = null, bool fun = false) {
			var session = new BattleSession(id, true, me, myName, peer, peerName, course, format, open || regulation != null, laps, checkMyCar, now) {
				State = Phase.Inviting,
				MatchId = matchId,
				MatchGame = matchGame,
				Regulation = regulation,
				Fun = fun,
				SingleFile = course != null && course.SingleFile,
			};
			session.Counted = regulation != null || !open;
			session.SendChallenge(now);
			session.Message = $"Sending the challenge to {session.PeerName}";
			return session;
		}

		/// <summary>A challenge someone sent this player, with the course it's on (found here, or sent with it).</summary>
		public static BattleSession FromChallenge(BattleMessage challenge, ulong me, string myName, Course course, Func<BattleCar> checkMyCar, double now) {
			var session = new BattleSession(challenge.BattleId, false, me, myName, challenge.From, challenge.FromName, course,
				challenge.Format, challenge.Open || challenge.Regulation != null, challenge.Laps, checkMyCar, now) {
				State = Phase.Invited,
				MatchId = challenge.MatchId,
				MatchGame = challenge.MatchGame,
				Regulation = challenge.Regulation,
				Fun = challenge.Fun,
				SingleFile = challenge.SingleFile,
			};
			session.Counted = challenge.Regulation != null || !challenge.Open;
			session.Message = $"{session.PeerName} challenges you";
			return session;
		}

		/// <summary>Messages to send now (taken out of the session).</summary>
		public List<BattleMessage> TakeOutbox() {
			var messages = new List<BattleMessage>(outbox_);
			outbox_.Clear();
			return messages;
		}

		public void Accept(double now) {
			if (State != Phase.Invited) {
				return;
			}
			BattleMessage answer = New(BattleKind.Answer);
			answer.Accept = true;
			outbox_.Add(answer);
			BeginGridding(now);
		}

		public void Decline(string reason, double now) {
			if (State != Phase.Invited) {
				return;
			}
			BattleMessage answer = New(BattleKind.Answer);
			answer.Reason = reason ?? "";
			outbox_.Add(answer);
			End(Phase.Ended, "You declined", now);
		}

		/// <summary>Leaves the battle (or takes back a challenge).</summary>
		public void Leave(double now) {
			if (!Active) {
				return;
			}
			BattleMessage leave = New(BattleKind.Leave);
			outbox_.Add(leave);
			ILeft = Started;
			End(Phase.Ended, State == Phase.Inviting ? "You took the challenge back" : "You left the battle", now);
		}

		private void End(Phase phase, string message, double now) {
			State = phase;
			Message = message;
			endedAt_ = now;
			MyReady = false;
		}

		private BattleMessage New(BattleKind kind) => new BattleMessage { Kind = kind, BattleId = Id, From = Me, To = Peer };

		/// <summary>The leader of a run: the first run's leader is picked by the battle ID (so both sides agree), then they swap.</summary>
		private ulong LeaderFor(int run) {
			ulong host = IsHost ? Me : Peer;
			ulong guest = IsHost ? Peer : Me;
			ulong first = (Id & 1) == 0 ? host : guest;
			return run % 2 == 0 ? first : (first == host ? guest : host);
		}
	}
}
