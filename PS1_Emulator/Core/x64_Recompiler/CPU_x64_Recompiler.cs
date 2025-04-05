using Iced.Intel;
using PSXEmulator.Core.MSIL_Recompiler;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Instruction = PSXEmulator.Core.Common.Instruction;
using Label = Iced.Intel.Label;

namespace PSXEmulator.Core.x64_Recompiler {
    public unsafe partial class CPU_x64_Recompiler : CPU, IDisposable {
        private bool disposed = false;
        
        public static CPUNativeStruct* CPU_Struct_Ptr;
        public const uint RESET_VECTOR = 0xBFC00000;
        public const uint BIOS_START = 0x1FC00000;      //Reset vector but masked
        public const uint BIOS_SIZE = 512 * 1024;        //512 KB
        public const uint RAM_SIZE = 2 * 1024 * 1024;    //2 MB

        const uint CYCLES_PER_SECOND = 33868800;
        const uint CYCLES_PER_FRAME = CYCLES_PER_SECOND / 60;

        double CyclesDone = 0;

        bool IsReadingFromBIOS => (CPU_Struct_Ptr->PC & 0x1FFFFFFF) >= BIOS_START;

        public static BUS BUS;
        public static GTE GTE;

        public static x64CacheBlock[] BIOS_CacheBlocks;
        public static x64CacheBlock[] RAM_CacheBlocks;
        public static x64CacheBlock CurrentBlock;
 
        public NativeMemoryManager MemoryManager;
        private static CPU_x64_Recompiler Instance;

        public static x64CacheBlocksStruct* x64CacheBlocksStructs;
        public static delegate* unmanaged[Stdcall]<uint> DispatcherPointer;

        bool IsLoadingEXE;
        string? EXEPath;

        HashSet<uint> r = new HashSet<uint>();

        private CPU_x64_Recompiler(bool isEXE, string? EXEPath, BUS bus) {           
            BUS = bus;
            GTE = new GTE();
            CurrentBlock = new x64CacheBlock();
            MemoryManager = NativeMemoryManager.GetOrCreateMemoryManager();
            IsLoadingEXE = isEXE;
            this.EXEPath = EXEPath;
            Reset();
            x64CacheBlocksStructs = MemoryManager.GetCacheBlocksStructPtr();
            DispatcherPointer = MemoryManager.CompileDispatcher();
        }

        public static CPU_x64_Recompiler GetOrCreateCPU(bool isEXE, string? EXEPath, BUS bus) {
            if (Instance == null) {
                Instance = new CPU_x64_Recompiler(isEXE, EXEPath, bus);
            }
            return Instance;
        }

        public void Reset() {
            MemoryManager.Reset();
            CPU_Struct_Ptr = MemoryManager.GetCPUNativeStructPtr();
            CPU_Struct_Ptr->PC = RESET_VECTOR;
            CPU_Struct_Ptr->Next_PC = RESET_VECTOR + 4;
            CPU_Struct_Ptr->HI = 0xDeadBeef;
            CPU_Struct_Ptr->LO = 0xDeadBeef;

            //Initialize JIT cache for BIOS region
            BIOS_CacheBlocks = new x64CacheBlock[BIOS_SIZE >> 2];
            for (int i = 0; i < BIOS_CacheBlocks.Length; i++) {
                BIOS_CacheBlocks[i] = new x64CacheBlock();
            }

            //Initialize JIT cache for RAM region
            RAM_CacheBlocks = new x64CacheBlock[RAM_SIZE >> 2];
            for (int i = 0; i < RAM_CacheBlocks.Length; i++) {
                RAM_CacheBlocks[i] = new x64CacheBlock();
            }
        }

        public int emu_cycle() {
            if (CPU_Struct_Ptr->PC == 0x80030000) {
                if (IsLoadingEXE) {
                    IsLoadingEXE = false;
                    loadTestRom(EXEPath);
                }
            }

            TTY(CPU_Struct_Ptr->PC);

            bool isBios = IsReadingFromBIOS;
            uint block = GetBlockAddress(CPU_Struct_Ptr->PC, isBios);

            if (NeedsRecompilation(block, isBios)) {
                Recompile(block, CPU_Struct_Ptr->PC, isBios);
            }

            return RunJIT(block, isBios);
        }

