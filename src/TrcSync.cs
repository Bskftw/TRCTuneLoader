using System;
using System.Collections.Generic;
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
			public DateTime ReceivedAt;

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

			public bool HasRatings => EloTouge > 0;
			public float EloFor(CourseKind kind) => kind == CourseKind.Highway ? EloHighway : kind == CourseKind.Circuit ? EloCircuit : EloTouge;
			public string Badge => HasRatings ? $"{RatingBook.LicenseName((License)License)} {Safety:0.00}" : "";
		}

		// 2: added the matched tune set. 3: added the stance check. 4: added the engine & drivetrain check.
		// Players on different protocol versions don't see each other's reports.
		private const byte ProtocolVersion = 4;
		private const int MaxMods = 64;
		private const int MaxText = 160;
		private static readonly TimeSpan MinReportGap = TimeSpan.FromSeconds(3);

		private readonly Func<Report> buildOwnReport_;
		private readonly object lock_ = new object();
		private readonly Dictionary<ulong, Report> reports_ = new Dictionary<ulong, Report>();
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

			try {
				IPacket packet = Kino.Sync.CreatePacket(requestPacket_);
				packet.WriteByte(ProtocolVersion);
				Kino.Sync.SendToAll(packet);
				SendOwnReport(force: true);
			} catch (Exception e) {
				TrcLog.Error(e);
			}
		}

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
			try {
				if (packet.ReadByte() == ProtocolVersion) {
					reportRequested_ = true;
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

			Kino.Sync.SendToAll(packet);
			lastSent_ = DateTime.UtcNow;
		}

		private void OnReport(IPacket packet) {
			try {
				if (packet.ReadByte() != ProtocolVersion) {
					return;
				}

				var report = new Report {
					SteamId = packet.ReadUInt64(),
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

				if (report.SteamId != 0) {
					lock (lock_) {
						reports_[report.SteamId] = report;
					}
				}
			} catch (Exception e) {
				TrcLog.Warning($"Ignored a broken TRC report: {e.Message}");
			}
		}

		private static string Clip(string text) {
			text = text ?? "";
			return text.Length <= MaxText ? text : text.Substring(0, MaxText);
		}
	}
}
