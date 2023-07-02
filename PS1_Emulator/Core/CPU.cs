using System;
using System.IO;

namespace PSXEmulator {
    public unsafe class CPU {
        private UInt32 pc;           //32-bit program counter
        private UInt32 next_pc;
        private UInt32 current_pc;
        public BUS BUS;
        private UInt32[] GPR;
        private Instruction current;
        private UInt32 SR;           //cop0 reg12 , the status register 
        private UInt32 cause;        //cop0 reg13 , the cause register 
        private UInt32 epc;          //cop0 reg14 , epc
        private UInt32 HI;           //Remainder of devision
        private UInt32 LO;           //Quotient of devision
        private bool _branch;
        private bool delay_slot;

        //Geometry Transformation Engine - Coprocessor 2
        GTE gte = new GTE();

        public static int cycles = 0;

        //Exception codes
        private const UInt32 IRQ = 0x0;
        private const UInt32 LoadAddressError = 0x4;
        private const UInt32 StoreAddressError = 0x5;
        private const UInt32 SysCall = 0x8;
        private const UInt32 Break = 0x9;
        private const UInt32 IllegalInstruction = 0xa;
        private const UInt32 CoprocessorError = 0xb;
        private const UInt32 Overflow = 0xc;
        private byte[] testRom;

        bool fastBoot = true;
        public bool isPaused = false;
        const uint CYCLES_PER_FRAME = 33868800 / 60;

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

        public CPU(BUS bus) {
            this.pc = 0xbfc00000;   //BIOS initial PC       
            this.next_pc = pc + 4;
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
            this._branch = false;
            this.delay_slot = false;
            Console.ForegroundColor = ConsoleColor.Green;   
        }
        struct RegisterLoad {
            public uint registerNumber;
            public uint value;
        }
        
        RegisterLoad registerLoad;      
        RegisterLoad registerDelayedLoad;
        RegisterLoad directWrite;       //Not memory access, will overwrite memory loads

        public void emu_cycle() {

            current_pc = pc;   //Save current pc In case of an exception
            intercept(pc);     //TTY

             /*if (fastBoot) {       //Skip Sony logo, doesn't work 
                 if (pc == 0x80030000) {
                    pc = GPR[31];
                    next_pc = pc + 4;
                    fastBoot = false;
                    registerLoad.value = 0;
                    registerLoad.registerNumber = 0;
                    registerDelayedLoad = registerLoad;
                    return;
                 }
             }*/
            //----------------------------------------------------------------------


            //PC must be 32 bit aligned, can be ignored?
            if ((current_pc & 0x3) != 0) {
                exception(this, LoadAddressError);
                return;
            }

            current = new Instruction(BUS.loadWord(pc));

            delay_slot = _branch;   //Branch delay 
            _branch = false;

            pc = next_pc;
            next_pc = next_pc + 4;

            if (IRQ_CONTROL.isRequestingIRQ()) {  //Interrupt check 
                cause |= 1 << 10;

                if (((SR & 1) != 0) && (((SR >> 10) & 1) != 0)) {
                    exception(this, IRQ);
                    return;
                }
            }

            executeInstruction(current);
            registerTransfer(this);
           
        }

