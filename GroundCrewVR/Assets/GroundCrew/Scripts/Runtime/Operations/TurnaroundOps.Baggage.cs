using System;
using System.Collections;
using System.Collections.Generic;
using GroundCrew.Aircraft;
using GroundCrew.Airport;
using GroundCrew.Crew;
using GroundCrew.Vehicles;
using UnityEngine;

namespace GroundCrew.Operations
{
    /// <summary>
    /// AI baggage loading through the belt loader at the forward hold: a cart of bags is parked
    /// at the foot of the belt; one worker lifts bags onto the running belt, the other kneels in
    /// the hold and stows them in rows from the far end. Unloading runs the other way.
    /// Bags handled by the AI move kinematically (no tumbling off the belt); they're normal
    /// grabbable bags, so you can join in by hand.
    /// </summary>
    public partial class TurnaroundOps
    {
        const float k_BagThickness = 0.26f, k_BagLength = 0.66f;

        [Serializable] class HoldFile { public HoldDef[] holds; }
        [Serializable] class HoldDef { public string name; public float[] center, size; }

        IEnumerator LoadBags(StandOps o) => Baggage(o, true);
        IEnumerator UnloadBags(StandOps o) => Baggage(o, false);

        IEnumerator Baggage(StandOps o, bool loading)
        {
            const OpsTask task = OpsTask.Bags;
            var rig = StandRegistry.AircraftAt(o.Stand);
            if (rig == null) { Set(o, task, TaskState.Failed, "No aircraft on the stand"); yield break; }
            var services = AircraftServices.For(rig);
            if (!loading && services.BagCount == 0) { Set(o, task, TaskState.Idle, "Hold is empty"); yield break; }
            if (config == null || config.bagPrefabs == null || config.bagPrefabs.Length == 0)
            { Set(o, task, TaskState.Failed, "No bag prefabs - run Ground Crew > Operations > Set Up"); yield break; }

            // The belt loader has to be at the forward hold first.
            if (o.Get(OpsTask.BeltLoader) != TaskState.Done)
            {
                Set(o, task, TaskState.Working, "Waiting for the belt loader");
                Run(o.Stand, OpsTask.BeltLoader);
                yield return WaitFor(() => o.Get(OpsTask.BeltLoader) == TaskState.Done, () => o.Get(OpsTask.BeltLoader) == TaskState.Failed, 400f);
                if (o.Get(OpsTask.BeltLoader) != TaskState.Done) { Set(o, task, TaskState.Failed, "No belt loader"); yield break; }
            }
            var loader = o.BeltLoader;
            var belt = loader != null ? FindDeep(loader.transform, "Belt") : null;
            var conveyor = loader != null ? loader.GetComponentInChildren<ConveyorBelt>() : null;
            var sill = FindDeep(rig.transform, "SP_Sill_CargoDoor_Fwd");
            if (belt == null || conveyor == null || sill == null) { Set(o, task, TaskState.Failed, "Belt loader not usable"); yield break; }
            var slots = HoldSlots(rig, sill);
            if (slots.Count == 0) { Set(o, task, TaskState.Failed, "Aircraft hold not defined"); yield break; }

            // Cart at the foot of the belt (bags on it when loading).
            var back = -Vector3.ProjectOnPlane(belt.forward, Vector3.up).normalized;
            var across = Vector3.Cross(Vector3.up, back);
            var foot = belt.TransformPoint(new Vector3(0f, 0.2f, 0.45f));
            if (o.BaggageCart == null) o.BaggageCart = SpawnCart(o, Ground(foot) + back * 3.2f + across * 2.2f, Quaternion.LookRotation(-back, Vector3.up));
            var cartBody = o.BaggageCart != null ? (FindDeep(o.BaggageCart.transform, "Cart") ?? o.BaggageCart.transform) : null;
            if (cartBody == null) { Set(o, task, TaskState.Failed, "No baggage cart available"); yield break; }
            var cartSlots = CartSlots(cartBody);
            if (loading && o.CartBags.Count == 0 && services.BagCount == 0)
            {
                var n = Mathf.Min(config.bagsPerFlight, cartSlots.Count, slots.Count);
                for (var i = 0; i < n; i++)
                {
                    var bag = Instantiate(config.bagPrefabs[i % config.bagPrefabs.Length]);
                    bag.name = "Bag " + (i + 1);
                    MakeKinematic(bag, true);
                    PoseBag(bag.transform, cartSlots[i].position, cartSlots[i].rotation);
                    bag.transform.SetParent(cartBody, true);
                    o.CartBags.Add(bag);
                }
            }
            if (loading && o.CartBags.Count == 0) { Set(o, task, TaskState.Done, $"{services.BagCount} bags in the hold"); yield break; }

            Set(o, task, TaskState.Working, "Waiting for two loaders");
            CrewAgent a = null, b = null;
            yield return GetWorker(o, x => a = x);
            yield return GetWorker(o, x => b = x);
            if (a == null || b == null) { Release(a); Release(b); Set(o, task, TaskState.Failed, "Not enough workers"); yield break; }

            // B into the hold: up the belt, in through the door, kneel.
            var outward = Outward(rig, sill);
            var footSide = Ground(foot) + across * 0.9f;
            var end = belt.TransformPoint(new Vector3(0f, 0.2f, 7.35f));
            var inside = sill.position - outward * 0.75f + Vector3.up * 0.02f;
            b.Activity = "Into the hold";
            a.Activity = "At the belt";
            var bIn = new List<Vector3>(Route(rig, b.transform.position, footSide)) { foot, end, inside };
            var aWalk = StartCoroutine(a.WalkPath(Route(rig, a.transform.position, footSide)));
            yield return b.WalkPath(bIn);
            b.transform.SetParent(rig.transform, true);
            b.Animator.Pose = CrewPose.Crouch;
            yield return aWalk;

            loader.StartEngine();
            conveyor.Run(loading ? 1 : -1);
            var arrived = new Queue<GameObject>();
            var onBelt = new List<GameObject>();
            var done = 0;
            var total = loading ? o.CartBags.Count : services.BagCount;

            if (loading)
            {
                StartCoroutine(Stower(o, b, rig, slots, services, arrived, () => done < total, () => done++));
                while (o.CartBags.Count > 0)
                {
                    o.CartBags.RemoveAll(x => x == null);
                    if (o.CartBags.Count == 0) break;
                    var bag = o.CartBags[o.CartBags.Count - 1];
                    o.CartBags.RemoveAt(o.CartBags.Count - 1);
                    Set(o, task, TaskState.Working, $"Loading bags {done}/{total}");
                    yield return a.WalkPath(new[] { Ground(bag.transform.position) + Vector3.ProjectOnPlane(bag.transform.position - cartBody.position, Vector3.up).normalized * 0.9f });
                    yield return a.Face(bag.transform.position);
                    yield return a.Crouch(0.4f);
                    a.Hold(bag, true);
                    MakeKinematic(bag, true);
                    yield return a.WalkPath(new[] { footSide });
                    yield return a.Face(foot);
                    // Wait for room at the foot of the belt.
                    while (onBelt.Count > 0 && onBelt[onBelt.Count - 1] != null && Vector3.Distance(onBelt[onBelt.Count - 1].transform.position, foot) < 1.4f)
                        yield return null;
                    var item = a.PutDown(foot, belt.rotation);
                    if (item == null) continue;
                    MakeKinematic(item, true);
                    onBelt.Add(item);
                    StartCoroutine(Ride(item, belt, conveyor, 0.45f, 7.35f, () => { onBelt.Remove(item); arrived.Enqueue(item); }));
                }
                while (done < total)
                {
                    Set(o, task, TaskState.Working, $"Loading bags {done}/{total}");
                    yield return new WaitForSeconds(0.5f);
                }
            }
            else
            {
                // B hands bags out of the hold onto the belt; A takes them off the bottom onto the cart.
                var free = new List<int>();
                for (var i = 0; i < cartSlots.Count; i++) free.Add(i);
                StartCoroutine(Unstower(b, belt, conveyor, services, end, arrived));
                while (done < total)
                {
                    Set(o, task, TaskState.Working, $"Unloading bags {done}/{total}");
                    if (arrived.Count == 0) { yield return null; continue; }
                    var bag = arrived.Dequeue();
                    if (bag == null) { done++; continue; }
                    yield return a.Face(bag.transform.position);
                    a.Hold(bag, true);
                    yield return a.Crouch(0.3f);
                    var slot = free.Count > 0 ? free[0] : 0;
                    if (free.Count > 0) free.RemoveAt(0);
                    var place = cartSlots[slot];
                    yield return a.WalkPath(new[] { Ground(place.position) + Vector3.ProjectOnPlane(place.position - cartBody.position, Vector3.up).normalized * 0.9f });
                    var item = a.PutDown(place.position, place.rotation);
                    if (item != null)
                    {
                        MakeKinematic(item, true);
                        PoseBag(item.transform, place.position, place.rotation);
                        item.transform.SetParent(cartBody, true);
                        o.CartBags.Add(item);
                    }
                    done++;
                    yield return a.WalkPath(new[] { footSide });
                }
            }

            conveyor.Run(0);
            loader.StopEngine();
            loader.SetParkingBrake(true);
            // B climbs out the way he came.
            b.StandUp();
            b.Animator.Pose = CrewPose.Idle;
            yield return b.WalkPath(new[] { end, foot, footSide });
            Set(o, task, loading ? TaskState.Done : TaskState.Idle, loading ? $"{services.BagCount} bags loaded" : $"Unloaded - {o.CartBags.Count} bags on the cart");
            StartCoroutine(GoHome(o, a, rig));
            yield return GoHome(o, b, rig);
        }

