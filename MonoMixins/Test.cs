using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MonoMixins {
    public static class Test {
        //[InjectInstruction(typeof(Test), "hello", "call")]

        //[ModifyArg(typeof(Test), "hello", "System.Void System.Console::WriteLine(System.String)", Index = 0, TakeArguments = true)]
        //public static void hi(int i, ref string s) {
        //    s = "hi " + i;
        //}

        [InjectInstruction(typeof(Test), "hello", "call", AsDelegate = false)]
        public static void hi() {
            int j = 74;
            Console.WriteLine("hi" + j);
        }

        public static void hello(int i) {
            int j = 3;
            Console.WriteLine("hello " + i);
        }

        [ModifyArg(typeof(Test), "callManyParameters", "System.Void MonoMixins.Test::manyParameters(System.Int32,System.Int32,System.Int32,System.Int32,System.Int32,System.Int32,System.Int32,System.Int32,System.Int32,System.Int32,System.Int32,System.Int32,System.Int32,System.Int32)", Index = -1, TakeArguments = true, AsDelegate = false)]
        public static void modifyParams(ref int a, ref int b, ref int c, ref int d, ref int e, ref int f, ref int g, ref int h, ref int i, ref int j, ref int k, ref int l, ref int m, ref int n) {
            Console.WriteLine(c);
            c = 4673;
        }
        
        public static void callManyParameters() {
            manyParameters(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13);
        }

        public static void manyParameters(int a, int b, int c, int d, int e, int f, int g, int h, int i, int j, int k, int l, int m, int n) {
            Console.WriteLine(a + " " + b + " " + c + " " + d);
        }

        public static void testNew(int j) {
            string s = new string('a', 10);
            Console.WriteLine("test");
            //Console.WriteLine(s + j);
        }
    }
}
