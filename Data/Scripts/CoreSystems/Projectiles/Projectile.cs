using System;
using System.Collections.Generic;
using CoreSystems.Support;
using Sandbox.Game.Entities;
using Sandbox.ModAPI.Ingame;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;
using static CoreSystems.Support.WeaponDefinition.AmmoDef.TrajectoryDef;
using static CoreSystems.Support.WeaponDefinition.AmmoDef.EwarDef.EwarType;
using static CoreSystems.Support.WeaponDefinition.AmmoDef.FragmentDef.TimedSpawnDef;

namespace CoreSystems.Projectiles
{
    internal class Projectile
    {
        internal const float StepConst = MyEngineConstants.PHYSICS_STEP_SIZE_IN_SECONDS;
        internal ProjectileState State;
        internal EntityState ModelState;
        internal MyEntityQueryType PruneQuery;
        internal CheckTypes CheckType;
        internal DroneStatus DroneStat;
        internal DroneMission DroneMsn;
        internal Vector3D AccelDir;
        internal Vector3D Position;
        internal Vector3D OffsetDir;
        internal Vector3D LastPosition;
        internal Vector3D StartSpeed;
        internal Vector3D Velocity;
        internal Vector3D PrevVelocity;
        internal Vector3D InitalStep;
        internal Vector3D AccelVelocity;
        internal Vector3D MaxVelocity;
        internal Vector3D TravelMagnitude;
        internal Vector3D LastEntityPos;
        internal Vector3D OriginTargetPos;
        internal Vector3D PredictedTargetPos;
        internal Vector3D PrevTargetPos;
        internal Vector3D TargetOffSet;
        internal Vector3D PrevTargetOffset;
        internal Vector3 PrevTargetVel;
        internal Vector3? LastHitEntVel;
        internal Vector3 Gravity;
        internal LineD Beam;
        internal BoundingSphereD PruneSphere;
        internal BoundingSphereD DeadSphere;
        internal MyOrientedBoundingBoxD ProjObb;
        internal double AccelInMetersPerSec;
        internal double DistanceToTravelSqr;
        internal double VelocityLengthSqr;
        internal double DistanceFromCameraSqr;
        internal double MaxSpeedSqr;
        internal double MaxSpeed;
        internal double MaxTrajectorySqr;
        internal double PrevEndPointToCenterSqr;
        internal float DesiredSpeed;
        internal int DeaccelRate;
        internal int ChaseAge;
        internal int EndStep;
        internal int ZombieLifeTime;
        internal int LastOffsetTime;
        internal int PruningProxyId = -1;
        internal int CachedId;
        internal int NewTargets;
        internal int SmartSlot;
        internal bool PickTarget;
        internal bool EnableAv;
        internal bool MoveToAndActivate;
        internal bool LockedTarget;
        internal bool DynamicGuidance;
        internal bool LinePlanetCheck;
        internal bool IsSmart;
        internal bool MineSeeking;
        internal bool MineActivated;
        internal bool MineTriggered;
        internal bool AtMaxRange;
        internal bool EarlyEnd;
        internal bool LineOrNotModel;
        internal bool EntitiesNear;
        internal bool FakeGravityNear;
        internal bool HadTarget;
        internal bool WasTracking;
        internal bool Intersecting;
        internal bool FinalizeIntersection;
        internal bool SphereCheck;
        internal bool LineCheck;
        internal bool Asleep;
        internal bool IsDrone;

        internal enum DroneStatus
        {
            Transit, //Movement from/to target area
            Approach, //Final transition between transit and orbit
            Orbit, //Orbit & shoot
            Strafe, //Nose at target movement, for PointType = direct and PointAtTarget = false
            Escape, //Move away from imminent collision
            Kamikaze,
            Return, //Return to "base"
            Dock,
        }
        internal enum DroneMission
        {
            Attack,
            Defend,
            RTB,
        }

        internal enum CheckTypes
        {
            Ray,
            Sphere,
            CachedSphere,
            CachedRay,
        }

        internal enum ProjectileState
        {
            Alive,
            Detonate,
            OneAndDone,
            Dead,
            Depleted,
            Destroy,
        }

        internal enum EntityState
        {
            Exists,
            None
        }

        internal readonly ProInfo Info = new ProInfo();
        internal readonly List<MyLineSegmentOverlapResult<MyEntity>> MySegmentList = new List<MyLineSegmentOverlapResult<MyEntity>>();
        internal readonly List<MyEntity> MyEntityList = new List<MyEntity>();
        internal readonly List<ProInfo> VrPros = new List<ProInfo>();
        internal readonly List<Projectile> EwaredProjectiles = new List<Projectile>();
        internal readonly List<Ai> Watchers = new List<Ai>();
        internal readonly HashSet<Projectile> Seekers = new HashSet<Projectile>();

