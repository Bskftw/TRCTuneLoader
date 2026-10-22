using System;
using System.Collections.Generic;

namespace TRC.TuneLoader.CodeCheck {
	/// <summary>Checks of TuneCheck, the "Check my current tune" logic. Run with "CodeCheck.exe --selftest".</summary>
	internal static class SelfTest {
		private static int failures_;

		public static int Run() {
			var official = Dict(("front_camber", 0.8f), ("rear_toe", 0.25f));
			var defaults = Dict(("front_camber", 0.5f), ("rear_toe", 0.5f), ("mass", 0.5f), ("wastegate_sound", 0f));
			string officialCode = TuneFingerprint.Compute("3", "119", official);

			// The car has exactly the official values; a newer setting the tune doesn't list is at its default.
			var same = TuneCheck.Compare("3", "119", officialCode, official, Dict(("front_camber", 0.8f), ("rear_toe", 0.25f), ("mass", 0.5f)), defaults);
			Expect(same.Matches, "official tune on the car matches");
			Expect(same.Code == officialCode, "official tune on the car gives the official code");

			// Differences smaller than the rounding step don't count.
			var noise = TuneCheck.Compare("3", "119", officialCode, official, Dict(("front_camber", 0.8002f), ("rear_toe", 0.25f)), defaults);
			Expect(noise.Matches, "float noise is ignored");

			// One official setting changed.
			var changed = TuneCheck.Compare("3", "119", officialCode, official, Dict(("front_camber", 0.9f), ("rear_toe", 0.25f)), defaults);
			Expect(!changed.Matches && changed.Differences.Count == 1, "a changed setting is reported");
			Expect(changed.Code != officialCode, "a changed setting changes the code");

			// A setting the tune doesn't list isn't at its default.
			var extra = TuneCheck.Compare("3", "119", officialCode, official, Dict(("front_camber", 0.8f), ("rear_toe", 0.25f), ("mass", 0.9f)), defaults);
			Expect(!extra.Matches && extra.Differences.Count == 1, "a non-default extra setting is reported");
			Expect(extra.Code != officialCode, "a non-default extra setting changes the code");

			// Sound-only settings never count.
			var sound = TuneCheck.Compare("3", "119", officialCode, official, Dict(("front_camber", 0.8f), ("rear_toe", 0.25f), ("wastegate_sound", 1f)), defaults);
			Expect(sound.Matches, "sound-only settings are ignored");

			// A different car or setup slot never matches.
			var otherCar = TuneCheck.Compare("4", "119", officialCode, official, official, defaults);
			Expect(!otherCar.Matches, "another car doesn't match");

			// The car is missing an official setting entirely.
			var missing = TuneCheck.Compare("3", "119", officialCode, official, Dict(("front_camber", 0.8f)), Dict(("front_camber", 0.5f)));
			Expect(!missing.Matches && missing.Differences.Count == 1, "a setting the car doesn't have is reported");

			LiveSnapshotTests();

			Console.WriteLine(failures_ == 0 ? "Self-test OK" : $"Self-test FAILED: {failures_} problem(s)");
			return failures_ == 0 ? 0 : 1;
		}

		// Stand-ins for CarX's BaseCar.Desc / CarDesc: public fields, nested classes and arrays.
		private sealed class FakeAero { public float frontDownforce = 100f; public float rearDownforce = 100f; }
		private sealed class FakeTransmission { public int gearShiftType; public bool sequentialTransmission; public float frontDiffPreload = 500f; }
		private sealed class FakeCarDesc {
			public FakeAero aero = new FakeAero();
			public FakeTransmission transmission = new FakeTransmission();
			public float[] gears = { 3.5f, 2.1f, 1.4f };
		}
		private sealed class FakeDesc { public FakeCarDesc carXDesc = new FakeCarDesc(); public float frontBaseWidth = 1.4f; }

		private static void LiveSnapshotTests() {
			var reference = DescSnapshot.Of(new FakeDesc());

			Expect(DescSnapshot.Of(new FakeDesc()).Differences(reference).Count == 0, "an unchanged car has no live differences");

			var noise = new FakeDesc();
			noise.carXDesc.aero.frontDownforce = 100.00001f;
			Expect(DescSnapshot.Of(noise).Differences(reference).Count == 0, "float noise in live values is ignored");

			var downforce = new FakeDesc();
			downforce.carXDesc.aero.rearDownforce = 140f;
			var found = DescSnapshot.Of(downforce).Differences(reference);
			Expect(found.Count == 1 && found[0].Contains("aero.rearDownforce"), "changed downforce is reported");

			var gears = new FakeDesc();
			gears.carXDesc.gears = new[] { 3.5f, 2.1f };
			Expect(DescSnapshot.Of(gears).Differences(reference).Count >= 1, "a missing gear is reported");

			var gearbox = new FakeDesc();
			gearbox.carXDesc.transmission.gearShiftType = 1;
			Expect(DescSnapshot.Of(gearbox).Differences(reference).Count == 0, "the player's gearbox mode is ignored");

			var sequential = new FakeDesc();
			sequential.carXDesc.transmission.sequentialTransmission = true;
			Expect(DescSnapshot.Of(sequential).Differences(reference).Count == 1, "a changed bool is reported");

			var unusedDiff = new FakeDesc();
			unusedDiff.carXDesc.transmission.frontDiffPreload = 100f;
			Expect(DescSnapshot.Of(unusedDiff).Differences(reference).Count == 1, "a changed front diff is reported by default");
			Expect(DescSnapshot.Of(unusedDiff).Differences(reference, new[] { "carXDesc.transmission.frontDiff" }).Count == 0,
				"a diff the drivetrain doesn't use can be left out");
		}

		private static void Expect(bool condition, string what) {
			if (!condition) {
				failures_++;
				Console.WriteLine($"FAILED: {what}");
			}
		}

		private static Dictionary<string, float> Dict(params (string key, float value)[] pairs) {
			var dict = new Dictionary<string, float>(StringComparer.Ordinal);
			foreach (var (key, value) in pairs) {
				dict[key] = value;
			}
			return dict;
		}
	}
}
