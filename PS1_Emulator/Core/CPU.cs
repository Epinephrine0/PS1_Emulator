using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PSXEmulator {
    public unsafe class CPU {
        private UInt32 PC;           //32-bit program counter
        private UInt32 Next_PC;
        private UInt32 Current_PC;
        public BUS BUS;
        private UInt32[] GPR;
        private UInt32 SR;           //cop0 reg12 , the status register 
        private UInt32 Cause;        //cop0 reg13 , the cause register 
        private UInt32 EPC;          //cop0 reg14 , EPC
        private UInt32 HI;           //Remainder of devision
        private UInt32 LO;           //Quotient of devision
        private bool Branch;
        private bool DelaySlot;
        private bool IscIsolateCache => (SR & 0x10000) != 0;
        //Geometry Transformation Engine - Coprocessor 2
        GTE Gte = new GTE();

        public static int cycles = 0;

        //Exception codes
        private enum Exceptions {
            IRQ = 0x0,
            LoadAddressError = 0x4,
            StoreAddressError = 0x5,
            BUSDataError = 0x7,
            SysCall = 0x8,
            Break = 0x9,
            IllegalInstruction = 0xa,
            CoprocessorError = 0xb,
            Overflow = 0xc
        }

        private byte[] EXE;
        private bool IsLoadingEXE;
        private string? EXEPath;

        bool FastBoot = false;                  //Skips the boot animation 
        public bool IsPaused = false;
        public bool IsStopped = false;
        const uint CYCLES_PER_FRAME = 33868800 / 60;
        const double GPU_FACTOR = ((double)715909) / 451584;
        List<byte> Chars = new List<byte>();    //Temporarily stores characters 

        private static readonly delegate*<CPU, Instruction, void>[] mainLookUpTable = new delegate*<CPU, Instruction, void>[] {
                &special,   &bxx,       &jump,      &jal,       &beq,        &bne,       &blez,      &bgtz,
                &addi,      &addiu,     &slti,      &sltiu,     &andi,       &ori,       &xori,      &lui,
                &cop0,      &cop1,      &cop2,      &cop3,      &illegal,    &illegal,   &illegal,   &illegal,
                &illegal,   &illegal,   &illegal,   &illegal,   &illegal,    &illegal,   &illegal,   &illegal,
                &lb,        &lh,        &lwl,       &lw,        &lbu,        &lhu,       &lwr,       &illegal,
                &sb,        &sh,        &swl,       &sw,        &illegal,    &illegal,   &swr,       &illegal,
                &lwc0,      &lwc1,      &lwc2,      &lwc3,      &illegal,    &illegal,   &illegal,   &illegal,
                &swc0,      &swc1,      &swc2,      &swc3,      &illegal,    &illegal,   &illegal,   &illegal
        };

        private static readonly delegate*<CPU, Instruction, void>[] specialLookUpTable = new delegate*<CPU, Instruction, void>[] {
                &sll,       &illegal,   &srl,       &sra,       &sllv,      &illegal,   &srlv,      &srav,
                &jr,        &jalr,      &illegal,   &illegal,   &syscall,   &break_,    &illegal,   &illegal,
                &mfhi,      &mthi,      &mflo,      &mtlo,      &illegal,   &illegal,   &illegal,   &illegal,
                &mult,      &multu,     &div,       &divu,      &illegal,   &illegal,   &illegal,   &illegal,
                &add,       &addu,      &sub,       &subu,      &and,       &or,        &xor,       &nor,
                &illegal,   &illegal,   &slt,       &sltu,      &illegal,   &illegal,   &illegal,   &illegal,
                &illegal,   &illegal,   &illegal,   &illegal,   &illegal,   &illegal,   &illegal,   &illegal,
                &illegal,   &illegal,   &illegal,   &illegal,   &illegal,   &illegal,   &illegal,   &illegal
        };

        public enum Register {
            zero = 0,
            at = 1,
            v0 = 2,
            v1 = 3,
            a0 = 4,
            a1 = 5,
            a2 = 6,
            a3 = 7,
            t0 = 8,
            t1 = 9,
            t2 = 10,
            t3 = 11,
            t4 = 12,
            t5 = 13,
            t6 = 14,
            t7 = 15,
            s0 = 16,
            s1 = 17,
            s2 = 18,
            s3 = 19,
            s4 = 20,
            s5 = 21,
            s6 = 22,
            s7 = 23,
            t8 = 24,
            t9 = 25,
            k0 = 26,
            k1 = 27,
            gp = 28,
            sp = 29,
            fp = 30,
            ra = 31
        }
        struct RegisterLoad {
            public uint registerNumber;
            public uint value;
        }

        RegisterLoad registerLoad;
        RegisterLoad registerDelayedLoad;
        RegisterLoad directWrite;       //Not memory access, will overwrite memory loads

        Instruction CurrentInstruction = new Instruction();

        public CPU(bool isEXE, string? EXEPath, BUS bus) {
            this.PC = 0xbfc00000;                   //BIOS initial PC       
            this.Next_PC = PC + 4;
            this.BUS = bus;
            this.GPR = new UInt32[32];
            this.GPR[0] = 0;
            this.SR = 0;
            this.directWrite.registerNumber = 0;    //Stupid load delay slot
            this.directWrite.value = 0;
            this.registerLoad.registerNumber = 0;
            this.registerLoad.value = 0;
            this.registerDelayedLoad.registerNumber = 0;
            this.registerDelayedLoad.value = 0; 
            this.HI = 0xdeadbeef;
            this.LO = 0xdeadbeef;
            this.Branch = false;
            this.DelaySlot = false;
            this.IsLoadingEXE = isEXE;
            this.EXEPath = EXEPath;
        }
       
        public void emu_cycle() {

            Current_PC = PC;   //Save current pc In case of an exception
            intercept(PC);     //For TTY

             if (FastBoot) {
                 if (PC == 0x80030000) {
                    PC = GPR[(int)Register.ra];
                    Next_PC = PC + 4;
                    FastBoot = false;
                    registerLoad.value = 0;
                    registerLoad.registerNumber = 0;
                    registerDelayedLoad = registerLoad;
                    return;
                 }
             }

            //PC must be 32 bit aligned, can be ignored?
            if ((Current_PC & 0x3) != 0) {
                Exception(this, (uint)Exceptions.LoadAddressError);
                return;
            }

            CurrentInstruction.FullValue = BUS.LoadWord(PC);    

            DelaySlot = Branch;   //Branch delay 
            Branch = false;

            PC = Next_PC;
            Next_PC = Next_PC + 4;

            if (IRQ_CONTROL.isRequestingIRQ()) {  //Interrupt check 
                Cause |= 1 << 10;

                //Skip IRQs if the current instruction is a GTE instruction to avoid the BIOS skipping it
                if (((SR & 1) != 0) && (((SR >> 10) & 1) != 0) && !InstructionIsGTE(this)) {
                    Exception(this, (uint)Exceptions.IRQ);
                    return;
                }
            }
            if (BUS.debug) {
                Console.WriteLine("[" + Current_PC.ToString("x").PadLeft(8, '0') + "]" + " --- " + CurrentInstruction.Getfull().ToString("x").PadLeft(8,'0'));
            }

            ExecuteInstruction(CurrentInstruction);
            RegisterTransfer(this);
        }

        private bool InstructionIsGTE(CPU cpu) {          
            return (cpu.CurrentInstruction.FullValue & 0xFE000000) == 0x4A000000;
        }

        private void ExecuteInstruction(Instruction instruction) {
            mainLookUpTable[instruction.GetOpcode()](this, instruction);
        }
        private static void special(CPU cpu, Instruction instruction) {
            specialLookUpTable[instruction.Get_Subfunction()](cpu, instruction);
        }
        private void RegisterTransfer(CPU cpu){    //Handle register transfers and delay slot
            if (cpu.registerLoad.registerNumber != cpu.registerDelayedLoad.registerNumber) {
                cpu.GPR[cpu.registerLoad.registerNumber] = cpu.registerLoad.value;
            }
            cpu.registerLoad.value = cpu.registerDelayedLoad.value;
            cpu.registerLoad.registerNumber = cpu.registerDelayedLoad.registerNumber;

            cpu.registerDelayedLoad.value = 0;
            cpu.registerDelayedLoad.registerNumber = 0;

            //Last step is direct register write, so it can overwrite any memory load on the same register
            cpu.GPR[cpu.directWrite.registerNumber] = cpu.directWrite.value;
            cpu.directWrite.registerNumber = 0;
            cpu.directWrite.value = 0;
            cpu.GPR[0] = 0;
        }
        private void intercept(uint pc) {

            switch (pc) {
               case 0x80030000: if (IsLoadingEXE) { loadTestRom(EXEPath); IsLoadingEXE = false; } break;

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

                        case 0xA4:
                        case 0xA5:
                        case 0xA6:
                        case 0x78:
                        case 0x7C:
                        case 0x7E:
                        case 0x81:
                        case 0x94:
                        case 0x54:
                        case 0x56:
                        case 0x71:
                        case 0x72:
                        case 0x90:
                        case 0x91:
                        case 0x92:
                        case 0x93:
                        case 0x95:
                        case 0x9E:
                        case 0xA2:
                        case 0xA3:
                            if (BUS.debug) {
                                CDROM_trace(GPR[9]);
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

                        case 0xB:
                            /*if (BUS.debug) {
                                Console.WriteLine("TestEvent");
                                Console.WriteLine("$a0: " + GPR[4].ToString("X"));
                                Console.WriteLine("$a1: " + GPR[5].ToString("X"));
                                Console.WriteLine("$a2: " + GPR[6].ToString("X"));
                                Console.WriteLine("$a3: " + GPR[7].ToString("X"));                              
                            }*/
                            break;

                     
                        case 0x08:
                         
                            /*Console.WriteLine("OpenEvent");
                            Console.WriteLine("$a0: " + GPR[4].ToString("X"));
                            Console.WriteLine("$a1: " + GPR[5].ToString("X"));
                            Console.WriteLine("$a2: " + GPR[6].ToString("X"));
                            Console.WriteLine("$a3: " + GPR[7].ToString("X"));*/
                            
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
                GPR[(int)Register.sp] = GPR[(int)Register.fp] = baseStackAndFrameAddress + stackAndFrameOffset;
            }

            GPR[(int)Register.gp] = (uint)(EXE[0x14] | (EXE[0x14 + 1] << 8) | (EXE[0x14 + 2] << 16) | (EXE[0x14 + 3] << 24));

            //Jump to the address specified by the EXE
            Current_PC = PC = (uint)(EXE[0x10] | (EXE[0x10 + 1] << 8) | (EXE[0x10 + 2] << 16) | (EXE[0x10 + 3] << 24));
            Next_PC = PC + 4;
        }

        public void CDROM_trace(uint func) {
            Console.Write("CDROM: ");

            switch (func) {
                case 0xA4:
                    Console.WriteLine("CdGetLbn");
                    break;

                case 0xA5:
                    Console.WriteLine("CdReadSector");
                    break;

                case 0xA6:
                    Console.WriteLine("CdGetStatus");
                    break;

                case 0x78:
                    Console.WriteLine("CdAsyncSeekL");
                    break;

                case 0x7C:
                    Console.WriteLine("CdAsyncGetStatus");
                    break;

                case 0x7E:
                    Console.WriteLine("CdAsyncReadSector");
                    break;

                case 0x81:
                    Console.WriteLine("CdAsyncSetMode");
                    break;

                case 0x94:
                    Console.WriteLine("CdromGetInt5errCode");
                    break;

                case 0x54:
                case 0x71:
                    Console.WriteLine("_96_init");
                    break;

                case 0x56:
                case 0x72:
                    Console.WriteLine(" _96_remove");
                    break;

                case 0x90:
                    Console.WriteLine("CdromIoIrqFunc1");
                    break;

                case 0x91:
                    Console.WriteLine("CdromDmaIrqFunc1");
                    break;

                case 0x92:
                    Console.WriteLine("CdromIoIrqFunc2");
                    break; 

                case 0x93:
                    Console.WriteLine("CdromDmaIrqFunc2");
                    break;

                case 0x95:
                    Console.WriteLine("CdInitSubFunc");
                    break;

                case 0x9E:
                    Console.WriteLine("SetCdromIrqAutoAbort");
                    break;

                case 0xA2:
                    Console.WriteLine("EnqueueCdIntr");
                    break;

                case 0xA3:
                    Console.WriteLine("DequeueCdIntr");
                    break;

                default:
                    Console.WriteLine("Unknown function: A(0x" + func.ToString("x")+")");
                    break;

            }
        }
        
        private static void cop0(CPU cpu, Instruction instruction) {
            switch (instruction.Get_rs()) {
                case 0b00100: mtc0(cpu, instruction); break;
                case 0b00000: mfc0(cpu, instruction); break;
                case 0b10000: rfe(cpu, instruction);  break;
                default: throw new Exception("Unhandled cop0 instruction: " + instruction.Getfull().ToString("X"));
            }

        }
        private static void illegal(CPU cpu, Instruction instruction) {
            Console.ForegroundColor = ConsoleColor.Red; 
            Console.WriteLine("[CPU] Illegal instruction: " + instruction.Getfull().ToString("X").PadLeft(8,'0') + " - Opcode(" + instruction.GetOpcode().ToString("X") + ") - " + " at PC: " + cpu.Current_PC.ToString("x"));
            Console.ForegroundColor = ConsoleColor.Green;
            Exception(cpu, (uint)Exceptions.IllegalInstruction);
            cpu.IsStopped = true;
        }

        private static void swc3(CPU cpu, Instruction instruction) {
            Exception(cpu, (uint)Exceptions.CoprocessorError); //StoreWord is not supported in this cop
        }
        private static void swc2(CPU cpu, Instruction instruction) {

            uint address = cpu.GPR[instruction.Get_rs()] + instruction.GetSignedImmediate();

            if ((address & 0x3) != 0) {
                Exception(cpu, (uint)Exceptions.LoadAddressError);
                return;
            }

            uint rt = instruction.Get_rt();
            uint word = cpu.Gte.read(rt);
            cpu.BUS.StoreWord(address, word);

        }

        private static void swc1(CPU cpu, Instruction instruction) {
            Exception(cpu, (uint)Exceptions.CoprocessorError); //StoreWord is not supported in this cop
        }

        private static void swc0(CPU cpu, Instruction instruction) {
            Exception(cpu, (uint)Exceptions.CoprocessorError); //StoreWord is not supported in this cop
        }

        private static void lwc3(CPU cpu, Instruction instruction) {
            Exception(cpu, (uint)Exceptions.CoprocessorError); //LoadWord is not supported in this cop
        }

        private static void lwc2(CPU cpu, Instruction instruction) {
            //TODO add 2 instructions delay

            uint address = cpu.GPR[instruction.Get_rs()] + instruction.GetSignedImmediate();

            if ((address & 0x3) != 0) {
                Exception(cpu, (uint)Exceptions.LoadAddressError);
                return;
            }

            uint word = cpu.BUS.LoadWord(address);
            uint rt = instruction.Get_rt();
            cpu.Gte.write(rt, word);

        }

        private static void lwc1(CPU cpu, Instruction instruction) {
            Exception(cpu, (uint)Exceptions.CoprocessorError); //LoadWord is not supported in this cop
        }

        private static void lwc0(CPU cpu, Instruction instruction) {
            Exception(cpu, (uint)Exceptions.CoprocessorError); //LoadWord is not supported in this cop
        }

        private static void swr(CPU cpu, Instruction instruction) {
            if (cpu.IscIsolateCache) { return; }

            //TODO add 2 instructions delay

            UInt32 addressRegPos = instruction.GetSignedImmediate();
            UInt32 base_ = instruction.Get_rs();
            UInt32 final_address = cpu.GPR[base_] + addressRegPos;

            UInt32 value =  cpu.GPR[instruction.Get_rt()];               
            UInt32 current_value = cpu.BUS.LoadWord((UInt32)(final_address & (~3)));     //Last 2 bits are for alignment position only 

            UInt32 finalValue;
            UInt32 pos = final_address & 3;

            switch (pos) {
                case 0: finalValue = (current_value & 0x00000000) | (value << 0); break;
                case 1: finalValue = (current_value & 0x000000ff) | (value << 8); break;
                case 2: finalValue = (current_value & 0x0000ffff) | (value << 16); break;
                case 3: finalValue = (current_value & 0x00ffffff) | (value << 24); break;
                default: throw new Exception("swl instruction error, pos:" + pos);
            }

            cpu.BUS.StoreWord((UInt32)(final_address & (~3)), finalValue);
        }

        private static void swl(CPU cpu, Instruction instruction) {
            if (cpu.IscIsolateCache) { return; }

            UInt32 addressRegPos = instruction.GetSignedImmediate();
            UInt32 base_ = instruction.Get_rs();
            UInt32 final_address = cpu.GPR[base_] + addressRegPos;

            UInt32 value = cpu.GPR[instruction.Get_rt()];           
            UInt32 current_value = cpu.BUS.LoadWord((UInt32)(final_address&(~3)));     //Last 2 bits are for alignment position only 

            UInt32 finalValue;
            UInt32 pos = final_address & 3;

            switch (pos) {
                case 0: finalValue = (current_value & 0xffffff00) | (value >> 24); break;
                case 1: finalValue = (current_value & 0xffff0000) | (value >> 16); break;
                case 2: finalValue = (current_value & 0xff000000) | (value >> 8); break;
                case 3: finalValue = (current_value & 0x00000000) | (value >> 0); break;
                default: throw new Exception("swl instruction error, pos:" + pos);
            }

            cpu.BUS.StoreWord((UInt32)(final_address & (~3)), finalValue);

        }

        private static void lwr(CPU cpu, Instruction instruction) {
            if (cpu.IscIsolateCache) { return; }

            UInt32 addressRegPos = instruction.GetSignedImmediate();
            UInt32 base_ = instruction.Get_rs();
            UInt32 final_address = cpu.GPR[base_] + addressRegPos;

            UInt32 current_value = cpu.GPR[instruction.Get_rt()];

            if (instruction.Get_rt() == cpu.registerLoad.registerNumber) {
                current_value = cpu.registerLoad.value;                         //Bypass load delay
            }

            UInt32 word = cpu.BUS.LoadWord((UInt32)(final_address & (~3)));     //Last 2 bits are for alignment position only 
            UInt32 finalValue;
            UInt32 pos = final_address & 3;

            switch (pos) {
                case 0: finalValue = (current_value & 0x00000000) | (word >> 0); break;
                case 1: finalValue = (current_value & 0xff000000) | (word >> 8); break;
                case 2: finalValue = (current_value & 0xffff0000) | (word >> 16); break;
                case 3: finalValue = (current_value & 0xffffff00) | (word >> 24); break;
                default: throw new Exception("lwr instruction error, pos:" + pos);
            }

            cpu.registerDelayedLoad.registerNumber = instruction.Get_rt();   //Position
            cpu.registerDelayedLoad.value = finalValue;                      //Value

        }

        private static void lwl(CPU cpu, Instruction instruction) {
            if (cpu.IscIsolateCache) { return; }
            UInt32 addressRegPos = instruction.GetSignedImmediate();
            UInt32 base_ = instruction.Get_rs();
            UInt32 final_address = cpu.GPR[base_] + addressRegPos;

            UInt32 current_value =  cpu.GPR[instruction.Get_rt()];

            if (instruction.Get_rt() == cpu.registerLoad.registerNumber) {
                current_value = cpu.registerLoad.value;             //Bypass load delay
            }

            UInt32 word = cpu.BUS.LoadWord((UInt32)(final_address&(~3)));     //Last 2 bits are for alignment position only 
            UInt32 finalValue;
            UInt32 pos = final_address & 3;

            switch (pos) {
                case 0: finalValue = (current_value & 0x00ffffff) | (word << 24); break;
                case 1: finalValue = (current_value & 0x0000ffff) | (word << 16); break;
                case 2: finalValue = (current_value & 0x000000ff) | (word << 8); break;
                case 3: finalValue = (current_value & 0x00000000) | (word << 0); break;
                default: throw new Exception("lwl instruction error, pos:" + pos);
            }

            cpu.registerDelayedLoad.registerNumber = instruction.Get_rt();   //Position
            cpu.registerDelayedLoad.value = finalValue;                      //Value
            
        }

        private static void cop2(CPU cpu, Instruction instruction) {

            if (((instruction.Get_rs() >> 4) & 1) == 1) {    //COP2 imm25 command
                cpu.Gte.execute(instruction);
                return;
            }

            //GTE registers reads/writes have delay of 1 (?) instruction

            switch (instruction.Get_rs()) {
                
                case 0b00000:   //MFC
                    cpu.registerDelayedLoad.registerNumber = instruction.Get_rt();
                    cpu.registerDelayedLoad.value = cpu.Gte.read(instruction.Get_rd());
                    break;

                case 0b00010:   //CFC
                    cpu.registerDelayedLoad.registerNumber = instruction.Get_rt();
                    cpu.registerDelayedLoad.value = cpu.Gte.read(instruction.Get_rd() + 32);
                    break;

                case 0b00110:  //CTC 
                    uint rd = instruction.Get_rd();
                    uint value = cpu.GPR[instruction.Get_rt()];
                    cpu.Gte.write(rd + 32,value);
                    break;

                case 0b00100:  //MTC 
                    rd = instruction.Get_rd();
                    value = cpu.GPR[instruction.Get_rt()];
                    cpu.Gte.write(rd, value);   //Same as CTC but without adding 32 to the position
                    break;

                default:  throw new Exception("Unhandled GTE opcode: " + instruction.Get_rs().ToString("X"));
            }
        }


        private static void cop3(CPU cpu, Instruction instruction) {
            Exception(cpu, (uint)Exceptions.CoprocessorError);
        }

        private static void cop1(CPU cpu, Instruction instruction) {
            Exception(cpu, (uint)Exceptions.CoprocessorError);
        }

        private static void xori(CPU cpu, Instruction instruction) {
            UInt32 imm = instruction.GetImmediate();
            cpu.directWrite.registerNumber = instruction.Get_rt();         //Position
            cpu.directWrite.value = cpu.GPR[instruction.Get_rs()] ^ imm;  //Value
        }

        private static void lh(CPU cpu, Instruction instruction) {
            if (cpu.IscIsolateCache) { return; }
            UInt32 addressRegPos = instruction.GetSignedImmediate();
            UInt32 base_ = instruction.Get_rs();
            UInt32 final_address = cpu.GPR[base_] + addressRegPos;

       
            if(final_address >= 0x1F800400 && final_address <= (0x1F800400 + 0xC00)) { Exception(cpu, (uint)Exceptions.BUSDataError); return; }
            
            //aligned?
            Int16 halfWord = (Int16)cpu.BUS.LoadHalf(final_address);
            if ((final_address & 0x1) == 0) {
                cpu.registerDelayedLoad.registerNumber = instruction.Get_rt();         //Position
                cpu.registerDelayedLoad.value = (UInt32)halfWord;                     //Value
            }
            else {
                Exception(cpu, (uint)Exceptions.LoadAddressError);
            }
        }

        private static void lhu(CPU cpu, Instruction instruction) {
            if (cpu.IscIsolateCache) { return; }

            UInt32 addressRegPos = instruction.GetSignedImmediate();
            UInt32 base_ = instruction.Get_rs();
            UInt32 final_address = cpu.GPR[base_] + addressRegPos;

            if ((final_address & 0x1) == 0) {
                UInt32 halfWord = (UInt32)cpu.BUS.LoadHalf(final_address);
                cpu.registerDelayedLoad.registerNumber = instruction.Get_rt();  //Position
                cpu.registerDelayedLoad.value = halfWord;                       //Value
               
            }
            else {
                Exception(cpu, (uint)Exceptions.LoadAddressError);
            }

        }

        private static void sltiu(CPU cpu, Instruction instruction) {
            cpu.directWrite.registerNumber = instruction.Get_rt();

            if (cpu.GPR[instruction.Get_rs()] < instruction.GetSignedImmediate()) {
                cpu.directWrite.value = 1;
            }
            else {
                cpu.directWrite.value = 0;
            }
     
        }

        private static void sub(CPU cpu, Instruction instruction) {
            Int32 reg1 = (Int32)cpu.GPR[instruction.Get_rs()];
            Int32 reg2 = (Int32)cpu.GPR[instruction.Get_rt()];

            try {
                Int32 value = checked(reg1 - reg2);        //Check for signed integer overflow 
                cpu.directWrite.registerNumber = instruction.Get_rd();
                cpu.directWrite.value = (UInt32)value;
            }
            catch (OverflowException) {
                Exception(cpu, (uint)Exceptions.Overflow);
            }
         
        }

        private static void mult(CPU cpu, Instruction instruction) {
            //Sign extend
            Int64 a = (Int64) ((Int32)cpu.GPR[instruction.Get_rs()]);
            Int64 b = (Int64) ((Int32)cpu.GPR[instruction.Get_rt()]);

            UInt64 v = (UInt64)(a * b);

            cpu.HI = (UInt32)(v >> 32);
            cpu.LO = (UInt32)(v);

            /*
              __mult_execution_time_____________________________________________________
              Fast  (6 cycles)   rs = 00000000h..000007FFh, or rs = FFFFF800h..FFFFFFFFh
              Med   (9 cycles)   rs = 00000800h..000FFFFFh, or rs = FFF00000h..FFFFF801h
              Slow  (13 cycles)  rs = 00100000h..7FFFFFFFh, or rs = 80000000h..FFF00001h

            */
            switch (a) {
                case Int64 x when (a >= 0x00000000 && a <= 0x000007FF) || (a >= 0xFFFFF800 && a <= 0xFFFFFFFF):
                    //CPU.cycles += 6;
                    break;

                case Int64 x when (a >= 0x00000800 && a <= 0x000FFFFF) || (a >= 0xFFF00000 && a <= 0xFFFFF801):
                    //CPU.cycles += 9;
                    break;

                case Int64 x when (a >= 0x00100000 && a <= 0x7FFFFFFF) || (a >= 0x80000000 && a <= 0xFFF00001):
                    //CPU.cycles += 13;
                    break;
            }

        }

        private static void break_(CPU cpu, Instruction instruction) {
            Exception(cpu, (uint)Exceptions.Break);
        }

        private static void xor(CPU cpu, Instruction instruction) {
            cpu.directWrite.registerNumber = instruction.Get_rd();
            cpu.directWrite.value = cpu.GPR[instruction.Get_rs()] ^ cpu.GPR[instruction.Get_rt()];
        }

        private static void multu(CPU cpu, Instruction instruction) {
            UInt64 a = (UInt64)cpu.GPR[instruction.Get_rs()];
            UInt64 b = (UInt64)cpu.GPR[instruction.Get_rt()];

            UInt64 v = a * b;

            cpu.HI = (UInt32)(v >> 32);
            cpu.LO = (UInt32)(v);


            /*
             __multu_execution_time_____________________________________________________
             Fast  (6 cycles)   rs = 00000000h..000007FFh
             Med   (9 cycles)   rs = 00000800h..000FFFFFh
             Slow  (13 cycles)  rs = 00100000h..FFFFFFFFh
             
            */

            switch (a) {
                case UInt64 x when a >= 0x00000000 && a <= 0x000007FF:
                    //CPU.cycles += 6;
                    break;

                case UInt64 x when a >= 0x00000800 && a <= 0x000FFFFF:
                    //CPU.cycles += 9;
                    break;

                case UInt64 x when a >= 0x00100000 && a <= 0xFFFFFFFF:
                    //CPU.cycles += 13;
                    break;
            }

        }

        private static void srlv(CPU cpu, Instruction instruction) {
            cpu.directWrite.registerNumber = instruction.Get_rd();
            cpu.directWrite.value = cpu.GPR[instruction.Get_rt()] >> ((Int32)(cpu.GPR[instruction.Get_rs()] & 0x1f));
        }
        private static void srav(CPU cpu, Instruction instruction) {
            Int32 value = ((Int32)cpu.GPR[instruction.Get_rt()]) >> ((Int32)(cpu.GPR[instruction.Get_rs()] & 0x1f));
            cpu.directWrite.registerNumber = instruction.Get_rd();
            cpu.directWrite.value = (UInt32)value;
        }

        private static void nor(CPU cpu, Instruction instruction) {
            cpu.directWrite.registerNumber = instruction.Get_rd();
            cpu.directWrite.value = ~(cpu.GPR[instruction.Get_rs()] | cpu.GPR[instruction.Get_rt()]);
        }

        private static void sllv(CPU cpu, Instruction instruction) {                             
            cpu.directWrite.registerNumber = instruction.Get_rd();             //Take 5 bits from register rs
            cpu.directWrite.value = cpu.GPR[instruction.Get_rt()] << ((Int32)(cpu.GPR[instruction.Get_rs()] & 0x1f));
        }

        private static void mthi(CPU cpu, Instruction instruction) {
            cpu.HI = cpu.GPR[instruction.Get_rs()];
        }
        private static void mtlo(CPU cpu, Instruction instruction) {
            cpu.LO = cpu.GPR[instruction.Get_rs()];
        }

        private static void syscall(CPU cpu,Instruction instruction) {
            Exception(cpu, (uint)Exceptions.SysCall);
        }

        private static void Exception(CPU cpu, UInt32 exceptionCause){
            //If the next instruction is a GTE instruction skip the exception
            //Otherwise the BIOS will try to handle the GTE bug by skipping the instruction  
           

            UInt32 handler;                                         //Get the handler

            if ((cpu.SR & (1 << 22)) != 0) {
                handler = 0xbfc00180;

            }
            else {
                handler = 0x80000080;
            }
  

            UInt32 mode = cpu.SR & 0x3f;                          //Disable interrupts 

            cpu.SR = (UInt32)(cpu.SR & ~0x3f);

            cpu.SR = cpu.SR | ((mode << 2) & 0x3f);


            cpu.Cause = exceptionCause << 2;                    //Update cause register

            cpu.EPC = cpu.Current_PC;                 //Save the current PC in register EPC

            if (cpu.DelaySlot) {                   //in case an exception occurs in a delay slot
                cpu.EPC -= 4;
                cpu.Cause = (UInt32)(cpu.Cause | (1 << 31));
            }

            cpu.PC = handler;                          //Jump to the handler address (no delay)
            cpu.Next_PC = cpu.PC + 4;
        }

        private static void slt(CPU cpu, Instruction instruction) {
            cpu.directWrite.registerNumber = instruction.Get_rd();
            if (((Int32)cpu.GPR[instruction.Get_rs()]) < ((Int32)cpu.GPR[instruction.Get_rt()])) {
                cpu.directWrite.value = 1;
            }
            else {
                cpu.directWrite.value = 0;
            }
        }

        private static void divu(CPU cpu, Instruction instruction) {

            UInt32 numerator = cpu.GPR[instruction.Get_rs()];
            UInt32 denominator = cpu.GPR[instruction.Get_rt()];

            if (denominator == 0) {
                cpu.LO = 0xffffffff;
                cpu.HI = (UInt32)numerator;
                return;
            }

            cpu.LO = (UInt32)(numerator / denominator);
            cpu.HI = (UInt32)(numerator % denominator);

            /*
              divu/div_execution_time
              Fixed (36 cycles)  no matter of rs and rt values

            */

            //CPU.cycles += 36;
        }

        private static void srl(CPU cpu, Instruction instruction) {
            //Right Shift (Logical)

            UInt32 val = cpu.GPR[instruction.Get_rt()];
            UInt32 shift = instruction.Get_sa();
            cpu.directWrite.registerNumber = instruction.Get_rd();
            cpu.directWrite.value = val >> (Int32)shift;
        }

        private static void mflo(CPU cpu, Instruction instruction) { //LO -> GPR[rd]
            cpu.directWrite.registerNumber = instruction.Get_rd();
            cpu.directWrite.value = cpu.LO;
        }
        private static void mfhi(CPU cpu, Instruction instruction) {        //HI -> GPR[rd]
            cpu.directWrite.registerNumber = instruction.Get_rd();
            cpu.directWrite.value = cpu.HI;
        }

        private static void div(CPU cpu, Instruction instruction) { // GPR[rs] / GPR[rt] -> (HI, LO) 
            Int32 numerator = (Int32)cpu.GPR[instruction.Get_rs()];
            Int32 denominator = (Int32)cpu.GPR[instruction.Get_rt()];

            if (numerator >= 0 && denominator == 0) {
                cpu.LO = 0xffffffff;
                cpu.HI = (UInt32)numerator;
                return;
            }
            else if (numerator < 0 && denominator == 0) {
                cpu.LO = 1;
                cpu.HI = (UInt32)numerator;
                return;
            }
            else if ((uint)numerator == 0x80000000 && (uint)denominator == 0xffffffff) {
                cpu.LO = 0x80000000;
                cpu.HI = 0;
                return;
            }

            cpu.LO = (UInt32)unchecked(numerator / denominator);
            cpu.HI = (UInt32)unchecked(numerator % denominator);

            /*
               divu/div_execution_time
               Fixed (36 cycles)  no matter of rs and rt values

             */

            //CPU.cycles += 36;

        }

        private static void sra(CPU cpu, Instruction instruction) {
            //Right Shift (Arithmetic)

            Int32 val = (Int32)cpu.GPR[instruction.Get_rt()];
            Int32 shift = (Int32)instruction.Get_sa();
            cpu.directWrite.registerNumber = instruction.Get_rd();
            cpu.directWrite.value = (UInt32)(val >> shift);

        }

        private static void slti(CPU cpu, Instruction instruction) {

            Int32 si = (Int32)instruction.GetSignedImmediate();
            Int32 rg = (Int32)cpu.GPR[instruction.Get_rs()];
            cpu.directWrite.registerNumber = instruction.Get_rt();

            if (rg<si) {
                cpu.directWrite.value = 1;
            }
            else {
                cpu.directWrite.value = 0;
             }

        }

        private static void bxx(CPU cpu,Instruction instruction) {         //*
            uint value = (uint)instruction.Getfull();
            
            //if rs is $ra, then the value used for the comparison is $ra's value before linking.
            if (((value >> 16) & 1) == 1) {
                //BGEZ
                if ((Int32)cpu.GPR[instruction.Get_rs()] >= 0) {
                    branch(cpu,instruction.GetSignedImmediate());
                }
            }
            else {
                //BLTZ
                if ((Int32)cpu.GPR[instruction.Get_rs()] < 0) {
                    branch(cpu,instruction.GetSignedImmediate());
                }

            }

            if (((value >> 17) & 0xF) == 0x8) {
               //Store return address in $31 if the value of bits [20:17] == 0x8
                cpu.directWrite.registerNumber = (uint)Register.ra;
                cpu.directWrite.value = cpu.Next_PC;
            }

        }

        private static void lbu(CPU cpu, Instruction instruction) {
            if (cpu.IscIsolateCache) { return; }
            UInt32 addressRegPos = instruction.GetSignedImmediate();
            UInt32 base_ = instruction.Get_rs();

            byte byte_ = cpu.BUS.LoadByte(cpu.GPR[base_] + addressRegPos);
            cpu.registerDelayedLoad.registerNumber = instruction.Get_rt();  //Position
            cpu.registerDelayedLoad.value = (UInt32)byte_;                     //Value
            
        }

        private static void blez(CPU cpu, Instruction instruction) {
            Int32 signedValue = (Int32)cpu.GPR[instruction.Get_rs()];
            if (signedValue <= 0) {
                branch(cpu,instruction.GetSignedImmediate());
            }
        }

        private static void bgtz(CPU cpu, Instruction instruction) {     //Branch if > 0
            Int32 signedValue = (Int32)cpu.GPR[instruction.Get_rs()];      
            if (signedValue > 0) {
                branch(cpu,instruction.GetSignedImmediate());
            }
        }
        private static void subu(CPU cpu, Instruction instruction) {
            cpu.directWrite.registerNumber = instruction.Get_rd();
            cpu.directWrite.value = cpu.GPR[instruction.Get_rs()] - cpu.GPR[instruction.Get_rt()];
        }

        private static void jalr(CPU cpu, Instruction instruction) {
            // Store return address in $rd
            cpu.directWrite.registerNumber = instruction.Get_rd();
            cpu.directWrite.value = cpu.Next_PC;

            if ((cpu.GPR[instruction.Get_rs()] & 0x3) != 0) {
                Exception(cpu, (uint)Exceptions.LoadAddressError);
                return;
            }
            // Jump to address in $rs
            cpu.Next_PC = cpu.GPR[instruction.Get_rs()];
            cpu.Branch = true;

        }
        private static void beq(CPU cpu, Instruction instruction) {
            if (cpu.GPR[instruction.Get_rs()].Equals(cpu.GPR[instruction.Get_rt()])) {
                branch(cpu,instruction.GetSignedImmediate());
            }
        }

        private static void lb(CPU cpu, Instruction instruction) {
            if (cpu.IscIsolateCache) { return; }
            UInt32 addressRegPos = instruction.GetSignedImmediate();
            UInt32 base_ = instruction.Get_rs();
            sbyte sb = (sbyte)cpu.BUS.LoadByte(cpu.GPR[base_] + addressRegPos);
            cpu.registerDelayedLoad.registerNumber = instruction.Get_rt();  //Position
            cpu.registerDelayedLoad.value = (UInt32)sb;                     //Value
        }

        private static void sb(CPU cpu, Instruction instruction) {
            if (cpu.IscIsolateCache) { return; }

            UInt32 targetReg = instruction.Get_rt();
            UInt32 addressRegPos = instruction.GetSignedImmediate();
            UInt32 base_ = instruction.Get_rs();
            cpu.BUS.StoreByte(cpu.GPR[base_] + addressRegPos, (byte)cpu.GPR[targetReg]);
        }

        private static void andi(CPU cpu,Instruction instruction) {
            UInt32 targetReg = instruction.Get_rt();
            UInt32 imm = instruction.GetImmediate();
            UInt32 rs = instruction.Get_rs();
            cpu.directWrite.registerNumber = targetReg;
            cpu.directWrite.value = cpu.GPR[rs] & imm;
        }

        private static void jal(CPU cpu, Instruction instruction) {
            cpu.directWrite.registerNumber = (uint)Register.ra;
            cpu.directWrite.value = cpu.Next_PC;             //Jump and link, store the PC to return to it later
            jump(cpu,instruction);
        }
        private static void sh(CPU cpu, Instruction instruction) {
            if (cpu.IscIsolateCache) { return; }

            UInt32 targetReg = instruction.Get_rt();

            UInt32 addressRegPos = instruction.GetSignedImmediate();
            UInt32 base_ = instruction.Get_rs();
            UInt32 final_address = cpu.GPR[base_] + addressRegPos;

            //Address must be 16 bit aligned
            if ((final_address & 1) == 0) {
                cpu.BUS.StoreHalf(final_address, (UInt16)cpu.GPR[targetReg]);
            }
            else {
                Exception(cpu, (uint)Exceptions.StoreAddressError);
            }

        }

        private static void addi(CPU cpu, Instruction instruction) {
            Int32 imm = (Int32)(instruction.GetSignedImmediate());
            Int32 s = (Int32)(cpu.GPR[instruction.Get_rs()]);
            try {
                Int32 value = checked(imm + s);        //Check for signed integer overflow 
                cpu.directWrite.registerNumber = instruction.Get_rt();
                cpu.directWrite.value = (UInt32)value;
            }
            catch (OverflowException) {
                Exception(cpu, (uint)Exceptions.Overflow);
            }
           
        }

        public static void lui(CPU cpu, Instruction instruction) {            
            UInt32 value = instruction.GetImmediate();
            cpu.directWrite.registerNumber = instruction.Get_rt();
            cpu.directWrite.value = value << 16;
        }

        public static void ori(CPU cpu, Instruction instruction) {
            UInt32 value = instruction.GetImmediate();
            UInt32 rs = instruction.Get_rs();
            cpu.directWrite.registerNumber = instruction.Get_rt();
            cpu.directWrite.value = cpu.GPR[rs] | value;
        }
        public static void or(CPU cpu, Instruction instruction) {
            cpu.directWrite.registerNumber = instruction.Get_rd();
            cpu.directWrite.value = cpu.GPR[instruction.Get_rs()] | cpu.GPR[instruction.Get_rt()];
        }
        private static void and(CPU cpu, Instruction instruction) {
            cpu.directWrite.registerNumber = instruction.Get_rd();
            cpu.directWrite.value = cpu.GPR[instruction.Get_rs()] & cpu.GPR[instruction.Get_rt()];
        }
        public static void sw(CPU cpu, Instruction instruction) {
            if (cpu.IscIsolateCache) { return; }

            UInt32 targetReg = instruction.Get_rt();

            UInt32 addressRegPos = instruction.GetSignedImmediate();
            UInt32 base_ = instruction.Get_rs();
            UInt32 final_address = cpu.GPR[base_] + addressRegPos;

            //Address must be 32 bit aligned
            if ((final_address & 0x3) == 0) {
                cpu.BUS.StoreWord(final_address, cpu.GPR[targetReg]);
            }
            else {
                Exception(cpu, (uint)Exceptions.StoreAddressError);
            }

        }

        public static void lw(CPU cpu, Instruction instruction) {
            if (cpu.IscIsolateCache) { return; }

            UInt32 addressRegPos = instruction.GetSignedImmediate();
            UInt32 base_ = instruction.Get_rs();
            UInt32 final_address = cpu.GPR[base_] + addressRegPos;
       
            //Address must be 32 bit aligned
            if ((final_address & 0x3) == 0) {
                 cpu.registerDelayedLoad.registerNumber = instruction.Get_rt();              //Position
                 cpu.registerDelayedLoad.value = cpu.BUS.LoadWord(final_address);           //Value
            }
            else {
                Exception(cpu, (uint)Exceptions.LoadAddressError);
            }
           
        }
        
        private static void add(CPU cpu, Instruction instruction) {
            Int32 reg1 = (Int32)cpu.GPR[instruction.Get_rs()];       
            Int32 reg2 = (Int32)cpu.GPR[instruction.Get_rt()];
            try {
                Int32 value = checked(reg1 + reg2);        //Check for signed integer overflow, can be ignored as no games rely on this 
                cpu.directWrite.registerNumber = instruction.Get_rd();
                cpu.directWrite.value = (UInt32)value;
            }
            catch (OverflowException) {
                Exception(cpu, (uint)Exceptions.Overflow);    
            }
        }

        private static void jr(CPU cpu, Instruction instruction) {
            cpu.Next_PC = cpu.GPR[instruction.Get_rs()];      //Return or Jump to address in register 
            if ((cpu.Next_PC & 0x3) != 0) {
                Exception(cpu, (uint)Exceptions.LoadAddressError);
            }
            cpu.Branch = true;
        }

        private static void addu(CPU cpu, Instruction instruction) {
            cpu.directWrite.registerNumber = instruction.Get_rd();
            cpu.directWrite.value = cpu.GPR[instruction.Get_rs()] + cpu.GPR[instruction.Get_rt()];
        }

        private static void sltu(CPU cpu, Instruction instruction) {
            cpu.directWrite.registerNumber = instruction.Get_rd();
            if (cpu.GPR[instruction.Get_rs()] < cpu.GPR[instruction.Get_rt()]) {
                cpu.directWrite.value = 1;
            }
            else {
                cpu.directWrite.value = 0;
            }
           
        }
        public static void sll(CPU cpu,Instruction instruction) {
            cpu.directWrite.registerNumber = instruction.Get_rd();
            cpu.directWrite.value = cpu.GPR[instruction.Get_rt()] << (Int32)instruction.Get_sa();

        }
        private static void addiu(CPU cpu, Instruction instruction) {
            cpu.directWrite.registerNumber = instruction.Get_rt();
            cpu.directWrite.value = cpu.GPR[instruction.Get_rs()] + instruction.GetSignedImmediate();
        }

        private static void jump(CPU cpu, Instruction instruction) {
            cpu.Next_PC = (cpu.Next_PC & 0xf0000000) | (instruction.GetImmediateJumpAddress() << 2);
            cpu.Branch = true;
        }


        private static void rfe(CPU cpu, Instruction instruction) {
            if (instruction.Get_Subfunction() != 0b010000) {    //Check bits [5:0]
                throw new Exception("Invalid cop0 instruction: " + instruction.Getfull().ToString("X"));
            }
                         /*
                        UInt32 mode = cpu.SR & 0x3f;                   
                        cpu.SR = (uint)(cpu.SR & ~0x3f);
                        cpu.SR = cpu.SR | (mode >> 2);*/

            uint temp = cpu.SR;
            cpu.SR = (uint)(cpu.SR & (~0xF));
            cpu.SR |= ((temp >> 2) & 0xF);
        }

        private static void mfc0(CPU cpu, Instruction instruction) {
            //MFC has load delay
            cpu.registerDelayedLoad.registerNumber = instruction.Get_rt();

            switch (instruction.Get_rd()) {
                case 12: cpu.registerDelayedLoad.value = cpu.SR; break;
                case 13: cpu.registerDelayedLoad.value = cpu.Cause; break;
                case 14: cpu.registerDelayedLoad.value = cpu.EPC; break;
                case 15: cpu.registerDelayedLoad.value = 0x00000002; break;     //COP0 R15 (PRID)
                default:  Console.WriteLine("Unhandled cop0 Register Read: " + instruction.Get_rd()); break;
            }
        }

        private static void mtc0(CPU cpu, Instruction instruction) {

            switch (instruction.Get_rd()) {
                case 3:
                case 5:                          //Breakpoints registers
                case 6:
                case 7:
                case 9:
                case 11:
                    if (cpu.GPR[instruction.Get_rt()] != 0) {
                        throw new Exception("Unhandled write to cop0 register: " + instruction.Get_rd());
                    }
                    break;

                case 12: cpu.SR = cpu.GPR[instruction.Get_rt()]; break;         //Setting the status register's 16th bit

                case 13:
                    //cause register, mostly read-only data describing the
                    //cause of an exception. Apparently only bits[9:8] are writable
                    if (cpu.GPR[instruction.Get_rt()] != 0) { 
                        //throw new Exception("Unhandled write to CAUSE register: " + instruction.get_rd());
                    }
                    break;

                default: throw new Exception("Unhandled cop0 register: " + instruction.Get_rd());
            }
        }
        private static void bne(CPU cpu, Instruction instruction) {
            if (!cpu.GPR[instruction.Get_rs()].Equals(cpu.GPR[instruction.Get_rt()])) {
                branch(cpu,instruction.GetSignedImmediate());
            }
        }

        private static void branch(CPU cpu, UInt32 offset) {
            offset = offset << 2;
            cpu.Next_PC = cpu.Next_PC + offset;
            cpu.Next_PC = cpu.Next_PC - 4;        //Cancel the +4 from the emu cycle 
            cpu.Branch = true;    
        }
     
        internal void tick() {
            if (IsPaused || IsStopped) { return; }
            for (int i = 0; i < CYCLES_PER_FRAME;) {        //Timings are nowhere near accurate 
                int add = IsReadingFromBIOS ? 20 : 2;
                emu_cycle();

                cycles += add;

                if (BUS.Timer1.isUsingSystemClock()) { BUS.Timer1.tick(cycles); }
                BUS.Timer2.tick(cycles);

                BUS.SPU.SPU_Tick(cycles);
                BUS.GPU.tick(cycles * GPU_FACTOR);
                BUS.IO_PORTS.tick(cycles);
                BUS.CDROM.tick(cycles);
                i += cycles;
                cycles = 0;
            }
        }
        bool IsReadingFromBIOS => BUS.BIOS.range.Contains(BUS.Mask(PC));
    }
}
