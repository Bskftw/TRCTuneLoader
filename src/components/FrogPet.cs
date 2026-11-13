// The frog pet that hops around the window.
using System;
using System.Collections.Generic;
using System.Reflection;
using KSL.API;
using UnityEngine;

namespace TRC.TuneLoader {
	/// <summary>
	/// The frog (bskftw, Nov 2: "a frog pet that jumps around when you have the trc loader open. make him an easter
	/// egg"). Click the TRC logo in the window's header 5 times quickly and he moves in: he hops about along the bottom
	/// of the window, blinks, and jumps and says something when clicked. 5 more clicks on the logo and he hops off.
	/// Saved in the <c>frog_pet</c> pref. Picture: Resources/frog.png (tools/make_frog.py).
	/// </summary>
	public partial class TRCTuneLoaderMod {
		private const string FrogPref = "frog_pet";
		private const float FrogSize = 56f;
		private const int LogoClicksForFrog = 5;
		private const float LogoClickSeconds = 3f;

		private static readonly string[] FrogLines = {
			"Ribbit!", "Ribbit ribbit!", "*croak*", "Full send!", "Is that TRC spec?", "Touge run?",
			"Purple sector!", "Brap brap!", "Lily pad drift!", "Don't jump the start!",
		};

		private enum FrogMove { Sit, Crouch, Jump, Land, Leave }

		private bool frogOn_;
		private Texture2D frogTexture_;
		private bool frogLoaded_;
		private readonly List<float> logoClicks_ = new List<float>();
		private readonly System.Random frogRandom_ = new System.Random();

		private FrogMove frogMove_;
		private float frogX_ = -1f;
		private float frogFromX_;
		private float frogToX_;
		private float frogSince_;
		private float frogJumpSeconds_;
		private float frogJumpHeight_;
		private float frogNextHop_;
		private float frogNextBlink_;
		private float frogBlinkUntil_;
		private string frogSays_ = "";
		private float frogSaysUntil_;
		private int frogPokes_;

		private void LoadFrog() {
			frogOn_ = Kino.Prefs.Get(FrogPref, false);
		}

		/// <summary>Called when the logo is clicked: the fifth click within a few seconds calls the frog in, or sends him off.</summary>
		private void LogoClicked() {
			float now = Time.unscaledTime;
			logoClicks_.Add(now);
			logoClicks_.RemoveAll(t => now - t > LogoClickSeconds);
			if (logoClicks_.Count < LogoClicksForFrog) {
				return;
			}
			logoClicks_.Clear();
			if (!frogOn_) {
				frogOn_ = true;
				frogX_ = -1f;
				SavePref(FrogPref, true);
				FrogSay("Ribbit! Hi!");
				TrcLog.Info("A frog moved in.");
			} else if (frogMove_ != FrogMove.Leave) {
				FrogSay("Bye! Ribbit!");
				frogMove_ = FrogMove.Leave;
				frogSince_ = now;
				TrcLog.Info("The frog hopped off.");
			}
		}

		private Rect FrogRect(float floor, float lift) => new Rect(frogX_, floor - FrogSize - lift, FrogSize, FrogSize);

		private float FrogFloor => windowRect_.height - 10f;

		/// <summary>At the start of the window's drawing, so a click on him isn't taken by a button underneath.</summary>
		private void FrogInput() {
			if (!frogOn_ || frogX_ < 0f) {
				return;
			}
			Event e = Event.current;
			if (e.type != EventType.MouseDown || e.button != 0 || !FrogRect(FrogFloor, FrogLift()).Contains(e.mousePosition)) {
				return;
			}
			e.Use();
			if (frogMove_ == FrogMove.Leave) {
				return;
			}
			frogPokes_++;
			FrogSay(frogPokes_ % 7 == 0 ? "Stop poking me!" : FrogLines[frogRandom_.Next(FrogLines.Length)]);
			if (frogMove_ == FrogMove.Sit) {
				StartHop(frogX_, 70f, 0.6f);
			}
		}