        private void executeInstruction(Instruction instruction) {
            mainLookUpTable[instruction.getOpcode()](this, instruction);
        }
        private static void special(CPU cpu, Instruction instruction) {
            specialLookUpTable[instruction.get_subfunction()](cpu, instruction);
        }
        private void registerTransfer(CPU cpu){    //Hanlde register transfers and delay slot
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
               case 0x80030000:   //For executing EXEs
                    //loadTestRom(@"C:\Users\Old Snake\Desktop\PSX-master\GPU\16BPP\RenderTextureRectangle\CLUT8BPP\RenderTextureRectangleCLUT8BPP.exe");
                    break;

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
                                while (BUS.loadByte(address) != 0) {
                                    character = (char)BUS.loadByte(address);
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
                            Console.Write(character);
                            break;

                        case 0x3F:                          //puts function, similar to printf but differ in dealing with 0 character
                            uint address = GPR[4];       //address of the string is in $a0
                            if (address == 0) {
                                Console.Write("\\<NULL>");
                            }
                            else {

                                while (BUS.loadByte(address) != 0) {
                                    character = (char)BUS.loadByte(address);
                                    Console.Write(character);
                                    address++;
                                }

                            }
                            break;

                        case 0xB:
                            if (BUS.debug) {
                                Console.WriteLine("TestEvent");
                                Console.WriteLine("$a0: " + GPR[4].ToString("X"));
                                Console.WriteLine("$a1: " + GPR[5].ToString("X"));
                                Console.WriteLine("$a2: " + GPR[6].ToString("X"));
                                Console.WriteLine("$a3: " + GPR[7].ToString("X"));
                               
                            }
                            break;

                     
                        case 0x08:
                         
                            /*Console.WriteLine("OpenEvent");
                            Console.WriteLine("$a0: " + regs[4].ToString("X"));
                            Console.WriteLine("$a1: " + regs[5].ToString("X"));
                            Console.WriteLine("$a2: " + regs[6].ToString("X"));
                            Console.WriteLine("$a3: " + regs[7].ToString("X"));

                            openEvent = true;*/
                            
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

        private void loadTestRom(string path) {
            testRom = File.ReadAllBytes(path);

            uint addressInRAM = (uint)(testRom[0x018] | (testRom[0x018 + 1] << 8) | (testRom[0x018 + 2] << 16) | (testRom[0x018 + 3] << 24));

            for (int i = 0x800; i < testRom.Length; i++) {

                BUS.storeByte((uint)(addressInRAM), testRom[i]);
                addressInRAM++;
            }
            this.pc = (uint)(testRom[0x10] | (testRom[0x10 + 1] << 8) | (testRom[0x10 + 2] << 16) | (testRom[0x10 + 3] << 24));
            next_pc = this.pc + 4;
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
            switch (instruction.get_rs()) {
                case 0b00100: mtc0(cpu, instruction); break;
                case 0b00000: mfc0(cpu, instruction); break;
                case 0b10000: rfe(cpu, instruction);  break;
                default: throw new Exception("Unhandled cop0 instruction: " + instruction.getfull().ToString("X"));
            }

        }
        private static void illegal(CPU cpu, Instruction instruction) {
            Console.ForegroundColor = ConsoleColor.Red; 
            Console.WriteLine("[CPU] Illegal instruction: " + instruction.getfull().ToString("X").PadLeft(8,'0') + " at PC: " + cpu.pc.ToString("x"));
            Console.ForegroundColor = ConsoleColor.Green;
            exception(cpu, IllegalInstruction);

        }

        private static void swc3(CPU cpu, Instruction instruction) {
            exception(cpu,CoprocessorError); //StoreWord is not supported in this cop
        }
        private static void swc2(CPU cpu, Instruction instruction) {

            uint address = cpu.GPR[instruction.get_rs()] + instruction.signed_imm();

            if ((address & 0x3) != 0) {
                exception(cpu,LoadAddressError);
                return;
            }

            uint rt = instruction.get_rt();
            uint word = cpu.gte.read(rt);
            cpu.BUS.storeWord(address, word);

        }

       

        private static void swc1(CPU cpu, Instruction instruction) {
            exception(cpu,CoprocessorError); //StoreWord is not supported in this cop
        }

        private static void swc0(CPU cpu, Instruction instruction) {
            exception(cpu,CoprocessorError); //StoreWord is not supported in this cop
        }

        private static void lwc3(CPU cpu, Instruction instruction) {
            exception(cpu,CoprocessorError); //LoadWord is not supported in this cop
        }

        private static void lwc2(CPU cpu, Instruction instruction) {
            //TODO add 2 instructions delay

            uint address = cpu.GPR[instruction.get_rs()] + instruction.signed_imm();

            if ((address & 0x3) != 0) {
                exception(cpu,LoadAddressError);
                return;
            }

            uint word = cpu.BUS.loadWord(address);
            uint rt = instruction.get_rt();
            cpu.gte.write(rt, word);

        }

        private static void lwc1(CPU cpu, Instruction instruction) {
            exception(cpu,CoprocessorError); //LoadWord is not supported in this cop
        }

        private static void lwc0(CPU cpu, Instruction instruction) {
            exception(cpu,CoprocessorError); //LoadWord is not supported in this cop
        }

        private static void swr(CPU cpu, Instruction instruction) {
            //TODO add 2 instructions delay

            UInt32 addressRegPos = instruction.signed_imm();
            UInt32 base_ = instruction.get_rs();
            UInt32 final_address = cpu.GPR[base_] + addressRegPos;

            UInt32 value =  cpu.GPR[instruction.get_rt()];               
            UInt32 current_value = cpu.BUS.loadWord((UInt32)(final_address & (~3)));     //Last 2 bits are for alignment position only 

            UInt32 finalValue;
            UInt32 pos = final_address & 3;

            switch (pos) {
                case 0: finalValue = (current_value & 0x00000000) | (value << 0); break;
                case 1: finalValue = (current_value & 0x000000ff) | (value << 8); break;
                case 2: finalValue = (current_value & 0x0000ffff) | (value << 16); break;
                case 3: finalValue = (current_value & 0x00ffffff) | (value << 24); break;
                default: throw new Exception("swl instruction error, pos:" + pos);
            }

            cpu.BUS.storeWord((UInt32)(final_address & (~3)), finalValue);
        }

        private static void swl(CPU cpu, Instruction instruction) {
            UInt32 addressRegPos = instruction.signed_imm();
            UInt32 base_ = instruction.get_rs();
            UInt32 final_address = cpu.GPR[base_] + addressRegPos;

            UInt32 value = cpu.GPR[instruction.get_rt()];           
            UInt32 current_value = cpu.BUS.loadWord((UInt32)(final_address&(~3)));     //Last 2 bits are for alignment position only 

            UInt32 finalValue;
            UInt32 pos = final_address & 3;

            switch (pos) {
                case 0: finalValue = (current_value & 0xffffff00) | (value >> 24); break;
                case 1: finalValue = (current_value & 0xffff0000) | (value >> 16); break;
                case 2: finalValue = (current_value & 0xff000000) | (value >> 8); break;
                case 3: finalValue = (current_value & 0x00000000) | (value >> 0); break;
                default: throw new Exception("swl instruction error, pos:" + pos);
            }

            cpu.BUS.storeWord((UInt32)(final_address & (~3)), finalValue);

        }

        private static void lwr(CPU cpu, Instruction instruction) {

            UInt32 addressRegPos = instruction.signed_imm();
            UInt32 base_ = instruction.get_rs();
            UInt32 final_address = cpu.GPR[base_] + addressRegPos;

            UInt32 current_value = cpu.GPR[instruction.get_rt()];

            if (instruction.get_rt() == cpu.registerLoad.registerNumber) {
                current_value = cpu.registerLoad.value;                         //Bypass load delay
            }

            UInt32 word = cpu.BUS.loadWord((UInt32)(final_address & (~3)));     //Last 2 bits are for alignment position only 
            UInt32 finalValue;
            UInt32 pos = final_address & 3;

            switch (pos) {
                case 0: finalValue = (current_value & 0x00000000) | (word >> 0); break;
                case 1: finalValue = (current_value & 0xff000000) | (word >> 8); break;
                case 2: finalValue = (current_value & 0xffff0000) | (word >> 16); break;
                case 3: finalValue = (current_value & 0xffffff00) | (word >> 24); break;
                default: throw new Exception("lwr instruction error, pos:" + pos);
            }

            cpu.registerDelayedLoad.registerNumber = instruction.get_rt();   //Position
            cpu.registerDelayedLoad.value = finalValue;                      //Value

        }

        private static void lwl(CPU cpu, Instruction instruction) {
            UInt32 addressRegPos = instruction.signed_imm();
            UInt32 base_ = instruction.get_rs();
            UInt32 final_address = cpu.GPR[base_] + addressRegPos;

            UInt32 current_value =  cpu.GPR[instruction.get_rt()];

            if (instruction.get_rt() == cpu.registerLoad.registerNumber) {
                current_value = cpu.registerLoad.value;             //Bypass load delay
            }

            UInt32 word = cpu.BUS.loadWord((UInt32)(final_address&(~3)));     //Last 2 bits are for alignment position only 
            UInt32 finalValue;
            UInt32 pos = final_address & 3;

            switch (pos) {
                case 0: finalValue = (current_value & 0x00ffffff) | (word << 24); break;
                case 1: finalValue = (current_value & 0x0000ffff) | (word << 16); break;
                case 2: finalValue = (current_value & 0x000000ff) | (word << 8); break;
                case 3: finalValue = (current_value & 0x00000000) | (word << 0); break;
                default: throw new Exception("lwl instruction error, pos:" + pos);
            }

            cpu.registerDelayedLoad.registerNumber = instruction.get_rt();   //Position
            cpu.registerDelayedLoad.value = finalValue;                      //Value
            
        }

        private static void cop2(CPU cpu, Instruction instruction) {

            if (((instruction.get_rs() >> 4) & 1) == 1) {    //COP2 imm25 command
                cpu.gte.execute(instruction);
                return;
            }

            //GTE registers reads/writes have delay of 1 (?) instruction

            switch (instruction.get_rs()) {
                
                case 0b00000:   //MFC
                    cpu.registerDelayedLoad.registerNumber = instruction.get_rt();
                    cpu.registerDelayedLoad.value = cpu.gte.read(instruction.get_rd());
                    break;

                case 0b00010:   //CFC
                    cpu.registerDelayedLoad.registerNumber = instruction.get_rt();
                    cpu.registerDelayedLoad.value = cpu.gte.read(instruction.get_rd() + 32);
                    break;

                case 0b00110:  //CTC 
                    uint rd = instruction.get_rd();
                    uint value = cpu.GPR[instruction.get_rt()];
                    cpu.gte.write(rd + 32,value);
                    break;

                case 0b00100:  //MTC 
                    rd = instruction.get_rd();
                    value = cpu.GPR[instruction.get_rt()];
                    cpu.gte.write(rd, value);   //Same as CTC but without adding 32 to the position
                    break;

                default:  throw new Exception("Unhandled GTE opcode: " + instruction.get_rs().ToString("X"));
            }
        }


        private static void cop3(CPU cpu, Instruction instruction) {
            exception(cpu,CoprocessorError);
        }

        private static void cop1(CPU cpu, Instruction instruction) {
            exception(cpu,CoprocessorError);
        }

        private static void xori(CPU cpu, Instruction instruction) {
            UInt32 imm = instruction.getImmediateValue();
            cpu.directWrite.registerNumber = instruction.get_rt();         //Position
            cpu.directWrite.value = cpu.GPR[instruction.get_rs()] ^ imm;  //Value
        }

        private static void lh(CPU cpu, Instruction instruction) {
            UInt32 addressRegPos = instruction.signed_imm();
            UInt32 base_ = instruction.get_rs();
            UInt32 final_address = cpu.GPR[base_] + addressRegPos;

            //aligned?
            Int16 halfWord = (Int16)cpu.BUS.loadHalf(final_address);
            if ((final_address & 0x1) == 0) {
                cpu.registerDelayedLoad.registerNumber = instruction.get_rt();         //Position
                cpu.registerDelayedLoad.value = (UInt32)halfWord;                     //Value
            }
            else {
                exception(cpu,LoadAddressError);
            }

        }

        private static void lhu(CPU cpu, Instruction instruction) {

            UInt32 addressRegPos = instruction.signed_imm();
            UInt32 base_ = instruction.get_rs();
            UInt32 final_address = cpu.GPR[base_] + addressRegPos;

            if ((final_address & 0x1) == 0) {
                UInt32 halfWord = (UInt32)cpu.BUS.loadHalf(final_address);
                cpu.registerDelayedLoad.registerNumber = instruction.get_rt();  //Position
                cpu.registerDelayedLoad.value = halfWord;                       //Value
               
            }
            else {
                exception(cpu,LoadAddressError);
            }

        }

        private static void sltiu(CPU cpu, Instruction instruction) {
            cpu.directWrite.registerNumber = instruction.get_rt();

            if (cpu.GPR[instruction.get_rs()] < instruction.signed_imm()) {
                cpu.directWrite.value = 1;
            }
            else {
                cpu.directWrite.value = 0;
            }
     
        }

        private static void sub(CPU cpu, Instruction instruction) {
            Int32 reg1 = (Int32)cpu.GPR[instruction.get_rs()];
            Int32 reg2 = (Int32)cpu.GPR[instruction.get_rt()];

            try {
                Int32 value = checked(reg1 - reg2);        //Check for signed integer overflow 
                cpu.directWrite.registerNumber = instruction.get_rd();
                cpu.directWrite.value = (UInt32)value;
            }
            catch (OverflowException) {
                exception(cpu,Overflow);
            }
         
        }

        private static void mult(CPU cpu, Instruction instruction) {
            //Sign extend
            Int64 a = (Int64) ((Int32)cpu.GPR[instruction.get_rs()]);
            Int64 b = (Int64) ((Int32)cpu.GPR[instruction.get_rt()]);

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
                    CPU.cycles += 6;
                    break;

                case Int64 x when (a >= 0x00000800 && a <= 0x000FFFFF) || (a >= 0xFFF00000 && a <= 0xFFFFF801):
                    CPU.cycles += 9;
                    break;

                case Int64 x when (a >= 0x00100000 && a <= 0x7FFFFFFF) || (a >= 0x80000000 && a <= 0xFFF00001):
                    CPU.cycles += 13;
                    break;
            }

        }

        private static void break_(CPU cpu, Instruction instruction) {
            exception(cpu,Break);
        }

        private static void xor(CPU cpu, Instruction instruction) {
            cpu.directWrite.registerNumber = instruction.get_rd();
            cpu.directWrite.value = cpu.GPR[instruction.get_rs()] ^ cpu.GPR[instruction.get_rt()];
        }

        private static void multu(CPU cpu, Instruction instruction) {
            UInt64 a = (UInt64)cpu.GPR[instruction.get_rs()];
            UInt64 b = (UInt64)cpu.GPR[instruction.get_rt()];

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
                    CPU.cycles += 6;
                    break;

                case UInt64 x when a >= 0x00000800 && a <= 0x000FFFFF:
                    CPU.cycles += 9;
                    break;

                case UInt64 x when a >= 0x00100000 && a <= 0xFFFFFFFF:
                    CPU.cycles += 13;
                    break;
            }

        }

