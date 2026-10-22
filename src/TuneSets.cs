using System;
using System.Collections.Generic;

namespace TRC.TuneLoader {
	/// <summary>
	/// The official TRC tune sets, main set first. A set's ID is its folder name under official_tunes/.
	/// The tune code doesn't include the set, so renaming a set never changes any code.
	/// </summary>
	public static class TuneSets {
		public sealed class Set {
			public readonly string Id;
			public readonly string Name;
			public readonly bool IsMain;

			public Set(string id, string name, bool isMain = false) {
				Id = id;
				Name = name;
				IsMain = isMain;
			}
		}

		public static readonly List<Set> All = new List<Set> {
			// The V2.1 test set TRC never released, released as "Revival": the newest and main set.
			new Set("Revival", "TRC Revival", isMain: true),
			new Set("V3.0", "TRC V3.0"),
			new Set("V2.6", "TRC V2.6"),
			new Set("V2.0", "TRC V2.0"),
			new Set("V1.0", "TRC V1.0"),
		};

		public static Set Main => All.Find(s => s.IsMain) ?? All[0];

		/// <summary>Position in the list (main set first); unknown sets go last.</summary>
		public static int Order(string id) {
			int index = All.FindIndex(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
			return index >= 0 ? index : All.Count;
		}

		public static string NameOf(string id) =>
			All.Find(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase))?.Name ?? $"TRC {id}";
	}
}
