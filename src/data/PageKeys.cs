// Saved page keys ("tab:view", in pinned pages) kept pointing at the same page when the menu's pages are renumbered.
using System.Collections.Generic;

namespace TRC.TuneLoader {
	/// <summary>
	/// Pinned pages are saved as "tab:view" (NavPage.Key). When pages are merged or renumbered, an old key would open the
	/// wrong page, so each renumbering adds a step here and the saved list is moved forward once (the pinned_pages_layout pref).
	/// Never reuse a key for a different page without a step here. No game code, so the self-test checks it.
	/// </summary>
	public static class PageKeys {
		/// <summary>The layout the keys are saved in now.</summary>
		public const int Layout = 2;

		/// <summary>Each step: from this layout to the next, old key to new key ("" when the page is gone).</summary>
		private static readonly Dictionary<string, string>[] Steps = {
			// 1 to 2 (Nov 18): Regulations and "Make or edit" became one page; "Shared with you" moved from view 2 to 1.
			new Dictionary<string, string> { { "7:1", "7:0" }, { "7:2", "7:1" } },
		};

		/// <summary>Every key a step moves a pin to (the menu checks each one is a real page).</summary>
		public static IEnumerable<string> Targets {
			get {
				foreach (Dictionary<string, string> step in Steps) {
					foreach (string to in step.Values) {
						if (to.Length > 0) {
							yield return to;
						}
					}
				}
			}
		}

		/// <summary>Saved keys from <paramref name="layout"/> (1 when it was never saved), as they are in <see cref="Layout"/>.</summary>
		public static List<string> Migrate(IEnumerable<string> keys, int layout) {
			var result = new List<string>();
			foreach (string key in keys) {
				string now = key;
				for (int step = System.Math.Max(1, layout); step < Layout && now.Length > 0; step++) {
					if (Steps[step - 1].TryGetValue(now, out string moved)) {
						now = moved;
					}
				}
				if (now.Length > 0 && !result.Contains(now)) {
					result.Add(now);
				}
			}
			return result;
		}
	}
}
