// A room message: what a TRC Tune Loader player is up to, and the courses, speed traps, regulations and runs they share. Its bytes never change between versions.
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

		private static string Clip(string text) => MessageText.Clip(text, MaxText);
	}
}
