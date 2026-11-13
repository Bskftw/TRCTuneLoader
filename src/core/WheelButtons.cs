// Reads steering wheel and controller buttons.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace TRC.TuneLoader {
	/// <summary>
	/// Wheel and controller buttons. CarX reads wheels, pads and joysticks through Rewired, so this asks Rewired
	/// (found by name, no reference) which buttons are held, and works out the presses itself. It only reads:
	/// CarX still gets every button as usual, so pick one CarX doesn't use. Without Rewired it does nothing.
	/// </summary>
	public sealed class WheelButtons {
		/// <summary>A button on one device, saved as "device name#button" (like "Logitech G29#7").</summary>
		public struct Binding {
			public string Device;
			public int Button;

			public bool IsSet => !string.IsNullOrEmpty(Device) && Button >= 0;

			public static Binding None => new Binding { Device = "", Button = -1 };

			public string Save() => IsSet ? $"{Device}#{Button.ToString(CultureInfo.InvariantCulture)}" : "";

			public static Binding Parse(string text) {
				int hash = (text ?? "").LastIndexOf('#');
				if (hash <= 0 || !int.TryParse(text.Substring(hash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int button)) {
					return None;
				}
				return new Binding { Device = text.Substring(0, hash), Button = button };
			}

			public override string ToString() => IsSet ? $"{Device}, button {Button + 1}" : "none";
		}

		private Type reInput_;
		private PropertyInfo isReady_;
		private PropertyInfo controllers_;
		private PropertyInfo joysticks_;
		private PropertyInfo name_;
		private PropertyInfo buttonCount_;
		private MethodInfo getButton_;

		/// <summary>
		/// GetButton as a direct call, made once (docs/archive/OPTIMIZATION_REPORT.md item 6: Invoke per button per frame was 100+ slow calls
		/// a frame, each boxing its number). Null if it couldn't be made: then Invoke as before.
		/// </summary>
		private Func<object, int, bool> getButtonFast_;
		private readonly object[] args_ = new object[1];
		private PropertyInfo buttons_;
		private bool looked_;
		private bool failed_;
		private readonly Dictionary<string, bool[]> held_ = new Dictionary<string, bool[]>(StringComparer.Ordinal);

		/// <summary>Buttons that went down since the last call, as bindings.</summary>
		public List<Binding> Pressed { get; } = new List<Binding>();

		public bool Available => !failed_ && reInput_ != null;
		public string Problem { get; private set; } = "";

		/// <summary>Device names Rewired sees now (for Options).</summary>
		public List<string> Devices { get; } = new List<string>();

		/// <summary>Each device's button names as Rewired knows them ("A", "D-Pad Up"...), by button number; empty when unknown.</summary>
		public Dictionary<string, string[]> ButtonNames { get; } = new Dictionary<string, string[]>(StringComparer.Ordinal);

		private void Find() {
			looked_ = true;
			try {
				foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) {
					reInput_ = assembly.GetType("Rewired.ReInput", false);
					if (reInput_ != null) {
						break;
					}
				}
				if (reInput_ == null) {
					Problem = "CarX's controller library (Rewired) wasn't found.";
					failed_ = true;
					return;
				}
				isReady_ = reInput_.GetProperty("isReady", BindingFlags.Public | BindingFlags.Static);
				controllers_ = reInput_.GetProperty("controllers", BindingFlags.Public | BindingFlags.Static);
				joysticks_ = controllers_?.PropertyType.GetProperty("Joysticks");
				Type joystick = reInput_.Assembly.GetType("Rewired.Joystick", false);
				Type controller = reInput_.Assembly.GetType("Rewired.Controller", false);
				name_ = controller?.GetProperty("name");
				buttonCount_ = controller?.GetProperty("buttonCount");
				getButton_ = joystick?.GetMethod("GetButton", new[] { typeof(int) });
				buttons_ = controller?.GetProperty("Buttons");
				if (isReady_ == null || joysticks_ == null || name_ == null || buttonCount_ == null || getButton_ == null) {
					Problem = "CarX's controller library changed: wheel buttons can't be read.";
					failed_ = true;
				} else if (getButton_.ReturnType == typeof(bool)) {
					try {
						var stick = System.Linq.Expressions.Expression.Parameter(typeof(object), "stick");
						var button = System.Linq.Expressions.Expression.Parameter(typeof(int), "button");
						getButtonFast_ = System.Linq.Expressions.Expression.Lambda<Func<object, int, bool>>(
							System.Linq.Expressions.Expression.Call(System.Linq.Expressions.Expression.Convert(stick, joystick), getButton_, button),
							stick, button).Compile();
					} catch (Exception e) {
						getButtonFast_ = null;
						TrcLog.Info($"Wheel buttons are read the slower way ({e.Message}).");
					}
				}
			} catch (Exception e) {
				Problem = $"Wheel buttons can't be read: {e.Message}";
				failed_ = true;
			}
		}

		/// <summary>Call every frame: fills <see cref="Pressed"/> and <see cref="Devices"/>.</summary>
		public void Poll() {
			Pressed.Clear();
			if (!looked_) {
				Find();
			}
			if (failed_) {
				return;
			}
			try {
				if (!(bool)isReady_.GetValue(null, null)) {
					return;
				}
				object helper = controllers_.GetValue(null, null);
				if (!(joysticks_.GetValue(helper, null) is IList list)) {
					return;
				}
				Devices.Clear();
				foreach (object stick in list) {
					if (stick == null) {
						continue;
					}
					string device = (name_.GetValue(stick, null) as string ?? "").Trim();
					int count = Math.Min(128, (int)buttonCount_.GetValue(stick, null));
					Devices.Add(device);
					if (!held_.TryGetValue(device, out bool[] was) || was.Length != count) {
						was = new bool[count];
						held_[device] = was;
						ButtonNames[device] = NamesOf(stick, count);
						// First look at this device: buttons already held don't count as presses.
						for (int i = 0; i < count; i++) {
							was[i] = Held(stick, i);
						}
						continue;
					}
					for (int i = 0; i < count; i++) {
						bool down = Held(stick, i);
						if (down && !was[i]) {
							Pressed.Add(new Binding { Device = device, Button = i });
						}
						was[i] = down;
					}
				}
			} catch (Exception e) {
				Problem = $"Wheel buttons stopped working: {e.Message}";
				failed_ = true;
				TrcLog.Warning(Problem);
			}
		}

		private bool Held(object stick, int button) {
			if (getButtonFast_ != null) {
				try {
					return getButtonFast_(stick, button);
				} catch (Exception e) {
					// The fast way broke: the slower way from now on, which worked before it.
					getButtonFast_ = null;
					TrcLog.Info($"Wheel buttons: back to the slower way ({e.Message}).");
				}
			}
			args_[0] = button;
			return (bool)getButton_.Invoke(stick, args_);
		}

		private string[] NamesOf(object stick, int count) {
			var names = new string[count];
			try {
				if (buttons_?.GetValue(stick, null) is IList buttons) {
					for (int i = 0; i < count && i < buttons.Count; i++) {
						object id = buttons[i]?.GetType().GetProperty("elementIdentifier")?.GetValue(buttons[i], null);
						names[i] = id?.GetType().GetProperty("name")?.GetValue(id, null) as string ?? "";
					}
				}
			} catch (Exception e) {
				// Without names, the menu's controller buttons can still be picked by pressing them (Options).
				TrcLog.Once("button names", $"Couldn't read a controller's button names ({e.Message}), so the menu may not know its D-pad and A button; said once.");
			}
			for (int i = 0; i < count; i++) {
				names[i] = names[i] ?? "";
			}
			return names;
		}

		// ---- Keeping the car still while the menu is open ----

		private readonly List<object> mapsTurnedOff_ = new List<object>();

		/// <summary>
		/// Turns CarX's controller maps for pads, wheels and joysticks off (false) while the mod's menu has the pad, so
		/// the car doesn't steer or drive from the same buttons, and back on (true): only the ones this turned off. It
		/// only changes which of CarX's own maps are on, on this PC. Returns false when Rewired can't be asked.
		/// </summary>
		public bool SetDriving(bool on) {
			if (!looked_) {
				Find();
			}
			if (failed_) {
				return false;
			}
			try {
				if (on) {
					foreach (object map in mapsTurnedOff_) {
						map?.GetType().GetProperty("enabled")?.SetValue(map, true, null);
					}
					mapsTurnedOff_.Clear();
					return true;
				}
				if (mapsTurnedOff_.Count > 0) {
					return true;
				}
				object players = reInput_.GetProperty("players", BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null);
				if (!(players?.GetType().GetProperty("AllPlayers")?.GetValue(players, null) is IList all)) {
					return false;
				}
				Type controllerType = reInput_.Assembly.GetType("Rewired.ControllerType", false);
				if (controllerType == null) {
					return false;
				}
				object joystick = Enum.Parse(controllerType, "Joystick");
				foreach (object player in all) {
					object controllers = player?.GetType().GetField("controllers")?.GetValue(player);
					object maps = controllers?.GetType().GetField("maps")?.GetValue(controllers);
					MethodInfo getAll = maps?.GetType().GetMethod("GetAllMaps", new[] { controllerType });
					if (!(getAll?.Invoke(maps, new[] { joystick }) is IEnumerable list)) {
						continue;
					}
					foreach (object map in list) {
						PropertyInfo enabled = map?.GetType().GetProperty("enabled");
						if (enabled != null && enabled.GetValue(map, null) is bool isOn && isOn) {
							enabled.SetValue(map, false, null);
							mapsTurnedOff_.Add(map);
						}
					}
				}
				return true;
			} catch (Exception e) {
				TrcLog.Warning($"Couldn't {(on ? "give the controller back to CarX" : "keep the car still while the menu is open")}: {e.Message}");
				return false;
			}
		}

		/// <summary>Whether <paramref name="binding"/> was pressed this frame.</summary>
		public bool WasPressed(Binding binding) {
			if (!binding.IsSet) {
				return false;
			}
			foreach (Binding b in Pressed) {
				if (b.Button == binding.Button && string.Equals(b.Device, binding.Device, StringComparison.Ordinal)) {
					return true;
				}
			}
			return false;
		}
	}
}
