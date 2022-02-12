using System.Collections.Concurrent;
using System.Collections.Generic;
using CoreSystems.Support;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;
namespace CoreSystems.Platform
{
    public partial class ControlSys : Part
    {

        internal readonly HashSet<IMySlimBlock> SuppotedBlocks = new HashSet<IMySlimBlock>();
        internal readonly Dictionary<IMySlimBlock, BlockBackup> BlockColorBackup = new Dictionary<IMySlimBlock, BlockBackup>();

        internal readonly ControlInfo Info = new ControlInfo();
        internal readonly ControlComponent Comp;
        internal readonly ControlSystem System;
        internal readonly MyStringHash PartHash;

        internal Dictionary<IMyMotorStator, RotorMap> TurretMap = new Dictionary<IMyMotorStator, RotorMap>();
        internal IMyMotorStator BaseRotor;
        internal IMyMotorStator TrackingRotor;
        internal Weapon TrackingWeapon;
        internal Dummy TrackingScope;

        internal int ActiveSubRotors;
        internal bool BaseHasTop;
        internal bool NoValidWeapons;

        internal ProtoControlPartState PartState;

        internal ControlSys(ControlSystem system, ControlComponent comp, int partId)
        {
            System = system;
            Comp = comp;

            Init(comp, system, partId);
            PartHash = Comp.Structure.PartHashes[partId];

            //_activeControls = GetControlCollection();

            if (!BaseComp.Ai.BlockMonitoring)
                BaseComp.Ai.DelayedEventRegistration(true);
        }

        internal class RotorMap
        {
            internal Ai Ai;
            internal Weapon PrimaryWeapon;
            internal Dummy Scope;
        }
    }
}
