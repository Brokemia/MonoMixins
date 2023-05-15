using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MonoMixins {
    public static class Test {
        //[InjectInstruction(typeof(Test), "hello", "call")]
        [ModifyArg(typeof(Test), "hello", "System.Void System.Console::WriteLine(System.String)", Index = -1, TakeArguments = true)]
        public static ValueTuple<string> hi(int i, string s) {
            return ValueTuple.Create("hi " + i);
        }

        public static void hello(int i) {
            Console.WriteLine("hello " + i/* + (1, 2).Item1*/);
        }

        public static bool MatchArg(Instruction ins) {
            return true;
        }

        public static void callManyParameters() {
            manyParameters(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13);
        }

        public static void manyParameters(int a, int b, int c, int d, int e, int f, int g, int h, int i, int j, int k, int l, int m, int n) {
            
        }

        public static void testNew(int j) {
            string s = new string('a', 10);
            Console.WriteLine("test");
            //Console.WriteLine(s + j);
        }
    }
}