        #region Start
        internal void Start()
        {
            var session = Info.System.Session;
            var ammoDef = Info.AmmoDef;
            var aConst = ammoDef.Const;

            if (aConst.FragmentPattern)
                Info.PatternShuffle = aConst.PatternShuffleArray.Count > 0 ? aConst.PatternShuffleArray.Pop() : new int[aConst.FragPatternCount];

            PrevVelocity = Vector3D.Zero;
            OffsetDir = Vector3D.Zero;
            Position = Info.Origin;
            AccelDir = Info.Direction;
            var cameraStart = session.CameraPos;
            Vector3D.DistanceSquared(ref cameraStart, ref Info.Origin, out DistanceFromCameraSqr);
            var probability = ammoDef.AmmoGraphics.VisualProbability;
            EnableAv = !aConst.VirtualBeams && !session.DedicatedServer && DistanceFromCameraSqr <= session.SyncDistSqr && (probability >= 1 || probability >= MyUtils.GetRandomDouble(0.0f, 1f));
            ModelState = EntityState.None;
            LastEntityPos = Position;
            LastHitEntVel = null;
            Info.AvShot = null;
            Info.Age = -1;
            ChaseAge = 0;
            NewTargets = 0;
            ZombieLifeTime = 0;
            LastOffsetTime = 0;
            PruningProxyId = -1;
            EntitiesNear = false;
            MineSeeking = false;
            MineActivated = false;
            MineTriggered = false;
            LinePlanetCheck = false;
            AtMaxRange = false;
            FakeGravityNear = false;
            HadTarget = false;
            WasTracking = false;
            Intersecting = false;
            Asleep = false;
            EndStep = 0;
            Info.PrevDistanceTraveled = 0;
            Info.DistanceTraveled = 0;
            PrevEndPointToCenterSqr = double.MaxValue;
            DroneStat = DroneStatus.Transit;
            DroneMsn = DroneMission.Attack;
            var trajectory = ammoDef.Trajectory;
            var guidance = trajectory.Guidance;
            CachedId = Info.MuzzleId == -1 ? Info.WeaponCache.VirutalId : Info.MuzzleId;
            DynamicGuidance = guidance != GuidanceType.None && guidance != GuidanceType.TravelTo && !aConst.IsBeamWeapon;
            if (DynamicGuidance && session.AntiSmartActive) DynTrees.RegisterProjectile(this);

            Info.MyPlanet = Info.Ai.MyPlanet;
            
            if (!session.VoxelCaches.TryGetValue(Info.UniqueMuzzleId, out Info.VoxelCache))
                Info.VoxelCache = session.VoxelCaches[ulong.MaxValue];

            if (Info.MyPlanet != null)
                Info.VoxelCache.PlanetSphere.Center = Info.Ai.ClosestPlanetCenter;

            Info.MyShield = Info.Ai.MyShield;
            Info.InPlanetGravity = Info.Ai.InPlanetGravity;
            Info.Ai.ProjectileTicker = Info.Ai.Session.Tick;

            IsDrone = guidance == GuidanceType.DroneAdvanced; //&& aConst.TimedFragments

            if (guidance == GuidanceType.Smart && DynamicGuidance)
            {
                IsSmart = true;
                SmartSlot = Info.Random.Range(0, 10);
            }
            else
            {
                IsSmart = false;
                SmartSlot = 0;
            }

            if (Info.Target.IsProjectile)
            {
                OriginTargetPos = Info.Target.Projectile.Position;
                Info.Target.Projectile.Seekers.Add(this);
            }
            else if (Info.Target.TargetEntity != null)
            {
                OriginTargetPos = Info.Target.TargetEntity.PositionComp.WorldAABB.Center;
                HadTarget = true;
            }
            else OriginTargetPos = Info.IsShrapnel ? PredictedTargetPos : Vector3D.Zero;
            LockedTarget = !Vector3D.IsZero(OriginTargetPos);

            if (IsSmart && aConst.TargetOffSet && (LockedTarget || Info.Target.IsFakeTarget))
            {
                OffSetTarget();
            }
            else
            {
                TargetOffSet = Vector3D.Zero;
            }

            PrevTargetOffset = Vector3D.Zero;

            var targetSpeed = (float)(!aConst.IsBeamWeapon ? aConst.DesiredProjectileSpeed : Info.MaxTrajectory * MyEngineConstants.UPDATE_STEPS_PER_SECOND);

            if (aConst.SpeedVariance && !aConst.IsBeamWeapon)
            {
                var min = trajectory.SpeedVariance.Start;
                var max = trajectory.SpeedVariance.End;
                var speedVariance = (float)Info.Random.NextDouble() * (max - min) + min;
                DesiredSpeed = targetSpeed + speedVariance;
            }
            else DesiredSpeed = targetSpeed;

            float variance = 0;
            if (aConst.RangeVariance)
            {
                var min = trajectory.RangeVariance.Start;
                var max = trajectory.RangeVariance.End;
                variance = (float)Info.Random.NextDouble() * (max - min) + min;
                Info.MaxTrajectory -= variance;
            }

            if (Vector3D.IsZero(PredictedTargetPos)) PredictedTargetPos = Position + (AccelDir * Info.MaxTrajectory);
            PrevTargetPos = PredictedTargetPos;
            PrevTargetVel = Vector3D.Zero;
            Info.ObjectsHit = 0;
            Info.BaseHealthPool = aConst.Health;
            Info.BaseEwarPool = aConst.Health;
            Info.TracerLength = aConst.TracerLength <= Info.MaxTrajectory ? aConst.TracerLength : Info.MaxTrajectory;

            MaxTrajectorySqr = Info.MaxTrajectory * Info.MaxTrajectory;

            if (!Info.IsShrapnel) StartSpeed = Info.ShooterVel;

            MoveToAndActivate = LockedTarget && !aConst.IsBeamWeapon && guidance == GuidanceType.TravelTo;

            if (MoveToAndActivate)
            {
                var distancePos = !Vector3D.IsZero(PredictedTargetPos) ? PredictedTargetPos : OriginTargetPos;
                if (!MyUtils.IsZero(variance))
                {
                    distancePos -= (AccelDir * variance);
                }
                Vector3D.DistanceSquared(ref Info.Origin, ref distancePos, out DistanceToTravelSqr);
            }
            else DistanceToTravelSqr = MaxTrajectorySqr;

            PickTarget = (aConst.OverrideTarget || Info.ModOverride && !LockedTarget) && !Info.Target.IsFakeTarget;
            if (PickTarget || LockedTarget && !Info.IsShrapnel) NewTargets++;

            var staticIsInRange = Info.Ai.ClosestStaticSqr * 0.5 < MaxTrajectorySqr;
            var pruneStaticCheck = Info.Ai.ClosestPlanetSqr * 0.5 < MaxTrajectorySqr || Info.Ai.StaticGridInRange;
            PruneQuery = (DynamicGuidance && pruneStaticCheck) || aConst.FeelsGravity && staticIsInRange || !DynamicGuidance && !aConst.FeelsGravity && staticIsInRange ? MyEntityQueryType.Both : MyEntityQueryType.Dynamic;

            if (Info.Ai.PlanetSurfaceInRange && Info.Ai.ClosestPlanetSqr <= MaxTrajectorySqr)
            {
                LinePlanetCheck = true;
                PruneQuery = MyEntityQueryType.Both;
            }

            if (DynamicGuidance && PruneQuery == MyEntityQueryType.Dynamic && staticIsInRange) CheckForNearVoxel(60);

            var accelPerSec = trajectory.AccelPerSec;
            AccelInMetersPerSec = !aConst.AmmoSkipAccel ? accelPerSec : DesiredSpeed;
            var desiredSpeed = (AccelDir * DesiredSpeed);
            var relativeSpeedCap = StartSpeed + desiredSpeed;
            MaxVelocity = relativeSpeedCap;
            MaxSpeed = MaxVelocity.Length();
            MaxSpeedSqr = MaxSpeed * MaxSpeed;
            AccelVelocity = (AccelDir * aConst.DeltaVelocityPerTick);

            if (aConst.AmmoSkipAccel)
            {
                Velocity = MaxVelocity;
                VelocityLengthSqr = MaxSpeed * MaxSpeed;
            }
            else Velocity = StartSpeed + AccelVelocity;

            if (Info.IsShrapnel)
                Vector3D.Normalize(ref Velocity, out Info.Direction);

            InitalStep = !Info.IsShrapnel && aConst.AmmoSkipAccel ? desiredSpeed * StepConst : Velocity * StepConst;

            TravelMagnitude = Velocity * StepConst;
            DeaccelRate = aConst.Ewar || aConst.IsMine ? trajectory.DeaccelTime : IsDrone? 100: 0;
            State = !aConst.IsBeamWeapon ? ProjectileState.Alive : ProjectileState.OneAndDone;

            if (EnableAv)
            {
                var originDir = !Info.IsShrapnel ? AccelDir : Info.Direction;
                Info.AvShot = session.Av.AvShotPool.Get();
                Info.AvShot.Init(Info, IsSmart, AccelInMetersPerSec * StepConst, MaxSpeed, ref originDir);
                Info.AvShot.SetupSounds(DistanceFromCameraSqr); //Pool initted sounds per Projectile type... this is expensive
                if (aConst.HitParticle && !aConst.IsBeamWeapon || aConst.EndOfLifeAoe && !ammoDef.AreaOfDamage.EndOfLife.NoVisuals)
                {
                    var hitPlayChance = Info.AmmoDef.AmmoGraphics.Particles.Hit.Extras.HitPlayChance;
                    Info.AvShot.HitParticleActive = hitPlayChance >= 1 || hitPlayChance >= MyUtils.GetRandomDouble(0.0f, 1f);
                }
            }

            if (!aConst.PrimeModel && !aConst.TriggerModel) ModelState = EntityState.None;
            else
            {
                if (EnableAv)
                {
                    ModelState = EntityState.Exists;

                    double triggerModelSize = 0;
                    double primeModelSize = 0;
                    if (aConst.TriggerModel) triggerModelSize = Info.AvShot.TriggerEntity.PositionComp.WorldVolume.Radius;
                    if (aConst.PrimeModel) primeModelSize = Info.AvShot.PrimeEntity.PositionComp.WorldVolume.Radius;
                    var largestSize = triggerModelSize > primeModelSize ? triggerModelSize : primeModelSize;

                    Info.AvShot.ModelSphereCurrent.Radius = largestSize * 2;
                }
            }

            if (EnableAv)
            {
                LineOrNotModel = aConst.DrawLine || ModelState == EntityState.None && aConst.AmmoParticle;
                Info.AvShot.ModelOnly = !LineOrNotModel && ModelState == EntityState.Exists;
            }
        }

        #endregion

        #region End

        internal void DestroyProjectile()
        {
            if (State == ProjectileState.Destroy)
            {
                Info.Hit = new Hit { Block = null, Entity = null, SurfaceHit = Position, LastHit = Position, HitVelocity = Info.InPlanetGravity ? Velocity * 0.33f : Velocity, HitTick = Info.System.Session.Tick };
                if (EnableAv || Info.AmmoDef.Const.VirtualBeams)
                {
                    Info.AvShot.ForceHitParticle = true;
                    Info.AvShot.Hit = Info.Hit;
                }

                Intersecting = true;
            }

            State = ProjectileState.Depleted;
        }

        internal void UnAssignProjectile(bool clear)
        {
            Info.Target.Projectile.Seekers.Remove(this);
            if (clear) Info.Target.Reset(Info.System.Session.Tick, Target.States.ProjectileClosed);
            else
            {
                Info.Target.IsProjectile = false;
                Info.Target.IsFakeTarget = false;
                Info.Target.Projectile = null;
            }
        }

        internal void ProjectileClose()
        {
            var aConst = Info.AmmoDef.Const;
            var session = Info.System.Session;
            if ((aConst.FragOnEnd && aConst.FragIgnoreArming || Info.Age >= aConst.MinArmingTime && (aConst.FragOnEnd || aConst.FragOnArmed && Info.ObjectsHit > 0)) && Info.SpawnDepth < aConst.FragMaxChildren)
                SpawnShrapnel(false);

            for (int i = 0; i < Watchers.Count; i++) Watchers[i].DeadProjectiles.Add(this);
            Watchers.Clear();

            foreach (var seeker in Seekers) seeker.Info.Target.Reset(session.Tick, Target.States.ProjectileClosed);
            Seekers.Clear();

            if (EnableAv && Info.AvShot.ForceHitParticle)
                Info.AvShot.HitEffects(true);

            State = ProjectileState.Dead;

            var detExp = aConst.EndOfLifeAv && (!aConst.ArmOnlyOnHit || Info.ObjectsHit > 0);

            if (EnableAv)
            {
                if (ModelState == EntityState.Exists)
                    ModelState = EntityState.None;
                if (!Info.AvShot.Active)
                    session.Av.AvShotPool.Return(Info.AvShot);
                else Info.AvShot.EndState = new AvClose { EndPos = Position, Dirty = true, DetonateEffect = detExp };
            }
            else if (Info.AmmoDef.Const.VirtualBeams)
            {
                for (int i = 0; i < VrPros.Count; i++)
                {
                    var vp = VrPros[i];
                    if (!vp.AvShot.Active)
                        session.Av.AvShotPool.Return(vp.AvShot);
                    else vp.AvShot.EndState = new AvClose { EndPos = Position, Dirty = true, DetonateEffect = detExp };

                    session.Projectiles.VirtInfoPool.Return(vp);
                }
                VrPros.Clear();
            }

            if (DynamicGuidance && session.AntiSmartActive)
                DynTrees.UnregisterProjectile(this);

            var target = Info.Target;
            CoreComponent comp;
            if (Info.DamageDone > 0 && Info.Ai?.Construct.RootAi != null && target.CoreEntity != null && !Info.Ai.MarkedForClose && !target.CoreEntity.MarkedForClose && Info.Ai.CompBase.TryGetValue(target.CoreEntity, out comp))
            {
                Info.Ai.Construct.RootAi.Construct.TotalEffect += Info.DamageDone;
                comp.TotalEffect += Info.DamageDone;
            }

            if (aConst.ProjectileSync && session.MpActive && session.IsServer)
                SyncProjectile(ProtoWeaponProSync.ProSyncState.Alive);

            PruningProxyId = -1;
            Info.Clean();
        }
        #endregion

