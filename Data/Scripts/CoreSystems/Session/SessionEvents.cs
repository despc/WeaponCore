﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using CoreSystems.Platform;
using CoreSystems.Support;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Weapons;
using SpaceEngineers.Game.ModAPI;
using VRage.Collections;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using static CoreSystems.Support.Ai;
using IMyControllableEntity = VRage.Game.ModAPI.Interfaces.IMyControllableEntity;

namespace CoreSystems
{
    public partial class Session
    {
        internal void OnEntityCreate(MyEntity entity)
        {
            try
            {
                if (!Inited) lock (InitObj) Init();

                var planet = entity as MyPlanet;
                if (planet != null)
                    PlanetMap.TryAdd(planet.EntityId, planet);

                var grid = entity as MyCubeGrid;
                if (grid != null)
                {
                    var gridMap = GridMapPool.Get();
                    gridMap.Trash = true;
                    GridToInfoMap.TryAdd(grid, gridMap);
                    grid.AddedToScene += AddGridToMap;
                    grid.OnClose += RemoveGridFromMap;
                }

                if (!PbApiInited && entity is IMyProgrammableBlock) PbActivate = true;
                var placer = entity as IMyBlockPlacerBase;
                if (placer != null && Placer == null) Placer = placer;

                var cube = entity as MyCubeBlock;
                var sorter = entity as MyConveyorSorter;
                var turret = entity as IMyLargeTurretBase;
                var controllableGun = entity as IMyUserControllableGun;
                var rifle = entity as IMyAutomaticRifleGun;
                var decoy = cube as IMyDecoy;
                var camera = cube as MyCameraBlock;
                var turretController = cube as IMyTurretControlBlock;
                if (sorter != null || turret != null || controllableGun != null || rifle != null || turretController != null && TurretControllerEnabled)
                {
                    lock (InitObj)
                    {
                        if (rifle != null) {
                            DelayedHandWeaponsSpawn.Enqueue(rifle);
                            return;
                        }
                        var cubeType = cube != null && (ReplaceVanilla && VanillaIds.ContainsKey(cube.BlockDefinition.Id) || PartPlatforms.ContainsKey(cube.BlockDefinition.Id)) || turretController != null;
                        var validType = cubeType;
                        if (!validType)
                        {
                            if (turret != null)
                                _vanillaTurretTick = Tick;
                            return;
                        }

                        if (!SorterControls && entity is MyConveyorSorter) {
                            MyAPIGateway.Utilities.InvokeOnGameThread(() => CreateTerminalUi<IMyConveyorSorter>(this));
                            SorterControls = true;
                            if (!EarlyInitOver) ControlQueue.Enqueue(typeof(IMyConveyorSorter));
                        }
                        else if (!TurretControls && turret != null) {
                            MyAPIGateway.Utilities.InvokeOnGameThread(() => CreateTerminalUi<IMyLargeTurretBase>(this));
                            TurretControls = true;
                            if (!EarlyInitOver) ControlQueue.Enqueue(typeof(IMyLargeTurretBase));
                        }
                        else if (!FixedMissileReloadControls && controllableGun is IMySmallMissileLauncherReload) {
                            MyAPIGateway.Utilities.InvokeOnGameThread(() => CreateTerminalUi<IMySmallMissileLauncherReload>(this));
                            FixedMissileReloadControls = true;
                            if (!EarlyInitOver) ControlQueue.Enqueue(typeof(IMySmallMissileLauncherReload));
                        }
                        else if (!FixedMissileControls && controllableGun is IMySmallMissileLauncher) {
                            MyAPIGateway.Utilities.InvokeOnGameThread(() => CreateTerminalUi<IMySmallMissileLauncher>(this));
                            FixedMissileControls = true;
                            if (!EarlyInitOver) ControlQueue.Enqueue(typeof(IMySmallMissileLauncher));
                        }
                        else if (!FixedGunControls && controllableGun is IMySmallGatlingGun) {
                            MyAPIGateway.Utilities.InvokeOnGameThread(() => CreateTerminalUi<IMySmallGatlingGun>(this));
                            FixedGunControls = true;
                            if (!EarlyInitOver) ControlQueue.Enqueue(typeof(IMySmallGatlingGun));
                        }
                        else if (!TurretControllerControls && turretController != null) {
                            MyAPIGateway.Utilities.InvokeOnGameThread(() => CreateTerminalUi<IMyTurretControlBlock>(this));
                            TurretControllerControls = true;
                            if (!EarlyInitOver) ControlQueue.Enqueue(typeof(IMyTurretControlBlock));
                        }

                    }

                    var def = cube?.BlockDefinition.Id ?? entity.DefinitionId;
                    InitComp(entity, ref def);
                }
                else if (decoy != null)
                {
                    if (!DecoyControls)
                    {
                        MyAPIGateway.Utilities.InvokeOnGameThread(() => CreateDecoyTerminalUi<IMyDecoy>(this));
                        DecoyControls = true;
                        if (!EarlyInitOver) ControlQueue.Enqueue(typeof(IMyDecoy));
                    }

                    cube.AddedToScene += DecoyAddedToScene;
                }
                else if (camera != null)
                {
                    if (!CameraDetected)
                    {
                        MyAPIGateway.Utilities.InvokeOnGameThread(() => CreateCameraTerminalUi<IMyCameraBlock>(this));
                        CameraDetected = true;
                    }

                    cube.AddedToScene += CameraAddedToScene;
                    cube.OnClose += CameraOnClose;
                }
            }
            catch (Exception ex) { Log.Line($"Exception in OnEntityCreate: {ex}", null, true); }
        }


