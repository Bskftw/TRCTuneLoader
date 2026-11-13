// A spec's folder: moving it to specs/removed (nothing is deleted), and opening a folder in Explorer.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		/// <summary>Moves the spec's folder into specs/removed (nothing is deleted), then reads the specs again.</summary>
		private void RemoveSpec(PlayerSpec spec) {
			try {
				string removed = Path.Combine(SpecsFolder, PlayerSpecs.RemovedFolder);
				Directory.CreateDirectory(removed);
				string to = Path.Combine(removed, $"{spec.Id} {DateTime.Now:yyyyMMdd-HHmmss}");
				Directory.Move(spec.Folder, to);
				removingSpec_ = "";
				pickedSpec_ = "";
				LoadSpecs();
				specResult_ = $"Removed {spec.Title} (moved to specs\\removed, in case you want it back).";
				TrcLog.Info($"Spec {spec.Title} moved to {to}.");
			} catch (Exception e) {
				specResult_ = $"Couldn't remove it: {e.Message}";
				TrcLog.Error(e);
			}
		}

		private static void OpenFolder(string folder) {
			try {
				Directory.CreateDirectory(folder);
				Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
			} catch (Exception e) {
				TrcLog.Warning($"Couldn't open {folder}: {e.Message}");
			}
		}
	}
}
