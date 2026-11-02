using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>What a TRC Tune Loader player is up to, for the room board.</summary>
	public enum RoomActivity : byte { Garage, FreeRoam, TimeAttack, Battle, Race, Game, RaceLobby, Qualifying, Building }

	public enum RoomKind : byte {
		/// <summary>Everyone, every few seconds and when it changes: what they're doing.</summary>
		Status = 1,

		/// <summary>A player shared one of their courses with the room.</summary>
		CourseShare = 2,

		/// <summary>A player shared their own speed traps on a map with the room or one player (0.15.2; older copies skip it).</summary>
		TrapShare = 3,

		/// <summary>A player shared a regulation with the room or one player (0.16; older copies skip it).</summary>
		RegulationShare = 4,

		/// <summary>A player shared a run (time, sectors and its ghost) with the room or one player (0.16; older copies skip it).</summary>
		RunShare = 5,
	}

	/// <summary>
	/// A message on the room channel (KSL packet "trc_room"): player statuses for the room board, shared courses
	/// and shared speed traps. Separate from battles and races, so those stay the same as 0.13.
	/// </summary>
	public sealed class RoomMessage {
		public const byte Protocol = 1;
		private const int MaxText = 80;

		public RoomKind Kind;
		public ulong From;
		public string Name = "";

		// Status
		public string Version = "";
		public RoomActivity Activity;

		/// <summary>The course, race or opponent, like "Akina Downhill" or "vs Apok" ("" when nothing).</summary>
		public string Detail = "";

		/// <summary>The player said they're up for a battle (Home).</summary>
		public bool UpForBattle;

		/// <summary>Their race lobby anyone can join (they host it or are in it), 0 when none.</summary>
		public uint OpenRaceId;
		public string MapKey = "";
		public string Car = "";

		// CourseShare
		public Course Course;

		// TrapShare
		/// <summary>Who it's for: one player's SteamID, or 0 for everyone in the room.</summary>
		public ulong To;

		/// <summary>What was shared, like "Tsubaki" or "All my traps", for the list.</summary>
		public string Label = "";
		public string MapName = "";
		public List<SpeedTrap> Traps = new List<SpeedTrap>();

		/// <summary>The most speed traps one share carries (about 25 pieces on the channel).</summary>
		public const int MaxTraps = 250;

		// RegulationShare (To and Label too)
		public Regulation Regulation;

		// RunShare (To, MapKey and MapName too): the ghost, packed.
		public Ghost Ghost;
		private const int MaxRegulationText = 12000;

		public byte[] Encode() {
			using (var stream = new MemoryStream())
			using (var writer = new BinaryWriter(stream, Encoding.UTF8)) {
				writer.Write((byte)Kind);
				writer.Write(From);
				writer.Write(Clip(BattleMessage.PlainName(Name)));
				switch (Kind) {
					case RoomKind.Status:
						writer.Write(Clip(Version));
						writer.Write((byte)Activity);
						writer.Write(Clip(Detail));
						writer.Write(UpForBattle);
						writer.Write(OpenRaceId);
						writer.Write(Clip(MapKey));
						writer.Write(Clip(Car));
						break;
					case RoomKind.CourseShare:
						BattleMessage.WriteCourse(writer, Course);
						writer.Write(Course.Traps.Count);
						foreach (CourseGate trap in Course.Traps) {
							WriteVector(writer, trap.Center);
							WriteVector(writer, trap.Forward);
							writer.Write(trap.Width);
						}
						writer.Write(Course.GridPath.Count);
						foreach (Vector3 point in Course.GridPath) {
							WriteVector(writer, point);
						}
						writer.Write((byte)Course.GridLayout);
						break;
					case RoomKind.TrapShare:
						writer.Write(To);
						writer.Write(Clip(MapKey));
						writer.Write(Clip(MapName));
						writer.Write(Clip(Label));
						int count = Math.Min(MaxTraps, Traps.Count);
						writer.Write((ushort)count);
						for (int i = 0; i < count; i++) {
							SpeedTrap trap = Traps[i];
							writer.Write(Clip(trap.Name));
							WriteVector(writer, trap.Gate.Center);
							writer.Write(trap.Gate.Forward.x);
							writer.Write(trap.Gate.Forward.z);
							writer.Write(trap.Gate.Width);
						}
						break;
					case RoomKind.RegulationShare: {
						writer.Write(To);
						string text = Regulation?.ToText() ?? "";
						writer.Write(text.Length <= MaxRegulationText ? text : "");
						break;
					}
					case RoomKind.RunShare: {
						writer.Write(To);
						writer.Write(Clip(MapKey));
						writer.Write(Clip(MapName));
						byte[] ghost = Ghost?.Encode() ?? new byte[0];
						writer.Write(ghost.Length <= Ghost.MaxBytes ? ghost.Length : 0);
						if (ghost.Length <= Ghost.MaxBytes) {
							writer.Write(ghost);
						}
						break;
					}
				}
				writer.Flush();
				return stream.ToArray();
			}
		}

		/// <summary>The message, or null when it's broken or of a kind this version doesn't know.</summary>
		public static RoomMessage Decode(byte[] data) {
			try {
				using (var stream = new MemoryStream(data))
				using (var reader = new BinaryReader(stream, Encoding.UTF8)) {
					var m = new RoomMessage {
						Kind = (RoomKind)reader.ReadByte(),
						From = reader.ReadUInt64(),
						Name = Clip(reader.ReadString()),
					};
					switch (m.Kind) {
						case RoomKind.Status:
							m.Version = Clip(reader.ReadString());
							m.Activity = (RoomActivity)Math.Min(reader.ReadByte(), (byte)RoomActivity.Building);
							m.Detail = Clip(reader.ReadString());
							m.UpForBattle = reader.ReadBoolean();
							m.OpenRaceId = reader.ReadUInt32();
							m.MapKey = Clip(reader.ReadString());
							m.Car = Clip(reader.ReadString());
							return m;
						case RoomKind.CourseShare:
							m.Course = BattleMessage.ReadCourse(reader);
							int traps = reader.ReadInt32();
							if (traps < 0 || traps > 64) {
								return null;
							}
							for (int i = 0; i < traps; i++) {
								m.Course.Traps.Add(new CourseGate { Center = ReadVector(reader), Forward = ReadVector(reader), Width = reader.ReadSingle() });
							}
							int grid = reader.ReadInt32();
							if (grid < 0 || grid > 2000) {
								return null;
							}
							for (int i = 0; i < grid; i++) {
								m.Course.GridPath.Add(ReadVector(reader));
							}
							m.Course.GridLayout = (GridLayout)reader.ReadByte();
							return m;
						case RoomKind.TrapShare: {
							m.To = reader.ReadUInt64();
							m.MapKey = Clip(reader.ReadString());
							m.MapName = Clip(reader.ReadString());
							m.Label = Clip(reader.ReadString());
							int count = reader.ReadUInt16();
							if (count > MaxTraps || m.MapKey.Length == 0) {
								return null;
							}
							for (int i = 0; i < count; i++) {
								string name = Clip(reader.ReadString());
								Vector3 center = ReadVector(reader);
								var forward = new Vector3(reader.ReadSingle(), 0f, reader.ReadSingle());
								float width = reader.ReadSingle();
								if (forward.sqrMagnitude < 0.01f || !(width >= 1f && width <= 60f) || float.IsNaN(center.x + center.y + center.z) || float.IsInfinity(center.x + center.y + center.z)) {
									return null;
								}
								m.Traps.Add(new SpeedTrap {
									Name = name.Length > 0 ? name : $"Speed trap {i + 1}", MapKey = m.MapKey, Source = CourseSource.Custom,
									Gate = new CourseGate { Center = center, Forward = forward.normalized, Width = width },
								});
							}
							return m;
						}
						case RoomKind.RegulationShare: {
							m.To = reader.ReadUInt64();
							string text = reader.ReadString();
							m.Regulation = text.Length > 0 && text.Length <= MaxRegulationText ? Regulation.Parse(text) : null;
							return m.Regulation != null ? m : null;
						}
						case RoomKind.RunShare: {
							m.To = reader.ReadUInt64();
							m.MapKey = Clip(reader.ReadString());
							m.MapName = Clip(reader.ReadString());
							int length = reader.ReadInt32();
							if (length <= 0 || length > Ghost.MaxBytes) {
								return null;
							}
							m.Ghost = Ghost.Decode(reader.ReadBytes(length));
							return m.Ghost != null && m.Ghost.Samples.Count >= 2 ? m : null;
						}
						default:
							return null;
					}
				}
			} catch (Exception) {
				return null;
			}
		}

		private static void WriteVector(BinaryWriter writer, Vector3 v) {
			writer.Write(v.x);
			writer.Write(v.y);
			writer.Write(v.z);
		}

		private static Vector3 ReadVector(BinaryReader reader) => new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

		private static string Clip(string text) {
			text = text ?? "";
			return text.Length <= MaxText ? text : text.Substring(0, MaxText);
		}
	}

	/// <summary>
	/// The room board: the latest status of every TRC Tune Loader player in the room, the courses shared with it,
	/// and when this player should send their own status (on a change, every <see cref="KeepAliveSeconds"/>, and
	/// soon after someone new shows up, so they see everyone at once).
	/// </summary>
	public sealed class RoomBoard {
		public const double KeepAliveSeconds = 15;
		public const double StaleSeconds = 45;
		public const double MinGapSeconds = 2;
		public const int MaxShared = 10;

		/// <summary>One sender can share a course this often at most; more are dropped.</summary>
		public const double ShareGapSeconds = 10;

		public sealed class Entry {
			public RoomMessage Status;
			public double At;
		}

		public sealed class Shared {
			public Course Course;
			public ulong From;
			public string FromName = "";
			public double At;
		}

		/// <summary>Speed traps someone shared with the room or with this player, waiting to be added.</summary>
		public sealed class SharedTraps {
			public ulong From;
			public string FromName = "";
			public string MapKey = "";
			public string MapName = "";
			public string Label = "";
			public List<SpeedTrap> Traps = new List<SpeedTrap>();

			/// <summary>Sent to this player only (not the whole room).</summary>
			public bool JustForMe;
			public double At;
		}

		/// <summary>A regulation or a run someone shared with the room or this player (0.16), waiting to be saved.</summary>
		public sealed class SharedItem {
			public ulong From;
			public string FromName = "";
			public bool JustForMe;
			public double At;
			public Regulation Regulation;
			public Ghost Ghost;
			public string MapKey = "";
			public string MapName = "";
		}

		public List<SharedItem> SharedRegulations { get; } = new List<SharedItem>();
		public List<SharedItem> SharedRuns { get; } = new List<SharedItem>();
		private readonly Dictionary<ulong, double> lastItemShare_ = new Dictionary<ulong, double>();

		/// <summary>
		/// Takes a shared regulation or run (0.16): for everyone, or for <paramref name="me"/>. Returns it (for a pop-up),
		/// or null when it's for someone else, broken, or the sender shared again too soon.
		/// </summary>
		public SharedItem ReceiveItem(RoomMessage m, double now, ulong me) {
			bool regulation = m.Kind == RoomKind.RegulationShare && m.Regulation != null;
			bool run = m.Kind == RoomKind.RunShare && m.Ghost != null;
			if ((!regulation && !run) || m.From == me || (m.To != 0 && m.To != me)) {
				return null;
			}
			if (lastItemShare_.TryGetValue(m.From, out double last) && now - last < ShareGapSeconds) {
				return null;
			}
			lastItemShare_[m.From] = now;
			var item = new SharedItem {
				From = m.From, FromName = BattleMessage.PlainName(m.Name), JustForMe = m.To == me, At = now,
				Regulation = m.Regulation, Ghost = m.Ghost, MapKey = m.MapKey, MapName = m.MapName,
			};
			List<SharedItem> list = regulation ? SharedRegulations : SharedRuns;
			if (regulation) {
				list.RemoveAll(i => i.From == m.From && i.Regulation.Code == m.Regulation.Code);
			} else {
				list.RemoveAll(i => i.From == m.From && i.Ghost.CourseCode == m.Ghost.CourseCode && Math.Abs(i.Ghost.Time - m.Ghost.Time) < 0.001);
				m.Ghost.DriverId = m.From;
			}
			list.Insert(0, item);
			if (list.Count > MaxShared) {
				list.RemoveRange(MaxShared, list.Count - MaxShared);
			}
			return item;
		}

		private readonly Dictionary<ulong, Entry> statuses_ = new Dictionary<ulong, Entry>();
		private readonly Dictionary<ulong, double> lastShare_ = new Dictionary<ulong, double>();
		public List<Shared> SharedCourses { get; } = new List<Shared>();
		public List<SharedTraps> SharedTrapSets { get; } = new List<SharedTraps>();
		private readonly Dictionary<ulong, double> lastTrapShare_ = new Dictionary<ulong, double>();

		private string lastSent_ = "";
		private double lastSentAt_ = double.NegativeInfinity;
		private bool someoneNew_;

		/// <summary>Takes a message from the room. Returns a newly shared course (for a pop-up), or null.</summary>
		public Shared Receive(RoomMessage m, double now) {
			if (m.Kind == RoomKind.Status) {
				if (!statuses_.ContainsKey(m.From)) {
					someoneNew_ = true;
				}
				statuses_[m.From] = new Entry { Status = m, At = now };
				return null;
			}
			if (m.Kind == RoomKind.CourseShare && m.Course != null) {
				if (lastShare_.TryGetValue(m.From, out double last) && now - last < ShareGapSeconds) {
					return null;
				}
				lastShare_[m.From] = now;
				SharedCourses.RemoveAll(s => s.Course.Code == m.Course.Code);
				var shared = new Shared { Course = m.Course, From = m.From, FromName = BattleMessage.PlainName(m.Name), At = now };
				SharedCourses.Insert(0, shared);
				if (SharedCourses.Count > MaxShared) {
					SharedCourses.RemoveRange(MaxShared, SharedCourses.Count - MaxShared);
				}
				return shared;
			}
			return null;
		}

		/// <summary>
		/// Takes speed traps shared on the room channel: for everyone, or for <paramref name="me"/>. Returns them
		/// (for a pop-up), or null when they're for someone else, empty, or the sender shared again too soon.
		/// </summary>
		public SharedTraps ReceiveTraps(RoomMessage m, double now, ulong me) {
			if (m.Kind != RoomKind.TrapShare || m.From == me || (m.To != 0 && m.To != me) || m.Traps.Count == 0) {
				return null;
			}
			if (lastTrapShare_.TryGetValue(m.From, out double last) && now - last < ShareGapSeconds) {
				return null;
			}
			lastTrapShare_[m.From] = now;
			SharedTrapSets.RemoveAll(s => s.From == m.From && s.MapKey == m.MapKey && s.Label == m.Label);
			var shared = new SharedTraps {
				From = m.From, FromName = BattleMessage.PlainName(m.Name), MapKey = m.MapKey, MapName = m.MapName, Label = m.Label,
				Traps = m.Traps, JustForMe = m.To == me, At = now,
			};
			SharedTrapSets.Insert(0, shared);
			if (SharedTrapSets.Count > MaxShared) {
				SharedTrapSets.RemoveRange(MaxShared, SharedTrapSets.Count - MaxShared);
			}
			return shared;
		}

		/// <summary>
		/// <paramref name="theirs"/> added to <paramref name="mine"/>: each one not within 3 m of a trap already there.
		/// Returns how many were added.
		/// </summary>
		public static int MergeTraps(List<SpeedTrap> mine, IEnumerable<SpeedTrap> theirs, IEnumerable<SpeedTrap> alsoThere = null) {
			var there = mine.Concat(alsoThere ?? Enumerable.Empty<SpeedTrap>()).Select(t => t.Gate.Center).ToList();
			int added = 0;
			foreach (SpeedTrap trap in theirs) {
				if (there.Any(c => Vector3.Distance(c, trap.Gate.Center) < 3f)) {
					continue;
				}
				mine.Add(new SpeedTrap { Name = trap.Name, Gate = trap.Gate, MapKey = trap.MapKey, Source = CourseSource.Custom });
				there.Add(trap.Gate.Center);
				added++;
			}
			return added;
		}

		/// <summary>
		/// Whether <paramref name="theirs"/> is a newer version than <paramref name="mine"/> ("0.15.1" over "0.15.0").
		/// Oct 29: a player's KSL had treated an update as optional and never installed it.
		/// </summary>
		public static bool IsNewer(string theirs, string mine) =>
			System.Version.TryParse(theirs ?? "", out System.Version a) && System.Version.TryParse(mine ?? "", out System.Version b) && a > b;

		public List<RoomMessage> Fresh(double now) =>
			statuses_.Values.Where(e => now - e.At <= StaleSeconds).Select(e => e.Status)
				.OrderBy(s => BattleMessage.PlainName(s.Name), StringComparer.OrdinalIgnoreCase).ToList();

		public RoomMessage StatusOf(ulong id, double now) =>
			statuses_.TryGetValue(id, out Entry e) && now - e.At <= StaleSeconds ? e.Status : null;

		/// <summary>Whether to send this status now (it changed, it's been a while, or someone new needs it).</summary>
		public bool ShouldSend(RoomMessage mine, double now) {
			string signature = $"{(byte)mine.Activity}|{mine.Detail}|{mine.UpForBattle}|{mine.OpenRaceId}|{mine.MapKey}|{mine.Car}";
			double since = now - lastSentAt_;
			if (since < MinGapSeconds) {
				return false;
			}
			if (signature != lastSent_ || since >= KeepAliveSeconds || someoneNew_) {
				lastSent_ = signature;
				lastSentAt_ = now;
				someoneNew_ = false;
				return true;
			}
			return false;
		}

		/// <summary>The room changed (left it, or joined another): forget everyone.</summary>
		public void Clear() {
			statuses_.Clear();
			lastShare_.Clear();
			lastTrapShare_.Clear();
			lastItemShare_.Clear();
			someoneNew_ = false;
			lastSentAt_ = double.NegativeInfinity;
		}

		/// <summary>"In a race on Akina Downhill", "Free roam" and so on.</summary>
		public static string ActivityText(RoomMessage s) {
			string detail = s.Detail.Length > 0 ? s.Detail : "";
			switch (s.Activity) {
				case RoomActivity.Garage: return "In the garage";
				case RoomActivity.FreeRoam: return "Free roam";
				case RoomActivity.TimeAttack: return detail.Length > 0 ? $"Time attack on {detail}" : "Time attack";
				case RoomActivity.Battle: return detail.Length > 0 ? $"Battling {detail}" : "In a battle";
				case RoomActivity.Race: return detail.Length > 0 ? $"Racing on {detail}" : "In a race";
				case RoomActivity.Game: return detail.Length > 0 ? $"Playing {detail}" : "In a room game";
				case RoomActivity.RaceLobby: return detail.Length > 0 ? $"Waiting in {detail}" : "In a race lobby";
				case RoomActivity.Qualifying: return detail.Length > 0 ? $"Qualifying on {detail}" : "Qualifying";
				case RoomActivity.Building: return "Making a course";
				default: return "";
			}
		}

		/// <summary>Free to battle right now (not busy in something that has them).</summary>
		public static bool Free(RoomMessage s) =>
			s.Activity == RoomActivity.Garage || s.Activity == RoomActivity.FreeRoam || s.Activity == RoomActivity.TimeAttack || s.Activity == RoomActivity.Building;
	}
}
