using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace MonoMixins {
    //public static InjectionTarget Field, Constant;

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public abstract class InjectAttribute : Attribute {
        internal MethodInfo Method { get; set; }

        public bool AsDelegate { get; set; } = true;

        public virtual bool InjectAfter { get; set; }

        // Whether the method should accept the arguments of the patched method
        public bool TakeArguments { get; set; }

        public InjectAttribute(Type type, string method) {
            // https://github.com/MonoMod/MonoMod/blob/34fa90162b0bc2a3317e9bd846a60bcbbe5c8205/MonoMod/MonoModder.cs#L855C70-L856
            Method = type.GetMethods().FirstOrDefault(m => m.GetID(withType: false) == method || m.GetID() == method || m.GetID(simple: true, withType: false) == method);
        }

        public abstract List<Instruction> FindTargets(ILContext ctx);

    }

    public class InjectHeadAttribute : InjectAttribute {
        public InjectHeadAttribute(Type type, string method) : base(type, method) {
            
        }
        
        public override List<Instruction> FindTargets(ILContext ctx) {
            return new List<Instruction>() { ctx.Instrs[0] };
        }
    }

    public class InjectTailAttribute : InjectAttribute {
        public InjectTailAttribute(Type type, string method) : base(type, method) {

        }
        
        public override List<Instruction> FindTargets(ILContext ctx) {
            return new List<Instruction>() { ctx.Instrs.Last() };
        }
    }

    public class InjectNewAttribute : InjectInstructionAttribute {

        public string TargetConstructor { get; set; }

        public InjectNewAttribute(Type type, string method, string targetCtor) : base(type, method) {
            TargetConstructor = targetCtor;
        }

        protected override bool MatchInstruction(Instruction ins) {
            var method = ins.Operand as MethodReference;
            return ins.OpCode == OpCodes.Newobj && (method.FullName == TargetConstructor || method.Name == TargetConstructor || $"{method.DeclaringType}::{method.Name}" == TargetConstructor);
        }
    }

    public class InjectCallAttribute : InjectInstructionAttribute {

        public string TargetMethod { get; set; }

        public InjectCallAttribute(Type type, string method, string targetMethod) : base(type, method) {
            TargetMethod = targetMethod;
        }

        protected override bool MatchInstruction(Instruction ins) {
            var method = ins.Operand as MethodReference;
            return (ins.OpCode == OpCodes.Call || ins.OpCode == OpCodes.Callvirt) &&
                (method.FullName == TargetMethod || method.Name == TargetMethod || $"{method.DeclaringType}::{method.Name}" == TargetMethod);
        }
    }

    public class InjectInstructionAttribute : InjectAttribute {
        // Limit the number of matches that can be made
        public int Limit { get; set; } = -1;

        // Which index of match to start injecting at
        public int Ordinal { get; set; } = 0;

        public Func<Instruction, bool> TargetPredicate { get; set; }

        public InjectInstructionAttribute(Type type, string method, Func<Instruction, bool> targetPredicate = null) : base(type, method) {
            TargetPredicate = targetPredicate;
        }

        public InjectInstructionAttribute(Type type, string method, string opcode) : this(type, method, i => i.OpCode.Name == opcode) {
            
        }

        public InjectInstructionAttribute(Type type, string method, string opcode, object operand) : this(type, method, i => i.OpCode.Name == opcode && i.Operand == operand) {
            
        }

        protected virtual bool MatchInstruction(Instruction ins) {
            return TargetPredicate?.Invoke(ins) ?? false;
        }

        public override List<Instruction> FindTargets(ILContext ctx) {
            var il = new ILCursor(ctx);
            var targets = new List<Instruction>();

            int count = 0;
            while ((Limit < 0 || count < Limit + Ordinal) && il.TryGotoNext(MatchInstruction)) {
                if (count >= Ordinal) {
                    targets.Add(il.Next);
                }
                count++;
            }

            return targets;
        }
    }

    public class InjectFieldAccess : InjectInstructionAttribute {
        // Leave null to match all field accesses
        public string OpCode { get; set; } = null;

        public string TargetField { get; set; }
        
        public InjectFieldAccess(Type type, string method, string field) : base(type, method) {
            TargetField = field;
        }

        protected override bool MatchInstruction(Instruction ins) {
            if (OpCode == ins.OpCode.Name || (OpCode == null && (ins.OpCode == OpCodes.Ldfld || ins.OpCode == OpCodes.Stfld
                || ins.OpCode == OpCodes.Ldsfld || ins.OpCode == OpCodes.Stsfld || ins.OpCode == OpCodes.Ldflda || ins.OpCode == OpCodes.Ldsflda))) {
                
                return ins.Operand is FieldReference field && field.FullName == TargetField;
            }
            
            return false;
        }
    }
}
