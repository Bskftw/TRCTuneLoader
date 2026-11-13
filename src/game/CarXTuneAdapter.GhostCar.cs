// A ghost's see-through car body: a copy of the car's meshes with no collider or physics.
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
		public GameObject BuildGhostBody(Material material) {
			try {
				if (!(States.mainCurrent is BaseRaceModeState race) || race.userCar == null || race.userCar.isNetworkCar || material == null) {
					return null;
				}
				RaceCar car = race.userCar;
				Transform body = car.carX != null ? car.carX.transform : car.transform;
				var root = new GameObject("TRC ghost") { hideFlags = HideFlags.DontSave };
				int count = 0;
				var skipped = new List<string>();
				foreach (MeshFilter filter in car.GetComponentsInChildren<MeshFilter>(false)) {
					MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
					if (filter.sharedMesh == null || renderer == null || !renderer.enabled || count >= 300) {
						continue;
					}
					if (NotCarBody(filter, renderer, body)) {
						// A fake shadow or decal plane under the car (bskftw, testing: a square on the ground round the ghost).
						skipped.Add(filter.name);
						continue;
					}
					var part = new GameObject("part") { hideFlags = HideFlags.DontSave };
					part.transform.SetParent(root.transform, false);
					part.transform.localPosition = body.InverseTransformPoint(filter.transform.position);
					part.transform.localRotation = Quaternion.Inverse(body.rotation) * filter.transform.rotation;
					part.transform.localScale = filter.transform.lossyScale;
					part.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
					MeshRenderer copy = part.AddComponent<MeshRenderer>();
					var materials = new Material[Math.Max(1, filter.sharedMesh.subMeshCount)];
					for (int i = 0; i < materials.Length; i++) {
						materials[i] = material;
					}
					copy.sharedMaterials = materials;
					copy.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
					copy.receiveShadows = false;
					count++;
				}
				if (count == 0) {
					UnityEngine.Object.Destroy(root);
					return null;
				}
				if (skipped.Count > 0) {
					TrcLog.Info($"Ghost: left out {skipped.Count} flat or shadow part(s) of the car: {string.Join(", ", skipped.Take(8))}.");
				}
				return root;
			} catch (Exception e) {
				TrcLog.Error(e);
				return null;
			}
		}

		/// <summary>
		/// Not part of the car's body: a flat plane (no thicker than 3 cm, over a metre across, like the fake shadow under
		/// a car), or something named or drawn like a shadow, decal or projector.
		/// </summary>
		private static bool NotCarBody(MeshFilter filter, MeshRenderer renderer, Transform body) {
			string name = (filter.name + " " + filter.sharedMesh.name).ToLowerInvariant();
			string[] words = { "shadow", "decal", "projector", "blob", "ground", "occlusion" };
			if (words.Any(w => name.Contains(w))) {
				return true;
			}
			foreach (Material material in renderer.sharedMaterials) {
				string shader = material != null && material.shader != null ? material.shader.name.ToLowerInvariant() : "";
				if (words.Any(w => shader.Contains(w)) || (material != null && material.name.ToLowerInvariant().Contains("shadow"))) {
					return true;
				}
			}
			Bounds bounds = filter.sharedMesh.bounds;
			Vector3 size = Vector3.Scale(bounds.size, filter.transform.lossyScale);
			// Measured along the car's up: thin and wide is a plane lying under it.
			Vector3 up = body.InverseTransformDirection(filter.transform.up);
			float thickness = Mathf.Abs(up.y) > 0.9f ? Mathf.Abs(size.y) : Mathf.Min(Mathf.Abs(size.x), Mathf.Min(Mathf.Abs(size.y), Mathf.Abs(size.z)));
			float across = Mathf.Max(Mathf.Abs(size.x), Mathf.Abs(size.z));
			return thickness < 0.03f && across > 1f;
		}
	}
}
