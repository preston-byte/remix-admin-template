using System;
using System.Collections;
using System.Collections.Generic;
using GroundCrew.Aircraft;
using GroundCrew.Airport;
using GroundCrew.Crew;
using UnityEngine;

namespace GroundCrew.Operations
{
    /// <summary>
    /// Passengers: they come from the terminal (through the jet bridge) or the crew drop-off
    /// (up the stairs), board back rows first, walk down the aisle to their own seat from the
    /// aircraft's seat map and sit. A seated passenger is swapped for one pre-posed mesh (one
    /// renderer instead of ~16 jointed parts), so a full cabin stays cheap on Quest; they stand
    /// up again as walking people when they deplane.
    /// </summary>
    public partial class TurnaroundOps
    {
        [Serializable] class SeatFile { public SeatDef[] seats; }
        [Serializable] class SeatDef { public string seat; public float[] position; }

        public void BoardPassengers(StandInfo stand)
        {
            var o = For(stand);
            if (o == null || o.PaxBusy) return;
            StartCoroutine(Board(o));
        }

        public void DeplanePassengers(StandInfo stand)
        {
            var o = For(stand);
            if (o == null || o.PaxBusy || o.SeatedPax.Count == 0) return;
            StartCoroutine(Deplane(o));
        }

        void SetPax(StandOps o, string status)
        {
            o.PaxStatus = status;
            Changed?.Invoke();
        }

        IEnumerator Board(StandOps o)
        {
            o.PaxBusy = true;
            var rig = StandRegistry.AircraftAt(o.Stand);
            var sill = rig != null ? FindDeep(rig.transform, "SP_Sill_Door_1L") : null;
            if (rig == null || sill == null) { SetPax(o, "No aircraft on the stand"); o.PaxBusy = false; yield break; }
            if (config == null || config.passengerPrefab == null) { SetPax(o, "No passenger prefab - run Ground Crew > Operations > Set Up"); o.PaxBusy = false; yield break; }
            var free = FreeSeats(o, rig);
            if (free.Count == 0) { SetPax(o, "Aircraft full"); o.PaxBusy = false; yield break; }

            // Same access as the crew: the bridge, or stairs at door 1L.
            if (o.Stand.IsGate)
            {
                if (!o.Stand.bridge.IsDocked)
                {
                    SetPax(o, "Waiting for the jet bridge");
                    Run(o.Stand, OpsTask.Bridge);
                    yield return WaitFor(() => o.Stand.bridge.IsDocked, () => o.Get(OpsTask.Bridge) == TaskState.Failed, 200f);
                    if (!o.Stand.bridge.IsDocked) { SetPax(o, "Bridge couldn't dock"); o.PaxBusy = false; yield break; }
                }
            }
            else if (o.Get(OpsTask.Stairs) != TaskState.Done)
            {
                SetPax(o, "Waiting for the stairs");
                Run(o.Stand, OpsTask.Stairs);
                yield return WaitFor(() => o.Get(OpsTask.Stairs) == TaskState.Done, () => o.Get(OpsTask.Stairs) == TaskState.Failed, 400f);
                if (o.Get(OpsTask.Stairs) != TaskState.Done) { SetPax(o, "No stairs"); o.PaxBusy = false; yield break; }
            }
            rig.SetOpen01("Door_1L", 1f);

            // Back rows first.
            var count = Mathf.Min(config.passengersPerFlight, free.Count);
            Shuffle(free);
            var chosen = free.GetRange(0, count);
            chosen.Sort((a, b) => a.Value.z.CompareTo(b.Value.z));
            var services = AircraftServices.For(rig);
            var walking = 0;
            for (var i = 0; i < chosen.Count; i++)
            {
                var a = Spawn(config.passengerPrefab, o.Stand.crewEntry, o.Stand.yaw, "Passenger " + chosen[i].Key);
                if (a == null) continue;
                var look = CrewAppearance.Random();
                var appearance = a.GetComponent<CrewAppearance>();
                if (appearance != null) appearance.Apply(look);
                walking++;
                StartCoroutine(BoardOne(o, rig, sill, a, chosen[i].Key, chosen[i].Value, look, services, () => walking--));
                SetPax(o, $"Boarding: {services.Passengers} seated, {walking} on the way");
                yield return new WaitForSeconds(2.4f);
            }
            while (walking > 0)
            {
                SetPax(o, $"Boarding ({walking} still walking)");
                yield return new WaitForSeconds(0.5f);
            }
            SetPax(o, $"{services.Passengers} passengers aboard");
            o.PaxBusy = false;
        }

        IEnumerator BoardOne(StandOps o, AircraftRig rig, Transform sill, CrewAgent a, string seat, Vector3 seatLocal,
            CrewAppearance.Choice look, AircraftServices services, Action done)
        {
            yield return a.WalkPath(AccessPath(o, rig));
            if (a == null) { done(); yield break; }
            yield return a.WalkPath(SeatPath(rig, sill, seatLocal));
            // Swap for the cheap seated version, in the same seat.
            var t = rig.transform;
            var local = new Vector3(seatLocal.x, seatLocal.y - 0.5f, seatLocal.z);
            var rec = new SeatedPassenger { Seat = seat, LocalPosition = local, LocalYaw = 0f, Look = look };
            rec.Body = SeatedBody(rig, rec);
            Destroy(a.gameObject);
            o.SeatedPax.Add(rec);
            services.Passengers = o.SeatedPax.Count;
            done();
        }

