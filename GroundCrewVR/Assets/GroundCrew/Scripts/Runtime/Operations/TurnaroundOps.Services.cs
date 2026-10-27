using System;
using System.Collections;
using GroundCrew.Aircraft;
using GroundCrew.Airport;
using GroundCrew.Crew;
using GroundCrew.Interaction;
using GroundCrew.Vehicles;
using UnityEngine;

namespace GroundCrew.Operations
{
    /// <summary>
    /// Aircraft services by the AI ground crew: ground power, refuelling, potable water,
    /// lavatory service and catering. Each job brings its vehicle, connects a hose or cable to
    /// the aircraft's service point, runs (quantities shown on the iPad), disconnects and takes
    /// the vehicle back where it came from.
    /// </summary>
    public partial class TurnaroundOps
    {
        const float k_FuelFlowKgPerSecond = 1000f / 60f * 0.8f;    // ~1000 l/min of Jet A-1

        // ------------------------------------------------------------------ ground power

        IEnumerator ConnectGpu(StandOps o)
        {
            const OpsTask task = OpsTask.GPU;
            Transform sp;
            var rig = StandRegistry_Aircraft(o, task, out sp, "SP_GPU");
            if (rig == null) yield break;
            VehicleController known;
            var gpu = o.Equipment.TryGetValue(task, out known) && known != null ? known
                : FindVehicle(o, v => v.GetComponent<GroundPowerUnit>() != null, e => e.prefab.GetComponentInChildren<GroundPowerUnit>(true) != null, 0);
            if (gpu == null) { Set(o, task, TaskState.Failed, "No ground power unit available"); yield break; }
            var unit = gpu.GetComponent<GroundPowerUnit>();
            Claim(o, task, gpu);

            Set(o, task, TaskState.Working, "Waiting for a worker");
            CrewAgent w = null;
            yield return GetWorker(o, a => w = a);
            if (w == null) { Free(o, task); Set(o, task, TaskState.Failed, "Nobody free"); yield break; }

            // Tow it by hand to the nose, right side, facing aft (approaching from ahead, clear of the engines).
            var handle = DrawbarHandle(gpu);
            Set(o, task, TaskState.Working, $"{w.DisplayName} towing the GPU");
            w.Activity = "Towing the GPU";
            yield return w.WalkPath(Route(rig, w.transform.position, Ground(handle != null ? handle.position : gpu.transform.position)));
            var side = Mathf.Sign(rig.transform.InverseTransformPoint(sp.position).x);
            var local = rig.transform.InverseTransformPoint(sp.position);
            var target = rig.transform.TransformPoint(new Vector3(local.x + side * 3.4f, 0f, local.z + 1.0f));
            var towing = true;
            if (handle != null) StartCoroutine(Escort(w, handle, gpu, () => towing));
            yield return AutoDriver.DriveTo(gpu, target, -rig.transform.forward, 1.3f, 5f);
            towing = false;
            yield return null;
            w.Animator.Pose = CrewPose.Idle;

            // Run the cable to the receptacle, start up, switch on.
            Set(o, task, TaskState.Working, $"{w.DisplayName} plugging in");
            w.Activity = "Plugging in the GPU";
            var outlet = FindDeep(gpu.transform, "Btn_GpuOutput") ?? gpu.transform;
            var cable = Hose(o, task, outlet, w.Animator.HandAttach(true), 0.05f, 9f);
            yield return w.WalkPath(Route(rig, w.transform.position, Ground(sp.position) + Outward(rig, sp) * 0.5f));
            yield return w.Face(sp.position);
            w.Animator.Pose = CrewPose.Reach;
            yield return new WaitForSeconds(1.2f);
            cable.Connect(outlet, Anchor(sp, "GPU plug"), 9f);
            w.Animator.Pose = CrewPose.Idle;
            yield return w.WalkPath(new[] { Ground(gpu.transform.position) - gpu.transform.right * 1.3f });
            gpu.StartEngine();
            yield return new WaitForSeconds(3f);
            if (unit != null) unit.SetOutput(true);
            var services = AircraftServices.For(rig);
            services.ExternalPower = true;
            rig.SetNavLights(true);
            Set(o, task, TaskState.Done, "On ground power (115 V 400 Hz)");
            yield return GoHome(o, w, rig);
        }

