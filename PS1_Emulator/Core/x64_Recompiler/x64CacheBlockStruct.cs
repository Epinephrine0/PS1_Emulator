using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
namespace PSXEmulator.Core.x64_Recompiler {

    [StructLayout(LayoutKind.Sequential)]
    public struct x64CacheBlocksStruct {
        public InlineRAMCacheArray<x64CacheBlockInternalStruct> RAM_CacheBlocks;
        public InlineBIOSCacheArray<x64CacheBlockInternalStruct> BIOS_CacheBlocks;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct x64CacheBlockInternalStruct {
        public uint Address;                    //Offset = 00
        public uint IsCompiled;                 //Offset = 04
        public uint TotalMIPS_Instructions;     //Offset = 08
        public uint TotalCycles;                //Offset = 12
        public uint MIPS_Checksum;              //Offset = 16
        public int SizeOfAllocatedBytes;        //Offset = 20
        public ulong FunctionPointer;           //Offset = 24     delegate* unmanaged[Stdcall]<void>
    }

    [StructLayout(LayoutKind.Sequential)]
    [InlineArray((int)(CPU_x64_Recompiler.BIOS_SIZE >> 2))]
    public struct InlineBIOSCacheArray<T> {
        private T _e0;
    }

    [StructLayout(LayoutKind.Sequential)]
    [InlineArray((int)(CPU_x64_Recompiler.RAM_SIZE >> 2))]
    public struct InlineRAMCacheArray<T> {
        private T _e0;
    }
}
