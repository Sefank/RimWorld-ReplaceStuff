﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Reflection.Emit;
using Verse;
using RimWorld;
using UnityEngine;
using Harmony;


namespace Replace_Stuff
{
	class ReplaceFrame : Frame, IConstructible
	{
		public Thing oldThing;
		public ThingDef oldStuff;

		private const int MaxDeconstructWork = 3000;
		public float WorkToDeconstruct
		{
			get
			{
				float deWork = def.entityDefToBuild.GetStatValueAbstract(StatDefOf.WorkToBuild, oldStuff);
				return Mathf.Min(deWork, MaxDeconstructWork);
			}
		}
		public float WorkToReplace
		{
			get
			{
				return def.entityDefToBuild.GetStatValueAbstract(StatDefOf.WorkToBuild, Stuff);
			}
		}
		public new float WorkToMake
		{
			get
			{
				return WorkToDeconstruct + WorkToReplace;
			}
		}

		public new float WorkLeft
		{
			get
			{
				return this.WorkToMake - this.workDone;
			}
		}

		public new float PercentComplete
		{
			get
			{
				return this.workDone / this.WorkToMake;
			}
		}

		public override string Label
		{
			get
			{
				string text = this.def.entityDefToBuild.label + "TD.ReplacingTag".Translate();
				if (base.Stuff != null)
				{
					return base.Stuff.label + " " + text;
				}
				return text;
			}
		}

		public int TotalStuffNeeded()
		{
			return TotalStuffNeeded(def.entityDefToBuild, Stuff);
		}
		public static int TotalStuffNeeded(BuildableDef toBuild, ThingDef stuff)
		{
			int count = Mathf.RoundToInt((float)toBuild.costStuffCount / stuff.VolumePerUnit);
			if (count < 1) count = 1;
			return count;
		}
		public int CountStuffHas()
		{
			return resourceContainer.TotalStackCountOfDef(Stuff);
		}
		public int CountStuffNeeded()
		{
			return TotalStuffNeeded() - CountStuffHas();
		}

		private List<ThingCountClass> cachedMaterialsNeeded = new List<ThingCountClass>();
		public new List<ThingCountClass> MaterialsNeeded()
		{
			this.cachedMaterialsNeeded.Clear();
			
			int need = CountStuffNeeded();

			if (need > 0)
				this.cachedMaterialsNeeded.Add(new ThingCountClass(Stuff, need));

			return this.cachedMaterialsNeeded;
		}

		public override void SpawnSetup(Map map, bool respawningAfterLoad)
		{
			base.SpawnSetup(map, respawningAfterLoad);
		}

		public new void CompleteConstruction(Pawn worker)
		{
			if (oldThing != null && oldThing.Spawned)
			{
				FinalizeReplace(oldThing, Stuff, worker);

				this.resourceContainer.ClearAndDestroyContents(DestroyMode.Vanish);
				this.Destroy(DestroyMode.Vanish);

				worker.records.Increment(RecordDefOf.ThingsConstructed);
				worker.records.Increment(RecordDefOf.ThingsDeconstructed);
			}
			else
			{
				this.resourceContainer.TryDropAll(Position, Map, ThingPlaceMode.Near);
				this.Destroy(DestroyMode.Cancel);
			}
		}

		public new void FailConstruction(Pawn worker)
		{
			workDone = Mathf.Min(workDone, WorkToDeconstruct);
			MoteMaker.ThrowText(this.DrawPos, Map, "TextMote_ConstructionFail".Translate());
			if (base.Faction == Faction.OfPlayer && this.WorkToReplace > 1400f)
			{
				Messages.Message("MessageConstructionFailed".Translate(new object[]
				{
					this.Label,
					worker.LabelShort
				}), new TargetInfo(base.Position, Map), MessageTypeDefOf.NegativeEvent);
			}
		}

		public static void DeconstructDropStuff(Thing oldThing)
		{
			if (Current.ProgramState != ProgramState.Playing)	return;

			ThingDef oldDef = oldThing.def;
			ThingDef stuffDef = oldThing.Stuff;
			
			if (GenLeaving.CanBuildingLeaveResources(oldThing, DestroyMode.Deconstruct))
			{
				int count = TotalStuffNeeded(oldDef, stuffDef);
				int leaveCount = GenLeaving.GetBuildingResourcesLeaveCalculator(oldThing, DestroyMode.Deconstruct)(count);
				if (leaveCount > 0)
				{
					Thing leftThing = ThingMaker.MakeThing(stuffDef);
					leftThing.stackCount = leaveCount;
					GenDrop.TryDropSpawn(leftThing, oldThing.Position, oldThing.Map, ThingPlaceMode.Near, out Thing dummyThing);
				}
			}
		}

		public static void FinalizeReplace(Thing thing, ThingDef stuff, Pawn worker = null)
		{
			DeconstructDropStuff(thing);
			
			thing.SetStuffDirect(stuff);
			thing.HitPoints = thing.MaxHitPoints;	//Deconstruction/construction implicitly repairs
			thing.Notify_ColorChanged();
			thing.Map.mapDrawer.SectionAt(thing.Position).RegenerateLayers(MapMeshFlag.Things);

			if (worker != null && thing.TryGetComp<CompQuality>() is CompQuality compQuality)
			{
				int level = worker.skills.GetSkill(SkillDefOf.Construction).Level;
				compQuality.SetQuality(QualityUtility.RandomCreationQuality(level), ArtGenerationContext.Colony);
			}
		}

		public override string GetInspectString()
		{
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.AppendLine("ContainedResources".Translate() + ":");
			stringBuilder.AppendLine(string.Concat(new object[]
			{
				Stuff.LabelCap,
				": ",
				CountStuffHas(),
				" / ",
				TotalStuffNeeded()
			}));
			stringBuilder.Append("WorkLeft".Translate() + ": " + this.WorkLeft.ToStringWorkAmount());
			return stringBuilder.ToString();
		}
	}


	//VIRTUAL virtual methods
	[HarmonyPatch(typeof(Frame), "MaterialsNeeded")]
	public static class Virtualize_MaterialsNeeded
	{
		//public List<ThingCountClass> MaterialsNeeded()
		public static bool Prefix(Frame __instance, ref List<ThingCountClass> __result)
		{
			if (__instance is ReplaceFrame replaceFrame)
			{
				__result = replaceFrame.MaterialsNeeded();
				return false;
			}
			return true;
		}
	}
	[HarmonyPatch(typeof(Frame), "CompleteConstruction")]
	public static class Virtualize_CompleteConstruction
	{
		//public void CompleteConstruction(Pawn worker)
		public static bool Prefix(Frame __instance, Pawn worker)
		{
			if (__instance is ReplaceFrame replaceFrame)
			{
				replaceFrame.CompleteConstruction(worker);
				return false;
			}
			return true;
		}
	}
	[HarmonyPatch(typeof(Frame), "get_WorkToMake")]
	public static class Virtualize_WorkToMake
	{
		//public float WorkToMake
		public static bool Prefix(Frame __instance, ref float __result)
		{
			if (__instance is ReplaceFrame replaceFrame)
			{
				__result = replaceFrame.WorkToMake;
				return false;
			}
			return true;
		}
	}
	
}
