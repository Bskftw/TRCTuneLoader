// The map's places over the track (F14): boards with a track's times, the parking area you're in, and territory in its team's colour.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// What a map maker puts in the map by name (MAP_MAKERS.md): a <b>TRC Board &lt;track&gt;</b> shows that track's best times
	/// above it; a <b>TRC Parking &lt;name&gt;</b> area is a meet point: parked there, the room board says where you are; a
	/// <b>TRC Territory &lt;region&gt;</b> mesh is drawn in the colour of the team that holds the region, from territory.txt
	/// next to the mod ("Region = Team = #RRGGBB" a line, copied from the season bot). All of it only on maps that have them.
	/// </summary>
	public partial class TRCTuneLoaderMod {
		private const string PlacesPref = "hud_places";
		private bool placesOn_ = true;
		private bool placesLoaded_;
		private string parkedAt_ = "";
		private float placesCheckAt_;
		private readonly Dictionary<string, (string Team, Color Colour)> territory_ = new Dictionary<string, (string, Color)>(StringComparer.OrdinalIgnoreCase);
		private string territoryRead_ = "";
		private readonly Dictionary<Color, Material> territoryMaterials_ = new Dictionary<Color, Material>();

		private bool MapPlacesVisible => placesOn_ && hasPose_ && !windowOpen_ && (CourseScanner.Boards.Count > 0 || parkedAt_.Length > 0);

		/// <summary>From Update: which parking area you're in (4 times a second), and the territory drawn (every frame).</summary>
		private void UpdateMapPlaces() {
			if (!placesLoaded_) {
				placesLoaded_ = true;
				placesOn_ = Kino.Prefs.Get(PlacesPref, true);
			}
			if (!placesOn_ || !hasPose_ || map_ == null) {
				parkedAt_ = "";
				return;
			}
			if (Time.unscaledTime >= placesCheckAt_) {
				placesCheckAt_ = Time.unscaledTime + 0.25f;
				string at = CourseScanner.Parking.FirstOrDefault(p => p.Bounds.Contains(pose_.Position))?.Name ?? "";
				if (at != parkedAt_) {
					parkedAt_ = at;
					if (at.Length > 0) {
						ShowNotice($"<b>{at}</b>  <color={Theme.MutedHex}>a meet point: the room sees you're here</color>", 2.5f);
					}
				}
				ReadTerritoryIfChanged();
			}
			DrawTerritory();
		}

		/// <summary>territory.txt: read again when it changes (looked at with the parking check).</summary>
		private void ReadTerritoryIfChanged() {
			if (CourseScanner.Territory.Count == 0) {
				return;
			}
			string path = Path.Combine(modFolder_, "territory.txt");
			string stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks.ToString(CultureInfo.InvariantCulture) : "";
			if (stamp == territoryRead_) {
				return;
			}
			territoryRead_ = stamp;
			territory_.Clear();
			if (stamp.Length == 0) {
				return;
			}
			try {
				foreach (string line in File.ReadAllLines(path)) {
					string[] parts = line.Split('=');
					if (parts.Length == 3 && ColorUtility.TryParseHtmlString(parts[2].Trim(), out Color colour)) {
						territory_[parts[0].Trim()] = (parts[1].Trim(), colour);
					}
				}
				TrcLog.Info($"Territory: {territory_.Count} region(s) from territory.txt, {CourseScanner.Territory.Count} territory zone(s) on this map.");
			} catch (Exception e) {
				TrcLog.Warning($"Territory: couldn't read territory.txt ({e.Message}).");
			}
		}

		/// <summary>Each territory zone's meshes drawn again, see-through, in its team's colour.</summary>
		private void DrawTerritory() {
			if (territory_.Count == 0 || CourseMarkers.GhostMaterial == null) {
				return;
			}
			foreach (CourseScanner.MapArea zone in CourseScanner.Territory) {
				if (zone.Transform == null || !territory_.TryGetValue(zone.Name, out var owner)) {
					continue;
				}
				if (!territoryMaterials_.TryGetValue(owner.Colour, out Material material)) {
					material = new Material(CourseMarkers.GhostMaterial) { hideFlags = HideFlags.DontSave, name = "TRC territory" };
					var tint = new Color(owner.Colour.r, owner.Colour.g, owner.Colour.b, 0.35f);
					foreach (string property in new[] { "_UnlitColor", "_BaseColor", "_Color" }) {
						if (material.HasProperty(property)) {
							material.SetColor(property, tint);
						}
					}
					territoryMaterials_[owner.Colour] = material;
				}
				foreach (MeshFilter filter in zone.Transform.GetComponentsInChildren<MeshFilter>(true)) {
					if (filter.sharedMesh != null) {
						Graphics.DrawMesh(filter.sharedMesh, filter.transform.localToWorldMatrix, material, 0);
					}
				}
			}
		}

		/// <summary>Each board near enough and in front of the camera: its track's best times, over it.</summary>
		private void DrawMapPlaces(float scale) {
			if (Event.current.type != EventType.Repaint) {
				return;
			}
			Camera camera = HudCamera();
			if (camera == null) {
				return;
			}
			foreach (CourseScanner.MapBoard board in CourseScanner.Boards) {
				Vector3 world = board.Position + Vector3.up * 3f;
				if ((world - camera.transform.position).sqrMagnitude > 150f * 150f) {
					continue;
				}
				Vector3 screen = camera.WorldToScreenPoint(world);
				if (screen.z <= 0f) {
					continue;
				}
				Course course = CoursesHere().FirstOrDefault(c => string.Equals(c.Name, board.Name, StringComparison.OrdinalIgnoreCase))
					?? CoursesHere().FirstOrDefault(c => c.Name.StartsWith(board.Name, StringComparison.OrdinalIgnoreCase));
				var lines = new List<string> { $"<b>{board.Name}</b>" };
				if (course != null) {
					var best = times_.Where(r => !r.IsLap && r.CourseCode == course.Code).OrderBy(r => r.TimeMs).Take(3).ToList();
					lines.AddRange(best.Select((r, i) => $"{i + 1}. {TimeAttackRun.FormatTime(r.TimeMs / 1000.0)}  <color={Theme.MutedHex}>{ShortCar(r.Car)}</color>"));
					if (best.Count == 0) {
						lines.Add($"<color={Theme.MutedHex}>no time yet</color>");
					}
				}
				float x = screen.x / scale, y = (Screen.height - screen.y) / scale;
				var rect = new Rect(x - 90f, y - 12f - lines.Count * 16f, 180f, 10f + lines.Count * 16f);
				GUI.Box(rect, GUIContent.none, Theme.HudPanel);
				GUI.Label(new Rect(rect.x + 8f, rect.y + 4f, rect.width - 16f, rect.height - 8f), string.Join("\n", lines), Theme.HudSmall);
			}
		}
	}
}