        #region Smart / Drones
        internal void RunDrone(MyEntity targetEnt)
        {
            var aConst = Info.AmmoDef.Const;
            var fragProx = Info.AmmoDef.Const.FragProximity;
            var tracking = aConst.DeltaVelocityPerTick <= 0 || (DroneStat==DroneStatus.Dock? true:Vector3D.DistanceSquared(Info.Origin, Position) >= aConst.SmartsDelayDistSqr);
            var newVel = new Vector3D();
            var parentPos = Vector3D.Zero;
            var parentEnt = Info.Target.CoreEntity;
            var topEnt = new MyEntity();
            try
            {
                //Logic to handle loss of target and reassigment to friendly target
                if (targetEnt != null && DroneMsn==DroneMission.Attack)
                {
                    topEnt = targetEnt.GetTopMostParent();                 
                }
                if (topEnt == null || topEnt.MarkedForClose)
                {
                    topEnt = parentEnt.GetTopMostParent();
                    DroneMsn = DroneMission.Defend;//Try to return to parent in defensive state
                    if (topEnt == null || topEnt.MarkedForClose) return;
                }
                if (DroneMsn==DroneMission.RTB)
                {
                    topEnt=parentEnt.GetTopMostParent();
                    if (topEnt == null || topEnt.MarkedForClose) return;
                }

                //General use vars
                var targetSphere = topEnt.PositionComp.WorldVolume;
                var orbitSphere = targetSphere; //desired orbit dist
                var orbitSphereFar = orbitSphere; //Indicates start of approach
                var orbitSphereClose = targetSphere; //"Too close" or collision imminent
                var hasKamikaze = Info.AmmoDef.AreaOfDamage.ByBlockHit.Enable || Info.AmmoDef.AreaOfDamage.EndOfLife.Enable; //check for explosive payload on drone
                var maxLife = Info.AmmoDef.Const.MaxLifeTime;
                var strafing = Info.AmmoDef.Fragment.TimedSpawns.PointType == PointTypes.Direct && Info.AmmoDef.Fragment.TimedSpawns.PointAtTarget == false;

                switch (DroneMsn)
                {
                    case DroneMission.Attack:
                    orbitSphere.Radius += fragProx;
                    orbitSphereFar.Radius += AccelInMetersPerSec + MaxSpeed; //first whack at dynamic setting   
                    orbitSphereClose.Radius += MaxSpeed * 0.25f; //Magic number, needs logical work?

                        if (DroneStat != DroneStatus.Kamikaze && DroneStat != DroneStatus.Return && DroneStat!=DroneStatus.Escape)
                        {
                            if (orbitSphere.Contains(Position) != ContainmentType.Disjoint)
                            {
                                if (orbitSphereClose.Contains(Position) != ContainmentType.Disjoint)
                                {
                                    DroneStat = DroneStatus.Escape;
                                }
                                else if (DroneStat !=DroneStatus.Escape)
                                {
                                    DroneStat = DroneStatus.Orbit;
                                    var fragInterval = Info.AmmoDef.Fragment.TimedSpawns.Interval;
                                    var fragGroupSize = Info.AmmoDef.Fragment.TimedSpawns.GroupSize;
                                    var fragGroupDelay = Info.AmmoDef.Fragment.TimedSpawns.GroupDelay;
                                    if (strafing && Info.Age - Info.LastFragTime>=fragInterval)
                                    {
                                        DroneStat = DroneStatus.Strafe;//TODO- incorporate group delays, relocate Orbit setting
                                    }

                                }
                            }
                            else if (DroneStat!=DroneStatus.Strafe && orbitSphereFar.Contains(Position) != ContainmentType.Disjoint && (DroneStat == DroneStatus.Transit || DroneStat == DroneStatus.Orbit))
                            {
                                DroneStat = DroneStatus.Approach;
                            }
                        }
                        else if (DroneStat==DroneStatus.Escape)
                        {
                            if (orbitSphere.Contains(Position)==ContainmentType.Disjoint) 
                            DroneStat = DroneStatus.Orbit;
                        }

                        if (hasKamikaze && DroneStat != DroneStatus.Kamikaze && maxLife > 0 )
                        {
                            var kamiFlightTime = orbitSphere.Radius / MaxSpeed * 60; //time needed for final dive into target
                            if (maxLife - Info.Age <= kamiFlightTime || (Info.Frags >= Info.AmmoDef.Fragment.TimedSpawns.MaxSpawns)) DroneStat = DroneStatus.Kamikaze;
                        }
                        else if (!hasKamikaze && targetEnt != parentEnt)
                        {
                            try
                            {
                                parentPos = Info.Target.CoreEntity.PositionComp.GetPosition();
                            }
                            catch
                            {
                                Log.Line($"Parent Pos not found");
                                //DroneStat = DroneStatus.Orbit; //Keep orbiting target if parent is dead?
                                break;
                            }

                            if (parentPos != Vector3D.Zero && DroneStat != DroneStatus.Return)
                            {
                                var rtbFlightTime = Vector3D.Distance(Position, parentPos) / MaxSpeed * 60 * 1.05d;//add a multiplier to ensure final docking time?
                                if ((maxLife > 0 && maxLife - Info.Age <= rtbFlightTime)||(Info.Frags>=Info.AmmoDef.Fragment.TimedSpawns.MaxSpawns))
                                {
                                    var rayTestPath = new RayD(Position, Vector3D.Normalize(parentPos - Position));//Check for clear LOS home
                                    if (rayTestPath.Intersects(orbitSphereClose)==null)
                                    { 
                                    DroneMsn = DroneMission.RTB;
                                    DroneStat = DroneStatus.Transit;
                                    }
                                }
                            }
                        }
                        break;
                    case DroneMission.Defend:
                        orbitSphere.Radius += fragProx;
                        orbitSphereFar.Radius += AccelInMetersPerSec + MaxSpeed;  
                        orbitSphereClose.Radius += MaxSpeed/2;
                        //Reserved for future use, w/ target of a friendly grid or point in space
                        if (DroneStat != DroneStatus.Return)
                        {
                            if (orbitSphere.Contains(Position) != ContainmentType.Disjoint)
                            {
                                if (orbitSphereClose.Contains(Position) != ContainmentType.Disjoint)
                                {
                                    DroneStat = DroneStatus.Escape;
                                }
                                else
                                {
                                    DroneStat = DroneStatus.Orbit;
                                }
                            }
                            else if (orbitSphereFar.Contains(Position) != ContainmentType.Disjoint && (DroneStat == DroneStatus.Transit || DroneStat == DroneStatus.Orbit))
                            {
                                DroneStat = DroneStatus.Approach;
                            }
                        }

                        try
                        {
                            parentPos = Info.Target.CoreEntity.PositionComp.GetPosition();
                        }
                        catch
                        {
                            Log.Line($"Parent Pos not found");
                            //DroneStat = DroneStatus.Orbit; //Keep orbiting target if parent is dead?
                            break;
                        }

                        if (parentPos != Vector3D.Zero && DroneStat != DroneStatus.Return)
                        {
                            var rtbFlightTime = Vector3D.Distance(Position, parentPos) / MaxSpeed * 60 * 1.05d;//add a multiplier to ensure final docking time?
                            if ((maxLife > 0 && maxLife - Info.Age <= rtbFlightTime) || (Info.Frags >= Info.AmmoDef.Fragment.TimedSpawns.MaxSpawns))
                            {
                                var rayTestPath = new RayD(Position, Vector3D.Normalize(parentPos - Position));//Check for clear LOS home
                                if (rayTestPath.Intersects(orbitSphereClose) == null)
                                {
                                    DroneMsn = DroneMission.RTB;
                                    DroneStat = DroneStatus.Transit;
                                }
                            }
                        }
                        
                        break;
                    case DroneMission.RTB:
                        orbitSphere.Radius += MaxSpeed;
                        orbitSphereFar.Radius += MaxSpeed*2;   
                        orbitSphereClose.Radius = targetSphere.Radius;
                        if (DroneStat != DroneStatus.Return && DroneStat !=DroneStatus.Dock)
                        {
                            if (orbitSphere.Contains(Position) != ContainmentType.Disjoint)
                            {
                                if (orbitSphereClose.Contains(Position) != ContainmentType.Disjoint)
                                {
                                    DroneStat = DroneStatus.Escape;
                                }
                                else
                                {
                                    DroneStat = DroneStatus.Return;
                                }
                            }
                            else if (orbitSphereFar.Contains(Position) != ContainmentType.Disjoint && (DroneStat == DroneStatus.Transit || DroneStat == DroneStatus.Orbit))
                            {
                                DroneStat = DroneStatus.Approach;
                            }
                        }
                        if (DroneStat == DroneStatus.Orbit || DroneStat==DroneStatus.Return || DroneStat==DroneStatus.Dock) Info.Age -= 1;
                        break;
                }

               


                //debug line draw stuff
                /*
                var debugLine = new LineD(Position, orbitSphere.Center);
                if (DroneStat == DroneStatus.Transit) DsDebugDraw.DrawLine(debugLine, Color.Blue, 0.5f);
                if (DroneStat == DroneStatus.Approach) DsDebugDraw.DrawLine(debugLine, Color.Cyan, 0.5f);
                if (DroneStat == DroneStatus.Kamikaze) DsDebugDraw.DrawLine(debugLine, Color.White, 0.5f);
                if (DroneStat == DroneStatus.Return) DsDebugDraw.DrawLine(debugLine, Color.Yellow, 0.5f);
                if (DroneStat == DroneStatus.Dock) DsDebugDraw.DrawLine(debugLine, Color.Crimson, 0.5f);
                if (DroneStat == DroneStatus.Strafe) DsDebugDraw.DrawLine(debugLine, Color.Pink, 0.5f);
                if (DroneStat == DroneStatus.Escape) DsDebugDraw.DrawLine(debugLine, Color.Red, 0.5f);
                if (DroneStat == DroneStatus.Orbit) DsDebugDraw.DrawLine(debugLine, Color.Green, 0.5f);
                */                



                if (tracking)
                {
                    var validEntity = !Info.Target.TargetEntity?.MarkedForClose ?? false;
                    var timeSlot = (Info.Age + SmartSlot) % 30 == 0;
                    var overMaxTargets = HadTarget && NewTargets > aConst.MaxTargets && aConst.MaxTargets != 0;
                    var fake = Info.Target.IsFakeTarget;
                    var validTarget = fake || Info.Target.IsProjectile || validEntity && !overMaxTargets;
                    var seekFirstTarget = !HadTarget && !validTarget && PickTarget && (Info.Age > 120 && timeSlot || Info.Age % 30 == 0 && Info.IsShrapnel);
                    var gaveUpChase = !fake && Info.Age - ChaseAge > aConst.MaxChaseTime && HadTarget;
                    var isZombie = aConst.CanZombie && HadTarget && !fake && !validTarget && ZombieLifeTime > 0 && (ZombieLifeTime + SmartSlot) % 30 == 0;
                    var seekNewTarget = timeSlot && HadTarget && !validEntity && !overMaxTargets;
                    var needsTarget = (PickTarget && timeSlot || seekNewTarget || gaveUpChase && validTarget || isZombie || seekFirstTarget);

                    if (needsTarget && NewTarget() || validTarget)
                        TrackSmartTarget(fake);
                    else if (!SmartRoam())
                        return;

                    ComputeSmartVelocity(topEnt, ref orbitSphere, ref orbitSphereClose, ref orbitSphereFar, ref targetSphere, ref parentPos, out newVel);

                }
            }
            catch (Exception ex) { Log.Line($"Exception in RunDrones :(  : {ex}", null, true); }
            UpdateSmartVelocity(newVel, tracking);
        }
        private void OffsetSmartVelocity(ref Vector3D commandedAccel)
        {
            var smarts = Info.AmmoDef.Trajectory.Smarts;
            var offsetTime = smarts.OffsetTime;
            var revCmdAccel = -commandedAccel / AccelInMetersPerSec;
            var revOffsetDir = MyUtils.IsZero(OffsetDir.X - revCmdAccel.X, 1E-03f) && MyUtils.IsZero(OffsetDir.Y - revCmdAccel.Y, 1E-03f) && MyUtils.IsZero(OffsetDir.Z - revCmdAccel.Z, 1E-03f);

            if (Info.Age % offsetTime == 0 || revOffsetDir)
            {
                double angle = Info.Random.NextDouble() * MathHelper.TwoPi;
                var up = Vector3D.CalculatePerpendicularVector(Info.Direction);
                var right = Vector3D.Cross(Info.Direction, up);
                OffsetDir = Math.Sin(angle) * up + Math.Cos(angle) * right;
                OffsetDir *= smarts.OffsetRatio;

            }

            commandedAccel += AccelInMetersPerSec * OffsetDir;
            commandedAccel = Vector3D.Normalize(commandedAccel) * AccelInMetersPerSec;
        }

