using System.Collections.Generic;
using CoreSystems.Support;
using Sandbox.Game.Entities;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRageMath;

namespace CoreSystems.Platform
{
    public partial class Weapon 
    {
        internal class ParallelRayCallBack
        {
            internal Weapon Weapon;

            internal ParallelRayCallBack(Weapon weapon)
            {
                Weapon = weapon;
            }

            public void NormalShootRayCallBack(IHitInfo hitInfo)
            {
                Weapon.Casting = false;
                Weapon.PauseShoot = false;
                var masterWeapon = Weapon.System.TrackTargets ? Weapon : Weapon.Comp.TrackingWeapon;
                var ignoreTargets = Weapon.Target.TargetState == Target.TargetStates.IsProjectile || Weapon.Target.TargetEntity is IMyCharacter;
                var scope = Weapon.GetScope;
                var trackingCheckPosition = scope.CachedPos;
                double rayDist = 0;


                if (Weapon.System.Session.DebugLos)
                {
                    var hitPos = hitInfo.Position;
                    if (rayDist <= 0) Vector3D.Distance(ref trackingCheckPosition, ref hitPos, out rayDist);

                    Weapon.System.Session.AddLosCheck(new Session.LosDebug { Part = Weapon, HitTick = Weapon.System.Session.Tick, Line = new LineD(trackingCheckPosition, hitPos) });
                }

                
                if (Weapon.Comp.Ai.ShieldNear)
                {
                    var targetPos = Weapon.Target.Projectile?.Position ?? Weapon.Target.TargetEntity.PositionComp.WorldMatrixRef.Translation;
                    var targetDir = targetPos - trackingCheckPosition;
                    if (Weapon.HitFriendlyShield(trackingCheckPosition, targetPos, targetDir))
                    {
                        masterWeapon.Target.Reset(Weapon.Comp.Session.Tick, Target.States.RayCheckFriendly);
                        if (masterWeapon != Weapon) Weapon.Target.Reset(Weapon.Comp.Session.Tick, Target.States.RayCheckFriendly);
                        return;
                    }
                }

                var hitTopEnt = (MyEntity)hitInfo?.HitEntity?.GetTopMostParent();
                if (hitTopEnt == null)
                {
                    if (ignoreTargets)
                        return;
                    masterWeapon.Target.Reset(Weapon.Comp.Session.Tick, Target.States.RayCheckMiss);
                    if (masterWeapon != Weapon) Weapon.Target.Reset(Weapon.Comp.Session.Tick, Target.States.RayCheckMiss);
                    return;
                }

                var targetTopEnt = Weapon.Target.TargetEntity?.GetTopMostParent();
                if (targetTopEnt == null)
                    return;

                var unexpectedHit = ignoreTargets || targetTopEnt != hitTopEnt;
                var topAsGrid = hitTopEnt as MyCubeGrid;

                if (unexpectedHit)
                {
                    if (hitTopEnt is MyVoxelBase)
                    {
                        masterWeapon.Target.Reset(Weapon.Comp.Session.Tick, Target.States.RayCheckVoxel);
                        if (masterWeapon != Weapon) Weapon.Target.Reset(Weapon.Comp.Session.Tick, Target.States.RayCheckVoxel);
                        return;
                    }

                    if (topAsGrid == null)
                        return;
                    if (Weapon.Target.TargetEntity != null && (Weapon.Comp.Ai.AiType == Ai.AiTypes.Grid && topAsGrid.IsSameConstructAs(Weapon.Comp.Ai.GridEntity)))
                    {
                        masterWeapon.Target.Reset(Weapon.Comp.Session.Tick, Target.States.RayCheckSelfHit);
                        if (masterWeapon != Weapon) Weapon.Target.Reset(Weapon.Comp.Session.Tick, Target.States.RayCheckSelfHit);
                        Weapon.PauseShoot = true;
                        return;
                    }
                    if (!topAsGrid.DestructibleBlocks || topAsGrid.Immune || topAsGrid.GridGeneralDamageModifier <= 0 || !Session.GridEnemy(Weapon.Comp.Ai.AiOwner, topAsGrid))
                    {
                        masterWeapon.Target.Reset(Weapon.Comp.Session.Tick, Target.States.RayCheckFriendly);
                        if (masterWeapon != Weapon) Weapon.Target.Reset(Weapon.Comp.Session.Tick, Target.States.RayCheckFriendly);
                        return;
                    }
                    return;
                }
                if (Weapon.System.ClosestFirst && topAsGrid != null && topAsGrid == targetTopEnt)
                {
                    var halfExtMin = topAsGrid.PositionComp.LocalAABB.HalfExtents.Min();
                    var minSize = topAsGrid.GridSizeR * 8;
                    var maxChange = halfExtMin > minSize ? halfExtMin : minSize;
                    var targetPos = Weapon.Target.TargetEntity.PositionComp.WorldAABB.Center;
                    var weaponPos = trackingCheckPosition;

                    if (rayDist <= 0) Vector3D.Distance(ref weaponPos, ref targetPos, out rayDist);
                    var newHitShortDist = rayDist * (1 - hitInfo.Fraction);
                    var distanceToTarget = rayDist * hitInfo.Fraction;

                    var shortDistExceed = newHitShortDist - Weapon.Target.HitShortDist > maxChange;
                    var escapeDistExceed = distanceToTarget - Weapon.Target.OrigDistance > Weapon.Target.OrigDistance;
                    if (shortDistExceed || escapeDistExceed)
                    {
                        masterWeapon.Target.Reset(Weapon.Comp.Session.Tick, Target.States.RayCheckDistOffset);
                        if (masterWeapon != Weapon) Weapon.Target.Reset(Weapon.Comp.Session.Tick, Target.States.RayCheckDistOffset);
                    }
                }
            }
        }

