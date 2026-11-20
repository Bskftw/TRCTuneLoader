// This player's staff pass: staff.pass in the mod's folder, when bskftw gave them one, and the flagged mods it excuses.
using System;
using System.IO;
using System.Text;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		private string staffPass_ = "";

		private void LoadStaffPass() {
			try {
				string path = Path.Combine(modFolder_, StaffPass.FileName);
				string text = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8).Trim() : "";
				staffPass_ = text.Length <= StaffPass.MaxLength ? text : "";
			} catch (Exception e) {
				staffPass_ = "";
				TrcLog.Warning($"Couldn't read {StaffPass.FileName}: {e.Message}");
			}
			if (staffPass_.Length > 0) {
				TrcLog.Info(HasStaffPass
					? $"Staff pass for {BattleMessage.PlainName(StaffPass.NameOf(staffPass_))}: flagged mods don't stop your runs counting."
					: $"{StaffPass.FileName} is there but isn't valid for this Steam account (or was taken back).");
			}
		}

		private bool HasStaffPass => StaffPass.IsValid(staffPass_, MySteamId);

		/// <summary>Flagged mods that count against this player's own runs: none with a valid staff pass.</summary>
		private int BlockingFlags => HasStaffPass ? 0 : modCheck_.Flags.Count;
	}
}
