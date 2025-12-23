using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace RuthlessPursuingMechanoids
{
    [StaticConstructorOnStartup]
    public static class HarmonyPatcher
    {
        static HarmonyPatcher()
        {
            new Harmony("matathias.ruthlessmechanoids").PatchAll(Assembly.GetExecutingAssembly());
        }
    }

    [HarmonyPatch(typeof(IncidentWorker_RaidEnemy), "FactionCanBeGroupSource")]
    internal class IncidentWorker_RaidEnemy_FactionCanBeGroupSource
    {
        private static void Postfix(Faction f, ref bool __result)
        {
            if (__result)
            {
                /* Look through ScenParts to see if a Ruthless Pursuit faction can be used for normal raids */
                foreach (ScenPart_RuthlessPursuingMechanoids part in Find.Scenario.AllParts.OfType<ScenPart_RuthlessPursuingMechanoids>())
                {
                    if (part.PursuitFaction == f)
                    {
                        if (!part.FactionCanNormalRaid)
                        {
                            __result = false;
                        }
                        break;
                    }
                }
            }
        }
    }
    [HarmonyPatch(typeof(Scenario), "PostWorldGenerate")]
    internal class Scenario_PostWorldGenerate_RuthlessOmniPursuitPatch
    {
        private bool Prefix(ref bool __result)
        {
            ScenPart_RuthlessOmniPursuit omniPursuit = Find.Scenario.AllParts.OfType<ScenPart_RuthlessOmniPursuit>().First();
            if (omniPursuit != null)
            {
                foreach(Faction fac in Find.FactionManager.GetFactions(false, true, true))
                {
                    if (fac.def.displayInFactionSelection && !fac.def.isPlayer && fac.def.canStageAttacks)
                    {
                        ScenPart_RuthlessPursuingMechanoids newPursuit = omniPursuit.GeneratePursuitScenPart(fac.def, fac.Name);
                        Find.Scenario.AllParts.AddItem(newPursuit);
                    }
                }
            }
            return __result;
        }
    }
}
