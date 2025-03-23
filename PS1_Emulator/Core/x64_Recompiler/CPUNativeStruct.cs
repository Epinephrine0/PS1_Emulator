using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PSXEmulator.Core.x64_Recompiler {

        [StructLayout(LayoutKind.Sequential)]
        public struct CPUNativeStruct {
            public InlineArray32<uint> GPR;             //Offset = [000]
            public uint PC;                             //Offset = [128]
            public uint Next_PC;                        //Offset = [132]
            public uint Current_PC;                     //Offset = [136]
            public uint HI;                             //Offset = [140]
            public uint LO;                             //Offset = [144]
            public uint ReadyRegisterLoad_Number;       //Offset = [148]
            public uint ReadyRegisterLoad_Value;        //Offset = [152]
            public uint DelayedRegisterLoad_Number;     //Offset = [156]
            public uint DelayedRegisterLoad_Value;      //Offset = [160]
            public uint DirectWrite_Number;             //Offset = [164]
            public uint DirectWrite_Value;              //Offset = [168]
            public uint Branch;                         //Offset = [172]
            public uint DelaySlot;                      //Offset = [176]
            public uint COP0_SR;                        //Offset = [180]
            public uint COP0_Cause;                     //Offset = [184]
            public uint COP0_EPC;                       //Offset = [188]
        }

        [InlineArray(32)]
        public struct InlineArray32<T> {
            private T _e0;
        }
    }
