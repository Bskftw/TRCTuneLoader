using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TRC.TuneLoader {
	/// <summary>
	/// The official TRC tunes, built into the mod as KINO .knd files (official_tunes/&lt;set&gt;/ in the
	/// project, embedded by TRCTuneLoader.csproj). Players can't see or change them; the encrypted .ksm
	/// hides them. Several sets can be built in (see TuneSets); each car can have a tune in each set.
	/// </summary>
	public sealed class OfficialTunes {
		private const string ResourcePrefix = "TRC.TuneLoader.Official/";

		/// <summary>All tunes, by car name and then set order (main set first).</summary>
		public List<TuneFile> Tunes { get; } = new List<TuneFile>();
		public List<string> Errors { get; } = new List<string>();

		/// <summary>The IDs of the sets built in, main set first.</summary>
		public List<string> Sets { get; } = new List<string>();

		private bool carsNamed_;

		public void Load() {
			Tunes.Clear();
			Errors.Clear();
			Sets.Clear();

			var assembly = typeof(OfficialTunes).Assembly;
			var resources = assembly.GetManifestResourceNames()
				.Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal))
				.OrderBy(n => n, StringComparer.Ordinal);

			foreach (string resource in resources) {
				string rest = resource.Substring(ResourcePrefix.Length);
				int slash = rest.IndexOfAny(new[] { '\\', '/' });
				string set = slash > 0 ? rest.Substring(0, slash) : "";
				string file = slash > 0 ? rest.Substring(slash + 1) : rest;

				try {
					byte[] data;
					using (var stream = assembly.GetManifestResourceStream(resource))
					using (var memory = new MemoryStream()) {
						stream.CopyTo(memory);
						data = memory.ToArray();
					}

					Tunes.Add(TuneFile.FromKnd(KndFile.Read(data), set, file));
					if (!Sets.Contains(set)) {
						Sets.Add(set);
					}
				} catch (Exception e) {
					Errors.Add($"{set}/{file}: {e.Message}");
				}
			}

			Sets.Sort((a, b) => TuneSets.Order(a).CompareTo(TuneSets.Order(b)));
			SortTunes();
		}

		/// <summary>Gives each tune CarX's car name. Needs the game's car list, so it's retried until it works.</summary>
		public void NameCars(ICarTuneAdapter adapter) {
			if (carsNamed_) {
				return;
			}

			bool all = true;
			foreach (TuneFile tune in Tunes) {
				if (int.TryParse(tune.CarId, out int carId) && adapter.TryGetCarName(carId, out string name)) {
					tune.SetCarName(name);
				} else {
					all = false;
				}
			}

			SortTunes();
			carsNamed_ = all;
		}

		/// <summary>The official tunes for this car and setup slot, one per set that has it, main set first.</summary>
		public List<TuneFile> ForCar(int carId, int slotId) => Tunes.Where(t => t.IsFor(carId, slotId)).ToList();

		/// <summary>The tune for this car and setup slot in one set, or null.</summary>
		public TuneFile ForCar(int carId, int slotId, string setId) =>
			Tunes.FirstOrDefault(t => t.IsFor(carId, slotId) && string.Equals(t.SetId, setId, StringComparison.OrdinalIgnoreCase));

		private void SortTunes() {
			Tunes.Sort((a, b) => {
				int byCar = string.Compare(a.Car, b.Car, StringComparison.OrdinalIgnoreCase);
				return byCar != 0 ? byCar : TuneSets.Order(a.SetId).CompareTo(TuneSets.Order(b.SetId));
			});
		}
	}
}
