// TRC reports between TRC Tune Loader users in a room: each player's mod list and tune checks, asked for and sent (protocol 4).
using System;
using System.Collections.Generic;
using System.Linq;
using KSL.API;

namespace TRC.TuneLoader {
	/// <summary>
	/// Lets TRC Tune Loader users in the same online room see each other's mod list and tune checks.
	///
	/// It uses only KSL's own mod-to-mod channel (Kino.Sync, carried by the KSL.CarX extension) and
	/// never touches CarX's online systems. It only works with players who also run TRC Tune Loader.
	/// What a player sends is self-reported: someone who edits the game could fake it, so treat it as
	/// a strong hint for referees, alongside replays and the shared setup check, not as proof.
	/// </summary>
	public sealed class TrcSync {
		public sealed class ModEntry {
			public string Name = "";
			public string Version = "";
			public string Author = "";
			public string Type = "";
		}

		public sealed class Report {
			public ulong SteamId;
			public string LoaderVersion = "";
			public bool ProtectedMode;
			public bool AllVerified;
			public int CarId;
			public int SlotId;
			public string SetupCode = "";
			public bool SetupOfficial;

			/// <summary>The official set the setup matches, like "TRC Revival" ("" when none).</summary>
			public string SetupSet = "";
			public int SetupDifferences;

			/// <summary>0 = not checked (no reference yet), 1 = live car matches, 2 = live car differs.</summary>
			public byte LiveStatus;
			public int LiveDifferences;

			/// <summary>How many stance values their body parts change (widebody kits); 0 = stock stance.</summary>
			public int StanceDifferences;

			/// <summary>"Stock engine" or their CarX engine swap ("" when not checked).</summary>
			public string Engine = "";

			/// <summary>RWD, FWD, AWD or 4WD ("" when not checked).</summary>
			public string Drivetrain = "";

			/// <summary>Engine, gearbox or drivetrain values changed outside CarX's tuning (KINO swaps, other mods); 0 = none.</summary>
			public int DrivetrainDifferences;
			public List<ModEntry> Mods = new List<ModEntry>();

			/// <summary>Their staff pass (Nov 19; "" when none). Signed by itself, so it rides after the seal, where older copies stop reading.</summary>
			public string StaffPass = "";
			public DateTime ReceivedAt;

			/// <summary>It came with a good seal (0.19 and newer; MessageSeal). False: an older version's report.</summary>
			public bool Signed;

			/// <summary>
			/// The player lets others see their tune's values (Options, off by default). Sent last, so reports from
			/// versions before 0.12 (which don't have it) read as not sharing, and they ignore it.
			/// </summary>
			public bool ShareValues;

			/// <summary>The player's TRC ratings (0.13 on): Elo for touge, highway and circuit, Safety Rating and license. 0 before 0.13.</summary>
			public float EloTouge;
			public float EloHighway;
			public float EloCircuit;
			public float Safety;
			public byte License;

			/// <summary>
			/// 0.17: the player specs their car is fully on (titles), and their regulations with whether the car meets each.
			/// Self-reported, like the rest: the Players page checks them itself where it has the same spec or regulation.
			/// </summary>
			public List<string> Specs = new List<string>();
			public List<RuleClaim> Regulations = new List<RuleClaim>();

			public bool HasRatings => EloTouge > 0;
			public float EloFor(CourseKind kind) => kind == CourseKind.Highway ? EloHighway : kind == CourseKind.Circuit ? EloCircuit : EloTouge;
			public string Badge => HasRatings ? $"{RatingBook.LicenseName((License)License)} {Safety:0.00}" : "";
		}

		/// <summary>One of a player's regulations in their report: its code, name, and whether their car meets it.</summary>
		public sealed class RuleClaim {
			public string Code = "";
			public string Name = "";
			public bool Ok;
		}

		/// <summary>The most specs and regulations a report carries.</summary>
		public const int MaxRules = 16;

		// 2: added the matched tune set. 3: added the stance check. 4: added the engine & drivetrain check.
		// Players on different protocol versions don't see each other's reports.
		public const byte ProtocolVersion = 4;
		private const int MaxMods = 64;
		private const int MaxText = 160;
		private static readonly TimeSpan MinReportGap = TimeSpan.FromSeconds(3);

		private readonly Func<Report> buildOwnReport_;
		private readonly object lock_ = new object();
		private readonly Dictionary<ulong, Report> reports_ = new Dictionary<ulong, Report>();

