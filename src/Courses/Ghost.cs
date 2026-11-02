using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>Where the car was at one moment of a run: seconds since GO, position, rotation and speed (km/h).</summary>
	public struct GhostSample {
		public float Time;
		public Vector3 Position;
		public Quaternion Rotation;
		public float Kmh;
	}

	/// <summary>
	/// A recorded run (bskftw, Nov 1): the car's position and rotation 10 times a second from GO to the finish, with
	/// its time and sectors, saved as Ghosts/&lt;course code&gt;/*.trcghost or shared with other players. Played back,
	/// it's only something to look at: the mod draws a see-through copy of a car body there, with no collider and no
	/// physics, so it can't push, tow or slipstream anyone (KINO's ghost could be used for a slipstream). It also
	/// gives the time at every point of the course, so a time attack can show the live delta and sectors against it.
	/// </summary>
	public sealed class Ghost {
		public const string Extension = ".trcghost";
		public const float SampleSeconds = 0.1f;
		private const string Magic = "TRCGHOST";
		/// <summary>2 (0.16): adds the name. Version 1 files still read.</summary>
		private const byte FileVersion = 2;

		/// <summary>A ghost is at most this long (20 minutes), and its packed bytes at most this big, so it fits a share.</summary>
		public const int MaxSamples = 12000;
		public const int MaxBytes = 120 * 1024;

		public string CourseCode = "";
		public string CourseName = "";
		public string MapKey = "";
		public ulong DriverId;
		public string Driver = "";
		public int CarId;
		public string Car = "";

		/// <summary>The tune set or rule it was driven to ("TRC Revival", "Open", a regulation's code), for the list.</summary>
		public string Rules = "";

		/// <summary>What it was: a time attack, a battle, a race.</summary>
		public string Kind = "time attack";

		/// <summary>The player's own name for it ("" for none), like "clean run before the cup".</summary>
		public string Name = "";

		public double Time;
		public List<double> Sectors = new List<double>();
		public float TrapTotal;
		public DateTime When = DateTime.Now;
		public List<GhostSample> Samples = new List<GhostSample>();

		/// <summary>Where it was loaded from ("" when only in memory).</summary>
		public string FilePath = "";

		public string Title => (Name.Length > 0 ? $"{Name}: " : "") + $"{Driver} · {TimeAttackRun.FormatTime(Time)} · {Car}";

		// ---- Playing it back ----

		/// <summary>Where the ghost is <paramref name="seconds"/> after GO (between two samples), false before the first or after the last.</summary>
		public bool PoseAt(double seconds, out GhostSample pose) {
			pose = default;
			if (Samples.Count == 0 || seconds < Samples[0].Time || seconds > Samples[Samples.Count - 1].Time) {
				return false;
			}
			int hi = Samples.FindIndex(s => s.Time >= seconds);
			if (hi <= 0) {
				pose = Samples[Math.Max(0, hi)];
				return true;
			}
			GhostSample a = Samples[hi - 1];
			GhostSample b = Samples[hi];
			float t = b.Time > a.Time ? Mathf.Clamp01((float)((seconds - a.Time) / (b.Time - a.Time))) : 0f;
			pose = new GhostSample {
				Time = (float)seconds,
				Position = Vector3.Lerp(a.Position, b.Position, t),
				Rotation = Blend(a.Rotation, b.Rotation, t),
				Kmh = Mathf.Lerp(a.Kmh, b.Kmh, t),
			};
			return true;
		}

		/// <summary>Between two rotations 0.1 s apart (a normalised blend, the shorter way round: as good as a slerp that close).</summary>
		private static Quaternion Blend(Quaternion a, Quaternion b, float t) {
			float dot = a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;
			float sign = dot < 0f ? -1f : 1f;
			return Unit(new Quaternion(a.x + (sign * b.x - a.x) * t, a.y + (sign * b.y - a.y) * t, a.z + (sign * b.z - a.z) * t, a.w + (sign * b.w - a.w) * t));
		}

		private static Quaternion Unit(Quaternion q) {
			float length = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
			return length > 1e-6f ? new Quaternion(q.x / length, q.y / length, q.z / length, q.w / length) : new Quaternion(0f, 0f, 0f, 1f);
		}

		/// <summary>
		/// The ghost's time at every <see cref="TimeAttackRun.TraceStep"/> m along the course's path: a reference for
		/// the live delta, like a best run's trace. Null when the course has no path or the ghost doesn't follow it.
		/// </summary>
		public float[] TraceOn(Course course) {
			if (course == null || !course.HasPath || course.Path.Count < 2 || Samples.Count < 2) {
				return null;
			}
			var trace = new List<float>();
			int segment = 0;
			float lastProgress = 0f;
			double lastTime = 0;
			foreach (GhostSample s in Samples) {
				PathPosition at = CourseGeometry.Project(course.Path, course.Distances, s.Position, segment - 3, segment + 40);
				if (at.Offset > 60f) {
					continue;
				}
				segment = at.Segment;
				float progress = Mathf.Max(lastProgress, at.Progress);
				while (trace.Count * TimeAttackRun.TraceStep <= progress) {
					float mark = trace.Count * TimeAttackRun.TraceStep;
					double t = progress > lastProgress ? lastTime + (s.Time - lastTime) * (mark - lastProgress) / (progress - lastProgress) : s.Time;
					trace.Add((float)Math.Max(0, t));
				}
				lastProgress = progress;
				lastTime = s.Time;
			}
			return trace.Count >= 2 && lastProgress > course.Length * 0.9f ? trace.ToArray() : null;
		}

		// ---- Recording ----

		/// <summary>Adds where the car is now, when a sample is due (every <see cref="SampleSeconds"/>, and the very first).</summary>
		public void Record(double seconds, Vector3 position, Quaternion rotation, float kmh) {
			if (Samples.Count >= MaxSamples || seconds < 0) {
				return;
			}
			if (Samples.Count > 0 && seconds - Samples[Samples.Count - 1].Time < SampleSeconds - 0.001) {
				return;
			}
			Samples.Add(new GhostSample { Time = (float)seconds, Position = position, Rotation = rotation, Kmh = Mathf.Abs(kmh) });
		}

		// ---- Bytes (the file and shares) ----

		/// <summary>The ghost packed (deflate): what's saved and what's shared.</summary>
		public byte[] Encode() {
			using (var memory = new MemoryStream()) {
				using (var deflate = new DeflateStream(memory, CompressionMode.Compress, true))
				using (var w = new BinaryWriter(deflate, Encoding.UTF8)) {
					w.Write(Magic);
					w.Write(FileVersion);
					w.Write(Clip(CourseCode));
					w.Write(Clip(CourseName));
					w.Write(Clip(MapKey));
					w.Write(DriverId);
					w.Write(Clip(BattleMessage.PlainName(Driver)));
					w.Write(CarId);
					w.Write(Clip(Car));
					w.Write(Clip(Rules));
					w.Write(Clip(Kind));
					w.Write(Clip(Name));
					w.Write(Time);
					w.Write((byte)Math.Min(Sectors.Count, 64));
					foreach (double s in Sectors.Take(64)) {
						w.Write(s);
					}
					w.Write(TrapTotal);
					w.Write(When.ToBinary());
					w.Write(Math.Min(Samples.Count, MaxSamples));
					foreach (GhostSample s in Samples.Take(MaxSamples)) {
						w.Write(s.Time);
						w.Write(s.Position.x);
						w.Write(s.Position.y);
						w.Write(s.Position.z);
						Quaternion q = s.Rotation;
						if (q.w < 0) {
							q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
						}
						w.Write((short)Mathf.Clamp(Mathf.RoundToInt(q.x * 32767f), -32767, 32767));
						w.Write((short)Mathf.Clamp(Mathf.RoundToInt(q.y * 32767f), -32767, 32767));
						w.Write((short)Mathf.Clamp(Mathf.RoundToInt(q.z * 32767f), -32767, 32767));
						w.Write((ushort)Mathf.Clamp(Mathf.RoundToInt(s.Kmh * 10f), 0, 65535));
					}
				}
				return memory.ToArray();
			}
		}

		/// <summary>The ghost in these bytes, or null when they aren't one.</summary>
		public static Ghost Decode(byte[] data) {
			if (data == null || data.Length == 0 || data.Length > MaxBytes * 4) {
				return null;
			}
			try {
				using (var deflate = new DeflateStream(new MemoryStream(data), CompressionMode.Decompress))
				using (var r = new BinaryReader(deflate, Encoding.UTF8)) {
					byte version = r.ReadString() == Magic ? r.ReadByte() : (byte)0;
					if (version < 1 || version > FileVersion) {
						return null;
					}
					var g = new Ghost {
						CourseCode = Clip(r.ReadString()),
						CourseName = Clip(r.ReadString()),
						MapKey = Clip(r.ReadString()),
						DriverId = r.ReadUInt64(),
						Driver = Clip(r.ReadString()),
						CarId = r.ReadInt32(),
						Car = Clip(r.ReadString()),
						Rules = Clip(r.ReadString()),
						Kind = Clip(r.ReadString()),
					};
					if (version >= 2) {
						g.Name = Clip(r.ReadString());
					}
					g.Time = r.ReadDouble();
					int sectors = r.ReadByte();
					for (int i = 0; i < sectors; i++) {
						g.Sectors.Add(r.ReadDouble());
					}
					g.TrapTotal = r.ReadSingle();
					g.When = DateTime.FromBinary(r.ReadInt64());
					int count = r.ReadInt32();
					if (count < 0 || count > MaxSamples || !(g.Time > 0 && g.Time < 36000)) {
						return null;
					}
					float last = float.MinValue;
					for (int i = 0; i < count; i++) {
						float time = r.ReadSingle();
						var position = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
						float x = r.ReadInt16() / 32767f, y = r.ReadInt16() / 32767f, z = r.ReadInt16() / 32767f;
						float w = Mathf.Sqrt(Mathf.Max(0f, 1f - x * x - y * y - z * z));
						float kmh = r.ReadUInt16() / 10f;
						if (float.IsNaN(time) || time < last || float.IsNaN(position.x) || Mathf.Abs(position.x) > 1e6f || Mathf.Abs(position.y) > 1e6f || Mathf.Abs(position.z) > 1e6f) {
							return null;
						}
						last = time;
						g.Samples.Add(new GhostSample { Time = time, Position = position, Rotation = Unit(new Quaternion(x, y, z, w)), Kmh = kmh });
					}
					return g;
				}
			} catch (Exception) {
				return null;
			}
		}

		private static string Clip(string text) {
			string clean = new string((text ?? "").Where(c => !char.IsControl(c)).ToArray());
			return clean.Length <= 80 ? clean : clean.Substring(0, 80);
		}

		// ---- The folder (Ghosts/<course code>/) ----

		public static string FolderFor(string ghostsFolder, string courseCode) =>
			Path.Combine(ghostsFolder, new string((courseCode ?? "").Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_').ToArray()));

		public void Save(string ghostsFolder) {
			string folder = FolderFor(ghostsFolder, CourseCode);
			Directory.CreateDirectory(folder);
			if (FilePath.Length == 0) {
				string name = new string($"{When.ToString("yyyy-MM-dd HHmmss", CultureInfo.InvariantCulture)} {Driver} {TimeAttackRun.FormatTime(Time).Replace(':', '.')}"
					.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
				FilePath = Path.Combine(folder, name + Extension);
			}
			File.WriteAllBytes(FilePath, Encode());
		}

		/// <summary>Every ghost saved for a course, fastest first.</summary>
		public static List<Ghost> LoadFor(string ghostsFolder, string courseCode) {
			var list = new List<Ghost>();
			string folder = FolderFor(ghostsFolder, courseCode);
			if (!Directory.Exists(folder)) {
				return list;
			}
			foreach (string path in Directory.GetFiles(folder, "*" + Extension)) {
				try {
					Ghost g = Decode(File.ReadAllBytes(path));
					if (g != null && g.CourseCode == courseCode) {
						g.FilePath = path;
						list.Add(g);
					}
				} catch (Exception) {
					// A file that can't be read is left out.
				}
			}
			return list.OrderBy(g => g.Time).ToList();
		}
	}
}
