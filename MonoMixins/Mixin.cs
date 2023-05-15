using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace MonoMixins {
    public static class Mixin {

        // FieldInfos for each item in each ValueTuple type
        private static FieldInfo[][] tupleFields = new FieldInfo[8][];

        static Mixin() {
            tupleFields[0] = new FieldInfo[] {
                typeof(ValueTuple<>).GetField("Item1")
            };
            var tupleType = typeof(ValueTuple<,>);
            tupleFields[1] = new FieldInfo[] {
                tupleType.GetField("Item1"),
                tupleType.GetField("Item2")
            };
            tupleType = typeof(ValueTuple<,,>);
            tupleFields[2] = new FieldInfo[] {
                tupleType.GetField("Item1"),
                tupleType.GetField("Item2"),
                tupleType.GetField("Item3")
            };
            tupleType = typeof(ValueTuple<,,,>);
            tupleFields[3] = new FieldInfo[] {
                tupleType.GetField("Item1"),
                tupleType.GetField("Item2"),
                tupleType.GetField("Item3"),
                tupleType.GetField("Item4")
            };
            tupleType = typeof(ValueTuple<,,,,>);
            tupleFields[4] = new FieldInfo[] {
                tupleType.GetField("Item1"),
                tupleType.GetField("Item2"),
                tupleType.GetField("Item3"),
                tupleType.GetField("Item4"),
                tupleType.GetField("Item5")
            };
            tupleType = typeof(ValueTuple<,,,,,>);
            tupleFields[5] = new FieldInfo[] {
                tupleType.GetField("Item1"),
                tupleType.GetField("Item2"),
                tupleType.GetField("Item3"),
                tupleType.GetField("Item4"),
                tupleType.GetField("Item5"),
                tupleType.GetField("Item6")
            };
            tupleType = typeof(ValueTuple<,,,,,,>);
            tupleFields[6] = new FieldInfo[] {
                tupleType.GetField("Item1"),
                tupleType.GetField("Item2"),
                tupleType.GetField("Item3"),
                tupleType.GetField("Item4"),
                tupleType.GetField("Item5"),
                tupleType.GetField("Item6"),
                tupleType.GetField("Item7")
            };
            tupleType = typeof(ValueTuple<,,,,,,,>);
            tupleFields[7] = new FieldInfo[] {
                tupleType.GetField("Item1"),
                tupleType.GetField("Item2"),
                tupleType.GetField("Item3"),
                tupleType.GetField("Item4"),
                tupleType.GetField("Item5"),
                tupleType.GetField("Item6"),
                tupleType.GetField("Item7"),
                tupleType.GetField("Rest")
            };
        }

        static void Main(string[] args) {
            //CreateHook(Test.testNew, Test.hi);
            Load(typeof(Mixin).Assembly);


            Test.hello(2);
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
            Console.WriteLine("\n\nOutput:\n");
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
                if (inject.TakeArguments || modifyArg.Index >= 0) {
                    var locals = ctx.Body.Variables;
                    // Last arguments are highest on the stack, so we need to get those
                    int earliestArgToStore = modifyArg.Index < 0 ? 0 : modifyArg.Index;
                    for (int i = callParamCount - 1; modifyArg.TakeArguments ? i >= earliestArgToStore : i > earliestArgToStore; i--) {
                        il.EmitStloc(locals.Count);
                        locals.Add(new VariableDefinition(method.Parameters[i].ParameterType));
                    }
                }
            }

            if (inject.TakeArguments) {
                var paramCount = ctx.Method.Parameters.Count;
                for (int i = 0; i < paramCount; i++) {
                    il.EmitLdarg(i);
                }

                // Put arguments to modify after the basic arguments
                if(modifyArg != null) {
                    int lastIndex = ctx.Body.Variables.Count - 1;
                    if (modifyArg.Index < 0) {
                        for (int i = callParamCount; i > 0; i--) {
                            il.EmitLdloc(lastIndex--);
                        }
                    } else {
                        il.EmitLdloc(lastIndex);
                    }
                }
            }
            
            il.Emit(OpCodes.Call, src);

            if(modifyArg != null) {
                // Unpack returned args
                if(modifyArg.Index < 0) {
                    int tupleIndex = ctx.Body.Variables.Count;
                    var tupleType = ctx.Import(src).ReturnType.Resolve();
                    ctx.Body.Variables.Add(new VariableDefinition(tupleType));
                    il.EmitStloc(tupleIndex);
                    il.EmitLdloc(tupleIndex);

                    //int localIndex = tupleIndex - 1;
                    //for (int i = 0; i < callParamCount; i++) {
                    //    il.EmitLdloc(tupleIndex);

                    //    // Go through all the necessary layers of nested tuples
                    //    int fieldID = i;
                    //    for (; fieldID >= 7; fieldID -= 7) {
                    //        // Keep getting tuple.Rest
                    //        var restField = tupleType.FindField("Rest");
                    //        il.Emit(OpCodes.Ldfld, restField);
                    //        tupleType = restField.FieldType.Resolve();
                    //    }

                    //    // Load the actual field
                    //    il.Emit(OpCodes.Ldfld, tupleType.FindField("Item" + (fieldID + 1)).Resolve());
                    //}
                } else { // Restore later args from local variables

                }
            }
        }

        public static void InjectDirect(ILContext ctx, MethodBase src, Instruction injectPoint, InjectAttribute inject) {
            int localOffset = ctx.Body.Variables.Count;
            var srcBody = ctx.Import(src).Resolve().Body;

            if(inject.InjectAfter) {
                injectPoint = injectPoint.Next;
            }

            // Copy over locals
            foreach (var v in srcBody.Variables) {
                ctx.Body.Variables.Add(new VariableDefinition(v.VariableType));
            }

            foreach (var ins in srcBody.Instructions) {
                if (ins.MatchRet()) {
                    ins.OpCode = OpCodes.Br;
                    ins.Operand = injectPoint;
                } else if (ins.MatchLdloc(out int local)) {
                    local += localOffset;
                    if (local <= 3) {
                        ins.OpCode = local switch {
                            0 => OpCodes.Ldloc_0,
                            1 => OpCodes.Ldloc_1,
                            2 => OpCodes.Ldloc_2,
                            _ => OpCodes.Ldloc_3
                        };
                    } else {
                        ins.OpCode = local <= byte.MaxValue ? OpCodes.Ldloc_S : OpCodes.Ldloc;
                        ins.Operand = local;
                    }
                } else if (ins.MatchStloc(out local)) {
                    local += localOffset;
                    if (local <= 3) {
                        ins.OpCode = local switch {
                            0 => OpCodes.Stloc_0,
                            1 => OpCodes.Stloc_1,
                            2 => OpCodes.Stloc_2,
                            _ => OpCodes.Stloc_3
                        };
                    } else {
                        ins.OpCode = local <= byte.MaxValue ? OpCodes.Stloc_S : OpCodes.Stloc;
                        ins.Operand = local;
                    }
                }

                // Leave off the final ret because it's an extra instruction we don't need
                if (ins.Next != null) {
                    ctx.IL.InsertBefore(injectPoint, ins);
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
