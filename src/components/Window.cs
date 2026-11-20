// The TRC window: opening and closing it, its input, and drawing whichever page is open.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// The TRC Tune Loader window: its own IMGUI window with the TRC look (see Theme), opened from the
	/// KSL menu or with the shortcut. This file is the window itself; each page it shows is in src/pages/, and the
	/// Options tab, shortcut and tour are in TRCTuneLoaderMod.Options.cs.
	/// </summary>
	public partial class TRCTuneLoaderMod {
		private const int WindowId = 0x7C1A11;
		/// <summary>The window grows a little with a bigger text size, so lines still fit.</summary>
		/// <summary>Wider since 0.17: each category's pages are a list down the left of every page.</summary>
		private static float WindowWidth => Mathf.Round(940f * (1f + (Theme.TextScale - 1f) * 0.6f));
		private static float WindowHeight => Mathf.Round(680f * (1f + (Theme.TextScale - 1f) * 0.4f));
		private const float StatusRefreshSeconds = 2.0f;
		// UGUI's EventSystem lives in UnityEngine.UI; found by name so the mod doesn't need that DLL.
		private static readonly PropertyInfo CurrentEventSystem =
			Type.GetType("UnityEngine.EventSystems.EventSystem, UnityEngine.UI")?.GetProperty("current", BindingFlags.Public | BindingFlags.Static);

		private const int HomeTab = 0;
		private const int MyCarTab = 1;
		private const int TuneSetsTab = 2;
		private const int PlayersTab = 3;
		private const int ModCheckTab = 4;

		private bool windowOpen_;
		private Rect windowRect_;
		private int tab_;
		private bool mouseOverWindow_;
		private Behaviour eventSystemWeDisabled_;
		private bool savedCursorVisible_;
		private CursorLockMode savedCursorLock_;
		private Texture2D logo_;

		private OwnCheck carStatus_;
		private float nextStatusRefresh_;

		private static readonly GUIStyle ContentArea = new GUIStyle { padding = new RectOffset(18, 14, 14, 14) };
		private static readonly GUIStyle TabArea = new GUIStyle { padding = new RectOffset(8, 8, 0, 0) };

		private void SetupWindow() {
			try {
				logo_ = Kino.Utils.LoadEmbeddedTexture(Assembly.GetExecutingAssembly(), "TRC.TuneLoader.Resources.trc_logo.png");
			} catch (Exception e) {
				TrcLog.Warning($"Couldn't load the TRC logo: {e.Message}");
			}
			LoadOptions();
		}

		private void ToggleWindow() {
			if (windowOpen_) {
				CloseWindow();
			} else {
				OpenWindow();
			}
		}

		private void OpenWindow() {
			if (windowOpen_) {
				return;
			}

			windowOpen_ = true;
			savedCursorVisible_ = Cursor.visible;
			savedCursorLock_ = Cursor.lockState;
			nextStatusRefresh_ = 0;
			RefreshCar();
			// A challenge is waiting: open right where it's answered.
			if (invites_.Count > 0 && tourSeen_) {
				tab_ = BattlesTab;
				battleViewMode_ = 0;
			}
			OnTabOpened(tab_);
			if (!tourSeen_ && tourStep_ < 0) {
				StartTour();
			}
		}

		private void CloseWindow() {
			if (!windowOpen_) {
				return;
			}

			windowOpen_ = false;
			mouseOverWindow_ = false;
			capturingShortcut_ = false;
			capturingRestart_ = false;
			capturingStart_ = false;
			ReleasePad();
			if (tourStep_ >= 0) {
				EndTour();
			}
			RestoreEventSystem();
			Cursor.visible = savedCursorVisible_;
			Cursor.lockState = savedCursorLock_;
			// Nothing of the window keeps the keyboard or a held click (a text field could keep taking keys).
			GUIUtility.keyboardControl = 0;
			GUIUtility.hotControl = 0;
		}

		private readonly InputBlocker inputBlocker_ = new InputBlocker();
		private bool inputBlockerFailed_;

		/// <summary>
		/// Called from Update: keeps the cursor usable and, while the window is open, keeps every click and the mouse
		/// wheel off the game (an invisible layer over CarX's menus: see <see cref="InputBlocker"/>). Turning CarX's
		/// EventSystem off instead, as before, let its camera zoom and turn with the mouse under the window.
		/// </summary>
		private void UpdateWindowInput() {
			if (!inputBlockerFailed_) {
				try {
					inputBlocker_.Set(windowOpen_);
				} catch (Exception e) {
					inputBlockerFailed_ = true;
					TrcLog.Warning($"Couldn't keep clicks off the game while the window is open: {e.Message}");
				}
			}
			if (!windowOpen_) {
				return;
			}

			Cursor.visible = true;
			Cursor.lockState = CursorLockMode.None;

			try {
				// Only when the layer couldn't be made: the old way, CarX's menus off while the mouse is over the window.
				if (inputBlockerFailed_ && mouseOverWindow_) {
					if (eventSystemWeDisabled_ == null && CurrentEventSystem?.GetValue(null, null) is Behaviour eventSystem && eventSystem.enabled) {
						eventSystem.enabled = false;
						eventSystemWeDisabled_ = eventSystem;
					}
				} else {
					RestoreEventSystem();
				}
			} catch (Exception e) {
				TrcLog.Warning($"Window input: {e.Message}");
			}
		}

		private void RestoreEventSystem() {
			if (eventSystemWeDisabled_ != null) {
				eventSystemWeDisabled_.enabled = true;
				eventSystemWeDisabled_ = null;
			}
		}

		private void OnGUI() {
			PerfLog.BeginGui();
			try {
				DrawGui();
			} finally {
				PerfLog.EndGui();
			}
		}

		private void DrawGui() {
			try {
				HandleShortcutEvent(Event.current);
			} catch (Exception e) {
				TrcLog.Error(e);
			}
			if (arranging_ && !windowOpen_) {
				arranging_ = false;
			}
			// While arranging the screen, only the sample boxes show (the real ones would sit on top of them).
			// Hide HUD (Tools > HUD, the quick menu) takes away every box over the track; the window, the speed trap signs on
			// the road and the quick menu stay.
			bool shown = !hudHidden_;
			bool hud = shown && !windowOpen_ && OnTrack && (showBadge_ || autoCheck_);
			bool courseHud = shown && (CourseHudVisible || NoticeVisible) && !arranging_;
			bool signs = TrapSignsVisible;
			bool suspension = shown && SuspensionOverlayVisible && !arranging_;
			bool telemetry = shown && TelemetryOverlayVisible && !arranging_;
			bool trafficScore = shown && TrafficScoreVisible && !arranging_;
			bool tandem = shown && TandemVisible && !arranging_;
			bool places = shown && MapPlacesVisible && !arranging_;
			if (!windowOpen_ && !hud && !courseHud && !signs && !suspension && !telemetry && !trafficScore && !tandem && !places && !quickOpen_) {
				return;
			}

			Theme.Ensure();
			GUISkin previousSkin = GUI.skin;
			Matrix4x4 previousMatrix = GUI.matrix;
			try {
				GUI.skin = Theme.Skin;
				GUI.depth = -100;

				float scale = FittedScale();
				GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
				float screenWidth = Screen.width / scale;
				float screenHeight = Screen.height / scale;

				if (signs) {
					DrawTrapSigns(scale);
				}
				// The boxes over the track at the HUD's own size (Tools > HUD), on top of the window size.
				float hudScale = scale * hudSize_;
				GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(hudScale, hudScale, 1f));
				float hudWidth = Screen.width / hudScale;
				float hudHeight = Screen.height / hudScale;
				if (courseHud) {
					DrawCourseHud(hudWidth, hudHeight, hudScale);
				}
				if (hud) {
					DrawHud(hudWidth, hudHeight);
				}
				if (suspension) {
					DrawSuspensionOverlay(hudWidth, hudHeight);
				}
				if (telemetry) {
					DrawTelemetryOverlay(hudWidth, hudHeight);
				}
				if (trafficScore) {
					DrawTrafficScore(hudWidth, hudHeight);
				}
				if (tandem) {
					DrawTandem(hudWidth, hudHeight);
				}
				if (places) {
					DrawMapPlaces(hudScale);
				}
				if (shown || windowOpen_) {
					DrawNotice(hudWidth, hudHeight);
				}
				DrawQuickMenu(hudWidth, hudHeight);
				if (!windowOpen_) {
					return;
				}
				if (arranging_) {
					DrawArranging(hudWidth, hudHeight);
					return;
				}
				GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

				if (windowRect_.width <= 0) {
					windowRect_ = new Rect((screenWidth - WindowWidth) / 2f, (screenHeight - WindowHeight) / 2f, WindowWidth, WindowHeight);
				}
				windowRect_.width = WindowWidth;
				windowRect_.height = Mathf.Min(WindowHeight, screenHeight - 20f);
				LogScreenFit(scale, screenWidth, screenHeight);
				windowRect_.x = Mathf.Clamp(windowRect_.x, 0f, Mathf.Max(0f, screenWidth - windowRect_.width));
				windowRect_.y = Mathf.Clamp(windowRect_.y, 0f, Mathf.Max(0f, screenHeight - windowRect_.height));

				windowRect_ = GUI.Window(WindowId, windowRect_, DrawWindow, GUIContent.none, Theme.Window);
				mouseOverWindow_ = windowRect_.Contains(Event.current.mousePosition);
			} catch (Exception e) {
				TrcLog.Error(e);
			} finally {
				GUI.skin = previousSkin;
				GUI.matrix = previousMatrix;
			}
		}

		private string screenFitLogged_ = "", screenFitSeen_ = "";
		private float screenFitSince_;

		/// <summary>
		/// KSL's UI scale times the window size, made smaller when the window wouldn't fit in 90% of the screen's height
		/// and 92% of its width (bskftw, Nov 19: at 1280x720 with KSL's 1.40 the window filled the screen). Everything
		/// drawn (window and HUD) shrinks with it; a big screen keeps the full scale. Never below 0.6, so text stays readable.
		/// </summary>
		private float FittedScale() {
			float wanted = (Kino.UI.Scale > 0.1f ? Kino.UI.Scale : 1f) * windowScale_;
			float fit = Mathf.Min(1f, Mathf.Min(Screen.height * 0.9f / (WindowHeight * wanted), Screen.width * 0.92f / (WindowWidth * wanted)));
			return Mathf.Max(0.6f, wanted * fit);
		}

		/// <summary>
		/// Once per screen size and scale (UI redesign Stage 6, the 720p / ultrawide / 4K check): the screen, the scale and
		/// whether the window fits, in the log. A window cut short or too wide for the screen says so.
		/// </summary>
		private void LogScreenFit(float scale, float screenWidth, float screenHeight) {
			string key = $"{Screen.width}x{Screen.height}@{scale:0.00}";
			// Once the size has settled for a second: dragging a window's edge changed it every frame (80 lines in a second, Nov 19).
			if (key != screenFitSeen_) {
				screenFitSeen_ = key;
				screenFitSince_ = Time.unscaledTime;
				return;
			}
			if (key == screenFitLogged_ || Time.unscaledTime - screenFitSince_ < 1f) {
				return;
			}
			screenFitLogged_ = key;
			float shortBy = WindowHeight - windowRect_.height;
			float wideBy = WindowWidth - screenWidth;
			string fit = shortBy > 0.5f ? $"cut {shortBy:0} px short (the page scrolls)" : wideBy > 0.5f ? $"{wideBy:0} px wider than the screen" : "fits";
			TrcLog.Info($"Menu: screen {Screen.width}x{Screen.height}, scale {scale:0.00} (KSL {Kino.UI.Scale:0.00} x window {windowScale_:0.00}{(scale < (Kino.UI.Scale > 0.1f ? Kino.UI.Scale : 1f) * windowScale_ - 0.001f ? ", made smaller to fit" : "")}), " +
				$"window {WindowWidth:0}x{windowRect_.height:0} of {screenWidth:0}x{screenHeight:0}: {fit}.");
		}

		private void DrawWindow(int id) {
			BeginNavFrame();
			try {
				FrogInput();
				// Stage 2 of the redesign: the rail on the left, the page with its top line on the right.
				GUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
				DrawRail();
				GUILayout.BeginVertical(ContentArea, GUILayout.ExpandHeight(true));
				DrawPageTop();
				DrawInviteBanner();
				DrawStartInviteBanner();
				bool showTab = tourStep_ < 0 || DrawTourCard();
				if (!showTab) {
					GUILayout.FlexibleSpace();
				} else if (tab_ == HomeTab) {
					DrawHomeTab();
				} else {
					DrawCategoryPage(DrawOpenTab);
				}
				GUILayout.EndVertical();
				GUILayout.EndHorizontal();
				DrawPadBar();
				DrawFrog();
			} finally {
				EndNavFrame();
			}

			GUI.DragWindow(new Rect(0, 0, WindowWidth, 64));
		}

		// How long the open page takes to draw (all its events in a frame), logged when it's slow (bskftw, Nov 6: pages
		// lagging; this says which one in the next bug report). Averaged over 2 s of frames on the same page, so one slow
		// click or the first frame of a page can't set it off (Nov 9 log; fixed Nov 12).
		private readonly System.Diagnostics.Stopwatch pageWatch_ = new System.Diagnostics.Stopwatch();
		private double pageFrameMs_, pageTotalMs_, pageWorstMs_;
		private int pageFrame_ = -1, pageTab_ = -1, pageFrames_;
		private float pageSince_, pageSlowLoggedAt_ = -100f;

		private void DrawOpenTab() {
			if (pageFrame_ != Time.frameCount) {
				CountPageFrame();
				pageFrameMs_ = 0;
				pageFrame_ = Time.frameCount;
			}
			pageWatch_.Restart();
			try {
				DrawOpenTabNow();
			} finally {
				pageWatch_.Stop();
				pageFrameMs_ += pageWatch_.Elapsed.TotalMilliseconds;
			}
		}

		/// <summary>Adds the last frame's drawing time to this page's 2 s and warns when their average is over 4 ms.</summary>
		private void CountPageFrame() {
			if (pageTab_ != tab_) {
				pageTab_ = tab_;
				pageTotalMs_ = pageWorstMs_ = 0;
				pageFrames_ = 0;
				pageSince_ = Time.unscaledTime;
				return;
			}
			pageTotalMs_ += pageFrameMs_;
			pageWorstMs_ = Math.Max(pageWorstMs_, pageFrameMs_);
			pageFrames_++;
			if (Time.unscaledTime - pageSince_ < 2f) {
				return;
			}
			// The slowest frame is left out of the average (Nov 12 log: one 4.3 s frame starting traffic made a 1 ms page read
			// 21 ms); the warning still names it.
			double average = (pageTotalMs_ - pageWorstMs_) / Math.Max(1, pageFrames_ - 1);
			if (pageFrames_ >= 30 && average > 4.0 && Time.unscaledTime - pageSlowLoggedAt_ > 30f) {
				pageSlowLoggedAt_ = Time.unscaledTime;
				TrcLog.Warning($"The window's page {tab_} (view {coursesView_}/{battleViewMode_}) takes {average:0.0} ms a frame to draw " +
					$"(average of {pageFrames_ - 1} frames, leaving out the slowest: {pageWorstMs_:0.0} ms).");
			}
			pageTotalMs_ = pageWorstMs_ = 0;
			pageFrames_ = 0;
			pageSince_ = Time.unscaledTime;
		}

		private void DrawOpenTabNow() {
			switch (tab_) {
				case MyCarTab: DrawMyCarTab(); break;
				case TuneSetsTab: DrawTuneSetsTab(); break;
				case CompareTab: DrawCompareTab(); break;
				case PlayersTab: DrawPlayersTab(); break;
				case ModCheckTab: DrawModCheckTab(); break;
				case CoursesTab: DrawCoursesTab(); break;
				case BattlesTab: DrawBattlesTab(); break;
				case RegulationsTab: DrawRegulationsTab(); break;
				case SuspensionTab: DrawSuspensionTab(); break;
				case OptionsTab: DrawOptionsTab(); break;
				case SpecsTab: DrawSpecsPage(); break;
				case SpecCarTab: DrawSpecCarPage(); break;
				case SpecPlayersTab: DrawSpecPlayersPage(); break;
				case BugTab: DrawBugTab(); break;
				case AboutTab: DrawAboutTab(); break;
				case HudTab: DrawHudTab(); break;
				case HostToolsTab: DrawHostToolsTab(); break;
				case StatsTab: DrawStatsTab(); break;
				default:
					bool drawn = false;
					DrawDevPage(tab_, ref drawn);
					if (!drawn) {
						DrawAboutTab();
					}
					break;
			}
		}

		private void OnTabOpened(int tab) {
			switch (tab) {
				case HomeTab: nextStatusRefresh_ = 0; nextPlayersRefresh_ = 0; break;
				case MyCarTab: nextStatusRefresh_ = 0; backupResult_ = ""; RefreshBackups(); break;
				case PlayersTab:
				case SpecPlayersTab:
					nextPlayersRefresh_ = 0;
					nextReportRequest_ = 0;
					break;
				case SpecCarTab: mySpecsUntil_ = 0; break;
				case ModCheckTab: modCheck_.Refresh(); break;
				case CoursesTab: deletePending_ = null; break;
			}
		}
	}
}