        private static void srlv(CPU cpu, Instruction instruction) {
            cpu.directWrite.registerNumber = instruction.get_rd();
            cpu.directWrite.value = cpu.GPR[instruction.get_rt()] >> ((Int32)(cpu.GPR[instruction.get_rs()] & 0x1f));
        }
        private static void srav(CPU cpu, Instruction instruction) {
            Int32 value = ((Int32)cpu.GPR[instruction.get_rt()]) >> ((Int32)(cpu.GPR[instruction.get_rs()] & 0x1f));
            cpu.directWrite.registerNumber = instruction.get_rd();
            cpu.directWrite.value = (UInt32)value;
        }

        private static void nor(CPU cpu, Instruction instruction) {
            cpu.directWrite.registerNumber = instruction.get_rd();
            cpu.directWrite.value = ~(cpu.GPR[instruction.get_rs()] | cpu.GPR[instruction.get_rt()]);
        }

        private static void sllv(CPU cpu, Instruction instruction) {                             
            cpu.directWrite.registerNumber = instruction.get_rd();             //Take 5 bits from register rs
            cpu.directWrite.value = cpu.GPR[instruction.get_rt()] << ((Int32)(cpu.GPR[instruction.get_rs()] & 0x1f));
        }

        private static void mthi(CPU cpu, Instruction instruction) {
            cpu.HI = cpu.GPR[instruction.get_rs()];
        }
        private static void mtlo(CPU cpu, Instruction instruction) {
            cpu.LO = cpu.GPR[instruction.get_rs()];
        }

