using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace MonoMixins {
    public static class Mixin {

        public static bool DEBUG_PRINT = false;

        public static void Load(Assembly assembly = null) {
            assembly ??= Assembly.GetCallingAssembly();
            
            foreach (var type in assembly.DefinedTypes) {
                foreach (var method in type.GetMethods()) {
                    foreach (var attr in method.GetCustomAttributes()) {
                        if (attr is InjectAttribute inject) {
                            new ILHook(inject.Method, ctx => Patch(ctx, method, inject));
                        }
                    }
                }
            }
        }

        public static void Patch(ILContext ctx, MethodInfo src, InjectAttribute inject) {
            if (DEBUG_PRINT) {
                Console.WriteLine("Before:\n");
                PrintIL(ctx.Instrs);
            }

            List<Instruction> injectBefore = inject.FindTargets(ctx);

            foreach (var ins in injectBefore) {
                if (inject.AsDelegate) {
                    InjectAsDelegate(ctx, src, ins, inject);
                } else {
                    InjectDirect(ctx, src, ins, inject);
                }
            }

            if (DEBUG_PRINT) {
                Console.WriteLine("\n\nAfter:\n");
                PrintIL(ctx.Instrs);
            }
        }

        public static void InjectAsDelegate(ILContext ctx, MethodInfo src, Instruction injectPoint, InjectAttribute inject) {
            var il = new ILCursor(ctx);
            il.Goto(injectPoint, inject.InjectAfter ? MoveType.After : MoveType.Before);

            // Store last arguments in local variables
            var modifyArg = inject as ModifyArgAttribute;
            int callParamCount = 0;
            if (modifyArg != null) {
                var method = (MethodReference)injectPoint.Operand;
                callParamCount = method.Parameters.Count;
                var locals = ctx.Body.Variables;
                // Last arguments are highest on the stack, so we need to get those
                int earliestArgToStore = modifyArg.Index < 0 ? 0 : modifyArg.Index;
                for (int i = callParamCount - 1; i >= earliestArgToStore; i--) {
                    locals.Add(new VariableDefinition(method.Parameters[i].ParameterType));
                    il.EmitStloc(locals.Count - 1);
                }
            }

            if (inject.TakeArguments) {
                var paramCount = ctx.Method.Parameters.Count;
                for (int i = 0; i < paramCount; i++) {
                    il.EmitLdarg(i);
                }
            }

            // Put arguments to modify after the basic arguments
            if (modifyArg != null) {
                int lastIndex = ctx.Body.Variables.Count - 1;
                if (modifyArg.Index < 0) {
                    for (int i = callParamCount; i > 0; i--) {
                        il.EmitLdloca(lastIndex--);
                    }
                } else {
                    il.EmitLdloca(lastIndex);
                }
            }

            // Load locals that we are capturing
            if (inject.CaptureLocals != null) {
                var srcParams = src.GetParameters();
                for (int i = 0; i < inject.CaptureLocals.Length; i++) {
                    // If `ref` or `out` is used, pass the local's address instead, so that the user can edit it
                    if (srcParams[srcParams.Length - inject.CaptureLocals.Length + i].ParameterType.IsByRef) {
                        il.EmitLdloca(inject.CaptureLocals[i]);
                    } else {
                        il.EmitLdloc(inject.CaptureLocals[i]);
                    }
                }
            }

            il.Emit(OpCodes.Call, src);

            // Restore args from locals to the stack
            if (modifyArg != null) {
                int lastIndex = ctx.Body.Variables.Count - 1;
                int earliestArgToStore = modifyArg.Index < 0 ? 0 : modifyArg.Index;
                for (int i = callParamCount - 1; i >= earliestArgToStore; i--) {
                    il.EmitLdloc(lastIndex--);
                }
            }
        }

        public static void InjectDirect(ILContext ctx, MethodBase src, Instruction injectPoint, InjectAttribute inject) {
            var locals = ctx.Body.Variables;
            int localOffset = locals.Count;
            var srcBody = ctx.Import(src).Resolve().Body;

            var il = new ILCursor(ctx);
            il.Goto(injectPoint, inject.InjectAfter ? MoveType.After : MoveType.Before);

            injectPoint = il.Next;
            
            // Copy over locals
            foreach (var v in srcBody.Variables) {
                locals.Add(new VariableDefinition(v.VariableType));
            }

            // Store last arguments in local variables
            var modifyArg = inject as ModifyArgAttribute;
            int lastModifyArgLocal = 0;
            int callParamCount = 0;
            if (modifyArg != null) {
                var method = (MethodReference)injectPoint.Operand;
                callParamCount = method.Parameters.Count;

                // Last arguments are highest on the stack, so we need to get those
                int earliestArgToStore = modifyArg.Index < 0 ? 0 : modifyArg.Index;
                for (int i = callParamCount - 1; i >= earliestArgToStore; i--) {
                    locals.Add(new VariableDefinition(method.Parameters[i].ParameterType));
                    il.EmitStloc(locals.Count - 1);
                }

                lastModifyArgLocal = locals.Count - 1;

                // Create `ref`-equivalent locals
                // TODO Optimize this by not making locals for arguments that aren't touched
                // Would probably require a mapping of locals, rather than using offsets and stuff
                if(modifyArg.Index < 0) {
                    for (int i = callParamCount - 1; i >= 0; i--) {
                        locals.Add(new VariableDefinition(method.Parameters[i].ParameterType));
                        il.EmitLdloca(lastModifyArgLocal - i);
                        il.EmitStloc(locals.Count - 1);
                    }
                } else {
                    locals.Add(new VariableDefinition(method.Parameters[modifyArg.Index].ParameterType));
                    il.EmitLdloca(lastModifyArgLocal);
                    il.EmitStloc(lastModifyArgLocal + 1);
                }
            }

            int firstCapturedRefLocal = locals.Count;
            // Maps arg index (relative to end of other args) to local index
            List<int> localCaptureMap = new();

            // If captured locals are taken by reference, we need to make ref variables for them
            int capturedLocalCount = 0;
            if (inject.CaptureLocals != null) {
                capturedLocalCount = inject.CaptureLocals.Length;
                var srcParams = src.GetParameters();
                for (int i = 0; i < capturedLocalCount; i++) {
                    if (srcParams[srcParams.Length - capturedLocalCount + i].ParameterType.IsByRef) {
                        int refLocal = locals.Count;
                        locals.Add(new VariableDefinition(locals[inject.CaptureLocals[i]].VariableType));
                        il.EmitLdloca(inject.CaptureLocals[i]);
                        il.EmitStloc(refLocal);
                        localCaptureMap.Add(refLocal);
                    } else {
                        localCaptureMap.Add(inject.CaptureLocals[i]);
                    }
                }
            }

            int ctxParamCount = ctx.Method.Parameters.Count;
            foreach (var ins in srcBody.Instructions) {
                if (ins.MatchRet()) {
                    // Leave off final ret
                    if (ins.Next != null) {
                        il.Emit(OpCodes.Br, injectPoint);
                    }
                    continue;
                } else if (ins.MatchLdloc(out int local)) {
                    local += localOffset;
                    il.EmitLdloc(local);
                    continue;
                } else if (ins.MatchLdloca(out local)) {
                    local += localOffset;
                    il.EmitLdloca(local);
                    continue;
                } else if (ins.MatchStloc(out local)) {
                    local += localOffset;
                    il.EmitStloc(local);
                    continue;
                }
                
                int modifiedArgCount = 0;
                if (modifyArg != null) {
                    modifiedArgCount = modifyArg.Index < 0 ? callParamCount : 1;
                    if (ins.MatchLdarg(out int arg) && arg >= ctxParamCount && arg < ctxParamCount + modifiedArgCount) {
                        il.EmitLdloc(firstCapturedRefLocal - 1 - arg + ctxParamCount);
                        continue;
                    } else if (ins.MatchLdarga(out arg) && arg >= ctxParamCount && arg < ctxParamCount + modifiedArgCount) {
                        il.EmitLdloca(firstCapturedRefLocal - 1 - arg + ctxParamCount);
                        continue;
                    } else if (ins.MatchStarg(out arg) && arg >= ctxParamCount && arg < ctxParamCount + modifiedArgCount) {
                        il.EmitStloc(firstCapturedRefLocal - 1 - arg + ctxParamCount);
                        continue;
                    }
                }

                // If a user assigns to an argument here, it will actually propagate back to the local, even if it's not passed by ref
                if (inject.CaptureLocals != null) {
                    if (ins.MatchLdarg(out int arg) && arg >= ctxParamCount + modifiedArgCount) {
                        il.EmitLdloc(localCaptureMap[arg - ctxParamCount - modifiedArgCount]);
                        continue;
                    } else if (ins.MatchLdarga(out arg) && arg >= ctxParamCount + modifiedArgCount) {
                        il.EmitLdloca(localCaptureMap[arg - ctxParamCount - modifiedArgCount]);
                        continue;
                    } else if (ins.MatchStarg(out arg) && arg >= ctxParamCount + modifiedArgCount) {
                        il.EmitStloc(localCaptureMap[arg - ctxParamCount - modifiedArgCount]);
                        continue;
                    }
                }

                ctx.IL.InsertBefore(injectPoint, ins);
            }

            // Restore args from locals to the stack
            if (modifyArg != null) {
                int lastIndex = lastModifyArgLocal;
                int earliestArgToStore = modifyArg.Index < 0 ? 0 : modifyArg.Index;
                for (int i = callParamCount - 1; i >= earliestArgToStore; i--) {
                    il.EmitLdloc(lastIndex--);
                }
            }

        }

        public static void PrintIL(Collection<Instruction> list) {
            foreach(var il in list) {
                Console.WriteLine(il);
            }
        }
    }
}
