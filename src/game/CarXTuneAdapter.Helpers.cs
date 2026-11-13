// Small helpers the CarX connection shares: finding the car and the dyno, garage screen problems, number formats, errors.
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
		private static int CarIdOf(RaceCar raceCar) => raceCar.metaInfo != null ? raceCar.metaInfo.id : raceCar.carId;

		/// <summary>How many decimals a .NET number format shows ("N0" none, "N2" or "0.00" two), as Describe prints them.</summary>
		private static int DecimalsOf(string format) {
			if (string.IsNullOrEmpty(format)) {
				return 0;
			}
			char kind = char.ToUpperInvariant(format[0]);
			if ((kind == 'N' || kind == 'F') && format.Length > 1 && int.TryParse(format.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out int digits)) {
				return Math.Min(digits, 6);
			}
			int dot = format.IndexOf('.');
			return dot < 0 ? 0 : Math.Min(6, format.Skip(dot + 1).TakeWhile(c => c == '0' || c == '#').Count());
		}

		// The tuning screen's own routines that rebuild its slider list from the preview (private in CarX).
		private static readonly MethodInfo DynoFillLists =
			typeof(UIDynostandContext).GetMethod("FillParamsListsByCategory", BindingFlags.Instance | BindingFlags.NonPublic);
		private static readonly MethodInfo DynoUpdateToeCamber =
			typeof(UIDynostandContext).GetMethod("UpdateToeCamberTopAndBottomLists", BindingFlags.Instance | BindingFlags.NonPublic);

		/// <summary>CarX's tuning screen if it's open (in the garage or in an online room), else null.</summary>
		private static UIDynostandContext FindOpenDyno() {
			var dyno = UnityEngine.Object.FindAnyObjectByType<UIDynostandContext>();
			return dyno != null && dyno.isActive ? dyno : null;
		}

		private static GamePrefs Prefs => DependencyInjector.Resolve<GamePrefs>();
		private static BaseModel Db => DependencyInjector.Resolve<BaseModel>();

		/// <summary>The player's car: the one on show in the garage, or the one they're driving.</summary>
		private static RaceCar FindPlayerCar(out bool inGarage) {
			inGarage = false;
			BaseState main = States.mainCurrent;

			if (main is GarageGUIState) {
				inGarage = true;
				var view = UnityEngine.Object.FindAnyObjectByType<GarageCar3dViewContext>();
				return view != null ? view.currentCar : null;
			}

			if (main is BaseRaceModeState race && race.userCar != null && !race.userCar.isNetworkCar) {
				return race.userCar;
			}

			return null;
		}

		/// <summary>
		/// Null on the main garage screen. Elsewhere in the garage the car on show may be one the player
		/// is only browsing, or a CarX screen holds its own copy of the setup and would overwrite ours.
		/// </summary>
		private static string GarageScreenProblem() {
			if (States.instance != null && States.current is DynoMenuState) {
				return "Close CarX's tuning screen first.";
			}

			if (!(States.mainCurrent is BaseGameState state) || state.contextOwner == null) {
				return "Go to the main garage screen first.";
			}

			ContextOwner screens = state.contextOwner;
			if (IsOpen(screens.Get<UISetupsContext>()) || IsOpen(screens.Get<UIDynostandContext>())) {
				return "Close CarX's Setups or tuning screen first.";
			}
			if (IsOpen(screens.Get<UICarSelectorContext>())) {
				return "Close the car list first.";
			}
			if (!IsOpen(screens.Get<UIMainMenuContext>()) && !IsOpen(screens.Get<UIGarageContext>())) {
				return "Go to the main garage screen first.";
			}

			return null;
		}

		private static bool IsOpen(BaseContext screen) => screen != null && screen.isActive;

		private static float DefaultOf(CarProfileController controller) {
			CarProfileChanger changer = controller.changers[0];
			return changer.EvaluatePhase(changer.defaultValue);
		}

		/// <summary>Formats a slider position the way CarX's tuning screen does (DynoParamsItemViewModel).</summary>
		private static string Describe(CarProfileController controller, float phase) {
			CarProfileChanger changer = controller.changers[0];
			var parameter = changer.parameter;

			if (controller.name == DiffTypeKey) {
				int type = Mathf.RoundToInt(changer.EvaluateValue(phase));
				return DiffTypeEnum != null ? (Enum.GetName(DiffTypeEnum, type) ?? type.ToString(CultureInfo.InvariantCulture)) : type.ToString(CultureInfo.InvariantCulture);
			}

			if (parameter.id == DrivetrainParameterId) {
				return DrivetrainSwitchDynoParamViewModel.GetDrivetrainTitle(Mathf.RoundToInt(changer.EvaluateValue(phase)));
			}

			if (parameter.id == SequentialGearboxParameterId) {
				return phase > 0.5f ? "Sequential" : "Standard";
			}

			if (controller.isGroup || controller.isPercent) {
				return (phase * 100f).ToString("#,0", CultureInfo.InvariantCulture) + "%";
			}

			if (parameter.isBool) {
				return phase > 0.5f ? "Yes" : "No";
			}

			float value = changer.EvaluateValue(phase) * changer.multiplier;
			string text = value.ToString(string.IsNullOrEmpty(parameter.format) ? "N0" : parameter.format, CultureInfo.InvariantCulture);
			return parameter.units == null || parameter.units == "none" ? text : $"{text} {parameter.units}";
		}

		private static bool Failed(Exception e, out string error) {
			error = $"CarX connection problem: {e.GetType().Name}: {e.Message}";
			TrcLog.Error(e);
			return false;
		}

		private static string Or(string value) => string.IsNullOrEmpty(value) ? "?" : value;
	}
}