        IEnumerator DisconnectGpu(StandOps o)
        {
            const OpsTask task = OpsTask.GPU;
            VehicleController gpu;
            if (!o.Equipment.TryGetValue(task, out gpu) || gpu == null) { Set(o, task, TaskState.Idle, ""); yield break; }
            var rig = StandRegistry.AircraftAt(o.Stand);
            Set(o, task, TaskState.Working, "Waiting for a worker");
            CrewAgent w = null;
            yield return GetWorker(o, a => w = a);
            if (w == null) { Set(o, task, TaskState.Failed, "Nobody free"); yield break; }
            w.Activity = "Disconnecting the GPU";
            Set(o, task, TaskState.Working, $"{w.DisplayName} disconnecting");
            yield return w.WalkPath(Route(rig, w.transform.position, Ground(gpu.transform.position) - gpu.transform.right * 1.3f));
            var unit = gpu.GetComponent<GroundPowerUnit>();
            if (unit != null) unit.SetOutput(false);
            gpu.StopEngine();
            if (rig != null)
            {
                var services = AircraftServices.For(rig);
                services.ExternalPower = false;
                rig.SetNavLights(false);
                var sp = FindDeep(rig.transform, "SP_GPU");
                GameObject line;
                if (sp != null && o.Hoses.TryGetValue(task, out line) && line != null)
                {
                    yield return w.WalkPath(Route(rig, w.transform.position, Ground(sp.position) + Outward(rig, sp) * 0.5f));
                    w.Animator.Pose = CrewPose.Reach;
                    yield return new WaitForSeconds(1f);
                    line.GetComponent<CableRenderer>().Connect(FindDeep(gpu.transform, "Btn_GpuOutput") ?? gpu.transform, w.Animator.HandAttach(true), 9f);
                    w.Animator.Pose = CrewPose.Idle;
                    yield return w.WalkPath(new[] { Ground(gpu.transform.position) - gpu.transform.right * 1.3f });
                }
            }
            DropHose(o, task);
            var handle = DrawbarHandle(gpu);
            if (handle != null) yield return w.WalkPath(new[] { Ground(handle.position) });
            var towing = true;
            if (handle != null) StartCoroutine(Escort(w, handle, gpu, () => towing));
            yield return ReturnHome(o, task, gpu, 1.3f);
            towing = false;
            yield return null;
            Free(o, task);
            Set(o, task, TaskState.Idle, "GPU parked away");
            yield return GoHome(o, w, rig);
        }

        /// <summary>The worker walks just ahead of the drawbar handle while the trailer moves (towing by hand).</summary>
        IEnumerator Escort(CrewAgent w, Transform handle, VehicleController trailer, Func<bool> active)
        {
            var last = w.transform.position;
            w.Animator.Pose = CrewPose.Walk;
            w.Animator.Carrying = true;
            while (active() && w != null && handle != null)
            {
                var away = Vector3.ProjectOnPlane(handle.position - trailer.transform.position, Vector3.up);
                away = away.sqrMagnitude > 1e-4f ? away.normalized : trailer.transform.forward;
                var p = Ground(handle.position) + away * 0.35f;
                w.transform.SetPositionAndRotation(p, Quaternion.LookRotation(away, Vector3.up));
                w.Animator.Speed = Vector3.Distance(p, last) / Mathf.Max(Time.deltaTime, 1e-4f);
                last = p;
                yield return null;
            }
            if (w != null)
            {
                w.Animator.Carrying = false;
                w.Animator.Speed = 0f;
            }
        }

        static Transform DrawbarHandle(VehicleController trailer)
        {
            var group = trailer.transform.parent != null ? trailer.transform.parent : trailer.transform;
            return FindDeep(group, "DrawbarHandle");
        }

        // ------------------------------------------------------------------ refuelling

