using System;
using System.Collections.Generic;
using CoreSystems.Platform;
using CoreSystems.Projectiles;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using VRage;
using VRageMath;
using static CoreSystems.WeaponRandomGenerator;

namespace CoreSystems.Support
{
    public partial class Ai
    {
        internal void CompChange(bool add, CoreComponent comp)
        {
            int idx;
            switch (comp.Type)
            {
                case CoreComponent.CompType.Weapon:
                    var wComp = (Weapon.WeaponComponent)comp;

                    if (comp.TypeSpecific != CoreComponent.CompTypeSpecific.Phantom)
                    {
                        if (add)
                        {
                            if (WeaponIdx.ContainsKey(wComp))
                            {
                                Log.Line($"CompAddFailed:<{wComp.CoreEntity.EntityId}> - comp({wComp.CoreEntity.DebugName}[{wComp.SubtypeName}]) already existed in {TopEntity.DebugName}");
                                return;
                            }

                            WeaponIdx.Add(wComp, WeaponComps.Count);
                            WeaponComps.Add(wComp);
                        }
                        else
                        {
                            if (!WeaponIdx.TryGetValue(wComp, out idx))
                            {
                                Log.Line($"CompRemoveFailed: <{wComp.CoreEntity.EntityId}> - {WeaponComps.Count}[{WeaponIdx.Count}]({CompBase.Count}) - {WeaponComps.Contains(wComp)}[{WeaponComps.Count}] - {Session.EntityAIs[wComp.TopEntity].CompBase.ContainsKey(wComp.CoreEntity)} - {Session.EntityAIs[wComp.TopEntity].CompBase.Count} ");
                                return;
                            }

                            WeaponComps.RemoveAtFast(idx);
                            if (idx < WeaponComps.Count)
                                WeaponIdx[WeaponComps[idx]] = idx;
                            WeaponIdx.Remove(wComp);
                        }
                    }
                    else
                    {
                        if (add)
                        {
                            if (PhantomIdx.ContainsKey(wComp))
                            {
                                Log.Line($"CompAddFailed:<{wComp.CoreEntity.EntityId}> - comp({wComp.CoreEntity.DebugName}[{wComp.SubtypeName}]) already existed in {TopEntity.DebugName}");
                                return;
                            }

                            PhantomIdx.Add(wComp, PhantomComps.Count);
                            PhantomComps.Add(wComp);
                        }
                        else
                        {
                            if (!PhantomIdx.TryGetValue(wComp, out idx))
                            {
                                Log.Line($"CompRemoveFailed: <{wComp.CoreEntity.EntityId}> - {WeaponComps.Count}[{PhantomIdx.Count}]({CompBase.Count}) - {PhantomComps.Contains(wComp)}[{PhantomComps.Count}] - {Session.EntityAIs[wComp.TopEntity].CompBase.ContainsKey(wComp.CoreEntity)} - {Session.EntityAIs[wComp.TopEntity].CompBase.Count} ");
                                return;
                            }

                            PhantomComps.RemoveAtFast(idx);
                            if (idx < SupportComps.Count)
                                PhantomIdx[PhantomComps[idx]] = idx;
                            PhantomIdx.Remove(wComp);
                        }
                    }


                    break;
                case CoreComponent.CompType.Upgrade:
                    var uComp = (Upgrade.UpgradeComponent)comp;

                    if (add)
                    {
                        if (UpgradeIdx.ContainsKey(uComp))
                        {
                            Log.Line($"CompAddFailed:<{uComp.CoreEntity.EntityId}> - comp({uComp.CoreEntity.DebugName}[{uComp.SubtypeName}]) already existed in {TopEntity.DebugName}");
                            return;
                        }

                        UpgradeIdx.Add(uComp, UpgradeComps.Count);
                        UpgradeComps.Add(uComp);
                    }
                    else
                    {
                        if (!UpgradeIdx.TryGetValue(uComp, out idx))
                        {
                            Log.Line($"CompRemoveFailed: <{uComp.CoreEntity.EntityId}> - {WeaponComps.Count}[{UpgradeIdx.Count}]({CompBase.Count}) - {UpgradeComps.Contains(uComp)}[{WeaponComps.Count}] - {Session.EntityAIs[uComp.TopEntity].CompBase.ContainsKey(uComp.CoreEntity)} - {Session.EntityAIs[uComp.TopEntity].CompBase.Count} ");
                            return;
                        }

                        UpgradeComps.RemoveAtFast(idx);
                        if (idx < UpgradeComps.Count)
                            UpgradeIdx[UpgradeComps[idx]] = idx;
                        UpgradeIdx.Remove(uComp);
                    }


                    break;
                case CoreComponent.CompType.Support:

                    var sComp = (SupportSys.SupportComponent)comp;
                    if (add)
                    {
                        if (SupportIdx.ContainsKey(sComp))
                        {
                            Log.Line($"CompAddFailed:<{sComp.CoreEntity.EntityId}> - comp({sComp.CoreEntity.DebugName}[{sComp.SubtypeName}]) already existed in {TopEntity.DebugName}");
                            return;
                        }
                        SupportIdx.Add(sComp, SupportComps.Count);
                        SupportComps.Add(sComp);
                    }
                    else
                    {
                        if (!SupportIdx.TryGetValue(sComp, out idx))
                        {
                            Log.Line($"CompRemoveFailed: <{sComp.CoreEntity.EntityId}> - {WeaponComps.Count}[{SupportIdx.Count}]({CompBase.Count}) - {SupportComps.Contains(sComp)}[{SupportComps.Count}] - {Session.EntityAIs[sComp.TopEntity].CompBase.ContainsKey(sComp.CoreEntity)} - {Session.EntityAIs[sComp.TopEntity].CompBase.Count} ");
                            return;
                        }

                        SupportComps.RemoveAtFast(idx);
                        if (idx < SupportComps.Count)
                            SupportIdx[SupportComps[idx]] = idx;
                        SupportIdx.Remove(sComp);
                    }
                    break;
            }
        }