        private void GridGroupsOnOnGridGroupCreated(IMyGridGroupData groupData)
        {
            if (groupData.LinkType != GridLinkTypeEnum.Mechanical)
                return;

            var map = GridGroupMapPool.Count > 0 ? GridGroupMapPool.Pop() : new GridGroupMap(this);
            map.Type = groupData.LinkType;
            map.GroupData = groupData;
            //groupData.OnReleased += map.OnReleased;
            groupData.OnGridAdded += map.OnGridAdded;
            groupData.OnGridRemoved += map.OnGridRemoved;
            GridGroupMap[groupData] = map;
        }

        private void GridGroupsOnOnGridGroupDestroyed(IMyGridGroupData groupData)
        {
            if (groupData.LinkType != GridLinkTypeEnum.Mechanical)
                return;

            GridGroupMap map;
            if (GridGroupMap.TryGetValue(groupData, out map))
            {
                //groupData.OnReleased -= map.OnReleased;
                groupData.OnGridAdded -= map.OnGridAdded;
                groupData.OnGridRemoved -= map.OnGridRemoved;
                
                GridGroupMap.Remove(groupData);
                map.Clean();
                GridGroupMapPool.Push(map);
            }
            else 
                Log.Line($"GridGroupsOnOnGridGroupDestroyed could not find map");
        }

        private void DecoyAddedToScene(MyEntity myEntity)
        {
            var term = (IMyTerminalBlock)myEntity;
            term.CustomDataChanged += DecoyCustomDataChanged;
            term.AppendingCustomInfo += DecoyAppendingCustomInfo;
            myEntity.OnMarkForClose += DecoyOnMarkForClose;

            long value = -1;
            long.TryParse(term.CustomData, out value);
            if (value < 1 || value > 7)
                value = 1;
            DecoyMap[myEntity] = (WeaponDefinition.TargetingDef.BlockTypes)value;
        }

        private void DecoyAppendingCustomInfo(IMyTerminalBlock term, StringBuilder stringBuilder)
        {
            if (term.CustomData.Length == 1)
                DecoyCustomDataChanged(term);
        }

        private void DecoyOnMarkForClose(MyEntity myEntity)
        {
            var term = (IMyTerminalBlock)myEntity;
            term.CustomDataChanged -= DecoyCustomDataChanged;
            term.AppendingCustomInfo -= DecoyAppendingCustomInfo;
            myEntity.OnMarkForClose -= DecoyOnMarkForClose;
        }

