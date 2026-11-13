// The best speeds at a speed trap: the fastest ever and the car that set it, the fastest in the car you're in, and your last.
using System.Globalization;

namespace TRC.TuneLoader {
	public partial class TRCTuneLoaderMod {
		/// <summary>The fastest speed ever saved at a trap and the car it was in, or (0, "").</summary>
		private (float Kmh, string Car) TrapBestEver(string key) {
			SpeedTrapRecord best = null;
			foreach (SpeedTrapRecord r in trapRecords_) {
				if (r.TrapKey == key && (best == null || r.Kmh > best.Kmh)) {
					best = r;
				}
			}
			return best != null ? (best.Kmh, best.Car ?? "") : (0f, "");
		}

		/// <summary>The fastest speed saved at a trap in this car, or 0.</summary>
		private float TrapBestInCar(string key, int carId) {
			float best = 0f;
			foreach (SpeedTrapRecord r in trapRecords_) {
				if (r.TrapKey == key && r.CarId == carId && r.Kmh > best) {
					best = r.Kmh;
				}
			}
			return best;
		}

		/// <summary>The fastest speed ever saved at a trap, any car, or "---". (Every speed caught is saved at once, so this session's are in.)</summary>
		private string FastestEverKmh(string key) {
			float best = TrapBestEver(key).Kmh;
			return best > 0 ? best.ToString("0", CultureInfo.InvariantCulture) : "---";
		}

		/// <summary>
		/// The fastest speed saved at a trap in this car, or "---". It used to take this session's last speed too, whichever
		/// car set it, so after a car change the sign could show the other car's speed.
		/// </summary>
		private string FastestKmh(string key) {
			float best = TrapBestInCar(key, pose_.CarId);
			return best > 0 ? best.ToString("0", CultureInfo.InvariantCulture) : "---";
		}

		/// <summary>The newest speed saved at a trap in this car, or "---".</summary>
		private string LastSavedKmh(string key) {
			for (int i = trapRecords_.Count - 1; i >= 0; i--) {
				SpeedTrapRecord r = trapRecords_[i];
				if (r.TrapKey == key && r.CarId == pose_.CarId) {
					return r.Kmh.ToString("0", CultureInfo.InvariantCulture);
				}
			}
			return "---";
		}

		/// <summary>Detailed timing, under a trap's sign: "best 192 LYNX · this car 187", or "".</summary>
		private string TrapSignDetail(string key) {
			var ever = TrapBestEver(key);
			if (ever.Kmh <= 0) {
				return "";
			}
			float mine = TrapBestInCar(key, pose_.CarId);
			return $"best {ever.Kmh:0} {ShortCar(ever.Car)}{(mine > 0 && mine < ever.Kmh ? $" · this car {mine:0}" : mine <= 0 ? " · this car ---" : "")}";
		}

		/// <summary>Detailed timing, in the trap flash (worked out before this speed is saved): the fastest ever with its car, and this car's best.</summary>
		private string TrapFlashDetail(string key, float kmh) {
			var ever = TrapBestEver(key);
			if (ever.Kmh <= 0) {
				return "";
			}
			float mine = TrapBestInCar(key, pose_.CarId);
			string best = kmh > ever.Kmh ? $"was {ever.Kmh:0} km/h in {ShortCar(ever.Car)}" : $"best ever {ever.Kmh:0} km/h in {ShortCar(ever.Car)}";
			return $"\n<color={Theme.MutedHex}>{best} · this car {(mine > 0 ? $"{mine:0}" : "---")}</color>";
		}
	}
}