        private static int[] GetDeck(ref int[] deck, ref int prevDeckLen, int firstCard, int cardsToSort, int cardsToShuffle, ref XorShiftRandomStruct rng)
        {
            var count = cardsToSort - firstCard;
            if (prevDeckLen < count)
            {
                deck = new int[count];
                prevDeckLen = count;
            }

            for (int i = 0; i < count; i++)
            {
                var j = i < cardsToShuffle ? rng.Range(0, i + 1) : i;
                deck[i] = deck[j];
                deck[j] = firstCard + i;
            }
            return deck;
        }

        internal void ComputeAccelSphere()
        {
            NearByEntityCache.Clear();
            if (MarkedForClose) return;

            AccelChecked = true;

            var numOfEntities = NearByEntities > 0 ? NearByEntities : 1f;
            var ratio = (MyProjectiles / numOfEntities) / 10f;
            var checkVol = Math.Max(ratio > 1 ? ScanVolume.Radius : ScanVolume.Radius * ratio, 500f);
            NearByEntitySphere = new BoundingSphereD(TopEntity.PositionComp.WorldAABB.Center, checkVol);
            var qType = ClosestStaticSqr < (checkVol * 2) * (checkVol * 2) ? MyEntityQueryType.Both : MyEntityQueryType.Dynamic;
            MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref NearByEntitySphere, NearByEntityCache, qType);
        }

        internal List<Projectile> GetProCache()
        {
            if (LiveProjectileTick > _pCacheTick) {
                ProjetileCache.Clear();
                ProjetileCache.AddRange(LiveProjectile);
                _pCacheTick = LiveProjectileTick;
            }
            return ProjetileCache;
        }

        private void WeaponShootOff()
        {
            for (int i = 0; i < WeaponComps.Count; i++) {

                var comp = WeaponComps[i];
                for (int x = 0; x < comp.Collection.Count; x++) {
                    var w = comp.Collection[x];
                    w.StopReloadSound();
                    w.StopShooting();
                }
            }
        }

        internal void UpdateGridPower()
        {
            try
            {
                GridMap map;
                bool powered = false;
                if (Session.GridDistributors.TryGetValue(GridEntity, out map))
                {
                    PowerDistributor = map.FakeController?.GridResourceDistributor;
                    if (PowerDistributor != null && PowerDistributor.SourcesEnabled != MyMultipleEnabledEnum.NoObjects && PowerDistributor.ResourceState != MyResourceStateEnum.NoPower)
                    {
                        GridMaxPower = PowerDistributor.MaxAvailableResourceByType(GId, GridEntity);
                        GridCurrentPower = PowerDistributor.TotalRequiredInputByType(GId, GridEntity);
                        if (Session.ShieldApiLoaded && ShieldBlock != null)
                        {
                            var shieldPower = Session.SApi.GetPowerUsed(ShieldBlock);
                            GridCurrentPower -= shieldPower;
                        }
                        powered = true;
                    }
                }

                if (!powered)
                {

                    if (HadPower)
                        WeaponShootOff();

                    GridCurrentPower = 0;
                    GridMaxPower = 0;
                    GridAvailablePower = 0;

                    HadPower = HasPower;
                    HasPower = false;
                    return;
                }

                if (Session.Tick60) {

                    BatteryMaxPower = 0;
                    BatteryCurrentOutput = 0;
                    BatteryCurrentInput = 0;

                    foreach (var battery in Batteries) {

                        if (!battery.IsWorking) continue;
                        var currentInput = battery.CurrentInput;
                        var currentOutput = battery.CurrentOutput;
                        var maxOutput = battery.MaxOutput;

                        if (currentInput > 0) {
                            BatteryCurrentInput += currentInput;
                            if (battery.IsCharging) BatteryCurrentOutput -= currentInput;
                            else BatteryCurrentOutput -= currentInput;
                        }
                        BatteryMaxPower += maxOutput;
                        BatteryCurrentOutput += currentOutput;
                    }
                }

                GridAvailablePower = GridMaxPower - GridCurrentPower;

                GridCurrentPower += BatteryCurrentInput;
                GridAvailablePower -= BatteryCurrentInput;
                UpdatePowerSources = false;

                HadPower = HasPower;
                HasPower = GridMaxPower > 0;

                if (Session.Tick60 && HasPower) {
                    var nearMax = GridMaxPower * 0.97;
                    var halfMax = GridMaxPower * 0.5f;
                    if (GridCurrentPower > nearMax && GridAssignedPower > halfMax)
                        Charger.Rebalance = true;
                }
                if (Session.Tick20 && HasPower)
                {
                    if (Charger.TotalDesired > GridAssignedPower && GridAvailablePower > GridMaxPower * 0.1f)
                        Charger.Rebalance = true;
                }

                if (HasPower) return;
                if (HadPower)
                    WeaponShootOff();
            }
            catch (Exception ex) { Log.Line($"Exception in UpdateGridPower: {ex} - SessionNull{Session == null} - FakeShipControllerNull{FakeShipController == null} - PowerDistributorNull{PowerDistributor == null} - MyGridNull{TopEntity == null}", null, true); }
        }

