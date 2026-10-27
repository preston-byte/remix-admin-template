using System;
using System.Collections;
using System.Collections.Generic;
using GroundCrew.Aircraft;
using GroundCrew.Airport;
using GroundCrew.Core;
using GroundCrew.Crew;
using GroundCrew.Vehicles;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Object = UnityEngine.Object;

namespace GroundCrew.Operations
{
    public enum OpsTask { Chocks, Cones, Stairs, BeltLoader, Bridge }
    public enum TaskState { Idle, Working, Done, Failed }

    /// <summary>Everything going on at one stand.</summary>
    public class StandOps
    {
        public StandInfo Stand;
        public readonly Dictionary<OpsTask, TaskState> State = new Dictionary<OpsTask, TaskState>();
        public readonly Dictionary<OpsTask, string> Note = new Dictionary<OpsTask, string>();
        public readonly List<GameObject> Chocks = new List<GameObject>();
        public readonly List<GameObject> Cones = new List<GameObject>();
        public readonly List<CrewAgent> Workers = new List<CrewAgent>();
        public readonly List<CrewAgent> FlightCrew = new List<CrewAgent>();
        public VehicleController Stairs, BeltLoader;
        public string CrewStatus = "Not called";
        public bool CrewBusy;

        public TaskState Get(OpsTask t) => State.TryGetValue(t, out var s) ? s : TaskState.Idle;
        public string NoteFor(OpsTask t) => Note.TryGetValue(t, out var n) ? n : "";
    }

    /// <summary>
    /// The turnaround's helpers: AI ground crew who chock the wheels, put the cones out, bring
    /// the passenger stairs and the belt loader to the doors (or dock the jet bridge), open the
    /// doors, and clear it all away again; and the flight crew (two pilots, four cabin crew) who
    /// walk out to the aircraft, board by the bridge or the stairs, and take their places.
    /// The Ops iPad drives this; every job is a coroutine built from <see cref="CrewAgent"/> moves
    /// and <see cref="AutoDriver"/> drives, and reports its state for the iPad to show.
    /// </summary>
    public class TurnaroundOps : MonoBehaviour
    {
        [SerializeField] OpsConfig config;

        static readonly string[] k_WorkerNames = { "Sam", "Priya", "Tomasz", "Mei", "Jonas", "Amara", "Luis", "Keira", "Ravi", "Noor" };
        static readonly string[] k_CrewNames = { "Capt. Reyes", "F/O Okafor", "Purser Lindqvist", "Hana", "Marco", "Aisha" };
        static readonly string[] k_ChockPoints =
        {
            "SP_Chock_NoseGear_Fwd", "SP_Chock_NoseGear_Aft", "SP_Chock_MainGear_L_Out_Fwd", "SP_Chock_MainGear_L_Out_Aft",
            "SP_Chock_MainGear_R_Out_Fwd", "SP_Chock_MainGear_R_Out_Aft",
        };
        static readonly string[] k_ConePoints = { "SP_Cone_Nose", "SP_Cone_Engine_L", "SP_Cone_WingTip_L", "SP_Cone_Tail", "SP_Cone_WingTip_R", "SP_Cone_Engine_R" };

        readonly Dictionary<StandInfo, StandOps> m_Ops = new Dictionary<StandInfo, StandOps>();
        readonly HashSet<VehicleController> m_Reserved = new HashSet<VehicleController>();
        int m_NameIndex;

        public static TurnaroundOps Instance { get; private set; }
        public OpsConfig Config => config;
        public event Action Changed;

        void OnEnable() => Instance = this;
        void OnDisable() { if (Instance == this) Instance = null; }

        public StandOps For(StandInfo stand)
        {
            if (stand == null) return null;
            if (!m_Ops.TryGetValue(stand, out var o)) m_Ops[stand] = o = new StandOps { Stand = stand };
            return o;
        }

        void Set(StandOps o, OpsTask t, TaskState s, string note)
        {
            o.State[t] = s;
            o.Note[t] = note;
            Changed?.Invoke();
            EventBus.Publish(new OpsEvent(o.Stand.name, t.ToString(), s, note));
        }