        IEnumerator Refuel(StandOps o)
        {
            const OpsTask task = OpsTask.Fuel;
            Transform coupling;
            var rig = StandRegistry_Aircraft(o, task, out coupling, "SP_RefuelCoupling");
            if (rig == null) yield break;
            var services = AircraftServices.For(rig);
            if (services.FuelKg >= services.FuelTargetKg - 50f) { Set(o, task, TaskState.Done, $"Already fuelled ({services.FuelKg:N0} kg)"); yield break; }
            var truck = FindVehicle(o, v => FindDeep(v.transform, "BondingReel") != null, e => FindDeep(e.prefab.transform, "BondingReel") != null, 1);
            if (truck == null) { Set(o, task, TaskState.Failed, "No fuel truck available"); yield break; }
            Claim(o, task, truck);

            Set(o, task, TaskState.Working, "Waiting for a driver");
            CrewAgent w = null;
            yield return GetWorker(o, a => w = a);
            if (w == null) { Free(o, task); Set(o, task, TaskState.Failed, "Nobody free"); yield break; }
            yield return w.WalkPath(Route(rig, w.transform.position, ExitPoint(truck)));
            if (PlayerDriving(truck)) { Abort(o, task, w, truck, "You're driving it"); yield break; }
            yield return EnterVehicle(w, truck);

            // Park ahead of the wing, outboard of the engine, facing aft; the hose reaches back under the wing.
            var c = rig.transform.InverseTransformPoint(coupling.position);
            var side = Mathf.Sign(c.x);
            var target = rig.transform.TransformPoint(new Vector3(c.x + side * 1.8f, 0f, c.z + 7.5f));
            Set(o, task, TaskState.Working, $"{w.DisplayName} driving the fuel truck to the wing");
            yield return AutoDriver.DriveTo(truck, target, -rig.transform.forward, 3f, 9f);
            if (PlayerDriving(truck)) { Abort(o, task, w, truck, "You took over"); yield break; }
            yield return ExitVehicle(w, truck);

            // Hose from the reel to the underwing coupling.
            var reel = FindDeep(truck.transform, "HoseReel") ?? FindDeep(truck.transform, "HoseNozzle") ?? truck.transform;
            yield return w.WalkPath(new[] { Ground(reel.position) + Vector3.ProjectOnPlane(-truck.transform.forward, Vector3.up).normalized * 0.8f });
            var hose = Hose(o, task, reel, w.Animator.HandAttach(true), 0.07f, 14f);
            Set(o, task, TaskState.Working, $"{w.DisplayName} connecting the hose");
            yield return w.WalkPath(Route(rig, w.transform.position, Ground(coupling.position) + rig.transform.forward * 0.4f));
            yield return w.Face(coupling.position);
            w.Animator.Pose = CrewPose.Reach;
            yield return new WaitForSeconds(1.5f);
            hose.Connect(reel, Anchor(coupling, "Refuel nozzle"), 14f);
            w.Animator.Pose = CrewPose.Idle;

            // On the deadman by the truck while it flows.
            yield return w.WalkPath(Route(rig, w.transform.position, Ground(reel.position) - truck.transform.right * 1.4f));
            w.Activity = "On the deadman";
            var scale = config != null ? config.serviceTimeScale : 4f;
            while (services.FuelKg < services.FuelTargetKg)
            {
                services.FuelKg = Mathf.Min(services.FuelTargetKg, services.FuelKg + k_FuelFlowKgPerSecond * scale * Time.deltaTime);
                o.Note[task] = $"Fuelling {services.FuelKg:N0} / {services.FuelTargetKg:N0} kg";
                o.State[task] = TaskState.Working;
                yield return null;
            }
            Set(o, task, TaskState.Working, $"{w.DisplayName} disconnecting");
            yield return w.WalkPath(Route(rig, w.transform.position, Ground(coupling.position) + rig.transform.forward * 0.4f));
            w.Animator.Pose = CrewPose.Reach;
            yield return new WaitForSeconds(1.2f);
            hose.Connect(reel, w.Animator.HandAttach(true), 14f);
            w.Animator.Pose = CrewPose.Idle;
            yield return w.WalkPath(Route(rig, w.transform.position, Ground(reel.position) + Vector3.ProjectOnPlane(-truck.transform.forward, Vector3.up).normalized * 0.8f));
            DropHose(o, task);
            yield return w.WalkPath(new[] { ExitPoint(truck) });
            yield return EnterVehicle(w, truck);
            yield return ReturnHome(o, task, truck, 3f);
            yield return ExitVehicle(w, truck);
            Free(o, task);
            Set(o, task, TaskState.Done, $"Fuelled: {services.FuelKg:N0} kg on board");
            yield return GoHome(o, w, rig);
        }

        // ------------------------------------------------------------------ water / lavatory

