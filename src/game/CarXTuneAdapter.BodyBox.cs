// A car's outline (left, right, front, back) from its own meshes, for gaps measured door to door instead of middle to middle.
using System.Collections.Generic;
using UnityEngine;

namespace TRC.TuneLoader {
	public sealed partial class CarXTuneAdapter : ICarTuneAdapter {
		/// <summary>Each car's outline, by the car object and its car id (a swap in a room makes a new entry).</summary>
		private readonly Dictionary<long, Bounds> bodyBoxes_ = new Dictionary<long, Bounds>();
		private readonly Dictionary<long, float> bodyBoxTried_ = new Dictionary<long, float>();

		/// <summary>
		/// The box round the car's body meshes in its own space (x across, z along), or an empty box when it can't be read yet
		/// (a room car's body can load late: tried again once a second). The same meshes the ghost copies (no shadow planes).
		/// shortcut: worked out once per car; a body kit changed mid-room keeps the old outline until the car is swapped.
		/// </summary>
		private Bounds BodyBox(RaceCar car, Transform body, int carId) {
			long key = ((long)car.GetInstanceID() << 20) ^ carId;
			if (bodyBoxes_.TryGetValue(key, out Bounds known)) {
				return known;
			}
			if (bodyBoxTried_.TryGetValue(key, out float tried) && Time.unscaledTime - tried < 1f) {
				return default;
			}
			bodyBoxTried_[key] = Time.unscaledTime;
			bool any = false;
			var box = new Bounds();
			foreach (MeshFilter filter in car.GetComponentsInChildren<MeshFilter>(false)) {
				MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
				if (filter.sharedMesh == null || renderer == null || !renderer.enabled || NotCarBody(filter, renderer, body)) {
					continue;
				}
				Bounds mesh = filter.sharedMesh.bounds;
				for (int corner = 0; corner < 8; corner++) {
					var local = new Vector3((corner & 1) == 0 ? mesh.min.x : mesh.max.x, (corner & 2) == 0 ? mesh.min.y : mesh.max.y, (corner & 4) == 0 ? mesh.min.z : mesh.max.z);
					Vector3 point = body.InverseTransformPoint(filter.transform.TransformPoint(local));
					if (any) {
						box.Encapsulate(point);
					} else {
						box = new Bounds(point, Vector3.zero);
						any = true;
					}
				}
			}
			// Anything outside a car's size (a light flare, an effect left on) means it isn't the body: not kept, tried again.
			if (!any || box.extents.x < 0.5f || box.extents.x > 1.6f || box.extents.z < 1.4f || box.extents.z > 3.5f) {
				return default;
			}
			bodyBoxes_[key] = box;
			bodyBoxTried_.Remove(key);
			TrcLog.Info($"Car {carId} outline: {box.size.x:0.00} m wide, {box.size.z:0.00} m long.");
			return box;
		}
	}
}
