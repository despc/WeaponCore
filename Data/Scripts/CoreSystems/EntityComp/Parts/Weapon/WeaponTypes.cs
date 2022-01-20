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
                var ignoreTargets = Weapon.Target.IsProjectile || Weapon.Target.TargetEntity is IMyCharacter;
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

    }
}
