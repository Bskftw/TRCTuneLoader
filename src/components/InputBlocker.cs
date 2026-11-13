// Keeps clicks and the mouse wheel off CarX's own menus while the window is open.
using UnityEngine;
using UnityEngine.UI;

namespace TRC.TuneLoader {
	/// <summary>
	/// While the mod's window is open, an invisible screen-wide UGUI layer on top of CarX's own menus (bskftw, Nov 3:
	/// "you cant click on anything in game", and the mouse wheel zoomed the game's camera). Clicks land on it instead of
	/// CarX's buttons, and CarX's camera, which only zooms and turns with the mouse when the pointer isn't over UI, sees
	/// the pointer over UI and leaves the camera alone. The mod's own window is IMGUI, drawn over all of it, so it still
	/// gets every click and scroll.
	/// </summary>
	internal sealed class InputBlocker {
		private GameObject root_;

		/// <summary>Turns the layer on or off (made the first time it's needed).</summary>
		public void Set(bool on) {
			if (on && root_ == null) {
				Make();
			}
			if (root_ != null && root_.activeSelf != on) {
				root_.SetActive(on);
			}
		}

		private void Make() {
			root_ = new GameObject("TRC menu input blocker") { hideFlags = HideFlags.DontSave };
			Object.DontDestroyOnLoad(root_);
			var canvas = root_.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.sortingOrder = short.MaxValue;
			root_.AddComponent<GraphicRaycaster>();

			var cover = new GameObject("cover", typeof(RectTransform));
			cover.transform.SetParent(root_.transform, false);
			var rect = (RectTransform)cover.transform;
			rect.anchorMin = Vector2.zero;
			rect.anchorMax = Vector2.one;
			rect.offsetMin = Vector2.zero;
			rect.offsetMax = Vector2.zero;
			// Fully clear, but it still catches the pointer (a raycast target doesn't need to be seen).
			var image = cover.AddComponent<Image>();
			image.color = new Color(0f, 0f, 0f, 0f);
			image.raycastTarget = true;
		}

		public void Destroy() {
			if (root_ != null) {
				Object.Destroy(root_);
				root_ = null;
			}
		}
	}
}
