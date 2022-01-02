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

                ++p.Info.Age;
                ++p.Info.Ai.MyProjectiles;
                p.Info.Ai.ProjectileTicker = p.Info.System.Session.Tick;
                if (p.Asleep)
                {
                    if (p.FieldTime > 300 && p.Info.Age % 100 != 0)
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

                        if ((p.Info.Age % 60 == 0 || (p.FakeGravityNear || p.EntitiesNear) && p.Info.Age % 10 == 0) && p.Info.Age > 0) {

                            float interference;
                            p.Gravity = Session.Physics.CalculateNaturalGravityAt(p.Position, out interference);
                            p.FakeGravityNear = !p.Info.InPlanetGravity;
                            p.EntitiesNear = false;
                        }

                        if (MyUtils.IsValid(p.Gravity) && !MyUtils.IsZero(ref p.Gravity)) {
                            p.Velocity += (p.Gravity * p.Info.AmmoDef.Const.GravityMultiplier) * Projectile.StepConst;
                            Vector3D.Normalize(ref p.Velocity, out p.Info.Direction);
                        }
                    }


                    if (p.DeltaVelocityPerTick > 0 && !p.Info.EwarAreaPulse) {

                        if (p.SmartsOn) p.RunSmart();
                        else {

                            var accel = true;
                            Vector3D newVel;
                            if (p.FieldTime > 0) {

                                var distToMax = p.Info.MaxTrajectory - p.Info.DistanceTraveled;

                                var stopDist = p.VelocityLengthSqr / 2 / (p.AccelInMetersPerSec);
                                if (distToMax <= stopDist)
                                    accel = false;

                                newVel = accel ? p.Velocity + p.AccelVelocity : p.Velocity - p.AccelVelocity;
                                p.VelocityLengthSqr = newVel.LengthSquared();

                                if (accel && p.VelocityLengthSqr > p.MaxSpeedSqr) newVel = p.Info.Direction * p.MaxSpeed;
                                else if (!accel && distToMax <= 0) {
                                    newVel = Vector3D.Zero;
                                    p.VelocityLengthSqr = 0;
                                }
                            }
                            else {
                                newVel = p.Velocity + p.AccelVelocity;
                                p.VelocityLengthSqr = newVel.LengthSquared();
                                if (p.VelocityLengthSqr > p.MaxSpeedSqr) newVel = p.Info.Direction * p.MaxSpeed;
                            }

                            p.Velocity = newVel;
                        }
                    }

                    if (p.State == ProjectileState.OneAndDone) {

                        p.LastPosition = p.Position;
                        var beamEnd = p.Position + (p.Info.Direction * p.Info.MaxTrajectory);
                        p.TravelMagnitude = p.Position - beamEnd;
                        p.Position = beamEnd;
                    }
                    else {

                        if (p.ConstantSpeed || p.VelocityLengthSqr > 0)
                            p.LastPosition = p.Position;

                        p.TravelMagnitude = p.Info.Age != 0 ? p.Velocity * StepConst : p.InitalStep;
                        p.Position += p.TravelMagnitude;
                    }

                    p.Info.PrevDistanceTraveled = p.Info.DistanceTraveled;

                    double distChanged;
                    Vector3D.Dot(ref p.Info.Direction, ref p.TravelMagnitude, out distChanged);
                    p.Info.DistanceTraveled += Math.Abs(distChanged);
                    if (p.Info.DistanceTraveled <= 500) ++p.Info.Ai.ProInMinCacheRange;

                    if (p.DynamicGuidance) {
                        if (p.PruningProxyId != -1) {
                            var sphere = new BoundingSphereD(p.Position, p.Info.AmmoDef.Const.LargestHitSize);
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
                    MatrixD.CreateWorld(ref p.Position, ref p.Info.Direction, ref up, out matrix);

                    if (p.Info.AmmoDef.Const.PrimeModel)
                        p.Info.AvShot.PrimeMatrix = matrix;
                    if (p.Info.AmmoDef.Const.TriggerModel && p.Info.TriggerGrowthSteps < p.Info.AmmoDef.Const.EwarRadius) 
                        p.Info.TriggerMatrix = matrix;
                }

                if (p.State != ProjectileState.OneAndDone)
                {
                    if (p.Info.Age > p.Info.AmmoDef.Const.MaxLifeTime) {
                        p.DistanceToTravelSqr = p.Info.DistanceTraveled * p.Info.DistanceTraveled;
                        p.EarlyEnd = true;
                    }

                    if (p.Info.DistanceTraveled * p.Info.DistanceTraveled >= p.DistanceToTravelSqr) {

                        p.AtMaxRange = !p.MineSeeking;
                        if (p.FieldTime > 0) {

                            p.FieldTime--;
                            if (p.Info.AmmoDef.Const.IsMine && !p.MineSeeking && !p.MineActivated) {
                                if (p.EnableAv) p.Info.AvShot.Cloaked = p.Info.AmmoDef.Trajectory.Mines.Cloak;
                                p.MineSeeking = true;
                            }
                        }
                    }
                }
                else p.AtMaxRange = true;

                if (p.Info.AmmoDef.Const.Ewar)
                    p.RunEwar();

                if (!p.Info.IsShrapnel && !p.DynamicGuidance && p.Info.Target.TargetEntity != null)
                {
                    var distSqrToTarget = Vector3D.DistanceSquared(p.Info.Target.TargetEntity.PositionComp.WorldAABB.Center, p.Position);
                    if (distSqrToTarget < p.Info.ClosestDistSqrToTarget || p.Info.ClosestDistSqrToTarget < 0)
                    {
                        p.Info.ClosestDistSqrToTarget = distSqrToTarget;
                        p.Info.PrevTargetPos = p.Info.Target.TargetEntity.PositionComp.WorldAABB.Center;
                    }
                    else if (p.Info.ClosestDistSqrToTarget > 0 && p.Info.ClosestDistSqrToTarget < distSqrToTarget)
                    {
                        p.Info.ClosestDistSqrToTarget = 0;
                        p.Info.WeaponCache.MissDistance = (p.Info.PrevTargetPos - p.LastPosition).Length();
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

                if (p.Info.AmmoDef.Const.IsBeamWeapon)
                    ++_beamCount;

                if (p.Info.Ai.ProInMinCacheRange > 99999 && !p.Info.Ai.AccelChecked)
                    p.Info.Ai.ComputeAccelSphere();

                p.UseEntityCache = p.Info.Ai.AccelChecked && p.Info.DistanceTraveled <= p.Info.Ai.NearByEntitySphere.Radius && !p.Info.Ai.MarkedForClose;
                var triggerRange = p.Info.AmmoDef.Const.EwarTriggerRange > 0 && !p.Info.EwarAreaPulse ? p.Info.AmmoDef.Const.EwarTriggerRange : 0;
                var useEwarSphere = (triggerRange > 0 || p.Info.EwarActive) && p.Info.AmmoDef.Const.Pulse;
                p.Beam = useEwarSphere ? new LineD(p.Position + (-p.Info.Direction * p.Info.AmmoDef.Const.EwarTriggerRange), p.Position + (p.Info.Direction * p.Info.AmmoDef.Const.EwarTriggerRange)) : new LineD(p.LastPosition, p.Position);

                if ((p.FieldTime <= 0 && p.State != ProjectileState.OneAndDone && p.Info.DistanceTraveled * p.Info.DistanceTraveled >= p.DistanceToTravelSqr)) {
                    
                    p.PruneSphere.Center = p.Position;
                    p.PruneSphere.Radius = p.Info.AmmoDef.Const.EndOfLifeRadius;

                    var dInfo = p.Info.AmmoDef.AreaOfDamage.EndOfLife;
                    if (p.MoveToAndActivate || dInfo.Enable && p.Info.Age >= dInfo.MinArmingTime && (!dInfo.ArmOnlyOnHit || p.Info.ObjectsHit > 0)) {

                        if (!p.UseEntityCache)
                            MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref p.PruneSphere, p.MyEntityList, p.PruneQuery);

                        if (p.Info.System.TrackProjectile)
                            foreach (var lp in p.Info.Ai.LiveProjectile)
                                if (p.PruneSphere.Contains(lp.Position) != ContainmentType.Disjoint && lp != p.Info.Target.Projectile)
                                    ProjectileHit(p, lp, p.Info.AmmoDef.Const.CollisionIsLine, ref p.Beam);

                        p.State = ProjectileState.Detonate;

                        if (p.EnableAv)
                            p.Info.AvShot.ForceHitParticle = true;
                    }
                    else
                        p.State = ProjectileState.Detonate;

                    p.EarlyEnd = true;
                    p.Info.Hit.SurfaceHit = p.Position;
                    p.Info.Hit.LastHit = p.Position;
                }

                p.SphereCheck = false;
                p.LineCheck = false;

                if (p.MineSeeking && !p.MineTriggered)
                    p.SeekEnemy();
                else if (useEwarSphere) {
                    if (p.Info.EwarActive) {
                        p.PruneSphere = new BoundingSphereD(p.Position, 0).Include(new BoundingSphereD(p.LastPosition, 0));
                        var currentRadius = p.Info.TriggerGrowthSteps < p.Info.AmmoDef.Const.EwarRadius ? p.Info.TriggerMatrix.Scale.AbsMax() : p.Info.AmmoDef.Const.EwarRadius;
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
                else if (p.Info.AmmoDef.Const.CollisionIsLine) {
                    p.PruneSphere.Center = p.Position;
                    p.PruneSphere.Radius = p.Info.AmmoDef.Const.CollisionSize;
                    if (p.Info.AmmoDef.Const.IsBeamWeapon || p.PruneSphere.Contains(p.DeadSphere) == ContainmentType.Disjoint)
                        p.LineCheck = true;
                }
                else {
                    p.SphereCheck = true;
                    p.PruneSphere = new BoundingSphereD(p.Position, 0).Include(new BoundingSphereD(p.LastPosition, 0));
                    if (p.PruneSphere.Radius < p.Info.AmmoDef.Const.CollisionSize) {
                        p.PruneSphere.Center = p.Position;
                        p.PruneSphere.Radius = p.Info.AmmoDef.Const.CollisionSize;
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
                
                p.Info.ShieldBypassed = p.Info.ShieldKeepBypass;
                p.Info.ShieldKeepBypass = false;

                if (p.Info.Target.IsProjectile || p.UseEntityCache && p.Info.Ai.NearByEntityCache.Count > 0 || p.CheckType == CheckTypes.Ray && p.MySegmentList.Count > 0 || p.CheckType == CheckTypes.Sphere && p.MyEntityList.Count > 0) {
                    ValidateHits.Add(p);
                }
                else if (p.MineSeeking && !p.MineTriggered && p.Info.Age - p.ChaseAge > 600)
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
                if (p.Info.AmmoDef.Const.VirtualBeams) {

                    Vector3D? hitPos = null;
                    if (!Vector3D.IsZero(p.Info.Hit.SurfaceHit)) hitPos = p.Info.Hit.SurfaceHit;
                    for (int v = 0; v < p.VrPros.Count; v++) {

                        var vp = p.VrPros[v];
                        var vs = vp.AvShot;

                        vp.TracerLength = p.Info.TracerLength;
                        vs.Init(vp, p.SmartsOn, p.AccelInMetersPerSec * StepConst, p.MaxSpeed, ref p.AccelDir);

                        if (p.Info.BaseDamagePool <= 0 || p.State == ProjectileState.Depleted)
                            vs.ProEnded = true;

                        vs.Hit = p.Info.Hit;

                        if (p.Info.AmmoDef.Const.ConvergeBeams) {
                            var beam = p.Intersecting ? new LineD(vs.Origin, hitPos ?? p.Position) : new LineD(vs.Origin, p.Position);
                            p.Info.System.Session.Projectiles.DeferedAvDraw.Add(new DeferedAv { AvShot = vs, StepSize = p.Info.DistanceTraveled - p.Info.PrevDistanceTraveled, VisualLength = beam.Length, TracerFront = beam.To, ShortStepSize = beam.Length, Hit = p.Intersecting, TriggerGrowthSteps = p.Info.TriggerGrowthSteps, Direction = beam.Direction });
                        }
                        else {
                            Vector3D beamEnd;
                            var hit = p.Intersecting && hitPos.HasValue;
                            if (!hit)
                                beamEnd = vs.Origin + (vp.Direction * p.Info.MaxTrajectory);
                            else
                                beamEnd = vs.Origin + (vp.Direction * p.Info.WeaponCache.HitDistance);

                            var line = new LineD(vs.Origin, beamEnd, !hit ? p.Info.MaxTrajectory : p.Info.WeaponCache.HitDistance);
                            if (p.Intersecting && hitPos.HasValue)
                                p.Info.System.Session.Projectiles.DeferedAvDraw.Add(new DeferedAv { AvShot = vs, StepSize = p.Info.DistanceTraveled - p.Info.PrevDistanceTraveled, VisualLength = line.Length, TracerFront = line.To, ShortStepSize = line.Length, Hit = true, TriggerGrowthSteps = p.Info.TriggerGrowthSteps, Direction = line.Direction });
                            else
                                p.Info.System.Session.Projectiles.DeferedAvDraw.Add(new DeferedAv { AvShot = vs, StepSize = p.Info.DistanceTraveled - p.Info.PrevDistanceTraveled, VisualLength = line.Length, TracerFront = line.To, ShortStepSize = line.Length, Hit = false, TriggerGrowthSteps = p.Info.TriggerGrowthSteps, Direction = line.Direction  });
                        }
                    }
                    continue;
                }

                if (!p.EnableAv) continue;

                if (p.Intersecting) {

                    if (p.Info.AmmoDef.Const.DrawLine || p.Info.AmmoDef.Const.PrimeModel || p.Info.AmmoDef.Const.TriggerModel) {
                        var useCollisionSize = p.ModelState == EntityState.None && p.Info.AmmoDef.Const.AmmoParticle && !p.Info.AmmoDef.Const.DrawLine;
                        p.Info.AvShot.TestSphere.Center = p.Info.Hit.LastHit;
                        p.Info.AvShot.ShortStepAvUpdate(p.Info, useCollisionSize, true, p.EarlyEnd, p.Position);
                    }

                    if (p.Info.BaseDamagePool <= 0 || p.State == ProjectileState.Depleted)
                        p.Info.AvShot.ProEnded = true;

                    p.Intersecting = false;
                    continue;
                }

                if ((int)p.State > 3)
                    continue;

                if (p.LineOrNotModel)
                {
                    if (p.State == ProjectileState.OneAndDone)
                        DeferedAvDraw.Add(new DeferedAv { AvShot = p.Info.AvShot, StepSize = p.Info.MaxTrajectory, VisualLength = p.Info.MaxTrajectory, TracerFront = p.Position, TriggerGrowthSteps = p.Info.TriggerGrowthSteps, Direction = p.Info.Direction });
                    else if (p.ModelState == EntityState.None && p.Info.AmmoDef.Const.AmmoParticle && !p.Info.AmmoDef.Const.DrawLine)
                    {
                        if (p.AtMaxRange) p.Info.AvShot.ShortStepAvUpdate(p.Info,true, false, p.EarlyEnd, p.Position);
                        else
                            DeferedAvDraw.Add(new DeferedAv { AvShot = p.Info.AvShot, StepSize = p.Info.DistanceTraveled - p.Info.PrevDistanceTraveled, VisualLength = p.Info.AmmoDef.Const.CollisionSize, TracerFront = p.Position, TriggerGrowthSteps = p.Info.TriggerGrowthSteps, Direction = p.Info.Direction});
                    }
                    else
                    {
                        var dir = (p.Velocity - p.StartSpeed) * StepConst;
                        double distChanged;
                        Vector3D.Dot(ref p.Info.Direction, ref dir, out distChanged);

                        p.Info.ProjectileDisplacement += Math.Abs(distChanged);
                        var displaceDiff = p.Info.ProjectileDisplacement - p.Info.TracerLength;
                        if (p.Info.ProjectileDisplacement < p.Info.TracerLength && Math.Abs(displaceDiff) > 0.0001)
                        {
                            if (p.AtMaxRange) p.Info.AvShot.ShortStepAvUpdate(p.Info,false, false, p.EarlyEnd, p.Position);
                            else
                            {
                                DeferedAvDraw.Add(new DeferedAv { AvShot = p.Info.AvShot, StepSize = p.Info.DistanceTraveled - p.Info.PrevDistanceTraveled, VisualLength = p.Info.ProjectileDisplacement, TracerFront = p.Position, TriggerGrowthSteps = p.Info.TriggerGrowthSteps, Direction = p.Info.Direction});
                            }
                        }
                        else
                        {
                            if (p.AtMaxRange) p.Info.AvShot.ShortStepAvUpdate(p.Info, false, false, p.EarlyEnd, p.Position);
                            else
                                DeferedAvDraw.Add(new DeferedAv { AvShot = p.Info.AvShot, StepSize = p.Info.DistanceTraveled - p.Info.PrevDistanceTraveled, VisualLength = p.Info.TracerLength, TracerFront = p.Position, TriggerGrowthSteps = p.Info.TriggerGrowthSteps, Direction = p.Info.Direction });
                        }
                    }
                }

                if (p.Info.AvShot.ModelOnly)
                    DeferedAvDraw.Add(new DeferedAv { AvShot = p.Info.AvShot, StepSize = p.Info.DistanceTraveled - p.Info.PrevDistanceTraveled, VisualLength = p.Info.TracerLength, TracerFront = p.Position, TriggerGrowthSteps = p.Info.TriggerGrowthSteps, Direction = p.Info.Direction });
            }
        }
    }
}
