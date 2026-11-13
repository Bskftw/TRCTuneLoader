// What a suspension log means: plain-language notes on what the numbers point to, and each event as text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace TRC.TuneLoader {
	public sealed partial class SuspensionLog {
		/// <summary>Plain-language notes on what the numbers point to, each with the wheel(s) it's about.</summary>
		public List<string> Advice() {
			var notes = new List<string>();
			for (int w = 0; w < 4; w++) {
				WheelReport r = Wheels[w];
				if (r.Seconds < 5) {
					continue;
				}
				string name = WheelNames[w];
				if (r.Packing > 0) {
					notes.Add($"{name}: packed down {r.Packing} time(s) through a run of bumps (it couldn't extend back before the next one). Try less rebound damping, slow and fast, or a little less fast bump.");
				}
				if (r.ContactLostAfterCompression >= 3) {
					notes.Add($"{name}: left the road {r.ContactLostAfterCompression} times right after a hard compression (thrown off the bump). Try less fast bump, or slightly less rebound so it follows the road down again.");
				}
				int otherLosses = r.ContactLost - r.ContactLostAfterCompression;
				if (otherLosses >= 3) {
					notes.Add($"{name}: unloaded {otherLosses} times over crests or dips. Rebound may be too slow for the wheel to follow the road down, or the spring too soft at that end.");
				}
				if (r.Bottomed >= 3) {
					notes.Add($"{name}: hit full compression {r.Bottomed} times. A stiffer spring, more slow bump, more ride height or more travel would keep it off the stops.");
				}
				if (r.LoadSpikes >= 5) {
					notes.Add($"{name}: load spiked to {SpikeTimes:0.#}x its share of the weight {r.LoadSpikes} times on bumps. Fast bump may be too stiff for this road.");
				}
				if (r.MaxCompression > 0 && r.MaxCompression < 0.6f && r.Bottomed == 0) {
					notes.Add($"{name}: used only up to {r.MaxCompression * 100:0}% of its travel. It could go softer (spring or slow bump) and use more of it.");
				}
				float share = r.BumpShare;
				if (r.BumpSeconds + r.ReboundSeconds > 2 && (share > 0.62f || share < 0.38f)) {
					notes.Add(share > 0.5f
						? $"{name}: the damper spends much longer moving in bump than rebound ({share * 100:0}% / {100 - share * 100:0}%): rebound is slow compared with bump, which leads to packing."
						: $"{name}: the damper spends much longer moving in rebound than bump ({share * 100:0}% / {100 - share * 100:0}%): bump is stiff compared with rebound.");
				}
			}
			if (notes.Count == 0 && Seconds >= 10) {
				notes.Add("Nothing stands out: no packing, no contact lost after compressions, no bottoming. Drive the bumpiest part of the road to see more.");
			}
			return notes;
		}

		public static string EventText(SuspensionEvent e) {
			string where = e.Along >= 0 ? $"at {TimeAttackRun.Distance(e.Along)}" : "";
			switch (e.What) {
				case SuspensionEvent.Kind.ContactLost:
					return $"{WheelNames[e.Wheel]} left the road{(e.AfterCompression ? " right after a compression" : "")}{(e.Seconds > 0 ? $" for {e.Seconds:0.00} s" : "")} {where}, {e.Kmh:0} km/h";
				case SuspensionEvent.Kind.Packing:
					return $"{WheelNames[e.Wheel]} packed down from {e.From * 100:0}% to {e.To * 100:0}% {where}, {e.Kmh:0} km/h";
				case SuspensionEvent.Kind.Bottomed:
					return $"{WheelNames[e.Wheel]} bottomed out {where}, {e.Kmh:0} km/h";
				default:
					return $"{WheelNames[e.Wheel]} load spike ({e.To:0.0}x its share) {where}, {e.Kmh:0} km/h";
			}
		}
	}
}