        private void DecoyCustomDataChanged(IMyTerminalBlock term)
        {
            long value = -1;
            long.TryParse(term.CustomData, out value);

            var entity = (MyEntity)term;
            var cube = (MyCubeBlock)entity;
            if (value > 0 && value <= 7)
            {
                var newType = (WeaponDefinition.TargetingDef.BlockTypes)value;
                WeaponDefinition.TargetingDef.BlockTypes type;
                ConcurrentDictionary<WeaponDefinition.TargetingDef.BlockTypes, ConcurrentCachingList<MyCubeBlock>> blockTypes;
                if (GridToBlockTypeMap.TryGetValue(cube.CubeGrid, out blockTypes) && DecoyMap.TryGetValue(entity, out type) && type != newType)
                {
                    blockTypes[type].Remove(cube, true);
                    var addColletion = blockTypes[newType];
                    addColletion.Add(cube);
                    addColletion.ApplyAdditions();
                    DecoyMap[entity] = newType;
                }
            }
        }

        private void CameraAddedToScene(MyEntity myEntity)
        {
            var term = (IMyTerminalBlock)myEntity;
            term.CustomDataChanged += CameraCustomDataChanged;
            term.AppendingCustomInfo += CameraAppendingCustomInfo;
            myEntity.OnMarkForClose += CameraOnMarkForClose;
            CameraCustomDataChanged(term);
        }

        private void CameraOnClose(MyEntity myEntity)
        {
            myEntity.OnClose -= CameraOnClose;
            myEntity.AddedToScene -= CameraAddedToScene;
        }

        private void CameraAppendingCustomInfo(IMyTerminalBlock term, StringBuilder stringBuilder)
        {
            if (term.CustomData.Length == 1)
                CameraCustomDataChanged(term);
        }

        private void CameraOnMarkForClose(MyEntity myEntity)
        {
            var term = (IMyTerminalBlock)myEntity;
            term.CustomDataChanged -= CameraCustomDataChanged;
            term.AppendingCustomInfo -= CameraAppendingCustomInfo;
            myEntity.OnMarkForClose -= CameraOnMarkForClose;
        }

        private void CameraCustomDataChanged(IMyTerminalBlock term)
        {
            var entity = (MyEntity)term;
            var cube = (MyCubeBlock)entity;
            long value = -1;
            if (long.TryParse(term.CustomData, out value))
            {
                CameraChannelMappings[cube] = value;
            }
            else
            {
                CameraChannelMappings[cube] = -1;
            }
        }

        private void AddGridToMap(MyEntity myEntity)
        {
            try
            {
                var grid = myEntity as MyCubeGrid;

                if (grid != null)
                {
                    GridMap gridMap;
                    if (GridToInfoMap.TryGetValue(grid, out gridMap))
                    {
                        var allFat = ConcurrentListPool.Get();

                        var gridFat = grid.GetFatBlocks();
                        for (int i = 0; i < gridFat.Count; i++)
                        {
                            var term = gridFat[i] as IMyTerminalBlock;
                            if (term == null) continue;

                            allFat.Add(gridFat[i]);
                        }
                        allFat.ApplyAdditions();

                        if (grid.Components.TryGet(out gridMap.Targeting))
                            gridMap.Targeting.AllowScanning = false;

                        gridMap.MyCubeBocks = allFat;

                        grid.OnFatBlockAdded += ToGridMap;
                        grid.OnFatBlockRemoved += FromGridMap;
                        using (_dityGridLock.Acquire())
                        {
                            DirtyGridInfos.Add(grid);
                            DirtyGrid = true;
                        }
                    }
                    else Log.Line($"AddGridToMap could not find gridmap");
                }

            }
            catch (Exception ex) { Log.Line($"Exception in GridAddedToScene: {ex}", null, true); }
        }