        private static void syscall(CPU cpu,Instruction instruction) {
            exception(cpu, SysCall);
        }

        private static void exception(CPU cpu, UInt32 exceptionCause){

            //If an interrupt occurs "on" a GTE command (cop2cmd), then the GTE command is executed 
            //ProjectPSX:
            //if ((cpu.bus.load32(cpu.current_pc) >> 26) == 0x12) { return; }

            /*
                PSX-SPX:
                if (cause AND 7Ch)=00h                      ;if excode=interrupt
                     if ([epc] AND FE000000h)=4A000000h     ;and opcode=cop2cmd
                         epc=epc+4                          ;then skip that opcode
             */



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


            cpu.cause = exceptionCause << 2;                    //Update cause register

            cpu.epc = cpu.current_pc;                 //Save the current PC in register EPC

            if (cpu.delay_slot) {                   //in case an exception occurs in a delay slot
                cpu.epc -= 4;
                cpu.cause = (UInt32)(cpu.cause | (1 << 31));
            }

            if (exceptionCause == IRQ && (cpu.epc & 0xFE000000) == 0x4A000000) {
                cpu.epc += 4;
            }

            cpu.pc = handler;                          //Jump to the handler address (no delay)
            cpu.next_pc = cpu.pc + 4;

            
        }

