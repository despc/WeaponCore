using System;
using System.Collections.Generic;
using CoreSystems.Projectiles;
using CoreSystems.Support;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Interfaces;
using VRage.Utils;
using VRageMath;
using static CoreSystems.Support.WeaponDefinition.AmmoDef.AreaDamageDef;
using static CoreSystems.Support.WeaponDefinition.AmmoDef.DamageScaleDef;

namespace CoreSystems
{

    public partial class Session
    {
        private bool _shieldNull;
        internal void ProcessHits()
        {
            LastDamageTick = Tick;
            _shieldNull = false;
            for (int x = 0; x < Hits.Count; x++)
            {

                var p = Hits[x];
                var info = p.Info;
                var maxObjects = info.AmmoDef.Const.MaxObjectsHit;
                var phantom = info.AmmoDef.BaseDamage <= 0;
                var pInvalid = (int)p.State > 3;
                var tInvalid = info.Target.IsProjectile && (int)info.Target.Projectile.State > 1;
                if (tInvalid) info.Target.Reset(Tick, Target.States.ProjectileClosed);
                var skip = pInvalid || tInvalid;
                for (int i = 0; i < info.HitList.Count; i++)
                {
                    var hitEnt = info.HitList[i];
                    var hitMax = info.ObjectsHit >= maxObjects;
                    var outOfPew = info.BaseDamagePool <= 0 && !(phantom && hitEnt.EventType == HitEntity.Type.Effect);

                    if (outOfPew && p.State == Projectile.ProjectileState.Detonate && i != info.HitList.Count - 1)
                    {
                        outOfPew = false;
                        info.BaseDamagePool = 0.01f;

                    }
                    if (skip || hitMax || outOfPew)
                    {
                        if (hitMax || outOfPew || pInvalid)
                        {
                            p.State = Projectile.ProjectileState.Depleted;
                        }
                        Projectiles.HitEntityPool.Return(hitEnt);
                        continue;
                    }

                    switch (hitEnt.EventType)
                    {
                        case HitEntity.Type.Shield:
                            DamageShield(hitEnt, info);
                            continue;
                        case HitEntity.Type.Grid:
                            DamageGrid(hitEnt, info);  //set to 2 for new det/radiant
                            continue;
                        case HitEntity.Type.Destroyable:
                            DamageDestObj(hitEnt, info);
                            continue;
                        case HitEntity.Type.Voxel:
                            DamageVoxel(hitEnt, info);
                            continue;
                        case HitEntity.Type.Projectile:
                            DamageProjectile(hitEnt, info);
                            continue;
                        case HitEntity.Type.Field:
                            UpdateField(hitEnt, info);
                            continue;
                        case HitEntity.Type.Effect:
                            UpdateEffect(hitEnt, info);
                            continue;
                    }

                    Projectiles.HitEntityPool.Return(hitEnt);
                }

                if (info.BaseDamagePool <= 0)
                    p.State = Projectile.ProjectileState.Depleted;

                info.HitList.Clear();
            }
            Hits.Clear();
        }

        private void DamageShield(HitEntity hitEnt, ProInfo info)
        {
            var shield = hitEnt.Entity as IMyTerminalBlock;
            if (shield == null || !hitEnt.HitPos.HasValue) return;
            if (!info.ShieldBypassed)
                info.ObjectsHit++;

            var directDmgGlobal = Settings.Enforcement.DirectDamageModifer;
            var areaDmgGlobal = Settings.Enforcement.AreaDamageModifer;
            var shieldDmgGlobal = Settings.Enforcement.ShieldDamageModifer;

            var damageScale = 1 * directDmgGlobal;
            var distTraveled = info.AmmoDef.Const.IsBeamWeapon ? hitEnt.HitDist ?? info.DistanceTraveled : info.DistanceTraveled;
            var fallOff = info.AmmoDef.Const.FallOffScaling && distTraveled > info.AmmoDef.Const.FallOffDistance;
            if (info.AmmoDef.Const.VirtualBeams) damageScale *= info.WeaponCache.Hits;
            var damageType = info.AmmoDef.DamageScales.Shields.Type;
            var heal = damageType == ShieldDef.ShieldType.Heal;
            var energy = info.AmmoDef.Const.EnergyShieldDmg;
            var areaEffect = info.AmmoDef.AreaEffect;
            var detonateOnEnd = info.AmmoDef.AreaEffect.Detonation.DetonateOnEnd && info.Age >= info.AmmoDef.AreaEffect.Detonation.MinArmingTime && areaEffect.AreaEffect != AreaEffectType.Disabled && !info.ShieldBypassed;
            var areaDamage = areaEffect.AreaEffect != AreaEffectType.Disabled ? (info.AmmoDef.Const.AreaEffectDamage * (info.AmmoDef.Const.AreaEffectSize * 0.5f)) * areaDmgGlobal : 0;
            var scaledBaseDamage = info.BaseDamagePool * damageScale;

            var scaledDamage = (scaledBaseDamage + areaDamage) * info.AmmoDef.Const.ShieldModifier * shieldDmgGlobal;

            if (fallOff)
            {
                var fallOffMultipler = MathHelperD.Clamp(1.0 - ((distTraveled - info.AmmoDef.Const.FallOffDistance) / (info.AmmoDef.Const.MaxTrajectory - info.AmmoDef.Const.FallOffDistance)), info.AmmoDef.DamageScales.FallOff.MinMultipler, 1);
                scaledDamage *= fallOffMultipler;
            }

            scaledDamage = (scaledDamage * info.ShieldResistMod) * info.ShieldBypassMod;
            var unscaledDetDmg = info.AmmoDef.Const.DetonationDamage * (info.AmmoDef.Const.DetonationRadius * 0.5f);
            var detonateDamage = detonateOnEnd && info.ShieldBypassMod >= 1 ? (unscaledDetDmg * info.AmmoDef.Const.ShieldModifier * areaDmgGlobal * shieldDmgGlobal) * info.ShieldResistMod : 0;
            if (heal)
            {
                var heat = SApi.GetShieldHeat(shield);

                switch (heat)
                {
                    case 0:
                        scaledDamage *= -1;
                        detonateDamage *= -1;
                        break;
                    case 100:
                        scaledDamage = -0.01f;
                        detonateDamage = -0.01f;
                        break;
                    default:
                        {
                            var dec = heat / 100f;
                            var healFactor = 1 - dec;
                            scaledDamage *= healFactor;
                            scaledDamage *= -1;
                            detonateDamage *= healFactor;
                            detonateDamage *= -1;
                            break;
                        }
                }
            }
            var hitWave = info.AmmoDef.Const.RealShotsPerMin <= 120;
            var hit = SApi.PointAttackShieldCon(shield, hitEnt.HitPos.Value, info.Target.CoreEntity.EntityId, (float)scaledDamage, (float)detonateDamage, energy, hitWave);
            if (hit.HasValue)
            {

                if (heal)
                {
                    info.BaseDamagePool = 0;
                    return;
                }

                var objHp = hit.Value;


                if (info.EwarActive)
                    info.BaseDamagePool -= 1;
                else if (objHp > 0)
                {

                    if (!info.ShieldBypassed)
                        info.BaseDamagePool = 0;
                    else
                        info.BaseDamagePool -= (info.BaseDamagePool * info.ShieldResistMod) * info.ShieldBypassMod;
                }
                else info.BaseDamagePool = (objHp * -1);

                if (info.AmmoDef.Mass <= 0) return;

                var speed = !info.AmmoDef.Const.IsBeamWeapon && info.AmmoDef.Const.DesiredProjectileSpeed > 0 ? info.AmmoDef.Const.DesiredProjectileSpeed : 1;
                if (Session.IsServer && !shield.CubeGrid.IsStatic && !SApi.IsFortified(shield))
                    ApplyProjectileForce((MyEntity)shield.CubeGrid, hitEnt.HitPos.Value, hitEnt.Intersection.Direction, info.AmmoDef.Mass * speed);
            }
            else if (!_shieldNull)
            {
                Log.Line($"DamageShield PointAttack returned null");
                _shieldNull = true;
            }
        }

