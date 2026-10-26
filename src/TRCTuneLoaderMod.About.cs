using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// The About tab: the story of TRC, the TRC Ultimate Grip Tunes and Project Dream, from the background notes
	/// TRC wrote (docs/source/TRC and Project Dream background.md). The tour has the short version.
	/// </summary>
	public partial class TRCTuneLoaderMod {
		private const int AboutTab = 8;
		private const string ModPage = "https://github.com/Bskftw/TRCTuneLoader";

		private Vector2 aboutScroll_;
		private List<string> aboutLines_;

		private void DrawAboutTab() {
			if (aboutLines_ == null) {
				aboutLines_ = AboutLines();
			}
			GUILayout.Label("TRC, the TRC tunes and Project Dream", Theme.Heading);
			DrawLines(ref aboutScroll_, aboutLines_);
			GUILayout.BeginHorizontal();
			if (GUILayout.Button("Mod page on GitHub", Theme.Button)) {
				Application.OpenURL(ModPage);
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
		}

		/// <summary>The About text; lines starting with "---" are headings, empty lines are gaps.</summary>
		private List<string> AboutLines() {
			string sets = string.Join(", ", official_.Sets.Select(s =>
				$"{TuneSets.NameOf(s)}{(s == TuneSets.Main.Id ? " (the main, newest set)" : "")} with {official_.Tunes.Count(t => t.SetId == s)} cars"));

			return new List<string> {
				"--- TRC: Touge Racing Community ---",
				"TRC is a CarX Drift Racing Online community founded by <b>WingsX</b>. It's built around competitive grip racing, inspired by Japanese touge culture, Initial D and real mountain passes.",
				"",
				"TRC is about respectful competition, close and clean racing, getting better as a driver, learning and teaching, and welcoming new drivers. " +
					"Veterans and brand-new drivers are all welcome to join voice chat, ask questions, learn the roads and take part in events. Above all, it's about having fun while you improve.",

				"--- The TRC Ultimate Grip Tunes ---",
				"In a lot of racing communities, results come down to who has the best setup instead of who drives best. " +
					"To fix that, WingsX spent more than three years building a collection of carefully balanced grip tunes for touge racing: the <b>TRC Ultimate Grip Tunes</b>.",
				"",
				"• A level playing field, with no pay-to-win or setup advantages.",
				"• Every car feels unique, but stays competitive.",
				"• Races are decided by driver skill.",
				"• Battles are close, clean and exciting.",
				"",
				"Every tune has its own strengths, weaknesses, driving feel and personality, and all of them are balanced against each other. " +
					"Instead of hunting for a better setup, drivers put their time into their driving, and that's why so many get hooked.",
				"",
				$"Built into this mod: {sets}.",

				"--- Project Dream ---",
				"Project Dream would never have existed without TRC. After a lot of time racing TRC tunes, its creator, <b>bskftw</b>, wanted to give something bigger back: " +
					"first by uploading tracks to CarX, then with a whole freeroam world made for touge racing.",
				"",
				"WingsX drew the first concept map. The aim was one connected world with as many legendary Japanese touge roads as possible, placed the way they really relate to each other. " +
					"Akina (Mount Haruna) and Akagi, for example, sit where you can see one from the other. The scale is smaller than real life, but the layout is believable.",
				"",
				"Over about ten months, roughly 45 roads, mountain passes, highways, rally stages and areas made by many talented 3D artists were joined into one seamless island, " +
					"with no loading screens between them. Today it has about 52 roads and areas:",
				"• Touge roads like Akina, Akagi, Irohazaka, Myogi, Usui Pass, Tsubaki Line, Hakone Turnpike, Happogahara and many more.",
				"• Highways and city: the Shutoko lines, Daikoku and Ikebukuro.",
				"• Rally stages: Forest Rally and Lake Louise Rally.",
				"• Carbon-inspired areas: Lookout Point and Gold Valley Run.",
				"• Lake Akina, hidden easter eggs, points of interest and places to meet up.",
				"",
				"Why \"Project Dream\"? WingsX really did dream about the map before work began. The project is full of Initial D. " +
					"And it's a dream the community shares: a whole touge world built for the way TRC races.",

				"--- TRC and Project Dream together ---",
				"They started separately and grew up together. TRC brings the community, the racing culture and the balanced tunes. " +
					"Project Dream brings the world, the roads, the adventures and the events. Together they make something you won't find anywhere else in CarX Drift Racing Online.",

				"--- What's coming ---",
				"Project Dream isn't finished: more touges and highways, more of the island, new points of interest and easter eggs, community events, endurance races, championships and tournaments are planned.",
				"",
				"This mod is part of it. It keeps every car TRC spec, and its time attack and battles are the start of TRC's leaderboards and ranked racing.",

				"--- This mod ---",
				$"TRC Tune Loader {Version}, made by bskftw for TRC. The tunes are TRC's, made by WingsX.",
			};
		}
	}
}