        private static void slt(CPU cpu, Instruction instruction) {
            cpu.directWrite.registerNumber = instruction.get_rd();
            if (((Int32)cpu.GPR[instruction.get_rs()]) < ((Int32)cpu.GPR[instruction.get_rt()])) {
                cpu.directWrite.value = 1;
            }
            else {
                cpu.directWrite.value = 0;
            }
        }

        private static void divu(CPU cpu, Instruction instruction) {

            UInt32 numerator = cpu.GPR[instruction.get_rs()];
            UInt32 denominator = cpu.GPR[instruction.get_rt()];

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

            CPU.cycles += 36;
        }

        private static void srl(CPU cpu, Instruction instruction) {
            //Right Shift (Logical)

            UInt32 val = cpu.GPR[instruction.get_rt()];
            UInt32 shift = instruction.get_sa();
            cpu.directWrite.registerNumber = instruction.get_rd();
            cpu.directWrite.value = val >> (Int32)shift;
        }

        private static void mflo(CPU cpu, Instruction instruction) { //LO -> GPR[rd]
            cpu.directWrite.registerNumber = instruction.get_rd();
            cpu.directWrite.value = cpu.LO;
        }
        private static void mfhi(CPU cpu, Instruction instruction) {        //HI -> GPR[rd]
            cpu.directWrite.registerNumber = instruction.get_rd();
            cpu.directWrite.value = cpu.HI;
        }

        private static void div(CPU cpu, Instruction instruction) { // GPR[rs] / GPR[rt] -> (HI, LO) 
            Int32 numerator = (Int32)cpu.GPR[instruction.get_rs()];
            Int32 denominator = (Int32)cpu.GPR[instruction.get_rt()];

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

            CPU.cycles += 36;

        }

        private static void sra(CPU cpu, Instruction instruction) {
            //Right Shift (Arithmetic)

            Int32 val = (Int32)cpu.GPR[instruction.get_rt()];
            Int32 shift = (Int32)instruction.get_sa();
            cpu.directWrite.registerNumber = instruction.get_rd();
            cpu.directWrite.value = (UInt32)(val >> shift);

        }