		/// <summary>At the end of the window's drawing: the frog over everything else, and what he's saying.</summary>
		private void DrawFrog() {
			if (!frogOn_) {
				return;
			}
			if (!frogLoaded_) {
				frogLoaded_ = true;
				try {
					frogTexture_ = Kino.Utils.LoadEmbeddedTexture(Assembly.GetExecutingAssembly(), "TRC.TuneLoader.Resources.frog.png");
					if (frogTexture_ != null) {
						frogTexture_.filterMode = FilterMode.Bilinear;
					}
				} catch (Exception e) {
					TrcLog.Warning($"Couldn't load the frog: {e.Message}");
				}
			}
			if (frogTexture_ == null) {
				return;
			}
			float now = Time.unscaledTime;
			float right = WindowWidth - FrogSize - 8f;
			if (frogX_ < 0f) {
				// Moving in: a hop in from the left edge.
				frogX_ = 8f;
				StartHop(8f + 60f + (float)frogRandom_.NextDouble() * 200f, 60f, 0.6f);
			}
			if (Event.current.type == EventType.Repaint) {
				StepFrog(now, right);
				if (!frogOn_) {
					return;
				}
			}

			float lift = FrogLift();
			int frame = frogMove_ == FrogMove.Jump || frogMove_ == FrogMove.Leave ? 3
				: frogMove_ == FrogMove.Crouch || frogMove_ == FrogMove.Land ? 2
				: now < frogBlinkUntil_ ? 1 : 0;
			Rect rect = FrogRect(FrogFloor, lift);
			GUI.DrawTextureWithTexCoords(rect, frogTexture_, new Rect(frame * 0.25f, 0f, 0.25f, 1f));

			if (frogSays_.Length > 0 && now < frogSaysUntil_) {
				var content = new GUIContent(frogSays_);
				Vector2 size = Theme.HudPill.CalcSize(content);
				float x = Mathf.Clamp(rect.center.x - size.x / 2f, 4f, WindowWidth - size.x - 4f);
				GUI.Label(new Rect(x, rect.y - size.y - 2f, size.x, size.y), content, Theme.HudPill);
			}
		}

		private void StepFrog(float now, float right) {
			float t = now - frogSince_;
			if (now >= frogNextBlink_) {
				frogBlinkUntil_ = now + 0.13f;
				frogNextBlink_ = now + 2f + (float)frogRandom_.NextDouble() * 3.5f;
			}
			switch (frogMove_) {
				case FrogMove.Sit:
					if (now >= frogNextHop_) {
						bool big = frogRandom_.NextDouble() < 0.2;
						float distance = big ? 150f + (float)frogRandom_.NextDouble() * 170f : 40f + (float)frogRandom_.NextDouble() * 100f;
						float to = frogX_ + (frogRandom_.Next(2) == 0 ? -distance : distance);
						if (to < 8f || to > right) {
							to = frogX_ + (to < 8f ? distance : -distance);
						}
						StartHop(Mathf.Clamp(to, 8f, right), big ? 90f : 34f, big ? 0.7f : 0.42f);
					}
					break;
				case FrogMove.Crouch:
					if (t >= 0.14f) {
						frogMove_ = FrogMove.Jump;
						frogSince_ = now;
					}
					break;
				case FrogMove.Jump:
					float p = Mathf.Clamp01(t / frogJumpSeconds_);
					frogX_ = Mathf.Lerp(frogFromX_, frogToX_, p);
					if (p >= 1f) {
						frogMove_ = FrogMove.Land;
						frogSince_ = now;
					}
					break;
				case FrogMove.Land:
					if (t >= 0.1f) {
						frogMove_ = FrogMove.Sit;
						frogSince_ = now;
						frogNextHop_ = now + 1.2f + (float)frogRandom_.NextDouble() * 3.3f;
					}
					break;
				case FrogMove.Leave:
					if (t >= 0.9f) {
						frogOn_ = false;
						frogX_ = -1f;
						frogMove_ = FrogMove.Sit;
						SavePref(FrogPref, false);
					}
					break;
			}
			frogX_ = Mathf.Clamp(frogX_, 8f, Mathf.Max(8f, right));
		}

		/// <summary>How high he is off the floor right now (pixels).</summary>
		private float FrogLift() {
			float t = Time.unscaledTime - frogSince_;
			if (frogMove_ == FrogMove.Jump) {
				float p = Mathf.Clamp01(t / frogJumpSeconds_);
				return 4f * frogJumpHeight_ * p * (1f - p);
			}
			if (frogMove_ == FrogMove.Leave) {
				// Up and out through the top of the window.
				return t * t * 1600f + t * 300f;
			}
			return 0f;
		}

		private void StartHop(float to, float height, float seconds) {
			frogFromX_ = frogX_;
			frogToX_ = to;
			frogJumpHeight_ = height;
			frogJumpSeconds_ = seconds;
			frogMove_ = FrogMove.Crouch;
			frogSince_ = Time.unscaledTime;
		}

		private void FrogSay(string line) {
			frogSays_ = line;
			frogSaysUntil_ = Time.unscaledTime + 1.8f;
		}
	}
}
