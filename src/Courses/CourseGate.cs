// A course's start or finish line: where it is, which way it faces, and how wide.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// A start or finish line: a line across the road through <see cref="Center"/>, <see cref="Width"/>
	/// wide, crossed in the <see cref="Forward"/> direction.
	/// </summary>
	public sealed class CourseGate {
		public Vector3 Center;

		/// <summary>The way you drive through the line, level (no up or down) and one metre long.</summary>
		public Vector3 Forward = Vector3.forward;
		public float Width = CourseGeometry.DefaultWidth;

		/// <summary>Level and to the right of <see cref="Forward"/>.</summary>
		public Vector3 Right => new Vector3(Forward.z, 0f, -Forward.x);
		public Vector3 LeftEnd => Center - Right * (Width / 2f);
		public Vector3 RightEnd => Center + Right * (Width / 2f);

		/// <summary>How far past the line a point is, along <see cref="Forward"/>; negative = behind it.</summary>
		public float Ahead(Vector3 point) => Vector3.Dot(point - Center, Forward);

		/// <summary>How far right of the middle of the line a point is; negative = left.</summary>
		public float Across(Vector3 point) => Vector3.Dot(point - Center, Right);

		/// <summary>A gate whose direction is <paramref name="direction"/> made level. False when it points straight up or down.</summary>
		public static bool TryMake(Vector3 center, Vector3 direction, float width, out CourseGate gate) {
			var level = new Vector3(direction.x, 0f, direction.z);
			gate = new CourseGate { Center = center, Width = width > 0.5f ? width : CourseGeometry.DefaultWidth };
			if (level.magnitude < 0.1f) {
				return false;
			}
			gate.Forward = level.normalized;
			return true;
		}
	}
}