        private void ComputeSmartVelocity(MyEntity topEnt, ref BoundingSphereD orbitSphere, ref BoundingSphereD orbitSphereClose, ref BoundingSphereD orbitSphereFar, ref BoundingSphereD targetSphere, ref Vector3D parentPos, out Vector3D newVel)
        {
            var smarts = Info.AmmoDef.Trajectory.Smarts;
            var droneNavTarget = new Vector3D();
            var parentCubePos = Info.Target.CoreCube.PositionComp.GetPosition();
            var parentCubeOrientation = Info.Target.CoreCube.PositionComp.GetOrientation();
            var droneSize = Math.Max(Info.AmmoDef.Shape.Diameter,5);//Minimum drone "size" clamped to 5m for nav purposes, prevents chasing tiny points in space

            switch (DroneStat)
            {

                case DroneStatus.Transit:
                    droneNavTarget = Vector3D.Normalize(targetSphere.Center - Position);
                    break;


                case DroneStatus.Approach:
                    if (DroneMsn == DroneMission.RTB)//Check for LOS to docking target
                    {
                    var returnTargetTest = new Vector3D(parentCubePos + parentCubeOrientation.Forward * orbitSphere.Radius);
                    var droneNavTargetAim = Vector3D.Normalize(returnTargetTest - Position);
                    var testPathRayCheck = new RayD(returnTargetTest, -droneNavTargetAim);//Ray looking out from dock approach point
                        if (testPathRayCheck.Intersects(orbitSphereClose)==null)
                        {                            
                            DroneStat = DroneStatus.Return;
                            break;
                        }
                    }
                    //tangential tomfoolery
                    var lineToCenter = new LineD(Position, orbitSphere.Center);
                    var distToCenter = lineToCenter.Length; 
                    var radius = orbitSphere.Radius * 0.99;//Multiplier to ensure drone doesn't get "stuck" on periphery
                    var centerOffset = distToCenter - Math.Sqrt((distToCenter * distToCenter) - (radius * radius));
                    var offsetDist = Math.Sqrt((radius * radius) - (centerOffset * centerOffset));
                    var offsetPoint = new Vector3D(orbitSphere.Center + (centerOffset * -lineToCenter.Direction));//
                    var angleQuat = Vector3D.CalculatePerpendicularVector(lineToCenter.Direction); //placeholder for a possible rand-rotated quat.  Should be 90*, rand*, 0* 
                    var tangentPoint = new Vector3D(offsetPoint + offsetDist * angleQuat);
                    droneNavTarget = Vector3D.Normalize(tangentPoint - Position);
                    break;

                case DroneStatus.Orbit://Orbit & shoot behavior
                    var noseOffset = new Vector3D(Position + (Info.Direction * (AccelInMetersPerSec)));
                    double length;
                    Vector3D.Distance(ref orbitSphere.Center, ref noseOffset, out length);
                    var dir = (noseOffset - orbitSphere.Center) / length;
                    var deltaDist = length - orbitSphere.Radius * 0.95; //0.95 modifier for hysterisis, keeps target inside dronesphere
                    var navPoint = noseOffset + (-dir * deltaDist);
                    droneNavTarget = Vector3D.Normalize(navPoint - Position);
                    break;
               
                case DroneStatus.Strafe:
                    droneNavTarget = Vector3D.Normalize(PrevTargetPos - Position);
                    break;

                case DroneStatus.Escape:
                    var metersInSideOrbit = MyUtils.GetSmallestDistanceToSphere(ref Position, ref orbitSphereClose);
                    if (metersInSideOrbit < 0)
                    {
                        var futurePos = (Position + (TravelMagnitude * Math.Abs(metersInSideOrbit)));
                        var dirToFuturePos = Vector3D.Normalize(futurePos - orbitSphereClose.Center);
                        var futureSurfacePos = orbitSphereClose.Center + (dirToFuturePos * orbitSphereClose.Radius);
                        droneNavTarget = Vector3D.Normalize(futureSurfacePos - Position);
                    }
                    else
                    {
                        droneNavTarget = Info.Direction;
                    }
                    break;

                case DroneStatus.Kamikaze:
                    droneNavTarget = Vector3D.Normalize(PrevTargetPos - Position);
                    break;

                case DroneStatus.Return:
                    var returnTarget = new Vector3D(parentCubePos + parentCubeOrientation.Forward * orbitSphere.Radius);
                    droneNavTarget = Vector3D.Normalize(returnTarget - Position);
                    DeaccelRate = 30;
                    if (Vector3D.Distance(Position, returnTarget) <= droneSize) DroneStat = DroneStatus.Dock;
                    break;

                case DroneStatus.Dock: //This is ugly and I hate it...
                    var maxLife = Info.AmmoDef.Const.MaxLifeTime;
                    var sphereTarget = new Vector3D(parentCubePos + parentCubeOrientation.Forward * (orbitSphereClose.Radius+MaxSpeed/2));
                    //DsDebugDraw.DrawLine(new LineD(parentCubePos, sphereTarget), Color.CadetBlue, 0.5f);

                    if (Vector3D.Distance(sphereTarget, Position) >= droneSize)
                    {
                        if (DeaccelRate >= 25)//Final Approach
                        {
                            droneNavTarget = Vector3D.Normalize(sphereTarget - Position);
                            //DsDebugDraw.DrawLine(new LineD(Position, sphereTarget), Color.Green, 0.5f);
                            DeaccelRate = 25;
                            //Info.Age -= 1; //Keep alive mechanic
                        }

                    }
                    else if (DeaccelRate >=25)
                    {
                        DeaccelRate = 15;
                    }

                    if (DeaccelRate <=15)
                    {
                        if (Vector3D.Distance(parentCubePos, Position) >= droneSize)
                        {
                            droneNavTarget = Vector3D.Normalize(parentCubePos - Position);
                            //DsDebugDraw.DrawLine(new LineD(Position, parentCubePos), Color.Orange, 0.5f);
                            //Info.Age -= 1; //Keep alive mechanic
                        }
                        else// docked
                        {
                            Log.Line($"Drone Docked");
                            Info.Age = int.MaxValue;
                        }
                    }
                        break;
            }
        
            
            
            var missileToTarget = droneNavTarget;
            var relativeVelocity = PrevTargetVel - Velocity;
            var normalMissileAcceleration = (relativeVelocity - (relativeVelocity.Dot(missileToTarget) * missileToTarget)) * smarts.Aggressiveness;
            Vector3D commandedAccel;
            if (Vector3D.IsZero(normalMissileAcceleration)) {commandedAccel = (missileToTarget * AccelInMetersPerSec);}
            else
            {
                var maxLateralThrust = AccelInMetersPerSec * Math.Min(1, Math.Max(0, Info.AmmoDef.Const.MaxLateralThrust));
                if (normalMissileAcceleration.LengthSquared() > maxLateralThrust * maxLateralThrust)
                {
                    Vector3D.Normalize(ref normalMissileAcceleration, out normalMissileAcceleration);
                    normalMissileAcceleration *= maxLateralThrust;
                }
                commandedAccel = Math.Sqrt(Math.Max(0, AccelInMetersPerSec * AccelInMetersPerSec - normalMissileAcceleration.LengthSquared())) * missileToTarget + normalMissileAcceleration;
            }

            if (smarts.OffsetTime > 0 && DroneStat != DroneStatus.Strafe && DroneStat!=DroneStatus.Return && DroneStat != DroneStatus.Dock) // suppress offsets when strafing or docking
                OffsetSmartVelocity(ref commandedAccel);

            newVel = Velocity + (commandedAccel * StepConst);
            var accelDir = commandedAccel / AccelInMetersPerSec;

            AccelDir = accelDir;

            Vector3D.Normalize(ref newVel, out Info.Direction);
        }