        private void RemoveGridFromMap(MyEntity myEntity)
        {
            var grid = (MyCubeGrid)myEntity;
            GridMap gridMap;
            if (GridToInfoMap.TryRemove(grid, out gridMap))
            {
                gridMap.Trash = true;
                grid.OnClose -= RemoveGridFromMap;
                grid.AddedToScene -= AddGridToMap;

                if (gridMap.MyCubeBocks != null)
                {
                    ConcurrentListPool.Return(gridMap.MyCubeBocks);
                    grid.OnFatBlockAdded -= ToGridMap;
                    grid.OnFatBlockRemoved -= FromGridMap;
                }

                GridMapPool.Return(gridMap);

                using (_dityGridLock.Acquire())
                {
                    DirtyGridInfos.Add(grid);
                    DirtyGrid = true;
                }
            }
            else Log.Line($"grid not removed and list not cleaned: marked:{grid.MarkedForClose}({grid.Closed}) - inScene:{grid.InScene}");
        }

        private void ToGridMap(MyCubeBlock myCubeBlock)
        {
            try
            {
                var term = myCubeBlock as IMyTerminalBlock;
                GridMap gridMap;
                if (term != null && GridToInfoMap.TryGetValue(myCubeBlock.CubeGrid, out gridMap))
                {
                    gridMap.MyCubeBocks.Add(myCubeBlock);
                    using (_dityGridLock.Acquire())
                    {
                        DirtyGridInfos.Add(myCubeBlock.CubeGrid);
                        DirtyGrid = true;
                    }
                }
                else if (term != null) Log.Line($"ToGridMap missing grid: cubeMark:{myCubeBlock.MarkedForClose} - gridMark:{myCubeBlock.CubeGrid.MarkedForClose} - name:{myCubeBlock.DebugName}");

            }
            catch (Exception ex) { Log.Line($"Exception in ToGridMap: {ex} - marked:{myCubeBlock.MarkedForClose}"); }
        }

        private void FromGridMap(MyCubeBlock myCubeBlock)
        {
            try
            {
                var term = myCubeBlock as IMyTerminalBlock;
                GridMap gridMap;
                if (term != null && GridToInfoMap.TryGetValue(myCubeBlock.CubeGrid, out gridMap))
                {
                    gridMap.MyCubeBocks.Remove(myCubeBlock);

                    using (_dityGridLock.Acquire())
                    {
                        DirtyGridInfos.Add(myCubeBlock.CubeGrid);
                        DirtyGrid = true;
                    }
                }
                else if (term != null) Log.Line($"ToGridMap missing grid: cubeMark:{myCubeBlock.MarkedForClose} - gridMark:{myCubeBlock.CubeGrid.MarkedForClose} - name:{myCubeBlock.DebugName}");
            }
            catch (Exception ex) { Log.Line($"Exception in FromGridMap: {ex} - marked:{myCubeBlock.MarkedForClose}"); }
        }

        internal void BeforeDamageHandler(object o, ref MyDamageInformation info)
        {
            var slim = o as IMySlimBlock;

            if (slim != null) {

                var cube = slim.FatBlock as MyCubeBlock;
                var grid = (MyCubeGrid)slim.CubeGrid;

                if (info.IsDeformation && info.AttackerId > 0 && DeformProtection.Contains(grid)) {
                    Log.Line("BeforeDamageHandler1");
                    info.Amount = 0f;
                    return;
                }

                CoreComponent comp;
                if (cube != null && ArmorCubes.TryGetValue(cube, out comp)) {

                    Log.Line("BeforeDamageHandler2");
                    info.Amount = 0f;
                    if (info.IsDeformation && info.AttackerId > 0) {
                        DeformProtection.Add(cube.CubeGrid);
                        LastDeform = Tick;
                    }
                }
            }
        }
        public void OnCloseAll()
        {
            //Log.Line($"test1");
            MyAPIGateway.GridGroups.OnGridGroupDestroyed -= GridGroupsOnOnGridGroupDestroyed;
            MyAPIGateway.GridGroups.OnGridGroupCreated -= GridGroupsOnOnGridGroupCreated;
        }


