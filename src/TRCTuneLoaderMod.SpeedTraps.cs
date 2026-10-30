using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// Speed traps in game: free-roam traps on the map (its own "TRC Speed Trap" objects and the player's), the traps
	/// along a course during a run, the flash when the car goes through one, every speed saved, placing, naming and
	/// sizing traps, and the floating "SPEED CHECK" sign over each one (a line on the road only while placing or
	/// editing one, to see how wide it is).
	/// </summary>
	public partial class TRCTuneLoaderMod {
		private const string FreeRoamTrapsPref = "freeroam_speed_traps";
		private const string TrapWidthPref = "trap_width";
		private const float TrapFlashSeconds = 4f;
		private static readonly float[] TrapWidths = { 4f, 6f, 8f, 10f, 12f, 16f, 20f, 30f };

		/// <summary>The sign floats this high (metres) over the middle of the trap, this wide, and shows from this far.</summary>
		private const float SignHeight = 5.5f;
		private const float SignWidth = 6f;
		private const float SignRange = 300f;

		/// <summary>A picture in the mod's folder by this name replaces the built-in sign.</summary>
		public const string SignFileName = "speed_trap_sign.png";

		private readonly SpeedTrapWatcher trapWatcher_ = new SpeedTrapWatcher();
		private readonly TrapMarkers trapMarkers_ = new TrapMarkers();
		private List<SpeedTrap> mapTraps_ = new List<SpeedTrap>();
		private List<SpeedTrapRecord> trapRecords_ = new List<SpeedTrapRecord>();
		private bool freeRoamTraps_ = true;
		private string trapFlash_ = "";
		private float trapFlashUntil_;
		private (int Index, float Kmh)? runTrapSeen_;
		private SpeedTrap trapDeletePending_;

		// Placing and editing.
		private bool placingTrap_;
		private string newTrapName_ = "";
		private float newTrapWidth_ = 10f;
		private SpeedTrap trapEditing_;
		private string trapEditName_ = "";
		private float trapEditWidth_;
		private CourseGate trapPreview_;
		private readonly HashSet<string> trapGroupsOpen_ = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		private Vector2 trapScroll_;

		// The signs.
		private Texture2D signTexture_;
		private bool signLoaded_;
		private GUIStyle signNumber_;
		private GUIStyle signName_;
		private readonly List<(string Key, CourseGate Gate, string Name)> signs_ = new List<(string, CourseGate, string)>();
		private readonly HashSet<string> signsHidden_ = new HashSet<string>(StringComparer.Ordinal);
		private float nextSignCheck_;

		/// <summary>The last speed caught at each trap (this session, else the newest saved for this car).</summary>
		private readonly Dictionary<string, float> lastTrapKmh_ = new Dictionary<string, float>(StringComparer.Ordinal);

		private string TrapsFile => map_ == null ? "" : Path.Combine(CoursesFolder, TuneBackups.SafeFileName(map_.Key.Replace(':', '_')), SpeedTrapFile.FileName);

		private void LoadTrapRecords() {
			try {
				trapRecords_ = SpeedTrapLog.Load(TimesFolder);
				freeRoamTraps_ = KSL.API.Kino.Prefs.Get(FreeRoamTrapsPref, true);
				float width = KSL.API.Kino.Prefs.Get(TrapWidthPref, 10f);
				newTrapWidth_ = TrapWidths.OrderBy(w => Math.Abs(w - width)).First();
			} catch (Exception e) {
				TrcLog.Error(e);
			}
		}

		/// <summary>The map's traps: its own objects (from the last scan) and the player's, for this map.</summary>
		private void RefreshTraps() {
			mapTraps_ = new List<SpeedTrap>(CourseScanner.SpeedTraps.Where(t => map_ != null && t.MapKey == map_.Key));
			if (map_ != null) {
				try {
					mapTraps_.AddRange(SpeedTrapFile.Load(TrapsFile, map_.Key));
				} catch (Exception e) {
					TrcLog.Error(e);
				}
			}
			trapWatcher_.Reset();
		}

		/// <summary>The course being driven (a run, a battle or a race), whose own traps count in it.</summary>
		private Course ActiveCourse => InRace && race_.Course != null ? race_.Course : InBattle ? battle_.Course : run_?.Course;

		/// <summary>
		/// Called from UpdateCourses every frame. Free-roam traps work all the time when they're on, during a time
		/// attack, a battle or a race too (since 0.15.0), except one sitting where the course has its own trap.
		/// </summary>
		private void UpdateSpeedTraps() {
			if (!hasPose_) {
				trapWatcher_.Reset();
				trapMarkers_.Show(null, Vector3.zero);
				signs_.Clear();
				return;
			}
			Course active = ActiveCourse;
			List<CourseGate> courseTraps = active?.Traps ?? new List<CourseGate>();
			bool freeRoam = freeRoamTraps_ && recorder_.State == CourseRecorder.Phase.Idle;
			List<SpeedTrap> watched = freeRoam
				? mapTraps_.Where(t => !courseTraps.Any(c => Vector3.Distance(c.Center, t.Gate.Center) < 3f)).ToList()
				: new List<SpeedTrap>();
			if (watched.Count > 0) {
				SpeedTrapHit? hit = trapWatcher_.Tick(Time.timeAsDouble, pose_.Position, pose_.SpeedKmh, watched);
				if (hit.HasValue) {
					TrapCaught(hit.Value.Trap, hit.Value.Kmh, "freeroam");
				}
			} else {
				trapWatcher_.Reset();
			}

			// A run's course traps: caught by the run itself; flashed and saved here.
			if (run_ != null && run_.LastTrap.HasValue && !run_.LastTrap.Equals(runTrapSeen_)) {
				runTrapSeen_ = run_.LastTrap;
				var (index, kmh) = run_.LastTrap.Value;
				TrapCaught(CourseTrap(run_.Course, index), kmh, run_.Course.Code);
			} else if (run_ == null || !run_.LastTrap.HasValue) {
				runTrapSeen_ = null;
			}

			// The line on the road only for the trap being placed or edited; every trap gets its sign.
			Camera camera = HudCamera();
			UpdateTrapPreview();
			trapMarkers_.Show(camera != null && trapPreview_ != null ? new List<CourseGate> { trapPreview_ } : null, camera != null ? camera.transform.position : Vector3.zero);

			signs_.Clear();
			if (freeRoam) {
				foreach (SpeedTrap trap in mapTraps_) {
					signs_.Add((trap.Key, trap.Gate, trap.Name));
				}
			}
			if (active != null) {
				for (int i = 0; i < courseTraps.Count; i++) {
					SpeedTrap trap = CourseTrap(active, i);
					signs_.Add((trap.Key, trap.Gate, trap.Name));
				}
			}
			if (camera != null && Time.unscaledTime >= nextSignCheck_) {
				nextSignCheck_ = Time.unscaledTime + 0.25f;
				CheckSignsInSight(camera.transform.position);
			}
		}

		/// <summary>Signs behind a hill or a building are hidden: a ray from the sign back to the camera (the car right in front of it doesn't count).</summary>
		private void CheckSignsInSight(Vector3 camera) {
			signsHidden_.Clear();
			foreach (var (key, gate, _) in signs_) {
				Vector3 sign = gate.Center + Vector3.up * SignHeight;
				Vector3 toCamera = camera - sign;
				float distance = toCamera.magnitude;
				if (distance > SignRange) {
					signsHidden_.Add(key);
					continue;
				}
				try {
					if (distance > 12f && Physics.Raycast(sign, toCamera / distance, distance - 10f, ~0, QueryTriggerInteraction.Ignore)) {
						signsHidden_.Add(key);
					}
				} catch (Exception) {
					// No physics scene yet: shown.
				}
			}
		}

		/// <summary>The line for the trap being placed (where the car is, as wide as picked) or edited.</summary>
		private void UpdateTrapPreview() {
			if (placingTrap_ && windowOpen_ && CourseGate.TryMake(pose_.Position, pose_.Forward, newTrapWidth_, out CourseGate here)) {
				// Built again only when the car has moved or the width changed, not every frame.
				if (trapPreview_ == null || Vector3.Distance(trapPreview_.Center, here.Center) > 0.5f || Math.Abs(trapPreview_.Width - newTrapWidth_) > 0.01f ||
					Vector3.Dot(trapPreview_.Forward, here.Forward) < 0.995f) {
					trapPreview_ = here;
				}
				return;
			}
			if (trapEditing_ != null && windowOpen_) {
				if (trapPreview_ == null || Math.Abs(trapPreview_.Width - trapEditWidth_) > 0.01f || Vector3.Distance(trapPreview_.Center, trapEditing_.Gate.Center) > 0.01f) {
					trapPreview_ = new CourseGate { Center = trapEditing_.Gate.Center, Forward = trapEditing_.Gate.Forward, Width = trapEditWidth_ };
				}
				return;
			}
			trapPreview_ = null;
		}

		/// <summary>A course's trap as a speed trap of its own, for the records (named by the course and its number).</summary>
		private static SpeedTrap CourseTrap(Course course, int index) =>
			new SpeedTrap { Name = $"{course.Name} trap {index + 1}", Gate = course.Traps[index], MapKey = course.MapKey, Source = course.Source };

		/// <summary>The car went through a trap: the speed flashes with how it compares, and it's saved.</summary>
		private void TrapCaught(SpeedTrap trap, float kmh, string context) {
			var mine = trapRecords_.Where(r => r.TrapKey == trap.Key && r.CarId == pose_.CarId).Select(r => r.Kmh).DefaultIfEmpty(0f).Max();
			var all = trapRecords_.Where(r => r.TrapKey == trap.Key).Select(r => r.Kmh).DefaultIfEmpty(0f).Max();
			string colour = all > 0 && kmh > all ? Theme.PurpleHex : mine <= 0 || kmh >= mine ? Theme.GoodHex : Theme.WarnHex;
			string compare = all > 0 && kmh > all ? "best ever!" : mine > 0 ? $"your best {mine:0} km/h" : "first time through";
			trapFlash_ = $"<b>SPEED TRAP · {trap.Name}</b>\n<color={colour}><b>{kmh:0} km/h</b></color>  <color={Theme.MutedHex}>{compare}</color>";
			trapFlashUntil_ = Time.unscaledTime + TrapFlashSeconds;
			lastTrapKmh_[trap.Key] = kmh;
			var record = new SpeedTrapRecord {
				When = DateTime.Now, TrapKey = trap.Key, TrapName = trap.Name, MapName = map_?.Name ?? "", Kmh = kmh, CarId = pose_.CarId,
				Car = car_?.CarName ?? "", TuneSet = carStatus_?.Matched == true ? carStatus_.Official.Name : "",
				Spec = carStatus_ != null && StateOf(carStatus_) == SpecState.Spec && modCheck_.Flags.Count == 0, Context = context,
			};
			trapRecords_.Add(record);
			try {
				SpeedTrapLog.Append(TimesFolder, record);
			} catch (Exception e) {
				TrcLog.Error(e);
			}
			TrcLog.Info($"Speed trap {trap.Name}: {kmh:0.0} km/h ({context}).");
		}

		/// <summary>The trap flash, a little below the timer's spot (or on its own in free roam).</summary>
		private void DrawTrapFlash(float screenWidth, float screenHeight) {
			if (Time.unscaledTime >= trapFlashUntil_ || trapFlash_.Length == 0) {
				return;
			}
			var content = new GUIContent(trapFlash_);
			float width = Mathf.Min(320f, screenWidth - 2 * HudMargin);
			float height = Theme.HudPill.CalcHeight(content, width);
			float y = hudRect_.width > 0 ? Mathf.Min(screenHeight - height, hudRect_.yMax + HudGap + 70f) : screenHeight * 0.2f;
			if (Event.current.type == EventType.Repaint) {
				GUI.Label(new Rect((screenWidth - width) / 2f, y, width, height), content, Theme.HudPill);
			}
		}

		private bool TrapFlashVisible => hasPose_ && Time.unscaledTime < trapFlashUntil_;

		// ---- The signs ----

		private bool TrapSignsVisible => hasPose_ && signs_.Count > 0;

		/// <summary>The sign picture: speed_trap_sign.png in the mod's folder if there is one, else the built-in one.</summary>
		private Texture2D SignTexture() {
			if (signLoaded_) {
				return signTexture_;
			}
			signLoaded_ = true;
			try {
				string file = Path.Combine(modFolder_, SignFileName);
				if (File.Exists(file)) {
					var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
					if (ImageConversion.LoadImage(texture, File.ReadAllBytes(file))) {
						signTexture_ = texture;
						TrcLog.Info($"Speed trap sign: using {file} ({texture.width}x{texture.height}).");
						return signTexture_;
					}
					TrcLog.Warning($"Speed trap sign: couldn't read {file}, so the built-in sign is used.");
				}
				signTexture_ = KSL.API.Kino.Utils.LoadEmbeddedTexture(Assembly.GetExecutingAssembly(), "TRC.TuneLoader.Resources.speed_trap_sign.png");
			} catch (Exception e) {
				TrcLog.Warning($"Couldn't load the speed trap sign: {e.Message}");
			}
			return signTexture_;
		}

		/// <summary>
		/// Called from OnGUI: each trap's sign floating over the road, sized by how far away it is, with the last
		/// speed caught there (this car) in the middle. Drawn over the screen, so it needs no 3D model.
		/// </summary>
		private void DrawTrapSigns(float scale) {
			if (Event.current.type != EventType.Repaint || signs_.Count == 0) {
				return;
			}
			Camera camera = HudCamera();
			Texture2D texture = SignTexture();
			if (camera == null || texture == null) {
				return;
			}
			if (signNumber_ == null) {
				signNumber_ = new GUIStyle { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.86f, 0.98f, 1f) }, wordWrap = false };
				signName_ = new GUIStyle { alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.55f, 0.88f, 1f) }, wordWrap = false, richText = false };
			}
			float focal = Screen.height / (2f * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad));
			Color before = GUI.color;
			// Far ones first, so near ones draw on top.
			foreach (var (key, gate, name) in signs_.OrderByDescending(s => (s.Gate.Center - camera.transform.position).sqrMagnitude)) {
				if (signsHidden_.Contains(key)) {
					continue;
				}
				Vector3 world = gate.Center + Vector3.up * SignHeight;
				Vector3 screen = camera.WorldToScreenPoint(world);
				if (screen.z < 2f || screen.z > SignRange) {
					continue;
				}
				float width = Mathf.Clamp(SignWidth * focal / screen.z, 36f, 560f) / scale;
				float height = width * texture.height / texture.width;
				float x = screen.x / scale - width / 2f;
				float y = (Screen.height - screen.y) / scale - height / 2f;
				if (x > Screen.width / scale || x + width < 0 || y > Screen.height / scale || y + height < 0) {
					continue;
				}
				float fade = Mathf.Clamp01((SignRange - screen.z) / 80f);
				GUI.color = new Color(1f, 1f, 1f, fade);
				GUI.DrawTexture(new Rect(x, y, width, height), texture, ScaleMode.ScaleToFit);
				string speed = lastTrapKmh_.TryGetValue(key, out float kmh) ? kmh.ToString("0", CultureInfo.InvariantCulture) : LastSavedKmh(key);
				signNumber_.fontSize = Mathf.Max(8, Mathf.RoundToInt(height * 0.34f));
				GUI.Label(new Rect(x, y + height * 0.27f, width, height * 0.45f), speed, signNumber_);
				if (width > 140f) {
					signName_.fontSize = Mathf.Max(9, Mathf.RoundToInt(height * 0.08f));
					GUI.Label(new Rect(x - width * 0.25f, y + height, width * 1.5f, height * 0.2f), name, signName_);
				}
			}
			GUI.color = before;
		}

		/// <summary>The newest speed saved at a trap in this car, or "---".</summary>
		private string LastSavedKmh(string key) {
			SpeedTrapRecord last = trapRecords_.LastOrDefault(r => r.TrapKey == key && r.CarId == pose_.CarId);
			return last != null ? last.Kmh.ToString("0", CultureInfo.InvariantCulture) : "---";
		}

		// ---- The window: Time attack > Speed traps ----

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
			GUI.enabled = enabled && hasPose_ && map_ != null && !placingTrap_;
			if (GUILayout.Button("New speed trap", Theme.ButtonPrimary)) {
				StartPlacingTrap();
			}
			GUI.enabled = enabled;
			GUILayout.EndHorizontal();
			GUILayout.Label("They catch your speed in free roam, and during time attacks, battles and races too. Name them like \"Tsubaki Speed Trap 1\" and they're grouped by track.", Theme.Small);

			if (placingTrap_) {
				DrawPlaceTrap();
			}

			GUILayout.BeginVertical(Theme.Card, GUILayout.ExpandHeight(true));
			trapScroll_ = GUILayout.BeginScrollView(trapScroll_);
			if (map_ == null) {
				GUILayout.Label("Drive onto a map to see its speed traps.", Theme.Small);
			} else if (mapTraps_.Count == 0) {
				GUILayout.Label("No speed traps on this map yet. Press New speed trap, stop on the road where you want one, and place it.", Theme.Small);
			}
			var groups = mapTraps_.GroupBy(t => t.Group, StringComparer.OrdinalIgnoreCase)
				.OrderBy(g => g.Key.Length == 0 ? 1 : 0).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase);
			foreach (var group in groups) {
				List<SpeedTrap> traps = group.OrderBy(t => SpeedTrap.NumberOf(t.Name)).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
				if (group.Key.Length == 0 && groups.Count() == 1) {
					// Nothing named into groups yet: just the list.
					traps.ForEach(DrawTrapRow);
					continue;
				}
				string title = group.Key.Length == 0 ? "Other speed traps" : group.Key;
				bool open = trapGroupsOpen_.Contains(title) || (trapEditing_ != null && traps.Contains(trapEditing_));
				float best = traps.SelectMany(t => trapRecords_.Where(r => r.TrapKey == t.Key)).Select(r => r.Kmh).DefaultIfEmpty(0f).Max();
				if (GUILayout.Button($"{(open ? "▾" : "▸")}  <b>{title}</b>  <color={Theme.MutedHex}>{traps.Count} trap(s){(best > 0 ? $" · fastest {best:0} km/h" : "")}</color>", Theme.Row)) {
					if (!trapGroupsOpen_.Remove(title)) {
						trapGroupsOpen_.Add(title);
					}
				}
				if (open) {
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

		private void DrawTrapRow(SpeedTrap trap) {
			float mine = trapRecords_.Where(r => r.TrapKey == trap.Key && r.CarId == pose_.CarId).Select(r => r.Kmh).DefaultIfEmpty(0f).Max();
			float best = trapRecords_.Where(r => r.TrapKey == trap.Key).Select(r => r.Kmh).DefaultIfEmpty(0f).Max();
			float away = hasPose_ ? Vector3.Distance(pose_.Position, trap.Gate.Center) : 0f;
			GUILayout.BeginHorizontal();
			GUILayout.Label($"<b>{trap.Name}</b>  <color={Theme.MutedHex}>{(trap.Source == CourseSource.Map ? "map" : "yours")} · {trap.Gate.Width:0} m wide · {TimeAttackRun.Distance(away)} away" +
				$"{(best > 0 ? $" · best {best:0} km/h" : "")}{(mine > 0 && mine < best ? $" · this car {mine:0}" : "")}</color>", Theme.Body);
			GUILayout.FlexibleSpace();
			if (trap.Source == CourseSource.Custom && trapEditing_ != trap && GUILayout.Button("Edit", Theme.Button)) {
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
			if (GUILayout.Button("Place it here", Theme.ButtonPrimary)) {
				AddFreeRoamTrap();
			}
			GUI.enabled = enabled;
			if (GUILayout.Button("Done", Theme.Button)) {
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
			if (GUILayout.Button("Save", Theme.ButtonPrimary)) {
				EditFreeRoamTrap(trap, TuneRules.Trim(trapEditName_), trapEditWidth_, move: false);
			}
			bool enabled = GUI.enabled;
			GUI.enabled = enabled && hasPose_;
			if (GUILayout.Button("Move it here", Theme.Button)) {
				EditFreeRoamTrap(trap, TuneRules.Trim(trapEditName_), trapEditWidth_, move: true);
			}
			GUI.enabled = enabled;
			if (trapDeletePending_ == trap) {
				if (GUILayout.Button("Delete it", Theme.ButtonPrimary)) {
					DeleteFreeRoamTrap(trap);
				}
				if (GUILayout.Button("Keep", Theme.Button)) {
					trapDeletePending_ = null;
				}
			} else if (GUILayout.Button("Delete", Theme.Button)) {
				trapDeletePending_ = trap;
			}
			if (GUILayout.Button("Cancel", Theme.Button)) {
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

		private void StartPlacingTrap() {
			placingTrap_ = true;
			trapEditing_ = null;
			SpeedTrap last = mapTraps_.LastOrDefault(t => t.Source == CourseSource.Custom);
			newTrapName_ = SpeedTrap.NextName(last?.Name ?? "", mapTraps_.Count(t => t.Source == CourseSource.Custom));
		}

		/// <summary>A free-roam trap where the car is, across the road the way it faces (either way through it counts).</summary>
		private void AddFreeRoamTrap() {
			if (!CourseGate.TryMake(pose_.Position, pose_.Forward, newTrapWidth_, out CourseGate gate)) {
				courseResult_ = "Couldn't tell which way the car faces.";
				return;
			}
			try {
				string name = TuneRules.Trim(newTrapName_);
				List<SpeedTrap> mine = SpeedTrapFile.Load(TrapsFile, map_.Key);
				mine.Add(new SpeedTrap { Name = name, Gate = gate, MapKey = map_.Key, Source = CourseSource.Custom });
				SpeedTrapFile.Save(TrapsFile, mine);
				RefreshTraps();
				SavePref(TrapWidthPref, newTrapWidth_);
				string group = SpeedTrap.GroupOf(name);
				if (group.Length > 0) {
					trapGroupsOpen_.Add(group);
				}
				courseResult_ = $"{name} placed here ({newTrapWidth_:0} m wide). Drive on to place the next one, or press Done.";
				newTrapName_ = SpeedTrap.NextName(name, mine.Count);
				TrcLog.Info($"Speed trap {name} added on {map_.Name} at {pose_.Position}, {newTrapWidth_:0} m wide.");
			} catch (Exception e) {
				courseResult_ = $"Couldn't save the speed trap: {e.Message}";
				TrcLog.Error(e);
			}
		}

		/// <summary>Renames a trap, changes its width, or moves it to where the car is (moving it starts its bests again: they belong to the old spot).</summary>
		private void EditFreeRoamTrap(SpeedTrap trap, string name, float width, bool move) {
			if (name.Length == 0) {
				courseResult_ = "Give the speed trap a name.";
				return;
			}
			CourseGate moved = null;
			if (move && !CourseGate.TryMake(pose_.Position, pose_.Forward, width, out moved)) {
				courseResult_ = "Couldn't tell which way the car faces.";
				return;
			}
			try {
				List<SpeedTrap> mine = SpeedTrapFile.Load(TrapsFile, map_.Key);
				SpeedTrap saved = mine.FirstOrDefault(t => t.Key == trap.Key);
				if (saved == null) {
					courseResult_ = "Couldn't find that speed trap in your file any more.";
					return;
				}
				saved.Name = name;
				saved.Gate = moved ?? new CourseGate { Center = saved.Gate.Center, Forward = saved.Gate.Forward, Width = width };
				SpeedTrapFile.Save(TrapsFile, mine);
				RefreshTraps();
				trapEditing_ = null;
				trapDeletePending_ = null;
				courseResult_ = move ? $"Moved {name} here." : $"Saved {name}.";
			} catch (Exception e) {
				courseResult_ = $"Couldn't save the speed trap: {e.Message}";
				TrcLog.Error(e);
			}
		}

		private void DeleteFreeRoamTrap(SpeedTrap trap) {
			trapDeletePending_ = null;
			trapEditing_ = null;
			try {
				List<SpeedTrap> mine = SpeedTrapFile.Load(TrapsFile, map_.Key);
				mine.RemoveAll(t => t.Key == trap.Key);
				SpeedTrapFile.Save(TrapsFile, mine);
				RefreshTraps();
				courseResult_ = $"Deleted {trap.Name}. Its speeds stay saved.";
			} catch (Exception e) {
				courseResult_ = $"Couldn't delete it: {e.Message}";
			}
		}

		/// <summary>Edit: a speed trap on this course where the car is (in driving order along the course).</summary>
		private void AddCourseTrap(Course course) {
			if (!CourseGate.TryMake(pose_.Position, pose_.Forward, course.Start.Width, out CourseGate gate)) {
				courseResult_ = "Couldn't tell which way the car faces.";
				return;
			}
			PathPosition at = CourseGeometry.Project(course.Path, course.Distances, pose_.Position, 0, course.Path.Count);
			if (at.Offset > 30f) {
				courseResult_ = "Drive onto the course first: a speed trap goes across its road.";
				return;
			}
			if (Vector3.Dot(gate.Forward, CourseGeometry.Level(course.Path[Math.Min(at.Segment + 1, course.Path.Count - 1)] - course.Path[at.Segment])) < 0f) {
				gate.Forward = -gate.Forward;
			}
			ReplaceCourse(course, Copy(course, c => {
				c.Traps.Add(gate);
				c.Traps = c.Traps.OrderBy(t => CourseGeometry.Project(c.Path, c.Distances, t.Center, 0, c.Path.Count).Progress).ToList();
			}), "Speed trap added.");
		}

		private void RemoveCourseTraps(Course course) {
			ReplaceCourse(course, Copy(course, c => c.Traps.Clear()), "Speed traps removed.");
		}

		/// <summary>
		/// For a speed trap race on a course without traps of its own: the map's free-roam traps that sit across the
		/// course's road (near its path, facing along it), in driving order.
		/// </summary>
		private List<CourseGate> TrapsAlong(Course course) {
			var found = new List<(float Progress, CourseGate Gate)>();
			if (course == null || course.Path.Count < 2) {
				return new List<CourseGate>();
			}
			foreach (SpeedTrap trap in mapTraps_) {
				PathPosition at = CourseGeometry.Project(course.Path, course.Distances, trap.Gate.Center, 0, course.Path.Count);
				if (at.Offset > 12f) {
					continue;
				}
				Vector3 along = CourseGeometry.Level(course.Path[Math.Min(at.Segment + 1, course.Path.Count - 1)] - course.Path[at.Segment]);
				float dot = Vector3.Dot(trap.Gate.Forward, along);
				if (Mathf.Abs(dot) < 0.7f) {
					continue;
				}
				var gate = new CourseGate { Center = trap.Gate.Center, Forward = dot < 0 ? -trap.Gate.Forward : trap.Gate.Forward, Width = trap.Gate.Width };
				found.Add((at.Progress, gate));
			}
			return found.OrderBy(f => f.Progress).Select(f => f.Gate).ToList();
		}

		/// <summary>The traps a speed trap race on this course scores: its own, or the free-roam ones along it.</summary>
		private List<CourseGate> RaceTrapsFor(Course course) => course == null ? new List<CourseGate>() : course.Traps.Count > 0 ? course.Traps : TrapsAlong(course);
	}
}
