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
using static HarmonyLib.Code;

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
        private bool startHostile = true;

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
        internal Faction PursuitFaction = null;
        private string PursuitFactionName = "";
        private PawnsArrivalModeDef PursuitRaidType = PawnsArrivalModeDefOf.RandomDrop;
        private bool PursuitFactionPermanentEnemy = true;
        /*-*-*-*-*- END OPTIONS VALUES -*-*-*-*-*/

        public string FactionName => PursuitFactionName;

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
                    /* If the pursuit was disabled due to relations, then delete the alertcache. This will force it to regenerate if
                     * pursuit is reenabled later. */
                    if (DisabledDueToRelations && AlertToBeDisabled)
                    {
                        alertCached = null;
                        AlertToBeDisabled = false;
                    }
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
        private bool DisabledDueToRelations = false;
        private bool AlertToBeDisabled = false;
        private bool ReenableDueToRelations = false;
        private bool FactionIsPermanentEnemy => pursuitFactionDef.permanentEnemy || PursuitFactionPermanentEnemy;

        public ScenPart_RuthlessPursuingMechanoids(FactionDef pfd, string pfn, bool permaEnemy, bool sh, int frdh, int frdvh, int rdh, int rdvh,
                                                   bool wd, int wdh, int wdvh, int swh, bool dew, int ewh, bool cdnr)
        {
            pursuitFactionDef = pfd;
            PursuitFactionName = pfn;
            PursuitFactionPermanentEnemy = permaEnemy;
            startHostile = sh;
            FirstRaidDelayHours = frdh;
            FirstRaidDelayVarianceHours = frdvh;
            RaidDelayHours = rdh;
            RaidDelayVarianceHours = rdvh;
            warningDisabled = wd;
            WarningDelayHours = wdh;
            WarningDelayVarianceHours = wdvh;
            SecondWaveHours = swh;
            disableEndlessWaves = dew;
            EndlessWavesHours = ewh;
            canDoNormalRaid = cdnr;
        }
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
            Scribe_Values.Look(ref DisabledDueToRelations, "disabledDueToRelations", defaultValue: false);
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
            Scribe_Defs.Look(ref PursuitRaidType, "pursuitRaidType");
            Scribe_Values.Look(ref PursuitFactionPermanentEnemy, "pursuitFactionPermanentEnemy", defaultValue: true);
            Scribe_Defs.Look(ref pursuitFactionDef, "pursuitFactionDef");
            Scribe_Values.Look(ref canDoNormalRaid, "canDoNormalRaid", defaultValue: false);
            Scribe_Values.Look(ref PursuitFactionName, "pursuitFactionName", "");
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
                SetFaction();
            }
            SetupRanges();
            DebugLog(PrintFields() + $"\n\tLoadSaveMode {Scribe.mode}");
        }

        public override void DoEditInterface(Listing_ScenEdit listing)
        {
            float totalBaseHeight = 15f;
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
                if (!PursuitFactionPermanentEnemy)
                {
                    Widgets.CheckboxLabeled(rect3, "rpmStartHostile".Translate(), ref startHostile);
                }
            }

            /* -- Fields for setting the timers -- */
            // 14400 hours is ten rimyears. Should be plenty for a maximum bound
            // The minimum value for all raid delays is 1 hour. If it were 0, then we could hit some infinite loop shit
            //   1 hour is pretty fucking low, probably unplayably low in most cases. But if the user really wants to fuck themselves, then why not let them?
            Widgets.TextArea(rect4, "rpmTimeInHours".Translate(), true);
            Widgets.TextFieldNumericLabeled(rect5, "rpmFirstRaidMeanDelay".Translate(), ref FirstRaidDelayHours, ref frdhbuf, 1, 14400);
            Widgets.TextFieldNumericLabeled(rect6, "rpmFirstRaidMeanDelayVariance".Translate(), ref FirstRaidDelayVarianceHours, ref frdvhbuf, 0, 14400);
            Widgets.TextFieldNumericLabeled(rect7, "rpmRaidMeanDelay".Translate(), ref RaidDelayHours, ref rdhbuf, 1, 14400);
            Widgets.TextFieldNumericLabeled(rect8, "rpmRaidMeanDelayVariance".Translate(), ref RaidDelayVarianceHours, ref rdvhbuf, 0, 14400);
            Widgets.CheckboxLabeled(rect9, "rpmWarningDisabled".Translate(), ref warningDisabled);
            Widgets.TextFieldNumericLabeled(rect10, "rpmWarningMeanDelay".Translate(), ref WarningDelayHours, ref wdhbuf, 0, 14400);
            Widgets.TextFieldNumericLabeled(rect11, "rpmWarningMeanDelayVariance".Translate(), ref WarningDelayVarianceHours, ref wdvhbuf, 0, 14400);
            Widgets.TextFieldNumericLabeled(rect12, "rpmSecondWaveHours".Translate(), ref SecondWaveHours, ref swhbuf, 1, 14400);
            Widgets.CheckboxLabeled(rect13, "rpmDisableEndless".Translate(), ref disableEndlessWaves);
            Widgets.TextFieldNumericLabeled(rect14, "rpmEndlessWaveHours".Translate(), ref EndlessWavesHours, ref ewhbuf, 1, 14400);
            Widgets.CheckboxLabeled(rect15, "rpmNormalRaid".Translate(), ref canDoNormalRaid);
        }

        private void SetFaction()
        {
            /* If we already have the faction's name (such as if this scenpart was added through Omni Pursuit, or after load), then find the faction
             * with that name. Otherwise, go by def. */
            if (!PursuitFactionName.NullOrEmpty() && PursuitFaction == null)
            {
                foreach (Faction tmpfac in Find.FactionManager.GetFactions(false, true, true))
                {
                    if (tmpfac.def == pursuitFactionDef && tmpfac.HasName && tmpfac.Name == PursuitFactionName)
                    {
                        PursuitFaction = tmpfac;
                        DebugLog($" [SetFaction] Found faction for name {PursuitFactionName}");
                        break;
                    }
                }
            }
            if (PursuitFaction == null)
            {
                /* If there's no saved name, or the saved name is invalid, then we'll fall back on finding the first faction that matches the faction def. */
                /* If There are multiple factions with this def, then find a faction that isn't already assigned to a Ruthless Pursuit ScenPart. If all
                 * factions are already assigned, then we move on without doing anything. */
                List<Faction> facList = Find.FactionManager.AllFactions.Where((Faction f) => f.def == pursuitFactionDef).ToList();
                DebugLog($" [SetFaction] Found {facList.Count()} factions of def {pursuitFactionDef.LabelCap}");
                if (facList.Count() == 1)
                {
                    PursuitFaction = Find.FactionManager.FirstFactionOfDef(pursuitFactionDef);
                }
                else if (facList.Count() > 1)
                {
                    List<ScenPart_RuthlessPursuingMechanoids> pursuitScenParts = Find.Scenario.AllParts.OfType<ScenPart_RuthlessPursuingMechanoids>().ToList();
                    foreach (Faction f in facList)
                    {
                        bool foundInScenPart = false;
                        foreach (ScenPart_RuthlessPursuingMechanoids part in pursuitScenParts)
                        {
                            if (part.FactionName == f.Name)
                            {
                                foundInScenPart = true;
                                break;
                            }
                        }
                        if (!foundInScenPart)
                        {
                            PursuitFaction = f;
                            break;
                        }
                    }
                    /* If PursuitFaction is still NULL at this point, then it means that every faction of def pursuitFactionDef has already been assigned
                     * to a Ruthless Pursuit ScenPart. We *could* assign a second part to a random faction, but that seems a little odd to do. So for now,
                     * we'll just leave PursuitFaction as NULL, meaning that this particular scenPart won't actually be doing anything. */
                }
                /* PursuitFaciton being NULL here could mean that there are *no* factions with def pursuitFactionDef. In which case, we... should
                 * also do nothing. */
                /* This following code is purely for debugging/logging */
                if (PursuitFaction == null)
                {
                    if (facList.Count() > 0)
                    {
                        DebugLog($" [SetFaction] found {facList.Count()} faction(s) of def {pursuitFactionDef.LabelCap}, but all were already assigned", LogMessageType.Warning);
                    }
                    else
                    {
                        DebugLog($" [SetFaction] found no factions of def {pursuitFactionDef.LabelCap}", LogMessageType.Warning);
                    }
                }
                else
                {
                    DebugLog($" [SetFaction] assigned faction {PursuitFaction.Name}");
                }
            }

            if (PursuitFactionName.NullOrEmpty())
            {
                PursuitFactionName = PursuitFaction?.Name ?? "null faction";
            }
            else if (PursuitFaction != null && PursuitFactionName != PursuitFaction.Name)
            {
                DebugLog($" [SetFaction] mismatch between saved PursuitFactionName {PursuitFactionName} and assigned Faction's name {PursuitFaction.Name}. Setting PursuitFactionName to Faction's name.",
                         LogMessageType.Error);
                PursuitFactionName = PursuitFaction.Name;
            }
        }

        public override void PostWorldGenerate()
        {
            isFirstPeriod = true;
            /* If the faction doesn't have the tech level for transport pods, then set them to raid through EdgeWalkIn instead.
             * This will also block them from attacking space maps. */
            if (pursuitFactionDef.techLevel < PursuitRaidType.minTechLevel)
            {
                PursuitRaidType = PawnsArrivalModeDefOf.EdgeWalkIn;
            }
            SetFaction();

            if (PursuitFaction != null && !pursuitFactionDef.permanentEnemy && (PursuitFactionPermanentEnemy || startHostile))
            {
                PursuitFaction.TryAffectGoodwillWith(Faction.OfPlayer, -200, false, false);
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
            UpdateDisabled();
            tmpMaps.Clear();
            tmpMaps.AddRange(mapWarningTimers.Keys);
            /* You can technically edit the permanentEnemy field in a FactionDef during runtime, but that seems pretty ill-advised, since it changes the def
             * for the *entire game* until it's rebooted.
             * So instead, if the player has their pursuit faction set to 'permanent enemy', then we just reset the faction's goodwill to -100. */
            /* We can't really do the reverse, though -- that is, if a faction is defined as a permanent enemy, then we can't just make them a not-permanent enemy
             * by futzing with goodwill. The player will have to rely on other mods to un-perma-enemy their factions. */
            if (!Disabled && PursuitFactionPermanentEnemy && !pursuitFactionDef.permanentEnemy)
            {
                FactionRelation factionRelation = PursuitFaction.RelationWith(Faction.OfPlayer);
                int relationDecrease = -1 * (100 + factionRelation.baseGoodwill);
                /* If relationDecrease is 0, then the faction's goodwill is already at -100. No need to futz with goodwill or send the player a message. */
                if (relationDecrease != 0)
                {
                    Messages.Message("rpmMessagePermaEnemyFactionRelationsReset".Translate(PursuitFaction.Name), GlobalTargetInfo.Invalid, MessageTypeDefOf.NegativeEvent);
                    PursuitFaction.TryAffectGoodwillWith(Faction.OfPlayer, relationDecrease);
                    DebugLog($"reduced faction {PursuitFaction.Name}'s goodwill by {relationDecrease}");
                }
            }
            foreach (Map tmpMap in tmpMaps)
            {
                if (Disabled)
                {
                    /* This is where vanilla would block pursuit if you don't have a grav engine. */
                    /* If pursuit is disabled due to relations, then don't remove the timers. Leaving them means we can access the map
                     * later, to restart the timers if relations degrade. */
                    if (!DisabledDueToRelations)
                    {
                        mapWarningTimers.Remove(tmpMap);
                        mapRaidTimers.Remove(tmpMap);
                    }
                    continue;
                }
                else if (ReenableDueToRelations)
                {
                    /* Restart the timers with minimum values */
                    DebugLog($"Restarting timers for faction {PursuitFaction?.Name ?? "null"} with minimum timers");
                    StartTimers(tmpMap, true);
                }
                /* This if clause only exists to wipe out pocket map timers that were added before the pocket map timer bug was fixed. */
                if (tmpMap.info.parent is PocketMapParent)
                {
                    MapRemoved(tmpMap);
                    continue;
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
                 * So I added an endless mode. The raids will *never* stop coming, not until you pack up and leave.
                 */
                if (!disableEndlessWaves &&
                    Find.TickManager.TicksGame > TimerIntervalTick(mapRaidTimers[tmpMap] + SecondRaidDelay) &&
                    Find.TickManager.TicksGame % TimerInterval(EndlessRaidInterval) == 0)
                {
                    FireRaid_NewTemp(tmpMap, 2f, 10000f);
                }
            }
            if (ReenableDueToRelations)
            {
                ReenableDueToRelations = false;
            }
        }

        private void StartTimers(Map map, bool forceMinimum = false)
        {
            /* If the raid type is EdgeWalkIn then don't start a timer if the map is in space. Not only is EdgeWalkIn not really valid for space maps, the poor bastards
             * would just suffocate and die. Not to mention that EdgeWalkIn factions wouldn't even have a way of reaching space! */
            if (!(map.info.parent is PocketMapParent) &&
                PursuitFaction != null &&
                !(PursuitRaidType == PawnsArrivalModeDefOf.EdgeWalkIn && 
                  (map.generatorDef == MapGeneratorDefOf.OrbitalRelay || map.generatorDef == MapGeneratorDefOf.Space || map.generatorDef ==  MapGeneratorDefOf.SpacePocket)) &&
                !(pursuitFactionDef == FactionDefOf.Mechanoid && map.generatorDef == MapGeneratorDefOf.Mechhive))
            {
                if (isFirstPeriod)
                {
                    /* It seems that we don't actually call into the Tick function at Tick 0. Which means that the first tick we check is actually TickInterval.
                     * So to make sure warning letters are sent properly, we set TickInterval to be the earliest tick a warning or raid can occur at. */
                    int warningDelayAbsolute = Math.Max(FirstRaidDelayHours - WarningDelayHours, 0);
                    IntRange tmpWarningDelayRange = new IntRange(Math.Max(warningDelayAbsolute - WarningDelayVarianceHours, 0) * GenDate.TicksPerHour,
                                                                         (warningDelayAbsolute + WarningDelayVarianceHours) * GenDate.TicksPerHour);
                    mapWarningTimers[map] = Find.TickManager.TicksGame + Math.Max((forceMinimum ? tmpWarningDelayRange.min : tmpWarningDelayRange.RandomInRange), TickInterval);
                    mapRaidTimers[map] = Find.TickManager.TicksGame + Math.Max((forceMinimum ? FirstRaidDelayRange.min : FirstRaidDelayRange.RandomInRange), TickInterval);
                    isFirstPeriod = false;
                }
                else
                {
                    mapWarningTimers[map] = Find.TickManager.TicksGame + (forceMinimum ? WarningDelayRange.min : WarningDelayRange.RandomInRange);
                    mapRaidTimers[map] = Find.TickManager.TicksGame + (forceMinimum ? RaidDelayRange.min : RaidDelayRange.RandomInRange);
                }
                DebugLog($"Starting Timers for faction {PursuitFaction.Name} | Warning timer: {mapWarningTimers[map]} Raid timer: {mapRaidTimers[map]} Current Tick: {Find.TickManager.TicksGame} Force Minimum: {forceMinimum}");
            }
        }

        private bool UpdateDisabled()
        {
            if (PursuitFaction == null || PursuitFaction.deactivated || PursuitFaction.defeated ||
                (!FactionIsPermanentEnemy && !FactionUtility.HostileTo(PursuitFaction, Faction.OfPlayer)))
            {
                if (!Disabled)
                {
                    DebugLog($"Disabling pursuit for faction {PursuitFaction?.Name ?? "null"}");
                    if (PursuitFaction.deactivated || PursuitFaction.defeated)
                    {
                        /* Send letter saying the faction was defeated, and thus pursuit is ceasing */
                        Find.LetterStack.ReceiveLetter("LetterLabelRuthlessFactionDefeated".Translate(PursuitFaction.NameColored), "LetterTextRuthlessFactionDefeated".Translate(PursuitFaction.NameColored), LetterDefOf.PositiveEvent);
                    }
                    else if (!FactionIsPermanentEnemy && !FactionUtility.HostileTo(PursuitFaction, Faction.OfPlayer))
                    {
                        /* Send letter saying that pursuit is ceasing due to improved faction relations */
                        DisabledDueToRelations = true;
                        AlertToBeDisabled = true;
                        Find.LetterStack.ReceiveLetter("LetterLabelRuthlessPursuitStopped".Translate(PursuitFaction.NameColored), "LetterTextRuthlessPursuitStopped".Translate(PursuitFaction.NameColored), LetterDefOf.PositiveEvent);
                    }
                }
                Disabled = true;
            }
            else
            {
                if (Disabled)
                {
                    DebugLog($"Enabling pursuit for faction {PursuitFaction?.Name ?? "null"}");
                    if (PursuitFaction != null && !FactionIsPermanentEnemy && FactionUtility.HostileTo(PursuitFaction, Faction.OfPlayer))
                    {
                        /* Send letter saying that pursuit is resuming due to degraded faction relations */
                        ReenableDueToRelations = true;
                        Find.LetterStack.ReceiveLetter("LetterLabelRuthlessPursuitResumed".Translate(PursuitFaction.NameColored), "LetterTextRuthlessPursuitResumed".Translate(PursuitFaction.NameColored), LetterDefOf.ThreatSmall);
                    }
                    else if (PursuitFaction != null)
                    {
                        /* It *should* be that the only way we're re-enabling pursuit is if faction relations previously improved to neutral or above, and
                         * then degraded back to hostile. It shouldn't be possible through normal gameplay for the deactivated or defeated fields
                         * to turn TRUE after being set to FALSE. But just in case we do hit that case, we'll log a warning message. */
                        /* NOTE: in this case, it is likely that all of the current maps will have been wiped of raid timers. We *could* grab all of the
                         * current maps and re-add timers... but since this case shouldn't ever be reached under normal operation, I think it's best to leave it
                         * code-lite. Timers will be applied to any new maps made after this point, anyways. */
                        DebugLog($"Unexpected pursuit re-enabling for faction {PursuitFaction.Name}. Deactivated: {PursuitFaction.deactivated} Defeated: {PursuitFaction.defeated}", LogMessageType.Warning);
                    }
                    else
                    {
                        /* It should be LITERALLY logically impossible to reach this case. I'm including it only for completeness. */
                        DebugLog($"Enabled Ruthless Pursuit for NULL faction (how the hell did that happen??)", LogMessageType.Error);
                    }
                }
                DisabledDueToRelations = false;
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
            output.AppendLine($"\tPursuit Faction: {PursuitFaction.Name} (Saved Name: {PursuitFactionName}) Permanent Enemy: {PursuitFactionPermanentEnemy} Pursuit Raid Type: {PursuitRaidType.defName} Can Normal Raid: {canDoNormalRaid}");
            output.AppendLine($"\tisFirstPeriod: {isFirstPeriod}  SecondWaveHours: {SecondWaveHours}  disableEndlessWaves: {disableEndlessWaves} EndlessWavesHours: {EndlessWavesHours}");
            output.AppendLine($"\tFIRST RAID DELAY Mean: {FirstRaidDelayHours} Variance: {FirstRaidDelayVarianceHours} Range: ({FirstRaidDelayRange.min},{FirstRaidDelayRange.max})");
            output.AppendLine($"\tRAID DELAY Mean: {RaidDelayHours} Variance: {RaidDelayVarianceHours} Range: ({RaidDelayRange.min},{RaidDelayRange.max})");
            output.AppendLine($"\tWARNING DELAY Disabled: {warningDisabled} Mean: {WarningDelayHours} Variance: {WarningDelayVarianceHours} Range: ({WarningDelayRange.min},{WarningDelayRange.max})");

            return output.ToString().Trim();
        }
        private void DebugLog(string msg, LogMessageType messageType = LogMessageType.Message)
        {
            string output = "[Ruthless Faction Pursuit] " + msg;
            if (messageType == LogMessageType.Message)
            {
                if (DebugLoggingEnabled)
                {
                    Log.Message(output);
                }
            }
            else if (messageType == LogMessageType.Warning)
            {
                Log.Warning(output);
            }
            else if (messageType == LogMessageType.Error)
            {
                Log.Error(output);
            }
        }
        public override bool CanCoexistWith(ScenPart other)
        {
            if (other is ScenPart_RuthlessOmniPursuit)
            {
                return false;
            }

            return true;
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
