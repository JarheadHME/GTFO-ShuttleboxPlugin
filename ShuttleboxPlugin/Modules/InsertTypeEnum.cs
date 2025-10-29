using Il2CppInterop.Runtime.Injection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace ShuttleboxPlugin.Modules
{
    public static class InsertTypeEnum
    {
        public static readonly eCarryItemInsertTargetType value;

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Init() { }
        
        static InsertTypeEnum()
        {
            int index = EnumUtil.GetValueLength<eCarryItemInsertTargetType>();
            EnumInjector.InjectEnumValues<eCarryItemInsertTargetType>(new Dictionary<string, object>()
            {
                { "ShuttleboxTransfer", index }
            });

            value = (eCarryItemInsertTargetType)index;
        }
    }
}
