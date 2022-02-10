using System;
using System.Collections.Generic;
using CoreSystems.Platform;
using Sandbox.Game.Entities;
using VRage.Game;
using VRage.Game.Entity;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using static CoreSystems.FocusData;
namespace CoreSystems.Support
{
    public partial class Ai
    {
        public void SubGridChanges(bool clean = false, bool dupCheck = false)
        {
            Construct.SubGridUpdateTick = Session.Tick;
            SubGridCache.Clear();
            foreach (var grid in GridMap.GroupMap.Construct.Keys) {

                SubGridCache.Add(grid);
                if (grid == TopEntity || SubGridsRegistered.ContainsKey(grid)) continue;
                RegisterSubGrid(grid, dupCheck);

            }

            foreach (var sub in SubGridsRegistered.Keys) {

                if (sub == TopEntity)
                    continue;

                if (!GridMap.GroupMap.Construct.ContainsKey(sub))
                    UnRegisterSubGrid(sub);
            }

            if (!clean)
                UpdateRoot();
        }

        public void UpdateRoot()
        {
            Construct.Refresh(this, Constructs.RefreshCaller.SubGridChange);
            
            foreach (var grid in SubGridCache) {
                
                if (Construct.RootAi != null)
                    Session.EntityToMasterAi[grid] = Construct.RootAi;
                else Log.Line("Construct.RootAi is null");
            }

            Constructs.UpdatePlayerStates(Construct.RootAi);

        }

        public void RegisterSubGrid(MyCubeGrid grid, bool dupCheck = false)
        {
            if (dupCheck && SubGridsRegistered.ContainsKey(grid))
                Log.Line($"sub Grid Already Registered: [Main]:{grid == TopEntity}");

            grid.Flags |= (EntityFlags)(1 << 31);
            grid.OnFatBlockAdded += FatBlockAdded;
            grid.OnFatBlockRemoved += FatBlockRemoved;

            SubGridsRegistered[grid] = byte.MaxValue;

            foreach (var cube in grid.GetFatBlocks()) {

                var battery = cube as MyBatteryBlock;
                if (battery != null || cube.HasInventory)
                {
                    FatBlockAdded(cube);
                }
            }
        }

        public void UnRegisterSubGrid(MyCubeGrid grid, bool clean = false)
        {
            if (!SubGridsRegistered.ContainsKey(grid)) {
                Log.Line($"sub Grid Already UnRegistered: [Main]:{grid == TopEntity}");
            }

            SubGridsRegistered.Remove(grid);
            grid.OnFatBlockAdded -= FatBlockAdded;
            grid.OnFatBlockRemoved -= FatBlockRemoved;

            foreach (var cube in grid.GetFatBlocks()) {
                
                var battery = cube as MyBatteryBlock;
                if (InventoryMonitor.ContainsKey(cube) || battery != null && Batteries.Contains(battery))
                {
                    FatBlockRemoved(cube);
                }
            }

            Ai removeAi;
            if (!Session.EntityAIs.ContainsKey(grid))
                Session.EntityToMasterAi.TryRemove(grid, out removeAi);
        }

        public void CleanSubGrids()
        {
            foreach (var grid in SubGridCache) {
                if (grid == TopEntity) continue;
                UnRegisterSubGrid(grid, true);
            }

            SubGridCache.Clear();
            SubGridsChanged = false;
        } 

        public class Constructs
        {
            internal readonly HashSet<MyDefinitionId> RecentItems = new HashSet<MyDefinitionId>(MyDefinitionId.Comparer);
            internal readonly HashSet<Weapon> OutOfAmmoWeapons = new HashSet<Weapon>();
            internal readonly List<Ai> RefreshedAis = new List<Ai>();
            internal readonly Dictionary<MyStringHash, int> Counter = new Dictionary<MyStringHash, int>(MyStringHash.Comparer);
            internal readonly Focus Focus = new Focus();
            internal readonly ConstructData Data = new ConstructData();
            internal readonly Dictionary<long, PlayerController> ControllingPlayers = new Dictionary<long, PlayerController>();

            internal readonly HashSet<MyEntity> PreviousTargets = new HashSet<MyEntity>();
            internal readonly RunningAverage DamageAverage = new RunningAverage(10);
            internal float OptimalDps;
            internal int BlockCount;
            internal Ai RootAi;
            internal Ai LargestAi;
            internal bool NewInventoryDetected;
            internal int DroneCount;
            internal uint LastDroneTick;
            internal uint LastEffectUpdateTick;
            internal uint SubGridUpdateTick;
            internal bool DroneAlert;