        bool InWaitLoop = false;

        private int RunJIT(uint block, bool isBios) {
            //Console.WriteLine("[JIT] Running: " + CPU_Struct_Ptr->PC.ToString("x"));

            x64CacheBlock[] currentCache;
            int totalCycles = 0;
            currentCache = isBios ? BIOS_CacheBlocks : RAM_CacheBlocks;

            currentCache[block].FunctionPointer();
            totalCycles = (int)(currentCache[block].TotalCycles + BUS.GetBusCycles());

            /*if (CPU_Struct_Ptr->PC == currentCache[block].Address && currentCache[block].TotalMIPS_Instructions <= 10) {
                if (IsWaitLoop(ref currentCache[block], isBios)) {
                    InWaitLoop = true;
                }
            }*/

            return totalCycles;
        }

        private bool IsWaitLoop(ref x64CacheBlock block, bool isBios) {
            ReadOnlySpan<byte> rawMemory;
            x64CacheBlock[] currentCache;
            int maskedAddress = (int)BUS.Mask(block.Address);

            if (isBios) {
                rawMemory = new ReadOnlySpan<byte>(BUS.BIOS.GetMemoryReference()).Slice((int)(maskedAddress - BIOS_START));
                currentCache = BIOS_CacheBlocks;
            } else {
                rawMemory = new ReadOnlySpan<byte>(BUS.RAM.GetMemoryReference()).Slice(maskedAddress);
                currentCache = RAM_CacheBlocks;
            }

            ReadOnlySpan<uint> instructionsSpan = MemoryMarshal.Cast<byte, uint>(rawMemory).Slice(0, (int)block.TotalMIPS_Instructions);
            Instruction instruction = new Instruction();

            int foundLoad = -1;
            uint loadTarget = 0;
        
            for (int i = 0; i < instructionsSpan.Length; i++) { 
                instruction.FullValue = instructionsSpan[i];
                uint op = instruction.GetOpcode();
                
                if (op >= 0x20 && op <= 0x26) {         //Memory loads
                    foundLoad = i;
                    loadTarget = instruction.Get_rt();
                }             
            }

            //.......

            return false;
        }