        /// <summary>The hold worker takes each bag that reaches the top and slides it into the next slot.</summary>
        IEnumerator Stower(StandOps o, CrewAgent b, AircraftRig rig, List<Pose> slots, AircraftServices services, Queue<GameObject> arrived,
            Func<bool> more, Action stowed)
        {
            while (more())
            {
                if (arrived.Count == 0) { yield return null; continue; }
                var bag = arrived.Dequeue();
                if (bag == null) { stowed(); continue; }
                var slot = slots[Mathf.Min(services.BagCount, slots.Count - 1)];
                b.Animator.Pose = CrewPose.Crouch;
                var from = bag.transform.position;
                var fromRot = bag.transform.rotation;
                var target = BagPose(slot.position, slot.rotation);
                for (var t = 0f; t < 1f; t += Time.deltaTime / 0.8f)
                {
                    bag.transform.SetPositionAndRotation(Vector3.Lerp(from, target.position, t), Quaternion.Slerp(fromRot, target.rotation, t));
                    yield return null;
                }
                bag.transform.SetPositionAndRotation(target.position, target.rotation);
                bag.transform.SetParent(rig.transform, true);
                services.HoldBags.Add(bag);
                stowed();
            }
        }

        /// <summary>Unloading: the hold worker puts bags back on the top of the belt, newest first.</summary>
        IEnumerator Unstower(CrewAgent b, Transform belt, ConveyorBelt conveyor, AircraftServices services, Vector3 end, Queue<GameObject> arrived)
        {
            var riding = 0;
            while (services.BagCount > 0)
            {
                var bag = services.HoldBags[services.HoldBags.Count - 1];
                services.HoldBags.RemoveAt(services.HoldBags.Count - 1);
                if (bag == null) continue;
                var from = bag.transform.position;
                var fromRot = bag.transform.rotation;
                var target = BagPose(end, belt.rotation);
                for (var t = 0f; t < 1f; t += Time.deltaTime / 0.8f)
                {
                    bag.transform.SetPositionAndRotation(Vector3.Lerp(from, target.position, t), Quaternion.Slerp(fromRot, target.rotation, t));
                    yield return null;
                }
                bag.transform.SetParent(null, true);
                riding++;
                StartCoroutine(Ride(bag, belt, conveyor, 7.35f, 0.45f, () => { riding--; arrived.Enqueue(bag); }));
                yield return new WaitForSeconds(2.2f);
            }
        }