            internal double TotalEffect;
            internal double PreviousTotalEffect;
            internal double AddEffect;
            internal double AverageEffect;
            internal double MaxLockRange;
            internal enum RefreshCaller
            {
                Init,
                SubGridChange,
            }

            internal enum UpdateType
            {
                Full,
                Focus,
                None,
            }

            internal void Refresh(Ai ai, RefreshCaller caller)
            {
                if (ai.Session.IsServer && RootAi.Construct.RecentItems.Count > 0) 
                    CheckEmptyWeapons();

                OptimalDps = 0;
                BlockCount = 0;
                if (ai.TopEntity != null && ai.Session.GridToInfoMap.ContainsKey(ai.TopEntity)) {
                    Ai leadingAi = null;
                    Ai largestAi = null;
                    int leadingBlocks = 0;
                    var maxLockRange = 0d;
                    foreach (var grid in ai.SubGridCache) {

                        Ai thisAi;
                        if (ai.Session.EntityAIs.TryGetValue(grid, out thisAi)) {
                            
                            if (leadingAi == null)
                                leadingAi = thisAi;
                            else  {

                                if (leadingAi.TopEntity.EntityId > grid.EntityId)
                                    leadingAi = thisAi;
                            }
                        }
                        if (ai.Session.GridToInfoMap.ContainsKey(grid)) {
                            var blockCount = ai.Session.GridToInfoMap[grid].MostBlocks;
                            if (blockCount > leadingBlocks)
                            {
                                leadingBlocks = blockCount;
                                largestAi = thisAi;
                            }
                            BlockCount += blockCount;

                            if (thisAi != null)
                            {
                                OptimalDps += thisAi.OptimalDps;
                                if (thisAi.Construct.MaxLockRange > maxLockRange)
                                    maxLockRange = thisAi.Construct.MaxLockRange;
                            }
                        }
                        else Log.Line($"ConstructRefresh Failed sub no GridMap, sub is caller:{grid == ai.TopEntity}");
                    }
                    RootAi = leadingAi;
                    LargestAi = largestAi;
                    if (RootAi == null) {
                        //Log.Line($"[rootAi is null in Update] - caller:{caller}, forcing rootAi to caller - inGridTarget:{ai.Session.EntityAIs.ContainsKey(ai.TopEntity)} -  myGridMarked:{ai.TopEntity.MarkedForClose} - aiMarked:{ai.MarkedForClose} - inScene:{ai.TopEntity.InScene} - lastClosed:{ai.AiCloseTick} - aiSpawned:{ai.AiSpawnTick} - diff:{ai.AiSpawnTick - ai.AiCloseTick} - sinceSpawn:{ai.Session.Tick - ai.AiSpawnTick} - entId:{ai.TopEntity.EntityId}");
                        RootAi = ai;
                    }

                    if (LargestAi == null) {
                        LargestAi = ai;
                        if (ai.Construct.MaxLockRange > maxLockRange)
                            maxLockRange = ai.Construct.MaxLockRange;
                    }

                    RootAi.Construct.MaxLockRange = maxLockRange;
                    BuildAiListAndCounters(ai);

                    return;
                }
                if (ai.TopEntity != null && ai.AiType != AiTypes.Grid)
                {
                    RootAi = ai;
                    LargestAi = ai;
                    ai.Session.EntityToMasterAi[RootAi.TopEntity] = RootAi;
                    UpdatePlayerStates(RootAi);

                    return;
                }
                Log.Line($"ConstructRefresh Failed main Ai no GridMap: {caller} - Marked: {ai.TopEntity?.MarkedForClose}");
                RootAi = null;
                LargestAi = null;
            }

            internal void UpdateEffect(uint tick)
            {
                var add = TotalEffect - PreviousTotalEffect;
                AddEffect = add > 0 ? add : AddEffect;
                AverageEffect = DamageAverage.Add((int)add);
                PreviousTotalEffect = TotalEffect;
                LastEffectUpdateTick = tick;
            }

            internal void DroneCleanup()
            {
                DroneAlert = false;
                DroneCount = 0;
            }

            internal void UpdateConstruct(UpdateType type, bool sync = true)
            {
                switch (type)
                {
                    case UpdateType.Full:
                    {
                        UpdateLeafs();
                        if (RootAi.Session.MpActive && RootAi.Session.IsServer && sync)
                            RootAi.Session.SendConstruct(RootAi);
                        break;
                    }
                    case UpdateType.Focus:
                    {
                        UpdateLeafFoci();
                        if (RootAi.Session.MpActive && RootAi.Session.IsServer && sync)
                            RootAi.Session.SendConstructFoci(RootAi);
                        break;
                    }
                }
            }

