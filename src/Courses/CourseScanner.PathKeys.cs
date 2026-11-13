// Fingerprints of a course path's ingredients, so a path remembered from an earlier load is only used when nothing changed.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace TRC.TuneLoader {
	public static partial class CourseScanner {
		private static SavedPaths saved_;

		/// <summary>Each path mesh's fingerprint, made once per scan (a shared path serves several courses).</summary>
		private static readonly Dictionary<MeshFilter, byte[]> meshPrints_ = new Dictionary<MeshFilter, byte[]>();

		// Where the path time goes (IMPROVEMENT_PLAN.md P1, step 3): the strips' centre lines, putting the points in order,
		// and the fingerprints.
		private static readonly Stopwatch centreTimer_ = new Stopwatch();
		private static readonly Stopwatch orderTimer_ = new Stopwatch();
		private static readonly Stopwatch printTimer_ = new Stopwatch();

		private static void StartPathTimers(SavedPaths saved) {
			saved_ = saved;
			meshPrints_.Clear();
			pathTimer_.Reset();
			centreTimer_.Reset();
			orderTimer_.Reset();
			printTimer_.Reset();
		}

		private static void EndPathTimers() {
			saved_ = null;
			meshPrints_.Clear();
		}

		private static string PathTimeDetails() {
			string details = $"centre lines {centreTimer_.ElapsedMilliseconds} ms, putting them in order {orderTimer_.ElapsedMilliseconds} ms";
			if (saved_ == null) {
				return details;
			}
			return $"{details}, fingerprints {printTimer_.ElapsedMilliseconds} ms; {saved_.Hits} of {saved_.Hits + saved_.Misses} path(s) remembered from an earlier load";
		}

		/// <summary>
		/// The fingerprint of everything a course's path is worked out from: the mod version and build, the course's name and kind,
		/// its start and finish lines, and every path mesh's exact vertices, faces and placement. Null when the path isn't
		/// remembered: a LineRenderer or child objects (quick to read anyway), a mesh without Read/Write, or no mesh.
		/// </summary>
		private static string PathKey(IList<Transform> pieces, string name, Course course) {
			printTimer_.Start();
			try {
				foreach (Transform piece in pieces) {
					LineRenderer line = piece.GetComponent<LineRenderer>();
					if (line != null && line.positionCount >= 2) {
						return null;
					}
				}
				using (var sha = SHA256.Create()) {
					var print = new PrintWriter(sha);
					// The build's own ID too: the version number stays the same between test builds, and a change to how paths are
					// worked out must never reuse a path an older build saved.
					print.Text($"paths 1|{saved_.ModVersion}|{typeof(CourseScanner).Module.ModuleVersionId}|{name}|{course.IsCircuit}|{MergeDistance}|{MaxPathGap}");
					print.Vector(course.Start.Center);
					print.Vector(course.Start.Forward);
					if (!course.IsCircuit) {
						print.Vector(course.Finish.Center);
					}
					int vertices = 0;
					foreach (MeshFilter filter in pieces.SelectMany(p => p.GetComponentsInChildren<MeshFilter>(true))) {
						Mesh mesh = filter.sharedMesh;
						if (mesh == null) {
							print.Text("no mesh");
							continue;
						}
						if (!mesh.isReadable) {
							return null;
						}
						if (!meshPrints_.TryGetValue(filter, out byte[] meshPrint)) {
							meshPrints_[filter] = meshPrint = MeshPrint(filter.transform.localToWorldMatrix, mesh);
						}
						print.Bytes(meshPrint);
						vertices += mesh.vertexCount;
					}
					if (vertices < 2) {
						return null;
					}
					return BitConverter.ToString(print.Finish()).Replace("-", "");
				}
			} finally {
				printTimer_.Stop();
			}
		}

		private static byte[] MeshPrint(Matrix4x4 placement, Mesh mesh) {
			using (var sha = SHA256.Create()) {
				var print = new PrintWriter(sha);
				for (int i = 0; i < 16; i++) {
					print.Float(placement[i]);
				}
				Vector3[] vertices = mesh.vertices;
				print.Int(vertices.Length);
				foreach (Vector3 v in vertices) {
					print.Vector(v);
				}
				int[] triangles = mesh.triangles;
				print.Int(triangles.Length);
				foreach (int t in triangles) {
					print.Int(t);
				}
				return print.Finish();
			}
		}

		/// <summary>Feeds numbers and text to a SHA-256 in big blocks (one call per number would be slow on big meshes).</summary>
		private sealed class PrintWriter {
			private readonly SHA256 sha_;
			private readonly byte[] buffer_ = new byte[16384];
			private int used_;

			public PrintWriter(SHA256 sha) {
				sha_ = sha;
			}

			public void Int(int value) {
				if (used_ + 4 > buffer_.Length) {
					Flush();
				}
				buffer_[used_++] = (byte)value;
				buffer_[used_++] = (byte)(value >> 8);
				buffer_[used_++] = (byte)(value >> 16);
				buffer_[used_++] = (byte)(value >> 24);
			}

			public void Float(float value) => Int(new FloatBits { Float = value }.Int);

			public void Vector(Vector3 v) {
				Float(v.x);
				Float(v.y);
				Float(v.z);
			}

			public void Text(string text) => Bytes(Encoding.UTF8.GetBytes(text + "\n"));

			public void Bytes(byte[] bytes) {
				Flush();
				sha_.TransformBlock(bytes, 0, bytes.Length, null, 0);
			}

			public byte[] Finish() {
				Flush();
				sha_.TransformFinalBlock(new byte[0], 0, 0);
				return sha_.Hash;
			}

			private void Flush() {
				if (used_ > 0) {
					sha_.TransformBlock(buffer_, 0, used_, null, 0);
					used_ = 0;
				}
			}
		}

		/// <summary>A float's exact bits, so the fingerprint changes when any vertex moves at all.</summary>
		[StructLayout(LayoutKind.Explicit)]
		private struct FloatBits {
			[FieldOffset(0)] public float Float;
			[FieldOffset(0)] public int Int;
		}
	}
}