        /// <summary>A bag carried along the belt surface at belt speed (only while the belt runs that way).</summary>
        static IEnumerator Ride(GameObject bag, Transform belt, ConveyorBelt conveyor, float fromZ, float toZ, Action arrived)
        {
            var dir = Mathf.Sign(toZ - fromZ);
            var z = fromZ;
            while ((toZ - z) * dir > 0f && bag != null)
            {
                if (conveyor.Direction == (int)dir) z += dir * 0.55f * Time.deltaTime;
                var surface = belt.TransformPoint(new Vector3(0f, 0.2f, z));
                PoseBag(bag.transform, surface, belt.rotation);
                yield return null;
            }
            arrived();
        }

        /// <summary>Stowage positions in the forward hold, aft end first, then the forward end; 2 layers, 3 across.</summary>
        static List<Pose> HoldSlots(AircraftRig rig, Transform sill)
        {
            var list = new List<Pose>();
            if (rig.Definition == null) return list;
            HoldFile file = null;
            try { file = JsonUtility.FromJson<HoldFile>(rig.Definition.text); } catch (Exception) { }
            if (file == null || file.holds == null) return list;
            HoldDef hold = null;
            foreach (var h in file.holds) if (h.name == "Hold_Fwd") hold = h;
            if (hold == null || hold.center == null || hold.size == null) return list;
            var t = rig.transform;
            var c = new Vector3(-hold.center[0], hold.center[1], hold.center[2]);      // glTF x is the aircraft's left
            var floor = c.y - hold.size[1] / 2f;
            var door = t.InverseTransformPoint(sill.position).z;
            var rot = t.rotation * Quaternion.Euler(0f, 90f, 0f);                    // bags lie across the hold
            var zs = new List<float>();
            for (var z = c.z - hold.size[2] / 2f + 0.3f; z < door - 0.8f; z += 0.46f) zs.Add(z);
            for (var z = c.z + hold.size[2] / 2f - 0.3f; z > door + 0.8f; z -= 0.46f) zs.Add(z);
            foreach (var z in zs)
                for (var layer = 0; layer < 2; layer++)
                    foreach (var x in new[] { 0f, -0.68f, 0.68f })
                        list.Add(new Pose(t.TransformPoint(new Vector3(x, floor + layer * (k_BagThickness + 0.01f), z)), rot));
            return list;
        }

