using Mono.Cecil.Cil;
using MonoMod.Cil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MonoMixins {
    public static class CursorExtensions {

        public static ILCursor EmitLdloc(this ILCursor il, int index) {
            if (index <= 3) {
                return il.Emit(index switch {
                    0 => OpCodes.Ldloc_0,
                    1 => OpCodes.Ldloc_1,
                    2 => OpCodes.Ldloc_2,
                    _ => OpCodes.Ldloc_3
                });
            }

            return il.Emit(index <= byte.MaxValue ? OpCodes.Ldloc_S : OpCodes.Ldloc, index);
        }

        public static ILCursor EmitStloc(this ILCursor il, int index) {
            if (index <= 3) {
                return il.Emit(index switch {
                    0 => OpCodes.Stloc_0,
                    1 => OpCodes.Stloc_1,
                    2 => OpCodes.Stloc_2,
                    _ => OpCodes.Stloc_3
                });
            }

            return il.Emit(index <= byte.MaxValue ? OpCodes.Stloc_S : OpCodes.Stloc, index);
        }

        public static ILCursor EmitLdarg(this ILCursor il, int index) {
            if (index <= 3) {
                return il.Emit(index switch {
                    0 => OpCodes.Ldarg_0,
                    1 => OpCodes.Ldarg_1,
                    2 => OpCodes.Ldarg_2,
                    _ => OpCodes.Ldarg_3
                });
            }

            return il.Emit(index <= byte.MaxValue ? OpCodes.Ldarg_S : OpCodes.Ldarg, index);
        }
    }
}