        private static void slti(CPU cpu, Instruction instruction) {

            Int32 si = (Int32)instruction.signed_imm();
            Int32 rg = (Int32)cpu.GPR[instruction.get_rs()];
            cpu.directWrite.registerNumber = instruction.get_rt();

            if (rg<si) {
                cpu.directWrite.value = 1;
            }
            else {
                cpu.directWrite.value = 0;
             }

        }

        private static void bxx(CPU cpu,Instruction instruction) {         //*
            uint value = (uint)instruction.getfull();
            
            //if rs is $ra, then the value used for the comparison is $ra's value before linking.
            if (((value >> 16) & 1) == 1) {
                //BGEZ
                if ((Int32)cpu.GPR[instruction.get_rs()] >= 0) {
                    branch(cpu,instruction.signed_imm());
                }
            }
            else {
                //BLTZ
                if ((Int32)cpu.GPR[instruction.get_rs()] < 0) {
                    branch(cpu,instruction.signed_imm());
                }

            }

            if (((value >> 17) & 0xF) == 0x8) {
               //Store return address if the value of bits [20:17] == 0x8
                cpu.directWrite.registerNumber = 31;
                cpu.directWrite.value = cpu.next_pc;
            }

        }

        private static void lbu(CPU cpu, Instruction instruction) {
            UInt32 addressRegPos = instruction.signed_imm();
            UInt32 base_ = instruction.get_rs();

            byte byte_ = cpu.BUS.loadByte(cpu.GPR[base_] + addressRegPos);
            cpu.registerDelayedLoad.registerNumber = instruction.get_rt();  //Position
            cpu.registerDelayedLoad.value = (UInt32)byte_;                     //Value
            
        }

        private static void blez(CPU cpu, Instruction instruction) {
            Int32 signedValue = (Int32)cpu.GPR[instruction.get_rs()];
            if (signedValue <= 0) {
                branch(cpu,instruction.signed_imm());
            }
        }

        private static void bgtz(CPU cpu, Instruction instruction) {     //Branch if > 0
            Int32 signedValue = (Int32)cpu.GPR[instruction.get_rs()];      
            if (signedValue > 0) {
                branch(cpu,instruction.signed_imm());
            }
        }
        private static void subu(CPU cpu, Instruction instruction) {
            cpu.directWrite.registerNumber = instruction.get_rd();
            cpu.directWrite.value = cpu.GPR[instruction.get_rs()] - cpu.GPR[instruction.get_rt()];
        }

        private static void jalr(CPU cpu, Instruction instruction) {
            // Store return address in $rd
            cpu.directWrite.registerNumber = instruction.get_rd();
            cpu.directWrite.value = cpu.next_pc;

            if ((cpu.GPR[instruction.get_rs()] & 0x3) != 0) {
                exception(cpu, LoadAddressError);
                return;
            }
            // Jump to address in $rs
            cpu.next_pc = cpu.GPR[instruction.get_rs()];
            cpu._branch = true;

        }

        private static void beq(CPU cpu, Instruction instruction) {
            if (cpu.GPR[instruction.get_rs()].Equals(cpu.GPR[instruction.get_rt()])) {
                branch(cpu,instruction.signed_imm());
            }
        }

        private static void lb(CPU cpu, Instruction instruction) {
            if ((cpu.SR & 0x10000) != 0) {
               // Debug.WriteLine("loading from memory ignored, cache is isolated");
                return;
            }

            UInt32 addressRegPos = instruction.signed_imm();
            UInt32 base_ = instruction.get_rs();
            sbyte sb = (sbyte)cpu.BUS.loadByte(cpu.GPR[base_] + addressRegPos);
            cpu.registerDelayedLoad.registerNumber = instruction.get_rt();  //Position
            cpu.registerDelayedLoad.value = (UInt32)sb;                     //Value

        }

        private static void sb(CPU cpu, Instruction instruction) {
            if ((cpu.SR & 0x10000) != 0) {
               // Debug.WriteLine("store ignored, cache is isolated");      //Ignore write when cache is isolated 
                return;
            }

            UInt32 targetReg = instruction.get_rt();
            UInt32 addressRegPos = instruction.signed_imm();
            UInt32 base_ = instruction.get_rs();
            cpu.BUS.storeByte(cpu.GPR[base_] + addressRegPos, (byte)cpu.GPR[targetReg]);
        }

        private static void andi(CPU cpu,Instruction instruction) {
            UInt32 targetReg = instruction.get_rt();
            UInt32 imm = instruction.getImmediateValue();
            UInt32 rs = instruction.get_rs();
            cpu.directWrite.registerNumber = targetReg;
            cpu.directWrite.value = cpu.GPR[rs] & imm;
        }

