using System;
using System.Collections.Generic;
using CoreSystems.Support;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Collections;
using VRage.Game;
using VRage.Utils;
using VRageMath;
using static CoreSystems.Projectiles.Projectile;
using static CoreSystems.Support.AvShot;

namespace CoreSystems.Projectiles
{
    public partial class Projectiles
    {
        private const float StepConst = MyEngineConstants.PHYSICS_STEP_SIZE_IN_SECONDS;
        internal readonly Session Session;
        internal readonly MyConcurrentPool<List<NewVirtual>> VirtInfoPools = new MyConcurrentPool<List<NewVirtual>>(128, vInfo => vInfo.Clear());
        internal readonly MyConcurrentPool<ProInfo> VirtInfoPool = new MyConcurrentPool<ProInfo>(128, vInfo => vInfo.Clean());
        internal readonly MyConcurrentPool<Fragments> ShrapnelPool = new MyConcurrentPool<Fragments>(32);
        internal readonly MyConcurrentPool<Fragment> FragmentPool = new MyConcurrentPool<Fragment>(32);
        internal readonly MyConcurrentPool<HitEntity> HitEntityPool = new MyConcurrentPool<HitEntity>(32, hitEnt => hitEnt.Clean());

        internal readonly ConcurrentCachingList<Projectile> FinalHitCheck = new ConcurrentCachingList<Projectile>(128);
        internal readonly ConcurrentCachingList<Projectile> ValidateHits = new ConcurrentCachingList<Projectile>(128);
        internal readonly ConcurrentCachingList<DeferedVoxels> DeferedVoxels = new ConcurrentCachingList<DeferedVoxels>(128);
        internal readonly List<Projectile> AddTargets = new List<Projectile>();
        internal readonly List<Fragments> ShrapnelToSpawn = new List<Fragments>(32);
        internal readonly List<Projectile> ActiveProjetiles = new List<Projectile>(2048);
        internal readonly List<DeferedAv> DeferedAvDraw = new List<DeferedAv>(256);
        internal readonly List<NewProjectile> NewProjectiles = new List<NewProjectile>(256);
        internal readonly Stack<Projectile> ProjectilePool = new Stack<Projectile>(2048);

        internal ulong CurrentProjectileId;
        internal Projectiles(Session session)
        {
            Session = session;
        }

        internal void SpawnAndMove() // Methods highly inlined due to keen's mod profiler
        {
            Session.StallReporter.Start("GenProjectiles", 11);
            if (NewProjectiles.Count > 0) GenProjectiles();
            Session.StallReporter.End();

            Session.StallReporter.Start("AddTargets", 11);
            if (AddTargets.Count > 0)
                AddProjectileTargets();
            Session.StallReporter.End();

            Session.StallReporter.Start($"UpdateState: {ActiveProjetiles.Count}", 11);
            if (ActiveProjetiles.Count > 0) 
                UpdateState();
            Session.StallReporter.End();

            Session.StallReporter.Start($"Spawn: {ShrapnelToSpawn.Count}", 11);
            if (ShrapnelToSpawn.Count > 0)
                SpawnFragments();
            Session.StallReporter.End();
        }

        internal void Intersect() // Methods highly inlined due to keen's mod profiler
        {
            Session.StallReporter.Start($"CheckHits: {ActiveProjetiles.Count}", 11);
            if (ActiveProjetiles.Count > 0)
                CheckHits();
            Session.StallReporter.End();

            if (!ValidateHits.IsEmpty) {

                Session.StallReporter.Start($"InitialHitCheck: {ValidateHits.Count} - beamCount:{_beamCount}", 11);
                InitialHitCheck();
                Session.StallReporter.End();

                Session.StallReporter.Start($"DeferedVoxelCheck: {ValidateHits.Count} - beamCount:{_beamCount} ", 11);
                DeferedVoxelCheck();
                Session.StallReporter.End();

                Session.StallReporter.Start($"FinalizeHits: {ValidateHits.Count} - beamCount:{_beamCount}", 11);
                FinalizeHits();
                Session.StallReporter.End();
            }
        }

