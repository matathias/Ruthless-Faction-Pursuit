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
    /* This is its own class, but it largely duplicates the functionality of ScenPart_PursuingMechanoids. Here are the major differences:
     *   1) The gravship check is removed, and all of the special "starting map" checks that were meant for the orbital platform start have also been removed
     *   2) Instead of calling a Notify_QuestCompleted() function from elsewhere, we periodically check if the Mechanoid faction still exists, or
     *      if it's been deactivated, and set "Disabled" accordingly.
     *   3) Instead of only sending two waves of mechanoids after the player, we now send endless waves.
     */
    /* #-#-# RUTHLESS 2.0 #-#-# */
    /* I'm expanding the scope of this mod to allow the player to set *any* faction to endlessly pursue them. But for the sake of backwards compatibility with
     * existing saves, I'm keeping the "RuthlessPursuingMechanoids" naming scheme.
     * Hopefully future me won't regret this.
     */
    public class ScenPart_RuthlessPursuingMechanoids : ScenPart
    {
        private bool DebugLoggingEnabled = true;
        private Dictionary<Map, int> mapWarningTimers = new Dictionary<Map, int>();

        private Dictionary<Map, int> mapRaidTimers = new Dictionary<Map, int>();
        internal FactionDef pursuitFactionDef = FactionDefOf.Mechanoid;

        /* 14 to 16 days */
        private IntRange WarningDelayRange = new IntRange(14 * 24 * GenDate.TicksPerHour, 16 * 24 * GenDate.TicksPerHour);

        /* 18 to 35 days */
        private IntRange RaidDelayRange = new IntRange(18 * 24 * GenDate.TicksPerHour, 35 * 24 * GenDate.TicksPerHour);

        private IntRange FirstRaidDelayRange = new IntRange(18 * 24 * GenDate.TicksPerHour, 35 * 24 * GenDate.TicksPerHour);

        /* As of Rimworld 1.6, GenDate.TicksPerHour = 2500 */
        private const int TickInterval = GenDate.TicksPerHour;

        private bool isFirstPeriod = false;
        private bool warningDisabled = false;
        private bool disableEndlessWaves = false;
        private bool canDoNormalRaid = false;

        public bool FactionCanNormalRaid => canDoNormalRaid;

        private int SecondRaidDelay => SecondWaveHours * GenDate.TicksPerHour;

        private int EndlessRaidInterval => EndlessWavesHours * GenDate.TicksPerHour;

        /*-*-*-*-*- OPTIONS VALUES -*-*-*-*-*/
        /* These fields are the default values for each field */
        /* This might be over-commenting, but the reason why each field has a modifiable version and a constant "def" version is to make sure
         * that the fields are initialized to the same default value as what we use when Saving/Loading. Better to have one place where we edit
         * the numbers, rather than two, which would risk them falling out of sync. */
        private const int FirstRaidDelayHoursDef         = 636; // 26.5 days
        private const int FirstRaidDelayVarianceHoursDef = 204; // +/-8.5 days
        private const int RaidDelayHoursDef              = 636; // 26.5 days
        private const int RaidDelayVarianceHoursDef      = 204; // +/-8.5 days
        private const int WarningDelayHoursDef           = 276; // 11.5 days before the mean Raid Delay
        private const int WarningDelayVarianceHoursDef   = 24;  // +/-1 day
        private const int SecondWaveHoursDef             = 12;
        private const int EndlessWavesHoursDef           = 3;
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
        internal Faction PursuitFaction;
        internal PawnsArrivalModeDef PursuitRaidType = PawnsArrivalModeDefOf.RandomDrop;
        internal bool PursuitFactionPermanentEnemy = true;
        /*-*-*-*-*- END OPTIONS VALUES -*-*-*-*-*/

        private Map cachedAlertMap;

        private Alert_PursuitFactionThreat alertCached;

        private List<Map> tmpWarningKeys;

        private List<int> tmpWarningValues;

        private List<Map> tmpRaidKeys;

        private List<int> tmpRaidValues;

        private List<Map> tmpMaps = new List<Map>();

        private Alert_PursuitFactionThreat AlertCached
        {
            get
            {
                if (Disabled || warningDisabled)
                {
                    return null;
                }
                if (cachedAlertMap != Find.CurrentMap)
                {
                    alertCached = null;
                }
                if (alertCached != null)
                {
                    return alertCached;
                }
                if (mapWarningTimers.TryGetValue(Find.CurrentMap, out var value) && Find.TickManager.TicksGame > TimerIntervalTick(value))
                {
                    alertCached = new Alert_PursuitFactionThreat
                    {
                        raidTick = mapRaidTimers[Find.CurrentMap],
                        factionName = PursuitFaction.NameColored
                    };
                    cachedAlertMap = Find.CurrentMap;
                }
                return alertCached;
            }
        }

        private bool Disabled = false;

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                foreach (Map item in mapWarningTimers.Keys.ToList())
                {
                    if (item?.Parent == null || item.Parent.Destroyed)
                    {
                        mapWarningTimers.Remove(item);
                        mapRaidTimers.Remove(item);
                    }
                }
            }
            Scribe_Collections.Look(ref mapWarningTimers, "mapWarningTimers", LookMode.Reference, LookMode.Value, ref tmpWarningKeys, ref tmpWarningValues);
            Scribe_Collections.Look(ref mapRaidTimers, "mapRaidTimers", LookMode.Reference, LookMode.Value, ref tmpRaidKeys, ref tmpRaidValues);
            Scribe_Values.Look(ref Disabled, "disabled", defaultValue: false);
            Scribe_Values.Look(ref isFirstPeriod, "isFirstPeriod", defaultValue: false);
            Scribe_Values.Look(ref FirstRaidDelayHours, "firstRaidDelayHours", FirstRaidDelayHoursDef);
            Scribe_Values.Look(ref FirstRaidDelayVarianceHours, "firstRaidDelayVarianceHours", FirstRaidDelayVarianceHoursDef);
            Scribe_Values.Look(ref RaidDelayHours, "raidDelayHours", RaidDelayHoursDef);
            Scribe_Values.Look(ref RaidDelayVarianceHours, "raidDelayVarianceHours", RaidDelayVarianceHoursDef);
            Scribe_Values.Look(ref warningDisabled, "warningDisabled", defaultValue: false);
            Scribe_Values.Look(ref WarningDelayHours, "warningDelayHours", WarningDelayHoursDef);
            Scribe_Values.Look(ref WarningDelayVarianceHours, "warningDelayVarianceHours", WarningDelayVarianceHoursDef);
            Scribe_Values.Look(ref SecondWaveHours, "secondWaveHours", SecondWaveHoursDef);
            Scribe_Values.Look(ref disableEndlessWaves, "disableEndlessWaves", defaultValue: false);
            Scribe_Values.Look(ref PursuitRaidType, "pursuitRaidType", PawnsArrivalModeDefOf.RandomDrop);
            Scribe_Values.Look(ref PursuitFactionPermanentEnemy, "pursuitFactionPermanentEnemy", defaultValue: true);
            Scribe_Values.Look(ref pursuitFactionDef, "pursuitFactionDef", FactionDefOf.Mechanoid);
            Scribe_Values.Look(ref PursuitFaction, "pursuitFaction", Faction.OfMechanoids);
            Scribe_Values.Look(ref canDoNormalRaid, "canDoNormalRaid", defaultValue: false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (mapWarningTimers == null)
                {
                    mapWarningTimers = new Dictionary<Map, int>();
                }
                if (mapRaidTimers == null)
                {
                    mapRaidTimers = new Dictionary<Map, int>();
                }
            }
            SetupRanges();
            DebugLog(PrintFields());
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
            /* -- Widgets for picking faction and raid type -- */
            /* Faction Picker */
            if (Widgets.ButtonText(rect1, pursuitFactionDef.LabelCap))
            {
                List<FloatMenuOption> faclist = new List<FloatMenuOption>();
                foreach (FactionDef item in DefDatabase<FactionDef>.AllDefs.Where((FactionDef d) => (d.displayInFactionSelection && !d.isPlayer && d.canStageAttacks)))
                {
                    FactionDef localFd = item;
                    faclist.Add(new FloatMenuOption(localFd.LabelCap, delegate
                    {
                        pursuitFactionDef = localFd;
                    }));
                }
                Find.WindowStack.Add(new FloatMenu(faclist));
            }
            /* Permanent enemy checkbox */
            if (pursuitFactionDef.permanentEnemy)
            {
                Widgets.TextArea(rect2, "rpmPermanentEnemyTip".Translate(), true);
            }
            else
            {
                Widgets.CheckboxLabeled(rect2, "rpmPermanentEnemy".Translate(), ref PursuitFactionPermanentEnemy);
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

        public override void PostWorldGenerate()
        {
            isFirstPeriod = true;
            PursuitFaction = Find.FactionManager.FirstFactionOfDef(pursuitFactionDef);
            if (PursuitFaction != null && !pursuitFactionDef.permanentEnemy)
            {
                PursuitFaction.TryAffectGoodwillWith(Faction.OfPlayer, -200, false, false);
                if (pursuitFactionDef.techLevel < PursuitRaidType.minTechLevel)
                {
                    PursuitRaidType = PawnsArrivalModeDefOf.EdgeWalkIn;
                }
            }
            SetupRanges();
            mapWarningTimers.Clear();
            mapRaidTimers.Clear();
            DebugLog(PrintFields());
        }

        public override void PostMapGenerate(Map map)
        {
            StartTimers(map);
        }

        public override void MapRemoved(Map map)
        {
            if (mapWarningTimers.Remove(map))
            {
                mapRaidTimers.Remove(map);
            }
        }

        private int TimerIntervalTick(int timer)
        {
            return (timer + TickInterval - 1) / TickInterval * TickInterval;
        }
        private int TimerInterval(int interval)
        {
            return (interval / TickInterval) * TickInterval;
        }

        public override void Tick()
        {
            if (Find.TickManager.TicksGame % TickInterval != 0)
            {
                return;
            }
            tmpMaps.Clear();
            tmpMaps.AddRange(mapWarningTimers.Keys);
            foreach (Map tmpMap in tmpMaps)
            {
                if (UpdateDisabled())
                {
                    /* This is where vanilla would block pursuit if you don't have a grav engine. */
                    mapWarningTimers.Remove(tmpMap);
                    mapRaidTimers.Remove(tmpMap);
                    continue;
                }
                /* This if clause only exists to wipe out pocket map timers that were added before the pocket map timer bug was fixed. */
                if (tmpMap.info.parent is PocketMapParent)
                {
                    MapRemoved(tmpMap);
                    continue;
                }
                /* You can technically edit the permanentEnemy field in a FactionDef during runtime, but that seems pretty ill-advised, since it changes the def
                 * for the *entire game* until it's rebooted.
                 * So instead, if the player has their pursuit faction set to 'permanent enemy', then we just reset the faction's goodwill to -100 every 12 in-game hours. */
                /* We can't really do the reverse, though -- that is, if a faction is defined as a permanent enemy, then we can't just make them a not-permanent enemy
                 * by futzing with goodwill. The player will have to rely on other mods to un-perma-enemy their factions. */
                if (PursuitFactionPermanentEnemy && !pursuitFactionDef.permanentEnemy &&
                    Find.TickManager.TicksGame % (TickInterval * 12) == 0)
                {
                    FactionRelation factionRelation = PursuitFaction.RelationWith(Faction.OfPlayer);
                    int relationDecrease = -1 * (100 + factionRelation.baseGoodwill);
                    PursuitFaction.TryAffectGoodwillWith(Faction.OfPlayer, relationDecrease, false);
                    DebugLog($"reduced faction {PursuitFaction.Name}'s goodwill by {relationDecrease}");
                }

                if (Find.TickManager.TicksGame == TimerIntervalTick(mapWarningTimers[tmpMap]))
                {
                    if (pursuitFactionDef == FactionDefOf.Mechanoid)
                    {
                        Find.LetterStack.ReceiveLetter("LetterLabelMechanoidThreatRuthless".Translate(), "LetterTextMechanoidThreatRuthless".Translate(), LetterDefOf.ThreatSmall);
                    }
                    else
                    {
                        Find.LetterStack.ReceiveLetter("LetterLabelRuthlessFaction".Translate(PursuitFaction.NameColored), "LetterTextRuthlessFaction".Translate(PursuitFaction.NameColored), LetterDefOf.ThreatSmall);
                    }
                }
                if (Find.TickManager.TicksGame == TimerIntervalTick(mapRaidTimers[tmpMap]))
                {
                    FireRaid_NewTemp(tmpMap, 1.5f, 2000f);
                }
                if (Find.TickManager.TicksGame == TimerIntervalTick(mapRaidTimers[tmpMap] + SecondRaidDelay))
                {
                    FireRaid_NewTemp(tmpMap, 2f, 8000f);
                }
                /* Vanilla seems to stop at the second raid. So, theoretically, if you beat both raids... you're home free? Seems too easy. Especially with mods.
                 * So I added an endless mode. The mechanoids will *never* stop coming, not until you leave.
                 */
                if (!disableEndlessWaves &&
                    Find.TickManager.TicksGame > TimerIntervalTick(mapRaidTimers[tmpMap] + SecondRaidDelay) &&
                    Find.TickManager.TicksGame % TimerInterval(EndlessRaidInterval) == 0)
                {
                    FireRaid_NewTemp(tmpMap, 2f, 10000f);
                }
            }
        }

        private void StartTimers(Map map)
        {
            if (!(map.info.parent is PocketMapParent) && PursuitFaction != null &&
                !(pursuitFactionDef == FactionDefOf.Mechanoid && map.generatorDef == MapGeneratorDefOf.Mechhive))
            {
                if (isFirstPeriod)
                {
                    mapWarningTimers[map] = Find.TickManager.TicksGame + WarningDelayRange.RandomInRange;
                    mapRaidTimers[map] = Find.TickManager.TicksGame + FirstRaidDelayRange.RandomInRange;
                    isFirstPeriod = false;
                }
                else
                {
                    mapWarningTimers[map] = Find.TickManager.TicksGame + WarningDelayRange.RandomInRange;
                    mapRaidTimers[map] = Find.TickManager.TicksGame + RaidDelayRange.RandomInRange;
                }
                DebugLog($"Starting Timers for faction {PursuitFaction.Name} | Warning timer: {mapWarningTimers[map]} Raid timer: {mapRaidTimers[map]}");
            }
        }

        private bool UpdateDisabled()
        {
            if (PursuitFaction == null || PursuitFaction.deactivated || PursuitFaction.defeated || 
                (!pursuitFactionDef.permanentEnemy && !FactionUtility.HostileTo(PursuitFaction, Faction.OfPlayer)))
            {
                if (!Disabled)
                    DebugLog($"Disabling pursuit for faction {PursuitFaction?.Name ?? "null"}");
                Disabled = true;
            }
            else
            {
                if (Disabled)
                    DebugLog($"Enabling for faction {PursuitFaction?.Name ?? "null"}");
                Disabled = false;
            }
            return Disabled;
        }

        private void SetupRanges()
        {
            int warningDelayAbsolute = Math.Max(RaidDelayHours - WarningDelayHours, 0);
            WarningDelayRange = new IntRange(Math.Max(warningDelayAbsolute - WarningDelayVarianceHours, 0) * GenDate.TicksPerHour,
                                             (warningDelayAbsolute + WarningDelayVarianceHours) * GenDate.TicksPerHour);
            RaidDelayRange = new IntRange((RaidDelayHours - RaidDelayVarianceHours) * GenDate.TicksPerHour,
                                          (RaidDelayHours + RaidDelayVarianceHours) * GenDate.TicksPerHour);
            FirstRaidDelayRange = new IntRange((FirstRaidDelayHours - FirstRaidDelayVarianceHours) * GenDate.TicksPerHour,
                                               (FirstRaidDelayHours + FirstRaidDelayVarianceHours) * GenDate.TicksPerHour);
        }

        private void FireRaid(Map map)
        {
            FireRaid_NewTemp(map, 1.5f, 5000f);
        }

        private void FireRaid_NewTemp(Map map, float pointsMultiplier, float minPoints)
        {
            IncidentParms incidentParms = new IncidentParms();
            incidentParms.forced = true;
            incidentParms.target = map;
            incidentParms.points = Mathf.Max(minPoints, StorytellerUtility.DefaultThreatPointsNow(map) * pointsMultiplier);
            incidentParms.faction = PursuitFaction;
            incidentParms.raidArrivalMode = PursuitRaidType;
            incidentParms.raidStrategy = RaidStrategyDefOf.ImmediateAttack;
            IncidentDefOf.RaidEnemy.Worker.TryExecute(incidentParms);
            DebugLog($"Firing new raid with {incidentParms.points} threat points");
        }

        public override IEnumerable<Alert> GetAlerts()
        {
            Map currentMap = Find.CurrentMap;
            if (currentMap != null && AlertCached != null)
            {
                yield return AlertCached;
            }
        }

        private string PrintFields()
        {
            StringBuilder output = new StringBuilder();
            if (PursuitFaction == null)
            {
                output.Append($"Faction is NULL for specified faction {pursuitFactionDef.LabelCap}");
                return output.ToString().Trim();
            }

            output.AppendLine($"Pursuit Disabled: {Disabled}");
            output.AppendLine($"\tPursuit Faction: {PursuitFaction.Name} Permanent Enemy: {PursuitFactionPermanentEnemy} Pursuit Raid Type: {PursuitRaidType.defName} Can Normal Raid: {canDoNormalRaid}");
            output.AppendLine($"\tisFirstPeriod: {isFirstPeriod}  SecondWaveHours: {SecondWaveHours}  disableEndlessWaves: {disableEndlessWaves} EndlessWavesHours: {EndlessWavesHours}");
            output.AppendLine($"\tFIRST RAID DELAY Mean: {FirstRaidDelayHours} Variance: {FirstRaidDelayVarianceHours} Range: ({FirstRaidDelayRange.min},{FirstRaidDelayRange.max})");
            output.AppendLine($"\tRAID DELAY Mean: {RaidDelayHours} Variance: {RaidDelayVarianceHours} Range: ({RaidDelayRange.min},{RaidDelayRange.max})");
            output.AppendLine($"\tWARNING DELAY Disabled: {warningDisabled} Mean: {WarningDelayHours} Variance: {WarningDelayVarianceHours} Range: ({WarningDelayRange.min},{WarningDelayRange.max})");

            return output.ToString().Trim();
        }
        private void DebugLog(string msg)
        {
            if (DebugLoggingEnabled)
            {
                string output = "[Ruthless Faction Pursuit] " + msg;
                Log.Message(output);
            }
        }
    }

    public class Alert_PursuitFactionThreat : Alert_Scenario
    {
        public int raidTick;

        public string factionName;

        private bool Red => Find.TickManager.TicksGame > raidTick - 60000;

        private bool Critical => Find.TickManager.TicksGame > raidTick;

        protected override Color BGColor
        {
            get
            {
                if (!Red)
                {
                    return Color.clear;
                }
                return Alert_Critical.BgColor();
            }
        }

        public override AlertReport GetReport()
        {
            return AlertReport.Active;
        }

        public override string GetLabel()
        {
            if (Critical)
            {
                return "AlertPursuitThreatCritical".Translate(factionName);
            }
            return "AlertPursuitThreat".Translate(factionName) + ": " + (raidTick - Find.TickManager.TicksGame).ToStringTicksToPeriod(allowSeconds: false, shortForm: false, canUseDecimals: false);
        }

        public override TaggedString GetExplanation()
        {
            if (Critical)
            {
                return "AlertPursuitThreatCriticalDesc".Translate(factionName);
            }
            return "AlertPursuitThreatDesc".Translate(factionName);
        }
    }
}