        private static void jal(CPU cpu, Instruction instruction) {
            cpu.directWrite.registerNumber = 31;
            cpu.directWrite.value = cpu.next_pc;             //Jump and link, store the PC to return to it later
            jump(cpu,instruction);
        }
        private static void sh(CPU cpu, Instruction instruction) {
            if ((cpu.SR & 0x10000) != 0) {
               // Debug.WriteLine("store ignored, cache is isolated");      //Ignore write, the writing should be on the cache 
                return;
            }

            UInt32 targetReg = instruction.get_rt();

            UInt32 addressRegPos = instruction.signed_imm();
            UInt32 base_ = instruction.get_rs();
            UInt32 final_address = cpu.GPR[base_] + addressRegPos;

            //Address must be 16 bit aligned
            if ((final_address & 1) == 0) {
                cpu.BUS.storeHalf(final_address, (UInt16)cpu.GPR[targetReg]);
            }
            else {
                exception(cpu,StoreAddressError);
            }

        }

        private static void addi(CPU cpu, Instruction instruction) {
            Int32 imm = (Int32)(instruction.signed_imm());
            Int32 s = (Int32)(cpu.GPR[instruction.get_rs()]);
            try {
                Int32 value = checked(imm + s);        //Check for signed integer overflow 
                cpu.directWrite.registerNumber = instruction.get_rt();
                cpu.directWrite.value = (UInt32)value;
            }
            catch (OverflowException) {
                exception(cpu, Overflow);
            }
           
        }

        public static void lui(CPU cpu, Instruction instruction) {
            UInt32 value = instruction.getImmediateValue();
            cpu.directWrite.registerNumber = instruction.get_rt();
            cpu.directWrite.value = value << 16;
        }

        public static void ori(CPU cpu, Instruction instruction) {
            UInt32 value = instruction.getImmediateValue();
            UInt32 rs = instruction.get_rs();
            cpu.directWrite.registerNumber = instruction.get_rt();
            cpu.directWrite.value = cpu.GPR[rs] | value;
        }
        public static void or(CPU cpu, Instruction instruction) {
            cpu.directWrite.registerNumber = instruction.get_rd();
            cpu.directWrite.value = cpu.GPR[instruction.get_rs()] | cpu.GPR[instruction.get_rt()];
        }
        private static void and(CPU cpu, Instruction instruction) {
            cpu.directWrite.registerNumber = instruction.get_rd();
            cpu.directWrite.value = cpu.GPR[instruction.get_rs()] & cpu.GPR[instruction.get_rt()];
        }
        public static void sw(CPU cpu, Instruction instruction) {
            if ((cpu.SR & 0x10000) != 0) {
               // Debug.WriteLine("store ignored, cache is isolated");      //Ignore write, the writing should be on the cache 
                return; 
            }       

            UInt32 targetReg = instruction.get_rt();

            UInt32 addressRegPos = instruction.signed_imm();
            UInt32 base_ = instruction.get_rs();
            UInt32 final_address = cpu.GPR[base_] + addressRegPos;

            //Address must be 32 bit aligned
            if ((final_address & 0x3) == 0) {
                cpu.BUS.storeWord(final_address, cpu.GPR[targetReg]);
            }
            else {
                exception(cpu,StoreAddressError);
            }

        }
        public static void lw(CPU cpu, Instruction instruction) {
            if ((cpu.SR & 0x10000) != 0) {
                //Debug.WriteLine("loading from memory ignored, cache is isolated");      
                return;
            }

            UInt32 addressRegPos = instruction.signed_imm();
            UInt32 base_ = instruction.get_rs();
            UInt32 final_address = cpu.GPR[base_] + addressRegPos;

            //Address must be 32 bit aligned
            if ((final_address & 0x3) == 0) {
                 cpu.registerDelayedLoad.registerNumber = instruction.get_rt();              //Position
                 cpu.registerDelayedLoad.value = cpu.BUS.loadWord(final_address);           //Value
            }
            else {
                exception(cpu,LoadAddressError);
            }
           
        }
        
        private static void add(CPU cpu, Instruction instruction) {
            Int32 reg1 = (Int32)cpu.GPR[instruction.get_rs()];       
            Int32 reg2 = (Int32)cpu.GPR[instruction.get_rt()];
            try {
                Int32 value = checked(reg1 + reg2);        //Check for signed integer overflow, can be ignored as no games rely on this 
                cpu.directWrite.registerNumber = instruction.get_rd();
                cpu.directWrite.value = (UInt32)value;
            }
            catch (OverflowException) {
                exception(cpu,Overflow);    
            }
        }

        private static void jr(CPU cpu, Instruction instruction) {
            cpu.next_pc = cpu.GPR[instruction.get_rs()];      //Return or Jump to address in register 
            if ((cpu.next_pc & 0x3) != 0) {
                exception(cpu, LoadAddressError);
            }
            cpu._branch = true;
        }

