using System;
using System.Collections.Generic;
using System.Linq;

namespace TRC.TuneLoader {
	/// <summary>Readable names and groups for CarX's setting names, in the order the menu shows them.</summary>
	public static class TuneLabels {
		public sealed class Setting {
			public readonly string Key;
			public readonly string Label;
			public readonly string Group;

			public Setting(string group, string key, string label) {
				Group = group;
				Key = key;
				Label = label;
			}
		}

		public static readonly List<Setting> Settings = new List<Setting> {
			new Setting("Front suspension", "front_spring_way", "Front spring length"),
			new Setting("Front suspension", "front_spring_stiffness", "Front spring stiffness"),
			new Setting("Front suspension", "front_fast_bump", "Front fast bump"),
			new Setting("Front suspension", "front_fast_rebound", "Front fast rebound"),
			new Setting("Front suspension", "front_slow_bump", "Front slow bump"),
			new Setting("Front suspension", "front_slow_rebound", "Front slow rebound"),
			new Setting("Front suspension", "front_stabilizer", "Front anti-roll bar"),
			new Setting("Front suspension", "front_toe", "Front toe"),
			new Setting("Front suspension", "front_camber", "Front camber"),
			new Setting("Front suspension", "front_caster", "Caster"),
			new Setting("Front suspension", "front_ackerman", "Ackermann"),
			new Setting("Front suspension", "front_lock", "Steering lock"),
			new Setting("Front suspension", "front_kingpin", "Kingpin"),
			new Setting("Front suspension", "front_wheel_center_trail", "Wheel center trail"),
			new Setting("Front suspension", "front_base_offset", "Front wheel offset"),

			new Setting("Rear suspension", "rear_spring_way", "Rear spring length"),
			new Setting("Rear suspension", "rear_spring_stiffness", "Rear spring stiffness"),
			new Setting("Rear suspension", "rear_fast_bump", "Rear fast bump"),
			new Setting("Rear suspension", "rear_fast_rebound", "Rear fast rebound"),
			new Setting("Rear suspension", "rear_slow_bump", "Rear slow bump"),
			new Setting("Rear suspension", "rear_slow_rebound", "Rear slow rebound"),
			new Setting("Rear suspension", "rear_stabilizer", "Rear anti-roll bar"),
			new Setting("Rear suspension", "rear_toe", "Rear toe"),
			new Setting("Rear suspension", "rear_camber", "Rear camber"),
			new Setting("Rear suspension", "rear_base_offset", "Rear wheel offset"),

			new Setting("Tyres and wheels", "front_tire_type", "Front tyre type"),
			new Setting("Tyres and wheels", "rear_tire_type", "Rear tyre type"),
			new Setting("Tyres and wheels", "front_tyre_pressure", "Front tyre pressure"),
			new Setting("Tyres and wheels", "rear_tyre_pressure", "Rear tyre pressure"),
			new Setting("Tyres and wheels", "front_tyre_width", "Front tyre width"),
			new Setting("Tyres and wheels", "rear_tyre_width", "Rear tyre width"),
			new Setting("Tyres and wheels", "front_tyre_profile", "Front tyre profile"),
			new Setting("Tyres and wheels", "rear_tyre_profile", "Rear tyre profile"),
			new Setting("Tyres and wheels", "front_disc_diam", "Front rim size"),
			new Setting("Tyres and wheels", "rear_disc_diam", "Rear rim size"),
			new Setting("Tyres and wheels", "front_tire_stretch", "Front tyre stretch"),
			new Setting("Tyres and wheels", "rear_tire_stretch", "Rear tyre stretch"),

			new Setting("Brakes", "max_brake_torque", "Brake strength"),
			new Setting("Brakes", "brake_front_percent", "Brake balance (front)"),
			new Setting("Brakes", "brake_abs", "ABS"),

			new Setting("Engine and gearbox", "max_torque", "Max torque"),
			new Setting("Engine and gearbox", "max_torque_rpm", "Max torque RPM"),
			new Setting("Engine and gearbox", "rev_limiter", "Rev limiter"),
			new Setting("Engine and gearbox", "turbo_charged", "Turbo"),
			new Setting("Engine and gearbox", "turbo_pressure", "Turbo pressure"),
			new Setting("Engine and gearbox", "traction_control", "Traction control"),
			new Setting("Engine and gearbox", "launch_control", "Launch control"),
			new Setting("Engine and gearbox", "drivetrain_type", "Drivetrain"),
			new Setting("Engine and gearbox", "final_drive", "Final drive"),
			new Setting("Engine and gearbox", "gearbox1", "Gear 1"),
			new Setting("Engine and gearbox", "gearbox2", "Gear 2"),
			new Setting("Engine and gearbox", "gearbox3", "Gear 3"),
			new Setting("Engine and gearbox", "gearbox4", "Gear 4"),
			new Setting("Engine and gearbox", "gearbox5", "Gear 5"),
			new Setting("Engine and gearbox", "gearbox6", "Gear 6"),
			new Setting("Engine and gearbox", "gearbox7", "Gear 7"),
			new Setting("Engine and gearbox", "gearbox8", "Gear 8"),

			new Setting("Differential", "diff_type", "Diff type"),
			new Setting("Differential", "diff_lock_coef", "Diff locking"),
			new Setting("Differential", "diff_preload", "Diff preload"),
			new Setting("Differential", "diff_lock_power", "Diff lock (power)"),
			new Setting("Differential", "diff_lock_coast", "Diff lock (coast)"),

			new Setting("Weight", "mass", "Weight"),
			new Setting("Weight", "weight_front_percent", "Weight balance (front)"),
			new Setting("Weight", "weight_up_percent", "Centre of gravity height"),

			new Setting("Sound only (not in the tune code)", "wastegate_sound", "Wastegate sound"),
			new Setting("Sound only (not in the tune code)", "seq_transmision_sound", "Sequential gearbox sound"),
		};

		private const string OtherGroup = "Other";

		private static readonly Dictionary<string, int> Order =
			Settings.Select((s, i) => new { s.Key, i }).ToDictionary(x => x.Key, x => x.i, StringComparer.Ordinal);

		private static readonly Dictionary<string, Setting> ByKey =
			Settings.ToDictionary(s => s.Key, StringComparer.Ordinal);

		public static string Label(string key) =>
			ByKey.TryGetValue(key, out var setting) ? setting.Label : key.Replace('_', ' ');

		public static string Group(string key) =>
			ByKey.TryGetValue(key, out var setting) ? setting.Group : OtherGroup;

		/// <summary>Known settings in menu order first, then any others alphabetically.</summary>
		public static IEnumerable<string> InDisplayOrder(IEnumerable<string> keys) =>
			keys.OrderBy(k => Order.TryGetValue(k, out int i) ? i : int.MaxValue)
				.ThenBy(k => k, StringComparer.Ordinal);
	}
}
