using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace ShuttleboxPlugin.Utils
{
    internal static class Extensions
    {
        public static bool TryCastAtHome<T, O> (this T inclass, out O outclass)
            where T : Il2CppObjectBase
            where O : Il2CppObjectBase
        {
            outclass = inclass.TryCast<O>();
            return outclass != null;
        }

        public static List<Transform> GetChildren(this Transform parent)
        {
            int count = parent.GetChildCount();
            List<Transform> outlist = new List<Transform>(count);
            for (int i = 0; i < count; i++)
                outlist.Add(parent.GetChild(i));

            return outlist;
        }
    }
}