        private void DamageGrid2(HitEntity hitEnt, ProInfo t)
        {
            
            var grid = hitEnt.Entity as MyCubeGrid;
            if (grid == null || grid.MarkedForClose || !hitEnt.HitPos.HasValue || hitEnt.Blocks == null)
            {
                hitEnt.Blocks?.Clear();
                Log.Line($"DamageGrid first null check hit");
                return;
            }

            if (t.AmmoDef.DamageScales.Shields.Type == ShieldDef.ShieldType.Heal || (!t.AmmoDef.Const.SelfDamage && !t.AmmoDef.Const.IsCriticalReaction) && t.Ai.AiType == Ai.AiTypes.Grid && t.Ai.GridEntity.IsInSameLogicalGroupAs(grid) || !grid.DestructibleBlocks || grid.Immune || grid.GridGeneralDamageModifier <= 0)
            {
                t.BaseDamagePool = 0;
                return;
            }
            var canDamage = t.DoDamage;
            _destroyedSlims.Clear();
            //_destroyedSlimsClient.Clear();
            //Global modifiers
            var directDmgGlobal = Settings.Enforcement.DirectDamageModifer;
            var areaDmgGlobal = Settings.Enforcement.AreaDamageModifer;
            var sync = MpActive && (DedicatedServer || IsServer);
            float gridDamageModifier = grid.GridGeneralDamageModifier;
            //Target/targeting Info
            var largeGrid = grid.GridSizeEnum == MyCubeSize.Large;
            var attackerId = t.Target.CoreEntity.EntityId;
            var maxObjects = t.AmmoDef.Const.MaxObjectsHit == 2147483647 ? 128 : t.AmmoDef.Const.MaxObjectsHit; //BDC - this adds a sensible max rather than int max
            var gridMatrix = grid.PositionComp.WorldMatrixRef;
            var playerAi = t.Ai.AiType == Ai.AiTypes.Player;
            //Ammo properties
            //Detonate info
            var detonateRadius = t.AmmoDef.AreaEffect.Detonation.DetonationRadius; 
            var detonateOnEnd = t.AmmoDef.AreaEffect.Detonation.DetonateOnEnd && t.Age >= t.AmmoDef.AreaEffect.Detonation.MinArmingTime;
            var detonateDmg = t.AmmoDef.Const.DetonationDamage;
            var hasDetDmg = detonateOnEnd && detonateDmg > 0 && detonateRadius > 0;
            var detonateDepth = t.AmmoDef.Const.DetonationMaxDepth; 
            var detonatefalloff = t.AmmoDef.AreaEffect.Detonation.DetonationFalloff;         
            //Radiant/area info
            var areaRadius = t.AmmoDef.AreaEffect.AreaEffectRadius;
            var areaDepth = t.AmmoDef.Const.AreaAffectMaxDepth; 
            var areaEffect = t.AmmoDef.AreaEffect.AreaEffect;
            var radiant = areaEffect == AreaEffectType.Radiant;
            var areaEffectDmg = areaEffect != AreaEffectType.Disabled ? t.AmmoDef.Const.AreaEffectDamage : 0;
            var hasAreaDmg = areaEffectDmg > 0 && areaRadius > 0;
            var radiantFall = t.AmmoDef.AreaEffect.RadiantFalloff;
            var totaldmg = 0f;
            var maxabsorb = t.AmmoDef.AreaEffect.AreaEffectMaxAbsorb;  //add to coreparts
            var radiantcomplete = false;
            var hasAoe = hasAreaDmg || hasDetDmg;
            var aoeHits = 0;  
            //Other properties
            var hitMass = t.AmmoDef.Mass;            
            var damageType = t.ShieldBypassed ? ShieldBypassDamageType : radiant || hasDetDmg ? MyDamageType.Explosion : MyDamageType.Bullet;
            var distTraveled = t.AmmoDef.Const.IsBeamWeapon ? hitEnt.HitDist ?? t.DistanceTraveled : t.DistanceTraveled;
            var direction = hitEnt.Intersection.Direction;
            //overall falloff scaling
            var fallOff = t.AmmoDef.Const.FallOffScaling && distTraveled > t.AmmoDef.Const.FallOffDistance;
            var fallOffMultipler = 1f;
            if (fallOff)
            {
                fallOffMultipler = (float)MathHelperD.Clamp(1.0 - ((distTraveled - t.AmmoDef.Const.FallOffDistance) / (t.AmmoDef.Const.MaxTrajectory - t.AmmoDef.Const.FallOffDistance)), t.AmmoDef.DamageScales.FallOff.MinMultipler, 1);
            }
            //hit & damage loop info
            var basePool = t.BaseDamagePool;
            int hits = 1;
            if (t.AmmoDef.Const.VirtualBeams)
            {
                hits = t.WeaponCache.Hits;
            }
            var partialShield = t.ShieldInLine && !t.ShieldBypassed && SApi.MatchEntToShieldFast(grid, true) != null;
            var objectsHit = t.ObjectsHit;
            var blockCount = hitEnt.Blocks.Count;
            var countBlocksAsObjects = t.AmmoDef.ObjectsHit.CountBlocks;

            //General damage data
            var d = t.AmmoDef.DamageScales;
            //Switches and setup for damage types/event loops
            var radiating = false;
            var novaing = false;
            var novacomplete = false;
            var earlyExit = false;
            var destroyed = 0;
            int maxDbc = 0;
            IMySlimBlock rootBlock = null;
            //Main loop (finally)
            
            for (int i = 0; i < blockCount; i++)
            {
                if (earlyExit || (basePool <= 0 || objectsHit >= maxObjects) && !novaing)
                {
                    Log.Line($"Early exit {earlyExit} basePool {basePool} objhit {objectsHit} maxObj {maxObjects}  novaing{novaing}");
                    basePool = 0;
                    break;
                }

                rootBlock = hitEnt.Blocks[i];
                if (!novaing)
                {
                    if (IsServer && _destroyedSlims.Contains(rootBlock) || IsClient && _destroyedSlimsClient.Contains(rootBlock)) continue;
                    if (rootBlock.IsDestroyed)
                    {
                        destroyed++;
                        if (IsClient)
                        {
                            _destroyedSlimsClient.Add(rootBlock);
                            _slimHealthClient.Remove(rootBlock);
                        }
                        else
                            _destroyedSlims.Add(rootBlock);

                        continue;
                    }
                    var fatBlock = rootBlock.FatBlock as MyCubeBlock;
                    var door = fatBlock as MyDoorBase;
                    if (door != null && door.Open && !HitDoor(hitEnt, door) || playerAi && !RayAccuracyCheck(hitEnt, rootBlock))
                        continue;
                }


                //radiant logic
                if (hasAreaDmg && !novaing && !radiantcomplete)
                {
                    RadiantAoe(rootBlock, grid, areaRadius, areaDepth, direction, ref maxDbc, ref aoeHits);
                    radiating = true;
                }

                //Nova logic
                if (hasDetDmg && novaing && !novacomplete)
                {
                    RadiantAoe(rootBlock, grid, detonateRadius, detonateDepth, direction, ref maxDbc, ref aoeHits);
                    novacomplete = true;
                }
          
                for (int j = 0; j < maxDbc+1; j++)//Loop through blocks "hit" by damage, in groups by range.  J essentially = dist to root
                {
                    if (totaldmg >= maxabsorb && !radiantcomplete)
                    {
                        radiantcomplete = true;
                        radiating = false;
                        aoeHits = 0;
                        if (hasDetDmg && basePool <= 0) novaing = true;
                        --i;
                        break;
                    }

                    int dbCount = 1;
                    float expDamageFall = 0;
                    List<IMySlimBlock> dbc = null;
                    if (hasAoe && novacomplete || radiating)
                    {
                        
                        dbc = DamageBlockCache[j];
                        dbCount = dbc.Count;
                        if (dbCount == 0)
                        {
                           continue;
                        }


                        //Falloff switches & calcs for type of explosion & expDamageFall as output
                        var maxfalldist = radiating ? areaRadius * grid.GridSizeR +1: detonateRadius * grid.GridSizeR+1;
                        var fallNone = radiating ? areaEffectDmg : detonateDmg; //outside of switch case, as we can use it for "raw damage" in all falloff cases
                        switch (radiating ? radiantFall : detonatefalloff)
                        {
                            case Falloff.Legacy:
                                if (radiating)//mimic InvCurve for legacy radiating
                                {
                                    expDamageFall= (float)((maxfalldist - j) / maxfalldist * (maxfalldist - j) / maxfalldist * fallNone);
                                }
                                else //mimic linear falloff for legacy detonations
                                {
                                    expDamageFall= (float)((maxfalldist - j) / maxfalldist * fallNone);
                                }
                                break;
                            case Falloff.None:  //No falloff, damage stays the same regardless of distance
                                expDamageFall = fallNone;
                                break;
                            case Falloff.Linear: //Damage is evenly stretched from 1 to max dist, dropping in equal increments
                                expDamageFall = (float)((maxfalldist - j) / maxfalldist * fallNone);
                                break;
                            case Falloff.Curve:  //Drops sharply closer to max range
                                expDamageFall = (float)(100 - j / maxfalldist / (maxfalldist - j) * fallNone);
                                break;
                            case Falloff.InvCurve:  //Drops at beginning, roughly similar to inv square
                                expDamageFall = (float)((maxfalldist - j) / maxfalldist * (maxfalldist - j) / maxfalldist * fallNone);
                                break;
                            case Falloff.Spall: //Damage is highest at furthest point from impact, to create a spall or crater
                                expDamageFall = (float)((j + 1) / maxfalldist / (maxfalldist - j) * fallNone);
                                break;

                        }
                    }

  
                    //apply to blocks (k) in distance group (j)
                    for (int k = 0; k < dbCount; k++)
                    {

                        var block = radiating || novaing && novacomplete ? dbc[k] : rootBlock;
                        if (block.IsDestroyed)
                           continue;

                        if (partialShield && SApi.IsBlockProtected(block))
                        {
                            earlyExit = true;
                            break;
                        }

                        var cubeBlockDef = (MyCubeBlockDefinition)block.BlockDefinition;
                        float cachedIntegrity;
                        var blockHp = !IsClient ? block.Integrity - block.AccumulatedDamage : (_slimHealthClient.TryGetValue(block, out cachedIntegrity) ? cachedIntegrity : block.Integrity);
                        var blockDmgModifier = cubeBlockDef.GeneralDamageMultiplier;
                        float damageScale = hits; 
                        float directDamageScale = directDmgGlobal;
                        float areaDamageScale = areaDmgGlobal;
                        float detDamageScale = areaDmgGlobal;
                        //Damage scaling for blocktypes
                        if (t.AmmoDef.Const.DamageScaling || !MyUtils.IsEqual(blockDmgModifier, 1f) || !MyUtils.IsEqual(gridDamageModifier, 1f))
                        {
                            if (blockDmgModifier < 0.000000001f || gridDamageModifier < 0.000000001f)
                                blockHp = float.MaxValue;
                            else
                                blockHp = (blockHp / blockDmgModifier / gridDamageModifier);

                            
                            if (d.MaxIntegrity > 0 && blockHp > d.MaxIntegrity)
                            {
                                basePool = 0;
                                continue;
                            }

                            if (d.Grids.Large >= 0 && largeGrid) damageScale *= d.Grids.Large;
                            else if (d.Grids.Small >= 0 && !largeGrid) damageScale *= d.Grids.Small;

                            MyDefinitionBase blockDef = null;
                            if (t.AmmoDef.Const.ArmorScaling)
                            {
                                blockDef = block.BlockDefinition;
                                var isArmor = AllArmorBaseDefinitions.Contains(blockDef) || CustomArmorSubtypes.Contains(blockDef.Id.SubtypeId);
                                if (isArmor && d.Armor.Armor >= 0) damageScale *= d.Armor.Armor;
                                else if (!isArmor && d.Armor.NonArmor >= 0) damageScale *= d.Armor.NonArmor;
                                if (isArmor && (d.Armor.Light >= 0 || d.Armor.Heavy >= 0))
                                {
                                    var isHeavy = HeavyArmorBaseDefinitions.Contains(blockDef) || CustomHeavyArmorSubtypes.Contains(blockDef.Id.SubtypeId);
                                    if (isHeavy && d.Armor.Heavy >= 0) damageScale *= d.Armor.Heavy;
                                    else if (!isHeavy && d.Armor.Light >= 0) damageScale *= d.Armor.Light;
                                }
                            }
                            if (t.AmmoDef.Const.CustomDamageScales)
                            {
                                if (blockDef == null) blockDef = block.BlockDefinition;
                                float modifier;
                                var found = t.AmmoDef.Const.CustomBlockDefinitionBasesToScales.TryGetValue(blockDef, out modifier);
                                if (found) damageScale *= modifier;
                                else if (t.AmmoDef.DamageScales.Custom.IgnoreAllOthers) continue;
                            }

                            if (GlobalDamageModifed)
                            {
                                if (blockDef == null) blockDef = block.BlockDefinition;
                                BlockDamage modifier;
                                var found = BlockDamageMap.TryGetValue(blockDef, out modifier);

                                if (found)
                                {
                                    directDamageScale *= modifier.DirectModifer;
                                    areaDamageScale *= modifier.AreaModifer;
                                    detDamageScale *= modifier.AreaModifer;
                                }
                            }
                            if (ArmorCoreActive)
                            {
                                var subtype = block.BlockDefinition.Id.SubtypeId;
                                if (ArmorCoreBlockMap.ContainsKey(subtype))
                                {
                                    var resistances = ArmorCoreBlockMap[subtype];
                                    directDamageScale /= t.AmmoDef.Const.EnergyBaseDmg ? resistances.EnergeticResistance : resistances.KineticResistance;
                                    areaDamageScale /= t.AmmoDef.Const.EnergyAreaDmg ? resistances.EnergeticResistance : resistances.KineticResistance;
                                    detDamageScale /= t.AmmoDef.Const.EnergyDetDmg ? resistances.EnergeticResistance : resistances.KineticResistance;
                                }
                            }

                            if (fallOff)
                                damageScale *= fallOffMultipler;
                        }

                        var primaryDamage = block == rootBlock && !novaing;
                        var baseScale = damageScale * directDamageScale;
                        var scaledDamage = basePool * baseScale;

                        //Radiant & nova specific damage scaling
                        if (radiating&&j>0)//Give radiant dmg to all blocks except root
                        {
                           scaledDamage = (expDamageFall * areaDamageScale);
                           totaldmg += scaledDamage;
                        }
                        else if (radiating && j == 0)
                        {
                            totaldmg += (expDamageFall * areaDamageScale); //Tallies radiant on root toward maxabsorb
                            scaledDamage += (expDamageFall * areaDamageScale); //Adds radiant dmg to primary for root block
                        }
                        else if (novacomplete)
                        {
                           scaledDamage = (expDamageFall * detDamageScale);
                        }
                        if (scaledDamage <= blockHp && primaryDamage)
                        {
                            basePool = 0;
                            t.BaseDamagePool = basePool;
                            novaing = hasDetDmg;
                            
                            if (hitMass > 0) 
                            {
                                var speed = !t.AmmoDef.Const.IsBeamWeapon && t.AmmoDef.Const.DesiredProjectileSpeed > 0 ? t.AmmoDef.Const.DesiredProjectileSpeed : 1;
                                if (Session.IsServer) ApplyProjectileForce(grid, grid.GridIntegerToWorld(rootBlock.Position), hitEnt.Intersection.Direction, (hitMass * speed));
                            }
                        }
                        else
                        {
                            destroyed++;
                            if (IsClient)
                            {
                                _destroyedSlimsClient.Add(block);
                                if (_slimHealthClient.ContainsKey(block))
                                    _slimHealthClient.Remove(block);
                            }
                            else
                                _destroyedSlims.Add(block);

                            if (primaryDamage)
                            {
                                basePool -= (blockHp / baseScale);
                                objectsHit++;
                            }
                        }



                        aoeHits--;
                        //Add in a final death-check to pop the nova iteration
                        var endCycle = ((basePool <= 0 || objectsHit >= maxObjects) || aoeHits<=0);
                        if (novacomplete) endCycle = true;
                        if (radiantcomplete && !hasDetDmg) endCycle = true;                    
                        //Apply damage
                        if (canDamage)
                        {
                            block.DoDamage(scaledDamage, damageType, sync, null, attackerId); //can this keen dmg method be avoided?
                        }
                        else
                        {
                            var realDmg = scaledDamage * gridDamageModifier * blockDmgModifier;
                            if (_slimHealthClient.ContainsKey(block))
                            {
                                if (_slimHealthClient[block] - realDmg > 0)
                                    _slimHealthClient[block] -= realDmg;
                                else
                                    _slimHealthClient.Remove(block);
                            }
                            else if (block.Integrity - realDmg > 0) _slimHealthClient[block] = blockHp - realDmg;
                        }

                        if (radiating && aoeHits == 0)
                        {
                            radiantcomplete = true;
                            radiating = false;
                        }
                        else if (!radiating && !radiant)
                        {
                            radiantcomplete = !radiant;
                        }

                        //doneskies
                        if (endCycle)
                        {


                            if (novaing && !novacomplete && radiantcomplete)
                            {
                                --i;
                                if (dbc != null)
                                {
                                    dbc.Clear();
                                }
                                break;
                            }

                            if (primaryDamage)
                            {
                                t.BaseDamagePool = 0;
                                t.ObjectsHit = objectsHit;
                            }
                        }
                    }
                    if (dbc != null)
                    {
                        dbc.Clear();
                    }


                }
            
            }

            //stuff I haven't looked at yet
            if (rootBlock != null && destroyed > 0)
            {
                var fat = rootBlock.FatBlock;
                MyOrientedBoundingBoxD obb;
                if (fat != null)
                    obb = new MyOrientedBoundingBoxD(fat.Model.BoundingBox, fat.PositionComp.WorldMatrixRef);
                else
                {
                    Vector3 halfExt;
                    rootBlock.ComputeScaledHalfExtents(out halfExt);
                    var blockBox = new BoundingBoxD(-halfExt, halfExt);
                    gridMatrix.Translation = grid.GridIntegerToWorld(rootBlock.Position);
                    obb = new MyOrientedBoundingBoxD(blockBox, gridMatrix);
                }

                var dist = obb.Intersects(ref hitEnt.Intersection);
                if (dist.HasValue)
                    t.Hit.LastHit = hitEnt.Intersection.From + (hitEnt.Intersection.Direction * dist.Value);
            }
            if (!countBlocksAsObjects) 
                t.ObjectsHit += 1;

            if (!novaing)
            {
                t.BaseDamagePool = basePool;
                t.ObjectsHit = objectsHit;
            }

            hitEnt.Blocks.Clear();
        }


