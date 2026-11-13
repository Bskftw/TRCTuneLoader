// A course in a message: its lines, path and gates as bytes, used by battle, race and room messages.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace TRC.TuneLoader {
	public sealed partial class BattleMessage {
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
