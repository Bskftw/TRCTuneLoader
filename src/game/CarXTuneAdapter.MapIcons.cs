// The mod's icons on CarX's minimap and big map: showing, sizing and hiding them.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using CarProfile;
using DB;
using DB.Meta;
using DI;
using GameOverlay;
using LocalCaching.Manager;
using ParameterType = Db.Meta.CarProfile.CarProfileParameterType;
using PrefsModels;
using SyncMultiplayer;
using UnityEngine;

namespace TRC.TuneLoader {
	public sealed partial class CarXTuneAdapter : ICarTuneAdapter {
		private readonly List<TrcMinimapItem> mapIcons_ = new List<TrcMinimapItem>();
		private int mapIconType_ = -1;
		private bool mapIconsFailed_;
		private Minimap iconsMinimap_;
		private static readonly FieldInfo IconColour = typeof(MinimapItem).GetField("m_color", BindingFlags.Instance | BindingFlags.NonPublic);
		private static readonly FieldInfo IconSize = typeof(MinimapItem).GetField("m_size", BindingFlags.Instance | BindingFlags.NonPublic);

		public bool TryShowMapIcons(IList<MapIcon> icons) {
			try {
				mapIcons_.RemoveAll(i => i == null); // a new map took them with the old scene
				if (Minimap.instance == null || IconColour == null || IconSize == null) {
					HideMapIcons(0);
					return false;
				}
				if (mapIconType_ < 0) {
					mapIconType_ = PickMapIconType();
				}
				if (!ReferenceEquals(Minimap.instance, iconsMinimap_)) {
					// A new map cleared CarX's list of icons: take ours off so they're put back on it.
					iconsMinimap_ = Minimap.instance;
					HideMapIcons(0);
				}
				for (int i = 0; i < icons.Count; i++) {
					if (i == mapIcons_.Count) {
						var go = new GameObject("TRC map icon") { hideFlags = HideFlags.DontSave };
						go.SetActive(false);
						TrcMinimapItem added = go.AddComponent<TrcMinimapItem>();
						added.typeIndex = mapIconType_;
						mapIcons_.Add(added);
					}
					TrcMinimapItem item = mapIcons_[i];
					item.transform.position = icons[i].Position;
					item.Shape = icons[i].Shape;
					Vector3 heading = new Vector3(icons[i].Heading.x, 0f, icons[i].Heading.z);
					// The map turns an icon with its object, so a line lies across the road.
					item.transform.rotation = heading.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(heading) : Quaternion.identity;
					IconColour.SetValue(item, (Color?)icons[i].Color);
					int? size = icons[i].Size;
					bool resized = !Equals(IconSize.GetValue(item), size);
					IconSize.SetValue(item, size);
					if (resized && item.gameObject.activeSelf) {
						// The map sizes an icon when it's added: take it off and put it back.
						item.gameObject.SetActive(false);
					}
					if (!item.gameObject.activeSelf) {
						item.gameObject.SetActive(true);
					}
				}
				HideMapIcons(icons.Count);
				return true;
			} catch (Exception e) {
				if (!mapIconsFailed_) {
					mapIconsFailed_ = true;
					TrcLog.Warning($"Map icons: {e.Message}");
				}
				return false;
			}
		}

		/// <summary>Our icons on the driving minimap are this much bigger than their size.</summary>
		private const float MinimapIconScale = 1.3f;

		/// <summary>
		/// On the full map (menu) they're smaller the bigger the map is (bskftw, Nov 3: Project Dream's is 30 km, and a lot
		/// of icons at the minimap's size would cover it): their size times this many metres over the map's width, at
		/// most <see cref="FullMapMostScale"/> of it, and never under a few pixels so they don't vanish. Zooming in on the
		/// full map spreads them apart (it stretches the map, not the icons).
		/// </summary>
		private const float FullMapMetres = 4000f;
		private const float FullMapMostScale = 0.55f;