        /// <summary>Bag positions on a baggage cart's deck: 2 across, 5 along, 2 layers.</summary>
        static List<Pose> CartSlots(Transform cart)
        {
            var list = new List<Pose>();
            var rot = cart.rotation * Quaternion.Euler(0f, 90f, 0f);
            for (var layer = 0; layer < 2; layer++)
                for (var row = 0; row < 5; row++)
                    foreach (var x in new[] { -0.36f, 0.36f })
                        list.Add(new Pose(cart.TransformPoint(new Vector3(x, 0.57f + layer * (k_BagThickness + 0.01f), -0.92f + row * 0.46f)), rot));
            return list;
        }

        /// <summary>Lay a (standing-modelled) suitcase flat, centred on a surface point.</summary>
        static void PoseBag(Transform bag, Vector3 surfaceCentre, Quaternion surface)
        {
            var p = BagPose(surfaceCentre, surface);
            bag.SetPositionAndRotation(p.position, p.rotation);
        }

        static Pose BagPose(Vector3 surfaceCentre, Quaternion surface)
        {
            var rot = surface * Quaternion.Euler(90f, 0f, 0f);
            var pos = surfaceCentre + surface * new Vector3(0f, k_BagThickness / 2f, 0f) - surface * new Vector3(0f, 0f, k_BagLength / 2f);
            return new Pose(pos, rot);
        }

        static void MakeKinematic(GameObject bag, bool kinematic)
        {
            var rb = bag.GetComponent<Rigidbody>();
            if (rb == null) return;
            rb.isKinematic = kinematic;
            rb.interpolation = kinematic ? RigidbodyInterpolation.None : RigidbodyInterpolation.Interpolate;
        }

        GameObject SpawnCart(StandOps o, Vector3 position, Quaternion rotation)
        {
            if (config == null || config.vehicles == null) return null;
            foreach (var e in config.vehicles.Entries)
            {
                if (e.prefab == null || !e.displayName.ToLowerInvariant().Contains("cart")) continue;
                var go = Instantiate(e.prefab, position, rotation);
                go.name = "Baggage cart (" + o.Stand.name + ")";
                foreach (var v in go.GetComponentsInChildren<VehicleController>()) v.SetParkingBrake(true);
                return go;
            }
            return null;
        }
    }
}
