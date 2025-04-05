using System;
using static PSXEmulator.Core.x64_Recompiler.CPU_x64_Recompiler;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace PSXEmulator.Core.x64_Recompiler {
    public unsafe class NativeMemoryManager : IDisposable {
        private bool disposedValue;     //Needed for disposing pattern

        //Import needed kernel functions
        [DllImport("kernel32.dll")]
        private static extern void* VirtualAlloc(void* addr, int size, int type, int protect);

        [DllImport("kernel32.dll")]
        private static extern bool VirtualProtect(void* addr, int size, int new_protect, int* old_protect);

        [DllImport("kernel32.dll")]
        private static extern bool VirtualFree(void* addr, int size, int type);

        private const int PAGE_EXECUTE_READWRITE = 0x40;
        private const int MEM_COMMIT = 0x00001000;
        private const int MEM_RELEASE = 0x00008000;

        private const int SIZE_OF_EXECUTABLE_MEMORY = 64 * 1024 * 1024; //64MB

        private static byte* ExecutableMemoryBase;
        private byte* AddressOfNextBlock;
        private CPUNativeStruct* CPU_Struct_Ptr;
        private x64CacheBlocksStruct* x64CacheBlocksStructs;

        private static NativeMemoryManager Instance;

        //Keeps a list of invalid blocks that were allocated to a new memory
        //to use their old space
        private List<(ulong address, int size)> InvalidBlocks;
        private int InvalidBlocksTotalSize = 0;

        //Precompiled RegisterTransfare Function
        public static void* RegisterTransfare;
        private int RegisterTransfareSize;

        //Function poitner to emitted dispatcher
        public static void* Dispatcher;
        private int DispatcherSize;

        private NativeMemoryManager() {
            //Allocate 64MB of executable memory
            ExecutableMemoryBase = (byte*)VirtualAlloc(null, SIZE_OF_EXECUTABLE_MEMORY, MEM_COMMIT, PAGE_EXECUTE_READWRITE);
            AddressOfNextBlock = ExecutableMemoryBase;

            //Allocate memory for the main cpu struct in the unmanaged heap for the native code to read/write
            CPU_Struct_Ptr = (CPUNativeStruct*)NativeMemory.AllocZeroed((nuint)sizeof(CPUNativeStruct));
            x64CacheBlocksStructs = (x64CacheBlocksStruct*)NativeMemory.AllocZeroed((nuint)sizeof(x64CacheBlocksStruct));

            InvalidBlocks = new List<(ulong address, int size)>();

            Console.WriteLine("[NativeMemoryManager] Memory Allocated");
        }

        public void Reset() {
            NativeMemory.Clear(CPU_Struct_Ptr, (nuint)sizeof(CPUNativeStruct));
            NativeMemory.Clear(x64CacheBlocksStructs, (nuint)sizeof(x64CacheBlocksStruct));
            NativeMemory.Clear(ExecutableMemoryBase, SIZE_OF_EXECUTABLE_MEMORY);
            AddressOfNextBlock = ExecutableMemoryBase;
            Console.WriteLine("[NativeMemoryManager] Memory Cleared");
        }

        public static NativeMemoryManager GetOrCreateMemoryManager() {
            if (Instance == null) {
                Instance = new NativeMemoryManager();
            }
            return Instance;
        }

        public CPUNativeStruct* GetCPUNativeStructPtr() {
            return CPU_Struct_Ptr;
        }

        public x64CacheBlocksStruct* GetCacheBlocksStructPtr() {
            return x64CacheBlocksStructs;
        }

        public void PrecompileRegisterTransfare() {
            Span<byte> emittedCode = x64_JIT.PrecompileRegisterTransfare();
            RegisterTransfareSize = emittedCode.Length;
            RegisterTransfare = VirtualAlloc(null, RegisterTransfareSize, MEM_COMMIT, PAGE_EXECUTE_READWRITE);
            fixed (byte* blockPtr = &emittedCode[0]) {
                NativeMemory.Copy(blockPtr, RegisterTransfare, (nuint)emittedCode.Length);
            }
        }

        public delegate* unmanaged[Stdcall] <uint> CompileDispatcher() {
            Span<byte> emittedCode = x64_JIT.EmitDispatcher();
            DispatcherSize = emittedCode.Length;
            Dispatcher = VirtualAlloc(null, DispatcherSize, MEM_COMMIT, PAGE_EXECUTE_READWRITE);
            fixed (byte* blockPtr = &emittedCode[0]) {
                NativeMemory.Copy(blockPtr, Dispatcher, (nuint)emittedCode.Length);
            }

            return (delegate* unmanaged[Stdcall]<uint>)Dispatcher;
        }

        public delegate* unmanaged[Stdcall]<void> WriteExecutableBlock(ref Span<byte> block, byte* oldPointer, int oldSize) {
            delegate* unmanaged[Stdcall] <void> function;

            //Check If we can replace it with another invalid block 
            byte* possibleFit = BestFit(block.Length);
            if (possibleFit != null) {
                fixed (byte* blockPtr = &block[0]) {
                    NativeMemory.Copy(blockPtr, possibleFit, (nuint)block.Length);
                }

                //Cast to delegate*
                return (delegate* unmanaged[Stdcall]<void>)possibleFit;
            }

            //Otherwise we copy to a new part of the memory and increment the pointer
            if (!HasEnoughMemory(block.Length)) {
                //Easiest solution: nuke Everything and start over
                //No need to call NativeMemory.Clear, we just reset the pointers and unlink the blocks.
                AddressOfNextBlock = ExecutableMemoryBase;
                UnlinkAllBlocks(ref BIOS_CacheBlocks);
                UnlinkAllBlocks(ref RAM_CacheBlocks);
                InvalidBlocks.Clear();
                Console.WriteLine("[NativeMemoryManager] Memory Resetted!");
            }
          
            //Copy code to the next block address
            //We need to fix the pointer to managed memory
            fixed (byte* blockPtr = &block[0]) {          
                NativeMemory.Copy(blockPtr, AddressOfNextBlock, (nuint)block.Length);
            }

            //Cast to delegate*
            function = (delegate* unmanaged[Stdcall]<void>)AddressOfNextBlock;

            //Update the address for the incoming blocks
            AddressOfNextBlock += block.Length;
            AddressOfNextBlock = Align(AddressOfNextBlock, 16);

            //If we use new part of the memory and there exists an old version of this block then mark it as free
            if (oldPointer != null && oldSize > 0) {
                InvalidBlocks.Add(((ulong)oldPointer, oldSize));
                InvalidBlocksTotalSize += oldSize;
            }

            return function;
        }


        private byte* Align(byte* address, ulong bytes) {
            ulong addressValue = (ulong)address;
            return (byte*)((addressValue + (bytes - 1)) & ~(bytes - 1));
        }

        private byte* BestFit(int size) {
            if (InvalidBlocks.Count > 0) {
                InvalidBlocks.Sort((a, b) => a.size.CompareTo(b.size)); //Sort by size (ascending)
                for (int i = 0; i < InvalidBlocks.Count; i++) {
                    if (InvalidBlocks[i].size >= size) {
                        ulong allocatedBlock = InvalidBlocks[i].address;

                        //If the block is larger, split it and keep the remainder
                        if (InvalidBlocks[i].size > size) {
                            InvalidBlocks[i] = (InvalidBlocks[i].address + (uint)size, InvalidBlocks[i].size - size);
                            InvalidBlocksTotalSize -= size;
                        } else {
                            //Exact fit, remove the block from the free list
                            InvalidBlocksTotalSize -= InvalidBlocks[i].size;
                            InvalidBlocks.RemoveAt(i);
                        }

                        return (byte*)allocatedBlock;
                    }
                }
            }
            return null;
        }

        private void UnlinkAllBlocks(ref x64CacheBlock[] cacheBlock) {
            foreach(x64CacheBlock block in cacheBlock) {
                if (block.IsCompiled) {
                    block.IsCompiled = false;
                    block.FunctionPointer = null;
                }
            }
        }

        public static void UnlinkRAMBlock(uint address) {   //Unused and Very bad
            foreach (x64CacheBlock block in RAM_CacheBlocks) {
                if (block.IsCompiled && address >= block.Address && address < (block.Address + (block.TotalMIPS_Instructions << 2))) {
                    block.IsCompiled = false;
                    block.FunctionPointer = null;
                }
            }
        }

        public bool HasEnoughMemory(int length) {
            //Console.WriteLine(((AddressOfNextBlock + length - ExecutableMemoryBase) / (1024*1024)));
            return SIZE_OF_EXECUTABLE_MEMORY > (AddressOfNextBlock + length - ExecutableMemoryBase);
        }

        protected virtual void Dispose(bool disposing) {
            if (!disposedValue) {
                if (disposing) {
                    // TODO: dispose managed state (managed objects)
                    Instance = null;
                }

                //Free unmanaged resources (unmanaged objects) and override finalizer
                VirtualFree(ExecutableMemoryBase, SIZE_OF_EXECUTABLE_MEMORY, MEM_RELEASE);  
                VirtualFree(RegisterTransfare, RegisterTransfareSize, MEM_RELEASE);
                VirtualFree(Dispatcher, DispatcherSize, MEM_RELEASE);

                NativeMemory.Free(CPU_Struct_Ptr);
                NativeMemory.Free(x64CacheBlocksStructs);
                ExecutableMemoryBase = null;
                AddressOfNextBlock = null;
                CPU_Struct_Ptr = null;
                x64CacheBlocksStructs = null;
                RegisterTransfare = null;

                disposedValue = true;
                Console.WriteLine("[NativeMemoryManager] Memory Freed Successfully!");
            }
        }

         ~NativeMemoryManager() {
             Dispose(false);
         }

        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