		private static float FullMapIconSize(int size) {
			Vector2 bounds = Minimap.instance != null ? Minimap.instance.boundsSize : Vector2.zero;
			float width = Mathf.Max(bounds.x, bounds.y);
			float scale = width > 1f ? Mathf.Min(FullMapMostScale, FullMapMetres / width) : FullMapMostScale;
			return Mathf.Max(size >= 20 ? 8f : 5f, size * scale);
		}
		private static readonly FieldInfo VisualizerIcons = typeof(Minimaps.BaseMinimapVisualizer).GetField("m_iconsList", BindingFlags.Instance | BindingFlags.NonPublic);
		private Minimaps.BaseMinimapVisualizer[] visualizers_ = new Minimaps.BaseMinimapVisualizer[0];
		private float visualizersAt_;

		public void SizeMapIcons() {
			try {
				if (VisualizerIcons == null || !mapIcons_.Exists(i => i != null && i.gameObject.activeSelf)) {
					return;
				}
				if (Time.unscaledTime >= visualizersAt_) {
					// The maps on screen (the minimap, the menu's full map), looked for again every couple of seconds.
					visualizersAt_ = Time.unscaledTime + 2f;
					visualizers_ = UnityEngine.Object.FindObjectsByType<Minimaps.BaseMinimapVisualizer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
				}
				foreach (Minimaps.BaseMinimapVisualizer map in visualizers_) {
					if (map == null || !(VisualizerIcons.GetValue(map) is List<HUD.UIMinimapItem> shown)) {
						continue;
					}
					bool full = map is Minimaps.GlobalMinimapVisualizer || map is Minimaps.UIMinimapPauseMenuVisualizer;
					foreach (HUD.UIMinimapItem icon in shown) {
						if (icon != null && icon.item is TrcMinimapItem ours) {
							float size = full ? FullMapIconSize(ours.size) : ours.size * MinimapIconScale;
							if (Mathf.Abs(icon.size.x - size) > 0.5f) {
								icon.size = new Vector2(size, size);
							}
							Sprite shape = MapIconSprites.For(ours.Shape);
							if (shape != null && icon.image != shape) {
								icon.image = shape;
							}
						}
					}
				}
			} catch (Exception e) {
				// Every frame: a failure only leaves the icons at CarX's size.
				TrcLog.Once("map icon size", $"Couldn't size the map icons ({e.Message}); said once.");
			}
		}

		private void HideMapIcons(int from) {
			for (int i = from; i < mapIcons_.Count; i++) {
				if (mapIcons_[i] != null && mapIcons_[i].gameObject.activeSelf) {
					mapIcons_[i].gameObject.SetActive(false);
				}
			}
		}

		/// <summary>CarX's round icon (the one network cars use), logged with the others the first time.</summary>
		private static int PickMapIconType() {
			Minimap.IconType[] types = ScriptableSingleton<MinimapIconSettings>.instance?.iconTypes ?? new Minimap.IconType[0];
			TrcLog.Info($"Map icon types: {string.Join(", ", types.Select((t, i) => $"{i} {t?.name} ({t?.size} px)"))}.");
			int round = Array.FindIndex(types, t => t != null && t.icon != null && (t.name ?? "").IndexOf("circle", StringComparison.OrdinalIgnoreCase) >= 0);
			if (round < 0) {
				round = Array.FindIndex(types, t => t != null && t.icon != null && (t.name ?? "").IndexOf("point", StringComparison.OrdinalIgnoreCase) >= 0);
			}
			return round >= 0 ? round : Math.Min(1, Math.Max(0, types.Length - 1));
		}
	}

	/// <summary>
	/// One of the mod's icons on CarX's minimap and big map. CarX's own icons wait for a car to belong to before they
	/// show (in online rooms); these belong to no car, so they go on the map straight away, like CarX's waypoint does.
	/// </summary>
	internal sealed class TrcMinimapItem : MinimapItem {
		public MapIconShape Shape;

		protected override Cysharp.Threading.Tasks.UniTask WaitCar() {
			Minimaps.MinimapProvider.AddIcon(this);
			return Cysharp.Threading.Tasks.UniTask.CompletedTask;
		}
	}
}
