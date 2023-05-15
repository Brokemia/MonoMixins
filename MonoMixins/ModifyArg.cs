using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MonoMixins {
    public class ModifyArgAttribute : InjectCallAttribute {
        // Which argument to modify, set to -1 for all arguments
        public int Index { get; set; }

        // Always inject before or this just doesn't work
        public override bool InjectAfter { get => false; set => base.InjectAfter = value; }

        public ModifyArgAttribute(Type type, string method, string targetMethod) : base(type, method, targetMethod) {
        }
    }
}
