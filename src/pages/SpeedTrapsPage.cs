// The Speed traps page (Time attack > Speed traps): the map's traps in groups, placing, editing and random traps.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		private readonly HashSet<string> trapGroupsOpen_ = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		private Vector2 trapScroll_;

		/// <summary>The map's speed traps, grouped by name ("Tsubaki Speed Trap 1, 2..." fold under "Tsubaki"), with your bests; placing and editing your own.</summary>
		private void DrawSpeedTraps() {
			GUILayout.BeginHorizontal();
			int on = Choice(new[] { "Free-roam traps on", "Off" }, freeRoamTraps_ ? 0 : 1);
			if ((on == 0) != freeRoamTraps_) {
				freeRoamTraps_ = on == 0;
				SavePref(FreeRoamTrapsPref, freeRoamTraps_);
			}
			GUILayout.FlexibleSpace();
			bool enabled = GUI.enabled;
			GUI.enabled = enabled && map_ != null && !sharingTraps_ && mapTraps_.Any(t => t.Source == CourseSource.Custom);
			if (NavButton("Share...", Theme.Button)) {
				sharingTraps_ = true;
				placingTrap_ = false;
			}
			GUI.enabled = enabled && hasPose_ && map_ != null && !placingTrap_;
			if (NavButton("New speed trap", Theme.ButtonPrimary)) {
				StartPlacingTrap();
				sharingTraps_ = false;
			}
			GUI.enabled = enabled;
			GUILayout.EndHorizontal();
			GUILayout.Label("They catch your speed in free roam, and during time attacks, battles and races too. Name them like \"Tsubaki Speed Trap 1\" and they're grouped by track.", Theme.Small);

			if (placingTrap_) {
				DrawPlaceTrap();
			}
			DrawRandomTraps();
			DrawSharedTraps();
			if (sharingTraps_) {
				DrawShareTraps();
			}

			EnsureTrapList();
			GUILayout.BeginVertical(Theme.Card, GUILayout.ExpandHeight(true));
			trapScroll_ = GUILayout.BeginScrollView(trapScroll_);
			if (map_ == null) {
				GUILayout.Label("Drive onto a map to see its speed traps.", Theme.Small);
			} else if (mapTraps_.Count == 0) {
				GUILayout.Label("No speed traps on this map yet. Press New speed trap, stop on the road where you want one, and place it.", Theme.Small);
			}
			foreach (TrapGroup group in trapGroups_) {
				List<SpeedTrap> traps = group.Traps;
				if (group.Key.Length == 0 && trapGroups_.Count == 1) {
					// Nothing named into groups yet: just the list.
					traps.ForEach(DrawTrapRow);
					continue;
				}
				string title = group.Key.Length == 0 ? "Other speed traps" : group.Key;
				bool open = trapGroupsOpen_.Contains(title) || (trapEditing_ != null && traps.Contains(trapEditing_));
				float best = group.Best;
				if (NavButton($"<b>{title}</b>  <color={Theme.MutedHex}>{traps.Count} trap(s){(best > 0 ? $" · fastest {best:0} km/h{group.BestCar}" : "")}</color>", Theme.Row)) {
					if (!trapGroupsOpen_.Remove(title)) {
						trapGroupsOpen_.Add(title);
					}
				}
				if (open) {
					if (group.Key.Length > 0 && traps.All(t => t.Source == CourseSource.Custom)) {
						GUILayout.BeginHorizontal();
						GUILayout.Space(18);
						if (trapGroupDeletePending_ == title) {
							GUILayout.Label($"Delete all {traps.Count} traps in {title}? Their speeds stay saved.", Theme.Small);
							if (NavButton("Delete them", Theme.ButtonPrimary)) {
								DeleteTrapGroup(title);
							}
							if (NavButton("Keep", Theme.Button)) {
								trapGroupDeletePending_ = "";
							}
						} else if (NavButton("Delete this group", Theme.Button)) {
							trapGroupDeletePending_ = title;
						}
						GUILayout.FlexibleSpace();
						GUILayout.EndHorizontal();
					}
					GUILayout.BeginHorizontal();
					GUILayout.Space(18);
					GUILayout.BeginVertical();
					traps.ForEach(DrawTrapRow);
					GUILayout.EndVertical();
					GUILayout.EndHorizontal();
				}
			}
			GUILayout.EndScrollView();
			GUILayout.EndVertical();
		}

		// The page's list, made again only when the traps or the records change, or once a second (bskftw, Nov 6: the tab
		// lagged; it grouped, sorted and searched every record for every trap several times a frame).
		private sealed class TrapGroup {
			public string Key = "";
			public List<SpeedTrap> Traps;
			public float Best;
			public string BestCar = "";
		}

		private List<TrapGroup> trapGroups_ = new List<TrapGroup>();
		private readonly Dictionary<string, float> trapBest_ = new Dictionary<string, float>(StringComparer.Ordinal);
		private readonly Dictionary<string, float> trapBestCar_ = new Dictionary<string, float>(StringComparer.Ordinal);
		/// <summary>The car each trap's fastest speed was set in.</summary>
		private readonly Dictionary<string, string> trapBestBy_ = new Dictionary<string, string>(StringComparer.Ordinal);
		private List<SpeedTrap> trapListFor_;
		private int trapListTraps_ = -1, trapListRecords_ = -1;
		private float trapListAt_ = -100f;

		private void EnsureTrapList() {
			if (trapListFor_ == mapTraps_ && trapListTraps_ == mapTraps_.Count && trapListRecords_ == trapRecords_.Count && Time.unscaledTime - trapListAt_ < 1f) {
				return;
			}
			trapListFor_ = mapTraps_;
			trapListTraps_ = mapTraps_.Count;
			trapListRecords_ = trapRecords_.Count;
			trapListAt_ = Time.unscaledTime;
			trapBest_.Clear();
			trapBestCar_.Clear();
			trapBestBy_.Clear();
			foreach (SpeedTrapRecord r in trapRecords_) {
				if (!trapBest_.TryGetValue(r.TrapKey, out float best) || r.Kmh > best) {
					trapBest_[r.TrapKey] = r.Kmh;
					trapBestBy_[r.TrapKey] = r.Car ?? "";
				}
				string car = r.TrapKey + "|" + r.CarId;
				if (!trapBestCar_.TryGetValue(car, out float mine) || r.Kmh > mine) {
					trapBestCar_[car] = r.Kmh;
				}
			}
			trapGroups_ = mapTraps_.GroupBy(t => t.Group, StringComparer.OrdinalIgnoreCase)
				.OrderBy(g => g.Key.Length == 0 ? 1 : 0).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
				.Select(g => {
					List<SpeedTrap> traps = g.OrderBy(t => SpeedTrap.NumberOf(t.Name)).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
					SpeedTrap top = traps.OrderByDescending(t => trapBest_.TryGetValue(t.Key, out float b) ? b : 0f).FirstOrDefault();
					float best = top != null && trapBest_.TryGetValue(top.Key, out float kmh) ? kmh : 0f;
					return new TrapGroup { Key = g.Key, Traps = traps, Best = best, BestCar = best > 0 ? CarWhoSet(top.Key) : "" };
				}).ToList();
		}

		/// <summary>" (LYNX)": the car a trap's fastest speed was set in, or "".</summary>
		private string CarWhoSet(string key) => trapBestBy_.TryGetValue(key, out string car) && car.Length > 0 ? $" ({ShortCar(car)})" : "";

		private void DrawTrapRow(SpeedTrap trap) {
			float mine = trapBestCar_.TryGetValue(trap.Key + "|" + pose_.CarId, out float m) ? m : 0f;
			float best = trapBest_.TryGetValue(trap.Key, out float b) ? b : 0f;
			float away = hasPose_ ? Vector3.Distance(pose_.Position, trap.Gate.Center) : 0f;
			GUILayout.BeginHorizontal();
			GUILayout.Label($"<b>{trap.Name}</b>  <color={Theme.MutedHex}>{(trap.Source == CourseSource.Map ? "map" : "yours")} · {trap.Gate.Width:0} m wide · {TimeAttackRun.Distance(away)} away" +
				$"{(best > 0 ? $" · best {best:0} km/h{CarWhoSet(trap.Key)}" : "")}{(mine > 0 && mine < best ? $" · this car {mine:0}" : "")}</color>", Theme.Body);
			GUILayout.FlexibleSpace();
			if (trap.Source == CourseSource.Custom && trapEditing_ != trap && NavButton("Edit", Theme.Button)) {
				trapEditing_ = trap;
				trapEditName_ = trap.Name;
				trapEditWidth_ = trap.Gate.Width;
				trapDeletePending_ = null;
				placingTrap_ = false;
			}
			GUILayout.EndHorizontal();
			if (trapEditing_ == trap) {
				DrawEditTrap(trap);
			}
		}

		private void DrawPlaceTrap() {
			GUILayout.BeginVertical(Theme.Guide);
			GUILayout.Label("New speed trap", Theme.Heading);
			GUILayout.Label("Stop on the road where you want it, facing along the road. The line on the road shows how wide it is: pick a width that covers the road.", Theme.Body);
			GUILayout.Label("Name", Theme.SectionLabel);
			newTrapName_ = GUILayout.TextField(newTrapName_ ?? "", 60, Theme.Field);
			GUILayout.Label("Width", Theme.SectionLabel);
			DrawTrapWidths(ref newTrapWidth_);
			GUILayout.BeginHorizontal();
			bool enabled = GUI.enabled;
			GUI.enabled = enabled && hasPose_ && TuneRules.Trim(newTrapName_).Length > 0;
			if (NavButton("Place it here", Theme.ButtonPrimary)) {
				AddFreeRoamTrap();
			}
			GUI.enabled = enabled;
			if (NavButton("Done", Theme.Button)) {
				placingTrap_ = false;
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.EndVertical();
		}

		private void DrawEditTrap(SpeedTrap trap) {
			GUILayout.BeginVertical(Theme.Card);
			GUILayout.Label("Name", Theme.SectionLabel);
			trapEditName_ = GUILayout.TextField(trapEditName_ ?? "", 60, Theme.Field);
			GUILayout.Label("Width (the line on the road shows it while you edit)", Theme.SectionLabel);
			DrawTrapWidths(ref trapEditWidth_);
			GUILayout.BeginHorizontal();
			if (NavButton("Save", Theme.ButtonPrimary)) {
				EditFreeRoamTrap(trap, TuneRules.Trim(trapEditName_), trapEditWidth_, move: false);
			}
			bool enabled = GUI.enabled;
			GUI.enabled = enabled && hasPose_;
			if (NavButton("Move it here", Theme.Button)) {
				EditFreeRoamTrap(trap, TuneRules.Trim(trapEditName_), trapEditWidth_, move: true);
			}
			GUI.enabled = enabled;
			if (trapDeletePending_ == trap) {
				if (NavButton("Delete it", Theme.ButtonPrimary)) {
					DeleteFreeRoamTrap(trap);
				}
				if (NavButton("Keep", Theme.Button)) {
					trapDeletePending_ = null;
				}
			} else if (NavButton("Delete", Theme.Button)) {
				trapDeletePending_ = trap;
			}
			if (NavButton("Cancel", Theme.Button)) {
				trapEditing_ = null;
				trapDeletePending_ = null;
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.EndVertical();
		}

		private void DrawTrapWidths(ref float width) {
			float current = width;
			int index = Array.FindIndex(TrapWidths, w => Math.Abs(w - current) < 0.01f);
			int picked = Choice(TrapWidths.Select(w => $"{w:0} m").ToArray(), index);
			if (picked >= 0 && picked != index) {
				width = TrapWidths[picked];
			}
		}

		private static readonly int[] RandomTrapCounts = { 3, 5, 8, 10, 15 };
		private bool randomTrapsOpen_;
		private int randomTrapCount_ = 1;
		private string randomTrapsCourse_ = "";
		private readonly CoursePicker randomTrapsPicker_ = new CoursePicker();
		private string trapGroupDeletePending_ = "";

		private void DrawRandomTraps() {
			if (!randomTrapsOpen_) {
				GUILayout.BeginHorizontal();
				bool enabled = GUI.enabled;
				GUI.enabled = enabled && map_ != null && CoursesHere().Any(c => c.HasPath);
				if (NavButton("Random traps on a track...", Theme.Button)) {
					randomTrapsOpen_ = true;
				}
				GUI.enabled = enabled;
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
				return;
			}
			GUILayout.BeginVertical(Theme.Guide);
			GUILayout.Label("Random speed traps on a track", Theme.Heading);
			GUILayout.Label("Spread along the track at random, grouped as \"<track> Random\". Doing it again for the same track puts new ones in their place; " +
				"Delete this group (open the group below) takes them all away.", Theme.Small);
			// The buttons above the list, so a long list never pushes them out of the window (bskftw, Nov 3).
			List<Course> withPaths = CoursesHere().Where(c => c.HasPath && c.Length > 300f).ToList();
			GUILayout.BeginHorizontal();
			GUILayout.Label("How many", Theme.SectionLabel, GUILayout.Width(80));
			randomTrapCount_ = Choice(RandomTrapCounts.Select(n => n.ToString(CultureInfo.InvariantCulture)).ToArray(), randomTrapCount_);
			GUILayout.EndHorizontal();
			GUILayout.BeginHorizontal();
			Course picked = withPaths.FirstOrDefault(c => c.Code == randomTrapsCourse_);
			bool on = GUI.enabled;
			GUI.enabled = on && picked != null;
			if (NavButton("Place them", Theme.ButtonPrimary)) {
				PlaceRandomTraps(picked, RandomTrapCounts[randomTrapCount_]);
			}
			GUI.enabled = on;
			if (NavButton("Close", Theme.Button)) {
				randomTrapsOpen_ = false;
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			randomTrapsCourse_ = PickCourse(randomTrapsPicker_, withPaths, randomTrapsCourse_,
				c => $"<b>{c.Name}</b>  <color={Theme.MutedHex}>{c.KindName} · {TimeAttackRun.Distance(c.Length)}</color>", 170f);
			GUILayout.EndVertical();
		}
	}
}