        IEnumerator ServiceTruck(StandOps o, OpsTask task, string point)
        {
            var water = task == OpsTask.Water;
            Transform panel;
            var rig = StandRegistry_Aircraft(o, task, out panel, point);
            if (rig == null) yield break;
            var services = AircraftServices.For(rig);
            var label = water ? "water" : "lavatory";
            var truck = FindVehicle(o, v => v.DisplayName.ToLowerInvariant().Contains(water ? "water" : "lavatory"),
                e => e.displayName.ToLowerInvariant().Contains(water ? "water" : "lavatory"), water ? 2 : 3);
            if (truck == null) { Set(o, task, TaskState.Failed, $"No {label} truck available"); yield break; }
            Claim(o, task, truck);
            yield return WaitAftZone(o, task);

            CrewAgent w = null;
            yield return GetWorker(o, a => w = a);
            if (w == null) { ReleaseAftZone(o, task); Free(o, task); Set(o, task, TaskState.Failed, "Nobody free"); yield break; }
            Set(o, task, TaskState.Working, $"{w.DisplayName} fetching the {label} truck");
            yield return w.WalkPath(Route(rig, w.transform.position, ExitPoint(truck)));
            if (PlayerDriving(truck)) { ReleaseAftZone(o, task); Abort(o, task, w, truck, "You're driving it"); yield break; }
            yield return EnterVehicle(w, truck);

            // Back the rear platform up to the service panel.
            var platform = FindDeep(truck.transform, "ServicePlatform");
            var outward = Outward(rig, panel);
            var rot = Quaternion.LookRotation(outward, Vector3.up);
            var platLocal = platform != null ? truck.transform.InverseTransformPoint(platform.position) : new Vector3(0f, 1f, -3f);
            var root = Ground(panel.position) + outward * 1.5f - rot * new Vector3(platLocal.x, 0f, platLocal.z);
            Set(o, task, TaskState.Working, $"Positioning the {label} truck");
            yield return AutoDriver.DriveTo(truck, root + outward * 7f, outward, 3f, 3f);
            yield return AutoDriver.Creep(truck, -7f, 0.6f);
            if (PlayerDriving(truck)) { ReleaseAftZone(o, task); Abort(o, task, w, truck, "You took over"); yield break; }
            yield return ExitVehicle(w, truck);

            // Onto the platform, up to the panel.
            HydraulicAxis lift = null;
            foreach (var a in truck.GetComponentsInChildren<HydraulicAxis>()) if (a.FunctionName.Contains("platform")) lift = a;
            if (platform != null)
            {
                yield return w.WalkPath(new[] { Ground(platform.position) + truck.transform.right * 1.2f });
                w.SnapTo(platform.position + Vector3.up * 0.02f, Quaternion.LookRotation(-outward).eulerAngles.y);
                w.transform.SetParent(platform, true);
            }
            var timeout = Time.time + 30f;
            while (lift != null && platform != null && platform.position.y < panel.position.y - 1.25f && Time.time < timeout)
            {
                lift.Command(1f);
                yield return null;
            }
            var reel = FindDeep(truck.transform, "HoseReel") ?? FindDeep(truck.transform, "HoseNozzle") ?? truck.transform;
            var hose = Hose(o, task, reel, w.Animator.HandAttach(true), 0.05f, 6f);
            yield return w.Face(panel.position);
            w.Animator.Pose = CrewPose.Reach;
            yield return new WaitForSeconds(1.2f);
            hose.Connect(reel, Anchor(panel, water ? "Water hose" : "Waste hose"), 6f);
            w.Animator.Pose = CrewPose.Idle;
            w.Activity = water ? "Filling potable water" : "Draining and flushing";
            var scale = config != null ? config.serviceTimeScale : 4f;
            while (water ? services.Water01 < 1f : services.Waste01 > 0f)
            {
                var step = 0.01f * scale * Time.deltaTime;
                if (water) services.Water01 = Mathf.Min(1f, services.Water01 + step);
                else services.Waste01 = Mathf.Max(0f, services.Waste01 - step);
                o.Note[task] = water ? $"Filling water tank {services.Water01:P0}" : $"Waste tank {services.Waste01:P0}";
                o.State[task] = TaskState.Working;
                yield return null;
            }
            w.Animator.Pose = CrewPose.Reach;
            yield return new WaitForSeconds(1f);
            DropHose(o, task);
            w.Animator.Pose = CrewPose.Idle;
            timeout = Time.time + 30f;
            while (lift != null && !lift.IsStowed && Time.time < timeout) { lift.Command(-1f); yield return null; }
            w.StandUp();
            w.SnapTo(Ground(w.transform.position) + truck.transform.right * 1.2f, w.transform.eulerAngles.y);
            yield return w.WalkPath(new[] { ExitPoint(truck) });
            yield return EnterVehicle(w, truck);
            yield return AutoDriver.Creep(truck, 7f, 1.5f);
            ReleaseAftZone(o, task);
            yield return ReturnHome(o, task, truck, 3f);
            yield return ExitVehicle(w, truck);
            Free(o, task);
            Set(o, task, TaskState.Done, water ? "Potable water full" : "Lavatories serviced");
            yield return GoHome(o, w, rig);
        }