        internal class Muzzle
        {
            internal Muzzle(Weapon weapon, int id, Session session)
            {
                MuzzleId = id;
                UniqueId = session.UniqueMuzzleId.Id;
                Weapon = weapon;
            }

            internal Weapon Weapon;
            internal Vector3D Position;
            internal Vector3D Direction;
            internal Vector3D UpDirection;
            internal Vector3D DeviatedDir;
            internal uint LastUpdateTick;
            internal uint LastAv1Tick;
            internal uint LastAv2Tick;
            internal int MuzzleId;
            internal ulong UniqueId;
            internal bool Av1Looping;
            internal bool Av2Looping;

        }

        public class ShootManager
        {
            public readonly WeaponComponent Comp;
            internal bool ShootToggled;
            internal bool RequestClientShootDelay;
            internal bool WaitingBurstResponse;
            internal bool FreezeClientShoot;
            internal uint CompletedCycles;
            internal uint LastCycle = uint.MaxValue;
            internal uint RequestShootBurstId;
            internal int WeaponsFired;
            internal ShootModes LastShootMode;


            public enum ShootModes
            {
                Default,
                MouseControl,
                KeyToggle,
                BurstFire,
            }

            internal enum ShootCodes
            {
                ServerResponse,
                ClientRequest,
                ServerRequest,
                ServerRelay,
                ToggleServerOff,
                ToggleClientOff,
            }

            public ShootManager(WeaponComponent comp)
            {
                Comp = comp;
            }

