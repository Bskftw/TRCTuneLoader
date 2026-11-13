// Workshop bodies: the parts and items a car uses, their titles, and cleaning their text to show.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using CarProfile;
using DB;
using DB.Meta;
using DI;
using GameOverlay;
using LocalCaching.Manager;
using PrefsModels;
using SyncMultiplayer;
using UnityEngine;
using ParameterType = Db.Meta.CarProfile.CarProfileParameterType;

namespace TRC.TuneLoader {
	public sealed partial class CarXTuneAdapter : ICarTuneAdapter {
		public bool TryGetWorkshopItems(CarInfo car, out List<WorkshopItem> items, out string error) {
			items = new List<WorkshopItem>();
			try {
				CarVisualManager.VisualSettings visual = Prefs.carSettings.GetVisualForCar(car.CarId);
				items = WorkshopItems(car.CarId, car.SlotId, visual?.parts, visual?.networkparts, out _);
				error = "";
				return true;
			} catch (Exception e) {
				return Failed(e, out error);
			}
		}

		/// <summary>
		/// The Workshop (or mod.io) items among a car's body parts. <paramref name="parts"/> are the parts CarX
		/// put on the car here (Workshop parts are "external"); <paramref name="networkParts"/> are another
		/// player's Workshop parts CarX doesn't show here (Workshop ID to part IDs), because this PC doesn't
		/// have the item or the option to show other players' Workshop cars is off. CarX then shows the car
		/// with its stock body, so the live stance check can't see them. When this PC has the item, the car's
		/// stance with all its parts is worked out from the parts' commands, the way CarX builds it
		/// (<paramref name="stance"/>: the changes from the stock stance).
		/// </summary>
		private static List<WorkshopItem> WorkshopItems(int carId, int slotId, ICollection<int> parts, IDictionary<ulong, HashSet<int>> networkParts, out List<string> stance) {
			var items = new List<WorkshopItem>();
			var partsOf = new Dictionary<WorkshopItem, List<Customization.CarPart>>();
			var allParts = parts != null ? new HashSet<int>(parts) : new HashSet<int>();

			WorkshopItem ItemFor(ulong id) {
				WorkshopItem found = items.Find(i => i.Id == id);
				if (found == null) {
					found = new WorkshopItem { Id = id };
					items.Add(found);
					partsOf[found] = new List<Customization.CarPart>();
				}
				return found;
			}

			if (parts != null) {
				foreach (var part in Customization.CarPartDatabase.ConvertToPartList(parts)) {
					if (part != null && part.external) {
						WorkshopItem item = ItemFor(part.packetid);
						item.Installed = true;
						item.ShownHere = true;
						item.Source = SourceName(part.ugcServiceProvider);
						partsOf[item].Add(part);
					}
				}
			}

			if (networkParts != null) {
				foreach (var pair in networkParts) {
					WorkshopItem item = ItemFor(pair.Key);
					if (!Customization.CarPartDatabase.externalDatabases.TryGetValue(pair.Key, out var database) || pair.Value == null) {
						continue;
					}
					foreach (int partId in pair.Value) {
						if (database.nativeParts.TryGetValue(partId, out var part) && part != null) {
							item.Installed = true;
							item.Source = SourceName(part.ugcServiceProvider);
							partsOf[item].Add(part);
							allParts.Add(part.id);
						}
					}
				}
			}

			stance = new List<string>();
			if (items.Count == 0) {
				return items;
			}

			// The car as CarX builds it with all these parts: base car data, then every part's commands.
			Stance stock = StockStance(carId, slotId);
			Stance built = BaseStance(slotId);
			if (stock != null && built != null) {
				ApplyParts(built, allParts.Count > 0 ? allParts : DefaultParts(carId));
				stance = built.DifferencesFrom(stock);
			}

			foreach (WorkshopItem item in items) {
				List<Customization.CarPart> itemParts = partsOf[item];
				item.Name = WorkshopTitle(item.Id, itemParts.Count > 0 ? itemParts[0].ugcServiceProvider : Services.UGC.Interfaces.IUGCManager.ServiceProvider.Steamworkshop);
				if (string.IsNullOrEmpty(item.Name)) {
					item.Name = Clean(string.Join(", ", itemParts.Select(p => p.description).Where(d => !string.IsNullOrEmpty(d)).Distinct()));
				}
				item.MovesWheels = stance.Count > 0 && itemParts.Exists(p => p.cmds != null && p.cmds.Exists(Stance.MovesWheels));
			}

			return items;
		}

		private static string SourceName(Services.UGC.Interfaces.IUGCManager.ServiceProvider provider) =>
			provider == Services.UGC.Interfaces.IUGCManager.ServiceProvider.ModIO ? "mod.io" : "Steam Workshop";

		/// <summary>The Workshop item's title when this PC is subscribed to it, else "".</summary>
		private static string WorkshopTitle(ulong id, Services.UGC.Interfaces.IUGCManager.ServiceProvider provider) {
			try {
				var manager = Services.UGC.Utils.UGCServiceProvider.instance.GetManagerBy(Services.UGC.Interfaces.IUGCManager.ContentType.Cars, provider);
				return Clean(manager?.TryGetInstalledItem(id)?.title);
			} catch (Exception) {
				return "";
			}
		}

		/// <summary>Text from Workshop authors, made safe to show: no rich-text tags, one line, at most 60 characters.</summary>
		private static string Clean(string text) {
			if (string.IsNullOrEmpty(text)) {
				return "";
			}
			string clean = new string(text.Where(c => c != '<' && c != '>' && !char.IsControl(c)).ToArray()).Trim();
			return clean.Length > 60 ? clean.Substring(0, 60) + "..." : clean;
		}
	}
}