        private void Recompile(uint block, uint pc, bool isBios) {
            Instruction instruction = new Instruction();
            Assembler emitter = new Assembler(64);
            Label endOfBlock = emitter.CreateLabel();
            ReadOnlySpan<byte> rawMemory;
            x64CacheBlock[] currentCache;
            uint cyclesPerInstruction;
            int maskedAddress = (int)BUS.Mask(pc);
            bool end = false;
           
            if (isBios) {
                rawMemory = new ReadOnlySpan<byte>(BUS.BIOS.GetMemoryReference()).Slice((int)(maskedAddress - BIOS_START));
                currentCache = BIOS_CacheBlocks;
                cyclesPerInstruction = 22;
            } else {
                rawMemory = new ReadOnlySpan<byte>(BUS.RAM.GetMemoryReference()).Slice(maskedAddress);
                currentCache = RAM_CacheBlocks;
                cyclesPerInstruction = 2;
            }

            ReadOnlySpan<uint> instructionsSpan = MemoryMarshal.Cast<byte, uint>(rawMemory);
            CurrentBlock = currentCache[block];
            InitializeBlock(ref CurrentBlock, pc);

            int instructionIndex = 0;

            //Emit save regs on block entry
            x64_JIT.EmitSaveNonVolatileRegisters(emitter);

            for (;;) {
                instruction.FullValue = instructionsSpan[instructionIndex++];
                EmitInstruction(instruction, emitter);

                //We end the block if any of these conditions is true
                //Note that syscall and break are immediate exceptions and they don't have delay slot

                if (end || CurrentBlock.TotalMIPS_Instructions >= 127 || IsSyscallOrBreak(instruction)) {
                    CurrentBlock.TotalCycles = CurrentBlock.TotalMIPS_Instructions * cyclesPerInstruction;
                    x64_JIT.EmitRestoreNonVolatileRegisters(emitter);
                    x64_JIT.TerminateBlock(emitter, ref endOfBlock);
                    AssembleAndLinkPointer(emitter, ref endOfBlock, ref CurrentBlock);
                    currentCache[block] = CurrentBlock;
                    return;
                }

                //For jumps and branches, we set the flag such that the delay slot is also included
                if (IsJumpOrBranch(instruction)) {
                    end = true;
                }
            }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
        public static void RecompileInJIT(x64CacheBlockInternalStruct* block, uint* pcPtr) {
            uint pc = *pcPtr;
            Instruction instruction = new Instruction();
            Assembler emitter = new Assembler(64);
            Label endOfBlock = emitter.CreateLabel();
            ReadOnlySpan<byte> rawMemory;
            uint cyclesPerInstruction;
            int maskedAddress = (int)(pc & 0x1FFFFFFF);
            bool end = false;
            bool isBios = (maskedAddress) >= BIOS_START;

            if (isBios) {
                rawMemory = new ReadOnlySpan<byte>(BUS.BIOS.GetMemoryReference()).Slice((int)(maskedAddress - BIOS_START));
                cyclesPerInstruction = 22;
            } else {
                rawMemory = new ReadOnlySpan<byte>(BUS.RAM.GetMemoryReference()).Slice(maskedAddress);
                cyclesPerInstruction = 2;
            }

            ReadOnlySpan<uint> instructionsSpan = MemoryMarshal.Cast<byte, uint>(rawMemory);

            block->Address = pc;
            block->IsCompiled = 0;
            block->TotalMIPS_Instructions = 0;
            block->MIPS_Checksum = 0;

            int instructionIndex = 0;

            //Emit save regs on block entry
            //x64_JIT.EmitSaveNonVolatileRegisters(emitter);

            for (;;) {
                instruction.FullValue = instructionsSpan[instructionIndex++];
                EmitInstruction(instruction, emitter, block);

                //We end the block if any of these conditions is true
                //Note that syscall and break are immediate exceptions and they don't have delay slot

                if (end || block->TotalMIPS_Instructions >= 127 || IsSyscallOrBreak(instruction)) {
                    block->TotalCycles = block->TotalMIPS_Instructions * cyclesPerInstruction;
                    //x64_JIT.EmitRestoreNonVolatileRegisters(emitter);
                    x64_JIT.TerminateBlock(emitter, ref endOfBlock);
                    AssembleAndLinkPointer(emitter, ref endOfBlock, block);
                    return;
                }

                //For jumps and branches, we set the flag such that the delay slot is also included
                if (IsJumpOrBranch(instruction)) {
                    end = true;
                }
            }
        }

        public void EmitInstruction(Instruction instruction, Assembler emitter) {
            x64_JIT.EmitSavePC(emitter);
            x64_JIT.EmitBranchDelayHandler(emitter);

            //Don't compile NOPs
            if (instruction.FullValue != 0) {
                x64_LUT.MainLookUpTable[instruction.GetOpcode()](instruction, emitter);
            }

            x64_JIT.EmitRegisterTransfare(emitter);
            CurrentBlock.MIPS_Checksum += instruction.FullValue;
            CurrentBlock.TotalMIPS_Instructions++;
        }

        public static void EmitInstruction(Instruction instruction, Assembler emitter, x64CacheBlockInternalStruct* block) {
            x64_JIT.EmitSavePC(emitter);
            x64_JIT.EmitBranchDelayHandler(emitter);

            //Don't compile NOPs
            if (instruction.FullValue != 0) {
                x64_LUT.MainLookUpTable[instruction.GetOpcode()](instruction, emitter);
            }

            x64_JIT.EmitRegisterTransfare(emitter);
            block->MIPS_Checksum += instruction.FullValue;
            block->TotalMIPS_Instructions++;
        }

        public void InitializeBlock(ref x64CacheBlock block, uint address) {
            //block.FunctionPointer = null;
            //block.SizeOfAllocatedBytes = 0;
            block.Address = address;
            block.IsCompiled = false;
            block.TotalMIPS_Instructions = 0;
            block.MIPS_Checksum = 0;
        }

        public void AssembleAndLinkPointer(Assembler emitter, ref Label endOfBlockLabel, ref x64CacheBlock block) {
            MemoryStream stream = new MemoryStream();
            AssemblerResult result = emitter.Assemble(new StreamCodeWriter(stream), 0, BlockEncoderOptions.ReturnNewInstructionOffsets);

            //Trim the extra zeroes and the padding in the block by including only up to the ret instruction
            //This works as long as there is no call instruction with the address being passed as 64 bit immediate
            //Otherwise, the address will be inserted at the end of the block and we need to include it in the span
            int endOfBlockIndex = (int)result.GetLabelRIP(endOfBlockLabel);
            Span<byte> emittedCode = new Span<byte>(stream.GetBuffer()).Slice(0, endOfBlockIndex);

            //Pass the old pointer and size. We need them for best fit allocation of next blocks
            block.FunctionPointer = MemoryManager.WriteExecutableBlock(ref emittedCode, (byte*)block.FunctionPointer, block.SizeOfAllocatedBytes);
            block.SizeOfAllocatedBytes = emittedCode.Length;      //Update the size to the new one
            block.IsCompiled = true;
        }

        public static void AssembleAndLinkPointer(Assembler emitter, ref Label endOfBlockLabel, x64CacheBlockInternalStruct* block) {
            MemoryStream stream = new MemoryStream();
            AssemblerResult result = emitter.Assemble(new StreamCodeWriter(stream), 0, BlockEncoderOptions.ReturnNewInstructionOffsets);

            //Trim the extra zeroes and the padding in the block by including only up to the ret instruction
            //This works as long as there is no call instruction with the address being passed as 64 bit immediate
            //Otherwise, the address will be inserted at the end of the block and we need to include it in the span
            int endOfBlockIndex = (int)result.GetLabelRIP(endOfBlockLabel);
            Span<byte> emittedCode = new Span<byte>(stream.GetBuffer()).Slice(0, endOfBlockIndex);

            //Pass the old pointer and size. We need them for best fit allocation of next blocks
            NativeMemoryManager manager = NativeMemoryManager.GetOrCreateMemoryManager();           //Get the instance, or make the instance static
            block->FunctionPointer = (ulong)manager.WriteExecutableBlock(ref emittedCode, (byte*)block->FunctionPointer, block->SizeOfAllocatedBytes);
            block->SizeOfAllocatedBytes = emittedCode.Length;      //Update the size to the new one
            block->IsCompiled = 1;
        }

        private bool NeedsRecompilation(uint block, bool isBios) {
            if (isBios) {
                return !BIOS_CacheBlocks[block].IsCompiled;    //Not need to invalidate the content as the BIOS is not writable
            } else {
                return (!RAM_CacheBlocks[block].IsCompiled) || (!InvalidateRAM_Block(block));
            }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
        public static uint NeedsRecompilation(x64CacheBlockInternalStruct* block, uint* pcPtr) {
            /*bool isBios = ((*pcPtr) & 0x1FFFFFFF) >= BIOS_START;
            if (isBios) {
                return (uint)(block->IsCompiled == 1? 0 : 1); 
            } else {
                if (block->IsCompiled == 1) {
                    uint valid = InvalidateRAM_Block(block);
                    return (uint)(valid == 1? 0 : 1);
                } else {
                    return 1;
                }
            }*/
            return 0;
        }

        private uint GetBlockAddress(uint address, bool biosBlock) {
            address = BUS.Mask(address);
            if (biosBlock) {
                address -= BIOS_START;
            } else {
                address &= ((1 << 21) - 1); // % 2MB 
            }
            return address >> 2;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
        public static ulong GetNativeBlock(uint* pcPtr) {
            uint address = (*pcPtr) & 0x1FFFFFFF;
            bool biosBlock = (address) >= BIOS_START;

            if (biosBlock) {
                address -= BIOS_START;
                address >>= 2;
                return (ulong) &x64CacheBlocksStructs->BIOS_CacheBlocks[(int)address];
            } else {
                address &= ((1 << 21) - 1); // % 2MB 
                address >>= 2;
                return (ulong)&x64CacheBlocksStructs->RAM_CacheBlocks[(int)address];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private bool InvalidateRAM_Block(uint block) {  //For RAM Blocks only
            uint address = BUS.Mask(RAM_CacheBlocks[block].Address);
            uint numberOfInstructions = RAM_CacheBlocks[block].TotalMIPS_Instructions;
            ReadOnlySpan<byte> rawMemory = new ReadOnlySpan<byte>(BUS.RAM.GetMemoryReference()).Slice((int)address, (int)(numberOfInstructions * 4));
            ReadOnlySpan<uint> instructionsSpan = MemoryMarshal.Cast<byte, uint> (rawMemory);

            uint memoryChecksum = 0;

            for (int i = 0; i < instructionsSpan.Length; i++) {
                memoryChecksum += instructionsSpan[i];
            }

            bool isValid = RAM_CacheBlocks[block].MIPS_Checksum == memoryChecksum;  
            return isValid;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static uint InvalidateRAM_Block(x64CacheBlockInternalStruct* block) {  //For RAM Blocks only
            uint address = (block->Address) & 0x1FFFFFFF;
            uint numberOfInstructions = block->TotalMIPS_Instructions;
            ReadOnlySpan<byte> rawMemory = new ReadOnlySpan<byte>(BUS.RAM.GetMemoryReference()).Slice((int)address, (int)(numberOfInstructions << 2));
            ReadOnlySpan<uint> instructionsSpan = MemoryMarshal.Cast<byte, uint>(rawMemory);

            uint memoryChecksum = 0;

            for (int i = 0; i < instructionsSpan.Length; i++) {
                memoryChecksum += instructionsSpan[i];
            }

            bool isValid = block->MIPS_Checksum == memoryChecksum;
            return (uint)(isValid ? 1 : 0);
        }

        private static bool IsJumpOrBranch(Instruction instruction) {
            uint op = instruction.GetOpcode();
            if (op == 0) {
                uint sub = instruction.Get_Subfunction();
                return sub == 0x8 || sub == 0x9;     //JR, JALR,
            } else {
                return op >= 1 && op <= 7;            //BXX, J, JAL, BEQ, BNE, BLEZ, BGTZ 
            }
        }

        private static bool IsSyscallOrBreak(Instruction instruction) {
            uint op = instruction.GetOpcode();
            if (op == 0) {
                uint sub = instruction.Get_Subfunction();
                return sub == 0xC || sub == 0xD;     //Syscall, Break
            }
            return false;
        }

        private static int IRQCheck(CPU_x64_Recompiler cpu) {
            if (IRQ_CONTROL.isRequestingIRQ()) {  //Interrupt check 
                CPU_Struct_Ptr->COP0_Cause |= (1 << 10);
                uint sr = CPU_Struct_Ptr->COP0_SR;

                //Skip IRQs if the current instruction is a GTE instruction to avoid the BIOS skipping it
                if (((sr & 1) != 0) && (((sr >> 10) & 1) != 0) && !InstructionIsGTE(cpu)) {
                    Exception(CPU_Struct_Ptr, (uint)CPU.Exceptions.IRQ);
                    return 1;
                }
            }
            return 0;
        }

        public static void Exception(CPUNativeStruct* cpuStruct, uint exceptionCause) {
            //If the next instruction is a GTE instruction skip the exception
            //Otherwise the BIOS will try to handle the GTE bug by skipping the instruction  
            uint handler;                                         //Get the handler

            if ((cpuStruct->COP0_SR & (1 << 22)) != 0) {
                handler = 0xbfc00180;
            } else {
                handler = 0x80000080;
            }

            uint mode = cpuStruct->COP0_SR & 0x3f;                     //Disable interrupts 

            cpuStruct->COP0_SR = (uint)(cpuStruct->COP0_SR & ~0x3f);
            cpuStruct->COP0_SR = cpuStruct->COP0_SR | ((mode << 2) & 0x3f);
            cpuStruct->COP0_Cause = exceptionCause << 2;               //Update cause register

            //Small hack: if IRQ happens step the branch delay to avoid having the handler pointing 
            //to the previous instruction which is the delay slot instruction
            //Note: when we leave JIT we are (almost always) in a delay slot
            if (exceptionCause == (int)CPU.Exceptions.IRQ) {
                cpuStruct->COP0_EPC = cpuStruct->PC;           //Save the PC in register EPC
                cpuStruct->DelaySlot = cpuStruct->Branch;
            } else {
                cpuStruct->COP0_EPC = cpuStruct->Current_PC;   //Save the current PC in register EPC
            }

            if (cpuStruct->DelaySlot == 1) {                            //In case an exception occurs in a delay slot
                cpuStruct->COP0_EPC -= 4;
                cpuStruct->COP0_Cause = (uint)(cpuStruct->COP0_Cause | (1 << 31));
            }

            cpuStruct->PC = handler;                                   //Jump to the handler address (no delay)
            cpuStruct->Next_PC = cpuStruct->PC + 4;
        }

        public void TickFrame() {
            /*for (int i = 0; i < CYCLES_PER_FRAME;) {
                int add = emu_cycle();
                i += add;
                BUS.Tick(add);
                IRQCheck(this);

                *//*if (InWaitLoop) {
                    while (!IRQ_CONTROL.isRequestingIRQ()) {
                        BUS.Tick(add);
                        IRQCheck(this);
                    }
                    InWaitLoop = false;
                }*//*
            }*/

            DispatcherPointer();
            CyclesDone += CYCLES_PER_FRAME;
        }

        public ref BUS GetBUS() {
            return ref BUS;
        }

        private void loadTestRom(string? path) {
            byte[] EXE = File.ReadAllBytes(path);

            //Copy the EXE data to memory
            uint addressInRAM = (uint)(EXE[0x018] | (EXE[0x018 + 1] << 8) | (EXE[0x018 + 2] << 16) | (EXE[0x018 + 3] << 24));

            for (int i = 0x800; i < EXE.Length; i++) {
                BUS.StoreByte(addressInRAM, EXE[i]);
                addressInRAM++;
            }

            //Set up SP, FP, and GP
            uint baseStackAndFrameAddress = (uint)(EXE[0x30] | (EXE[0x30 + 1] << 8) | (EXE[0x30 + 2] << 16) | (EXE[0x30 + 3] << 24));

            if (baseStackAndFrameAddress != 0) {
                uint stackAndFrameOffset = (uint)(EXE[0x34] | (EXE[0x34 + 1] << 8) | (EXE[0x34 + 2] << 16) | (EXE[0x34 + 3] << 24));
              CPU_Struct_Ptr->GPR[(int)CPU.Register.sp] = CPU_Struct_Ptr->GPR[(int)CPU.Register.fp] = baseStackAndFrameAddress + stackAndFrameOffset;
            }

            CPU_Struct_Ptr->GPR[(int)CPU.Register.gp] = (uint)(EXE[0x14] | (EXE[0x14 + 1] << 8) | (EXE[0x14 + 2] << 16) | (EXE[0x14 + 3] << 24));

            //Jump to the address specified by the EXE
            CPU_Struct_Ptr->Current_PC = CPU_Struct_Ptr->PC = (uint)(EXE[0x10] | (EXE[0x10 + 1] << 8) | (EXE[0x10 + 2] << 16) | (EXE[0x10 + 3] << 24));
            CPU_Struct_Ptr->Next_PC = CPU_Struct_Ptr->PC + 4;
        }
      
        private void TTY(uint pc) {

            switch (pc) {
                case 0xA0:      //Intercepting prints to the TTY Console and printing it in console 
                    char character;

                    switch (CPU_Struct_Ptr->GPR[9]) {
                        case 0x3C:                       //putchar function (Prints the char in $a0)
                            character = (char)CPU_Struct_Ptr->GPR[4];
                            Console.Write(character);
                            break;

                        case 0x3E:                        //puts function, similar to printf but differ in dealing with 0 character
                            uint address = CPU_Struct_Ptr->GPR[4];       //address of the string is in $a0
                            if (address == 0) {
                                Console.Write("\\<NULL>");
                            } else {
                                while (BUS.LoadByte(address) != 0) {
                                    character = (char)BUS.LoadByte(address);
                                    Console.Write(character);
                                    address++;
                                }

                            }
                            break;
                    }
                    break;

                case 0xB0:
                    switch (CPU_Struct_Ptr->GPR[9]) {
                        case 0x3D:                       //putchar function (Prints the char in $a0)
                            character = (char)CPU_Struct_Ptr->GPR[4];            
                            Console.Write(character);                            
                            break;

                        case 0x3F:                                       //puts function, similar to printf but differ in dealing with 0 character
                            uint address = CPU_Struct_Ptr->GPR[4];       //address of the string is in $a0
                            if (address == 0) {
                                Console.Write("\\<NULL>");
                            } else {
                                while (BUS.LoadByte(address) != 0) {
                                    character = (char)BUS.LoadByte(address);
                                    Console.Write(character);
                                    address++;
                                }
                            }
                            break;
                    }
                    break;
            }
        }

        private static bool InstructionIsGTE(CPU_x64_Recompiler cpu) {
            return false; //Does not work with current JIT
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
        public static void BUSTickWrapper(int cycles) {
             BUS.Tick((int)(cycles + BUS.GetBusCycles()));
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
        public static void CheckIRQInJIT() {
            if (IRQ_CONTROL.isRequestingIRQ()) {  //Interrupt check 
                CPU_Struct_Ptr->COP0_Cause |= (1 << 10);
                uint sr = CPU_Struct_Ptr->COP0_SR;

                //Skip IRQs if the current instruction is a GTE instruction to avoid the BIOS skipping it
                if (((sr & 1) != 0) && (((sr >> 10) & 1) != 0)) {
                    Exception(CPU_Struct_Ptr, (uint)CPU.Exceptions.IRQ);
                }
            }
        }
        
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
        public static byte BUSReadByteWrapper(uint address) {
            return BUS.LoadByte(address);
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
        public static ushort BUSReadHalfWrapper(uint address) {
            return BUS.LoadHalf(address);
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
        public static uint BUSReadWordWrapper(uint address) {
            return BUS.LoadWord(address);
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
        public static void BUSWriteByteWrapper(uint address, byte value) {
            BUS.StoreByte(address, value);
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
        public static void BUSWriteHalfWrapper(uint address, ushort value) {
            BUS.StoreHalf(address, value);
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
        public static void BUSWriteWordWrapper(uint address, uint value) {
            BUS.StoreWord(address, value);
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
        public static uint GTEReadWrapper(uint rd) {
            return GTE.read(rd);
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
        public static void GTEWriteWrapper(uint rd, uint value) {
            GTE.write(rd, value);
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
        public static void GTEExecuteWrapper(uint value) {
            GTE.execute(value);
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
        public static void ExceptionWrapper(CPUNativeStruct* cpuStruct, uint cause) {
            Exception(cpuStruct, cause);
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
        public static void Print(uint val) {
            Console.WriteLine("[X64 Debug] " + val.ToString("x"));
        }

        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing) {
            if (!disposed) {
                if (disposing) {
                    //Free managed objects
                    //Memory manager will handle freeing CPU_Struct_Ptr and the executable memory
                    MemoryManager.Dispose();

                    MemoryManager = null;
                    CurrentBlock.FunctionPointer = null;

                    foreach (x64CacheBlock block in BIOS_CacheBlocks) {
                        block.FunctionPointer = null;
                    }

                    foreach (x64CacheBlock block in RAM_CacheBlocks) {
                        block.FunctionPointer = null;
                    }

                    BIOS_CacheBlocks = null;
                    RAM_CacheBlocks = null;
                    GTE = null;
                    Instance = null;
                }

                //Free unmanaged objects
                CPU_Struct_Ptr = null;              //We should not call NativeMemory.Free() here.
                disposed = true;
            }
        }

        //Sampled every second by timer
        public double GetSpeed() {
            double returnValue = (CyclesDone / CYCLES_PER_SECOND) * 100;
            CyclesDone = 0;
            return returnValue;
        }

        ~CPU_x64_Recompiler() {
            Dispose(false);
        }
    }
}