            #region Main
            internal void RequestShootSync(long playerId) // this shoot method mixes client initiation with server delayed server confirmation in order to maintain sync while avoiding authoritative delays in the common case. 
            {
                var set = Comp.Data.Repo.Values.Set;
                var sMode = set.Overrides.ShootMode;
                var mouseMode = sMode == ShootModes.MouseControl;
                var newMousePress = !ShootToggled && Comp.Session.UiInput.MouseButtonLeftNewPressed;
                var newMouseRelease = ShootToggled && Comp.Session.UiInput.MouseButtonLeftReleased;
                var validMouseState = !Comp.Session.HandlesInput || newMousePress || newMouseRelease;
                var toggleMode = set.Overrides.ShootMode == ShootModes.KeyToggle || mouseMode && validMouseState;

                if ((sMode == ShootModes.Default || mouseMode && !validMouseState) && LastShootMode == sMode) // quick terminate if shoot mode state invalid
                    return;

                var modeChange = LastShootMode != sMode;
                LastShootMode = sMode;

                if (sMode == ShootModes.Default && !ShootToggled)
                    return;

                var sendRequest = !Comp.Session.IsClient || playerId == Comp.Session.PlayerId; // this method is used both by initiators and by receives. 

                var state = Comp.Data.Repo.Values.State;
                var wasToggled = ShootToggled;

                //Log.Line($"sendRequest:{sendRequest} - wasToggled:{wasToggled} - modeChange:{modeChange} - weaponsToFire:{weaponsToFire}({WeaponsFired} < {TotalWeapons}) - newMousePress:{newMousePress} - newMouseRelease:{newMouseRelease} ");
                if (sendRequest)
                {
                    if (toggleMode || ShootToggled && modeChange)
                        ShootToggled = !ShootToggled;
                    else
                        ShootToggled = false;

                    var toggleChange = wasToggled && !ShootToggled;

                    if (toggleChange)
                    { // only run on initiators when this call is toggling off 

                        if (Comp.Session.MpActive)
                        {
                            RequestClientShootDelay = Comp.Session.IsClient; //if the initiators is a client pause future cycles until the server returns which cycle state to terminate on.
                            //Log.Line($"RequestClientShootDelay:{RequestClientShootDelay}");
                            ulong packagedMessage;
                            Session.EncodeShootState(state.ShootSyncStateId, 0, CompletedCycles, (uint)ShootCodes.ToggleServerOff, out packagedMessage);
                            Comp.Session.SendBurstRequest(Comp, packagedMessage, PacketType.ShootSync, RewriteShootSyncToServerResponse, playerId);
                        }

                        if (Comp.Session.IsServer)
                        {
                            ClearShootState();
                            Log.Line($"server for clear target");
                        }
                    }
                }

                if (Comp.IsDisabled || wasToggled || RequestClientShootDelay || WaitingBurstResponse || RequestShootBurstId != state.ShootSyncStateId || set.Overrides.BurstCount <= 0 || Comp.IsBlock && !Comp.Cube.IsWorking || !ReadyToShoot()) return; // check if already active and all weapons are in a clean ready state.
                //Log.Line($"success - totalWeapons:{TotalWeapons}");
                if (Comp.IsBlock && Comp.Session.HandlesInput)
                    Comp.Session.TerminalMon.HandleInputUpdate(Comp);

                RequestShootBurstId = state.ShootSyncStateId + 1;

                state.PlayerId = playerId;

                if (Comp.Session.MpActive && sendRequest)
                {
                    WaitingBurstResponse = Comp.Session.IsClient; // this will be set false on the client once the server responds to this packet

                    var code = Comp.Session.IsServer ? playerId == 0 ? ShootCodes.ServerRequest : ShootCodes.ServerRelay : ShootCodes.ClientRequest;
                    ulong packagedMessage;
                    Session.EncodeShootState(state.ShootSyncStateId, (uint)set.Overrides.ShootMode, CompletedCycles, (uint)code, out packagedMessage);

                    if (playerId > 0) // if this is the server responding to a request, rewrite the packet sent to the origin client with a special response code.
                        Comp.Session.SendBurstRequest(Comp, packagedMessage, PacketType.ShootSync, RewriteShootSyncToServerResponse, playerId);
                    else
                        Comp.Session.SendBurstRequest(Comp, packagedMessage, PacketType.ShootSync, null, playerId);
                }
            }

            internal void UpdateShootSync(Weapon w)
            {
                //var state = Data.Repo.Values.State;

                //Log.Line($"Before: ShootCount: {w.ShootCount} - WeaponsFired:{WeaponsFired} >= {TotalWeapons} - {state.ShootSyncStateId} vs {RequestShootBurstId} - CompletedCycles:{CompletedCycles} - lastCycle:{LastCycle} - ShootToggled:{ShootToggled}");
                if (--w.ShootCount == 0 && ++WeaponsFired >= Comp.TotalWeapons)
                {
                    w.ShootDelay = w.Comp.Data.Comp.Data.Repo.Values.Set.Overrides.BurstDelay;

                    if (!RequestClientShootDelay && (!ShootToggled && LastCycle == uint.MaxValue || ++CompletedCycles >= LastCycle))
                    {
                        ClearShootState();
                    }
                    else
                    {
                        ReadyToShoot(true);

                        if (RequestClientShootDelay)
                        {
                            RequestClientShootDelay = false;
                            FreezeClientShoot = LastCycle == uint.MaxValue;
                        }
                    }

                }

                //Log.Line($"After: ShootCount: {w.ShootCount} - WeaponsFired:{WeaponsFired} >= {TotalWeapons} - {state.ShootSyncStateId} vs {RequestShootBurstId} - CompletedCycles:{CompletedCycles} - lastCycle:{LastCycle} - ShootToggled:{ShootToggled}");
            }


