// Whether the player's own car can pass through things (no-clip, from any mod): read-only, the local car only.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace TRC.TuneLoader {
	public sealed partial class CarXTuneAdapter : ICarTuneAdapter {
		private int noClipCar_;
		private readonly List<Collider> noClipColliders_ = new List<Collider>();
		private readonly RaycastHit[] noClipHits_ = new RaycastHit[8];

		/// <summary>What the car's physics look like right now, for <see cref="NoClipSign"/> and the log.</summary>
		private struct CarPhysics {
			public bool Read, Collides, Kinematic, Gravity, LayerIgnored;
			public int CollidersOn, Colliders, Layer, RoadLayer;
			public string Road;
		}

		private CarPhysics ReadCarPhysics() {
			var p = new CarPhysics { RoadLayer = -1, Layer = -1, Road = "" };
			if (!(States.mainCurrent is BaseRaceModeState race) || race.userCar == null || race.userCar.isNetworkCar || race.userCar.carX == null) {
				return p;
			}
			RaceCar car = race.userCar;
			Rigidbody body = car.carX.getRigidbody;
			if (body == null) {
				return p;
			}
			p.Read = true;
			p.Collides = body.detectCollisions;
			p.Kinematic = body.isKinematic;
			p.Gravity = body.useGravity;
			int carId = car.GetInstanceID();
			if (carId != noClipCar_ || noClipColliders_.Count == 0) {
				noClipCar_ = carId;
				noClipColliders_.Clear();
				foreach (Collider c in car.GetComponentsInChildren<Collider>(true)) {
					if (!c.isTrigger) {
						noClipColliders_.Add(c);
					}
				}
			}
			Collider solid = null;
			foreach (Collider c in noClipColliders_) {
				if (c != null) {
					p.Colliders++;
					if (c.enabled && c.gameObject.activeInHierarchy && !c.isTrigger) {
						p.CollidersOn++;
						solid = solid ?? c;
					}
				}
			}
			if (solid != null) {
				p.Layer = solid.gameObject.layer;
				int count = Physics.RaycastNonAlloc(body.position + Vector3.up, Vector3.down, noClipHits_, 6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
				for (int i = 0; i < count; i++) {
					Collider ground = noClipHits_[i].collider;
					if (ground == null || ground.attachedRigidbody == body) {
						continue;
					}
					p.RoadLayer = ground.gameObject.layer;
					p.Road = ground.name;
					p.LayerIgnored = Physics.GetIgnoreLayerCollision(p.Layer, p.RoadLayer);
					break;
				}
			}
			return p;
		}

		/// <summary>
		/// What's wrong when the player's car can't hit anything (bskftw, Nov 18: no-clipping through a track fakes
		/// times), or "" when it collides as it should, or can't be read. Signs: its body has stopped colliding or is moved
		/// by hand (kinematic), its colliders are off, or its layer no longer meets the road's.
		/// shortcut: a no-clip that changes something else isn't seen; <see cref="CarPhysicsState"/> logs what changes, to add its sign.
		/// </summary>
		public string NoClipSign() {
			try {
				CarPhysics p = ReadCarPhysics();
				if (!p.Read) {
					return "";
				}
				if (!p.Collides) {
					return "the car's collisions are off";
				}
				if (p.Kinematic) {
					return "the car is moved without physics";
				}
				if (p.Colliders > 0 && p.CollidersOn == 0) {
					return "the car's colliders are off";
				}
				if (p.LayerIgnored) {
					return $"the car's layer ({p.Layer}) passes through the road's ({p.RoadLayer})";
				}
				return "";
			} catch (Exception e) {
				TrcLog.Once("no-clip check", $"The no-clip check isn't working ({e.Message}); said once.");
				return "";
			}
		}

		/// <summary>The car's physics in a line for the log (Nov 18: to learn what a no-clip mod changes), or "" when there's no car.</summary>
		public string CarPhysicsState() {
			try {
				CarPhysics p = ReadCarPhysics();
				return !p.Read ? "" : $"collisions {(p.Collides ? "on" : "OFF")}, {(p.Kinematic ? "KINEMATIC" : "physics")}, gravity {(p.Gravity ? "on" : "off")}, " +
					$"colliders {p.CollidersOn}/{p.Colliders} on, layer {p.Layer}{(p.LayerIgnored ? $", ground layer {p.RoadLayer} IGNORED" : "")}";
			} catch (Exception e) {
				return $"couldn't read ({e.Message})";
			}
		}
	}
}