        void SetCrew(StandOps o, string status)
        {
            o.CrewStatus = status;
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ commands (iPad)

        public void Run(StandInfo stand, OpsTask task)
        {
            var o = For(stand);
            if (o == null || o.Get(task) == TaskState.Working) return;
            switch (task)
            {
                case OpsTask.Chocks: StartCoroutine(PlaceItems(o, task, config != null ? config.chockPrefab : null, k_ChockPoints, o.Chocks, true)); break;
                case OpsTask.Cones: StartCoroutine(PlaceItems(o, task, config != null ? config.conePrefab : null, k_ConePoints, o.Cones, false)); break;
                case OpsTask.Stairs: StartCoroutine(PositionEquipment(o, task, true)); break;
                case OpsTask.BeltLoader: StartCoroutine(PositionEquipment(o, task, false)); break;
                case OpsTask.Bridge: StartCoroutine(DockBridge(o)); break;
            }
        }

        public void Undo(StandInfo stand, OpsTask task)
        {
            var o = For(stand);
            if (o == null || o.Get(task) == TaskState.Working) return;
            switch (task)
            {
                case OpsTask.Chocks: StartCoroutine(CollectItems(o, task, o.Chocks)); break;
                case OpsTask.Cones: StartCoroutine(CollectItems(o, task, o.Cones)); break;
                case OpsTask.Stairs: StartCoroutine(RemoveEquipment(o, task)); break;
                case OpsTask.BeltLoader: StartCoroutine(RemoveEquipment(o, task)); break;
                case OpsTask.Bridge:
                    if (o.Stand.bridge != null) o.Stand.bridge.Retract();
                    Set(o, task, TaskState.Idle, "Retracting");
                    break;
            }
        }

        public void SetupAll(StandInfo stand)
        {
            Run(stand, OpsTask.Chocks);
            Run(stand, OpsTask.Cones);
            Run(stand, stand.IsGate ? OpsTask.Bridge : OpsTask.Stairs);
            Run(stand, OpsTask.BeltLoader);
        }

        public void ClearAll(StandInfo stand)
        {
            foreach (OpsTask t in Enum.GetValues(typeof(OpsTask)))
                if (For(stand).Get(t) == TaskState.Done) Undo(stand, t);
        }

        public void CallCrew(StandInfo stand)
        {
            var o = For(stand);
            if (o == null || o.CrewBusy || o.FlightCrew.Count > 0) return;
            StartCoroutine(BoardCrew(o));
        }

        public void CrewOff(StandInfo stand)
        {
            var o = For(stand);
            if (o == null || o.CrewBusy || o.FlightCrew.Count == 0) return;
            StartCoroutine(DeplaneCrew(o));
        }

        // ------------------------------------------------------------------ workers

        CrewAgent Spawn(GameObject prefab, Vector3 position, float yaw, string name)
        {
            if (prefab == null) return null;
            var go = Instantiate(prefab, position, Quaternion.Euler(0f, yaw, 0f));
            go.name = name;
            var agent = go.GetComponent<CrewAgent>();
            if (agent == null) agent = go.AddComponent<CrewAgent>();
            agent.DisplayName = name;
            return agent;
        }

        IEnumerator GetWorker(StandOps o, Action<CrewAgent> got)
        {
            var waited = 0f;
            while (true)
            {
                o.Workers.RemoveAll(w => w == null);
                foreach (var w in o.Workers)
                    if (!w.Busy) { w.Busy = true; got(w); yield break; }
                var max = config != null ? config.maxWorkersPerStand : 3;
                if (o.Workers.Count < max && config != null && config.groundCrewPrefab != null)
                {
                    var name = k_WorkerNames[m_NameIndex++ % k_WorkerNames.Length];
                    var w = Spawn(config.groundCrewPrefab, o.Stand.staging + new Vector3(o.Workers.Count * 0.9f, 0f, 0f), 0f, name);
                    if (w != null)
                    {
                        w.Role = CrewRole.GroundCrew;
                        w.Busy = true;
                        o.Workers.Add(w);
                        Changed?.Invoke();
                        got(w);
                        yield break;
                    }
                }
                waited += 0.5f;
                if (waited > 600f) { got(null); yield break; }
                yield return new WaitForSeconds(0.5f);
            }
        }

        void Release(CrewAgent w, string activity = "Standing by")
        {
            if (w == null) return;
            w.Busy = false;
            w.Activity = activity;
            Changed?.Invoke();
        }

        IEnumerator GoHome(StandOps o, CrewAgent w, AircraftRig rig)
        {
            if (w == null) yield break;
            w.Activity = "Walking back";
            yield return w.WalkPath(Route(rig, w.transform.position, o.Stand.staging + new Vector3(UnityEngine.Random.Range(-2f, 2f), 0f, UnityEngine.Random.Range(-1f, 1f))));
            Release(w);
        }

        // ------------------------------------------------------------------ chocks / cones

        IEnumerator PlaceItems(StandOps o, OpsTask task, GameObject prefab, string[] pointNames, List<GameObject> placed, bool chocks)
        {
            var rig = StandRegistry.AircraftAt(o.Stand);
            if (rig == null) { Set(o, task, TaskState.Failed, "No aircraft on the stand"); yield break; }
            if (prefab == null) { Set(o, task, TaskState.Failed, "No prefab - run Ground Crew > Operations > Set Up"); yield break; }
            var points = new List<Transform>();
            foreach (var n in pointNames) { var t = FindDeep(rig.transform, n); if (t != null) points.Add(t); }
            if (points.Count == 0) { Set(o, task, TaskState.Failed, "Aircraft has no positions for them"); yield break; }
            placed.RemoveAll(i => i == null);
            if (placed.Count > 0) { Set(o, task, TaskState.Done, "Already out"); yield break; }

            Set(o, task, TaskState.Working, "Waiting for a worker");
            CrewAgent w = null;
            yield return GetWorker(o, a => w = a);
            if (w == null) { Set(o, task, TaskState.Failed, "Nobody free"); yield break; }
            var what = chocks ? "chocks" : "cones";

            for (var i = 0; i < points.Count; i += 2)
            {
                if (w == null) { Set(o, task, TaskState.Failed, "Worker gone"); yield break; }
                w.Activity = "Fetching " + what;
                Set(o, task, TaskState.Working, $"{w.DisplayName} fetching {what} ({i}/{points.Count})");
                yield return w.WalkPath(Route(rig, w.transform.position, o.Stand.staging));
                yield return w.Crouch(0.7f);
                for (var k = i; k < Mathf.Min(i + 2, points.Count); k++)
                {
                    var item = Instantiate(prefab);
                    item.name = (chocks ? "Chock " : "Cone ") + points[k].name.Replace("SP_Chock_", "").Replace("SP_Cone_", "");
                    w.Hold(item, k == i);
                }
                for (var k = Mathf.Min(i + 2, points.Count) - 1; k >= i; k--)
                {
                    var point = points[k];
                    var place = point.position;
                    var outward = Vector3.ProjectOnPlane(place - rig.transform.position, Vector3.up);
                    outward -= rig.transform.forward * Vector3.Dot(outward, rig.transform.forward);
                    var side = outward.sqrMagnitude > 0.04f ? outward.normalized : rig.transform.right;
                    var stand = place + side * 0.75f;
                    w.Activity = "Placing " + what;
                    Set(o, task, TaskState.Working, $"{w.DisplayName} placing {what} ({k + 1}/{points.Count})");
                    yield return w.WalkPath(Route(rig, w.transform.position, stand));
                    yield return w.Face(place);
                    w.Animator.Pose = CrewPose.Crouch;
                    yield return new WaitForSeconds(0.8f);
                    var rotation = chocks ? ChockRotation(rig, point, points) : Quaternion.Euler(0f, rig.transform.eulerAngles.y, 0f);
                    var item = w.PutDown(new Vector3(place.x, place.y + 0.01f, place.z), rotation);
                    if (item != null) placed.Add(item);
                    yield return new WaitForSeconds(0.4f);
                    w.Animator.Pose = CrewPose.Idle;
                }
            }
            Set(o, task, TaskState.Done, chocks ? "Wheels chocked" : "Cones out");
            yield return GoHome(o, w, rig);
        }

        static Quaternion ChockRotation(AircraftRig rig, Transform point, List<Transform> all)
        {
            // The wedge's toe (-x) goes under the tyre: find the partner chock; the wheel is between them.
            var partnerName = point.name.EndsWith("_Fwd") ? point.name.Replace("_Fwd", "_Aft") : point.name.Replace("_Aft", "_Fwd");
            Vector3 wheel = point.position - rig.transform.forward * (point.name.EndsWith("_Fwd") ? 0.5f : -0.5f);
            foreach (var p in all) if (p.name == partnerName) wheel = (p.position + point.position) * 0.5f;
            var away = Vector3.ProjectOnPlane(point.position - wheel, Vector3.up).normalized;
            if (away.sqrMagnitude < 0.5f) away = rig.transform.forward;
            return Quaternion.LookRotation(Vector3.Cross(away, Vector3.up), Vector3.up);
        }

        IEnumerator CollectItems(StandOps o, OpsTask task, List<GameObject> items)
        {
            items.RemoveAll(i => i == null);
            if (items.Count == 0) { Set(o, task, TaskState.Idle, "None out"); yield break; }
            var rig = StandRegistry.AircraftAt(o.Stand);
            Set(o, task, TaskState.Working, "Waiting for a worker");
            CrewAgent w = null;
            yield return GetWorker(o, a => w = a);
            if (w == null) { Set(o, task, TaskState.Failed, "Nobody free"); yield break; }
            var queue = new List<GameObject>(items);
            var n = 0;
            foreach (var item in queue)
            {
                if (item == null || w == null) continue;
                var grab = item.GetComponent<XRGrabInteractable>();
                if (grab != null && grab.isSelected) continue;               // the player has it
                w.Activity = "Collecting";
                Set(o, task, TaskState.Working, $"{w.DisplayName} collecting ({++n}/{queue.Count})");
                var p = item.transform.position;
                var side = Vector3.ProjectOnPlane(p - (rig != null ? rig.transform.position : p), Vector3.up);
                var standAt = p + (side.sqrMagnitude > 0.04f ? side.normalized : Vector3.right) * 0.75f;
                yield return w.WalkPath(Route(rig, w.transform.position, standAt));
                if (item == null) continue;
                yield return w.Face(item.transform.position);
                yield return w.Crouch(0.6f);
                items.Remove(item);
                w.Hold(item, w.CarriedCount % 2 == 0);
                if (w.CarriedCount >= 2)
                {
                    yield return w.WalkPath(Route(rig, w.transform.position, o.Stand.staging));
                    w.DropAllAndDestroy();
                }
            }
            if (w != null)
            {
                yield return w.WalkPath(Route(rig, w.transform.position, o.Stand.staging));
                w.DropAllAndDestroy();
                Release(w);
            }
            Set(o, task, TaskState.Idle, "Collected");
        }

        // ------------------------------------------------------------------ stairs / belt loader

        IEnumerator PositionEquipment(StandOps o, OpsTask task, bool stairs)
        {
            var rig = StandRegistry.AircraftAt(o.Stand);
            if (rig == null) { Set(o, task, TaskState.Failed, "No aircraft on the stand"); yield break; }
            var sill = FindDeep(rig.transform, stairs ? "SP_Sill_Door_1L" : "SP_Sill_CargoDoor_Fwd");
            if (sill == null) { Set(o, task, TaskState.Failed, "Aircraft has no such door"); yield break; }
            var label = stairs ? "Stairs" : "Belt loader";

            // Already there (perhaps you drove it in yourself)?
            var docked = DockedEquipment(rig, sill, stairs);
            if (docked != null)
            {
                if (stairs) o.Stairs = docked; else o.BeltLoader = docked;
                m_Reserved.Add(docked);
                rig.SetOpen01(stairs ? "Door_1L" : "CargoDoor_Fwd", 1f);
                Set(o, task, TaskState.Done, label + " already docked - door open");
                yield break;
            }
            var vehicle = stairs ? o.Stairs : o.BeltLoader;
            if (vehicle == null) vehicle = FindEquipment(o, stairs);
            if (vehicle == null) { Set(o, task, TaskState.Failed, "No " + label.ToLowerInvariant() + " available"); yield break; }
            m_Reserved.Add(vehicle);
            if (stairs) o.Stairs = vehicle; else o.BeltLoader = vehicle;

            Set(o, task, TaskState.Working, "Waiting for a driver");
            CrewAgent w = null;
            yield return GetWorker(o, a => w = a);
            if (w == null) { Set(o, task, TaskState.Failed, "Nobody free"); m_Reserved.Remove(vehicle); yield break; }

            // Walk to the vehicle, get in.
            Set(o, task, TaskState.Working, $"{w.DisplayName} walking to the {label.ToLowerInvariant()}");
            w.Activity = "Going to the " + label.ToLowerInvariant();
            yield return w.WalkPath(Route(rig, w.transform.position, ExitPoint(vehicle)));
            if (PlayerDriving(vehicle)) { Abort(o, task, w, vehicle, "You're driving it"); yield break; }
            yield return EnterVehicle(w, vehicle);

            // Drive to a point square to the door, a little out from it.
            var edge = FindDeep(vehicle.transform, stairs ? "PlatformEdge" : "BeltEnd");
            if (edge == null) { Abort(o, task, w, vehicle, "Vehicle has no docking edge"); yield break; }
            var outward = Outward(rig, sill);
            var heading = -outward;
            var edgeLocal = vehicle.transform.InverseTransformPoint(edge.position);
            var rot = Quaternion.LookRotation(heading, Vector3.up);
            var targetEdge = sill.position + outward * (stairs ? 0.9f : 1.0f);
            var root = targetEdge - rot * new Vector3(edgeLocal.x, 0f, edgeLocal.z);
            Set(o, task, TaskState.Working, $"{w.DisplayName} driving the {label.ToLowerInvariant()} to the aircraft");
            w.Activity = "Driving";
            yield return AutoDriver.DriveTo(vehicle, root, heading, 3.2f, 9f);
            if (PlayerDriving(vehicle)) { Abort(o, task, w, vehicle, "You took over"); yield break; }
            vehicle.SetParkingBrake(true);

            // Raise to the sill.
            Set(o, task, TaskState.Working, "Raising to the sill");
            var stairsMech = vehicle.GetComponent<StairsMechanism>();
            HydraulicAxis belt = null;
            if (!stairs)
                foreach (var a in vehicle.GetComponentsInChildren<HydraulicAxis>()) if (a.FunctionName.Contains("Belt")) belt = a;
            var timeout = Time.time + 60f;
            var aim = stairs ? sill.position.y - 0.04f : sill.position.y + 0.03f;
            while (edge.position.y < aim && Time.time < timeout)
            {
                if (stairsMech != null) stairsMech.Command(1f, 0f);
                if (belt != null) belt.Command(1f);
                yield return null;
            }

            // Creep in to the fuselage.
            Set(o, task, TaskState.Working, "Final approach");
            var gap = Vector3.Dot(edge.position - sill.position, outward);
            yield return AutoDriver.Creep(vehicle, gap - (stairs ? 0.3f : 0.12f), 0.3f);
            vehicle.SetParkingBrake(true);
            if (stairsMech != null)
            {
                timeout = Time.time + 15f;
                while (Vector3.Dot(edge.position - sill.position, outward) > 0.06f && stairsMech.Extension < stairsMech.ExtensionTravel - 0.01f && Time.time < timeout)
                {
                    stairsMech.Command(0f, 1f);
                    yield return null;
                }
            }

            // Out of the cab; open the door.
            yield return ExitVehicle(w, vehicle);
            if (stairs)
            {
                w.Activity = "Opening the door";
                Set(o, task, TaskState.Working, $"{w.DisplayName} opening door 1L");
                var up = StairsPath(vehicle, sill, outward);
                yield return w.WalkPath(up);
                yield return w.Face(sill.position);
                yield return new WaitForSeconds(0.6f);
                rig.SetOpen01("Door_1L", 1f);
                yield return new WaitForSeconds(2.5f);
                up.Reverse();
                yield return w.WalkPath(up);
            }
            else rig.SetOpen01("CargoDoor_Fwd", 1f);

            Set(o, task, TaskState.Done, stairs ? "Stairs at door 1L, door open" : "Belt loader at the forward hold, door open");
            yield return GoHome(o, w, rig);
        }

        IEnumerator RemoveEquipment(StandOps o, OpsTask task)
        {
            var stairs = task == OpsTask.Stairs;
            var vehicle = stairs ? o.Stairs : o.BeltLoader;
            if (vehicle == null) { Set(o, task, TaskState.Idle, "Nothing to remove"); yield break; }
            var rig = StandRegistry.AircraftAt(o.Stand);
            Set(o, task, TaskState.Working, "Waiting for a driver");
            CrewAgent w = null;
            yield return GetWorker(o, a => w = a);
            if (w == null) { Set(o, task, TaskState.Failed, "Nobody free"); yield break; }
            if (rig != null && stairs)
            {
                // Someone has to close the door first.
                var sill = FindDeep(rig.transform, "SP_Sill_Door_1L");
                if (sill != null && rig.GetOpen01("Door_1L") > 0.05f && o.FlightCrew.Count == 0)
                {
                    w.Activity = "Closing the door";
                    var up = StairsPath(vehicle, sill, Outward(rig, sill));
                    yield return w.WalkPath(Route(rig, w.transform.position, up[0]));
                    yield return w.WalkPath(up);
                    rig.SetOpen01("Door_1L", 0f);
                    yield return new WaitForSeconds(2.5f);
                    up.Reverse();
                    yield return w.WalkPath(up);
                }
            }
            else if (rig != null) rig.SetOpen01("CargoDoor_Fwd", 0f);

            Set(o, task, TaskState.Working, $"{w.DisplayName} removing the {(stairs ? "stairs" : "belt loader")}");
            yield return w.WalkPath(Route(rig, w.transform.position, ExitPoint(vehicle)));
            if (PlayerDriving(vehicle)) { Abort(o, task, w, vehicle, "You're driving it"); yield break; }
            yield return EnterVehicle(w, vehicle);
            vehicle.SetParkingBrake(true);
            var stairsMech = vehicle.GetComponent<StairsMechanism>();
            HydraulicAxis belt = null;
            foreach (var a in vehicle.GetComponentsInChildren<HydraulicAxis>()) if (a.FunctionName.Contains("Belt")) belt = a;
            var timeout = Time.time + 15f;
            while (stairsMech != null && stairsMech.Extension > 0.001f && Time.time < timeout) { stairsMech.Command(0f, -1f); yield return null; }
            yield return AutoDriver.Creep(vehicle, -3f, 0.6f);
            timeout = Time.time + 60f;
            while (Time.time < timeout && ((stairsMech != null && stairsMech.Rise > 0.001f) || (belt != null && !belt.IsStowed)))
            {
                if (stairsMech != null) stairsMech.Command(-1f, 0f);
                if (belt != null) belt.Command(-1f);
                yield return null;
            }
            var slot = stairs ? o.Stand.equipmentPark : o.Stand.beltPark;
            var away = Vector3.ProjectOnPlane(slot - vehicle.transform.position, Vector3.up);
            yield return AutoDriver.DriveTo(vehicle, slot, away.sqrMagnitude > 1f ? away.normalized : vehicle.transform.forward, 3.2f, 4f);
            yield return ExitVehicle(w, vehicle);
            m_Reserved.Remove(vehicle);
            if (stairs) o.Stairs = null; else o.BeltLoader = null;
            Set(o, task, TaskState.Idle, "Parked away");
            yield return GoHome(o, w, rig);
        }

        void Abort(StandOps o, OpsTask task, CrewAgent w, VehicleController v, string why)
        {
            if (v != null) m_Reserved.Remove(v);
            if (w != null)
            {
                w.StandUp();
                Release(w);
            }
            Set(o, task, TaskState.Failed, why);
        }

        static VehicleController DockedEquipment(AircraftRig rig, Transform sill, bool stairs)
        {
            var door = sill.name.Substring("SP_Sill_".Length);
            foreach (var v in FindObjectsByType<VehicleController>(FindObjectsSortMode.None))
            {
                if (stairs != (v.GetComponent<StairsMechanism>() != null)) continue;
                foreach (var sensor in v.GetComponentsInChildren<DoorDockingSensor>())
                    if (sensor.State == DockingState.Aligned && sensor.Door == door && Vector3.Distance(v.transform.position, sill.position) < 12f)
                        return v;
            }
            return null;
        }

        VehicleController FindEquipment(StandOps o, bool stairs)
        {
            VehicleController best = null;
            var bestDistance = 260f;
            foreach (var v in FindObjectsByType<VehicleController>(FindObjectsSortMode.None))
            {
                if (m_Reserved.Contains(v) || v.Occupied) continue;
                var fits = stairs ? v.GetComponent<StairsMechanism>() != null : FindDeep(v.transform, "BeltEnd") != null;
                if (!fits) continue;
                var d = Vector3.Distance(v.transform.position, o.Stand.stopPosition);
                if (d < bestDistance) { bestDistance = d; best = v; }
            }
            if (best != null || config == null || config.vehicles == null) return best;
            // None nearby: bring one from the pool at the equipment park.
            foreach (var e in config.vehicles.Entries)
            {
                if (e.prefab == null) continue;
                var fits = stairs ? e.prefab.GetComponentInChildren<StairsMechanism>(true) != null : FindDeep(e.prefab.transform, "BeltEnd") != null;
                if (!fits) continue;
                var slot = stairs ? o.Stand.equipmentPark : o.Stand.beltPark;
                var go = Instantiate(e.prefab, slot, Quaternion.Euler(0f, o.Stand.yaw + 180f, 0f));
                go.name = e.displayName;
                return go.GetComponentInChildren<VehicleController>();
            }
            return null;
        }

        static bool PlayerDriving(VehicleController v) => VehicleSeat.Current != null && VehicleSeat.Current.Vehicle == v;

        static Vector3 ExitPoint(VehicleController v)
        {
            var e = FindDeep(v.transform, "ExitPoint");
            var p = e != null ? e.position : v.transform.position - v.transform.right * 2f;
            p.y = v.transform.position.y;
            return p;
        }

        static IEnumerator EnterVehicle(CrewAgent w, VehicleController v)
        {
            yield return w.Face(v.transform.position);
            yield return new WaitForSeconds(0.4f);
            var eye = FindDeep(v.transform, "DriverEye");
            var seat = eye != null ? eye.position - v.transform.up * 1.22f - v.transform.forward * 0.05f : v.transform.position + Vector3.up;
            w.SitAt(seat, v.transform.eulerAngles.y, v.transform, true);
            w.Activity = "Driving the " + v.DisplayName.ToLowerInvariant();
        }

        static IEnumerator ExitVehicle(CrewAgent w, VehicleController v)
        {
            yield return new WaitForSeconds(0.5f);
            w.StandUp();
            var p = ExitPoint(v);
            w.SnapTo(p, v.transform.eulerAngles.y - 90f);
            yield return new WaitForSeconds(0.3f);
        }

        /// <summary>Up the passenger stairs to the door: foot, flight, platform, plate edge.</summary>
        static List<Vector3> StairsPath(VehicleController v, Transform sill, Vector3 outward)
        {
            var path = new List<Vector3>();
            var bottom = FindDeep(v.transform, "FlightBottom");
            var edge = FindDeep(v.transform, "PlatformEdge");
            var back = -Vector3.ProjectOnPlane(v.transform.forward, Vector3.up).normalized;
            var groundY = v.transform.position.y;
            if (bottom != null)
            {
                var foot = bottom.position + back * 1.3f;
                foot.y = groundY;
                path.Add(foot);
                path.Add(new Vector3(bottom.position.x, groundY, bottom.position.z));
            }
            if (edge != null)
            {
                path.Add(edge.position + outward * 0.9f + Vector3.up * 0.05f);
                path.Add(edge.position + outward * 0.25f + Vector3.up * 0.05f);
            }
            else path.Add(sill.position + outward * 0.5f);
            return path;
        }

        // ------------------------------------------------------------------ jet bridge

        IEnumerator DockBridge(StandOps o)
        {
            var bridge = o.Stand.bridge;
            if (bridge == null) { Set(o, OpsTask.Bridge, TaskState.Failed, "No jet bridge on this stand"); yield break; }
            bridge.Dock();
            Set(o, OpsTask.Bridge, TaskState.Working, bridge.Message);
            var timeout = Time.time + 180f;
            while (bridge.State == BridgeState.Docking && Time.time < timeout)
            {
                Set(o, OpsTask.Bridge, TaskState.Working, bridge.Message);
                yield return new WaitForSeconds(0.5f);
            }
            Set(o, OpsTask.Bridge, bridge.IsDocked ? TaskState.Done : TaskState.Failed, bridge.Message);
        }

        // ------------------------------------------------------------------ flight crew

        IEnumerator BoardCrew(StandOps o)
        {
            o.CrewBusy = true;
            var rig = StandRegistry.AircraftAt(o.Stand);
            if (rig == null) { SetCrew(o, "No aircraft on the stand"); o.CrewBusy = false; yield break; }
            var sill = FindDeep(rig.transform, "SP_Sill_Door_1L");
            if (sill == null) { SetCrew(o, "Aircraft has no door 1L"); o.CrewBusy = false; yield break; }

            // Access first: the bridge, or stairs at door 1L.
            if (o.Stand.IsGate)
            {
                if (!o.Stand.bridge.IsDocked)
                {
                    SetCrew(o, "Waiting for the jet bridge");
                    Run(o.Stand, OpsTask.Bridge);
                    yield return WaitFor(() => o.Stand.bridge.IsDocked, () => o.Get(OpsTask.Bridge) == TaskState.Failed, 200f);
                    if (!o.Stand.bridge.IsDocked) { SetCrew(o, "Bridge couldn't dock - crew waiting"); o.CrewBusy = false; yield break; }
                }
            }
            else if (o.Get(OpsTask.Stairs) != TaskState.Done)
            {
                SetCrew(o, "Waiting for the stairs");
                Run(o.Stand, OpsTask.Stairs);
                yield return WaitFor(() => o.Get(OpsTask.Stairs) == TaskState.Done, () => o.Get(OpsTask.Stairs) == TaskState.Failed, 400f);
                if (o.Get(OpsTask.Stairs) != TaskState.Done) { SetCrew(o, "No stairs - crew waiting"); o.CrewBusy = false; yield break; }
            }
            rig.SetOpen01("Door_1L", 1f);

            SetCrew(o, "Crew on their way");
            var roles = new[] { CrewRole.Captain, CrewRole.FirstOfficer, CrewRole.Purser, CrewRole.CabinCrew, CrewRole.CabinCrew, CrewRole.CabinCrew };
            var running = 0;
            for (var i = 0; i < roles.Length; i++)
            {
                var pilot = roles[i] == CrewRole.Captain || roles[i] == CrewRole.FirstOfficer;
                var prefab = config != null ? (pilot ? config.pilotPrefab : config.cabinCrewPrefab) : null;
                var a = Spawn(prefab, o.Stand.crewEntry, o.Stand.yaw, k_CrewNames[i]);
                if (a == null) continue;
                a.Role = roles[i];
                a.Busy = true;
                o.FlightCrew.Add(a);
                running++;
                StartCoroutine(Board(o, rig, a, i, () => running--));
                yield return new WaitForSeconds(1.6f);
            }
            if (running == 0) SetCrew(o, "No crew prefabs - run Ground Crew > Operations > Set Up");
            while (running > 0)
            {
                SetCrew(o, $"Boarding ({o.FlightCrew.Count - running}/{o.FlightCrew.Count} aboard)");
                yield return new WaitForSeconds(0.5f);
            }
            if (o.FlightCrew.Count > 0)
            {
                rig.SetFlightDeckLight(true);
                SetCrew(o, $"Crew aboard ({o.FlightCrew.Count})");
            }
            o.CrewBusy = false;
        }

        IEnumerator Board(StandOps o, AircraftRig rig, CrewAgent a, int index, Action done)
        {
            a.Activity = "Walking to the aircraft";
            yield return a.WalkPath(AccessPath(o, rig));
            var sill = FindDeep(rig.transform, "SP_Sill_Door_1L");
            if (sill == null) { done(); yield break; }
            a.Activity = "Boarding";
            float finalYaw;
            bool sit;
            var inside = InsidePath(rig, sill, a.Role, index, out finalYaw, out sit);
            yield return a.WalkPath(inside);
            a.transform.SetParent(rig.transform, true);
            yield return a.TurnTo(finalYaw);
            if (sit)
            {
                // Slide into the seat: the flight-deck floor is level with the door sill.
                var layout = AircraftCrewLayout.For(rig);
                var side = a.Role == CrewRole.Captain ? -1f : 1f;
                var local = rig.transform.InverseTransformPoint(sill.position);
                var floor = rig.transform.TransformPoint(new Vector3(side * layout.SeatX, local.y, layout.SeatZ - 0.05f));
                a.SitAt(floor, finalYaw, rig.transform);
                a.Activity = a.Role == CrewRole.Captain ? "In the left seat" : "In the right seat";
            }
            else a.Activity = a.Role == CrewRole.Purser ? "Forward galley" : index >= 4 ? "Aft galley" : "At door 1L";
            a.Busy = false;
            done();
        }

        IEnumerator DeplaneCrew(StandOps o)
        {
            o.CrewBusy = true;
            var rig = StandRegistry.AircraftAt(o.Stand);
            var sill = rig != null ? FindDeep(rig.transform, "SP_Sill_Door_1L") : null;
            if (rig == null || sill == null) { SetCrew(o, "Aircraft gone"); o.CrewBusy = false; yield break; }
            if (o.Stand.IsGate ? !o.Stand.bridge.IsDocked : o.Get(OpsTask.Stairs) != TaskState.Done)
            {
                SetCrew(o, o.Stand.IsGate ? "Dock the bridge first" : "Bring the stairs first");
                o.CrewBusy = false;
                yield break;
            }
            rig.SetOpen01("Door_1L", 1f);
            rig.SetFlightDeckLight(false);
            var running = 0;
            var crew = new List<CrewAgent>(o.FlightCrew);
            for (var i = crew.Count - 1; i >= 0; i--)
            {
                var a = crew[i];
                if (a == null) continue;
                running++;
                StartCoroutine(Leave(o, rig, sill, a, i, () => running--));
                yield return new WaitForSeconds(1.4f);
            }
            while (running > 0)
            {
                SetCrew(o, $"Crew leaving ({running} still to go)");
                yield return new WaitForSeconds(0.5f);
            }
            o.FlightCrew.Clear();
            SetCrew(o, "Crew gone");
            o.CrewBusy = false;
        }

        IEnumerator Leave(StandOps o, AircraftRig rig, Transform sill, CrewAgent a, int index, Action done)
        {
            a.Busy = true;
            a.Activity = "Leaving";
            a.StandUp(rig.transform);
            float unusedYaw;
            bool unusedSit;
            var inside = InsidePath(rig, sill, a.Role, index, out unusedYaw, out unusedSit);
            inside.Reverse();
            inside.RemoveAt(0);
            yield return a.WalkPath(inside);
            a.transform.SetParent(null, true);
            var access = AccessPath(o, rig);
            access.Reverse();
            yield return a.WalkPath(access);
            Destroy(a.gameObject);
            done();
        }

        /// <summary>From the crew entry to just outside door 1L, by the bridge or the stairs.</summary>
        List<Vector3> AccessPath(StandOps o, AircraftRig rig)
        {
            var sill = FindDeep(rig.transform, "SP_Sill_Door_1L");
            var outward = Outward(rig, sill);
            if (o.Stand.IsGate)
            {
                var path = o.Stand.bridge.WalkPath(o.Stand.crewEntry);
                path.Add(sill.position + outward * 0.35f);
                return path;
            }
            var route = new List<Vector3> { o.Stand.crewEntry };
            if (o.Stairs != null)
            {
                var stairs = StairsPath(o.Stairs, sill, outward);
                route.AddRange(Route(rig, o.Stand.crewEntry, stairs[0]));
                route.AddRange(stairs.GetRange(1, stairs.Count - 1));
            }
            route.Add(sill.position + outward * 0.35f);
            return route;
        }

        /// <summary>From just outside door 1L to a crew member's place; returns facing and whether they sit.</summary>
        static List<Vector3> InsidePath(AircraftRig rig, Transform sill, CrewRole role, int index, out float yaw, out bool sit)
        {
            var t = rig.transform;
            var layout = AircraftCrewLayout.For(rig);
            var s = t.InverseTransformPoint(sill.position);
            var floorY = s.y + 0.02f;
            Func<float, float, Vector3> L = (x, z) => t.TransformPoint(new Vector3(x, floorY, z));
            var path = new List<Vector3> { L(s.x * 0.55f, s.z), L(0f, s.z) };
            var heading = t.eulerAngles.y;
            sit = false;
            yaw = heading;
            switch (role)
            {
                case CrewRole.Captain:
                case CrewRole.FirstOfficer:
                {
                    var side = role == CrewRole.Captain ? -1f : 1f;
                    path.Add(L(0f, layout.BulkheadZ - 0.3f));
                    path.Add(L(0f, layout.BulkheadZ + 0.5f));
                    path.Add(L(side * layout.SeatX * 0.35f, layout.SeatZ - 0.55f));
                    sit = true;
                    break;
                }
                case CrewRole.Purser:
                    path.Add(L(0.25f, layout.FwdGalleyZ + 0.2f));
                    yaw = heading - 90f;
                    break;
                default:
                    if (index == 3)
                    {
                        path.Add(L(-0.2f, s.z - 0.6f));
                        yaw = heading - 90f;
                    }
                    else
                    {
                        var aft = FindDeep(rig.transform, "SP_Sill_Door_2L");
                        var z2 = aft != null ? t.InverseTransformPoint(aft.position).z : s.z - 26f;
                        path.Add(L(0f, z2 + 1.6f));
                        path.Add(L(index == 4 ? 0.3f : -0.2f, z2 + (index == 4 ? 0.7f : 0.1f)));
                        yaw = heading + (index == 4 ? 180f : 90f);
                    }
                    break;
            }
            return path;
        }

        // ------------------------------------------------------------------ routing

        /// <summary>
        /// Walking route around an aircraft: anything behind the engines' front is reached along
        /// a corridor outboard of the engines, so nobody walks through a nacelle or under the belly.
        /// </summary>
        public static List<Vector3> Route(AircraftRig rig, Vector3 from, Vector3 to)
        {
            var path = new List<Vector3>();
            if (rig == null) { path.Add(to); return path; }
            var t = rig.transform;
            var a = t.InverseTransformPoint(from);
            var b = t.InverseTransformPoint(to);
            const float frontZ = 2f, corridor = 9.5f, behind = -6.5f;
            var aBehind = a.z < behind && Mathf.Abs(a.x) < corridor + 3f;
            var bBehind = b.z < behind && Mathf.Abs(b.x) < corridor + 3f;
            Func<float, float, Vector3> W = (x, z) => t.TransformPoint(new Vector3(x, 0f, z));
            if (aBehind || bBehind)
            {
                var sa = Mathf.Abs(a.x) > 0.5f ? Mathf.Sign(a.x) : Mathf.Sign(b.x == 0f ? 1f : b.x);
                var sb = Mathf.Abs(b.x) > 0.5f ? Mathf.Sign(b.x) : sa;
                if (aBehind) { path.Add(W(sa * corridor, a.z)); path.Add(W(sa * corridor, frontZ)); }
                if (bBehind)
                {
                    if (!aBehind || sa != sb) path.Add(W(sb * corridor, frontZ));
                    path.Add(W(sb * corridor, b.z));
                }
            }
            path.Add(to);
            for (var i = 0; i < path.Count; i++) path[i] = new Vector3(path[i].x, i == path.Count - 1 ? to.y : t.position.y, path[i].z);
            return path;
        }

        static Vector3 Outward(AircraftRig rig, Transform sill)
        {
            var heading = Vector3.ProjectOnPlane(rig.transform.forward, Vector3.up).normalized;
            var outward = Vector3.ProjectOnPlane(sill.position - rig.transform.position, Vector3.up);
            return (outward - heading * Vector3.Dot(outward, heading)).normalized;
        }

        static IEnumerator WaitFor(Func<bool> ok, Func<bool> failed, float timeout)
        {
            var end = Time.time + timeout;
            while (!ok() && !failed() && Time.time < end) yield return new WaitForSeconds(0.5f);
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            foreach (Transform c in root)
            {
                var hit = FindDeep(c, name);
                if (hit != null) return hit;
            }
            return null;
        }
    }

    public readonly struct OpsEvent : IGameEvent
    {
        public readonly string Stand, Task, Note;
        public readonly TaskState State;
        public OpsEvent(string stand, string task, TaskState state, string note) { Stand = stand; Task = task; State = state; Note = note; }
    }
}