            internal void NetRefreshAi()
            {
                if (RootAi.AiType == AiTypes.Grid) {

                    foreach (var sub in RootAi.SubGridCache) {

                        Ai ai;
                        if (RootAi.Session.EntityAIs.TryGetValue(sub, out ai))
                        {
                            ai.AiSleep = false;

                            if (ai.Session.MpActive)
                                ai.Session.SendAiData(ai);
                        }
                    }
                }
                else {
                    RootAi.AiSleep = false;

                    if (RootAi.Session.MpActive)
                        RootAi.Session.SendAiData(RootAi);
                }
            }

            internal static void UpdatePlayerLockState(Ai rootAi, long playerId)
            {
                PlayerMap playerMap;
                if (!rootAi.Session.Players.TryGetValue(playerId, out playerMap))
                    Log.Line($"failed to get PlayerMap");
                else if (playerMap.Player.Character != null && playerMap.Player.Character.Components.TryGet(out playerMap.TargetFocus) && playerMap.Player.Character.Components.TryGet(out playerMap.TargetLock))
                {
                    playerMap.TargetFocusDef.AngularToleranceFromCrosshair = 25;
                    //playerMap.TargetFocusDef.FocusSearchMaxDistance = !setDefault ? RootAi.Construct.MaxLockRange : 2000;
                    playerMap.TargetFocusDef.FocusSearchMaxDistance = 0; //temp
                    playerMap.TargetFocus.Init(playerMap.TargetFocusDef);
                }
                else
                    Log.Line($"failed to get and set player focus and lock");
            }

            internal static void UpdatePlayerStates(Ai rootAi)
            {
                var rootConstruct = rootAi.Construct;
                foreach (var sub in rootAi.SubGridCache)
                {
                    Ai subAi;
                    if (rootAi.Session.EntityAIs.TryGetValue(sub, out subAi))
                        subAi.Construct.ControllingPlayers.Clear();

                    GridMap gridMap;
                    if (rootAi.Session.GridToInfoMap.TryGetValue(sub, out gridMap))
                    {
                        foreach (var m in gridMap.PlayerControllers)
                        {
                            rootConstruct.ControllingPlayers.Add(m.Key, m.Value);
                            UpdatePlayerLockState(rootAi, m.Key);
                        }
                    }
                }
            }

            internal static void BuildAiListAndCounters(Ai cAi)
            {
                cAi.Construct.RefreshedAis.Clear();
                cAi.Construct.RefreshedAis.Add(cAi);

                if (cAi.SubGridCache.Count > 1) {
                    foreach (var sub in cAi.SubGridCache) {
                        if (sub == null || sub == cAi.TopEntity)
                            continue;

                        Ai subAi;
                        if (cAi.Session.EntityAIs.TryGetValue(sub, out subAi))
                            cAi.Construct.RefreshedAis.Add(subAi);
                    }
                }

                for (int i = 0; i < cAi.Construct.RefreshedAis.Count; i++) {

                    var checkAi = cAi.Construct.RefreshedAis[i];
                    checkAi.Construct.Counter.Clear();

                    for (int x = 0; x < cAi.Construct.RefreshedAis.Count; x++) {
                        foreach (var wc in cAi.Construct.RefreshedAis[x].PartCounting)
                            checkAi.Construct.AddWeaponCount(wc.Key, wc.Value.Current);
                    }
                }
            }


            internal void AddWeaponCount(MyStringHash weaponHash, int incrementBy = 1)
            {
                if (!Counter.ContainsKey(weaponHash))
                    Counter.Add(weaponHash, incrementBy);
                else Counter[weaponHash] += incrementBy;
            }

            internal int GetPartCount(MyStringHash weaponHash)
            {
                int value;
                return Counter.TryGetValue(weaponHash, out value) ? value : 0;
            }

            internal void UpdateLeafs()
            {
                foreach (var sub in RootAi.SubGridCache)
                {
                    if (RootAi.TopEntity == sub)
                        continue;

                    Ai ai;
                    if (RootAi.Session.EntityAIs.TryGetValue(sub, out ai))
                    {
                        ai.Construct.Data.Repo.Sync(ai.Construct, RootAi.Construct.Data.Repo, true);
                    }
                }
            }

            internal void UpdateLeafFoci()
            {
                foreach (var sub in RootAi.SubGridCache)
                {
                    if (RootAi.TopEntity == sub)
                        continue;

                    Ai ai;
                    if (RootAi.Session.EntityAIs.TryGetValue(sub, out ai))
                        ai.Construct.Data.Repo.FocusData.Sync(ai, RootAi.Construct.Data.Repo.FocusData);
                }
            }