		/// <summary>
		/// Reports are answers, at most one every 3 s from each player: more than that from one sender, or a flood from made-up
		/// ones, is cut (docs/archive/IMPROVEMENT_PLAN.md N7), and no more than <see cref="MaxReports"/> are kept. A report only ever shows
		/// for a player CarX lists in the room (the Players page asks by their Steam ID), so nothing here decides who's real.
		/// </summary>
		private readonly ChannelLimit reportLimit_ = new ChannelLimit("trc_report", 2, 8, 60, 240);
		private const int MaxReports = 128;
		private int requestPacket_ = -1;
		private int reportPacket_ = -1;
		private DateTime lastSent_ = DateTime.MinValue;

		// Set when someone asks for a report. Answered from Tick() on Unity's main thread, because
		// building a report reads the game and KSL may deliver packets on another thread.
		private volatile bool reportRequested_;

		public bool IsAvailable { get; private set; }
		public string Status { get; private set; } = "Not started.";

		public TrcSync(Func<Report> buildOwnReport) {
			buildOwnReport_ = buildOwnReport;
		}

		public void Start() {
			MessageSeal.EnsureKey();
			try {
				if (!Kino.Sync.IsAvailable) {
					Status = "Sharing with other players needs KSL's KSL.CarX extension.";
					return;
				}

				if (!Kino.Sync.TryRegisterPacket("trc_request", out requestPacket_) ||
					!Kino.Sync.TryRegisterPacket("trc_report", out reportPacket_)) {
					Status = "KSL didn't accept TRC Tune Loader's sharing messages.";
					return;
				}

				Kino.Sync.SetCallback(requestPacket_, OnRequest);
				Kino.Sync.SetCallback(reportPacket_, OnReport);
				IsAvailable = true;
				Status = "Sharing with other TRC Tune Loader users in the room.";
			} catch (Exception e) {
				Status = $"Sharing isn't available: {e.Message}";
				TrcLog.Error(e);
			}
		}

		/// <summary>
		/// Forgets the reports of players no longer in the room (docs/archive/OPTIMIZATION_REPORT.md item 14: everyone ever met was kept).
		/// Only with a room list that has someone in it, so a list read while joining never wipes the reports.
		/// </summary>
		public void KeepOnly(ICollection<ulong> inRoom) {
			if (inRoom == null || inRoom.Count == 0) {
				return;
			}
			lock (lock_) {
				foreach (ulong gone in reports_.Keys.Where(id => !inRoom.Contains(id)).ToList()) {
					reports_.Remove(gone);
				}
			}
		}

		/// <summary>The latest report from a player, or null if they haven't sent one.</summary>
		public Report ReportFrom(ulong steamId) {
			lock (lock_) {
				return reports_.TryGetValue(steamId, out Report report) ? report : null;
			}
		}

		/// <summary>Asks every TRC Tune Loader user in the room for a fresh report, and sends ours.</summary>
		/// <summary>Sends this player's report to the room now (after an option that's in it changed).</summary>
		public void SendReportNow() {
			if (IsAvailable) {
				try {
					SendOwnReport(force: true);
				} catch (Exception e) {
					TrcLog.Warning($"Couldn't send the TRC report: {e.Message}");
				}
			}
		}

		public void RequestReports() {
			if (!IsAvailable) {
				return;
			}
			// Someone else asked the room in the last 10 s (docs/archive/OPTIMIZATION_REPORT.md item 12): everyone's answers to them reach this
			// PC too, and this player answers them as well (with a fresh check, as always), so asking again would only add work.
			long heard = System.Threading.Interlocked.Read(ref otherRequestTicks_);
			if (heard != 0 && DateTime.UtcNow.Ticks - heard < OtherRequestCovers.Ticks) {
				return;
			}

			try {
				IPacket packet = Kino.Sync.CreatePacket(requestPacket_);
				packet.WriteByte(ProtocolVersion);
				Kino.Sync.SendToAll(packet);
				PerfLog.Packet("trc_request", true);
				SendOwnReport(force: true);
			} catch (Exception e) {
				TrcLog.Error(e);
			}
		}

		/// <summary>When another player's request was last heard (UTC ticks; 0 = never).</summary>
		private long otherRequestTicks_;
		private static readonly TimeSpan OtherRequestCovers = TimeSpan.FromSeconds(10);

		/// <summary>Call every frame (from the mod's Update) to answer report requests.</summary>
		public void Tick() {
			if (!reportRequested_ || DateTime.UtcNow - lastSent_ < MinReportGap) {
				return;
			}

			reportRequested_ = false;
			try {
				SendOwnReport(force: true);
			} catch (Exception e) {
				TrcLog.Error(e);
			}
		}

		private void OnRequest(IPacket packet) {
			PerfLog.Packet("trc_request", false);
			try {
				if (packet.ReadByte() == ProtocolVersion) {
					reportRequested_ = true;
					System.Threading.Interlocked.Exchange(ref otherRequestTicks_, DateTime.UtcNow.Ticks);
				}
			} catch (Exception e) {
				TrcLog.Warning($"Ignored a broken TRC request: {e.Message}");
			}
		}

