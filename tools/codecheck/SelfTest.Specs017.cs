using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader.CodeCheck {
	/// <summary>
	/// Nov 3 (bskftw): manual sector checkpoints that replace the automatic ones and travel with the course, and specs
	/// made by players (a folder of tunes each) that can be shared as a zip and required by a regulation.
	/// </summary>
	internal static partial class SelfTest {
		private static void Tests017() {
			ManualSectorsReplaceAutomatic();
			ManualSectorsTravelWithTheCourse();
			SectorsCodeRoundTrip();
			PlayerSpecFolders();
			RegulationCanNeedASpec();
			DiffTypesByName();
		}

		/// <summary>bskftw, Nov 3: the diff type is a named type (Open, Welded, Viscous, Clutch type), not a number to keep between two values.</summary>
		private static void DiffTypesByName() {
			var plain = new Regulation { Name = "Grip", MaxHp = 500 };
			string before = plain.Code;
			var reg = Regulation.Parse(plain.ToText());
			reg.DiffTypes = new List<int> { 0, 3 };
			Regulation back = Regulation.Parse(reg.ToText());
			Expect(back.DiffTypes.Count == 2 && back.DiffTypes.Contains(0) && back.DiffTypes.Contains(3) && back.Code != before && Regulation.Parse(plain.ToText()).Code == before,
				"allowed diff types are saved by type, and a regulation without them keeps its code");
			var welded = new CarFacts { CarId = 7, Numbers = new Dictionary<string, float> { [Regulation.DiffTypeKey] = 1f } };
			RegulationResult result = back.Check(welded);
			Expect(result.Broken.Count == 1 && result.Broken[0].Contains("Welded") && result.Broken[0].Contains("Open, Clutch type"), "a welded diff breaks a regulation allowing open and clutch type, named in the message");
			var clutch = new CarFacts { CarId = 7, Numbers = new Dictionary<string, float> { [Regulation.DiffTypeKey] = 3f } };
			Expect(back.Check(clutch).Passes, "a clutch type diff meets it");
			Expect(back.Summary().Contains("Open/Clutch type diff"), "the regulation's summary names the diff types");
		}

		private static void ManualSectorsReplaceAutomatic() {
			Course course = MakeCourse(new List<Vector3> { new Vector3(0, 0, 0), new Vector3(0, 0, 1000) });
			int automatic = course.SectorEnds.Length;
			course.ManualSectors = new List<Vector3> { new Vector3(0, 0, 200) };
			bool wasOn = Course.UseManualSectors;
			try {
				Course.UseManualSectors = true;
				course.RefreshSectors();
				Expect(course.SectorEnds.Length == 2 && Mathf.Abs(course.SectorEnds[0] - 200f) < 1f && Mathf.Abs(course.SectorEnds[1] - course.Length) < 0.01f,
					"one manual sector end at 200 m makes two sectors, the first ending there");
				Course.UseManualSectors = false;
				course.RefreshSectors();
				Expect(course.SectorEnds.Length == automatic, "with manual sectors off in Options, the automatic sectors are used again");
			} finally {
				Course.UseManualSectors = wasOn;
				course.RefreshSectors();
			}
		}

		private static void ManualSectorsTravelWithTheCourse() {
			Course course = MakeCourse(new List<Vector3> { new Vector3(0, 0, 0), new Vector3(0, 0, 1000) });
			string code = course.Code;
			course.ManualSectors = new List<Vector3> { new Vector3(0, 0, 300), new Vector3(0, 0, 650) };
			course.Build(resample: false);
			Course back = CourseFile.Parse(CourseFile.ToText(course), CourseSource.Custom);
			Expect(back.ManualSectors.Count == 2 && back.Problems.Count == 0, "a course file keeps its manual sectors in [sectors]");
			Expect(back.Code == code && course.Code == code, "manual sectors don't change the course code, so times on it stay together");
			Expect(!Course.UseManualSectors || back.SectorEnds.Length == 3, "read back, the course has the three sectors its manual ends make");
		}

		private static void SectorsCodeRoundTrip() {
			var points = new List<Vector3> { new Vector3(12.34f, 5f, -78.9f), new Vector3(-1f, 2.5f, 300f) };
			string text = SectorOverlay.ToCode("CRS-1A2B-3C4D", points);
			Expect(SectorOverlay.TryParseCode(text, out string code, out List<Vector3> back) && code == "CRS-1A2B-3C4D" && back.Count == 2 &&
				Vector3.Distance(back[0], points[0]) < 0.01f, "a sectors code carries the course code and every sector end");
			Expect(!SectorOverlay.TryParseCode("CRS-1A2B-3C4D", out _, out _) && !SectorOverlay.TryParseCode("", out _, out _), "other text isn't taken for a sectors code");

			string folder = Path.Combine(Path.GetTempPath(), "trc_sectors_" + Guid.NewGuid().ToString("N"));
			try {
				string file = Path.Combine(folder, SectorOverlay.FileName);
				SectorOverlay.Load(file);
				SectorOverlay.Set("CRS-AAAA-BBBB", points);
				SectorOverlay.Load(file);
				Expect(SectorOverlay.For("crs-aaaa-bbbb").Count == 2 && SectorOverlay.For("CRS-OTHER").Count == 0, "sectors.txt keeps a map course's manual sectors by its code");
				SectorOverlay.Set("CRS-AAAA-BBBB", new List<Vector3>());
				SectorOverlay.Load(file);
				Expect(SectorOverlay.For("CRS-AAAA-BBBB").Count == 0, "clearing a course's manual sectors takes it out of sectors.txt");
			} finally {
				SectorOverlay.Load("");
				try {
					Directory.Delete(folder, true);
				} catch (Exception) {
				}
			}
		}

		private static void PlayerSpecFolders() {
			string root = Path.Combine(Path.GetTempPath(), "trc_specs_" + Guid.NewGuid().ToString("N"));
			string desk = Path.Combine(Path.GetTempPath(), "trc_specs_out_" + Guid.NewGuid().ToString("N"));
			try {
				string folder = PlayerSpecs.Create(root, "Drift Kings", "bskftw", "v2", "league spec");
				PlayerSpec spec = PlayerSpecs.Load(folder);
				Expect(spec.Title == "Drift Kings v2" && spec.Author == "bskftw" && spec.Tunes.Count == 0, "a new spec is a folder with its name, author and version");
				var draft = new TuneDraft { Name = "Drift Kings v2 - AE86", Car = "AE86", CarId = 7, SlotId = 3, Slot = "Drift", Values = new Dictionary<string, float> { ["camber_front"] = 0.4f, ["toe_rear"] = 0.55f } };
				PlayerSpecs.SaveTune(spec, draft);
				draft.Values["camber_front"] = 0.5f;
				PlayerSpecs.SaveTune(spec, draft);
				spec = PlayerSpecs.Load(folder);
				Expect(spec.Tunes.Count == 1 && spec.For(7, 3) != null && Math.Abs(spec.For(7, 3).Values["camber_front"] - 0.5f) < 1e-6, "saving a car's tune again replaces its old one in the spec");
				Expect(PlayerSpecs.Create(root, "Drift Kings", "", "v2", "") != folder, "a second spec with the same name gets its own folder");

				string zip = PlayerSpecs.Export(spec, desk);
				string other = Path.Combine(Path.GetTempPath(), "trc_specs_friend_" + Guid.NewGuid().ToString("N"));
				Directory.CreateDirectory(other);
				File.Copy(zip, Path.Combine(other, Path.GetFileName(zip)));
				File.WriteAllText(Path.Combine(other, "not a spec.txt"), "hello");
				List<string> done = PlayerSpecs.InstallZips(other);
				List<PlayerSpec> installed = PlayerSpecs.LoadAll(other);
				Expect(done.Count == 1 && installed.Count == 1 && installed[0].Title == "Drift Kings v2" && installed[0].For(7, 3) != null,
					"a shared zip dropped in another player's specs folder installs as the same spec");
				Expect(!Directory.GetFiles(other, "*.zip").Any() && Directory.Exists(Path.Combine(other, PlayerSpecs.InstalledFolder)),
					"the zip is moved out of the way once it's installed, and that folder isn't read as a spec");
				Directory.Delete(other, true);
			} finally {
				foreach (string f in new[] { root, desk }) {
					try {
						Directory.Delete(f, true);
					} catch (Exception) {
					}
				}
			}
		}

		private static void RegulationCanNeedASpec() {
			var plain = new Regulation { Name = "Gunma", MaxHp = 400 };
			string before = plain.Code;
			var specced = Regulation.Parse(plain.ToText());
			Expect(specced != null && specced.Code == before, "a regulation without a spec keeps its code (it's only written when set)");
			specced.Spec = "Drift Kings v2";
			Regulation back = Regulation.Parse(specced.ToText());
			Expect(back.Spec == "Drift Kings v2" && back.Code != before, "a regulation's spec is saved, and is part of its code");
			Expect(!back.Check(new CarFacts { CarId = 7, Specs = new List<string>() }).Passes, "a car that isn't on the spec doesn't meet it");
			Expect(back.Check(new CarFacts { CarId = 7, Specs = new List<string> { "drift kings v2" } }).Broken.Count == 0, "a car on the spec meets that rule");
			Expect(back.Check(new CarFacts { CarId = 7 }).Unknown.Contains("spec"), "another player's car (spec not known) is unknown, not failed");
			// Another player's car, checked on a PC that has the spec: judged; on a PC that doesn't: unknown, never failed.
			var theirs = new CarFacts { CarId = 7, Specs = new List<string>(), KnownSpecs = new List<string> { "Drift Kings v2" } };
			Expect(back.Check(theirs).Broken.Count == 1, "with the spec installed here, another player off the spec breaks the regulation");
			theirs.KnownSpecs = new List<string> { "Some other spec" };
			Expect(back.Check(theirs).Broken.Count == 0 && back.Check(theirs).Unknown.Contains("spec"), "without the spec installed here, it can't be judged, so it isn't failed");
		}
	}
}