        private void MenuOpened(object obj)
        {
            try
            {
                InMenu = true;
                Ai ai;
                if (ActiveControlBlock != null && EntityToMasterAi.TryGetValue(ActiveControlBlock.CubeGrid, out ai))  {
                    //Send updates?
                }
            }
            catch (Exception ex) { Log.Line($"Exception in MenuOpened: {ex}", null, true); }
        }

        private void MenuClosed(object obj)
        {
            try
            {
                InMenu = false;
                HudUi.NeedsUpdate = true;
                Ai ai;
                if (ActiveControlBlock != null && EntityToMasterAi.TryGetValue(ActiveControlBlock.CubeGrid, out ai))  {
                    //Send updates?
                }
            }
            catch (Exception ex) { Log.Line($"Exception in MenuClosed: {ex}", null, true); }
        }

        private void PlayerControlAcquired(MyEntity lastEnt)
        {
            var topMost = lastEnt.GetTopMostParent();
            Ai ai;
            if (topMost != null && EntityAIs.TryGetValue(topMost, out ai)) {

                CoreComponent comp;
                if (ai.CompBase.TryGetValue(lastEnt, out comp)) {
                    if (comp.Type == CoreComponent.CompType.Weapon)
                        ((Weapon.WeaponComponent)comp).RequestShootUpdate(CoreComponent.TriggerActions.TriggerOff, comp.Session.MpServer ? comp.Session.PlayerId : -1);
                }
            }
        }

        private void PlayerControlNotify(MyEntity entity)
        {
            var topMost = entity.GetTopMostParent();
            Ai ai;
            if (topMost != null && EntityAIs.TryGetValue(topMost, out ai))
            {
                if (HandlesInput && ai.AiOwner == 0)
                {
                    MyAPIGateway.Utilities.ShowNotification($"Ai computer is not owned, take ownership of grid weapons! - current ownerId is: {ai.AiOwner}", 10000);
                }
            }
        }
        
        private void PlayerConnected(long id)
        {
            try
            {
                if (Players.ContainsKey(id)) return;
                MyAPIGateway.Multiplayer.Players.GetPlayers(null, myPlayer => FindPlayer(myPlayer, id));
            }
            catch (Exception ex) { Log.Line($"Exception in PlayerConnected: {ex}", null, true); }
        }

        private void PlayerDisconnected(long l)
        {
            try
            {
                PlayerEventId++;
                PlayerMap removedPlayer;
                if (Players.TryRemove(l, out removedPlayer))
                {
                    long playerId;

                    SteamToPlayer.TryRemove(removedPlayer.Player.SteamUserId, out playerId);
                    PlayerEntityIdInRange.Remove(removedPlayer.Player.SteamUserId);
                    PlayerMouseStates.Remove(playerId);
                    PlayerDummyTargets.Remove(playerId);
                    if (PlayerControllerMonitor.Remove(removedPlayer.Player))
                        removedPlayer.Player.Controller.ControlledEntityChanged -= OnPlayerController;

                    if (IsServer && MpActive)
                        SendPlayerConnectionUpdate(l, false);

                    if (AuthorIds.Contains(removedPlayer.Player.SteamUserId))
                        ConnectedAuthors.Remove(playerId);
                }
            }
            catch (Exception ex) { Log.Line($"Exception in PlayerDisconnected: {ex}"); }
        }

        private bool FindPlayer(IMyPlayer player, long id)
        {
            if (player.IdentityId == id)
            {
                if (!Players.ContainsKey(id))
                    BuildPlayerMap(player, id);

                SteamToPlayer[player.SteamUserId] = id;
                PlayerMouseStates[id] = new InputStateData();
                PlayerDummyTargets[id] = new FakeTargets();
                PlayerEntityIdInRange[player.SteamUserId] = new HashSet<long>();

                var controller = player.Controller;
                if (controller != null && PlayerControllerMonitor.Add(player))
                {
                    controller.ControlledEntityChanged += OnPlayerController;
                    OnPlayerController(null, controller.ControlledEntity);
                }

                PlayerEventId++;
                if (AuthorIds.Contains(player.SteamUserId))
                    ConnectedAuthors.Add(id, player.SteamUserId);

                if (IsServer && MpActive)
                {
                    SendPlayerConnectionUpdate(id, true);
                    SendServerStartup(player.SteamUserId);
                }
                else if (MpActive && MultiplayerId != player.SteamUserId && JokePlayerList.Contains(player.SteamUserId))
                    PracticalJokes();
            }
            return false;
        }