        internal void Damage()
        {
            if (Session.EffectedCubes.Count > 0)
                Session.ApplyGridEffect();

            if (Session.Tick60)
                Session.GridEffects();

            if (Session.IsClient && Session.CurrentClientEwaredCubes.Count > 0 && (Session.ClientEwarStale || Session.Tick120))
                Session.SyncClientEwarBlocks();

            if (Session.Hits.Count > 0) Session.ProcessHits();
        }

        internal void AvUpdate()
        {
            if (!Session.DedicatedServer)
            {
                Session.StallReporter.Start($"AvUpdate: {ActiveProjetiles.Count}", 11);
                UpdateAv();
                DeferedAvStateUpdates(Session);
                Session.StallReporter.End();
            }
        }

        private void UpdateState(int end = 0)
        {
            for (int i = ActiveProjetiles.Count - 1; i >= end; i--)
            {
                var p = ActiveProjetiles[i];
                var info = p.Info;
                var aConst = info.AmmoDef.Const;
                var target = info.Target;
                var ai = p.Info.Ai;
                ++info.Age;
                ++ai.MyProjectiles;
                ai.ProjectileTicker = Session.Tick;
                if (p.Asleep)
                {
                    if (p.FieldTime > 300 && info.Age % 100 != 0)
                    {
                        p.FieldTime--;
                        continue;
                    }
                    p.Asleep = false;
                }
                switch (p.State) {
                    case ProjectileState.Destroy:
                        p.DestroyProjectile();
                        continue;
                    case ProjectileState.Dead:
                        continue;
                    case ProjectileState.OneAndDone:
                    case ProjectileState.Depleted:
                    case ProjectileState.Detonate:
                        if (p.Info.Age == 0 && p.State == ProjectileState.OneAndDone)
                            break;

                        p.ProjectileClose();
                        ProjectilePool.Push(p);
                        ActiveProjetiles.RemoveAtFast(i);
                        continue;
                }

                if (p.Info.Target.IsProjectile)
                    if (p.Info.Target.Projectile.State != ProjectileState.Alive)
                        p.UnAssignProjectile(true);

                if (!p.AtMaxRange) {

                    if (p.FeelsGravity) {

                        if ((info.Age % 60 == 0 || (p.FakeGravityNear || p.EntitiesNear) && info.Age % 10 == 0) && info.Age > 0) {

                            float interference;
                            p.Gravity = Session.Physics.CalculateNaturalGravityAt(p.Position, out interference);
                            p.FakeGravityNear = !info.InPlanetGravity;
                            p.EntitiesNear = false;
                        }

                        if (MyUtils.IsValid(p.Gravity) && !MyUtils.IsZero(ref p.Gravity)) {
                            p.Velocity += (p.Gravity * aConst.GravityMultiplier) * Projectile.StepConst;
                            Vector3D.Normalize(ref p.Velocity, out p.Info.Direction);
                        }
                    }


                    if (p.DeltaVelocityPerTick > 0 && !info.EwarAreaPulse) {

                        if (p.SmartsOn) p.RunSmart();
                        else {

                            var accel = true;
                            Vector3D newVel;
                            if (p.FieldTime > 0) {

                                var distToMax = info.MaxTrajectory - info.DistanceTraveled;

                                var stopDist = p.VelocityLengthSqr / 2 / (p.AccelInMetersPerSec);
                                if (distToMax <= stopDist)
                                    accel = false;

                                newVel = accel ? p.Velocity + p.AccelVelocity : p.Velocity - p.AccelVelocity;
                                p.VelocityLengthSqr = newVel.LengthSquared();

                                if (accel && p.VelocityLengthSqr > p.MaxSpeedSqr) newVel = info.Direction * p.MaxSpeed;
                                else if (!accel && distToMax <= 0) {
                                    newVel = Vector3D.Zero;
                                    p.VelocityLengthSqr = 0;
                                }
                            }
                            else {
                                newVel = p.Velocity + p.AccelVelocity;
                                p.VelocityLengthSqr = newVel.LengthSquared();
                                if (p.VelocityLengthSqr > p.MaxSpeedSqr) newVel = info.Direction * p.MaxSpeed;
                            }

                            p.Velocity = newVel;
                        }
                    }

                    if (p.State == ProjectileState.OneAndDone) {

                        p.LastPosition = p.Position;
                        var beamEnd = p.Position + (info.Direction * info.MaxTrajectory);
                        p.TravelMagnitude = p.Position - beamEnd;
                        p.Position = beamEnd;
                    }
                    else {

                        if (p.ConstantSpeed || p.VelocityLengthSqr > 0)
                            p.LastPosition = p.Position;

                        p.TravelMagnitude = info.Age != 0 ? p.Velocity * StepConst : p.InitalStep;
                        p.Position += p.TravelMagnitude;

                        if (aConst.TimedFragments && aConst.FragStartTime > info.Age && info.Age - info.LastFragTime >= aConst.FragInterval)
                        {
                            if (!aConst.HasFragProximity)
                                p.SpawnShrapnel();
                            else if (target.TargetEntity != null)
                            {
                                var inflatedSize = aConst.FragProximity + target.TargetEntity.PositionComp.LocalVolume.Radius;
                                if (Vector3D.DistanceSquared(target.TargetEntity.PositionComp.WorldAABB.Center, p.Position) <= inflatedSize * inflatedSize)
                                    p.SpawnShrapnel();
                            }
                        }
                    }

                    info.PrevDistanceTraveled = info.DistanceTraveled;

                    double distChanged;
                    Vector3D.Dot(ref info.Direction, ref p.TravelMagnitude, out distChanged);
                    info.DistanceTraveled += Math.Abs(distChanged);
                    if (info.DistanceTraveled <= 500) ++ai.ProInMinCacheRange;

                    if (p.DynamicGuidance) {
                        if (p.PruningProxyId != -1) {
                            var sphere = new BoundingSphereD(p.Position, aConst.LargestHitSize);
                            BoundingBoxD result;
                            BoundingBoxD.CreateFromSphere(ref sphere, out result);
                            var displacement = 0.1 * p.Velocity;
                            Session.ProjectileTree.MoveProxy(p.PruningProxyId, ref result, displacement);
                        }
                    }
                }
                if (p.ModelState == EntityState.Exists) {

                    var up = MatrixD.Identity.Up;
                    MatrixD matrix;
                    MatrixD.CreateWorld(ref p.Position, ref info.Direction, ref up, out matrix);

                    if (aConst.PrimeModel)
                        info.AvShot.PrimeMatrix = matrix;
                    if (aConst.TriggerModel && info.TriggerGrowthSteps < aConst.EwarRadius) 
                        info.TriggerMatrix = matrix;
                }

                if (p.State != ProjectileState.OneAndDone)
                {
                    if (info.Age > aConst.MaxLifeTime) {
                        p.DistanceToTravelSqr = info.DistanceTraveled * info.DistanceTraveled;
                        p.EarlyEnd = true;
                    }

                    if (info.DistanceTraveled * info.DistanceTraveled >= p.DistanceToTravelSqr) {

                        p.AtMaxRange = !p.MineSeeking;
                        if (p.FieldTime > 0) {

                            p.FieldTime--;
                            if (aConst.IsMine && !p.MineSeeking && !p.MineActivated) {
                                if (p.EnableAv) info.AvShot.Cloaked = p.Info.AmmoDef.Trajectory.Mines.Cloak;
                                p.MineSeeking = true;
                            }
                        }
                    }
                }
                else p.AtMaxRange = true;

                if (aConst.Ewar)
                    p.RunEwar();

                if (!info.IsShrapnel && !p.DynamicGuidance && target.TargetEntity != null)
                {
                    var distSqrToTarget = Vector3D.DistanceSquared(target.TargetEntity.PositionComp.WorldAABB.Center, p.Position);
                    if (distSqrToTarget < info.ClosestDistSqrToTarget || info.ClosestDistSqrToTarget < 0)
                    {
                        info.ClosestDistSqrToTarget = distSqrToTarget;
                        info.PrevTargetPos = target.TargetEntity.PositionComp.WorldAABB.Center;
                    }
                    else if (info.ClosestDistSqrToTarget > 0 && info.ClosestDistSqrToTarget < distSqrToTarget)
                    {
                        info.ClosestDistSqrToTarget = 0;
                        info.WeaponCache.MissDistance = (p.Info.PrevTargetPos - p.LastPosition).Length();
                    }
                }
            }
        }

