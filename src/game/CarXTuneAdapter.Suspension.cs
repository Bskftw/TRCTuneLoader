// Reading the four wheels' suspension each frame, and the setup per corner, for suspension logging.
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
		private bool suspensionFailureLogged_;

		/// <summary>
		/// The four wheels as CarX's own D-BOX motion-seat support reads them (CarXDboxManager): each wheel's height under
		/// its top mount against the spring's full length. Fully extended is the full length below the mount, fully
		/// compressed is up at it.
		/// </summary>
		public bool TryGetSuspension(SuspensionFrame frame) {
			try {
				if (!(States.mainCurrent is BaseRaceModeState race) || race.userCar == null || race.userCar.isNetworkCar || race.userCar.carX == null) {
					return false;
				}
				CarX.Car car = race.userCar.carX;
				for (int i = 0; i < 4; i++) {
					var index = (CarX.WheelIndex)i;
					CarX.Wheel wheel = car.GetWheel(index);
					Transform t = car.GetWheelTransform(index);
					if (!wheel.isValid || t == null) {
						return false;
					}
					float full = Mathf.Max(0.01f, wheel.maxSpringLen);
					float below = wheel.topPos.y - t.localPosition.y;
					frame.Wheels[i] = new WheelFrame {
						Compression = Mathf.Clamp01(1f - below / full),
						Load = wheel.vertForce.magnitude,
						Contact = wheel.HasPatch(),
						SlipAngle = wheel.sa,
						SlipRatio = wheel.sr,
					};
				}
				frame.Position = car.transform.position;
				frame.Kmh = Mathf.Abs(car.speedKMH);
				return true;
			} catch (Exception e) {
				if (!suspensionFailureLogged_) {
					suspensionFailureLogged_ = true;
					TrcLog.Error(e);
				}
				return false;
			}
		}

		public bool TryGetSuspensionSetup(out CornerSetup[] corners) {
			corners = null;
			try {
				if (!(States.mainCurrent is BaseRaceModeState race) || race.userCar == null || race.userCar.isNetworkCar || race.userCar.carX == null) {
					return false;
				}
				CarX.Car car = race.userCar.carX;
				CarX.CarDesc.Weight weight = race.userCar.GetDesc().carXDesc.weight;
				float front = Mathf.Clamp01(weight.frontPercent / 100f);
				float right = Mathf.Clamp01(weight.rightPercent / 100f);
				corners = new CornerSetup[4];
				for (int i = 0; i < 4; i++) {
					CarX.Wheel wheel = car.GetWheel((CarX.WheelIndex)i);
					float axle = i < 2 ? front : 1f - front;
					float side = i % 2 == 1 ? right : 1f - right;
					corners[i] = new CornerSetup {
						Travel = wheel.maxSpringLen, Spring = wheel.springCoef,
						SlowBump = wheel.slowBump, FastBump = wheel.fastBump, SlowRebound = wheel.slowRebound, FastRebound = wheel.fastRebound,
						BumpSlowLimit = wheel.bumpSlowLimit, BumpFastLimit = wheel.bumpFastLimit,
						ReboundSlowLimit = wheel.reboundSlowLimit, ReboundFastLimit = wheel.reboundFastLimit,
						CornerMass = weight.mass * axle * side,
					};
				}
				return true;
			} catch (Exception e) {
				TrcLog.Error(e);
				return false;
			}
		}
	}
}
