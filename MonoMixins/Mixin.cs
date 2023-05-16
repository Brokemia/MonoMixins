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

        static void Main(string[] args) {
            //CreateHook(Test.testNew, Test.hi);
            Load(typeof(Mixin).Assembly);

            Console.WriteLine("\n\nhello() Output:\n");
            int i = 2;
            Test.hello(i);

            Console.WriteLine("\n\ncallManyParameters() Output:\n");
            Test.callManyParameters();
            Console.ReadKey();
        }

        public static void Load(Assembly assembly) {
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
            Console.WriteLine("Before:\n");
            PrintIL(ctx.Instrs);

            List<Instruction> injectBefore = inject.FindTargets(ctx);

            foreach (var ins in injectBefore) {
                if (inject.AsDelegate) {
                    InjectAsDelegate(ctx, src, ins, inject);
                } else {
                    InjectDirect(ctx, src, ins, inject);
                }
            }

            Console.WriteLine("\n\nAfter:\n");
            PrintIL(ctx.Instrs);
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
                
                if (modifyArg != null) {
                    if (ins.MatchLdarg(out int arg) && arg >= ctxParamCount) {
                        il.EmitLdloc(locals.Count - 1 - arg + ctxParamCount);
                        continue;
                    } else if (ins.MatchLdarga(out arg) && arg >= ctxParamCount) {
                        il.EmitLdloca(locals.Count - 1 - arg + ctxParamCount);
                        continue;
                    } else if (ins.MatchStarg(out arg) && arg >= ctxParamCount) {
                        il.EmitStloc(locals.Count - 1 - arg + ctxParamCount);
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