        private void DamageDestObj(HitEntity hitEnt, ProInfo info)
        {
            var entity = hitEnt.Entity;
            var destObj = hitEnt.Entity as IMyDestroyableObject;

            if (destObj == null || entity == null) return;

            var directDmgGlobal = Settings.Enforcement.DirectDamageModifer;
            var areaDmgGlobal = Settings.Enforcement.AreaDamageModifer;

            var shieldHeal = info.AmmoDef.DamageScales.Shields.Type == ShieldDef.ShieldType.Heal;
            var sync = MpActive && IsServer;

            var attackerId = info.Target.CoreEntity.EntityId;

            var objHp = destObj.Integrity;
            var integrityCheck = info.AmmoDef.DamageScales.MaxIntegrity > 0;
            if (integrityCheck && objHp > info.AmmoDef.DamageScales.MaxIntegrity || shieldHeal)
            {
                info.BaseDamagePool = 0;
                return;
            }

            var character = hitEnt.Entity as IMyCharacter;
            float damageScale = 1;
            if (info.AmmoDef.Const.VirtualBeams) damageScale *= info.WeaponCache.Hits;
            if (character != null && info.AmmoDef.DamageScales.Characters >= 0)
                damageScale *= info.AmmoDef.DamageScales.Characters;

            var areaEffect = info.AmmoDef.AreaEffect;
            var areaDamage = areaEffect.AreaEffect != AreaEffectType.Disabled ? (info.AmmoDef.Const.AreaEffectDamage * (info.AmmoDef.Const.AreaEffectSize * 0.5f)) * areaDmgGlobal : 0;
            var scaledDamage = (float)((((info.BaseDamagePool * damageScale) * directDmgGlobal) + areaDamage) * info.ShieldResistMod);

            var distTraveled = info.AmmoDef.Const.IsBeamWeapon ? hitEnt.HitDist ?? info.DistanceTraveled : info.DistanceTraveled;

            var fallOff = info.AmmoDef.Const.FallOffScaling && distTraveled > info.AmmoDef.Const.FallOffDistance;
            if (fallOff)
            {
                var fallOffMultipler = (float)MathHelperD.Clamp(1.0 - ((distTraveled - info.AmmoDef.Const.FallOffDistance) / (info.AmmoDef.Const.MaxTrajectory - info.AmmoDef.Const.FallOffDistance)), info.AmmoDef.DamageScales.FallOff.MinMultipler, 1);
                scaledDamage *= fallOffMultipler;
            }

            if (scaledDamage < objHp) info.BaseDamagePool = 0;
            else
            {
                var damageLeft = scaledDamage - objHp;
                var reduction = scaledDamage / damageLeft;

                info.BaseDamagePool *= reduction;
            }

            if (info.DoDamage)
                destObj.DoDamage(scaledDamage, !info.ShieldBypassed ? MyDamageType.Bullet : MyDamageType.Drill, sync, null, attackerId);
            if (info.AmmoDef.Mass > 0)
            {
                var speed = !info.AmmoDef.Const.IsBeamWeapon && info.AmmoDef.Const.DesiredProjectileSpeed > 0 ? info.AmmoDef.Const.DesiredProjectileSpeed : 1;
                if (Session.IsServer) ApplyProjectileForce(entity, entity.PositionComp.WorldAABB.Center, hitEnt.Intersection.Direction, (info.AmmoDef.Mass * speed));
            }
        }