        private bool SmartRoam()
        {
            var smarts = Info.AmmoDef.Trajectory.Smarts;
            var roam = smarts.Roam;
            PrevTargetPos = roam ? PredictedTargetPos : Position + (Info.Direction * Info.MaxTrajectory);

            if (ZombieLifeTime++ > Info.AmmoDef.Const.TargetLossTime && !smarts.KeepAliveAfterTargetLoss && (smarts.NoTargetExpire || HadTarget))
            {
                DistanceToTravelSqr = Info.DistanceTraveled * Info.DistanceTraveled;
                EarlyEnd = true;
            }

            if (roam && Info.Age - LastOffsetTime > 300 && HadTarget)
            {

                double dist;
                Vector3D.DistanceSquared(ref Position, ref PrevTargetPos, out dist);
                if (dist < Info.AmmoDef.Const.SmartOffsetSqr + VelocityLengthSqr && Vector3.Dot(Info.Direction, Position - PrevTargetPos) > 0)
                {

                    OffSetTarget(true);
                    PrevTargetPos += TargetOffSet;
                    PredictedTargetPos = PrevTargetPos;
                }
            }
            else if (MineSeeking)
            {
                ResetMine();
                return false;
            }

            return true;
        }
        private void UpdateSmartVelocity(Vector3D newVel, bool tracking)
        {

            if (!tracking)
                newVel = Velocity += (Info.Direction * Info.AmmoDef.Const.DeltaVelocityPerTick);
            VelocityLengthSqr = newVel.LengthSquared();

            if (VelocityLengthSqr > MaxSpeedSqr || (DeaccelRate <100&&IsDrone)) newVel = Info.Direction * MaxSpeed*DeaccelRate/100;
            Velocity = newVel;
        }

        private void TrackSmartTarget(bool fake)
        {
            var aConst = Info.AmmoDef.Const;
            HadTarget = true;
            if (ZombieLifeTime > 0)
            {
                ZombieLifeTime = 0;
                OffSetTarget();
            }

            var targetPos = Vector3D.Zero;

            Ai.FakeTarget.FakeWorldTargetInfo fakeTargetInfo = null;

            if (fake && Info.DummyTargets != null)
            {
                var fakeTarget = Info.DummyTargets.PaintedTarget.EntityId != 0 ? Info.DummyTargets.PaintedTarget : Info.DummyTargets.ManualTarget;
                fakeTargetInfo = fakeTarget.LastInfoTick != Info.System.Session.Tick ? fakeTarget.GetFakeTargetInfo(Info.Ai) : fakeTarget.FakeInfo;
                targetPos = fakeTargetInfo.WorldPosition;
            }
            else if (Info.Target.IsProjectile)
            {
                targetPos = Info.Target.Projectile.Position;
            }
            else if (Info.Target.TargetEntity != null)
            {
                targetPos = Info.Target.TargetEntity.PositionComp.WorldAABB.Center;
            }

            if (aConst.TargetOffSet && WasTracking)
            {

                if (Info.Age - LastOffsetTime > 300)
                {

                    double dist;
                    Vector3D.DistanceSquared(ref Position, ref targetPos, out dist);
                    if (dist < aConst.SmartOffsetSqr + VelocityLengthSqr && Vector3.Dot(Info.Direction, Position - targetPos) > 0)
                        OffSetTarget();
                }
                targetPos += TargetOffSet;
            }

            PredictedTargetPos = targetPos;

            var physics = Info.Target.TargetEntity?.Physics ?? Info.Target.TargetEntity?.Parent?.Physics;
            if (!(Info.Target.IsProjectile || fake) && (physics == null || Vector3D.IsZero(targetPos)))
            {
                PrevTargetPos = PredictedTargetPos;
            }
            else
            {
                PrevTargetPos = targetPos;

            }

            var tVel = Vector3.Zero;
            if (fake && fakeTargetInfo != null)
            {
                tVel = fakeTargetInfo.LinearVelocity;
            }
            else if (Info.Target.IsProjectile)
            {
                tVel = Info.Target.Projectile.Velocity;
            }
            else if (physics != null)
            {
                tVel = physics.LinearVelocity;
            }

            if (aConst.TargetLossDegree > 0 && Vector3D.DistanceSquared(Info.Origin, Position) >= aConst.SmartsDelayDistSqr)
                SmartTargetLoss(targetPos);

            PrevTargetVel = tVel;
        }

        private void SmartTargetLoss(Vector3D targetPos)
        {

            if (WasTracking && (Info.System.Session.Tick20 || Vector3.Dot(Info.Direction, Position - targetPos) > 0) || !WasTracking)
            {
                var targetDir = -Info.Direction;
                var refDir = Vector3D.Normalize(Position - targetPos);
                if (!MathFuncs.IsDotProductWithinTolerance(ref targetDir, ref refDir, Info.AmmoDef.Const.TargetLossDegree))
                {
                    if (WasTracking)
                        PickTarget = true;
                }
                else if (!WasTracking)
                    WasTracking = true;
            }
        }

