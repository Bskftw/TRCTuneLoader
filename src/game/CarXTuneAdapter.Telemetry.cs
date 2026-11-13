// Reading your own car's inputs, gear and RPM, g-force and wheel slip from CarX's physics each frame, for the telemetry overlay.
using System;
using UnityEngine;

namespace TRC.TuneLoader {
	public sealed partial class CarXTuneAdapter : ICarTuneAdapter {
		private bool telemetryFailureLogged_;

		/// <summary>
		/// Your own car only (never a room player's), read from <c>CarX.Car</c>: the pedals and steering CarX's physics is
		/// using, gear, RPM and rev limiter, its forward and sideways acceleration, and each wheel's slip against the slip
		/// its tyre grips best at. Read-only.
		/// </summary>
		public bool TryGetTelemetry(TelemetryFrame frame) {
			try {
				if (!(States.mainCurrent is BaseRaceModeState race) || race.userCar == null || race.userCar.isNetworkCar || race.userCar.carX == null) {
					return false;
				}
				CarX.Car car = race.userCar.carX;
				frame.Throttle = Mathf.Clamp01(car.accelerate);
				frame.Brake = Mathf.Clamp01(car.brake);
				frame.Handbrake = Mathf.Clamp01(car.handbrake);
				frame.Clutch = Mathf.Clamp01(car.clutch);
				frame.Steer = Mathf.Clamp(car.steer, -1f, 1f);
				frame.Gear = car.gear;
				frame.Rpm = car.rpm;
				frame.RevLimit = car.engineRevLimiter;
				frame.Kmh = Mathf.Abs(car.speedKMH);
				frame.LongG = car.longAccel / 9.81f;
				frame.SideG = car.sideAccel / 9.81f;
				for (int i = 0; i < 4; i++) {
					CarX.Wheel wheel = car.GetWheel((CarX.WheelIndex)i);
					if (!wheel.isValid) {
						frame.Slip[i] = 0f;
						frame.Contact[i] = false;
						continue;
					}
					frame.Slip[i] = TelemetryFrame.PastGrip(wheel.sa, wheel.bestSlipAngle, wheel.sr, wheel.bestSlipRatio);
					frame.Contact[i] = wheel.HasPatch();
				}
				return true;
			} catch (Exception e) {
				if (!telemetryFailureLogged_) {
					telemetryFailureLogged_ = true;
					TrcLog.Error(e);
				}
				return false;
			}
		}
	}
}
