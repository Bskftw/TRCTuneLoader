// The first-run tour: a notice in the garage, then steps through the window when the player opens it.
using System;
using System.Collections.Generic;
using System.Linq;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		/// <summary>
		/// The tour's version, saved when it's seen. 2: TRC and Project Dream, time attack and battles. 3 (0.14): Home and
		/// everything since. Players who saw any tour get What's new instead of the tour again (Home).
		/// </summary>
		private const int TourVersion = 3;

		/// <summary>How long the player is on the garage screen before the first-run tour opens.</summary>
		private const float TourGarageDelaySeconds = 2f;

		private int tourStep_ = -1;
		private bool tourSeen_;
		private float garageSince_ = -1f;

		private sealed class TourStep {
			public string Title;
			public string Text;

			/// <summary>The tab shown under the step, or -1 for none.</summary>
			public int Tab;
		}

		private TourStep[] TourSteps() => new[] {
			new TourStep {
				Title = "Welcome to TRC Tune Loader",
				Text = "This mod puts the official TRC tunes on your car and checks that it's TRC spec, and it's a whole racing toolkit: time attack, battles, races, " +
					"tournaments, room games, speed traps and ratings. This tour shows each part of the window. Use Next and Back, or Skip if you know your way around.",
				Tab = -1,
			},
			new TourStep {
				Title = "TRC, the tunes and Project Dream",
				Text = "TRC (Touge Racing Community) was founded by WingsX for competitive grip racing, inspired by Japanese touge and Initial D. " +
					"WingsX spent over three years balancing the TRC Ultimate Grip Tunes, so races come down to driver skill, not setups. " +
					"Project Dream is a connected freeroam island of about 52 roads, built for TRC racing. The About tab tells the whole story.",
				Tab = AboutTab,
			},
			new TourStep {
				Title = "Opening this window",
				Text = $"Press <b>{shortcut_}</b> anytime in the game, or open the KSL menu and click TRCTuneLoader. " +
					"Press it again or click × to close the window. You can change the shortcut in Options, and set a wheel or controller button for it too.",
				Tab = -1,
			},
			new TourStep {
				Title = "Home",
				Text = "Home has a card for each part of the mod: TRC spec, Specs, Time attack, Battles and Free roam. Click a card (or one of the buttons on it) to go in; " +
					"inside, the parts are across the top and each one's pages are down the left, and the logo brings you back here. Under the cards is anything waiting for you " +
					"and the room board: what every TRC Tune Loader player in the room is doing. The tools are along the bottom, with Report a bug. " +
					"On a controller: the D-pad moves, A presses, B goes back, LB and RB change part.",
				Tab = HomeTab,
			},
			new TourStep {
				Title = "TRC Tunes: the status bar",
				Text = "TRC Tunes shows the car you're in and its setup. The coloured bar says whether it's TRC spec right now: " +
					"green is spec, amber means it hasn't been fully checked yet, and red means something isn't spec.",
				Tab = MyCarTab,
			},
			new TourStep {
				Title = "Putting a TRC tune on",
				Text = "Pick a tune set (TRC Revival is the newest), then press Apply on the main garage screen. " +
					"When CarX's tuning screen is open, the button says Load into the dyno instead, and that works in online rooms too: " +
					"leave the tuning screen and choose Yes to save. Your own tune is always backed up first.",
				Tab = MyCarTab,
			},
			new TourStep {
				Title = "Checking your car",
				Text = "Check my tune lists anything that isn't TRC spec: your saved setup, the car you're driving, body kit stance, " +
					"Workshop bodies, engine swaps, and gearbox or drivetrain changes from mods like KINO. " +
					"Turn on the TRC spec badge (Options) and a small tag in a corner shows whether you're spec while you drive; turn on Check my car on the track and a message pops up if something isn't. " +
					"Your code (like TRC-95F4-06D9) is what the TRC bot uses to verify leaderboard runs.",
				Tab = MyCarTab,
			},
			new TourStep {
				Title = "Tune sets, Players, Mod check",
				Text = "Tune sets lists every official set built into the mod. Players checks every car in an online room. " +
					"Mod check (in Tools) lists the mods KSL has loaded. Your own tunes are kept in Backups, beside the tune sets in TRC Tunes, whenever a TRC tune replaces one.",
				Tab = PlayersTab,
			},
			new TourStep {
				Title = "Specs and regulations",
				Text = "Make your own spec: a tune for each car, like TRC's V1, V2, V3 and Revival sets. Save your car's tune into it, share it as a zip, and install other players' specs " +
					"by dropping theirs in your specs folder. A regulation can require a spec, so you can race to it, and Time attack can be driven to one.",
				Tab = SpecsTab,
			},
			new TourStep {
				Title = "Time attack",
				Text = "Tracks lists the courses on the map you're driving: touge, highway and circuits. Press Time attack and stop behind the start line for 3-2-1-GO " +
					"(or pick a flying start, and laps on a circuit). Your sectors stay on the timer in purple, green or amber, the live delta shows how far ahead or behind your best you are, " +
					$"and your best line is drawn on the road. {restartShortcut_} sends you back to the start.",
				Tab = CoursesTab,
			},
			new TourStep {
				Title = "Speed traps",
				Text = "Drive through a speed trap and your speed flashes up: purple for a new best, green close to it, amber slower, and its SPEED CHECK sign shows your last speed. " +
					"Speed traps (in Free roam) lists the map's traps grouped by track; New speed trap places your own, with a name and a width. A course's Edit can add traps along it.",
				Tab = CoursesTab,
			},
			new TourStep {
				Title = "Battles, races and fun games",
				Text = "Battles has 1v1 battles (side by side, cat and mouse, or a Tokyo Xtreme Racer style highway battle on SP), Races and Tournaments " +
					"(qualifying, grids, points series, knockouts, heats, and speed trap races; console players can race too), and Fun games: cops and runners, tag, infected, king of the hill, keep-up, convoy and highway cut-up. " +
					"Run any of them to TRC spec, a regulation (Specs, Regulations) or Open, and just for fun if you like. When it's over, Run it back plays it again.",
				Tab = BattlesTab,
			},
			new TourStep {
				Title = "My times and ratings",
				Text = "My times (in Time attack) keeps your best times and laps for each car and tune set; you can delete times there. My ratings (in Free roam) shows your Elo for touge, highway and circuit, " +
					"your Safety Rating and your license. Ratings are still being tested and will be reset before they count. The Course builder makes your own courses.",
				Tab = CoursesTab,
			},
			new TourStep {
				Title = "Options",
				Text = $"Change the shortcut, the restart key, the start key ({startShortcut_}: starts the track open in Tracks), wheel or controller buttons, the window size, the colours and text size (Look), " +
					"the badge and track check, sectors (and your own manual ones) and best line, and how many backups to keep, or take this tour again. Report a bug is in Tools. See you on the touge!",
				Tab = OptionsTab,
			},
		};

		/// <summary>Called from Update until the tour has been seen: once the player has been in the garage for a moment, a notice
		/// says how to open the window (once per start of the game). The tour starts when they open it themselves: the window
		/// never opens by itself (bskftw, Nov 9).</summary>
		private void StartTourWhenReady() {
			if (tourSeen_ || windowOpen_ || tourNoticeShown_) {
				return;
			}

			RefreshCarIfDue();
			if (car_ == null || !car_.InGarage || car_.InDyno) {
				garageSince_ = -1f;
				return;
			}
			if (garageSince_ < 0f) {
				garageSince_ = Time.unscaledTime;
			}
			if (Time.unscaledTime - garageSince_ >= TourGarageDelaySeconds) {
				tourNoticeShown_ = true;
				garageSince_ = -1f;
				ShowNotice($"<b>TRC Tune Loader is installed</b>\n<color={Theme.MutedHex}>Press {shortcut_} (or open it from the KSL menu) for a quick tour</color>", 12f);
			}
		}

		private bool tourNoticeShown_;

		private void StartTour() => GoToTourStep(0);

		private void GoToTourStep(int step) {
			TourStep[] steps = TourSteps();
			tourStep_ = Mathf.Clamp(step, 0, steps.Length - 1);
			int tab = steps[tourStep_].Tab;
			if (tab >= 0 && tab != tab_) {
				tab_ = tab;
				OnTabOpened(tab);
			}
		}

		/// <summary>Ends the tour (finished, skipped or the window closed) and remembers that it's been seen.</summary>
		private void EndTour() {
			tourStep_ = -1;
			if (tourSeen_ && Kino.Prefs.Get(TourPref, 0) >= TourVersion) {
				return;
			}
			tourSeen_ = true;
			try {
				Kino.Prefs.Set(TourPref, TourVersion);
				// A new player who took the tour doesn't need What's new as well.
				whatsNewPending_ = false;
				Kino.Prefs.Set(WhatsNewPref, WhatsNewVersion);
			} catch (Exception e) {
				TrcLog.Warning($"Couldn't save that the tour was seen: {e.Message}");
			}
		}

		/// <summary>The tour's card above the tab. Returns whether the step shows a tab under it.</summary>
		private bool DrawTourCard() {
			TourStep[] steps = TourSteps();
			TourStep step = steps[Mathf.Clamp(tourStep_, 0, steps.Length - 1)];
			bool last = tourStep_ >= steps.Length - 1;

			GUILayout.BeginVertical(Theme.Guide);
			GUILayout.Label($"Tour · {tourStep_ + 1} of {steps.Length}", Theme.Group);
			GUILayout.Label(step.Title, Theme.Heading);
			GUILayout.Label(step.Text, Theme.Body);
			GUILayout.Space(4);
			GUILayout.BeginHorizontal();
			if (!last && NavButton("Skip tour", Theme.Button)) {
				EndTour();
			}
			GUILayout.FlexibleSpace();
			if (tourStep_ > 0 && NavButton("Back", Theme.Button)) {
				GoToTourStep(tourStep_ - 1);
			}
			if (NavButton(last ? "Finish" : "Next", Theme.ButtonPrimary)) {
				if (last) {
					EndTour();
				} else {
					GoToTourStep(tourStep_ + 1);
				}
			}
			GUILayout.EndHorizontal();
			GUILayout.EndVertical();

			return step.Tab >= 0;
		}
	}
}