        internal void RunSmart()
        {
            Vector3D newVel;
            var aConst = Info.AmmoDef.Const;
            if (aConst.DeltaVelocityPerTick <= 0 || Vector3D.DistanceSquared(Info.Origin, Position) >= aConst.SmartsDelayDistSqr)
            {

                var smarts = Info.AmmoDef.Trajectory.Smarts;
                var fake = Info.Target.IsFakeTarget;
                var gaveUpChase = !fake && Info.Age - ChaseAge > aConst.MaxChaseTime && HadTarget;
                var overMaxTargets = HadTarget && NewTargets > aConst.MaxTargets && aConst.MaxTargets != 0;
                var validEntity = !Info.Target.TargetEntity?.MarkedForClose ?? false;
                var validTarget = fake || Info.Target.IsProjectile || validEntity && !overMaxTargets;
                var isZombie = aConst.CanZombie && HadTarget && !fake && !validTarget && ZombieLifeTime > 0 && (ZombieLifeTime + SmartSlot) % 30 == 0;
                var timeSlot = (Info.Age + SmartSlot) % 30 == 0;
                var seekNewTarget = timeSlot && HadTarget && !validEntity && !overMaxTargets;
                var seekFirstTarget = !HadTarget && !validTarget && PickTarget && (Info.Age > 120 && timeSlot || Info.Age % 30 == 0 && Info.IsShrapnel);
                if ((PickTarget && timeSlot || seekNewTarget || gaveUpChase && validTarget || isZombie || seekFirstTarget) && NewTarget() || validTarget)
                {

                    HadTarget = true;
                    if (ZombieLifeTime > 0)
                    {
                        ZombieLifeTime = 0;
                        OffSetTarget();
                    }
                    var targetPos = Vector3D.Zero;

                    Ai.FakeTarget.FakeWorldTargetInfo fakeTargetInfo = null;
                    if (fake && Info.DummyTargets != null)
                    {
                        var fakeTarget = Info.DummyTargets.PaintedTarget.EntityId != 0 ? Info.DummyTargets.PaintedTarget : Info.DummyTargets.ManualTarget;
                        fakeTargetInfo = fakeTarget.LastInfoTick != Info.System.Session.Tick ? fakeTarget.GetFakeTargetInfo(Info.Ai) : fakeTarget.FakeInfo;
                        targetPos = fakeTargetInfo.WorldPosition;
                    }
                    else if (Info.Target.IsProjectile) targetPos = Info.Target.Projectile.Position;
                    else if (Info.Target.TargetEntity != null) targetPos = Info.Target.TargetEntity.PositionComp.WorldAABB.Center;

                    if (aConst.TargetOffSet && WasTracking)
                    {
                        if (Info.Age - LastOffsetTime > 300)
                        {
                            double dist;
                            Vector3D.DistanceSquared(ref Position, ref targetPos, out dist);
                            if (dist < aConst.SmartOffsetSqr + VelocityLengthSqr && Vector3.Dot(Info.Direction, Position - targetPos) > 0)
                                OffSetTarget();
                        }
                        targetPos += TargetOffSet;
                    }

                    PredictedTargetPos = targetPos;

                    var physics = Info.Target.TargetEntity?.Physics ?? Info.Target.TargetEntity?.Parent?.Physics;
                    if (!(Info.Target.IsProjectile || fake) && (physics == null || Vector3D.IsZero(targetPos)))
                        PrevTargetPos = PredictedTargetPos;
                    else
                        PrevTargetPos = targetPos;

                    var tVel = Vector3.Zero;
                    if (fake && fakeTargetInfo != null) tVel = fakeTargetInfo.LinearVelocity;
                    else if (Info.Target.IsProjectile) tVel = Info.Target.Projectile.Velocity;
                    else if (physics != null) tVel = physics.LinearVelocity;


                    if (aConst.TargetLossDegree > 0 && Vector3D.DistanceSquared(Info.Origin, Position) >= aConst.SmartsDelayDistSqr)
                    {

                        if (WasTracking && (Info.System.Session.Tick20 || Vector3.Dot(Info.Direction, Position - targetPos) > 0) || !WasTracking)
                        {
                            var targetDir = -Info.Direction;
                            var refDir = Vector3D.Normalize(Position - targetPos);
                            if (!MathFuncs.IsDotProductWithinTolerance(ref targetDir, ref refDir, aConst.TargetLossDegree))
                            {
                                if (WasTracking)
                                    PickTarget = true;
                            }
                            else if (!WasTracking)
                                WasTracking = true;
                        }
                    }

                    PrevTargetVel = tVel;
                }
                else
                {
                    var roam = smarts.Roam;
                    PrevTargetPos = roam ? PredictedTargetPos : Position + (Info.Direction * Info.MaxTrajectory);

                    if (ZombieLifeTime++ > aConst.TargetLossTime && !smarts.KeepAliveAfterTargetLoss && (smarts.NoTargetExpire || HadTarget))
                    {
                        DistanceToTravelSqr = Info.DistanceTraveled * Info.DistanceTraveled;
                        EarlyEnd = true;
                    }

                    if (roam && Info.Age - LastOffsetTime > 300 && HadTarget)
                    {

                        double dist;
                        Vector3D.DistanceSquared(ref Position, ref PrevTargetPos, out dist);
                        if (dist < aConst.SmartOffsetSqr + VelocityLengthSqr && Vector3.Dot(Info.Direction, Position - PrevTargetPos) > 0)
                        {

                            OffSetTarget(true);
                            PrevTargetPos += TargetOffSet;
                            PredictedTargetPos = PrevTargetPos;
                        }
                    }
                    else if (MineSeeking)
                    {
                        ResetMine();
                        return;
                    }
                }

                var missileToTarget = Vector3D.Normalize(PrevTargetPos - Position);
                var relativeVelocity = PrevTargetVel - Velocity;
                var normalMissileAcceleration = (relativeVelocity - (relativeVelocity.Dot(missileToTarget) * missileToTarget)) * smarts.Aggressiveness;

                Vector3D commandedAccel;
                if (Vector3D.IsZero(normalMissileAcceleration)) commandedAccel = (missileToTarget * AccelInMetersPerSec);
                else
                {

                    var maxLateralThrust = AccelInMetersPerSec * Math.Min(1, Math.Max(0, aConst.MaxLateralThrust));
                    if (normalMissileAcceleration.LengthSquared() > maxLateralThrust * maxLateralThrust)
                    {
                        Vector3D.Normalize(ref normalMissileAcceleration, out normalMissileAcceleration);
                        normalMissileAcceleration *= maxLateralThrust;
                    }
                    commandedAccel = Math.Sqrt(Math.Max(0, AccelInMetersPerSec * AccelInMetersPerSec - normalMissileAcceleration.LengthSquared())) * missileToTarget + normalMissileAcceleration;
                }

                var offsetTime = smarts.OffsetTime;
                if (offsetTime > 0)
                {
                    if ((Info.Age % offsetTime == 0))
                    {
                        double angle = Info.Random.NextDouble() * MathHelper.TwoPi;
                        var up = Vector3D.CalculatePerpendicularVector(Info.Direction);
                        var right = Vector3D.Cross(Info.Direction, up);
                        OffsetDir = Math.Sin(angle) * up + Math.Cos(angle) * right;
                        OffsetDir *= smarts.OffsetRatio;
                    }

                    commandedAccel += AccelInMetersPerSec * OffsetDir;
                    commandedAccel = Vector3D.Normalize(commandedAccel) * AccelInMetersPerSec;
                }

                newVel = Velocity + (commandedAccel * StepConst);
                var accelDir = commandedAccel / AccelInMetersPerSec;

                AccelDir = accelDir;

                Vector3D.Normalize(ref newVel, out Info.Direction);
            }
            else
                newVel = Velocity += (Info.Direction * aConst.DeltaVelocityPerTick);
            VelocityLengthSqr = newVel.LengthSquared();

            if (VelocityLengthSqr > MaxSpeedSqr) newVel = Info.Direction * MaxSpeed;
            Velocity = newVel;
        }

        internal void OffSetTarget(bool roam = false)
        {
            var randAzimuth = (Info.Random.NextDouble() * 1) * 2 * Math.PI;
            var randElevation = ((Info.Random.NextDouble() * 1) * 2 - 1) * 0.5 * Math.PI;
            var offsetAmount = roam ? 100 : Info.AmmoDef.Trajectory.Smarts.Inaccuracy;
            Vector3D randomDirection;
            Vector3D.CreateFromAzimuthAndElevation(randAzimuth, randElevation, out randomDirection); // this is already normalized
            PrevTargetOffset = TargetOffSet;
            TargetOffSet = (randomDirection * offsetAmount);
            if (Info.Age != 0) LastOffsetTime = Info.Age;
        }

        internal bool NewTarget()
        {
            var giveUp = HadTarget && ++NewTargets > Info.AmmoDef.Const.MaxTargets && Info.AmmoDef.Const.MaxTargets != 0;
            ChaseAge = Info.Age;
            PickTarget = false;
            if (giveUp || !Ai.ReacquireTarget(this))
            {
                var badEntity = !Info.LockOnFireState && Info.Target.TargetEntity != null && Info.Target.TargetEntity.MarkedForClose || Info.LockOnFireState && (Info.Target.TargetEntity?.GetTopMostParent()?.MarkedForClose ?? true);
                if (!giveUp && !Info.LockOnFireState || Info.LockOnFireState && giveUp || !Info.AmmoDef.Trajectory.Smarts.NoTargetExpire || badEntity)
                {
                    Info.Target.TargetEntity = null;
                }

                if (Info.Target.IsProjectile) UnAssignProjectile(true);
                return false;
            }

            if (Info.Target.IsProjectile) UnAssignProjectile(false);
            return true;
        }

        internal void ForceNewTarget()
        {
            ChaseAge = Info.Age;
            PickTarget = false;
        }

        internal bool TrajectoryEstimation(WeaponDefinition.AmmoDef ammoDef, ref Vector3D shooterPos, out Vector3D targetDirection)
        {
            var aConst = Info.AmmoDef.Const;
            if (Info.Target.TargetEntity.GetTopMostParent()?.Physics?.LinearVelocity == null)
            {
                targetDirection = Vector3D.Zero;
                return false;
            }

            var targetPos = Info.Target.TargetEntity.PositionComp.WorldAABB.Center;

            if (aConst.FragPointType == PointTypes.Direct)
            {
                targetDirection = Vector3D.Normalize(targetPos - Position);
                return true;
            }


            var targetVel = Info.Target.TargetEntity.GetTopMostParent().Physics.LinearVelocity;
            var shooterVel = !Info.AmmoDef.Const.FragDropVelocity ? Velocity : Vector3D.Zero;

            var projectileMaxSpeed = ammoDef.Const.DesiredProjectileSpeed;
            Vector3D deltaPos = targetPos - shooterPos;
            Vector3D deltaVel = targetVel - shooterVel;
            Vector3D deltaPosNorm;
            if (Vector3D.IsZero(deltaPos)) deltaPosNorm = Vector3D.Zero;
            else if (Vector3D.IsUnit(ref deltaPos)) deltaPosNorm = deltaPos;
            else Vector3D.Normalize(ref deltaPos, out deltaPosNorm);

            double closingSpeed;
            Vector3D.Dot(ref deltaVel, ref deltaPosNorm, out closingSpeed);

            Vector3D closingVel = closingSpeed * deltaPosNorm;
            Vector3D lateralVel = deltaVel - closingVel;
            double projectileMaxSpeedSqr = projectileMaxSpeed * projectileMaxSpeed;
            double ttiDiff = projectileMaxSpeedSqr - lateralVel.LengthSquared();

            if (ttiDiff < 0)
            {
                targetDirection = Info.Direction;
                return aConst.FragPointType == PointTypes.Direct;
            }

            double projectileClosingSpeed = Math.Sqrt(ttiDiff) - closingSpeed;

            double closingDistance;
            Vector3D.Dot(ref deltaPos, ref deltaPosNorm, out closingDistance);

            double timeToIntercept = ttiDiff < 0 ? 0 : closingDistance / projectileClosingSpeed;

            if (timeToIntercept < 0)
            {
                
                if (aConst.FragPointType == PointTypes.Lead)
                {
                    targetDirection = Vector3D.Normalize((targetPos + timeToIntercept * (targetVel - shooterVel)) - shooterPos);
                    return true;
                }
                
                targetDirection = Info.Direction;
                return false;
            }

            targetDirection = Vector3D.Normalize(targetPos + timeToIntercept * (targetVel - shooterVel * 1) - shooterPos);
            return true;
        }
        #endregion