        // ------------------------------------------------------------------ catering

        IEnumerator Cater(StandOps o)
        {
            const OpsTask task = OpsTask.Catering;
            Transform sill;
            var rig = StandRegistry_Aircraft(o, task, out sill, "SP_Sill_Door_2R");
            if (rig == null) yield break;
            var truck = FindVehicle(o, v => v.GetComponent<CateringLift>() != null, e => e.prefab.GetComponentInChildren<CateringLift>(true) != null, 4);
            if (truck == null) { Set(o, task, TaskState.Failed, "No catering truck available"); yield break; }
            var lift = truck.GetComponent<CateringLift>();
            Claim(o, task, truck);
            yield return WaitAftZone(o, task);
            CrewAgent w = null;
            yield return GetWorker(o, a => w = a);
            if (w == null) { ReleaseAftZone(o, task); Free(o, task); Set(o, task, TaskState.Failed, "Nobody free"); yield break; }
            Set(o, task, TaskState.Working, $"{w.DisplayName} fetching the catering truck");
            yield return w.WalkPath(Route(rig, w.transform.position, ExitPoint(truck)));
            if (PlayerDriving(truck)) { ReleaseAftZone(o, task); Abort(o, task, w, truck, "You're driving it"); yield break; }
            yield return EnterVehicle(w, truck);

            var edge = FindDeep(truck.transform, "PlatformEdge");
            var platform = FindDeep(truck.transform, "Platform");
            var outward = Outward(rig, sill);
            var heading = -outward;
            var edgeLocal = edge != null ? truck.transform.InverseTransformPoint(edge.position) : new Vector3(0f, 0f, 4f);
            var rot = Quaternion.LookRotation(heading, Vector3.up);
            var root = sill.position + outward * 1.0f - rot * new Vector3(edgeLocal.x, 0f, edgeLocal.z);
            Set(o, task, TaskState.Working, "Driving to door 2R");
            yield return AutoDriver.DriveTo(truck, root, heading, 3f, 9f);
            if (PlayerDriving(truck)) { ReleaseAftZone(o, task); Abort(o, task, w, truck, "You took over"); yield break; }
            yield return ExitVehicle(w, truck);

            // Onto the front platform; ride the box up.
            if (platform != null)
            {
                yield return w.WalkPath(new[] { Ground(platform.position) - truck.transform.right * 1.8f });
                // Platform floor: 7 cm above its pivot, 0.45 m ahead of the box front.
                w.SnapTo(platform.position + truck.transform.forward * 0.3f + Vector3.up * 0.07f, truck.transform.eulerAngles.y);
                w.transform.SetParent(platform, true);
            }
            Set(o, task, TaskState.Working, "Raising the box");
            var timeout = Time.time + 60f;
            while (edge != null && edge.position.y < sill.position.y - 0.04f && Time.time < timeout) { lift.Command(1f, 0f); yield return null; }
            var gap = edge != null ? Vector3.Dot(edge.position - sill.position, outward) : 0.3f;
            yield return AutoDriver.Creep(truck, gap - 0.3f, 0.3f);
            truck.SetParkingBrake(true);
            timeout = Time.time + 15f;
            while (edge != null && Vector3.Dot(edge.position - sill.position, outward) > 0.06f && lift.Extension < lift.PlatformTravel - 0.01f && Time.time < timeout)
            {
                lift.Command(0f, 1f);
                yield return null;
            }
            rig.SetOpen01("Door_2R", 1f);
            yield return new WaitForSeconds(2.5f);

            // Trolley exchange: back and forth between the box and the galley door.
            w.Activity = "Exchanging trolleys";
            // (Turning between box and door on the spot: the platform is small and moving.)
            for (var k = 0; k < 4 && edge != null; k++)
            {
                Set(o, task, TaskState.Working, $"Exchanging trolleys ({k + 1}/4)");
                w.Animator.Carrying = true;
                yield return w.Face(sill.position);
                yield return new WaitForSeconds(2f);
                w.Animator.Carrying = k % 2 == 0;
                yield return w.TurnTo(truck.transform.eulerAngles.y + 180f);
                yield return new WaitForSeconds(2f);
            }
            w.Animator.Carrying = false;
            AircraftServices.For(rig).CateringDone = true;
            rig.SetOpen01("Door_2R", 0f);
            yield return new WaitForSeconds(2.5f);
            timeout = Time.time + 15f;
            while (lift.Extension > 0.001f && Time.time < timeout) { lift.Command(0f, -1f); yield return null; }
            yield return AutoDriver.Creep(truck, -1.5f, 0.4f);
            timeout = Time.time + 60f;
            while (lift.Rise > 0.001f && Time.time < timeout) { lift.Command(-1f, 0f); yield return null; }
            w.StandUp();
            w.SnapTo(Ground(w.transform.position) - truck.transform.right * 1.8f, w.transform.eulerAngles.y);
            yield return w.WalkPath(new[] { ExitPoint(truck) });
            yield return EnterVehicle(w, truck);
            ReleaseAftZone(o, task);
            yield return ReturnHome(o, task, truck, 3f);
            yield return ExitVehicle(w, truck);
            Free(o, task);
            Set(o, task, TaskState.Done, "Catering loaded");
            yield return GoHome(o, w, rig);
        }

