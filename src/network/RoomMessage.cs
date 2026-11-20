// A room message: what a TRC Tune Loader player is up to, and the courses, speed traps, regulations and runs they share. Its bytes never change between versions; new parts only go on the end, where older copies stop reading.
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

		/// <summary>Host tools (0.19): come to the start of a track (Label: its code, Detail: its name). Older copies skip it.</summary>
		Invite = 6,
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

		/// <summary>
		/// Their traffic score (Stage 4.4, T11; on the end of a status, so older copies never see it): whether traffic runs for them,
		/// the score now, its combo and their best this run. Self-reported, so for fun only, never rated.
		/// </summary>
		public bool Traffic;
		public float TrafficPoints, TrafficBest;
		public byte TrafficCombo;

		/// <summary>The score attack they're in (F11; 0 none) and how long it has left (s).</summary>
		public uint AttackId;
		public float AttackSecondsLeft;

		private const int TrafficPartBytes = 1 + 4 + 4 + 1 + 4 + 4;

		/// <summary>
		/// What this copy speaks (docs/archive/IMPROVEMENT_PLAN.md N3, 0.19): each channel's versions, like "battle:5-7 race:2 lanes:5 sealed:1".
		/// "" from a copy before 0.19 (<see cref="RoomBoard.Before019"/> is what those speak).
		/// </summary>
		public string Features = "";

		/// <summary>Tests only: encode the status as a copy before 0.19 did (no parts on the end).</summary>
		public bool AsBefore019;

		// The parts on the end of a status (0.19): each is an id, its length and its bytes, so a later version can add more and
		// this one skips what it doesn't know (docs/archive/IMPROVEMENT_PLAN.md N4: only ever add to the end).
		private const byte TrafficPart = 1, FeaturesPart = 2;
		private const int MostPartBytes = 1024;

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
						if (!AsBefore019) {
							WriteParts(writer);
						}
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
					case RoomKind.Invite:
						writer.Write(To);
						writer.Write(Clip(MapKey));
						writer.Write(Clip(MapName));
						writer.Write(Clip(Label));
						writer.Write(Clip(Detail));
						break;
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
							// A status from before 0.19 ends here.
							if (stream.Position < stream.Length) {
								ReadParts(reader, m);
							}
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
						case RoomKind.Invite:
							m.To = reader.ReadUInt64();
							m.MapKey = Clip(reader.ReadString());
							m.MapName = Clip(reader.ReadString());
							m.Label = Clip(reader.ReadString());
							m.Detail = Clip(reader.ReadString());
							return m.Label.Length > 0 ? m : null;
						default:
							return null;
					}
				}
			} catch (Exception) {
				return null;
			}
		}

		private void WriteParts(BinaryWriter writer) {
			var parts = new List<(byte Id, byte[] Bytes)>();
			if (Traffic || AttackId != 0) {
				parts.Add((TrafficPart, Part(w => {
					w.Write(Traffic);
					w.Write(TrafficPoints);
					w.Write(TrafficBest);
					w.Write(TrafficCombo);
					w.Write(AttackId);
					w.Write(AttackSecondsLeft);
				})));
			}
			if (Features.Length > 0) {
				parts.Add((FeaturesPart, Part(w => w.Write(Clip(Features)))));
			}
			if (parts.Count == 0) {
				return; // nothing on the end: the bytes are exactly 0.18's
			}
			writer.Write((byte)parts.Count);
			foreach (var (id, bytes) in parts) {
				writer.Write(id);
				writer.Write((ushort)bytes.Length);
				writer.Write(bytes);
			}
		}

		private static byte[] Part(Action<BinaryWriter> write) {
			using (var stream = new MemoryStream())
			using (var w = new BinaryWriter(stream, Encoding.UTF8)) {
				write(w);
				w.Flush();
				return stream.ToArray();
			}
		}

		/// <summary>The parts on the end of a status: the known ones read, any other skipped; a broken one ends the reading (the status itself stays).</summary>
		private static void ReadParts(BinaryReader reader, RoomMessage m) {
			try {
				int count = reader.ReadByte();
				for (int i = 0; i < count; i++) {
					byte id = reader.ReadByte();
					int length = reader.ReadUInt16();
					if (length > MostPartBytes) {
						return;
					}
					byte[] bytes = reader.ReadBytes(length);
					if (bytes.Length != length) {
						return;
					}
					using (var r = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8)) {
						if (id == TrafficPart && length >= TrafficPartBytes) {
							m.Traffic = r.ReadBoolean();
							m.TrafficPoints = Sane(r.ReadSingle());
							m.TrafficBest = Sane(r.ReadSingle());
							m.TrafficCombo = r.ReadByte();
							m.AttackId = r.ReadUInt32();
							m.AttackSecondsLeft = Math.Min(Sane(r.ReadSingle()), 3600f);
						} else if (id == FeaturesPart) {
							m.Features = Clip(r.ReadString());
						}
					}
				}
			} catch (Exception) {
				// A cut-short or broken part: what was read stays.
			}
		}

		private static void WriteVector(BinaryWriter writer, Vector3 v) {
			writer.Write(v.x);
			writer.Write(v.y);
			writer.Write(v.z);
		}

		private static Vector3 ReadVector(BinaryReader reader) => new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

		private static string Clip(string text) => MessageText.Clip(text, MaxText);

		/// <summary>A number from someone else's PC: not negative, not broken, not silly.</summary>
		private static float Sane(float f) => float.IsNaN(f) || float.IsInfinity(f) ? 0f : Math.Max(0f, Math.Min(f, 1e9f));
	}
}