            internal void CheckEmptyWeapons()
            {
                foreach (var w in OutOfAmmoWeapons)
                {
                    if (RecentItems.Contains(w.ActiveAmmoDef.AmmoDefinitionId))
                        w.CheckInventorySystem = true;
                }
                RecentItems.Clear();
            }

            internal void CheckForMissingAmmo()
            {
                NewInventoryDetected = false;
                foreach (var w in RootAi.Construct.OutOfAmmoWeapons)
                    w.CheckInventorySystem = true;
            }
            
            internal void Init(Ai ai)
            {
                RootAi = ai;
                Data.Init(ai);
            }
            
            internal void Clean()
            {
                Data.Clean();
                OptimalDps = 0;
                BlockCount = 0;
                AverageEffect = 0;
                TotalEffect = 0;
                PreviousTotalEffect = 0;
                RootAi = null;
                LargestAi = null;
                Counter.Clear();
                RefreshedAis.Clear();
                PreviousTargets.Clear();
                ControllingPlayers.Clear();
            }
        }
    }

    public class Focus
    {
        public long OldTarget;
        public LockModes OldLocked;

        public uint LastUpdateTick;
        public bool OldHasFocus;
        public float OldDistToNearestFocusSqr;
        
        public bool ChangeDetected(Ai ai)
        {
            var fd = ai.Construct.Data.Repo.FocusData;
            var forceUpdate = LastUpdateTick == 0 || ai.Session.Tick - LastUpdateTick > 600;
            if (forceUpdate || fd.Target != OldTarget || fd.Locked != OldLocked || fd.HasFocus != OldHasFocus || Math.Abs(fd.DistToNearestFocusSqr - OldDistToNearestFocusSqr) > 0) {

                OldTarget = fd.Target;
                OldLocked = fd.Locked;
                OldHasFocus = fd.HasFocus;
                OldDistToNearestFocusSqr = fd.DistToNearestFocusSqr;
                LastUpdateTick = ai.Session.Tick;
                return true;
            }

            return false;
        }

        internal void ServerAddFocus(MyEntity target, Ai ai)
        {
            var session = ai.Session;
            var fd = ai.Construct.Data.Repo.FocusData;
            if (fd.Target != target.EntityId)
            {
                fd.Target = target.EntityId;
                ai.TargetResetTick = session.Tick + 1;
            }
            ServerIsFocused(ai);

            ai.Construct.UpdateConstruct(Ai.Constructs.UpdateType.Focus, ChangeDetected(ai));
        }

        internal void RequestAddFocus(MyEntity target, Ai ai)
        {
            if (ai.Session.IsServer)
                ServerAddFocus(target, ai);
            else
                ai.Session.SendFocusTargetUpdate(ai, target.EntityId);
        }

        internal void ServerCycleLock(Ai ai)
        {
            var session = ai.Session;
            var fd = ai.Construct.Data.Repo.FocusData;
            var modeCount = Enum.GetNames(typeof(LockModes)).Length;

            var nextMode = (int)fd.Locked + 1 < modeCount ? fd.Locked + 1 : 0;
            fd.Locked = nextMode;
            ai.TargetResetTick = session.Tick + 1;
            ServerIsFocused(ai);

            ai.Construct.UpdateConstruct(Ai.Constructs.UpdateType.Focus, ChangeDetected(ai));
        }

        internal void RequestAddLock(Ai ai)
        {
            if (ai.Session.IsServer)
                ServerCycleLock(ai);
            else
                ai.Session.SendFocusLockUpdate(ai);
        }

        internal void ServerReleaseActive(Ai ai)
        {
            var fd = ai.Construct.Data.Repo.FocusData;

            fd.Target = -1;
            fd.Locked = LockModes.None;

            ServerIsFocused(ai);

            ai.Construct.UpdateConstruct(Ai.Constructs.UpdateType.Focus, ChangeDetected(ai));
        }

        internal void RequestReleaseActive(Ai ai)
        {
            if (ai.Session.IsServer)
                ServerReleaseActive(ai);
            else
                ai.Session.SendReleaseActiveUpdate(ai);

        }

        internal bool ServerIsFocused(Ai ai)
        {
            var fd = ai.Construct.Data.Repo.FocusData;

            if (fd.Target > 0 && MyEntities.GetEntityById(fd.Target) != null) {
                fd.HasFocus = true;
                return true;
            }

            fd.Target = -1;
            fd.Locked = LockModes.None;
            fd.HasFocus = false;

            return false;
        }