        private static void DamageProjectile(HitEntity hitEnt, ProInfo attacker)
        {
            var pTarget = hitEnt.Projectile;
            if (pTarget == null) return;

            attacker.ObjectsHit++;
            var objHp = pTarget.Info.BaseHealthPool;
            var integrityCheck = attacker.AmmoDef.DamageScales.MaxIntegrity > 0;
            if (integrityCheck && objHp > attacker.AmmoDef.DamageScales.MaxIntegrity) return;

            var damageScale = (float)attacker.AmmoDef.Const.HealthHitModifier;
            if (attacker.AmmoDef.Const.VirtualBeams) damageScale *= attacker.WeaponCache.Hits;
            var scaledDamage = 1 * damageScale;

            var distTraveled = attacker.AmmoDef.Const.IsBeamWeapon ? hitEnt.HitDist ?? attacker.DistanceTraveled : attacker.DistanceTraveled;

            var fallOff = attacker.AmmoDef.Const.FallOffScaling && distTraveled > attacker.AmmoDef.Const.FallOffDistance;
            if (fallOff)
            {
                var fallOffMultipler = (float)MathHelperD.Clamp(1.0 - ((distTraveled - attacker.AmmoDef.Const.FallOffDistance) / (attacker.AmmoDef.Const.MaxTrajectory - attacker.AmmoDef.Const.FallOffDistance)), attacker.AmmoDef.DamageScales.FallOff.MinMultipler, 1);
                scaledDamage *= fallOffMultipler;
            }

            if (scaledDamage >= objHp)
            {

                var safeObjHp = objHp <= 0 ? 0.0000001f : objHp;
                var remaining = (scaledDamage / safeObjHp) / damageScale;
                attacker.BaseDamagePool -= remaining;
                pTarget.Info.BaseHealthPool = 0;
                pTarget.State = Projectile.ProjectileState.Destroy;
                if (attacker.AmmoDef.Const.DetonationDamage > 0 && attacker.AmmoDef.AreaEffect.Detonation.DetonateOnEnd && attacker.Age >= attacker.AmmoDef.AreaEffect.Detonation.MinArmingTime)
                    DetonateProjectile(hitEnt, attacker);
            }
            else
            {
                attacker.BaseDamagePool = 0;
                pTarget.Info.BaseHealthPool -= scaledDamage;
                DetonateProjectile(hitEnt, attacker);
            }
        }

