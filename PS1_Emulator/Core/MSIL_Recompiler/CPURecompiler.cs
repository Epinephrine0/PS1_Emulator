﻿using PSXEmulator.Core;
using PSXEmulator.Core.Common;
using PSXEmulator.Core.Recompiler;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PSXEmulator {
    public unsafe partial class CPURecompiler : CPU {
        [assembly: AllowPartiallyTrustedCallers]
        [assembly: SecurityTransparent]
        [assembly: SecurityRules(SecurityRuleSet.Level2, SkipVerificationInFullTrust = true)]

        const uint CYCLES_PER_FRAME = 33868800 / 60;

        //BUS connects CPU to other peripherals
        public BUS BUS;

        //32-bit program counters
        public uint PC;
        public uint Next_PC;
        public uint Current_PC;

        //General Purpose Registers
        public uint[] GPR;

        //COP0
        public struct COP0 {
            public uint SR;         //R12 , the status register 
            public uint Cause;      //R13 , the cause register 
            public uint EPC;        //R14 , Return Address from Trap
        }

        public COP0 Cop0;

        public uint HI;           //Remainder of devision
        public uint LO;           //Quotient of devision


        //Geometry Transformation Engine - Coprocessor 2
        public GTE GTE = new GTE();

        //Flags to emulate branch delay 
        public bool Branch;
        public bool DelaySlot;

        //This is needed because writes to memory are ignored (well, not really) When cache is isolated
        public bool IscIsolateCache => (Cop0.SR & 0x10000) != 0;

        //Counting how many cycles to clock the other peripherals
        public static int Cycles = 0;

        private byte[] EXE;
        private bool IsLoadingEXE;
        private string? EXEPath;

        bool FastBoot = false;                  //Skips the boot animation 
        List<byte> Chars = new List<byte>();    //Temporarily stores characters 
      
        //To emulate load delay
        public struct RegisterLoad {
            public uint RegisterNumber;
            public uint Value;
        }

        public RegisterLoad ReadyRegisterLoad;
        public RegisterLoad DelayedRegisterLoad;
        public RegisterLoad DirectWrite;       //Not memory access, will overwrite memory loads

        Instruction CurrentInstruction = new Instruction();
        Instruction InterpreterInstruction = new Instruction();

        public CacheBlock[] BIOS_CacheBlocks;
        public CacheBlock[] RAM_CacheBlocks;
        public CacheBlock CurrentBlock = new CacheBlock();

        public const uint BIOS_START = 0x1FC00000;
        public const uint BIOS_SIZE = 512 * 1024;        //512 KB
        public const uint RAM_SIZE = 2 * 1024 * 1024;    //2 MB

        bool IsReadingFromBIOS => BUS.BIOS.range.Contains(BUS.Mask(PC));

        public CPURecompiler(bool isEXE, string? EXEPath, BUS bus) {
            PC = 0xbfc00000;                   //BIOS initial PC       
            Next_PC = PC + 4;
            BUS = bus;
            GPR = new uint[32];
            GPR[0] = 0;
            Cop0.SR = 0;
            DirectWrite.RegisterNumber = 0;    //Stupid load delay slot
            DirectWrite.Value = 0;
            ReadyRegisterLoad.RegisterNumber = 0;
            ReadyRegisterLoad.Value = 0;
            DelayedRegisterLoad.RegisterNumber = 0;
            DelayedRegisterLoad.Value = 0; 
            HI = 0xdeadbeef;
            LO = 0xdeadbeef;
            Branch = false;
            DelaySlot = false;
            IsLoadingEXE = isEXE;
            this.EXEPath = EXEPath;

            MSIL_JIT.PreCompileSavePC();
            MSIL_JIT.PreCompileDelayHandler();
            MSIL_JIT.PreCompileRT();

            // CacheBlocks = CacheManager.LoadCache(this);

            //Initialize JIT cache for BIOS region
            BIOS_CacheBlocks = new CacheBlock[BIOS_SIZE >> 2];
            for (int i = 0; i < BIOS_CacheBlocks.Length; i++) {
                BIOS_CacheBlocks[i] = new CacheBlock();
            }

            //Initialize JIT cache for RAM region
            RAM_CacheBlocks = new CacheBlock[RAM_SIZE >> 2];
            for (int i = 0; i < RAM_CacheBlocks.Length; i++) {
                RAM_CacheBlocks[i] = new CacheBlock();
            }
        }

        public int emu_cycle() {
            /*Intercept(PC);

            if (PC == 0x80030000) {
                if (IsLoadingEXE) {
                    IsLoadingEXE = false;
                    loadTestRom(EXEPath);
                    StartOfBlock = true;

                } else if (FastBoot && BUS.CDROM.DataController.Disk != null) {                   
                    Current_PC = PC = GPR[(int)Register.ra];
                    Next_PC = PC + 4;
                    ReadyRegisterLoad.Value = 0;
                    ReadyRegisterLoad.RegisterNumber = 0;
                    DelayedRegisterLoad = ReadyRegisterLoad;
                    StartOfBlock = true;
                    FastBoot = false;
                }
            }*/

            bool isBios = IsReadingFromBIOS;
            uint block = GetBlockAddress(PC, isBios);
            if (IsInvalidBlock(block, isBios)) {
                //Recompile
                Recompile(block, PC, isBios);
            }

            return RunJIT(block, isBios);
        }

        private int Recompile(uint block, uint pc, bool isBios) {
            Instruction instruction = new Instruction();
            
            uint temp = pc;
            bool end = false;
            CacheBlock[] currentCache = isBios ? BIOS_CacheBlocks : RAM_CacheBlocks;
            CurrentBlock = currentCache[block];
            CurrentBlock.Init(pc);

            for (;;) {
                instruction.FullValue = BUS.LoadWord(temp);
                temp += 4;
                EmitInstruction(instruction);

                //We end the block if any of these conditions is true
                //Note that syscall and break are immediate exceptions and they don't have delay slot
                if (end || CurrentBlock.Total >= 127 || IsSyscallOrBreak(instruction)) {
                    MSIL_JIT.EmitRet(CurrentBlock);
                    CurrentBlock.Compile();
                    currentCache[block] = CurrentBlock;
                    return (int)CurrentBlock.Total;
                }

                //For jumps and branches, we set the flag such that the delay slot is also included
                if (IsJumpOrBranch(instruction)) {
                    end = true;
                }
            }
        }

        private int RunJIT(uint block, bool isBios) {
            if (BUS.debug) {
                Console.WriteLine("[JIT]: " + PC.ToString("x"));
            }

            CacheBlock[] currentCache;
            int totalCycles = 0;
            int cycleMultiplier = 0;

            if (isBios) {
                currentCache = BIOS_CacheBlocks;
                cycleMultiplier = 22;
            } else {
                currentCache = RAM_CacheBlocks;
                cycleMultiplier = 2;
            }

            currentCache[block].FunctionPointer(this);
            totalCycles = (int)((currentCache[block].Total * cycleMultiplier) + BUS.GetBusCycles());

            return totalCycles;
        }

        public void EmitInstruction(Instruction instruction) {
            MSIL_JIT.EmitSavePC(CurrentBlock);
            MSIL_JIT.EmitBranchDelayHandler(CurrentBlock);

            //Don't compile NOPs
            if (instruction.FullValue != 0) {
                MSIL_LUT.MainLookUpTable[instruction.GetOpcode()](this, instruction);
            }

            CurrentBlock.Checksum ^= instruction.FullValue;
            CurrentBlock.Total++;
            MSIL_JIT.EmitRegisterTransfare(CurrentBlock);
        }

        private bool IsInvalidBlock(uint block, bool isBios) {
            if (isBios) {
                return !BIOS_CacheBlocks[block].IsCompiled;    //Not need to invalidate the content as the BIOS is not writable
            } else {
                return (!RAM_CacheBlocks[block].IsCompiled) || (!InvalidateRAM_Block(block));
            }
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

        private bool InvalidateRAM_Block(uint block) {  //For RAM Blocks only
            uint address = RAM_CacheBlocks[block].Address;
            uint memoryChecksum = 0;

            for (int i = 0; i < RAM_CacheBlocks[block].Total; i++) {
                uint memAddress = (uint)(address + (i * 4));
                uint memoryValue = BUS.LoadWord(memAddress);
                memoryChecksum ^= memoryValue;
            }

            return RAM_CacheBlocks[block].Checksum == memoryChecksum;
        }

        private bool IsJumpOrBranch(Instruction instruction) {
            uint op = instruction.GetOpcode();
            if (op == 0) {
                uint sub = instruction.Get_Subfunction();
                return sub == 0x8 || sub == 0x9 ;     //JR, JALR,
            } else {
                return op >= 1 && op <= 7;            //BXX, J, JAL, BEQ, BNE, BLEZ, BGTZ 
            }
        }

        private bool IsSyscallOrBreak(Instruction instruction) {
            uint op = instruction.GetOpcode();
            if (op == 0) {
                uint sub = instruction.Get_Subfunction();
                return sub == 0xC || sub == 0xD;     //Syscall, Break
            }
            return false;
        }

        private static int IRQCheck(CPURecompiler cpu) {
            if (IRQ_CONTROL.isRequestingIRQ()) {  //Interrupt check 
                cpu.Cop0.Cause |= (1 << 10);
                //Skip IRQs if the current instruction is a GTE instruction to avoid the BIOS skipping it
                if (((cpu.Cop0.SR & 1) != 0) && (((cpu.Cop0.SR >> 10) & 1) != 0) && !InstructionIsGTE(cpu)) {                  
                    Exception(cpu, (uint)CPU.Exceptions.IRQ);
                    return 1;
                }
            }
            return 0;
        }

        private static void HandleDelay(CPURecompiler cpu) {
            //Not used as currently this is being precompiled and emitted in IL then called
            //We could instead call this C# method..
            cpu.DelaySlot = cpu.Branch;   //Branch delay 
            cpu.Branch = false;
            cpu.PC = cpu.Next_PC;
            cpu.Next_PC = cpu.Next_PC + 4;
        }

        private static bool InstructionIsGTE(CPURecompiler cpu) {
            return false; //Does not work with current JIT
            return (cpu.CurrentInstruction.FullValue & 0xFE000000) == 0x4A000000;
        }

        private void RegisterTransfer(CPURecompiler cpu){
            //Not used as currently this is being precompiled and emitted in IL then called
            //We could instead call this C# method..

            //Handle register transfers and delay slot
            if (cpu.ReadyRegisterLoad.RegisterNumber != cpu.DelayedRegisterLoad.RegisterNumber) {
                cpu.GPR[cpu.ReadyRegisterLoad.RegisterNumber] = cpu.ReadyRegisterLoad.Value;
            }

            cpu.ReadyRegisterLoad.Value = cpu.DelayedRegisterLoad.Value;
            cpu.ReadyRegisterLoad.RegisterNumber = cpu.DelayedRegisterLoad.RegisterNumber;

            cpu.DelayedRegisterLoad.Value = 0;
            cpu.DelayedRegisterLoad.RegisterNumber = 0;

            //Last step is direct register write, so it can overwrite any memory load on the same register
            cpu.GPR[cpu.DirectWrite.RegisterNumber] = cpu.DirectWrite.Value;
            cpu.DirectWrite.RegisterNumber = 0;
            cpu.DirectWrite.Value = 0;
            cpu.GPR[0] = 0;
        }

        private void Intercept(uint pc) {

            switch (pc) {
                case 0xA0:      //Intercepting prints to the TTY Console and printing it in console 
                    char character;

                    switch (GPR[9]) {
                        case 0x3C:                       //putchar function (Prints the char in $a0)
                            character = (char)GPR[4];
                            Console.Write(character);
                            break;

                        case 0x3E:                        //puts function, similar to printf but differ in dealing with 0 character
                            uint address = GPR[4];       //address of the string is in $a0
                            if (address == 0) {
                                Console.Write("\\<NULL>");
                            }
                            else {
                                while (BUS.LoadByte(address) != 0) {
                                    character = (char)BUS.LoadByte(address);
                                    Console.Write(character);
                                    address++;
                                }

                            }
                            break;

                        default:
                            if (BUS.debug) {
                                Console.WriteLine("Function A: " + GPR[9].ToString("x"));
                            }
                            break;
                    }

                    break;

                case 0xB0:
                    switch (GPR[9]) {
                        case 0x3D:                       //putchar function (Prints the char in $a0)
                            character = (char)GPR[4];
                            if (Char.IsAscii(character)) {
                                if (Chars.Count > 0) {
                                    string unicoded = Encoding.UTF8.GetString(Chars.ToArray());
                                    Console.Write(unicoded);
                                    Chars.Clear();

                                }
                                Console.Write(character);
                            } else {
                                Chars.Add((byte)GPR[4]);
                            }
                            break;

                        case 0x3F:                          //puts function, similar to printf but differ in dealing with 0 character
                            uint address = GPR[4];       //address of the string is in $a0
                            if (address == 0) {
                                Console.Write("\\<NULL>");
                            }
                            else {

                                while (BUS.LoadByte(address) != 0) {
                                    character = (char)BUS.LoadByte(address);
                                    Console.Write(character);
                                    address++;
                                }

                            }
                            break;
                            
                        default:
                            if (BUS.debug) {
                                Console.WriteLine("Function B: " + GPR[9].ToString("x"));
                            }
                            break;
                    }

                    break;

                case 0xC0:
                    if (BUS.debug) {
                        Console.WriteLine("Function C: " + GPR[9].ToString("x"));
                    }
                    break;
            }
        }

        private void loadTestRom(string? path) {
            EXE = File.ReadAllBytes(path);

            //Copy the EXE data to memory
            uint addressInRAM = (uint)(EXE[0x018] | (EXE[0x018 + 1] << 8) |  (EXE[0x018 + 2] << 16) | (EXE[0x018 + 3] << 24));

            for (int i = 0x800; i < EXE.Length; i++) {
                BUS.StoreByte(addressInRAM, EXE[i]);
                addressInRAM++;
            }
         
            //Set up SP, FP, and GP
            uint baseStackAndFrameAddress = (uint)(EXE[0x30] | (EXE[0x30 + 1] << 8) | (EXE[0x30 + 2] << 16) | (EXE[0x30 + 3] << 24));

            if (baseStackAndFrameAddress != 0) {              
                uint stackAndFrameOffset = (uint)(EXE[0x34] | (EXE[0x34 + 1] << 8) | (EXE[0x34 + 2] << 16) | (EXE[0x34 + 3] << 24));
                GPR[(int)CPU.Register.sp] = GPR[(int)CPU.Register.fp] = baseStackAndFrameAddress + stackAndFrameOffset;
            }

            GPR[(int)CPU.Register.gp] = (uint)(EXE[0x14] | (EXE[0x14 + 1] << 8) | (EXE[0x14 + 2] << 16) | (EXE[0x14 + 3] << 24));

            //Jump to the address specified by the EXE
            Current_PC = PC = (uint)(EXE[0x10] | (EXE[0x10 + 1] << 8) | (EXE[0x10 + 2] << 16) | (EXE[0x10 + 3] << 24));
            Next_PC = PC + 4;
        }

        public static void Exception(CPURecompiler cpu, uint exceptionCause) {
            //If the next instruction is a GTE instruction skip the exception
            //Otherwise the BIOS will try to handle the GTE bug by skipping the instruction  
            uint handler;                                         //Get the handler

            if ((cpu.Cop0.SR & (1 << 22)) != 0) {
                handler = 0xbfc00180;
            } else {
                handler = 0x80000080;
            }

            uint mode = cpu.Cop0.SR & 0x3f;                          //Disable interrupts 

            cpu.Cop0.SR = (uint)(cpu.Cop0.SR & ~0x3f);

            cpu.Cop0.SR = cpu.Cop0.SR | ((mode << 2) & 0x3f);

            cpu.Cop0.Cause = exceptionCause << 2;                    //Update cause register

            //Small hack: if IRQ happens step the branch delay to avoid having the handler pointing 
            //to the previous instruction which is the delay slot instruction
            //Note: when we leave JIT we are (almost always) in a delay slot
            if (exceptionCause == (int)CPU.Exceptions.IRQ) {
                cpu.Cop0.EPC = cpu.PC;                          //Save the PC in register EPC
                cpu.DelaySlot = cpu.Branch;
            } else {
                cpu.Cop0.EPC = cpu.Current_PC;                 //Save the current PC in register EPC
            }

            if (cpu.DelaySlot) {                              //In case an exception occurs in a delay slot
                cpu.Cop0.EPC -= 4;
                cpu.Cop0.Cause = (uint)(cpu.Cop0.Cause | (1 << 31));
            }

            cpu.PC = handler;                          //Jump to the handler address (no delay)
            cpu.Next_PC = cpu.PC + 4;
        }
       
        public static void branch(CPURecompiler cpu, uint offset) {
            offset = offset << 2;
            cpu.Next_PC = cpu.Next_PC + offset;
            cpu.Next_PC = cpu.Next_PC - 4;        //Cancel the +4 from the emu cycle 
            cpu.Branch = true;
        }

        public void Tick() {
            for (int i = 0; i < CYCLES_PER_FRAME;) { 
                int add = emu_cycle();
                i += add;
                BUS.Tick(add);
                IRQCheck(this);
            }
        }

        public void Reset() {
            Current_PC = PC = 0xbfc00000;
            Next_PC = PC + 4;
        }

        public ref BUS GetBUS() {
            return ref BUS;
        }
    }
}
