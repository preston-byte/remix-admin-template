using System;
using UnityEngine;

namespace GroundCrew.Player
{
    public enum MoveMode
    {
        /// <summary>Left stick walks smoothly, right stick forward teleports.</summary>
        SmoothAndTeleport = 0,
        /// <summary>Both sticks teleport only (most comfortable).</summary>
        TeleportOnly = 1,
    }

    public enum TurnMode
    {
        Snap = 0,
        Smooth = 1,
    }

    /// <summary>
    /// Player comfort options, persisted with PlayerPrefs. Applied to the XR rig by
    /// <see cref="LocomotionComfortApplier"/>; changed from the wrist tablet.
    /// </summary>
    public static class ComfortSettings
    {
        const string k_Move = "gc.comfort.move";
        const string k_Turn = "gc.comfort.turn";
        const string k_Snap = "gc.comfort.snap";
        const string k_Assist = "gc.driving.assist";
        static readonly int[] s_SnapAngles = { 30, 45, 90 };

        public static event Action Changed;

        public static MoveMode Move
        {
            get => (MoveMode)PlayerPrefs.GetInt(k_Move, (int)MoveMode.SmoothAndTeleport);
            set { PlayerPrefs.SetInt(k_Move, (int)value); Save(); }
        }

        public static TurnMode Turn
        {
            get => (TurnMode)PlayerPrefs.GetInt(k_Turn, (int)TurnMode.Snap);
            set { PlayerPrefs.SetInt(k_Turn, (int)value); Save(); }
        }

        public static int SnapAngle
        {
            get => PlayerPrefs.GetInt(k_Snap, 45);
            set { PlayerPrefs.SetInt(k_Snap, value); Save(); }
        }

        public static void CycleMove()
        {
            Move = Move == MoveMode.SmoothAndTeleport ? MoveMode.TeleportOnly : MoveMode.SmoothAndTeleport;
        }

        /// <summary>Snap 30 -> Snap 45 -> Snap 90 -> Smooth -> Snap 30 ...</summary>
        public static void CycleTurn()
        {
            if (Turn == TurnMode.Smooth)
            {
                PlayerPrefs.SetInt(k_Snap, s_SnapAngles[0]);
                Turn = TurnMode.Snap;
                return;
            }

            var index = Array.IndexOf(s_SnapAngles, SnapAngle);
            if (index < 0 || index == s_SnapAngles.Length - 1)
                Turn = TurnMode.Smooth;
            else
                SnapAngle = s_SnapAngles[index + 1];
        }

        /// <summary>
        /// Driving assists (default on): pressing the accelerator starts the engine, selects
        /// Drive and releases the parking brake; getting out or using a lift sets the brake.
        /// Off = realistic: every step is yours, and forgetting one is a mistake.
        /// </summary>
        public static bool DrivingAssists
        {
            get => PlayerPrefs.GetInt(k_Assist, 1) == 1;
            set { PlayerPrefs.SetInt(k_Assist, value ? 1 : 0); Save(); }
        }

        public static void ToggleDrivingAssists() => DrivingAssists = !DrivingAssists;

        public static string MoveLabel => Move == MoveMode.SmoothAndTeleport ? "Walk + Teleport" : "Teleport only";
        public static string TurnLabel => Turn == TurnMode.Smooth ? "Smooth turn" : $"Snap {SnapAngle}°";

        static void Save()
        {
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Changed = null;
    }
}