        private static void addu(CPU cpu, Instruction instruction) {
            cpu.directWrite.registerNumber = instruction.get_rd();
            cpu.directWrite.value = cpu.GPR[instruction.get_rs()] + cpu.GPR[instruction.get_rt()];
        }

        private static void sltu(CPU cpu, Instruction instruction) {
            cpu.directWrite.registerNumber = instruction.get_rd();
            if (cpu.GPR[instruction.get_rs()] < cpu.GPR[instruction.get_rt()]) {
                cpu.directWrite.value = 1;
            }
            else {
                cpu.directWrite.value = 0;
            }
           
        }
        public static void sll(CPU cpu,Instruction instruction) {
            cpu.directWrite.registerNumber = instruction.get_rd();
            cpu.directWrite.value = cpu.GPR[instruction.get_rt()] << (Int32)instruction.get_sa();

        }
        private static void addiu(CPU cpu, Instruction instruction) {
            cpu.directWrite.registerNumber = instruction.get_rt();
            cpu.directWrite.value = cpu.GPR[instruction.get_rs()] + instruction.signed_imm();
        }

        private static void jump(CPU cpu, Instruction instruction) {
            cpu.next_pc = (cpu.next_pc & 0xf0000000) | (instruction.imm_jump() << 2);
            cpu._branch = true;
        }


        private static void rfe(CPU cpu, Instruction instruction) {
            if (instruction.get_subfunction() != 0b010000) {    //Check bits [5:0]
                throw new Exception("Invalid cop0 instruction: " + instruction.getfull().ToString("X"));
            }

            UInt32 mode = cpu.SR & 0x3f;                   //Enable interrupts
            cpu.SR = (uint)(cpu.SR & ~0x3f);
            cpu.SR = cpu.SR | (mode >> 2);

        }

        private static void mfc0(CPU cpu, Instruction instruction) {
            //MFC has load delay
            cpu.registerDelayedLoad.registerNumber = instruction.get_rt();

            switch (instruction.get_rd()) {
                case 12: cpu.registerDelayedLoad.value = cpu.SR; break;
                case 13: cpu.registerDelayedLoad.value = cpu.cause; break;
                case 14: cpu.registerDelayedLoad.value = cpu.epc; break;
                default: return; //yeah no, TODO: handle the rest
                    throw new Exception("Unhandled cop0 register: " + instruction.get_rd());
            }

        }

        private static void mtc0(CPU cpu, Instruction instruction) {

            switch (instruction.get_rd()) {
                case 3:
                case 5:                          //Breakpoints registers
                case 6:
                case 7:
                case 9:
                case 11:
                    if (cpu.GPR[instruction.get_rt()] != 0) {
                        throw new Exception("Unhandled write to cop0 register: " + instruction.get_rd());
                    }
                    break;

                case 12: cpu.SR = cpu.GPR[instruction.get_rt()]; break;         //Setting the status register's 16th bit

                case 13:
                    //cause register, mostly read-only data describing the
                    //cause of an exception. Apparently only bits[9:8] are writable
                    if (cpu.GPR[instruction.get_rt()] != 0) { 
                        //throw new Exception("Unhandled write to CAUSE register: " + instruction.get_rd());
                    }
                    break;

                default: throw new Exception("Unhandled cop0 register: " + instruction.get_rd());
            }
        }
        private static void bne(CPU cpu, Instruction instruction) {
            if (!cpu.GPR[instruction.get_rs()].Equals(cpu.GPR[instruction.get_rt()])) {
                branch(cpu,instruction.signed_imm());
            }
        }

        private static void branch(CPU cpu, UInt32 offset) {
            offset = offset << 2;
            cpu.next_pc = cpu.next_pc + offset;
            cpu.next_pc = cpu.next_pc - 4;        //Cancel the +4 from the emu cycle 
            cpu._branch = true;    
        }
     
        internal void tick() {
            if (isPaused) { return; }

            for (int i = 0; i < CYCLES_PER_FRAME;) {        //Timings are nowhere near accurate 

                /*try {
                    cpu.emu_cycle();

                }
                catch (Exception ex) {
                    File.WriteAllTextAsync("Crash.log", ex.ToString());
                    Close();
                }*/

                emu_cycle();
                emu_cycle();

                cycles += 2;

                if (BUS.Timer1.isUsingSystemClock()) { BUS.Timer1.tick(cycles); }
                BUS.Timer2.tick(cycles);

                BUS.SPU.SPU_Tick(cycles);
                BUS.GPU.tick(cycles * (double)11 / 7);
                BUS.IO_PORTS.tick(cycles);
                BUS.CD_ROM.tick(cycles);
                i += cycles;
                cycles = 0;
            }
        }
    }
}