        #region Mines
        internal void ActivateMine()
        {
            var ent = Info.Target.TargetEntity;
            MineActivated = true;
            AtMaxRange = false;
            var targetPos = ent.PositionComp.WorldAABB.Center;
            var deltaPos = targetPos - Position;
            var targetVel = ent.Physics?.LinearVelocity ?? Vector3.Zero;
            var deltaVel = targetVel - Vector3.Zero;
            var timeToIntercept = MathFuncs.Intercept(deltaPos, deltaVel, DesiredSpeed);
            var predictedPos = targetPos + (float)timeToIntercept * deltaVel;
            PredictedTargetPos = predictedPos;
            PrevTargetPos = predictedPos;
            PrevTargetVel = targetVel;
            LockedTarget = true;

            if (Info.AmmoDef.Trajectory.Guidance == GuidanceType.DetectFixed) return;
            Vector3D.DistanceSquared(ref Info.Origin, ref predictedPos, out DistanceToTravelSqr);
            Info.DistanceTraveled = 0;
            Info.PrevDistanceTraveled = 0;

            Info.Direction = Vector3D.Normalize(predictedPos - Position);
            AccelDir = Info.Direction;
            VelocityLengthSqr = 0;

            MaxVelocity = (Info.Direction * DesiredSpeed);
            MaxSpeed = MaxVelocity.Length();
            MaxSpeedSqr = MaxSpeed * MaxSpeed;
            AccelVelocity = (Info.Direction * Info.AmmoDef.Const.DeltaVelocityPerTick);

            if (Info.AmmoDef.Const.AmmoSkipAccel)
            {
                Velocity = MaxVelocity;
                VelocityLengthSqr = MaxSpeed * MaxSpeed;
            }
            else Velocity = AccelVelocity;

            if (Info.AmmoDef.Trajectory.Guidance == GuidanceType.DetectSmart)
            {

                IsSmart = true;

                if (IsSmart && Info.AmmoDef.Const.TargetOffSet && LockedTarget)
                {
                    OffSetTarget();
                }
                else
                {
                    TargetOffSet = Vector3D.Zero;
                }
            }

            TravelMagnitude = Velocity * StepConst;
        }


        internal void SeekEnemy()
        {
            var mineInfo = Info.AmmoDef.Trajectory.Mines;
            var detectRadius = mineInfo.DetectRadius;
            var deCloakRadius = mineInfo.DeCloakRadius;

            var wakeRadius = detectRadius > deCloakRadius ? detectRadius : deCloakRadius;
            PruneSphere = new BoundingSphereD(Position, wakeRadius);
            var inRange = false;
            var activate = false;
            var minDist = double.MaxValue;
            if (!MineActivated)
            {
                MyEntity closestEnt = null;
                MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref PruneSphere, MyEntityList, MyEntityQueryType.Dynamic);
                for (int i = 0; i < MyEntityList.Count; i++)
                {
                    var ent = MyEntityList[i];
                    var grid = ent as MyCubeGrid;
                    var character = ent as IMyCharacter;
                    if (grid == null && character == null || ent.MarkedForClose || !ent.InScene) continue;
                    MyDetectedEntityInfo entInfo;

                    if (!Info.Ai.CreateEntInfo(ent, Info.Ai.AiOwner, out entInfo)) continue;
                    switch (entInfo.Relationship)
                    {
                        case MyRelationsBetweenPlayerAndBlock.Owner:
                            continue;
                        case MyRelationsBetweenPlayerAndBlock.FactionShare:
                            continue;
                    }
                    var entSphere = ent.PositionComp.WorldVolume;
                    entSphere.Radius += Info.AmmoDef.Const.CollisionSize;
                    var dist = MyUtils.GetSmallestDistanceToSphereAlwaysPositive(ref Position, ref entSphere);
                    if (dist >= minDist) continue;
                    minDist = dist;
                    closestEnt = ent;
                }
                MyEntityList.Clear();

                if (closestEnt != null)
                {
                    ForceNewTarget();
                    Info.Target.TargetEntity = closestEnt;
                }
            }
            else if (Info.Target.TargetEntity != null && !Info.Target.TargetEntity.MarkedForClose)
            {
                var entSphere = Info.Target.TargetEntity.PositionComp.WorldVolume;
                entSphere.Radius += Info.AmmoDef.Const.CollisionSize;
                minDist = MyUtils.GetSmallestDistanceToSphereAlwaysPositive(ref Position, ref entSphere);
            }
            else
                TriggerMine(true);

            if (EnableAv)
            {
                if (Info.AvShot.Cloaked && minDist <= deCloakRadius) Info.AvShot.Cloaked = false;
                else if (Info.AvShot.AmmoDef.Trajectory.Mines.Cloak && !Info.AvShot.Cloaked && minDist > deCloakRadius) Info.AvShot.Cloaked = true;
            }

            if (minDist <= Info.AmmoDef.Const.CollisionSize) activate = true;
            if (minDist <= detectRadius) inRange = true;
            if (MineActivated)
            {
                if (!inRange)
                    TriggerMine(true);
            }
            else if (inRange) ActivateMine();

            if (activate)
            {
                TriggerMine(false);
                MyEntityList.Add(Info.Target.TargetEntity);
            }
        }
        internal void TriggerMine(bool startTimer)
        {
            DistanceToTravelSqr = double.MinValue;
            if (Info.AmmoDef.Const.Ewar)
            {
                Info.AvShot.Triggered = true;
            }

            if (startTimer) DeaccelRate = Info.AmmoDef.Trajectory.Mines.FieldTime;
            MineTriggered = true;
        }

        internal void ResetMine()
        {
            if (MineTriggered)
            {
                IsSmart = false;
                Info.DistanceTraveled = double.MaxValue;
                DeaccelRate = 0;
                return;
            }

            DeaccelRate = Info.AmmoDef.Const.Ewar || Info.AmmoDef.Const.IsMine ? Info.AmmoDef.Trajectory.DeaccelTime : 0;
            DistanceToTravelSqr = MaxTrajectorySqr;

            Info.AvShot.Triggered = false;
            MineTriggered = false;
            MineActivated = false;
            LockedTarget = false;
            MineSeeking = true;

            if (Info.AmmoDef.Trajectory.Guidance == GuidanceType.DetectSmart)
            {
                IsSmart = false;
                IsSmart = false;
                SmartSlot = 0;
                TargetOffSet = Vector3D.Zero;
            }

            Info.Direction = Vector3D.Zero;
            AccelDir = Vector3D.Zero;
            Velocity = Vector3D.Zero;
            TravelMagnitude = Vector3D.Zero;
            VelocityLengthSqr = 0;
        }

        #endregion

        #region Ewar
        internal void RunEwar()
        {
            if (Info.AmmoDef.Const.Pulse && !Info.EwarAreaPulse && (VelocityLengthSqr <= 0 || AtMaxRange) && !Info.AmmoDef.Const.IsMine)
            {
                Info.EwarAreaPulse = true;
                PrevVelocity = Velocity;
                Velocity = Vector3D.Zero;
                DistanceToTravelSqr = Info.DistanceTraveled * Info.DistanceTraveled;
            }

            if (Info.EwarAreaPulse)
            {
                var maxSteps = Info.AmmoDef.Const.PulseGrowTime;
                if (Info.TriggerGrowthSteps++ < maxSteps)
                {
                    var areaSize = Info.AmmoDef.Const.EwarRadius;
                    var expansionPerTick = areaSize / maxSteps;
                    var nextSize = Info.TriggerGrowthSteps * expansionPerTick;
                    if (nextSize <= areaSize)
                    {
                        var nextRound = nextSize + 1;
                        if (nextRound > areaSize)
                        {
                            if (nextSize < areaSize)
                            {
                                nextSize = areaSize;
                                ++Info.TriggerGrowthSteps;
                            }
                        }
                        Info.TriggerMatrix = MatrixD.Identity;
                        Info.TriggerMatrix.Translation = Position;
                        MatrixD.Rescale(ref Info.TriggerMatrix, nextSize);
                        if (EnableAv)
                        {
                            Info.AvShot.Triggered = true;
                            Info.AvShot.TriggerMatrix = Info.TriggerMatrix;
                        }
                    }
                }
            }

            if (!Info.AmmoDef.Const.Pulse || Info.AmmoDef.Const.Pulse && Info.Age % Info.AmmoDef.Const.PulseInterval == 0)
                EwarEffects();
            else Info.EwarActive = false;
        }