        internal bool ClientIsFocused(Ai ai)
        {
            var fd = ai.Construct.Data.Repo.FocusData;

            if (ai.Session.IsServer)
                return ServerIsFocused(ai);

            return fd.Target > 0 && MyEntities.GetEntityById(fd.Target) != null;
        }

        internal bool GetPriorityTarget(Ai ai, out MyEntity target)
        {
            var fd = ai.Construct.Data.Repo.FocusData;

            if (fd.Target > 0 && MyEntities.TryGetEntityById(fd.Target, out target, true))
                return true;

            if (MyEntities.TryGetEntityById(fd.Target, out target, true))
                return true;

            target = null;
            return false;
        }

        internal void ReassignTarget(MyEntity target, Ai ai)
        {
            var fd = ai.Construct.Data.Repo.FocusData;

            if (target == null || target.MarkedForClose) return;
            fd.Target = target.EntityId;
            ServerIsFocused(ai);

            ai.Construct.UpdateConstruct(Ai.Constructs.UpdateType.Focus, ChangeDetected(ai));
        }

        internal bool FocusInRange(Weapon w)
        {

            if (w.PosChangedTick != w.Comp.Session.Tick)
                w.UpdatePivotPos();

            var fd = w.Comp.Ai.Construct.Data.Repo.FocusData;
            
            fd.DistToNearestFocusSqr = float.MaxValue;
                if (fd.Target <= 0)
                    return false;

            MyEntity target;
            if (MyEntities.TryGetEntityById(fd.Target, out target))
            {
                var sphere = target.PositionComp.WorldVolume;
                var distSqr = (float)MyUtils.GetSmallestDistanceToSphere(ref w.MyPivotPos, ref sphere);
                distSqr *= distSqr;
                if (distSqr < fd.DistToNearestFocusSqr)
                    fd.DistToNearestFocusSqr = distSqr;
            }

            return fd.DistToNearestFocusSqr <= w.MaxTargetDistanceSqr;
        }

        internal bool EntityIsFocused(Ai ai, MyEntity entToCheck)
        {
            var targets = ai.Construct?.Data?.Repo?.FocusData?.Target;

            if (targets != null)
            {
                var tId = targets ?? 0;
                if (tId == 0)
                    return false;

                MyEntity target;
                if (MyEntities.TryGetEntityById(tId, out target) && target == entToCheck)
                    return true;
            }
            return false;
        }

        internal bool ValidFocusTarget(Weapon w)
        {
            var targets = w.Comp.Ai.Construct.Data.Repo.FocusData?.Target;

            var targetEnt = w.Target.TargetEntity;
            if (w.PosChangedTick != w.Comp.Session.Tick)
                w.UpdatePivotPos();

            if (targets != null && targetEnt != null)
            {
                var tId = targets ?? 0;
                if (tId == 0) return false;

                var block = targetEnt as MyCubeBlock;

                MyEntity target;
                if (MyEntities.TryGetEntityById(tId, out target) && (target == targetEnt || block != null && target == block.CubeGrid))
                {
                    var worldVolume = target.PositionComp.WorldVolume;
                    var targetPos = worldVolume.Center;
                    var tRadius = worldVolume.Radius;
                    var maxRangeSqr = tRadius + w.MaxTargetDistance;
                    var minRangeSqr = tRadius + w.MinTargetDistance;

                    maxRangeSqr *= maxRangeSqr;
                    minRangeSqr *= minRangeSqr;
                    double rangeToTarget;
                    Vector3D.DistanceSquared(ref targetPos, ref w.MyPivotPos, out rangeToTarget);
                    
                    if (rangeToTarget <= maxRangeSqr && rangeToTarget >= minRangeSqr)
                    {
                        var overrides = w.Comp.Data.Repo.Values.Set.Overrides;
                        if (overrides.FocusSubSystem && overrides.SubSystem != WeaponDefinition.TargetingDef.BlockTypes.Any && block != null && !w.ValidSubSystemTarget(block, overrides.SubSystem))
                            return false;

                        if (w.System.LockOnFocus)
                        {
                            var targetSphere = targetEnt.PositionComp.WorldVolume;
                            targetSphere.Center = targetEnt.PositionComp.WorldAABB.Center;
                            w.AimCone.ConeDir = w.MyPivotFwd;
                            w.AimCone.ConeTip = w.BarrelOrigin;
                            return MathFuncs.TargetSphereInCone(ref targetSphere, ref w.AimCone);
                        }
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
