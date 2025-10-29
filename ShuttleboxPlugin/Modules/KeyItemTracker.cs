using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShuttleboxPlugin.Modules
{
    public class KeyItemTracker
    {
        public static List<uint> KeyIdsInLevel = new List<uint>();

        public static void TrackAllKeysInLevel()
        {
            foreach (var obj in ProgressionObjectivesManager.Current.m_allProgressionObjectives)
            {
                var keyobj = obj.TryCast<ProgressionObjective_KeyCard>();
                if (keyobj != null)
                {
                    KeyIdsInLevel.Add(keyobj.KeyItem.DataBlockID);
                }
            }
        }

        public static void ClearTrackedKeyIds()
        {
            KeyIdsInLevel.Clear();
        }
    }
}
