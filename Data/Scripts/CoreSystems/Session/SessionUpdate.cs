using System.Collections.Generic;
using System.Diagnostics;
using CoreSystems.Platform;
using CoreSystems.Projectiles;
using CoreSystems.Support;
using VRage.Game;
using VRageMath;
using static CoreSystems.Support.Target;
using static CoreSystems.Support.CoreComponent.Start;
using static CoreSystems.Support.CoreComponent.TriggerActions;
using static CoreSystems.Support.WeaponDefinition.HardPointDef.HardwareDef;
using static CoreSystems.Support.WeaponDefinition.AmmoDef.TrajectoryDef.GuidanceType;
using static CoreSystems.ProtoWeaponState;
namespace CoreSystems
{
    public partial class Session
    {
        private void AiLoop()
        { //Fully Inlined due to keen's mod profiler
            foreach (var ai in EntityAIs.Values)
            {

                ///
                /// GridAi update section
                ///
                ai.MyProjectiles = 0;
                ai.ProInMinCacheRange = 0;
                ai.AccelChecked = false;

                if (ai.MarkedForClose || !ai.AiInit || ai.TopEntity == null || ai.Construct.RootAi == null || ai.TopEntity.MarkedForClose)
                    continue;

                ai.Concealed = ((uint)ai.TopEntity.Flags & 4) > 0;
                if (ai.Concealed)
                    continue;

                if (!ai.ScanInProgress && Tick - ai.TargetsUpdatedTick > 100 && DbTask.IsComplete)
                    ai.RequestDbUpdate();

                if (ai.DeadProjectiles.Count > 0) {
                    for (int i = 0; i < ai.DeadProjectiles.Count; i++) ai.LiveProjectile.Remove(ai.DeadProjectiles[i]);
                    ai.DeadProjectiles.Clear();
                    ai.LiveProjectileTick = Tick;
                }
                var enemyProjectiles = ai.LiveProjectile.Count > 0;
                ai.CheckProjectiles = Tick - ai.NewProjectileTick <= 1;

                if (ai.AiType == Ai.AiTypes.Grid && (ai.UpdatePowerSources || !ai.HadPower && ai.GridEntity.IsPowered || ai.HasPower && !ai.GridEntity.IsPowered || Tick10))
                    ai.UpdateGridPower();

                if (ai.AiType == Ai.AiTypes.Grid && !ai.HasPower || Settings.Enforcement.ServerSleepSupport && IsServer && ai.AwakeComps == 0 && ai.WeaponsTracking == 0 && ai.SleepingComps > 0 && !ai.CheckProjectiles && ai.AiSleep && !ai.DbUpdated) 
                    continue;

                if (Tick60 && ai.AiType == Ai.AiTypes.Grid && ai.BlockChangeArea != BoundingBox.Invalid)
                {
                    ai.BlockChangeArea.Min *= ai.GridEntity.GridSize;
                    ai.BlockChangeArea.Max *= ai.GridEntity.GridSize;
                }

                if (IsServer) {

                    if (ai.Construct.RootAi.Construct.NewInventoryDetected)
                        ai.Construct.RootAi.Construct.CheckForMissingAmmo();
                    else if (Tick60 && ai.Construct.RootAi.Construct.RecentItems.Count > 0)
                        ai.Construct.RootAi.Construct.CheckEmptyWeapons();
                }

                ///
                /// Upgrade update section
                ///
                for (int i = 0; i < ai.UpgradeComps.Count; i++)
                {
                    var uComp = ai.UpgradeComps[i];
                    if (uComp.Status != Started)
                        uComp.HealthCheck();

                    if (ai.DbUpdated || !uComp.UpdatedState)
                    {
                        uComp.DetectStateChanges();
                    }

                    if (uComp.Platform.State != CorePlatform.PlatformState.Ready || uComp.IsAsleep || !uComp.IsWorking || uComp.CoreEntity.MarkedForClose || uComp.IsDisabled || uComp.LazyUpdate && !ai.DbUpdated && Tick > uComp.NextLazyUpdateStart)
                        continue;

                    for (int j = 0; j < uComp.Platform.Upgrades.Count; j++)
                    {
                        var u = uComp.Platform.Upgrades[j];
                    }
                }
                ///
                /// Support update section
                ///
                for (int i = 0; i < ai.SupportComps.Count; i++)
                {
                    var sComp = ai.SupportComps[i];
                    if (sComp.Status != Started)
                        sComp.HealthCheck();

                    if (ai.DbUpdated || !sComp.UpdatedState)
                    {
                        sComp.DetectStateChanges();
                    }

                    if (sComp.Platform.State != CorePlatform.PlatformState.Ready || sComp.IsAsleep || !sComp.IsWorking || sComp.CoreEntity.MarkedForClose || sComp.IsDisabled || !Tick60)
                        continue;

                    for (int j = 0; j < sComp.Platform.Support.Count; j++)
                    {
                        var s = sComp.Platform.Support[j];
                        if (s.LastBlockRefreshTick < ai.LastBlockChangeTick && s.IsPrime || s.LastBlockRefreshTick < ai.LastBlockChangeTick && !sComp.Structure.CommonBlockRange)
                            s.RefreshBlocks();

                        if (s.ShowAffectedBlocks != sComp.Data.Repo.Values.Set.Overrides.ArmorShowArea)
                            s.ToggleAreaEffectDisplay();

                        if (s.Active)
                            s.Charge();
                    }
                }

                ///
                /// Phantom update section
                ///
                for (int i = 0; i < ai.PhantomComps.Count; i++)
                {
                    var pComp = ai.PhantomComps[i];

                    if (pComp.CloseCondition || pComp.HasCloseConsition && pComp.AllWeaponsOutOfAmmo()) {
                        if (!pComp.CloseCondition) 
                            pComp.ForceClose(pComp.SubtypeName);
                        continue;
                    }
                    if (pComp.Status != Started)
                        pComp.HealthCheck();

                    if (pComp.Platform.State != CorePlatform.PlatformState.Ready || pComp.IsDisabled || pComp.IsAsleep  || pComp.CoreEntity.MarkedForClose || pComp.LazyUpdate && !ai.DbUpdated && Tick > pComp.NextLazyUpdateStart)
                        continue;

                    if (ai.DbUpdated || !pComp.UpdatedState) {
                        pComp.DetectStateChanges();
                    }

                    ///
                    /// Phantom update section
                    /// 
                    for (int j = 0; j < pComp.Platform.Phantoms.Count; j++)
                    {
                        var p = pComp.Platform.Phantoms[j];
                        if (p.ActiveAmmoDef.AmmoDef.Const.Reloadable && !p.System.DesignatorWeapon && !p.Loading) { 

                            if (IsServer && (p.ProtoWeaponAmmo.CurrentAmmo == 0 || p.CheckInventorySystem))
                                p.ComputeServerStorage();
                            else if (IsClient) {

                                if (p.ClientReloading && p.Reload.EndId > p.ClientEndId && p.Reload.StartId == p.ClientStartId)
                                    p.Reloaded();
                                else
                                    p.ClientReload();
                            }
                        }
                        else if (p.Loading && Tick >= p.ReloadEndTick)
                            p.Reloaded(1);

                        var reloading = p.ActiveAmmoDef.AmmoDef.Const.Reloadable && p.ClientMakeUpShots == 0 && (p.Loading || p.ProtoWeaponAmmo.CurrentAmmo == 0);
                        var canShoot = !p.PartState.Overheated && !reloading && !p.System.DesignatorWeapon;
                        var validShootStates = p.PartState.Action == TriggerOn || p.PartState.Action == TriggerOnce || p.AiShooting && p.PartState.Action == TriggerOff;
                        var delayedFire = p.System.DelayCeaseFire && !p.Target.IsAligned && Tick - p.CeaseFireDelayTick <= p.System.CeaseFireDelay;
                        var shoot = (validShootStates || p.FinishShots || delayedFire);
                        var shotReady = canShoot && (shoot || p.LockOnFireState);

                        if (shotReady) {
                            p.Shoot();
                        }
                        else {

                            if (p.IsShooting)
                                p.StopShooting();

                            if (p.BarrelSpinning) {
                                var spinDown = !(shotReady && ai.CanShoot && p.System.Values.HardPoint.Loading.SpinFree);
                                p.SpinBarrel(spinDown);
                            }
                        }
                    }
                }

                ///
                /// WeaponComp update section
                ///
                for (int i = 0; i < ai.WeaponComps.Count; i++) {

                    var wComp = ai.WeaponComps[i];

                    if (wComp.Status != Started)
                        wComp.HealthCheck();

                    if (ai.DbUpdated || !wComp.UpdatedState) {

                        wComp.DetectStateChanges();
                    }

                    if (IsServer && wComp.Data.Repo.Values.State.PlayerId > 0 && !ai.Data.Repo.ControllingPlayers.ContainsKey(wComp.Data.Repo.Values.State.PlayerId))
                        wComp.ResetPlayerControl();

                    if (wComp.Platform.State != CorePlatform.PlatformState.Ready || wComp.IsDisabled || wComp.IsAsleep || !wComp.IsWorking || wComp.CoreEntity.MarkedForClose || wComp.LazyUpdate && !ai.DbUpdated && Tick > wComp.NextLazyUpdateStart)
                        continue;

                    if (HandlesInput) {

                        if (wComp.TypeSpecific == CoreComponent.CompTypeSpecific.Rifle && wComp.Data.Repo.Values.State.Control != ControlMode.Toolbar)
                            wComp.RequestShootUpdate(TriggerClick, PlayerId);

                        var wasTrack = wComp.Data.Repo.Values.State.TrackingReticle;

                        var isControllingPlayer = wComp.Data.Repo.Values.State.PlayerId == PlayerId;
                        var track = (isControllingPlayer && (wComp.Data.Repo.Values.Set.Overrides.Control != ProtoWeaponOverrides.ControlModes.Auto) && TargetUi.DrawReticle && !InMenu && wComp.Ai.Construct.RootAi.Data.Repo.ControllingPlayers.ContainsKey(PlayerId) && (!UiInput.CameraBlockView || UiInput.CameraChannelId > 0 && UiInput.CameraChannelId == wComp.Data.Repo.Values.Set.Overrides.CameraChannel));
                        if (isControllingPlayer)
                        {
                            TargetUi.LastTrackTick = Tick;
                            if (MpActive && wasTrack != track)
                                wComp.Session.SendTrackReticleUpdate(wComp, track);
                            else if (IsServer)
                                wComp.Data.Repo.Values.State.TrackingReticle = track;
                        }
                    }

                    wComp.ManualMode = wComp.Data.Repo.Values.State.TrackingReticle && wComp.Data.Repo.Values.Set.Overrides.Control == ProtoWeaponOverrides.ControlModes.Manual;

                    Ai.FakeTargets fakeTargets = null;
                    if (wComp.ManualMode || wComp.Data.Repo.Values.Set.Overrides.Control == ProtoWeaponOverrides.ControlModes.Painter)
                        PlayerDummyTargets.TryGetValue(wComp.Data.Repo.Values.State.PlayerId, out fakeTargets);

                    wComp.PainterMode = fakeTargets != null && wComp.Data.Repo.Values.Set.Overrides.Control == ProtoWeaponOverrides.ControlModes.Painter && fakeTargets.PaintedTarget.EntityId != 0;
                    wComp.FakeMode = wComp.ManualMode || wComp.PainterMode;
                    wComp.WasControlled = wComp.UserControlled;
                    wComp.UserControlled = wComp.Data.Repo.Values.State.Control != ControlMode.None;

                    if (!PlayerMouseStates.TryGetValue(wComp.Data.Repo.Values.State.PlayerId, out wComp.InputState))
                        wComp.InputState = DefaultInputStateData;

                    var compManualMode = wComp.Data.Repo.Values.State.Control == ControlMode.Camera || wComp.ManualMode;
                    var canManualShoot = !ai.SuppressMouseShoot && !wComp.InputState.InMenu;

                    ///
                    /// Weapon update section
                    ///
                    for (int j = 0; j < wComp.Platform.Weapons.Count; j++) {

                        var w = wComp.Platform.Weapons[j];
                        var comp = w.Comp;
                        if (w.PartReadyTick > Tick) {

                            if (w.Target.HasTarget && !IsClient)
                                w.Target.Reset(comp.Session.Tick, States.WeaponNotReady);
                            continue;
                        }
                        if (w.AvCapable && Tick20) {
                            var avWasEnabled = w.PlayTurretAv;
                            double distSqr;
                            var pos = w.Comp.CoreEntity.PositionComp.WorldAABB.Center;
                            Vector3D.DistanceSquared(ref CameraPos, ref pos, out distSqr);
                            w.PlayTurretAv = distSqr < w.System.HardPointAvMaxDistSqr;
                            if (avWasEnabled != w.PlayTurretAv) w.StopBarrelAvTick = Tick;
                        }

                        ///
                        ///Check Reload
                        ///                        

                        if (w.ActiveAmmoDef.AmmoDef.Const.Reloadable && !w.System.DesignatorWeapon && !w.Loading) { // does this need StayCharged?

                            if (IsServer)
                            {
                                if (w.ProtoWeaponAmmo.CurrentAmmo == 0 || w.CheckInventorySystem)
                                    w.ComputeServerStorage();
                            }
                            else if (IsClient) {

                                if (w.ClientReloading && w.Reload.EndId > w.ClientEndId && w.Reload.StartId == w.ClientStartId)
                                    w.Reloaded(5);
                                else 
                                    w.ClientReload();
                            }
                        }
                        else if (w.Loading && (IsServer && Tick >= w.ReloadEndTick || IsClient && w.Reload.EndId > w.ClientEndId))
                            w.Reloaded(1);

                        if (DedicatedServer && w.Reload.WaitForClient && !w.Loading && (wComp.Data.Repo.Values.State.PlayerId <= 0 || Tick - w.LastLoadedTick > 60))
                            SendWeaponReload(w, true);

                        ///
                        /// Update Weapon Hud Info
                        /// 
                        var addWeaponToHud = HandlesInput && (w.HeatPerc >= 0.01 || (w.Loading || w.Reload.WaitForClient) && w.ShowReload || w.System.LockOnFocus && !w.Comp.ModOverride && ai.Construct.Data.Repo.FocusData.Locked[0] != FocusData.LockModes.Locked);
                        if (addWeaponToHud && !Session.Config.MinimalHud && ActiveControlBlock != null && ai.SubGrids.Contains(ActiveControlBlock.CubeGrid)) {
                            HudUi.TexturesToAdd++;
                            HudUi.WeaponsToDisplay.Add(w);
                        }

                        if (w.System.PartType != HardwareType.BlockWeapon)
                            continue;

                        if (w.CriticalReaction && !wComp.CloseCondition && (wComp.Data.Repo.Values.Set.Overrides.Armed || wComp.Data.Repo.Values.State.CountingDown || wComp.Data.Repo.Values.State.CriticalReaction))
                            w.CriticalMonitor();

                        if (w.Target.ClientDirty)
                            w.Target.ClientUpdate(w, w.TargetData);

                        ///
                        /// Check target for expire states
                        /// 
                        bool targetLock = false;
                        var noAmmo = w.NoMagsToLoad && w.ProtoWeaponAmmo.CurrentAmmo == 0 && w.ActiveAmmoDef.AmmoDef.Const.Reloadable && !w.System.DesignatorWeapon && Tick - w.LastMagSeenTick > 600;
                        if (w.Target.HasTarget) {

                            if (w.PosChangedTick != Tick)
                                w.UpdatePivotPos();

                            if (!IsClient && noAmmo)
                                w.Target.Reset(Tick, States.Expired);
                            else if (!IsClient && w.Target.TargetEntity == null && w.Target.Projectile == null && !comp.FakeMode || comp.ManualMode && (fakeTargets == null || Tick - fakeTargets.ManualTarget.LastUpdateTick > 120))
                                w.Target.Reset(Tick, States.Expired, !comp.ManualMode);
                            else if (!IsClient && w.Target.TargetEntity != null && (comp.UserControlled && !w.System.SuppressFire || w.Target.TargetEntity.MarkedForClose))
                                w.Target.Reset(Tick, States.Expired);
                            else if (!IsClient && w.Target.Projectile != null && (!ai.LiveProjectile.Contains(w.Target.Projectile) || w.Target.IsProjectile && w.Target.Projectile.State != Projectile.ProjectileState.Alive)) {
                                w.Target.Reset(Tick, States.Expired);
                                w.FastTargetResetTick = Tick + 6;
                            }
                            else if (w.AiEnabled) {

                                if (!Weapon.TrackingTarget(w, w.Target, out targetLock) && !IsClient && w.Target.ExpiredTick != Tick)
                                    w.Target.Reset(Tick, States.LostTracking, !comp.ManualMode && (w.Target.CurrentState != States.RayCheckFailed && !w.Target.HasTarget));
                            }
                            else {

                                Vector3D targetPos;
                                if (w.IsTurret) {

                                    if (!w.TrackTarget && !IsClient) {

                                        if ((comp.TrackingWeapon.Target.Projectile != w.Target.Projectile || w.Target.IsProjectile && w.Target.Projectile.State != Projectile.ProjectileState.Alive || comp.TrackingWeapon.Target.TargetEntity != w.Target.TargetEntity || comp.TrackingWeapon.Target.IsFakeTarget != w.Target.IsFakeTarget))
                                            w.Target.Reset(Tick, States.Expired);
                                        else
                                            targetLock = true;
                                    }
                                    else if (!Weapon.TargetAligned(w, w.Target, out targetPos) && !IsClient)
                                        w.Target.Reset(Tick, States.Expired);
                                }
                                else if (w.TrackTarget && !Weapon.TargetAligned(w, w.Target, out targetPos) && !IsClient)
                                    w.Target.Reset(Tick, States.Expired);
                            }
                        }

                        w.ProjectilesNear = enemyProjectiles && w.System.TrackProjectile && w.Comp.Data.Repo.Values.Set.Overrides.Projectiles && !w.Target.HasTarget && (w.Target.TargetChanged || QCount == w.ShortLoadId );

                        if (comp.Data.Repo.Values.State.Control == ControlMode.Camera && UiInput.MouseButtonPressed)
                            w.Target.TargetPos = Vector3D.Zero;

                        ///
                        /// Queue for target acquire or set to tracking weapon.
                        /// 

                        var seek = comp.FakeMode && !w.Target.IsFakeTarget || (!noAmmo && !w.Target.HasTarget && (comp.DetectOtherSignals && ai.DetectionInfo.OtherInRange || ai.DetectionInfo.PriorityInRange) && (!comp.UserControlled && !Settings.Enforcement.DisableAi || w.PartState.Action == TriggerClick));
                        if (!IsClient && (seek || w.TrackTarget && ai.TargetResetTick == Tick && !comp.UserControlled && !Settings.Enforcement.DisableAi) && !w.AcquiringTarget && (comp.Data.Repo.Values.State.Control == ControlMode.None || comp.Data.Repo.Values.State.Control == ControlMode.Ui))
                        {
                            w.AcquiringTarget = true;
                            AcquireTargets.Add(w);
                        }

                        if (w.Target.TargetChanged) // Target changed
                            w.TargetChanged();

                        ///
                        /// Check weapon's turret to see if its time to go home
                        ///

                        if (w.TurretMode && !w.IsHome && !w.ReturingHome && !w.Target.HasTarget && Tick - w.Target.ResetTick > 239 && !comp.UserControlled && w.PartState.Action == TriggerOff)
                            w.ScheduleWeaponHome();

                        ///
                        /// Determine if its time to shoot
                        ///
                        ///
                        w.AiShooting = targetLock && !comp.UserControlled && !w.System.SuppressFire;

                        var reloading = w.ActiveAmmoDef.AmmoDef.Const.Reloadable && w.ClientMakeUpShots == 0 && (w.Loading || w.ProtoWeaponAmmo.CurrentAmmo == 0 || w.Reload.WaitForClient);
                        var canShoot = !w.PartState.Overheated && !reloading && !w.System.DesignatorWeapon;
                        var paintedTarget = comp.PainterMode && w.Target.IsFakeTarget && w.Target.IsAligned;
                        var validShootStates = paintedTarget || w.PartState.Action == TriggerOn || w.PartState.Action == TriggerOnce || w.AiShooting && w.PartState.Action == TriggerOff;
                        var manualShot = (compManualMode || w.PartState.Action == TriggerClick) && canManualShoot && comp.InputState.MouseButtonLeft;
                        var delayedFire = w.System.DelayCeaseFire && !w.Target.IsAligned && Tick - w.CeaseFireDelayTick <= w.System.CeaseFireDelay;
                        var shoot = (validShootStates || manualShot || w.FinishShots || delayedFire);
                        w.LockOnFireState = shoot && (w.System.LockOnFocus && !w.Comp.ModOverride) && ai.Construct.Data.Repo.FocusData.HasFocus && ai.Construct.Focus.FocusInRange(w);
                        var shotReady = canShoot && (shoot && (!w.System.LockOnFocus || w.Comp.ModOverride) || w.LockOnFireState);
                        
                        if (shotReady && ai.CanShoot) {

                            if (MpActive && HandlesInput && !ManualShot)
                                ManualShot = !validShootStates && !w.FinishShots && !delayedFire;

                            if (w.System.DelayCeaseFire && (validShootStates || manualShot || w.FinishShots))
                                w.CeaseFireDelayTick = Tick;

                            ShootingWeapons.Add(w);
                        }
                        else {

                            if (w.IsShooting || w.PreFired)
                                w.StopShooting();

                            if (w.BarrelSpinning) {
                                var spinDown = !(shotReady && ai.CanShoot && w.System.Values.HardPoint.Loading.SpinFree);
                                w.SpinBarrel(spinDown);
                            }
                        }

                        if (comp.Debug && !DedicatedServer)
                            WeaponDebug(w);
                    }
                }
                
                if (ai.AiType == Ai.AiTypes.Grid && Tick60 && ai.BlockChangeArea != BoundingBox.Invalid) {
                    ai.BlockChangeArea = BoundingBox.CreateInvalid();
                    ai.AddedBlockPositions.Clear();
                    ai.RemovedBlockPositions.Clear();
                }
                ai.DbUpdated = false;
            }

            if (DbTask.IsComplete && DbsToUpdate.Count > 0 && !DbUpdating)
                UpdateDbsInQueue();
        }