            internal bool ReadyToShoot(bool skipReady = false)
            {
                var weaponsReady = 0;
                var totalWeapons = Comp.Collection.Count;
                var burstTarget = Comp.Data.Repo.Values.Set.Overrides.BurstCount;
                for (int i = 0; i < totalWeapons; i++)
                {
                    var w = Comp.Collection[i];
                    if (!w.System.DesignatorWeapon)
                    {
                        var aConst = w.ActiveAmmoDef.AmmoDef.Const;
                        var reloading = aConst.Reloadable && w.ClientMakeUpShots == 0 && (w.Loading || w.ProtoWeaponAmmo.CurrentAmmo == 0 || w.Reload.WaitForClient);
                        var canShoot = !w.PartState.Overheated && !reloading;
                        var weaponReady = canShoot && !w.IsShooting;

                        if (!weaponReady && !skipReady)
                            break;

                        weaponsReady += 1;

                        w.ShootCount = MathHelper.Clamp(burstTarget, 1, w.ProtoWeaponAmmo.CurrentAmmo + w.ClientMakeUpShots);
                    }
                    else
                        weaponsReady += 1;
                }

                var ready = weaponsReady == totalWeapons;

                if (!ready && weaponsReady > 0)
                    ClearShootState();

                return ready;
            }

            internal void ClearShootState()
            {
                for (int i = 0; i < Comp.TotalWeapons; i++)
                {
                    var w = Comp.Collection[i];
                    w.ShootCount = 0;
                    w.ShootDelay = 0;
                }

                if (Comp.Session.IsServer)
                {
                    var state = Comp.Data.Repo.Values.State;
                    var oldState = state.ShootSyncStateId;

                    if (RequestShootBurstId != 65535)
                        state.ShootSyncStateId = RequestShootBurstId;
                    else
                    {
                        state.ShootSyncStateId = 0;
                        RequestShootBurstId = 0;
                    }

                    if (Comp.Session.MpActive && oldState != state.ShootSyncStateId)
                        Comp.Session.SendState(Comp);
                }
                //Log.Line($"clear shoot state");
                CompletedCycles = 0;
                LastCycle = uint.MaxValue;
                ShootToggled = false;
                WeaponsFired = 0;
            }

            #endregion

            #region InputManager

            private bool ProcessInput()
            {

                return false;
            }

            #endregion

            #region Network
            internal void ServerToggleResponse(uint interval)
            {
                if (interval > CompletedCycles)
                {
                    Log.Line($"client had a higher interval than server: client: {interval} > server:{CompletedCycles}");
                    //LastCycle = interval;
                }
                //else LastCycle = CompletedCycles;

                LastCycle = CompletedCycles;
                var values = Comp.Data.Repo.Values;

                ulong packagedMessage;
                Session.EncodeShootState(values.State.ShootSyncStateId, (uint)values.Set.Overrides.ShootMode, LastCycle, (uint)ShootCodes.ToggleClientOff, out packagedMessage);
                Comp.Session.SendBurstRequest(Comp, packagedMessage, PacketType.ShootSync, null, 0);
                ClearShootState();
            }

            internal void ClientToggleResponse(uint interval)
            {
                Log.Line($"client received ToggleOff: CompletedCycles:{CompletedCycles} - lastCycle:{interval} - freeze: {FreezeClientShoot}({RequestClientShootDelay})");
                FreezeClientShoot = false;
                RequestClientShootDelay = false;

                if (CompletedCycles >= interval)
                {
                    ClearShootState();
                }
                else
                {
                    LastCycle = interval;
                }

            }

            private static object RewriteShootSyncToServerResponse(object o)
            {
                var ulongPacket = (ULongUpdatePacket)o;

                uint stateId;
                ShootModes mode;
                ShootCodes code;
                uint internval;

                Session.DecodeShootState(ulongPacket.Data, out stateId, out mode, out internval, out code);

                code = ShootCodes.ServerResponse;
                ulong packagedMessage;
                Session.EncodeShootState(stateId, (uint)mode, 0, (uint)code, out packagedMessage);

                ulongPacket.Data = packagedMessage;

                return ulongPacket;
            }

            #endregion
        }
    }
}
