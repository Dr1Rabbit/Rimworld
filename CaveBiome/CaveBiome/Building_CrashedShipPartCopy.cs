﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Verse;
using Verse.Noise;
using RimWorld;
using RimWorld.Planet;
using Verse.AI;
using Verse.AI.Group;
using Verse.Sound;
using UnityEngine;

namespace CaveBiome
{
    // TODO: remove it! Just a copy of the internal corresponding class...
    public class Building_CrashedShipPartCopy : Building
    {
        private const float MechanoidsDefendRadius = 21f;
        private const int SnowExpandInterval = 500;
        private const float SnowAddAmount = 0.12f;
        private const float SnowMaxRadius = 55f;
        public float pointsLeft;
        protected int age;
        private Lord lord;
        private float snowRadius;
        private ModuleBase snowNoise;
        private int ticksToPlantHarm;
        private static HashSet<IntVec3> reachableCells = new HashSet<IntVec3>();
        protected virtual float PlantHarmRange
        {
            get
            {
                return 0f;
            }
        }
        protected virtual int PlantHarmInterval
        {
            get
            {
                return 4;
            }
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.LookValue<float>(ref this.pointsLeft, "mechanoidPointsLeft", 0f, false);
            Scribe_Values.LookValue<int>(ref this.age, "age", 0, false);
            Scribe_Values.LookValue<float>(ref this.snowRadius, "snowRadius", 0f, false);
            Scribe_References.LookReference<Lord>(ref this.lord, "defenseLord", false);
        }
        public override string GetInspectString()
        {
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.AppendLine(base.GetInspectString());
            stringBuilder.AppendLine("AwokeDaysAgo".Translate(new object[]
			{
				this.age.TicksToDays().ToString("F1")
			}));
            return stringBuilder.ToString();
        }
        public override void Tick()
        {
            base.Tick();
            this.age++;
            this.ticksToPlantHarm--;
            if (this.ticksToPlantHarm <= 0)
            {
                this.HarmPlant();
            }
            if ((Find.TickManager.TicksGame + this.HashOffset()) % 500 == 0)
            {
                this.ExpandSnow();
            }
        }
        public override void PreApplyDamage(DamageInfo dinfo, out bool absorbed)
        {
            base.PreApplyDamage(dinfo, out absorbed);
            if (absorbed)
            {
                return;
            }
            if (dinfo.Def.harmsHealth)
            {
                float num = (float)(this.HitPoints - dinfo.Amount);
                if ((num < (float)base.MaxHitPoints * 0.98f && dinfo.Instigator != null && dinfo.Instigator.Faction != null) || num < (float)base.MaxHitPoints * 0.9f)
                {
                    this.TrySpawnMechanoids();
                }
            }
            absorbed = false;
        }
        private void TrySpawnMechanoids()
        {
            if (this.pointsLeft <= 0f)
            {
                return;
            }
            if (this.lord == null)
            {
                IntVec3 invalid;
                if (!CellFinder.TryFindRandomCellNear(base.Position, 5, (IntVec3 c) => c.Standable() && c.CanReach(this, PathEndMode.Touch, TraverseParms.For(TraverseMode.PassDoors, Danger.Deadly, false)), out invalid))
                {
                    Log.Error("Found no place for mechanoids to defend " + this);
                    invalid = IntVec3.Invalid;
                }
                LordJob_MechanoidsDefendShip lordJob = new LordJob_MechanoidsDefendShip(this, base.Faction, 21f, invalid);
                this.lord = LordMaker.MakeNewLord(Faction.OfMechanoids, lordJob, null);
            }
            PawnKindDef kindDef;
            while ((
                from def in DefDatabase<PawnKindDef>.AllDefs
                where def.RaceProps.IsMechanoid && def.isFighter && def.combatPower <= this.pointsLeft
                select def).TryRandomElement(out kindDef))
            {
                IntVec3 center;
                if ((
                    from cell in GenAdj.CellsAdjacent8Way(this)
                    where this.CanSpawnMechanoidAt(cell)
                    select cell).TryRandomElement(out center))
                {
                    Pawn pawn = PawnGenerator.GeneratePawn(kindDef, Faction.OfMechanoids);
                    if (GenPlace.TryPlaceThing(pawn, center, ThingPlaceMode.Near, null))
                    {
                        this.lord.AddPawn(pawn);
                        this.pointsLeft -= pawn.kindDef.combatPower;
                        continue;
                    }
                    Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.Discard);
                }
                this.pointsLeft = 0f;
                SoundDefOf.PsychicPulseGlobal.PlayOneShotOnCamera();
                return;
            }
            this.pointsLeft = 0f;
            SoundDefOf.PsychicPulseGlobal.PlayOneShotOnCamera();
            return;
        }
        private bool CanSpawnMechanoidAt(IntVec3 c)
        {
            return c.Walkable();
        }
        private void TrySwitchMechanoidsToAssaultMode()
        {
            if (this.lord.CurLordToil is LordToil_DefendPoint)
            {
                this.lord.ReceiveMemo("AssaultColony");
            }
        }
        private void ExpandSnow()
        {
            if (this.snowNoise == null)
            {
                this.snowNoise = new Perlin(0.054999999701976776, 2.0, 0.5, 5, Rand.Range(0, 651431), QualityMode.Medium);
            }
            if (this.snowRadius < 8f)
            {
                this.snowRadius += 1.3f;
            }
            else
            {
                if (this.snowRadius < 17f)
                {
                    this.snowRadius += 0.7f;
                }
                else
                {
                    if (this.snowRadius < 30f)
                    {
                        this.snowRadius += 0.4f;
                    }
                    else
                    {
                        this.snowRadius += 0.1f;
                    }
                }
            }
            if (this.snowRadius > 55f)
            {
                this.snowRadius = 55f;
            }
            CellRect occupiedRect = this.OccupiedRect();
            Building_CrashedShipPartCopy.reachableCells.Clear();
            FloodFiller.FloodFill(base.Position, (IntVec3 x) => x.DistanceToSquared(this.Position) <= this.snowRadius * this.snowRadius && (occupiedRect.Contains(x) || !x.Filled()), delegate(IntVec3 x)
            {
                Building_CrashedShipPartCopy.reachableCells.Add(x);
            });
            int num = GenRadial.NumCellsInRadius(this.snowRadius);
            for (int i = 0; i < num; i++)
            {
                IntVec3 intVec = base.Position + GenRadial.RadialPattern[i];
                if (intVec.InBounds())
                {
                    if (Building_CrashedShipPartCopy.reachableCells.Contains(intVec))
                    {
                        float num2 = this.snowNoise.GetValue(intVec);
                        num2 += 1f;
                        num2 *= 0.5f;
                        if (num2 < 0.1f)
                        {
                            num2 = 0.1f;
                        }
                        if (Find.SnowGrid.GetDepth(intVec) <= num2)
                        {
                            float lengthHorizontal = (intVec - base.Position).LengthHorizontal;
                            float num3 = 1f - lengthHorizontal / this.snowRadius;
                            Find.SnowGrid.AddDepth(intVec, num3 * 0.12f * num2);
                        }
                    }
                }
            }
        }
        private void HarmPlant()
        {
            if (this.PlantHarmRange < 0.0001f)
            {
                return;
            }
            float angle = Rand.Range(0f, 360f);
            float num = Rand.Range(0f, this.PlantHarmRange);
            num = Mathf.Sqrt(num / this.PlantHarmRange) * this.PlantHarmRange;
            Quaternion rotation = Quaternion.AngleAxis(angle, Vector3.up);
            Vector3 point = Vector3.forward * num;
            Vector3 v = rotation * point;
            IntVec3 b = IntVec3.FromVector3(v);
            IntVec3 c = base.Position + b;
            if (c.InBounds())
            {
                Plant plant = c.GetPlant();
                if (plant != null)
                {
                    if (Rand.Value < 0.2f)
                    {
                        plant.Destroy(DestroyMode.Kill);
                    }
                    else
                    {
                        plant.MakeLeafless();
                    }
                }
            }
            this.ticksToPlantHarm = this.PlantHarmInterval;
        }
    }
}
