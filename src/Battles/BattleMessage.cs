using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
		public const byte Protocol = 1;

		public BattleKind Kind;
		public uint BattleId;
		public ulong From;
		public ulong To;

		// Challenge
		public string FromName = "";
		public BattleFormat Format;
		public bool Open;
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
						writer.Write(Clip(CourseCode));
						writer.Write(Course != null);
						if (Course != null) {
							WriteCourse(writer, Course);
						}
						break;
					case BattleKind.Answer:
					case BattleKind.Leave:
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
							message.CourseCode = Clip(reader.ReadString());
							if (reader.ReadBoolean()) {
								message.Course = ReadCourse(reader);
							}
							break;
						case BattleKind.Answer:
						case BattleKind.Leave:
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

		/// <summary>
		/// The course as it is after Build (rounded to file precision), so the guest rebuilds exactly the same
		/// course, with the same code.
		/// </summary>
		private static void WriteCourse(BinaryWriter writer, Course course) {
			writer.Write(Clip(course.Name));
			writer.Write((byte)course.Kind);
			writer.Write(Clip(course.MapKey));
			writer.Write(Clip(course.MapName));
			WriteGate(writer, course.Start);
			WriteGate(writer, course.Finish);
			writer.Write(course.HasPath);
			writer.Write(course.HasPath ? course.Path.Count : 0);
			if (course.HasPath) {
				foreach (Vector3 point in course.Path) {
					writer.Write(point.x);
					writer.Write(point.y);
					writer.Write(point.z);
				}
			}
		}

		private static Course ReadCourse(BinaryReader reader) {
			var course = new Course {
				Name = Clip(reader.ReadString()),
				Kind = (CourseKind)reader.ReadByte(),
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
			var path = new List<Vector3>(count);
			for (int i = 0; i < count; i++) {
				path.Add(new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()));
			}
			course.Path = path;
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