        private void CheckAcquire()
        {
            for (int i = AcquireTargets.Count - 1; i >= 0; i--)
            {
                var w = AcquireTargets[i];
                var comp = w.Comp;
                if (w.BaseComp.IsAsleep || w.BaseComp.Ai == null || comp.Ai.TopEntity.MarkedForClose || !comp.Ai.HasPower || comp.Ai.Concealed || comp.CoreEntity.MarkedForClose || !comp.Ai.DbReady || !comp.IsWorking  || w.NoMagsToLoad && w.ProtoWeaponAmmo.CurrentAmmo == 0 && Tick - w.LastMagSeenTick > 600) {
                    
                    w.AcquiringTarget = false;
                    AcquireTargets.RemoveAtFast(i);
                    continue;
                }

                if (!w.Acquire.Monitoring && IsServer && w.TrackTarget)
                    AcqManager.Monitor(w.Acquire);

                var acquire = (w.Acquire.IsSleeping && AsleepCount == w.Acquire.SlotId || !w.Acquire.IsSleeping && AwakeCount == w.Acquire.SlotId);

                var seekProjectile = w.ProjectilesNear || w.System.TrackProjectile && w.Comp.Data.Repo.Values.Set.Overrides.Projectiles && w.BaseComp.Ai.CheckProjectiles;
                var checkTime = w.Target.TargetChanged || acquire || seekProjectile || w.FastTargetResetTick == Tick;

                if (checkTime || w.BaseComp.Ai.TargetResetTick == Tick && w.Target.HasTarget) {

                    if (seekProjectile || comp.Data.Repo.Values.State.TrackingReticle || (comp.DetectOtherSignals && w.BaseComp.Ai.DetectionInfo.OtherInRange || w.BaseComp.Ai.DetectionInfo.PriorityInRange) && w.BaseComp.Ai.DetectionInfo.ValidSignalExists(w)) {
                        if (comp.TrackingWeapon != null && comp.TrackingWeapon.System.DesignatorWeapon && comp.TrackingWeapon != w && comp.TrackingWeapon.Target.HasTarget) {

                            var topMost = comp.TrackingWeapon.Target.TargetEntity?.GetTopMostParent();
                            Ai.AcquireTarget(w, false, topMost);
                        }
                        else
                        {
                            Ai.AcquireTarget(w, w.BaseComp.Ai.TargetResetTick == Tick);
                        }
                    }

                    if (w.Target.HasTarget || !(comp.DetectOtherSignals && w.BaseComp.Ai.DetectionInfo.OtherInRange || w.BaseComp.Ai.DetectionInfo.PriorityInRange)) {

                        w.AcquiringTarget = false;
                        AcquireTargets.RemoveAtFast(i);
                        if (w.Target.HasTarget && MpActive) {
                            w.Target.PushTargetToClient(w);
                        }
                    }
                }
            }
        }

        private void ShootWeapons()
        {
            for (int i = ShootingWeapons.Count - 1; i >= 0; i--) {
                
                var w = ShootingWeapons[i];
                var invalidWeapon = w.Comp.CoreEntity.MarkedForClose || w.Comp.Ai == null || w.Comp.Ai.Concealed || w.Comp.Ai.TopEntity.MarkedForClose || w.Comp.Platform.State != CorePlatform.PlatformState.Ready;
                var smartTimer = w.ActiveAmmoDef.AmmoDef.Trajectory.Guidance == Smart && QCount == w.ShortLoadId && (w.Target.HasTarget && Tick - w.LastSmartLosCheck > 240 || Tick - w.LastSmartLosCheck > 1200);
                var quickSkip = invalidWeapon || w.Comp.IsBlock && smartTimer && !w.SmartLos() || w.PauseShoot || (w.ProtoWeaponAmmo.CurrentAmmo == 0 && w.ClientMakeUpShots == 0) && w.ActiveAmmoDef.AmmoDef.Const.Reloadable;
                if (quickSkip) continue;

                w.Shoot();
            }
            ShootingWeapons.Clear();
        }
    }
}
