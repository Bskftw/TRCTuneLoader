// A message between the two TRC Tune Loader users in a battle: its kinds and fields, and plain player names. Its bytes never change between versions.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace TRC.TuneLoader {
	public enum BattleKind : byte {
		/// <summary>Host to guest: a challenge, with the course (its data too, unless it's an official course the guest has).</summary>
		Challenge = 1,

		/// <summary>Guest to host: accepted or declined.</summary>
		Answer = 2,

		/// <summary>Both ways, several times a second: clocks, ready, tune set, progress and run results.</summary>
		Heartbeat = 3,

		/// <summary>Host to guest: GO is at this moment (host's clock).</summary>
		Countdown = 4,

		/// <summary>Either way: the countdown is off (a jump start, or a car left its start box).</summary>
		Abort = 5,

		/// <summary>Either way: leaving the battle.</summary>
		Leave = 6,

		/// <summary>Guest to host, for every copy of a challenge: it got here, and whether it can be accepted (Reason says why not).</summary>
		Seen = 7,
	}

	/// <summary>
	/// A message between the two TRC Tune Loader users in a battle. Every message goes to the whole room (KSL's
	/// channel can't be aimed reliably), so each says who it's from and for, and the other players ignore it.
	/// </summary>
	public sealed partial class BattleMessage {
		/// <summary>2 since 0.10.1: small pieces, the path packed in centimetres, and Seen. 3 since 0.11.0: laps, and circuit courses. 4 since 0.12.0: tournament matches. 5 since 0.13.0: positions and SP (highway battles).</summary>
		public const byte Protocol = 5;

		/// <summary>
		/// A speed trap challenge (0.15.1) goes out as protocol 6, so a 0.15.0 copy, which doesn't know the format,
		/// never sees it. Everything else stays 5; the trap totals ride at the end of a heartbeat, where 0.15.0 stops reading.
		/// </summary>
		public const byte TrapProtocol = 6;

		/// <summary>
		/// A challenge run to a regulation (0.16) goes out as protocol 7, so older copies (which can't check one) never
		/// see it. The fun flag and the regulation ride at the end of a challenge; the car's rule and Elo at the end of a heartbeat.
		/// </summary>
		public const byte RuleProtocol = 7;
		private const int MaxRegulationText = 12000;

		/// <summary>The most traps a speed trap challenge carries.</summary>
		public const int MaxTraps = 64;

		public BattleKind Kind;
		public uint BattleId;
		public ulong From;
		public ulong To;

		// Challenge
		public string FromName = "";
		public BattleFormat Format;
		public bool Open;

		/// <summary>Laps per run, on a circuit.</summary>
		public int Laps = 1;

		/// <summary>A knockout tournament's battle (0 = an ordinary challenge), and which game of the match: accepted by itself.</summary>
		public uint MatchId;
		public int MatchGame;
		public string CourseCode = "";

		/// <summary>The course itself, sent when the guest may not have it (anything but an official course).</summary>
		public Course Course;

		/// <summary>A speed trap battle's traps (the course's own, or the free-roam ones along it), in driving order.</summary>
		public List<CourseGate> Traps = new List<CourseGate>();

		/// <summary>0.16: just for fun (no ratings change), and the regulation it's run to (null: TRC spec or Open).</summary>
		public bool Fun;
		public Regulation Regulation;

		/// <summary>Side by side won't fit on the road at the start: the drivers line up one behind the other, each timed from the line.</summary>
		public bool SingleFile;

		// Answer, Abort and Leave
		public bool Accept;
		public string Reason = "";

		// Heartbeat
		/// <summary>The sender's clock when sending, and the last of ours it heard with how long it held it: for the clock sync.</summary>
		public double SentAt;
		public double Echo;
		public double EchoHold;
		public bool Ready;
		public bool Spec;
		public string TuneSet = "";
		public string TuneCode = "";
		public string Car = "";
		public float Progress;
		public int RunIndex;

		/// <summary>The sender's battle phase (BattleSession.Phase), so a lost countdown or abort can be noticed.</summary>
		public byte Phase;

		/// <summary>The sender's latest run result (any run), so it still arrives if the other side moved on.</summary>
		public RunResult Result;

		/// <summary>Where the sender's car is, the way it points and its speed (km/h), and (highway battles) its SP.</summary>
		public Vector3 Position;
		public Vector3 Heading;
		public float Kmh;
		public float Sp;

		/// <summary>The sender's trap total so far in this run (speed trap battles), for the live standings.</summary>
		public float TrapTotal;

		/// <summary>0.16: the sender's car meets the battle's regulation, and their Elo for this battle's ratings.</summary>
		public bool HasRuleOk;
		public bool RuleOk;
		public float Elo;

		// Countdown and Abort
		public int CountdownId;
		public double GoHostTime;

		private static string Clip(string text) => MessageText.Clip(text, MaxText);

		private static readonly Regex RichText = new Regex(
			@"</?(color|b|i|u|s|size|material|quad|font|mark|sup|sub|alpha|cspace|mspace|voffset|link|style|noparse|gradient|lowercase|uppercase|smallcaps|nobr|rotate|width|pos|indent|margin|align|line-height)(=[^<>]*)?>|<#[0-9a-fA-F]{3,8}>|</>|\[[0-9a-fA-F]{6}([0-9a-fA-F]{2})?\]|\[-\]",
			RegexOptions.IgnoreCase | RegexOptions.Compiled);

		/// <summary>
		/// A player name without its colour and style tags (CarX names can colour every letter, which makes them
		/// too long to send whole and breaks when cut).
		/// </summary>
		/// <remarks>
		/// Oct 29: a name showed cut off, with its codes. KINO colours names as "[00D928]Crazy Frog" (seen in its
		/// log), and gradient names colour every letter, so this also takes [RRGGBB] and [-] codes, "&lt;#00FF00&gt;"
		/// tags, and a tag or code left unfinished at the end.
		/// </remarks>
		public static string PlainName(string name) => UnfinishedTag.Replace(RichText.Replace(name ?? "", ""), "").Trim();

		private static readonly Regex UnfinishedTag = new Regex(@"</?[#a-zA-Z][^<>]*$|\[[0-9a-fA-F]{0,7}$", RegexOptions.Compiled);
	}
}