        private void ForceCloseAiInventories()
        {
            foreach (var pair in InventoryMonitor)
                InventoryRemove(pair.Key, pair.Value);
            
            if (InventoryMonitor.Count > 0) {
                Log.Line($"Found stale inventories during AI close - failedToRemove:{InventoryMonitor.Count}");
                InventoryMonitor.Clear();
            }

        }
        
        internal void AiDelayedClose()
        {
            if (Session == null || TopEntity == null || Closed) {
                Log.Line($"AiDelayedClose: Session is null {Session == null} - Grid is null {TopEntity == null}  - Closed: {Closed}");
                return;
            }

            if (!ScanInProgress && Session.Tick - ProjectileTicker > 29 && AiMarkedTick != uint.MaxValue && Session.Tick - AiMarkedTick > 29) {

                using (DbLock.AcquireExclusiveUsing())
                {
                    if (ScanInProgress)
                        return;
                    Session.GridAiPool.Return(this);
                }
            }
        }

        internal void AiForceClose()
        {
            if (Session == null || TopEntity == null || Closed) {
                Log.Line($"AiDelayedClose: Session is null {Session == null} - Grid is null {TopEntity == null} - Closed: {Closed}");
                return;
            }

            RegisterMyGridEvents(false, true);
            Session.GridAiPool.Return(this);
        }

        internal void CleanSortedTargets()
        {
            for (int i = 0; i < SortedTargets.Count; i++)
            {
                var tInfo = SortedTargets[i];
                tInfo.Target = null;
                tInfo.MyAi = null;
                tInfo.TargetAi = null;
                Session.TargetInfoPool.Return(tInfo);
            }
            SortedTargets.Clear();
        }

        internal void CleanUp()
        {
            AiCloseTick = Session.Tick;

            TopEntity.Components.Remove<AiComponent>();

            if (Session.IsClient)
                Session.SendUpdateRequest(TopEntity.EntityId, PacketType.ClientAiRemove);

            Data.Repo.ControllingPlayers.Clear();
            Data.Repo.ActiveTerminal = 0;
            Charger.Clean();

            CleanSortedTargets();
            Construct.Clean();
            Obstructions.Clear();
            ObstructionsTmp.Clear();
            TargetAis.Clear();
            TargetAisTmp.Clear();
            EntitiesInRange.Clear();
            Batteries.Clear();
            NoTargetLos.Clear();
            Targets.Clear();

            WeaponComps.Clear();
            UpgradeComps.Clear();
            SupportComps.Clear();
            PhantomComps.Clear();
            WeaponIdx.Clear();
            SupportIdx.Clear();
            UpgradeIdx.Clear();
            PhantomIdx.Clear();
            CompBase.Clear();

            LiveProjectile.Clear();
            DeadProjectiles.Clear();
            NearByShieldsTmp.Clear();
            NearByFriendlyShields.Clear();
            StaticsInRange.Clear();
            StaticsInRangeTmp.Clear();
            TestShields.Clear();
            NewEntities.Clear();
            SubGridsRegistered.Clear();
            SourceCount = 0;
            PartCount = 0;
            AiOwner = 0;
            ProjectileTicker = 0;
            NearByEntities = 0;
            NearByEntitiesTmp = 0;
            MyProjectiles = 0;
            AccelChecked = false;
            PointDefense = false;
            FadeOut = false;
            SuppressMouseShoot = false;
            UpdatePowerSources = false;
            DbReady = false;
            AiInit = false;
            TouchingWater = false;
            BlockMonitoring = false;
            ShieldFortified = false;
            Data.Clean();

            MyShield = null;
            MyPlanetTmp = null;
            MyPlanet = null;
            TerminalSystem = null;
            LastTerminal = null;
            PowerDistributor = null;
            PowerBlock = null;
            TopEntity = null;
            PowerDistributor = null;
            Session = null;
            Closed = true;
            CanShoot = true;
            Version++;
        }
    }
}
