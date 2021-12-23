using CoreSystems.Support;
using VRageMath;
using static CoreSystems.Support.NewProjectile;
using static CoreSystems.Support.WeaponDefinition.AmmoDef.TrajectoryDef;

namespace CoreSystems.Projectiles
{
    public partial class Projectiles
    {
        private void GenProjectiles()
        {
            for (int i = 0; i < NewProjectiles.Count; i++)
            {
                var gen = NewProjectiles[i];
                var w = gen.Muzzle.Weapon;
                var a = gen.AmmoDef;
                var t = gen.Type;
                var virts = gen.NewVirts;
                var muzzle = gen.Muzzle;
                var aimed = w.Comp.Data.Repo.Values.State.PlayerId == w.Comp.Session.PlayerId || w.Comp.TypeSpecific == CoreComponent.CompTypeSpecific.Phantom;

                var patternCycle = gen.PatternCycle;
                var targetable = w.ActiveAmmoDef.AmmoDef.Const.Health > 0 && !w.ActiveAmmoDef.AmmoDef.Const.IsBeamWeapon;
                var p = Session.Projectiles.ProjectilePool.Count > 0 ? Session.Projectiles.ProjectilePool.Pop() : new Projectile();
                p.Info.Id = Session.Projectiles.CurrentProjectileId++;
                p.Info.System = w.System;
                p.Info.Ai = w.Comp.Ai;
                p.Info.AimedShot = aimed;
                p.Info.AmmoDef = a;
                p.Info.DoDamage = w.System.Session.IsServer && (!a.Const.ClientPredictedAmmo || t == Kind.Client || w.Comp.Data.Repo.Values.State.PlayerId < 0); // shrapnel do not run this loop, but do inherit DoDamage from parent.

                p.Info.Overrides = w.Comp.Data.Repo.Values.Set.Overrides;
                p.Info.Target.TargetEntity = t != Kind.Client ? w.Target.TargetEntity : gen.TargetEnt;
                p.Info.Target.Projectile = w.Target.Projectile;
                p.Info.Target.IsProjectile = w.Target.Projectile != null;
                p.Info.Target.IsFakeTarget = w.Comp.FakeMode;
                p.Info.Target.CoreCube = w.Comp.Cube;
                p.Info.Target.CoreEntity = w.Comp.CoreEntity;

                p.Info.DummyTargets = null;
                if (w.Comp.FakeMode)
                    w.Comp.Session.PlayerDummyTargets.TryGetValue(w.Comp.Data.Repo.Values.State.PlayerId, out p.Info.DummyTargets);

                p.Info.PartId = w.PartId;
                p.Info.BaseDamagePool = a == w.ActiveAmmoDef.AmmoDef ? w.BaseDamage : a.Const.BaseDamage;
                p.Info.EnableGuidance = w.Comp.Data.Repo.Values.Set.Guidance;
                p.Info.WeaponCache = w.WeaponCache;
                p.Info.Random = new XorShiftRandomStruct((ulong)(w.TargetData.WeaponRandom.CurrentSeed + (w.Reload.EndId + w.ProjectileCounter++)));
                p.Info.LockOnFireState = w.LockOnFireState || w.SkipAimChecks;
                p.Info.ModOverride = w.Comp.ModOverride;
                p.Info.ShooterVel = w.Comp.Ai.GridVel;

                p.Info.OriginUp = t != Kind.Client ? w.MyPivotUp : gen.OriginUp;
                p.Info.MaxTrajectory = t != Kind.Client ? a.Const.MaxTrajectoryGrows && w.FireCounter < a.Trajectory.MaxTrajectoryTime ? a.Const.TrajectoryStep * w.FireCounter : a.Const.MaxTrajectory : gen.MaxTrajectory;
                p.Info.MuzzleId = t != Kind.Virtual ? muzzle.MuzzleId : -1;
                p.Info.UniqueMuzzleId = muzzle.UniqueId;
                p.Info.WeaponCache.VirutalId = t != Kind.Virtual ? -1 : p.Info.WeaponCache.VirutalId;
                p.Info.Origin = t != Kind.Client ? t != Kind.Virtual ? muzzle.Position : w.MyPivotPos : gen.Origin;
                p.Info.Direction = t != Kind.Client ? t != Kind.Virtual ? gen.Direction : w.MyPivotFwd : gen.Direction;
                
                if (t == Kind.Client && !a.Const.IsBeamWeapon) 
                    p.Velocity = gen.Velocity;
                
                float shotFade;
                if (a.Const.HasShotFade && !a.Const.VirtualBeams)
                {
                    if (patternCycle > a.AmmoGraphics.Lines.Tracer.VisualFadeStart)
                        shotFade = MathHelper.Clamp(((patternCycle - a.AmmoGraphics.Lines.Tracer.VisualFadeStart)) * a.Const.ShotFadeStep, 0, 1);
                    else if (w.System.DelayCeaseFire && w.CeaseFireDelayTick != Session.Tick)
                        shotFade = MathHelper.Clamp(((Session.Tick - w.CeaseFireDelayTick) - a.AmmoGraphics.Lines.Tracer.VisualFadeStart) * a.Const.ShotFadeStep, 0, 1);
                    else shotFade = 0;
                }
                else shotFade = 0;
                p.Info.ShotFade = shotFade;
                p.PredictedTargetPos = w.Target.TargetPos;
                p.DeadSphere.Center = w.MyPivotPos;
                p.DeadSphere.Radius = w.Comp.Ai.AiType == Ai.AiTypes.Grid ? w.Comp.Ai.GridEntity.GridSizeHalf + 0.1 : 0.5f;

                if (a.Const.FeelsGravity && w.System.Session.Tick - w.GravityTick > 60)
                {
                    w.GravityTick = w.System.Session.Tick;
                    float interference;
                    w.GravityPoint = Session.Physics.CalculateNaturalGravityAt(p.Position, out interference);
                }

                p.Gravity = w.GravityPoint;

                if (t != Kind.Virtual)
                {
                    p.Info.PrimeEntity = a.Const.PrimeModel ? a.Const.PrimeEntityPool.Get() : null;
                    p.Info.TriggerEntity = a.Const.TriggerModel ? Session.TriggerEntityPool.Get() : null;

                    if (targetable)
                        Session.Projectiles.AddTargets.Add(p);
                }
                else
                {
                    p.Info.WeaponCache.VirtualHit = false;
                    p.Info.WeaponCache.Hits = 0;
                    p.Info.WeaponCache.HitEntity.Entity = null;
                    for (int j = 0; j < virts.Count; j++)
                    {
                        var v = virts[j];
                        p.VrPros.Add(v.Info);
                        if (!a.Const.RotateRealBeam) p.Info.WeaponCache.VirutalId = 0;
                        else if (v.Rotate)
                        {
                            p.Info.Origin = v.Muzzle.Position;
                            p.Info.Direction = v.Muzzle.Direction;
                            p.Info.WeaponCache.VirutalId = v.VirtualId;
                        }
                    }
                    virts.Clear();
                    VirtInfoPools.Return(virts);
                }

                Session.Projectiles.ActiveProjetiles.Add(p);
                p.Start();

                p.Info.Monitors = w.Monitors;
                if (p.Info.Monitors?.Count > 0)
                {
                    Session.MonitoredProjectiles[p.Info.Id] = p;
                    for (int j = 0; j < p.Info.Monitors.Count; j++)
                        p.Info.Monitors[j].Invoke(w.Comp.Cube.EntityId, w.PartId, p.Info.Id, p.Info.Target.TargetId, p.Position, true);
                }
            }
            NewProjectiles.Clear();
        }