		private void SendOwnReport(bool force) {
			if (!force && DateTime.UtcNow - lastSent_ < MinReportGap) {
				return;
			}

			Report own = buildOwnReport_();
			if (own == null) {
				return;
			}

			IPacket packet = Kino.Sync.CreatePacket(reportPacket_);
			packet.WriteByte(ProtocolVersion);
			packet.WriteUInt64(own.SteamId);
			packet.WriteString(Clip(own.LoaderVersion));
			packet.WriteBool(own.ProtectedMode);
			packet.WriteBool(own.AllVerified);
			packet.WriteInt32(own.CarId);
			packet.WriteInt32(own.SlotId);
			packet.WriteString(Clip(own.SetupCode));
			packet.WriteBool(own.SetupOfficial);
			packet.WriteString(Clip(own.SetupSet));
			packet.WriteInt32(own.SetupDifferences);
			packet.WriteByte(own.LiveStatus);
			packet.WriteInt32(own.LiveDifferences);
			packet.WriteInt32(own.StanceDifferences);
			packet.WriteString(Clip(own.Engine));
			packet.WriteString(Clip(own.Drivetrain));
			packet.WriteInt32(own.DrivetrainDifferences);

			int count = Math.Min(own.Mods.Count, MaxMods);
			packet.WriteInt32(count);
			for (int i = 0; i < count; i++) {
				packet.WriteString(Clip(own.Mods[i].Name));
				packet.WriteString(Clip(own.Mods[i].Version));
				packet.WriteString(Clip(own.Mods[i].Author));
				packet.WriteString(Clip(own.Mods[i].Type));
			}
			packet.WriteBool(own.ShareValues);
			packet.WriteFloat(own.EloTouge);
			packet.WriteFloat(own.EloHighway);
			packet.WriteFloat(own.EloCircuit);
			packet.WriteFloat(own.Safety);
			packet.WriteByte(own.License);
			int specs = Math.Min(own.Specs.Count, MaxRules);
			packet.WriteInt32(specs);
			for (int i = 0; i < specs; i++) {
				packet.WriteString(Clip(own.Specs[i]));
			}
			int rules = Math.Min(own.Regulations.Count, MaxRules);
			packet.WriteInt32(rules);
			for (int i = 0; i < rules; i++) {
				packet.WriteString(Clip(own.Regulations[i].Code));
				packet.WriteString(Clip(own.Regulations[i].Name));
				packet.WriteBool(own.Regulations[i].Ok);
			}

			// The seal (0.19, MessageSeal), over the report as it's read back: on the end, where older copies stop reading.
			var seal = MessageSeal.Make("trc_report", ReportBody(own));
			packet.WriteInt64(seal.Ticks);
			packet.WriteUInt32(seal.Counter);
			packet.WriteBytes(seal.Tag);
			packet.WriteBytes(MessageSeal.Magic);
			packet.WriteString(own.StaffPass.Length <= TuneLoader.StaffPass.MaxLength ? own.StaffPass : "");

			Kino.Sync.SendToAll(packet);
			PerfLog.Packet("trc_report", true);
			lastSent_ = DateTime.UtcNow;
		}