        private int _beamCount;
        private void CheckHits()
        {
            _beamCount = 0;
            for (int x = ActiveProjetiles.Count - 1; x >= 0; x--) {

                var p = ActiveProjetiles[x];
                
                if ((int) p.State > 3 || p.Asleep)
                    continue;

                var info = p.Info;
                var ai = info.Ai;
                var aDef = info.AmmoDef;
                var aConst = aDef.Const;
                if (aConst.IsBeamWeapon)
                    ++_beamCount;

                if (ai.ProInMinCacheRange > 99999 && !ai.AccelChecked)
                    ai.ComputeAccelSphere();

                p.UseEntityCache = ai.AccelChecked && info.DistanceTraveled <= ai.NearByEntitySphere.Radius && !ai.MarkedForClose;
                var triggerRange = aConst.EwarTriggerRange > 0 && !info.EwarAreaPulse ? aConst.EwarTriggerRange : 0;
                var useEwarSphere = (triggerRange > 0 || info.EwarActive) && aConst.Pulse;
                p.Beam = useEwarSphere ? new LineD(p.Position + (-info.Direction * aConst.EwarTriggerRange), p.Position + (info.Direction * aConst.EwarTriggerRange)) : new LineD(p.LastPosition, p.Position);

                if ((p.FieldTime <= 0 && p.State != ProjectileState.OneAndDone && info.DistanceTraveled * info.DistanceTraveled >= p.DistanceToTravelSqr)) {
                    
                    p.PruneSphere.Center = p.Position;
                    p.PruneSphere.Radius = aConst.EndOfLifeRadius;

                    if (p.MoveToAndActivate || aConst.EndOfLifeAoe && info.Age >= aConst.MinArmingTime && (!aConst.ArmOnlyOnHit || info.ObjectsHit > 0)) {

                        if (!p.UseEntityCache)
                            MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref p.PruneSphere, p.MyEntityList, p.PruneQuery);

                        if (info.System.TrackProjectile)
                            foreach (var lp in ai.LiveProjectile)
                                if (p.PruneSphere.Contains(lp.Position) != ContainmentType.Disjoint && lp != info.Target.Projectile)
                                    ProjectileHit(p, lp, aConst.CollisionIsLine, ref p.Beam);

                        p.State = ProjectileState.Detonate;

                        if (p.EnableAv)
                            info.AvShot.ForceHitParticle = true;
                    }
                    else
                        p.State = ProjectileState.Detonate;

                    p.EarlyEnd = true;
                    info.Hit.SurfaceHit = p.Position;
                    info.Hit.LastHit = p.Position;
                }

                p.SphereCheck = false;
                p.LineCheck = false;

                if (p.MineSeeking && !p.MineTriggered)
                    p.SeekEnemy();
                else if (useEwarSphere) {
                    if (info.EwarActive) {
                        p.PruneSphere = new BoundingSphereD(p.Position, 0).Include(new BoundingSphereD(p.LastPosition, 0));
                        var currentRadius = info.TriggerGrowthSteps < aConst.EwarRadius ? info.TriggerMatrix.Scale.AbsMax() : aConst.EwarRadius;
                        if (p.PruneSphere.Radius < currentRadius) {
                            p.PruneSphere.Center = p.Position;
                            p.PruneSphere.Radius = currentRadius;
                        }
                    }
                    else
                        p.PruneSphere = new BoundingSphereD(p.Position, triggerRange);

                    if (p.PruneSphere.Contains(p.DeadSphere) == ContainmentType.Disjoint)
                        p.SphereCheck = true;
                }
                else if (aConst.CollisionIsLine) {
                    p.PruneSphere.Center = p.Position;
                    p.PruneSphere.Radius = aConst.CollisionSize;
                    if (aConst.IsBeamWeapon || p.PruneSphere.Contains(p.DeadSphere) == ContainmentType.Disjoint)
                        p.LineCheck = true;
                }
                else {
                    p.SphereCheck = true;
                    p.PruneSphere = new BoundingSphereD(p.Position, 0).Include(new BoundingSphereD(p.LastPosition, 0));
                    if (p.PruneSphere.Radius < aConst.CollisionSize) {
                        p.PruneSphere.Center = p.Position;
                        p.PruneSphere.Radius = aConst.CollisionSize;
                    }
                }
            }