        private static void DetonateProjectile(HitEntity hitEnt, ProInfo attacker)
        {
            if (attacker.AmmoDef.Const.DetonationDamage > 0 && attacker.AmmoDef.AreaEffect.Detonation.DetonateOnEnd && attacker.Age >= attacker.AmmoDef.AreaEffect.Detonation.MinArmingTime)
            {
                var areaSphere = new BoundingSphereD(hitEnt.Projectile.Position, attacker.AmmoDef.Const.DetonationRadius);
                foreach (var sTarget in attacker.Ai.LiveProjectile)
                {

                    if (areaSphere.Contains(sTarget.Position) != ContainmentType.Disjoint)
                    {

                        var objHp = sTarget.Info.BaseHealthPool;
                        var integrityCheck = attacker.AmmoDef.DamageScales.MaxIntegrity > 0;
                        if (integrityCheck && objHp > attacker.AmmoDef.DamageScales.MaxIntegrity) continue;

                        var damageScale = (float)attacker.AmmoDef.Const.HealthHitModifier;
                        if (attacker.AmmoDef.Const.VirtualBeams) damageScale *= attacker.WeaponCache.Hits;
                        var scaledDamage = 1 * damageScale;

                        if (scaledDamage >= objHp)
                        {
                            sTarget.Info.BaseHealthPool = 0;
                            sTarget.State = Projectile.ProjectileState.Destroy;
                        }
                        else sTarget.Info.BaseHealthPool -= attacker.AmmoDef.Const.Health;
                    }
                }
            }
        }

        private void DamageVoxel(HitEntity hitEnt, ProInfo info)
        {
            var entity = hitEnt.Entity;
            var destObj = hitEnt.Entity as MyVoxelBase;
            if (destObj == null || entity == null || !hitEnt.HitPos.HasValue) return;
            var shieldHeal = info.AmmoDef.DamageScales.Shields.Type == ShieldDef.ShieldType.Heal;
            if (!info.AmmoDef.Const.VoxelDamage || shieldHeal)
            {
                info.BaseDamagePool = 0;
                return;
            }

            var directDmgGlobal = Settings.Enforcement.DirectDamageModifer;
            var detDmgGlobal = Settings.Enforcement.AreaDamageModifer;

            using (destObj.Pin())
            {
                var detonateOnEnd = info.AmmoDef.Const.AmmoAreaEffect && info.AmmoDef.AreaEffect.Detonation.DetonateOnEnd && info.Age >= info.AmmoDef.AreaEffect.Detonation.MinArmingTime;

                info.ObjectsHit++;
                float damageScale = 1 * directDmgGlobal;
                if (info.AmmoDef.Const.VirtualBeams) damageScale *= info.WeaponCache.Hits;

                var scaledDamage = info.BaseDamagePool * damageScale;

                var distTraveled = info.AmmoDef.Const.IsBeamWeapon ? hitEnt.HitDist ?? info.DistanceTraveled : info.DistanceTraveled;
                var fallOff = info.AmmoDef.Const.FallOffScaling && distTraveled > info.AmmoDef.Const.FallOffDistance;

                if (fallOff)
                {
                    var fallOffMultipler = (float)MathHelperD.Clamp(1.0 - ((distTraveled - info.AmmoDef.Const.FallOffDistance) / (info.AmmoDef.Const.MaxTrajectory - info.AmmoDef.Const.FallOffDistance)), info.AmmoDef.DamageScales.FallOff.MinMultipler, 1);
                    scaledDamage *= fallOffMultipler;
                }

                var oRadius = info.AmmoDef.Const.AreaEffectSize;
                var minTestRadius = distTraveled - info.PrevDistanceTraveled;
                var tRadius = oRadius < minTestRadius && !info.AmmoDef.Const.IsBeamWeapon ? minTestRadius : oRadius;
                var objHp = (int)MathHelper.Clamp(MathFuncs.VolumeCube(MathFuncs.LargestCubeInSphere(tRadius)), 5000, double.MaxValue);


                if (tRadius > 5) objHp *= 5;

                if (scaledDamage < objHp)
                {
                    var reduceBy = objHp / scaledDamage;
                    oRadius /= reduceBy;
                    if (oRadius < 1) oRadius = 1;

                    info.BaseDamagePool = 0;
                }
                else
                {
                    info.BaseDamagePool -= objHp;
                    if (oRadius < minTestRadius) oRadius = minTestRadius;
                }
                destObj.PerformCutOutSphereFast(hitEnt.HitPos.Value, (float)(oRadius * info.AmmoDef.Const.VoxelHitModifier), false);

                if (detonateOnEnd && info.BaseDamagePool <= 0)
                {
                    var dRadius = info.AmmoDef.Const.DetonationRadius;
                    var dDamage = info.AmmoDef.Const.DetonationDamage * detDmgGlobal;

                    if (dRadius < 1.5) dRadius = 1.5f;

                    if (info.DoDamage)
                        SUtils.CreateMissileExplosion(this, dDamage, dRadius, hitEnt.HitPos.Value, hitEnt.Intersection.Direction, info.Target.CoreEntity, destObj, info.AmmoDef, true);
                }
            }
        }

        public void RadiantAoe(IMySlimBlock root, MyCubeGrid grid, double radius, double depth, Vector3D direction, ref int maxDbc, ref int AoeHits) //added depth and angle
        {
            //TODO: Factor in maximum depth and angle of impact
            var rootPos = root.Position; //local cube grid

            radius *= grid.GridSizeR;  //GridSizeR is 0.4 for LG
            depth *= grid.GridSizeR;
            
            int maxradius = (int)Math.Floor(radius);  //changed to floor, experiment for precision/rounding bias
            int i, j, k;
            int maxdepth = (int)Math.Ceiling(depth*grid.GridSizeR); //Meters to cube conversion.  Round up or down?
            Vector3I min2 = Vector3I.Max(rootPos - maxradius, grid.Min);
            Vector3I max2 = Vector3I.Min(rootPos + maxradius, grid.Max);


            Log.Line($"AOE: rootpos {rootPos}  blkrad {maxradius}  blkdepth {maxdepth}");

            if (maxdepth < maxradius)
            {
                switch (direction.AbsMaxComponent())//sort out which "face" was hit and coming/going along that axis
                {                   
                    case 0://hit face perp to y
                        if (direction.Y <= 0f)
                        { 
                            min2.Y = rootPos.Y - maxdepth + 1;
                            max2.Y = rootPos.Y + maxdepth - 1;
                        }
                        else
                        { 
                            min2.Y = rootPos.Y + maxdepth - 1;
                            max2.Y = rootPos.Y - maxdepth + 1;
                        }
                        break;

                    case 1://hit face perp to x
                        if (direction.X <= 0f)
                        { 
                            min2.X = rootPos.X - maxdepth + 1;
                            max2.X = rootPos.X + maxdepth - 1;        
                        }
                        else
                        { 
                            min2.X = rootPos.X + maxdepth -1;
                            max2.X = rootPos.X - maxdepth +1;
                        }
                        break;

                    case 2://Hit face is perp to z
                        if (direction.Z <= 0f)
                        {
                            min2.Z = rootPos.Z - maxdepth + 1;
                            max2.Z = rootPos.Z + maxdepth - 1;
                        }
                        else
                        { 
                            min2.Z = rootPos.Z + maxdepth - 1;
                            max2.Z = rootPos.Z - maxdepth + 1;
                        }
                        break;
                }
         
            }


            var damageBlockCache = DamageBlockCache;

            for (i = min2.X; i <= max2.X; ++i)
            {
                for (j = min2.Y; j <= max2.Y; ++j)
                {
                    for (k = min2.Z; k <= max2.Z; ++k)
                    {
                        var vector3I = new Vector3I(i, j, k);
                        MyCube cube;
                        if (grid.TryGetCube(vector3I, out cube))  
                        {
                            var slim = (IMySlimBlock)cube.CubeBlock;
                            int posdist = Vector3I.DistanceManhattan(rootPos, slim.Position);
                            int hitdist = Vector3I.DistanceManhattan(rootPos, vector3I);
                            var slimmin = slim.Min;
                            var slimmax = slim.Max;
                            if (hitdist <= maxradius)
                            {

                                if (slim.IsDestroyed)
                                    continue;
                                var distArray = damageBlockCache[hitdist];

                                if (slimmax != slimmin)//Block larger than 1x1x1
                                {

                                    if (slim == root)//Handle root block> 1x1x1
                                    {
                                        //¯\_(ツ)_/¯
                                        //Log.Line($"Root block>1x1x1  {rootPos}   {vector3I}   hitdist{hitdist}     posdist{posdist}");
                                    }
                                    else//Handle >1x1x1 when not root
                                    {
                                        var hitblkbound = new BoundingBoxI(slimmin, slimmax);
                                        var rootposbound = new BoundingBoxI(rootPos, rootPos);
                                        rootposbound.IntersectWith(ref hitblkbound);
                                        rootposbound.Inflate(1);
                                        if (rootposbound.Contains(vector3I) == ContainmentType.Contains)
                                        {
                                            distArray.Add(slim);
                                            if (hitdist >= maxDbc) maxDbc = hitdist;
                                            AoeHits++;
                                            slim.Dithering = 0.5f;//temp debug to make "hits" go clear, including the root block
                                        }
   
                                    }


                                }
                                else//Happy normal 1x1x1
                                {
                                    distArray.Add(slim);
                                    if (hitdist >= maxDbc) maxDbc = hitdist;
                                    AoeHits++;
                                    slim.Dithering = 0.5f;//temp debug to make "hits" go clear, including the root block
                                }
                            }
                        }
                    }
                }
            }
        }

