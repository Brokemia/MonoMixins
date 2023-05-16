using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MonoMixins {
    public static class Test {

        static void Main(string[] args) {
            //CreateHook(Test.testNew, Test.hi);
            Mixin.Load(typeof(Test).Assembly);

            Console.WriteLine("\n\nhello() Output:\n");
            int i = 2;
            Test.hello(i);

            Console.WriteLine("\n\ncallManyParameters() Output:\n");
            Test.callManyParameters(7);
            Console.ReadKey();
        }

        //[InjectInstruction(typeof(Test), "hello", "call")]

        //[ModifyArg(typeof(Test), "hello", "System.Void System.Console::WriteLine(System.String)", Index = 0, TakeArguments = true)]
        //public static void hi(int i, ref string s) {
        //    s = "hi " + i;
        //}

        [InjectInstruction(typeof(Test), "hello", "call", AsDelegate = false, TakeArguments = true, CaptureLocals = new int[] { 0 })]
        public static void hi(int i, int j2) {
            int j = 74;
            j2 = 67;
            Console.WriteLine("hi" + j + " " + j2);
        }

        public static void hello(int i) {
            int j = 3;
            Console.WriteLine("hello " + i + " " + j);
        }

        [ModifyArg(typeof(Test), "callManyParameters", "MonoMixins.Test::manyParameters", Index = -1, TakeArguments = true, AsDelegate = false)]
        public static void modifyParams(int hi, ref string a, ref int b, ref string c, ref int d, ref int e, ref int f, ref int g, ref int h, ref int i, ref int j, ref int k, ref int l, ref int m, ref int n) {
            Console.WriteLine(c + " " + hi);
            c = "47213";
        }
        
        public static void callManyParameters(int hi) {
            manyParameters("0", 1, "2", 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13);
        }

        public static void manyParameters(string a, int b, string c, int d, int e, int f, int g, int h, int i, int j, int k, int l, int m, int n) {
            Console.WriteLine(a + " " + b + " " + c + " " + d);
        }

        public static void testNew(int j) {
            string s = new string('a', 10);
            Console.WriteLine("test");
            //Console.WriteLine(s + j);
        }
    }
}