        private void BuildPlayerMap(IMyPlayer player, long id)
        {
            MyTargetFocusComponent targetFocus = null;
            MyTargetLockingComponent targetLock = null;
            if (!(player.Character != null && player.Character.Components.TryGet(out targetFocus) && player.Character.Components.TryGet(out targetLock)))
                Log.Line($"failed to get player: {player.Character == null}, focus:{targetFocus == null} or lock:{targetLock == null} - PlayerId:{PlayerId} - PlayerCount:{Players.Count}");

            Players[id] = new PlayerMap { Player = player, PlayerId = id, TargetFocus = targetFocus, TargetLock = targetLock };
        }

        private void OnPlayerController(IMyControllableEntity exitController, IMyControllableEntity enterController)
        {
            try
            {
                GridMap gridMap;
                var exit = exitController as MyEntity;
                if (exit != null && enterController?.ControllerInfo != null)
                {
                    var cube = exit as MyCubeBlock;

                    if (cube != null)
                    {
                        if (GridToInfoMap.TryGetValue(cube.CubeGrid, out gridMap))
                        {
                            var playerId = enterController.ControllerInfo.ControllingIdentityId;
                            gridMap.LastControllerTick = Tick + 1;
                            gridMap.GroupMap.LastControllerTick = Tick + 1;
                            gridMap.PlayerControllers.Remove(playerId);

                            Ai ai;
                            if (EntityAIs.TryGetValue(cube.CubeGrid, out ai))
                            {
                                CoreComponent comp;
                                if (ai.CompBase.TryGetValue(cube, out comp) && comp is Weapon.WeaponComponent)
                                {
                                    if (IsServer)
                                    {
                                        var wComp = (Weapon.WeaponComponent)comp;
                                        wComp.Data.Repo.Values.State.PlayerId = -1;
                                        wComp.Data.Repo.Values.State.Control = ProtoWeaponState.ControlMode.None;

                                        if (MpActive)
                                            SendComp(wComp);
                                    }
                                    if (HandlesInput)
                                        GunnerRelease(cube);
                                }
                            }
                        }
                    }
                }

                var enter = enterController as MyEntity;
                if (enter != null && enterController.ControllerInfo != null)
                {
                    var cube = enter as MyCubeBlock;

                    if (cube != null)
                    {

                        var playerId = enterController.ControllerInfo.ControllingIdentityId;

                        if (GridToInfoMap.TryGetValue(cube.CubeGrid, out gridMap))
                        {
                            gridMap.LastControllerTick = Tick + 1;
                            gridMap.GroupMap.LastControllerTick = Tick + 1;
                            var pController = new PlayerController { ControllBlock = cube, Id = playerId, EntityId = cube.EntityId, ChangeTick = Tick };
                            gridMap.PlayerControllers[playerId] = pController;

                            Ai ai;
                            if (EntityAIs.TryGetValue(cube.CubeGrid, out ai))
                            {
                                CoreComponent comp;
                                if (IsServer && ai.CompBase.TryGetValue(cube, out comp) && comp is Weapon.WeaponComponent)
                                {
                                    if (IsServer)
                                    {
                                        var wComp = (Weapon.WeaponComponent)comp;
                                        wComp.Data.Repo.Values.State.PlayerId = PlayerId;
                                        wComp.Data.Repo.Values.State.Control = ProtoWeaponState.ControlMode.Camera;


                                        if (MpActive)
                                            SendComp(wComp);
                                    }

                                    if (HandlesInput)
                                        GunnerAcquire(cube);

                                }
                            }
                        }



                    }
                }
            }
            catch (Exception ex) { Log.Line($"Exception in OnPlayerController: {ex}"); }
        }
    }
}