        public static void GetBlocksInsideSphereFast(MyCubeGrid grid, ref BoundingSphereD sphere, bool checkDestroyed, List<IMySlimBlock> blocks)
        {
            var radius = sphere.Radius;
            radius *= grid.GridSizeR;
            var center = grid.WorldToGridInteger(sphere.Center);
            var gridMin = grid.Min;
            var gridMax = grid.Max;
            double radiusSq = radius * radius;
            int radiusCeil = (int)Math.Ceiling(radius);
            int i, j, k;
            Vector3I max2 = Vector3I.Min(Vector3I.One * radiusCeil, gridMax - center);
            Vector3I min2 = Vector3I.Max(Vector3I.One * -radiusCeil, gridMin - center);
            for (i = min2.X; i <= max2.X; ++i)
            {
                for (j = min2.Y; j <= max2.Y; ++j)
                {
                    for (k = min2.Z; k <= max2.Z; ++k)
                    {
                        if (i * i + j * j + k * k < radiusSq)
                        {
                            MyCube cube;
                            var vector3I = center + new Vector3I(i, j, k);

                            if (grid.TryGetCube(vector3I, out cube))
                            {
                                var slim = (IMySlimBlock)cube.CubeBlock;
                                if (slim.Position == vector3I)
                                {
                                    if (checkDestroyed && slim.IsDestroyed)
                                        continue;

                                    blocks.Add(slim);

                                }
                            }
                        }
                    }
                }
            }
        }

        private void ShiftAndPruneBlockSphere(MyCubeGrid grid, Vector3I center, List<Vector3I> sphereOfCubes, List<RadiatedBlock> slims)
        {
            slims.Clear(); // Ugly but super inlined V3I check
            var gMinX = grid.Min.X;
            var gMinY = grid.Min.Y;
            var gMinZ = grid.Min.Z;
            var gMaxX = grid.Max.X;
            var gMaxY = grid.Max.Y;
            var gMaxZ = grid.Max.Z;

            for (int i = 0; i < sphereOfCubes.Count; i++)
            {
                var v3ICheck = center + sphereOfCubes[i];
                var contained = gMinX <= v3ICheck.X && v3ICheck.X <= gMaxX && (gMinY <= v3ICheck.Y && v3ICheck.Y <= gMaxY) && (gMinZ <= v3ICheck.Z && v3ICheck.Z <= gMaxZ);
                if (!contained) continue;
                MyCube cube;
                if (grid.TryGetCube(v3ICheck, out cube))
                {
                    IMySlimBlock slim = cube.CubeBlock;
                    if (slim.Position == v3ICheck){}
                        slims.Add(new RadiatedBlock { Slim = slim });
                }
            }
        }

        public static void ApplyProjectileForce(MyEntity entity, Vector3D intersectionPosition, Vector3 normalizedDirection, float impulse)
        {
            if (entity.Physics == null || !entity.Physics.Enabled || entity.Physics.IsStatic || entity.Physics.Mass / impulse > 500)
                return;
            entity.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_IMPULSE_AND_WORLD_ANGULAR_IMPULSE, normalizedDirection * impulse, intersectionPosition, Vector3.Zero);
        }

        private bool HitDoor(HitEntity hitEnt, MyDoorBase door)
        {
            var ray = new RayD(ref hitEnt.Intersection.From, ref hitEnt.Intersection.Direction);
            var rayHit = ray.Intersects(door.PositionComp.WorldVolume);
            if (rayHit != null)
            {
                var hitPos = hitEnt.Intersection.From + (hitEnt.Intersection.Direction * (rayHit.Value + 0.25f));
                IHitInfo hitInfo;
                if (Physics.CastRay(hitPos, hitEnt.Intersection.To, out hitInfo, 15))
                {
                    var obb = new MyOrientedBoundingBoxD(door.PositionComp.LocalAABB, door.PositionComp.WorldMatrixRef);

                    var sphere = new BoundingSphereD(hitInfo.Position + (hitEnt.Intersection.Direction * 0.15f), 0.01f);
                    if (obb.Intersects(ref sphere))
                        return true;
                }
            }
            return false;
        }

        private bool RayAccuracyCheck(HitEntity hitEnt, IMySlimBlock block)
        {
            BoundingBoxD box;
            block.GetWorldBoundingBox(out box);
            var ray = new RayD(ref hitEnt.Intersection.From, ref hitEnt.Intersection.Direction);
            var rayHit = ray.Intersects(box);
            if (rayHit != null)
            {
                var hitPos = hitEnt.Intersection.From + (hitEnt.Intersection.Direction * (rayHit.Value - 0.1f));
                IHitInfo hitInfo;
                if (Physics.CastRay(hitPos, hitEnt.Intersection.To, out hitInfo, 15))
                {
                    var hit = (MyEntity)hitInfo.HitEntity;
                    var hitPoint = hitInfo.Position + (hitEnt.Intersection.Direction * 0.1f);
                    var rayHitTarget = box.Contains(hitPoint) != ContainmentType.Disjoint && hit == block.CubeGrid;
                    return rayHitTarget;
                }
            }
            return false;
        }

        public void GetBlockSphereDb(MyCubeGrid grid, double areaRadius, out List<Vector3I> radiatedBlocks)
        {
            areaRadius = Math.Ceiling(areaRadius);

            if (grid.GridSizeEnum == MyCubeSize.Large)
            {
                if (areaRadius < 3) areaRadius = 3;
                LargeBlockSphereDb.TryGetValue(areaRadius, out radiatedBlocks);
            }
            else SmallBlockSphereDb.TryGetValue(areaRadius, out radiatedBlocks);
        }

        private void GenerateBlockSphere(MyCubeSize gridSizeEnum, double radiusInMeters)
        {
            var gridSizeInv = 2.0; // Assume small grid (1 / 0.5)
            if (gridSizeEnum == MyCubeSize.Large)
                gridSizeInv = 0.4; // Large grid (1 / 2.5)

            var radiusInBlocks = radiusInMeters * gridSizeInv;
            var radiusSq = radiusInBlocks * radiusInBlocks;
            var radiusCeil = (int)Math.Ceiling(radiusInBlocks);
            int i, j, k;
            var max = Vector3I.One * radiusCeil;
            var min = Vector3I.One * -radiusCeil;

            var blockSphereLst = _blockSpherePool.Get();
            for (i = min.X; i <= max.X; ++i)
            for (j = min.Y; j <= max.Y; ++j)
            for (k = min.Z; k <= max.Z; ++k)
                if (i * i + j * j + k * k < radiusSq)
                    blockSphereLst.Add(new Vector3I(i, j, k));

            blockSphereLst.Sort((a, b) => Vector3I.Dot(a, a).CompareTo(Vector3I.Dot(b, b)));
            if (gridSizeEnum == MyCubeSize.Large)
                LargeBlockSphereDb.Add(radiusInMeters, blockSphereLst);
            else
                SmallBlockSphereDb.Add(radiusInMeters, blockSphereLst);
        }