            var apCount = ActiveProjetiles.Count;
            var minCount = Session.Settings.Enforcement.BaseOptimizations ? 96 : 99999;
            var stride = apCount < minCount ? 100000 : 48;

            MyAPIGateway.Parallel.For(0, apCount, i =>
            {
                var p = ActiveProjetiles[i];
                if (p.SphereCheck) {
                    if (p.DynamicGuidance && p.PruneQuery == MyEntityQueryType.Dynamic && Session.Tick60)
                        p.CheckForNearVoxel(60);

                    if (!p.UseEntityCache)
                        MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref p.PruneSphere, p.MyEntityList, p.PruneQuery);
                }
                else if (p.LineCheck) {
                    if (p.DynamicGuidance && p.PruneQuery == MyEntityQueryType.Dynamic && Session.Tick60) p.CheckForNearVoxel(60);

                    if (!p.UseEntityCache)
                        MyGamePruningStructure.GetTopmostEntitiesOverlappingRay(ref p.Beam, p.MySegmentList, p.PruneQuery);
                }

                p.CheckType = p.UseEntityCache && p.SphereCheck ? CheckTypes.CachedSphere : p.UseEntityCache ? CheckTypes.CachedRay : p.SphereCheck ? CheckTypes.Sphere : CheckTypes.Ray;

                var info = p.Info;
                info.ShieldBypassed = info.ShieldKeepBypass;
                info.ShieldKeepBypass = false;

                if (info.Target.IsProjectile || p.UseEntityCache && info.Ai.NearByEntityCache.Count > 0 || p.CheckType == CheckTypes.Ray && p.MySegmentList.Count > 0 || p.CheckType == CheckTypes.Sphere && p.MyEntityList.Count > 0) {
                    ValidateHits.Add(p);
                }
                else if (p.MineSeeking && !p.MineTriggered && info.Age - p.ChaseAge > 600)
                {
                    p.Asleep = true;
                }
            }, stride);
            ValidateHits.ApplyAdditions();
        }

        private void UpdateAv()
        {
            for (int x = ActiveProjetiles.Count - 1; x >= 0; x--) {

                var p = ActiveProjetiles[x];

                var info = p.Info;
                var aConst = info.AmmoDef.Const;
                if (aConst.VirtualBeams) {

                    Vector3D? hitPos = null;
                    if (!Vector3D.IsZero(info.Hit.SurfaceHit)) hitPos = info.Hit.SurfaceHit;
                    for (int v = 0; v < p.VrPros.Count; v++) {

                        var vp = p.VrPros[v];
                        var vs = vp.AvShot;

                        vp.TracerLength = info.TracerLength;
                        vs.Init(vp, p.SmartsOn, p.AccelInMetersPerSec * StepConst, p.MaxSpeed, ref p.AccelDir);

                        if (info.BaseDamagePool <= 0 || p.State == ProjectileState.Depleted)
                            vs.ProEnded = true;

                        vs.Hit = info.Hit;

                        if (aConst.ConvergeBeams) {
                            var beam = p.Intersecting ? new LineD(vs.Origin, hitPos ?? p.Position) : new LineD(vs.Origin, p.Position);
                            Session.Projectiles.DeferedAvDraw.Add(new DeferedAv { AvShot = vs, StepSize = info.DistanceTraveled - info.PrevDistanceTraveled, VisualLength = beam.Length, TracerFront = beam.To, ShortStepSize = beam.Length, Hit = p.Intersecting, TriggerGrowthSteps = info.TriggerGrowthSteps, Direction = beam.Direction });
                        }
                        else {
                            Vector3D beamEnd;
                            var hit = p.Intersecting && hitPos.HasValue;
                            if (!hit)
                                beamEnd = vs.Origin + (vp.Direction * info.MaxTrajectory);
                            else
                                beamEnd = vs.Origin + (vp.Direction * info.WeaponCache.HitDistance);

                            var line = new LineD(vs.Origin, beamEnd, !hit ? info.MaxTrajectory : info.WeaponCache.HitDistance);
                            if (p.Intersecting && hitPos.HasValue)
                                Session.Projectiles.DeferedAvDraw.Add(new DeferedAv { AvShot = vs, StepSize = info.DistanceTraveled - info.PrevDistanceTraveled, VisualLength = line.Length, TracerFront = line.To, ShortStepSize = line.Length, Hit = true, TriggerGrowthSteps = info.TriggerGrowthSteps, Direction = line.Direction });
                            else
                                Session.Projectiles.DeferedAvDraw.Add(new DeferedAv { AvShot = vs, StepSize = info.DistanceTraveled - info.PrevDistanceTraveled, VisualLength = line.Length, TracerFront = line.To, ShortStepSize = line.Length, Hit = false, TriggerGrowthSteps = info.TriggerGrowthSteps, Direction = line.Direction  });
                        }
                    }
                    continue;
                }

                if (!p.EnableAv) continue;

                if (p.Intersecting) {

                    if (aConst.DrawLine || aConst.PrimeModel || aConst.TriggerModel) {
                        var useCollisionSize = p.ModelState == EntityState.None && aConst.AmmoParticle && !aConst.DrawLine;
                        info.AvShot.TestSphere.Center = info.Hit.LastHit;
                        info.AvShot.ShortStepAvUpdate(info, useCollisionSize, true, p.EarlyEnd, p.Position);
                    }

                    if (info.BaseDamagePool <= 0 || p.State == ProjectileState.Depleted)
                        info.AvShot.ProEnded = true;

                    p.Intersecting = false;
                    continue;
                }

                if ((int)p.State > 3)
                    continue;

                if (p.LineOrNotModel)
                {
                    if (p.State == ProjectileState.OneAndDone)
                        DeferedAvDraw.Add(new DeferedAv { AvShot = info.AvShot, StepSize = info.MaxTrajectory, VisualLength = info.MaxTrajectory, TracerFront = p.Position, TriggerGrowthSteps = info.TriggerGrowthSteps, Direction = info.Direction });
                    else if (p.ModelState == EntityState.None && aConst.AmmoParticle && !aConst.DrawLine)
                    {
                        if (p.AtMaxRange) info.AvShot.ShortStepAvUpdate(p.Info,true, false, p.EarlyEnd, p.Position);
                        else
                            DeferedAvDraw.Add(new DeferedAv { AvShot = info.AvShot, StepSize = info.DistanceTraveled - info.PrevDistanceTraveled, VisualLength = aConst.CollisionSize, TracerFront = p.Position, TriggerGrowthSteps = info.TriggerGrowthSteps, Direction = info.Direction});
                    }
                    else
                    {
                        var dir = (p.Velocity - p.StartSpeed) * StepConst;
                        double distChanged;
                        Vector3D.Dot(ref info.Direction, ref dir, out distChanged);

                        info.ProjectileDisplacement += Math.Abs(distChanged);
                        var displaceDiff = info.ProjectileDisplacement - info.TracerLength;
                        if (info.ProjectileDisplacement < info.TracerLength && Math.Abs(displaceDiff) > 0.0001)
                        {
                            if (p.AtMaxRange) p.Info.AvShot.ShortStepAvUpdate(p.Info,false, false, p.EarlyEnd, p.Position);
                            else
                            {
                                DeferedAvDraw.Add(new DeferedAv { AvShot = p.Info.AvShot, StepSize = info.DistanceTraveled - info.PrevDistanceTraveled, VisualLength = info.ProjectileDisplacement, TracerFront = p.Position, TriggerGrowthSteps = info.TriggerGrowthSteps, Direction = info.Direction});
                            }
                        }
                        else
                        {
                            if (p.AtMaxRange) info.AvShot.ShortStepAvUpdate(info, false, false, p.EarlyEnd, p.Position);
                            else
                                DeferedAvDraw.Add(new DeferedAv { AvShot = info.AvShot, StepSize = info.DistanceTraveled - info.PrevDistanceTraveled, VisualLength = info.TracerLength, TracerFront = p.Position, TriggerGrowthSteps = info.TriggerGrowthSteps, Direction = info.Direction });
                        }
                    }
                }

                if (info.AvShot.ModelOnly)
                    DeferedAvDraw.Add(new DeferedAv { AvShot = info.AvShot, StepSize = info.DistanceTraveled - info.PrevDistanceTraveled, VisualLength = info.TracerLength, TracerFront = p.Position, TriggerGrowthSteps = info.TriggerGrowthSteps, Direction = info.Direction });
            }
        }
    }
}
