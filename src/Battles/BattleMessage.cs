using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace TRC.TuneLoader {
	public enum BattleFormat : byte { Race = 0, CatAndMouse = 1 }

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

	/// <summary>One driver's result for one run of a battle.</summary>
	public sealed class RunResult {
		public int Run;
		public bool Finished;

		/// <summary>Seconds from GO to the finish line (when finished).</summary>
		public double Time;

		/// <summary>Why the run was stopped, when it didn't finish.</summary>
		public string Reason = "";

		/// <summary>TRC spec at the finish (TRC spec battles only care).</summary>
		public bool SpecAtEnd;
	}

	/// <summary>
	/// A message between the two TRC Tune Loader users in a battle. Every message goes to the whole room (KSL's
	/// channel can't be aimed reliably), so each says who it's from and for, and the other players ignore it.
	/// </summary>
	public sealed class BattleMessage {
		/// <summary>2 since 0.10.1: small pieces, the path packed in centimetres, and Seen. 3 since 0.11.0: laps, and circuit courses. 4 since 0.12.0: tournament matches.</summary>
		public const byte Protocol = 4;

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

		// Countdown and Abort
		public int CountdownId;
		public double GoHostTime;

		public byte[] Encode() {
			using (var memory = new MemoryStream())
			using (var writer = new BinaryWriter(memory, Encoding.UTF8)) {
				writer.Write(Protocol);
				writer.Write((byte)Kind);
				writer.Write(BattleId);
				writer.Write(From);
				writer.Write(To);
				switch (Kind) {
					case BattleKind.Challenge:
						writer.Write(Clip(FromName));
						writer.Write((byte)Format);
						writer.Write(Open);
						writer.Write((byte)Math.Max(1, Math.Min(255, Laps)));
						writer.Write(MatchId);
						writer.Write((byte)Math.Max(0, Math.Min(255, MatchGame)));
						writer.Write(Clip(CourseCode));
						writer.Write(Course != null);
						if (Course != null) {
							WriteCourse(writer, Course);
						}
						break;
					case BattleKind.Answer:
					case BattleKind.Leave:
					case BattleKind.Seen:
						writer.Write(Accept);
						writer.Write(Clip(Reason));
						break;
					case BattleKind.Heartbeat:
						writer.Write(SentAt);
						writer.Write(Echo);
						writer.Write(EchoHold);
						writer.Write(Ready);
						writer.Write(Spec);
						writer.Write(Clip(TuneSet));
						writer.Write(Clip(TuneCode));
						writer.Write(Clip(Car));
						writer.Write(Progress);
						writer.Write(RunIndex);
						writer.Write(Phase);
						writer.Write(Result != null);
						if (Result != null) {
							writer.Write(Result.Run);
							writer.Write(Result.Finished);
							writer.Write(Result.Time);
							writer.Write(Clip(Result.Reason));
							writer.Write(Result.SpecAtEnd);
						}
						break;
					case BattleKind.Countdown:
						writer.Write(CountdownId);
						writer.Write(RunIndex);
						writer.Write(GoHostTime);
						break;
					case BattleKind.Abort:
						writer.Write(CountdownId);
						writer.Write(Clip(Reason));
						break;
				}
				writer.Flush();
				return memory.ToArray();
			}
		}

		/// <summary>The message in these bytes, or null when they aren't one (another protocol version, or damaged).</summary>
		public static BattleMessage Decode(byte[] data) {
			try {
				using (var reader = new BinaryReader(new MemoryStream(data), Encoding.UTF8)) {
					if (reader.ReadByte() != Protocol) {
						return null;
					}
					var message = new BattleMessage {
						Kind = (BattleKind)reader.ReadByte(),
						BattleId = reader.ReadUInt32(),
						From = reader.ReadUInt64(),
						To = reader.ReadUInt64(),
					};
					switch (message.Kind) {
						case BattleKind.Challenge:
							message.FromName = Clip(reader.ReadString());
							message.Format = (BattleFormat)reader.ReadByte();
							message.Open = reader.ReadBoolean();
							message.Laps = Math.Max(1, (int)reader.ReadByte());
							message.MatchId = reader.ReadUInt32();
							message.MatchGame = reader.ReadByte();
							message.CourseCode = Clip(reader.ReadString());
							if (reader.ReadBoolean()) {
								message.Course = ReadCourse(reader);
							}
							break;
						case BattleKind.Answer:
						case BattleKind.Leave:
						case BattleKind.Seen:
							message.Accept = reader.ReadBoolean();
							message.Reason = Clip(reader.ReadString());
							break;
						case BattleKind.Heartbeat:
							message.SentAt = reader.ReadDouble();
							message.Echo = reader.ReadDouble();
							message.EchoHold = reader.ReadDouble();
							message.Ready = reader.ReadBoolean();
							message.Spec = reader.ReadBoolean();
							message.TuneSet = Clip(reader.ReadString());
							message.TuneCode = Clip(reader.ReadString());
							message.Car = Clip(reader.ReadString());
							message.Progress = reader.ReadSingle();
							message.RunIndex = reader.ReadInt32();
							message.Phase = reader.ReadByte();
							if (reader.ReadBoolean()) {
								message.Result = new RunResult {
									Run = reader.ReadInt32(),
									Finished = reader.ReadBoolean(),
									Time = reader.ReadDouble(),
									Reason = Clip(reader.ReadString()),
									SpecAtEnd = reader.ReadBoolean(),
								};
							}
							break;
						case BattleKind.Countdown:
							message.CountdownId = reader.ReadInt32();
							message.RunIndex = reader.ReadInt32();
							message.GoHostTime = reader.ReadDouble();
							break;
						case BattleKind.Abort:
							message.CountdownId = reader.ReadInt32();
							message.Reason = Clip(reader.ReadString());
							break;
						default:
							return null;
					}
					return message;
				}
			} catch (Exception) {
				return null;
			}
		}

		private const int MaxText = 160;
		private const int MaxPoints = 50000;

		private static string Clip(string text) {
			text = text ?? "";
			return text.Length <= MaxText ? text : text.Substring(0, MaxText);
		}

		private static readonly Regex RichText = new Regex(
			@"</?(color|b|i|u|s|size|material|quad|font|mark|sup|sub|alpha|cspace|mspace|voffset|link|style|noparse)(=[^<>]*)?>",
			RegexOptions.IgnoreCase | RegexOptions.Compiled);

		/// <summary>
		/// A player name without its colour and style tags (CarX names can colour every letter, which makes them
		/// too long to send whole and breaks when cut).
		/// </summary>
		public static string PlainName(string name) => RichText.Replace(name ?? "", "").Trim();

		/// <summary>
		/// The course as it is after Build (rounded to file precision), so the guest rebuilds exactly the same
		/// course, with the same code.
		/// </summary>
		internal static void WriteCourse(BinaryWriter writer, Course course) {
			writer.Write(Clip(course.Name));
			writer.Write((byte)course.Kind);
			writer.Write(Clip(course.MapKey));
			writer.Write(Clip(course.MapName));
			WriteGate(writer, course.Start);
			WriteGate(writer, course.Finish);
			writer.Write(course.HasPath);
			writer.Write(course.HasPath ? course.Path.Count : 0);
			if (course.HasPath) {
				WritePath(writer, course.Path);
			}
		}

		/// <summary>
		/// The path in whole centimetres (Build rounds every point to those), sent as how much each step differs
		/// from the step before. Along a road those are a few centimetres, so most take one byte per axis: about a
		/// quarter of the size of plain numbers. A long Touge path still takes several KB, so it's split in pieces.
		/// </summary>
		internal static void WritePath(BinaryWriter writer, List<Vector3> path) {
			long[] last = null;
			var step = new long[3];
			foreach (Vector3 point in path) {
				var here = new long[] { Centimetres(point.x), Centimetres(point.y), Centimetres(point.z) };
				for (int axis = 0; axis < 3; axis++) {
					if (last == null) {
						WriteVarint(writer, here[axis]);
					} else {
						long next = here[axis] - last[axis];
						WriteVarint(writer, next - step[axis]);
						step[axis] = next;
					}
				}
				last = here;
			}
		}

		internal static List<Vector3> ReadPath(BinaryReader reader, int count) {
			var path = new List<Vector3>(count);
			var cm = new long[3];
			var step = new long[3];
			for (int i = 0; i < count; i++) {
				for (int axis = 0; axis < 3; axis++) {
					if (i == 0) {
						cm[axis] = ReadVarint(reader);
					} else {
						step[axis] += ReadVarint(reader);
						cm[axis] += step[axis];
					}
				}
				path.Add(new Vector3(Metres(cm[0]), Metres(cm[1]), Metres(cm[2])));
			}
			return path;
		}

		/// <summary>A whole number in as few bytes as it needs: 7 bits a byte, small negative numbers small too (zigzag).</summary>
		private static void WriteVarint(BinaryWriter writer, long value) {
			ulong zigzag = (ulong)((value << 1) ^ (value >> 63));
			while (zigzag >= 0x80) {
				writer.Write((byte)(zigzag | 0x80));
				zigzag >>= 7;
			}
			writer.Write((byte)zigzag);
		}

		private static long ReadVarint(BinaryReader reader) {
			ulong zigzag = 0;
			for (int shift = 0; ; shift += 7) {
				if (shift > 63) {
					throw new InvalidDataException("number too long");
				}
				byte b = reader.ReadByte();
				zigzag |= (ulong)(b & 0x7F) << shift;
				if (b < 0x80) {
					break;
				}
			}
			return (long)(zigzag >> 1) ^ -(long)(zigzag & 1);
		}

		private static long Centimetres(float metres) => (long)Math.Round((double)metres * 100.0, MidpointRounding.AwayFromZero);

		private static float Metres(long centimetres) => (float)(centimetres / 100.0);

		internal static Course ReadCourse(BinaryReader reader) {
			var course = new Course {
				Name = Clip(reader.ReadString()),
				Kind = (CourseKind)Math.Min(reader.ReadByte(), (byte)CourseKind.Circuit),
				MapKey = Clip(reader.ReadString()),
				MapName = Clip(reader.ReadString()),
				Source = CourseSource.Custom,
				Start = ReadGate(reader),
				Finish = ReadGate(reader),
				HasPath = reader.ReadBoolean(),
			};
			int count = reader.ReadInt32();
			if (count < 0 || count > MaxPoints) {
				throw new InvalidDataException("too many path points");
			}
			course.Path = course.HasPath ? ReadPath(reader, count) : new List<Vector3>();
			course.Build(resample: false);
			return course;
		}

		private static void WriteGate(BinaryWriter writer, CourseGate gate) {
			writer.Write(gate.Center.x);
			writer.Write(gate.Center.y);
			writer.Write(gate.Center.z);
			writer.Write(gate.Forward.x);
			writer.Write(gate.Forward.z);
			writer.Write(gate.Width);
		}

		private static CourseGate ReadGate(BinaryReader reader) =>
			new CourseGate {
				Center = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
				Forward = new Vector3(reader.ReadSingle(), 0f, reader.ReadSingle()),
				Width = reader.ReadSingle(),
			};
	}
}