        private void DamageGrid(HitEntity hitEnt, ProInfo t)
        {
            try
            {
                var grid = hitEnt.Entity as MyCubeGrid;
                if (grid == null || grid.MarkedForClose || !hitEnt.HitPos.HasValue || hitEnt.Blocks == null)
                {
                    hitEnt.Blocks?.Clear();
                    Log.Line($"DamageGrid first null check hit");
                    return;
                }

                if (t.AmmoDef.DamageScales.Shields.Type == ShieldDef.ShieldType.Heal || (!t.AmmoDef.Const.SelfDamage && !t.AmmoDef.Const.IsCriticalReaction) && t.Ai.AiType == Ai.AiTypes.Grid && t.Ai.GridEntity.IsInSameLogicalGroupAs(grid) || !grid.DestructibleBlocks || grid.Immune || grid.GridGeneralDamageModifier <= 0)
                {
                    t.BaseDamagePool = 0;
                    return;
                }
                var canDamage = t.DoDamage;

                _destroyedSlims.Clear();
                //_destroyedSlimsClient.Clear();
                var largeGrid = grid.GridSizeEnum == MyCubeSize.Large;
                var areaRadius = largeGrid ? t.AmmoDef.Const.AreaRadiusLarge : t.AmmoDef.Const.AreaRadiusSmall;
                var detonateRadius = largeGrid ? t.AmmoDef.Const.DetonateRadiusLarge : t.AmmoDef.Const.DetonateRadiusSmall;
                var maxObjects = t.AmmoDef.Const.MaxObjectsHit;
                var areaEffect = t.AmmoDef.AreaEffect.AreaEffect;
                var explosive = areaEffect == AreaEffectType.Explosive;
                var radiant = areaEffect == AreaEffectType.Radiant;
                var detonateOnEnd = t.AmmoDef.AreaEffect.Detonation.DetonateOnEnd && t.Age >= t.AmmoDef.AreaEffect.Detonation.MinArmingTime;
                var detonateDmg = t.AmmoDef.Const.DetonationDamage;

                var attackerId = t.Target.CoreEntity.EntityId;
                var attacker = t.Target.CoreEntity;

                var areaEffectDmg = areaEffect != AreaEffectType.Disabled ? t.AmmoDef.Const.AreaEffectDamage : 0;
                var hitMass = t.AmmoDef.Mass;
                var sync = MpActive && (DedicatedServer || IsServer);
                var hasAreaDmg = areaEffectDmg > 0;
                var radiantCascade = radiant && !detonateOnEnd;
                var primeDamage = !radiantCascade || !hasAreaDmg;

                var radiantBomb = radiant && detonateOnEnd;
                var damageType = t.ShieldBypassed ? ShieldBypassDamageType : explosive || radiant ? MyDamageType.Explosion : MyDamageType.Bullet;
                var minAoeOffset = largeGrid ? 1.25 : 0.5f;
                var gridMatrix = grid.PositionComp.WorldMatrixRef;

                var directDmgGlobal = Settings.Enforcement.DirectDamageModifer;
                var areaDmgGlobal = Settings.Enforcement.AreaDamageModifer;

                var playerAi = t.Ai.AiType == Ai.AiTypes.Player;
                float gridDamageModifier = grid.GridGeneralDamageModifier;

                var distTraveled = t.AmmoDef.Const.IsBeamWeapon ? hitEnt.HitDist ?? t.DistanceTraveled : t.DistanceTraveled;

                var fallOff = t.AmmoDef.Const.FallOffScaling && distTraveled > t.AmmoDef.Const.FallOffDistance;
                var fallOffMultipler = 1f;
                if (fallOff)
                {
                    fallOffMultipler = (float)MathHelperD.Clamp(1.0 - ((distTraveled - t.AmmoDef.Const.FallOffDistance) / (t.AmmoDef.Const.MaxTrajectory - t.AmmoDef.Const.FallOffDistance)), t.AmmoDef.DamageScales.FallOff.MinMultipler, 1);
                }

                var damagePool = t.BaseDamagePool;
                int hits = 1;
                if (t.AmmoDef.Const.VirtualBeams)
                {
                    hits = t.WeaponCache.Hits;
                    areaEffectDmg *= hits;
                }

                var objectsHit = t.ObjectsHit;
                var countBlocksAsObjects = t.AmmoDef.ObjectsHit.CountBlocks;
                var partialShield = t.ShieldInLine && !t.ShieldBypassed && SApi.MatchEntToShieldFast(grid, true) != null;

                List<Vector3I> radiatedBlocks = null;
                if (radiant) GetBlockSphereDb(grid, areaRadius, out radiatedBlocks);

                var done = false;
                var nova = false;
                var outOfPew = false;
                var earlyExit = false;
                IMySlimBlock rootBlock = null;
                var destroyed = 0;

                for (int i = 0; i < hitEnt.Blocks.Count; i++)
                {

                    if (done || earlyExit || outOfPew && !nova) break;
                    rootBlock = hitEnt.Blocks[i];

                    if (!nova)
                    {
                        if (_destroyedSlims.Contains(rootBlock) || _destroyedSlimsClient.Contains(rootBlock)) continue;
                        if (rootBlock.IsDestroyed)
                        {
                            destroyed++;
                            _destroyedSlims.Add(rootBlock);
                            if (IsClient)
                            {
                                _destroyedSlimsClient.Add(rootBlock);
                                _slimHealthClient.Remove(rootBlock);
                            }
                            continue;
                        }
                    }

                    var fatBlock = rootBlock.FatBlock as MyCubeBlock;
                    var door = fatBlock as MyDoorBase;
                    if (door != null && door.Open && !HitDoor(hitEnt, door) || playerAi && !RayAccuracyCheck(hitEnt, rootBlock))
                        continue;

                    var radiate = radiantCascade || nova;
                    var dmgCount = 1;
                    if (radiate)
                    {
                        if (nova) GetBlockSphereDb(grid, detonateRadius, out radiatedBlocks);
                        if (radiatedBlocks != null) ShiftAndPruneBlockSphere(grid, rootBlock.Position, radiatedBlocks, SlimsSortedList);

                        done = nova;
                        dmgCount = SlimsSortedList.Count;
                    }

                    try
                    {
                        for (int j = 0; j < dmgCount; j++)
                        {
                            var block = radiate ? SlimsSortedList[j].Slim : rootBlock;
                            if (partialShield && SApi.IsBlockProtected(block))
                            {
                                earlyExit = true;
                                break;
                            }

                            var cubeBlockDef = (MyCubeBlockDefinition)block.BlockDefinition;
                            float cachedIntegrity;
                            var blockHp = !IsClient ? block.Integrity - block.AccumulatedDamage : (_slimHealthClient.TryGetValue(block, out cachedIntegrity) ? cachedIntegrity : block.Integrity);
                            var blockDmgModifier = cubeBlockDef.GeneralDamageMultiplier;
                            float damageScale = hits;
                            float directDamageScale = directDmgGlobal;
                            float areaDamageScale = areaDmgGlobal;
                            float detDamageScale = areaDmgGlobal;

                            if (t.AmmoDef.Const.DamageScaling || !MyUtils.IsEqual(blockDmgModifier, 1f) || !MyUtils.IsEqual(gridDamageModifier, 1f))
                            {

                                if (blockDmgModifier < 0.000000001f || gridDamageModifier < 0.000000001f)
                                    blockHp = float.MaxValue;
                                else
                                    blockHp = (blockHp / blockDmgModifier / gridDamageModifier);

                                var d = t.AmmoDef.DamageScales;
                                if (d.MaxIntegrity > 0 && blockHp > d.MaxIntegrity)
                                {
                                    outOfPew = true;
                                    damagePool = 0;
                                    continue;
                                }

                                if (d.Grids.Large >= 0 && largeGrid) damageScale *= d.Grids.Large;
                                else if (d.Grids.Small >= 0 && !largeGrid) damageScale *= d.Grids.Small;

                                MyDefinitionBase blockDef = null;
                                if (t.AmmoDef.Const.ArmorScaling)
                                {
                                    blockDef = block.BlockDefinition;
                                    var isArmor = AllArmorBaseDefinitions.Contains(blockDef) || CustomArmorSubtypes.Contains(blockDef.Id.SubtypeId);
                                    if (isArmor && d.Armor.Armor >= 0) damageScale *= d.Armor.Armor;
                                    else if (!isArmor && d.Armor.NonArmor >= 0) damageScale *= d.Armor.NonArmor;

                                    if (isArmor && (d.Armor.Light >= 0 || d.Armor.Heavy >= 0))
                                    {
                                        var isHeavy = HeavyArmorBaseDefinitions.Contains(blockDef) || CustomHeavyArmorSubtypes.Contains(blockDef.Id.SubtypeId);
                                        if (isHeavy && d.Armor.Heavy >= 0) damageScale *= d.Armor.Heavy;
                                        else if (!isHeavy && d.Armor.Light >= 0) damageScale *= d.Armor.Light;
                                    }
                                }
                                if (t.AmmoDef.Const.CustomDamageScales)
                                {
                                    if (blockDef == null) blockDef = block.BlockDefinition;
                                    float modifier;
                                    var found = t.AmmoDef.Const.CustomBlockDefinitionBasesToScales.TryGetValue(blockDef, out modifier);

                                    if (found) damageScale *= modifier;
                                    else if (t.AmmoDef.DamageScales.Custom.IgnoreAllOthers) continue;
                                }
                                if (GlobalDamageModifed)
                                {
                                    if (blockDef == null) blockDef = block.BlockDefinition;
                                    BlockDamage modifier;
                                    var found = BlockDamageMap.TryGetValue(blockDef, out modifier);

                                    if (found)
                                    {
                                        directDamageScale *= modifier.DirectModifer;
                                        areaDamageScale *= modifier.AreaModifer;
                                        detDamageScale *= modifier.AreaModifer;
                                    }
                                }
                                if (ArmorCoreActive)
                                {
                                    var subtype = block.BlockDefinition.Id.SubtypeId;
                                    if (ArmorCoreBlockMap.ContainsKey(subtype))
                                    {
                                        var resistances = ArmorCoreBlockMap[subtype];

                                        directDamageScale /= t.AmmoDef.Const.EnergyBaseDmg ? resistances.EnergeticResistance : resistances.KineticResistance;

                                        areaDamageScale /= t.AmmoDef.Const.EnergyAreaDmg ? resistances.EnergeticResistance : resistances.KineticResistance;

                                        detDamageScale /= t.AmmoDef.Const.EnergyDetDmg ? resistances.EnergeticResistance : resistances.KineticResistance;
                                    }
                                }

                                if (fallOff)
                                    damageScale *= fallOffMultipler;
                            }

                            var blockIsRoot = block == rootBlock;
                            var primaryDamage = primeDamage || blockIsRoot;

                            if (damagePool <= 0 && primaryDamage || objectsHit >= maxObjects)
                            {
                                outOfPew = true;
                                damagePool = 0;
                                break;
                            }

                            var scaledDamage = damagePool * damageScale * directDamageScale;

                            if (primaryDamage)
                            {
                                if (countBlocksAsObjects) objectsHit++;

                                if (scaledDamage <= blockHp)
                                {
                                    outOfPew = true;
                                    damagePool = 0;
                                }
                                else
                                {
                                    destroyed++;
                                    _destroyedSlims.Add(block);
                                    if (IsClient)
                                    {
                                        _destroyedSlimsClient.Add(block);
                                        if (_slimHealthClient.ContainsKey(block))
                                            _slimHealthClient.Remove(block);
                                    }
                                    damagePool -= (blockHp / (damageScale * directDamageScale));
                                }
                            }
                            else
                            {
                                scaledDamage = (areaEffectDmg * damageScale) * areaDamageScale;
                                if (scaledDamage >= blockHp)
                                {
                                    destroyed++;
                                    _destroyedSlims.Add(block);
                                    if (IsClient)
                                    {
                                        _destroyedSlimsClient.Add(block);
                                        if (_slimHealthClient.ContainsKey(block))
                                            _slimHealthClient.Remove(block);
                                    }
                                }
                            }

                            if (canDamage)
                            {
                                block.DoDamage(scaledDamage, damageType, sync, null, attackerId);
                            }
                            else
                            {
                                var hasBlock = _slimHealthClient.ContainsKey(block);
                                var realDmg = scaledDamage * gridDamageModifier * blockDmgModifier;
                                if (hasBlock && _slimHealthClient[block] - realDmg > 0)
                                    _slimHealthClient[block] -= realDmg;
                                else if (hasBlock)
                                    _slimHealthClient.Remove(block);
                                else if (block.Integrity - realDmg > 0)
                                    _slimHealthClient[block] = blockHp - realDmg;
                            }

                            var theEnd = damagePool <= 0 || objectsHit >= maxObjects;

                            if (explosive && (!detonateOnEnd && blockIsRoot || detonateOnEnd && theEnd))
                            {
                                var travelOffset = hitEnt.Intersection.Length > minAoeOffset ? hitEnt.Intersection.Length : minAoeOffset;
                                var aoeOffset = Math.Min(areaRadius * 0.5f, travelOffset);
                                var expOffsetClamp = MathHelperD.Clamp(aoeOffset, minAoeOffset, 2f);
                                var blastCenter = hitEnt.HitPos.Value + (-hitEnt.Intersection.Direction * expOffsetClamp);
                                if ((areaEffectDmg * areaDamageScale) > 0) SUtils.CreateMissileExplosion(this, (areaEffectDmg * damageScale) * areaDamageScale, areaRadius, blastCenter, hitEnt.Intersection.Direction, attacker, grid, t.AmmoDef, true);
                                if (detonateOnEnd && theEnd)
                                    SUtils.CreateMissileExplosion(this, (detonateDmg * damageScale) * detDamageScale, detonateRadius, blastCenter, hitEnt.Intersection.Direction, attacker, grid, t.AmmoDef, true);
                            }
                            else if (!nova)
                            {
                                if (hitMass > 0 && blockIsRoot)
                                {
                                    var speed = !t.AmmoDef.Const.IsBeamWeapon && t.AmmoDef.Const.DesiredProjectileSpeed > 0 ? t.AmmoDef.Const.DesiredProjectileSpeed : 1;
                                    if (Session.IsServer) ApplyProjectileForce(grid, grid.GridIntegerToWorld(rootBlock.Position), hitEnt.Intersection.Direction, (hitMass * speed));
                                }

                                if (radiantBomb && theEnd)
                                {
                                    nova = true;
                                    i--;
                                    t.BaseDamagePool = 0;
                                    t.ObjectsHit = objectsHit;
                                    if (t.AmmoDef.Const.DetonationDamage > 0) damagePool = (detonateDmg * detDamageScale);
                                    else if (t.AmmoDef.Const.AreaEffectDamage > 0) damagePool = (areaEffectDmg * areaDamageScale);
                                    else damagePool = scaledDamage;
                                    break;
                                }
                            }
                        }

                    }
                    catch (Exception ex) { Log.Line($"Exception in DamageGrid loop catch: {ex}", null, true); }
                }
                try
                {
                    if (rootBlock != null && destroyed > 0)
                    {
                        var fat = rootBlock.FatBlock;
                        MyOrientedBoundingBoxD obb;
                        if (fat != null)
                            obb = new MyOrientedBoundingBoxD(fat.Model.BoundingBox, fat.PositionComp.WorldMatrixRef);
                        else
                        {
                            Vector3 halfExt;
                            rootBlock.ComputeScaledHalfExtents(out halfExt);
                            var blockBox = new BoundingBoxD(-halfExt, halfExt);
                            gridMatrix.Translation = grid.GridIntegerToWorld(rootBlock.Position);
                            obb = new MyOrientedBoundingBoxD(blockBox, gridMatrix);
                        }

                        var dist = obb.Intersects(ref hitEnt.Intersection);
                        if (dist.HasValue)
                        {
                            t.Hit.LastHit = hitEnt.Intersection.From + (hitEnt.Intersection.Direction * dist.Value);
                        }
                    }
                    if (!countBlocksAsObjects) t.ObjectsHit += 1;
                    if (!nova)
                    {
                        t.BaseDamagePool = damagePool;

                        t.ObjectsHit = objectsHit;
                    }
                    if (radiantCascade || nova) SlimsSortedList.Clear();
                    hitEnt.Blocks.Clear();
                }
                catch (Exception ex) { Log.Line($"Exception in DamageGrid finish catch: {ex}", null, true); }
            }
            catch (Exception ex) { Log.Line($"Exception in DamageGrid main catch: {ex}", null, true); }
        }

    }
}