        internal void EwarEffects()
        {
            switch (Info.AmmoDef.Const.EwarType)
            {
                case AntiSmart:
                    var eWarSphere = new BoundingSphereD(Position, Info.AmmoDef.Const.EwarRadius);

                    DynTrees.GetAllProjectilesInSphere(Info.System.Session, ref eWarSphere, EwaredProjectiles, false);
                    for (int j = 0; j < EwaredProjectiles.Count; j++)
                    {
                        var netted = EwaredProjectiles[j];

                        if (eWarSphere.Intersects(new BoundingSphereD(netted.Position, netted.Info.AmmoDef.Const.CollisionSize)))
                        {
                            if (netted.Info.Ai.AiType == Ai.AiTypes.Grid && Info.Target.CoreCube != null && netted.Info.Target.CoreCube.CubeGrid.IsSameConstructAs(Info.Target.CoreCube.CubeGrid) || netted.Info.Target.IsProjectile) continue;
                            if (Info.Random.NextDouble() * 100f < Info.AmmoDef.Const.PulseChance || !Info.AmmoDef.Const.Pulse)
                            {
                                Info.BaseEwarPool -= (float)netted.Info.AmmoDef.Const.HealthHitModifier;
                                if (Info.BaseEwarPool <= 0 && Info.BaseHealthPool-- > 0)
                                {
                                    Info.EwarActive = true;
                                    netted.Info.Target.Projectile = this;
                                    netted.Info.Target.IsProjectile = true;
                                    Seekers.Add(netted);
                                }
                            }
                        }
                    }
                    EwaredProjectiles.Clear();
                    return;
                case Push:
                    if (Info.EwarAreaPulse && Info.Random.NextDouble() * 100f <= Info.AmmoDef.Const.PulseChance || !Info.AmmoDef.Const.Pulse)
                        Info.EwarActive = true;
                    break;
                case Pull:
                    if (Info.EwarAreaPulse && Info.Random.NextDouble() * 100f <= Info.AmmoDef.Const.PulseChance || !Info.AmmoDef.Const.Pulse)
                        Info.EwarActive = true;
                    break;
                case Tractor:
                    if (Info.EwarAreaPulse && Info.Random.NextDouble() * 100f <= Info.AmmoDef.Const.PulseChance || !Info.AmmoDef.Const.Pulse)
                        Info.EwarActive = true;
                    break;
                case JumpNull:
                    if (Info.EwarAreaPulse && Info.Random.NextDouble() * 100f <= Info.AmmoDef.Const.PulseChance || !Info.AmmoDef.Const.Pulse)
                        Info.EwarActive = true;
                    break;
                case Anchor:
                    if (Info.EwarAreaPulse && Info.Random.NextDouble() * 100f <= Info.AmmoDef.Const.PulseChance || !Info.AmmoDef.Const.Pulse)
                        Info.EwarActive = true;
                    break;
                case EnergySink:
                    if (Info.EwarAreaPulse && Info.Random.NextDouble() * 100f <= Info.AmmoDef.Const.PulseChance || !Info.AmmoDef.Const.Pulse)
                        Info.EwarActive = true;
                    break;
                case Emp:
                    if (Info.EwarAreaPulse && Info.Random.NextDouble() * 100f <= Info.AmmoDef.Const.PulseChance || !Info.AmmoDef.Const.Pulse)
                        Info.EwarActive = true;
                    break;
                case Offense:
                    if (Info.EwarAreaPulse && Info.Random.NextDouble() * 100f <= Info.AmmoDef.Const.PulseChance || !Info.AmmoDef.Const.Pulse)
                        Info.EwarActive = true;
                    break;
                case Nav:
                    if (!Info.AmmoDef.Const.Pulse || Info.EwarAreaPulse && Info.Random.NextDouble() * 100f <= Info.AmmoDef.Const.PulseChance)
                        Info.EwarActive = true;
                    break;
                case Dot:
                    if (Info.EwarAreaPulse && Info.Random.NextDouble() * 100f <= Info.AmmoDef.Const.PulseChance || !Info.AmmoDef.Const.Pulse)
                    {
                        Info.EwarActive = true;
                    }
                    break;
            }
        }
        #endregion

        #region Misc
        internal void SpawnShrapnel(bool timedSpawn = true) // inception begins
        {
            var ammoDef = Info.AmmoDef;
            var aConst = ammoDef.Const;
            var patternIndex = aConst.FragPatternCount;
            var pattern = ammoDef.Pattern;

            if (aConst.FragmentPattern)
            {
                if (pattern.Random)
                {
                    if (pattern.TriggerChance >= 1 || pattern.TriggerChance >= Info.Random.NextDouble())
                        patternIndex = Info.Random.Range(pattern.RandomMin, pattern.RandomMax);

                    for (int w = 0; w < aConst.FragPatternCount; w++)
                    {

                        var y = Info.Random.Range(0, w + 1);
                        Info.PatternShuffle[w] = Info.PatternShuffle[y];
                        Info.PatternShuffle[y] = w;
                    }
                }
                else if (pattern.PatternSteps > 0 && pattern.PatternSteps <= aConst.FragPatternCount)
                {
                    patternIndex = pattern.PatternSteps;
                    for (int p = 0; p < aConst.FragPatternCount; ++p)
                    {   
                        Info.PatternShuffle[p] = (Info.PatternShuffle[p] + patternIndex) % aConst.FragPatternCount;
                    }
                }
            }

            var fireOnTarget = timedSpawn && aConst.HasFragProximity && aConst.FragPointAtTarget;

            Vector3D newOrigin;
            if (!aConst.HasFragmentOffset)
                newOrigin = !Vector3D.IsZero(Info.Hit.LastHit) ? Info.Hit.LastHit : Position;
            else
            {
                var pos = !Vector3D.IsZero(Info.Hit.LastHit) ? Info.Hit.LastHit : Position;
                var offSet = (Info.Direction * aConst.FragmentOffset);
                newOrigin = aConst.HasNegFragmentOffset ? pos - offSet : pos + offSet;
            }

            var spawn = false;
            for (int i = 0; i < patternIndex; i++)
            {
                var fragAmmoDef = aConst.FragmentPattern ? aConst.AmmoPattern[Info.PatternShuffle[i] > 0 ? Info.PatternShuffle[i] - 1 : aConst.FragPatternCount-1] : Info.System.AmmoTypes[aConst.FragmentId].AmmoDef;
                Vector3D pointDir;
                if (!fireOnTarget)
                {
                    pointDir = Info.Direction;

                    if (IsDrone)
                    {
                        MathFuncs.Cone aimCone;
                        var targetSphere = Info.Target.TargetEntity.PositionComp.WorldVolume;  
                        aimCone.ConeDir = Info.Direction;
                        aimCone.ConeTip = Position;
                        aimCone.ConeAngle = MathHelper.ToRadians(3); //toleranceInRadians;
                        if (!MathFuncs.TargetSphereInCone(ref targetSphere, ref aimCone)) break;
                    }

               }

                else if (!TrajectoryEstimation(fragAmmoDef, ref newOrigin, out pointDir))
                    continue;
                spawn = true;

                if (fragAmmoDef.Const.HasAdvFragOffset)
                {
                    MatrixD matrix;
                    MatrixD.CreateWorld(ref Position, ref Info.Direction, ref Info.OriginUp, out matrix);

                    Vector3D advOffSet;
                    var offSet = fragAmmoDef.Const.FragOffset;
                    Vector3D.Rotate(ref offSet, ref matrix, out advOffSet);
                    newOrigin += offSet;
                }


                var projectiles = Info.System.Session.Projectiles;
                var shrapnel = projectiles.ShrapnelPool.Get();
                shrapnel.Init(this, projectiles.FragmentPool, fragAmmoDef, ref newOrigin, ref pointDir);
                projectiles.ShrapnelToSpawn.Add(shrapnel);
            }

            if (!spawn)
                return;

            ++Info.SpawnDepth;

            if (timedSpawn && ++Info.Frags == aConst.MaxFrags && aConst.FragParentDies)
                DistanceToTravelSqr = Info.DistanceTraveled * Info.DistanceTraveled;

            Info.LastFragTime = Info.Age;
        }


        internal void CheckForNearVoxel(uint steps)
        {
            var possiblePos = BoundingBoxD.CreateFromSphere(new BoundingSphereD(Position, ((MaxSpeed) * (steps + 1) * StepConst) + Info.AmmoDef.Const.CollisionSize));
            if (MyGamePruningStructure.AnyVoxelMapInBox(ref possiblePos))
            {
                PruneQuery = MyEntityQueryType.Both;
            }
        }

        internal void SyncProjectile(ProtoWeaponProSync.ProSyncState state)
        {
            var target = Info.Target;
            var session = Info.System.Session;
            var proSync = session.ProtoWeaponProSyncPool.Count > 0 ? session.ProtoWeaponProSyncPool.Pop() : new ProtoWeaponProSync();
            proSync.UniquePartId = Info.UniquePartId;
            proSync.State = state;
            proSync.Position = Position;
            proSync.Velocity = Velocity;
            proSync.ProId = Info.SyncId;
            proSync.TargetId = target.TargetId;
            proSync.Type = target.TargetEntity != null ? ProtoWeaponProSync.TargetTypes.Entity : target.IsProjectile ? ProtoWeaponProSync.TargetTypes.Projectile : target.IsFakeTarget ? ProtoWeaponProSync.TargetTypes.Fake : ProtoWeaponProSync.TargetTypes.None;
            var weaponSync = session.WeaponProSyncs[Info.UniquePartId];

            weaponSync[Info.SyncId] = proSync;
        }

        #endregion
    }
}