		private void OnReport(IPacket packet) {
			PerfLog.Packet("trc_report", false);
			try {
				if (packet.ReadByte() != ProtocolVersion) {
					return;
				}

				ulong sender = packet.ReadUInt64();
				if (!reportLimit_.Allow(sender)) {
					return;
				}
				var report = new Report {
					SteamId = sender,
					LoaderVersion = Clip(packet.ReadString()),
					ProtectedMode = packet.ReadBool(),
					AllVerified = packet.ReadBool(),
					CarId = packet.ReadInt32(),
					SlotId = packet.ReadInt32(),
					SetupCode = Clip(packet.ReadString()),
					SetupOfficial = packet.ReadBool(),
					SetupSet = Clip(packet.ReadString()),
					SetupDifferences = packet.ReadInt32(),
					LiveStatus = packet.ReadByte(),
					LiveDifferences = packet.ReadInt32(),
					StanceDifferences = packet.ReadInt32(),
					Engine = Clip(packet.ReadString()),
					Drivetrain = Clip(packet.ReadString()),
					DrivetrainDifferences = packet.ReadInt32(),
					ReceivedAt = DateTime.Now,
				};

				int count = packet.ReadInt32();
				if (count < 0 || count > MaxMods) {
					return;
				}
				for (int i = 0; i < count; i++) {
					report.Mods.Add(new ModEntry {
						Name = Clip(packet.ReadString()),
						Version = Clip(packet.ReadString()),
						Author = Clip(packet.ReadString()),
						Type = Clip(packet.ReadString()),
					});
				}
				try {
					report.ShareValues = packet.ReadBool();
					report.EloTouge = packet.ReadFloat();
					report.EloHighway = packet.ReadFloat();
					report.EloCircuit = packet.ReadFloat();
					report.Safety = packet.ReadFloat();
					report.License = packet.ReadByte();
				} catch (Exception) {
					// A report from an older version: whatever it didn't have stays at none.
				}
				try {
					int specs = packet.ReadInt32();
					for (int i = 0; i < specs && i < MaxRules; i++) {
						report.Specs.Add(Clip(packet.ReadString()));
					}
					int rules = packet.ReadInt32();
					for (int i = 0; i < rules && i < MaxRules; i++) {
						report.Regulations.Add(new RuleClaim { Code = Clip(packet.ReadString()), Name = Clip(packet.ReadString()), Ok = packet.ReadBool() });
					}
				} catch (Exception) {
					// Before 0.17: no specs or regulations in the report.
				}
				var seal = MessageSeal.ReadSeal(packet.ReadInt64, packet.ReadUInt32, packet.ReadBytes);
				MessageSeal.Result sealed_ = MessageSeal.Check("trc_report", ReportBody(report), seal.Ticks, seal.Counter, seal.Tag, sender, guard_, DateTime.UtcNow.Ticks);
				if (sealed_ == MessageSeal.Result.Unsigned) {
					MessageSeal.NoteUnsigned(sender, "TRC reports");
				} else if (sealed_ != MessageSeal.Result.Good) {
					MessageSeal.NoteRefused(sender, "A TRC report", sealed_);
					return;
				}
				report.Signed = sealed_ == MessageSeal.Result.Good;
				try {
					string pass = packet.ReadString() ?? "";
					report.StaffPass = pass.Length <= TuneLoader.StaffPass.MaxLength ? pass : "";
				} catch (Exception) {
					// Before Nov 19, or no pass: nothing after the seal.
				}

				if (report.SteamId != 0) {
					lock (lock_) {
						if (reports_.Count < MaxReports || reports_.ContainsKey(report.SteamId)) {
							reports_[report.SteamId] = report;
						}
					}
				}
			} catch (Exception e) {
				TrcLog.Warning($"Ignored a broken TRC report: {e.Message}");
			}
		}

		private static string Clip(string text) => MessageText.Clip(text, MaxText);

		private readonly MessageSeal.Guard guard_ = new MessageSeal.Guard();

		/// <summary>A report's fields as they're sent and read (clipped and capped the same), for its seal.</summary>
		private static byte[] ReportBody(Report r) {
			var body = new System.IO.MemoryStream();
			using (var w = new System.IO.BinaryWriter(body, System.Text.Encoding.UTF8)) {
				w.Write(r.SteamId);
				w.Write(Clip(r.LoaderVersion));
				w.Write(r.ProtectedMode);
				w.Write(r.AllVerified);
				w.Write(r.CarId);
				w.Write(r.SlotId);
				w.Write(Clip(r.SetupCode));
				w.Write(r.SetupOfficial);
				w.Write(Clip(r.SetupSet));
				w.Write(r.SetupDifferences);
				w.Write(r.LiveStatus);
				w.Write(r.LiveDifferences);
				w.Write(r.StanceDifferences);
				w.Write(Clip(r.Engine));
				w.Write(Clip(r.Drivetrain));
				w.Write(r.DrivetrainDifferences);
				int mods = Math.Min(r.Mods.Count, MaxMods);
				w.Write(mods);
				for (int i = 0; i < mods; i++) {
					w.Write(Clip(r.Mods[i].Name));
					w.Write(Clip(r.Mods[i].Version));
					w.Write(Clip(r.Mods[i].Author));
					w.Write(Clip(r.Mods[i].Type));
				}
				w.Write(r.ShareValues);
				w.Write(r.EloTouge);
				w.Write(r.EloHighway);
				w.Write(r.EloCircuit);
				w.Write(r.Safety);
				w.Write(r.License);
				int specs = Math.Min(r.Specs.Count, MaxRules);
				w.Write(specs);
				for (int i = 0; i < specs; i++) {
					w.Write(Clip(r.Specs[i]));
				}
				int rules = Math.Min(r.Regulations.Count, MaxRules);
				w.Write(rules);
				for (int i = 0; i < rules; i++) {
					w.Write(Clip(r.Regulations[i].Code));
					w.Write(Clip(r.Regulations[i].Name));
					w.Write(r.Regulations[i].Ok);
				}
			}
			return body.ToArray();
		}
	}
}
