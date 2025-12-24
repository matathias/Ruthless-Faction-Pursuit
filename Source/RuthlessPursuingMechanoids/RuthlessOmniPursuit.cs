using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace RuthlessPursuingMechanoids
{
    /* This scenpart forces the Ruthless Pursuit scenpart on all factions in the game.
     * Well, all "normal" factions, at least.
     */
    public class ScenPart_RuthlessOmniPursuit : ScenPart
    {
        private List<ScenPart_RuthlessPursuingMechanoids> pursuitParts = new List<ScenPart_RuthlessPursuingMechanoids>();
        private bool warningDisabled = false;
        private bool disableEndlessWaves = false;
        private bool canDoNormalRaid = false;
        private bool startHostile = true;

        /*-*-*-*-*- OPTIONS VALUES -*-*-*-*-*/
        /* These fields are the default values for each field */
        /* This might be over-commenting, but the reason why each field has a modifiable version and a constant "def" version is to make sure
         * that the fields are initialized to the same default value as what we use when Saving/Loading. Better to have one place where we edit
         * the numbers, rather than two, which would risk them falling out of sync. */
        private const int FirstRaidDelayHoursDef = 636; // 26.5 days
        private const int FirstRaidDelayVarianceHoursDef = 204; // +/-8.5 days
        private const int RaidDelayHoursDef = 636; // 26.5 days
        private const int RaidDelayVarianceHoursDef = 204; // +/-8.5 days
        private const int WarningDelayHoursDef = 276; // 11.5 days before the mean Raid Delay
        private const int WarningDelayVarianceHoursDef = 24;  // +/-1 day
        private const int SecondWaveHoursDef = 12;
        private const int EndlessWavesHoursDef = 3;
        /* These fields are the ones edited by the scenario part UI. They influence the actual values used in calculations. */
        /* - - FIRST RAID DELAY - - */
        /* Having a different value for the first raid allows the scenario part to be setup like the vanilla Pursuing Mechanoids, if desired.
         * We'll default to it being the same as the regular delay though. */
        private int FirstRaidDelayHours = FirstRaidDelayHoursDef;
        private string frdhbuf;
        private int FirstRaidDelayVarianceHours = FirstRaidDelayVarianceHoursDef;
        private string frdvhbuf;
        /* - - RAID DELAY - - */
        /* Default settings of 636 and 408 hours produce the 18-35 day window of vanilla. */
        private int RaidDelayHours = RaidDelayHoursDef;
        private string rdhbuf;
        private int RaidDelayVarianceHours = RaidDelayVarianceHoursDef;
        private string rdvhbuf;
        /* - - WARNING DELAY - - */
        /* End warning delay is calculated based on the Raid Delay. Negative warning delay disables the warning altogether. You know, just in
         * case someone wants to play real hardcore. */
        private int WarningDelayHours = WarningDelayHoursDef;
        private string wdhbuf;
        private int WarningDelayVarianceHours = WarningDelayVarianceHoursDef;
        private string wdvhbuf;
        /* - - SECOND WAVE ARRIVAL TIME - - */
        private int SecondWaveHours = SecondWaveHoursDef;
        private string swhbuf;
        /* - - ENDLESS WAVES PERIOD - - */
        private int EndlessWavesHours = EndlessWavesHoursDef;
        private string ewhbuf;
        /* - - FACTION SETTINGS - - */
        internal bool PursuitFactionPermanentEnemy = true;
        /*-*-*-*-*- END OPTIONS VALUES -*-*-*-*-*/
        private List<Alert_PursuitFactionThreat> AlertCache = new List<Alert_PursuitFactionThreat>();

        public override void ExposeData()
        {
            base.ExposeData();
            /* We only use all of the above fields to initialize the various sub-ScenParts. There's no need to save anything in the Omni ScenPart aside
             * from said sub-ScenParts. */
            DebugUtility.DebugLog($"--*-*-* Ruthless Omni Pursuit {Scribe.mode} START *-*-*--");
            Scribe_Collections.Look(ref pursuitParts, "omniPursuitParts", LookMode.Deep);
            DebugUtility.DebugLog($"--*-*-* Ruthless Omni Pursuit {Scribe.mode} END *-*-*--");
        }
        public override void DoEditInterface(Listing_ScenEdit listing)
        {
            float totalBaseHeight = 14f;
            Rect scenPartRect = listing.GetScenPartRect(this, ScenPart.RowHeight * totalBaseHeight);
            float rowHeight = scenPartRect.height / totalBaseHeight;
            new Rect(scenPartRect.x, scenPartRect.y, scenPartRect.width, rowHeight);
            Rect rect1 = new Rect(scenPartRect.x, scenPartRect.y, scenPartRect.width, rowHeight);
            Rect rect2 = new Rect(scenPartRect.x, scenPartRect.y + rowHeight, scenPartRect.width, rowHeight);
            Rect rect3 = new Rect(scenPartRect.x, scenPartRect.y + rowHeight * 2f, scenPartRect.width, rowHeight);
            Rect rect4 = new Rect(scenPartRect.x, scenPartRect.y + rowHeight * 3f, scenPartRect.width, rowHeight);
            Rect rect5 = new Rect(scenPartRect.x, scenPartRect.y + rowHeight * 4f, scenPartRect.width, rowHeight);
            Rect rect6 = new Rect(scenPartRect.x, scenPartRect.y + rowHeight * 5f, scenPartRect.width, rowHeight);
            Rect rect7 = new Rect(scenPartRect.x, scenPartRect.y + rowHeight * 6f, scenPartRect.width, rowHeight);
            Rect rect8 = new Rect(scenPartRect.x, scenPartRect.y + rowHeight * 7f, scenPartRect.width, rowHeight);
            Rect rect9 = new Rect(scenPartRect.x, scenPartRect.y + rowHeight * 8f, scenPartRect.width, rowHeight);
            Rect rect10 = new Rect(scenPartRect.x, scenPartRect.y + rowHeight * 9f, scenPartRect.width, rowHeight);
            Rect rect11 = new Rect(scenPartRect.x, scenPartRect.y + rowHeight * 10f, scenPartRect.width, rowHeight);
            Rect rect12 = new Rect(scenPartRect.x, scenPartRect.y + rowHeight * 11f, scenPartRect.width, rowHeight);
            Rect rect13 = new Rect(scenPartRect.x, scenPartRect.y + rowHeight * 12f, scenPartRect.width, rowHeight);
            Rect rect14 = new Rect(scenPartRect.x, scenPartRect.y + rowHeight * 13f, scenPartRect.width, rowHeight);
            Rect rect15 = new Rect(scenPartRect.x, scenPartRect.y + rowHeight * 14f, scenPartRect.width, rowHeight);
            Rect rect16 = new Rect(scenPartRect.x, scenPartRect.y + rowHeight * 15f, scenPartRect.width, rowHeight);
            /* Permanent enemy checkbox */
            Widgets.CheckboxLabeled(rect1, "rpmPermanentEnemies".Translate(), ref PursuitFactionPermanentEnemy);
            if (!PursuitFactionPermanentEnemy)
            {
                Widgets.CheckboxLabeled(rect2, "rpmStartHostiles".Translate(), ref startHostile);
            }

            /* -- Fields for setting the timers -- */
            // 14400 hours is ten rimyears. Should be plenty for a maximum bound
            // The minimum value for all raid delays is 1 hour. If it were 0, then we could hit some infinite loop shit
            //   1 hour is pretty fucking low, probably unplayably low in most cases. But if the user really wants to fuck themselves, then why not let them?
            Widgets.TextArea(rect3, "rpmTimeInHours".Translate(), true);
            Widgets.TextFieldNumericLabeled(rect4, "rpmFirstRaidMeanDelay".Translate(), ref FirstRaidDelayHours, ref frdhbuf, 1, 14400);
            Widgets.TextFieldNumericLabeled(rect5, "rpmFirstRaidMeanDelayVariance".Translate(), ref FirstRaidDelayVarianceHours, ref frdvhbuf, 0, 14400);
            Widgets.TextFieldNumericLabeled(rect6, "rpmRaidMeanDelay".Translate(), ref RaidDelayHours, ref rdhbuf, 1, 14400);
            Widgets.TextFieldNumericLabeled(rect7, "rpmRaidMeanDelayVariance".Translate(), ref RaidDelayVarianceHours, ref rdvhbuf, 0, 14400);
            Widgets.CheckboxLabeled(rect8, "rpmWarningDisabled".Translate(), ref warningDisabled);
            Widgets.TextFieldNumericLabeled(rect9, "rpmWarningMeanDelay".Translate(), ref WarningDelayHours, ref wdhbuf, 0, 14400);
            Widgets.TextFieldNumericLabeled(rect10, "rpmWarningMeanDelayVariance".Translate(), ref WarningDelayVarianceHours, ref wdvhbuf, 0, 14400);
            Widgets.TextFieldNumericLabeled(rect11, "rpmSecondWaveHours".Translate(), ref SecondWaveHours, ref swhbuf, 1, 14400);
            Widgets.CheckboxLabeled(rect12, "rpmDisableEndless".Translate(), ref disableEndlessWaves);
            Widgets.TextFieldNumericLabeled(rect13, "rpmEndlessWaveHours".Translate(), ref EndlessWavesHours, ref ewhbuf, 1, 14400);
            Widgets.CheckboxLabeled(rect14, "rpmNormalRaid".Translate(), ref canDoNormalRaid);
        }
        private void GeneratePursuitScenPart(FactionDef inFaction, string facName)
        {
            ScenPart_RuthlessPursuingMechanoids newPursuit = new ScenPart_RuthlessPursuingMechanoids(inFaction, facName, PursuitFactionPermanentEnemy, startHostile,
                                                                                                     FirstRaidDelayHours, FirstRaidDelayVarianceHours, RaidDelayHours,
                                                                                                     RaidDelayVarianceHours, warningDisabled, WarningDelayHours, WarningDelayVarianceHours,
                                                                                                     SecondWaveHours, disableEndlessWaves, EndlessWavesHours, canDoNormalRaid);
            pursuitParts.Add(newPursuit);
            DebugUtility.DebugLog($"added new pursuit for faction {facName}");
        }

        public override bool CanCoexistWith(ScenPart other)
        {
            if (other is ScenPart_RuthlessPursuingMechanoids)
            {
                return false;
            }

            return true;
        }
        public override void PostWorldGenerate()
        {
            pursuitParts.Clear();
            DebugUtility.DebugLog("Ruthless Omni Pursuit post-world-gen scenpart initialization step 1...");
            foreach (Faction fac in Find.FactionManager.GetFactions(true, true, true))
            {
                if (fac.def.displayInFactionSelection && !fac.def.isPlayer && fac.def.canStageAttacks)
                {
                    GeneratePursuitScenPart(fac.def, fac.Name);
                }
            }
            DebugUtility.DebugLog("Ruthless Omni Pursuit post-world-gen scenpart initialization step 2...");
            foreach (ScenPart_RuthlessPursuingMechanoids part in pursuitParts)
            {
                part.PostWorldGenerate();
            }
            DebugUtility.DebugLog("Ruthless Omni Pursuit post-world-gen scenpart initialization complete.");
        }

        public override void PostMapGenerate(Map map)
        {
            foreach (ScenPart_RuthlessPursuingMechanoids part in pursuitParts)
            {
                part.PostMapGenerate(map);
            }
        }

        public override void MapRemoved(Map map)
        {
            foreach (ScenPart_RuthlessPursuingMechanoids part in pursuitParts)
            {
                part.MapRemoved(map);
            }
        }

        public override void Tick()
        {
            foreach (ScenPart_RuthlessPursuingMechanoids part in pursuitParts)
            {
                part.Tick();
            }

            /* I feel like there's gotta be a better way to handle the alerts than just rawdogging it like this... */
            if (Time.frameCount % 20 == 0)
            {
                UpdateAlertCache();
            }
        }
        public override IEnumerable<Alert> GetAlerts()
        {
            return AlertCache;
        }

        private void UpdateAlertCache()
        {
            AlertCache.Clear();
            foreach (ScenPart_RuthlessPursuingMechanoids part in pursuitParts)
            {
                foreach (Alert_PursuitFactionThreat a in part.GetAlerts())
                {
                    AlertCache.Add(a);
                }
            }
        }
    }
}
