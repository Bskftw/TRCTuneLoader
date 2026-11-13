// The course code (CRS-XXXX-XXXX): a fingerprint of the map, kind, lines and path.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// The course code: SHA-256 of the map, the kind, both lines and the path, rounded so float noise
	/// doesn't change it. Two drivers with the same code are on exactly the same course.
	/// </summary>
	public static class CourseCode {
		public static string Compute(Course course) {
			var text = new StringBuilder();
			text.Append("map=").Append(course.MapKey ?? "").Append('\n');
			text.Append("kind=").Append(Course.KindWord(course.Kind)).Append('\n');
			AppendGate(text, "start", course.Start);
			AppendGate(text, "finish", course.Finish);
			text.Append("path=").Append(course.HasPath ? course.Path.Count.ToString(CultureInfo.InvariantCulture) : "none").Append('\n');
			if (course.HasPath) {
				foreach (Vector3 point in course.Path) {
					text.Append(Round(point.x, 1)).Append(',').Append(Round(point.y, 1)).Append(',').Append(Round(point.z, 1)).Append('\n');
				}
			}

			byte[] hash;
			using (var sha = SHA256.Create()) {
				hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()));
			}
			string hex = BitConverter.ToString(hash, 0, 4).Replace("-", "");
			return $"CRS-{hex.Substring(0, 4)}-{hex.Substring(4, 4)}";
		}

		private static void AppendGate(StringBuilder text, string name, CourseGate gate) {
			text.Append(name).Append('=')
				.Append(Round(gate.Center.x, 1)).Append(',').Append(Round(gate.Center.y, 1)).Append(',').Append(Round(gate.Center.z, 1)).Append(',')
				.Append(Round(gate.Forward.x, 3)).Append(',').Append(Round(gate.Forward.z, 3)).Append(',')
				.Append(Round(gate.Width, 1)).Append('\n');
		}

		/// <summary>Rounded half away from zero, "-0" printed as "0", invariant culture.</summary>
		public static string Round(float value, int decimals) {
			double rounded = Math.Round((double)value, decimals, MidpointRounding.AwayFromZero);
			if (rounded == 0) {
				rounded = 0;
			}
			return rounded.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
		}
	}
}