        GameObject SeatedBody(AircraftRig rig, SeatedPassenger rec)
        {
            if (config == null || config.seatedPassengerPrefab == null) return null;
            var go = Instantiate(config.seatedPassengerPrefab, rig.transform);
            go.name = "Seated " + rec.Seat;
            go.transform.localPosition = rec.LocalPosition;
            go.transform.localRotation = Quaternion.Euler(0f, rec.LocalYaw, 0f);
            var look = rec.Look;
            look.Backpack = false;
            var appearance = go.GetComponent<CrewAppearance>();
            if (appearance == null) appearance = go.AddComponent<CrewAppearance>();
            appearance.Apply(look);
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        IEnumerator Deplane(StandOps o)
        {
            o.PaxBusy = true;
            var rig = StandRegistry.AircraftAt(o.Stand);
            var sill = rig != null ? FindDeep(rig.transform, "SP_Sill_Door_1L") : null;
            if (rig == null || sill == null) { SetPax(o, "Aircraft gone"); o.PaxBusy = false; yield break; }
            if (o.Stand.IsGate ? !o.Stand.bridge.IsDocked : o.Get(OpsTask.Stairs) != TaskState.Done)
            {
                SetPax(o, o.Stand.IsGate ? "Dock the bridge first" : "Bring the stairs first");
                o.PaxBusy = false;
                yield break;
            }
            rig.SetOpen01("Door_1L", 1f);
            var services = AircraftServices.For(rig);
            var doorZ = rig.transform.InverseTransformPoint(sill.position).z;
            var order = new List<SeatedPassenger>(o.SeatedPax);
            order.Sort((a, b) => Mathf.Abs(a.LocalPosition.z - doorZ).CompareTo(Mathf.Abs(b.LocalPosition.z - doorZ)));   // front rows first
            var walking = 0;
            foreach (var rec in order)
            {
                if (config == null || config.passengerPrefab == null) break;
                if (rec.Body != null) Destroy(rec.Body);
                o.SeatedPax.Remove(rec);
                services.Passengers = o.SeatedPax.Count;
                var world = rig.transform.TransformPoint(rec.LocalPosition);
                var a = Spawn(config.passengerPrefab, world, rig.transform.eulerAngles.y, "Passenger " + rec.Seat);
                if (a == null) continue;
                var appearance = a.GetComponent<CrewAppearance>();
                if (appearance != null) appearance.Apply(rec.Look);
                a.SitAt(world, rig.transform.eulerAngles.y, rig.transform);
                walking++;
                StartCoroutine(LeaveSeat(o, rig, sill, a, rec, () => walking--));
                SetPax(o, $"Deplaning ({o.SeatedPax.Count} still seated)");
                yield return new WaitForSeconds(1.8f);
            }
            while (walking > 0)
            {
                SetPax(o, $"Deplaning ({walking} walking off)");
                yield return new WaitForSeconds(0.5f);
            }
            SetPax(o, "All passengers off");
            o.PaxBusy = false;
        }

        IEnumerator LeaveSeat(StandOps o, AircraftRig rig, Transform sill, CrewAgent a, SeatedPassenger rec, Action done)
        {
            yield return new WaitForSeconds(0.8f);
            a.StandUp(rig.transform);
            var seatLocal = new Vector3(rec.LocalPosition.x, rec.LocalPosition.y + 0.5f, rec.LocalPosition.z);
            var path = SeatPath(rig, sill, seatLocal);
            path.Reverse();
            yield return a.WalkPath(path);
            a.transform.SetParent(null, true);
            var access = AccessPath(o, rig);
            access.Reverse();
            yield return a.WalkPath(access);
            Destroy(a.gameObject);
            done();
        }

        /// <summary>From just outside door 1L down the aisle into the row, ready to sit.</summary>
        static List<Vector3> SeatPath(AircraftRig rig, Transform sill, Vector3 seat)
        {
            var t = rig.transform;
            var s = t.InverseTransformPoint(sill.position);
            var floorY = s.y + 0.02f;
            Func<float, float, Vector3> L = (x, z) => t.TransformPoint(new Vector3(x, floorY, z));
            return new List<Vector3>
            {
                L(s.x * 0.55f, s.z), L(0f, s.z), L(0f, seat.z + 0.42f), L(seat.x * 0.85f, seat.z + 0.42f),
            };
        }

        /// <summary>Seats from the aircraft's seat map (Unity local frame) that nobody is sitting in.</summary>
        static List<KeyValuePair<string, Vector3>> FreeSeats(StandOps o, AircraftRig rig)
        {
            var list = new List<KeyValuePair<string, Vector3>>();
            if (rig.Definition == null) return list;
            SeatFile file = null;
            try { file = JsonUtility.FromJson<SeatFile>(rig.Definition.text); } catch (Exception) { }
            if (file == null || file.seats == null) return list;
            var taken = new HashSet<string>();
            foreach (var p in o.SeatedPax) taken.Add(p.Seat);
            foreach (var s in file.seats)
                if (s.position != null && s.position.Length >= 3 && !taken.Contains(s.seat))
                    list.Add(new KeyValuePair<string, Vector3>(s.seat, new Vector3(-s.position[0], s.position[1], s.position[2])));
            return list;
        }

        static void Shuffle<T>(IList<T> list)
        {
            for (var i = list.Count - 1; i > 0; i--)
            {
                var j = UnityEngine.Random.Range(0, i + 1);
                var tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }
    }
}
