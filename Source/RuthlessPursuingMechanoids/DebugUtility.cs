using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace RuthlessPursuingMechanoids
{
    public static class DebugUtility
    {
        public static bool DebugLoggingEnabled = true;
        public static void DebugLog(string msg, LogMessageType messageType = LogMessageType.Message)
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
    }
}
