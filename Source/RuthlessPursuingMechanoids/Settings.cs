using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace RuthlessPursuingMechanoids
{
    public class RFPSettings : ModSettings
    {
        public static bool printDebug = false;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref printDebug, "printDebug", false, true);
        }

        string buffer1;
        public void DoWindowContents(Rect inRect)
        {
            var list = new Listing_Standard()
            {
                ColumnWidth = inRect.width
            };

            list.CheckboxLabeled("printDebug".Translate(), ref printDebug);

            list.End();
        }

    }
    public class RFPMod : Mod
    {
        public static RFPSettings settings = new RFPSettings();

        public RFPMod(ModContentPack content) : base(content)
        {
            Pack = content;
            settings = GetSettings<RFPSettings>();
        }

        public ModContentPack Pack { get; }

        public override string SettingsCategory() => Pack.Name;

        public override void DoSettingsWindowContents(Rect inRect) => settings.DoWindowContents(inRect);
    }
}
