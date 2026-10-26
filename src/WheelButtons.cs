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
		private bool looked_;
		private bool failed_;
		private readonly Dictionary<string, bool[]> held_ = new Dictionary<string, bool[]>(StringComparer.Ordinal);

		/// <summary>Buttons that went down since the last call, as bindings.</summary>
		public List<Binding> Pressed { get; } = new List<Binding>();

		public bool Available => !failed_ && reInput_ != null;
		public string Problem { get; private set; } = "";

		/// <summary>Device names Rewired sees now (for Options).</summary>
		public List<string> Devices { get; } = new List<string>();

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
				if (isReady_ == null || joysticks_ == null || name_ == null || buttonCount_ == null || getButton_ == null) {
					Problem = "CarX's controller library changed: wheel buttons can't be read.";
					failed_ = true;
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
				var args = new object[1];
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
						// First look at this device: buttons already held don't count as presses.
						for (int i = 0; i < count; i++) {
							args[0] = i;
							was[i] = (bool)getButton_.Invoke(stick, args);
						}
						continue;
					}
					for (int i = 0; i < count; i++) {
						args[0] = i;
						bool down = (bool)getButton_.Invoke(stick, args);
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