        private void SpawnFragments()
        {
            if (Session.FragmentsNeedingEntities.Count > 0)
                PrepFragmentEntities();

            int spawned = 0;
            for (int j = 0; j < ShrapnelToSpawn.Count; j++)
            {
                int count;
                ShrapnelToSpawn[j].Spawn(out count);
                spawned += count;
            }
            ShrapnelToSpawn.Clear();

            if (AddTargets.Count > 0)
                AddProjectileTargets();

            UpdateState(ActiveProjetiles.Count - spawned);
        }

        internal void PrepFragmentEntities()
        {
            for (int i = 0; i < Session.FragmentsNeedingEntities.Count; i++)
            {
                var frag = Session.FragmentsNeedingEntities[i];
                if (frag.AmmoDef.Const.PrimeModel && frag.PrimeEntity == null) frag.PrimeEntity = frag.AmmoDef.Const.PrimeEntityPool.Get();
                if (frag.AmmoDef.Const.TriggerModel && frag.TriggerEntity == null) frag.TriggerEntity = Session.TriggerEntityPool.Get();
            }
            Session.FragmentsNeedingEntities.Clear();
        }

        internal void AddProjectileTargets() // This calls AI late for fragments need to fix
        {
            for (int i = 0; i < AddTargets.Count; i++)
            {
                var p = AddTargets[i];
                for (int t = 0; t < p.Info.Ai.TargetAis.Count; t++)
                {

                    var targetAi = p.Info.Ai.TargetAis[t];
                    var addProjectile = p.Info.AmmoDef.Trajectory.Guidance != GuidanceType.None && targetAi.PointDefense;
                    if (!addProjectile && targetAi.PointDefense)
                    {
                        if (Vector3.Dot(p.Info.Direction, p.Info.Origin - targetAi.TopEntity.PositionComp.WorldMatrixRef.Translation) < 0)
                        {

                            var targetSphere = targetAi.TopEntity.PositionComp.WorldVolume;
                            targetSphere.Radius *= 3;
                            var testRay = new RayD(p.Info.Origin, p.Info.Direction);
                            var quickCheck = Vector3D.IsZero(targetAi.GridVel, 0.025) && targetSphere.Intersects(testRay) != null;

                            if (!quickCheck)
                            {
                                var deltaPos = targetSphere.Center - p.Info.Origin;
                                var deltaVel = targetAi.GridVel - p.Info.Ai.GridVel;
                                var timeToIntercept = MathFuncs.Intercept(deltaPos, deltaVel, p.Info.AmmoDef.Const.DesiredProjectileSpeed);
                                var predictedPos = targetSphere.Center + (float)timeToIntercept * deltaVel;
                                targetSphere.Center = predictedPos;
                            }

                            if (quickCheck || targetSphere.Intersects(testRay) != null)
                                addProjectile = true;
                        }
                    }
                    if (addProjectile)
                    {
                        targetAi.DeadProjectiles.Remove(p);
                        targetAi.LiveProjectile.Add(p);
                        targetAi.LiveProjectileTick = Session.Tick;
                        targetAi.NewProjectileTick = Session.Tick;
                        p.Watchers.Add(targetAi);
                    }
                }
            }
            AddTargets.Clear();
        }
    }
}