        // ------------------------------------------------------------------ shared

        AircraftRig StandRegistry_Aircraft(StandOps o, OpsTask task, out Transform point, string pointName)
        {
            point = null;
            var rig = Airport.StandRegistry.AircraftAt(o.Stand);
            if (rig == null) { Set(o, task, TaskState.Failed, "No aircraft on the stand"); return null; }
            point = FindDeep(rig.transform, pointName);
            if (point == null) { Set(o, task, TaskState.Failed, "Aircraft has no " + pointName.Replace("SP_", "")); return null; }
            return rig;
        }

        void Claim(StandOps o, OpsTask task, VehicleController v)
        {
            m_Reserved.Add(v);
            o.Equipment[task] = v;
            if (!o.Home.ContainsKey(task)) o.Home[task] = new Pose(v.transform.position, v.transform.rotation);
        }

        void Free(StandOps o, OpsTask task)
        {
            if (o.Equipment.TryGetValue(task, out var v) && v != null) m_Reserved.Remove(v);
            o.Equipment.Remove(task);
            o.Home.Remove(task);
        }

        IEnumerator ReturnHome(StandOps o, OpsTask task, VehicleController v, float speed)
        {
            Pose home;
            if (!o.Home.TryGetValue(task, out home)) yield break;
            var heading = home.rotation * Vector3.forward;
            yield return AutoDriver.DriveTo(v, home.position, heading, speed, 6f);
        }

        IEnumerator WaitAftZone(StandOps o, OpsTask task)
        {
            while (o.AftZone != null && !Equals(o.AftZone, task))
            {
                Set(o, task, TaskState.Working, "Waiting for the aft service area to be free");
                yield return new WaitForSeconds(1f);
            }
            o.AftZone = task;
        }

        static void ReleaseAftZone(StandOps o, OpsTask task)
        {
            if (Equals(o.AftZone, task)) o.AftZone = null;
        }

        /// <summary>A hose or cable drawn between two points (it sags; follows them as they move).</summary>
        CableRenderer Hose(StandOps o, OpsTask task, Transform from, Transform to, float width, float length)
        {
            DropHose(o, task);
            var go = new GameObject(task + " hose");
            var line = go.AddComponent<LineRenderer>();
            line.widthMultiplier = width;
            line.numCapVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (config != null && config.hoseMaterial != null) line.sharedMaterial = config.hoseMaterial;
            var cable = go.AddComponent<CableRenderer>();
            cable.Connect(from, to, length);
            o.Hoses[task] = go;
            return cable;
        }

        static void DropHose(StandOps o, OpsTask task)
        {
            if (o.Hoses.TryGetValue(task, out var go) && go != null) Destroy(go);
            o.Hoses.Remove(task);
        }

        static Transform Anchor(Transform parent, string name)
        {
            var existing = parent.Find(name);
            if (existing != null) return existing;
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        static Vector3 Ground(Vector3 p) => new Vector3(p.x, 0f, p.z);
    }
}
