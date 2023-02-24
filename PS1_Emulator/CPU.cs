using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace PS1_Emulator {
    public unsafe class CPU {
        private UInt32 pc;                //32-bit program counter
        private UInt32 next_pc;
        private UInt32 current_pc;
        public  Interconnect bus;
        private UInt32[] regs;
        private UInt32[] outRegs;
        private Instruction current;
        private UInt32 SR;           //cop0 reg12 , the status register 
        private UInt32 cause;        //cop0 reg13 , the cause register 
        private UInt32 epc;          //cop0 reg14, epc

        private (UInt32, UInt32) pendingload; //to emulate the load delay

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

        bool fastBoot = false;

        /* Main Opcodes:
           00h=SPECIAL 08h=ADDI  10h=COP0 18h=N/A   20h=LB   28h=SB   30h=LWC0 38h=SWC0
           01h=BcondZ  09h=ADDIU 11h=COP1 19h=N/A   21h=LH   29h=SH   31h=LWC1 39h=SWC1
           02h=J       0Ah=SLTI  12h=COP2 1Ah=N/A   22h=LWL  2Ah=SWL  32h=LWC2 3Ah=SWC2
           03h=JAL     0Bh=SLTIU 13h=COP3 1Bh=N/A   23h=LW   2Bh=SW   33h=LWC3 3Bh=SWC3
           04h=BEQ     0Ch=ANDI  14h=N/A  1Ch=N/A   24h=LBU  2Ch=N/A  34h=N/A  3Ch=N/A
           05h=BNE     0Dh=ORI   15h=N/A  1Dh=N/A   25h=LHU  2Dh=N/A  35h=N/A  3Dh=N/A
           06h=BLEZ    0Eh=XORI  16h=N/A  1Eh=N/A   26h=LWR  2Eh=SWR  36h=N/A  3Eh=N/A
           07h=BGTZ    0Fh=LUI   17h=N/A  1Fh=N/A   27h=N/A  2Fh=N/A  37h=N/A  3Fh=N/A

       */

        private static readonly delegate*<CPU,Instruction, void>[] mainLookUpTable = new delegate*<CPU, Instruction, void>[] {
                &special,   &bxx,       &jump,      &jal,       &beq,        &bne,       &blez,      &bgtz,
                &addi,      &addiu,     &slti,      &sltiu,     &andi,       &ori,       &xori,      &lui,
                &cop0,      &cop1,      &cop2,      &cop3,      &illegal,    &illegal,   &illegal,   &illegal,
                &illegal,   &illegal,   &illegal,   &illegal,   &illegal,    &illegal,   &illegal,   &illegal,
                &lb,        &lh,        &lwl,       &lw,        &lbu,        &lhu,       &lwr,       &illegal,
                &sb,        &sh,        &swl,       &sw,        &illegal,    &illegal,   &swr,       &illegal,
                &lwc0,      &lwc1,      &lwc2,      &lwc3,      &illegal,    &illegal,   &illegal,   &illegal,
                &swc0,      &swc1,      &swc2,      &swc3,      &illegal,    &illegal,   &illegal,   &illegal

            };


        /*
            Special Opcodes:
            00h=SLL   08h=JR      10h=MFHI 18h=MULT  20h=ADD  28h=N/A  30h=N/A  38h=N/A
            01h=N/A   09h=JALR    11h=MTHI 19h=MULTU 21h=ADDU 29h=N/A  31h=N/A  39h=N/A
            02h=SRL   0Ah=N/A     12h=MFLO 1Ah=DIV   22h=SUB  2Ah=SLT  32h=N/A  3Ah=N/A
            03h=SRA   0Bh=N/A     13h=MTLO 1Bh=DIVU  23h=SUBU 2Bh=SLTU 33h=N/A  3Bh=N/A
            04h=SLLV  0Ch=SYSCALL 14h=N/A  1Ch=N/A   24h=AND  2Ch=N/A  34h=N/A  3Ch=N/A
            05h=N/A   0Dh=BREAK   15h=N/A  1Dh=N/A   25h=OR   2Dh=N/A  35h=N/A  3Dh=N/A
            06h=SRLV  0Eh=N/A     16h=N/A  1Eh=N/A   26h=XOR  2Eh=N/A  36h=N/A  3Eh=N/A
            07h=SRAV  0Fh=N/A     17h=N/A  1Fh=N/A   27h=NOR  2Fh=N/A  37h=N/A  3Fh=N/A
        
         */

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
        public CPU(Interconnect bus) {
            this.pc = 0xbfc00000;         
            this.next_pc = pc + 4;
            this.bus = bus;
            this.regs = new UInt32[32];
            this.outRegs = new UInt32[32];
            this.regs[0] = 0;
            this.outRegs[0] = 0;
            this.SR = 0;
            this.pendingload = (0, 0);
            this.HI = 0xdeadbeef;
            this.LO = 0xdeadbeef;
            this._branch = false;
            this.delay_slot = false;

            
            Console.ForegroundColor = ConsoleColor.Green;   //For TTY Console
            Console.Title = "TTY Console";
        }

        public void emu_cycle() {   
              
            current_pc = pc;   //Save current pc In case of an exception

            //Should move these to J,Jal,Jr,Jalr instead of checking on every instruction
            intercept(pc);
            /*if (fastBoot) {       //Skip Sony logo, doesn't work 
                if (pc == 0x80030000) {
                    pc = outRegs[31];
                    next_pc = pc+4;
                    fastBoot = false;
                }
            }*/
            //----------------------------------------------------------------------


            //PC must be 32 bit aligned 
            if (current_pc % 4 != 0) {
                exception(this, LoadAddressError);
                return;
            }

            current = new Instruction(bus.load32(pc));
            

            delay_slot = _branch;
            _branch = false;       
                                
            pc = next_pc;
            next_pc = next_pc + 4;

            outRegs[pendingload.Item1] = pendingload.Item2;     //Load any pending load 
            pendingload = (0, 0);                              //Reset 

            outRegs[0] = 0;


            if (IRQ_CONTROL.isRequestingIRQ()) {  //Interrupt check 
                cause |= 1 << 10;

                if (((SR & 1) != 0) && (((SR >> 10) & 1) != 0)) {
                    exception(this,IRQ);
                    return;
                }

            }

            executeInstruction(current);

             outRegs[0] = 0;

            for (int i = 0; i < regs.Length; i++) {
                regs[i] = outRegs[i];
            }

        }

        private void executeInstruction(Instruction instruction) {
            mainLookUpTable[instruction.getOpcode()](this,instruction);
        }

        private void intercept(uint pc) {

            switch (pc) {
               case 0x80030000:   //For executing EXEs
                    //loadTestRom(@"C:\Users\Old Snake\Desktop\PS1\tests\gpu\lines\lines.exe");

                    break;

                case 0xA0:      //Intercepting prints to the TTY Console and printing it in console 
                    char character;

                    switch (regs[9]) {


                        case 0x03:                        //Writes a number of characters to a file, but I just write it to the console 
                            character = (char)regs[5];      //$a1
                            uint size = regs[6];            //$a2
                            outRegs[2] = size;

                            //fd?

                            while (size > 0) {
                                //write
                                size--;
                            }

                            break;

                        case 0x09:

                            character = (char)regs[4];    //Writes a character to a file
                                                         
                            break;

                        case 0x3C:                       //putchar function (Prints the char in $a0)
                            character = (char)regs[4];
                            Console.Write(character);
                            break;

                        case 0x3E:                        //puts function, similar to printf but differ in dealing with 0 character

                            uint address = regs[4];       //address of the string is in $a0
                            if (address == 0) {
                                Console.Write("\\<NULL>");
                            }
                            else {

                                while (bus.load8(address) != 0) {
                                    character = (char)bus.load8(address);
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
                            if (bus.print) {
                                CDROM_trace(regs[9]);
                            }
                            break;

                        default:
                            if (bus.print) {
                                Console.WriteLine("Function A: " + regs[9].ToString("x"));
                            }

                            break;


                    }
                    break;

                case 0xB0:
                    switch (regs[9]) {
                        case 0x35:                      //Writes a number of characters to a file, but I just write it to the console 
                            character = (char)regs[5];      //$a1
                            uint size = regs[6];            //$a2
                            outRegs[2] = size;

                            //fd?

                            while (size > 0) {
                                // Console.Write(character);
                                size--;
                            }

                            break;


                        case 0x3D:                       //putchar function (Prints the char in $a0)
                            character = (char)regs[4];
                            Console.Write(character);
                            break;

                        case 0x3B:

                            character = (char)regs[4];    //Writes a character to a file, but I just write it to the console 
                                                          //  Console.Write(character);
                            break;

                        case 0x3F:                          //puts function, similar to printf but differ in dealing with 0 character

                            uint address = regs[4];       //address of the string is in $a0
                            if (address == 0) {
                                Console.Write("\\<NULL>");
                            }
                            else {

                                while (bus.load8(address) != 0) {
                                    character = (char)bus.load8(address);
                                    Console.Write(character);
                                    address++;
                                }

                            }

                            break;

                        case 0xB:
                            if (bus.print) {
                                Console.WriteLine("TestEvent");
                                Console.WriteLine("$a0: " + regs[4].ToString("X"));
                                Console.WriteLine("$a1: " + regs[5].ToString("X"));
                                Console.WriteLine("$a2: " + regs[6].ToString("X"));
                                Console.WriteLine("$a3: " + regs[7].ToString("X"));
                               

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
                            if (bus.print) {
                                Console.WriteLine("Function B: " + regs[9].ToString("x"));
                            }
                            break;


                    }

                    break;
                case 0xC0:
                    if (bus.print) {
                        Console.WriteLine("Function C: " + regs[9].ToString("x"));
                    }
                    break;
            }
        }

        private void loadTestRom(string path) {
            testRom = File.ReadAllBytes(path);


            uint addressInRAM = (uint)(testRom[0x018] | (testRom[0x018 + 1] << 8) | (testRom[0x018 + 2] << 16) | (testRom[0x018 + 3] << 24));

            for (int i = 0x800; i < testRom.Length; i++) {

                bus.store8((uint)(addressInRAM), testRom[i]);
                addressInRAM++;
            }
            this.pc = (uint)(testRom[0x10] | (testRom[0x10 + 1] << 8) | (testRom[0x10 + 2] << 16) | (testRom[0x10 + 3] << 24));
            next_pc = this.pc + 4;
            //Console.WriteLine("Execute at PC:" + pc.ToString("x"));


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
        
       
       
        /*public void decode_execute(Instruction instruction) {

            switch (instruction.getOpcode()) {

                case 0b001111:

                    lui(instruction);
                    break;

                case 0b001101:

                    ori(instruction);
                    break;

                case 0b101011:

                    sw(instruction);
                    break;

                case 0b000000:

                    special(instruction);
                    break;

                case 0b001001:

                    addiu(instruction);
                    break;

                case 0b000010:

                    jump(instruction);
                    break;

                case 0b010000:

                    cop0(instruction);
                    break;

                case 0b000101:

                    bne(instruction);
                    break;

                case 0b001000:

                    addi(instruction);
                    break;

                case 0b100011:

                    lw(instruction);
                    break;

                case 0b101001:

                    sh(instruction);

                    break;

                case 0b000011:

                    jal(instruction);
                    break;

                case 0b001100:

                    andi(instruction);
                    break;

                case 0b101000:

                    sb(instruction);
                    break;

                case 0b100000:

                    lb(instruction);
                    break;


                case 0b000100:

                    beq(instruction);
                    break;

                case 0b000111:

                    bgtz(instruction);
                    break;

                case 0b000110:

                    blez(instruction);

                    break;

                case 0b100100:

                    lbu(instruction);
                    break;

                case 0b000001:

                    bxx(instruction);
                    break;

                case 0b001010:

                    slti(instruction);
                    break;

                case 0b001011:

                    sltiu(instruction);
                    break;

                case 0b100101:

                    lhu(instruction);
                    break;

                case 0b100001:

                    lh(instruction);
                    break;

                case 0xe:

                    xori(instruction);
                    break;

                case 0x11:

                    cop1(instruction);
                    break;

                case 0x12:

                    cop2(instruction);
                    break;

                case 0x13:

                    cop3(instruction);
                    break;

                case 0x22:

                    lwl(instruction);
                    break;

                case 0x26:

                    lwr(instruction);
                    break;

                case 0x2a:

                    swl(instruction);
                    break;

                case 0x2e:

                    swr(instruction);
                    break;

                case 0x30:

                    lwc0(instruction);
                    break;

                case 0x31:

                    lwc1(instruction);
                    break;

                case 0x32:
                    
                    lwc2(instruction);
                    break;

                case 0x33:

                    lwc3(instruction);
                    break;

                case 0x38:

                    swc0(instruction);
                    break;

                case 0x39:

                    swc1(instruction);
                    break;

            
                case 0x41:

                    swc3(instruction);

                    break;

                case 0x3A:

                    swc2(instruction);
                    break;

                default:

                    illegal(instruction);
                    break;

            }


        }*/


        private static void special(CPU cpu, Instruction instruction) {
            specialLookUpTable[instruction.get_subfunction()](cpu, instruction);

            /*switch (instruction.get_subfunction()) {

                case 0b000000:

                    sll(instruction);

                    break;

                case 0b100101:

                    OR(instruction);

                    break;

                case 0b100100:

                    AND(instruction);

                    break;

                case 0b101011:

                    stlu(instruction);

                    break;

                case 0b100001:

                    addu(instruction);
                    break;

                case 0b001000:

                    jr(instruction);
                    break;

                case 0b100000:

                    add(instruction);
                    break;


                case 0b001001:

                    jalr(instruction);
                    break;

                case 0b100011:

                    subu(instruction);
                    break;

                case 0b000011:


                    sra(instruction);
                    break;

                case 0b011010:

                    div(instruction);
                    break;

                case 0b010010:

                    mflo(instruction);
                    break;

                case 0b000010:

                    srl(instruction);
                    break;

                case 0b011011:

                    divu(instruction);
                    break;

                case 0b010000:

                    mfhi(instruction);
                    break;

                case 0b101010:

                    slt(instruction);
                    break;

                case 0b001100:

                    syscall(instruction);
                    break;

                case 0b010011:

                    mtlo(instruction);

                    break;

                case 0b010001:

                    mthi(instruction);

                    break;

                case 0b000100:

                    sllv(instruction);
                    break;

                case 0b100111:

                    nor(instruction);
                    break;

                case 0b000111:

                    srav(instruction);
                    break;

                case 0b000110:

                    srlv(instruction);
                    break;

                case 0b011001:

                    multu(instruction);
                    break;

                case 0b100110:

                    xor(instruction);
                    break;

                case 0xd:

                    break_(instruction);
                    break;

                case 0x18:

                    mult(instruction);
                    break;

                case 0x22:

                    sub(instruction);

                    break;

                case 0x0E:
                    illegal(instruction);

                    break;


                default:

                    throw new Exception("Unhandeled special instruction [ " + instruction.getfull().ToString("X").PadLeft(8, '0') + " ] with subfunction: " + Convert.ToString(instruction.get_subfunction(), 2).PadLeft(6, '0'));

            }*/

        }
        private static void cop0(CPU cpu, Instruction instruction) {

            switch (instruction.get_rs()) {

                case 0b00100:

                    mtc0(cpu, instruction);

                    break;


                case 0b00000:

                    mfc0(cpu, instruction);

                    break;

                case 0b10000:

                    rfe(cpu, instruction);


                    break;

                default:

                    throw new Exception("Unhandled cop0 instruction: " + instruction.getfull().ToString("X"));
            }


        }
        private static void illegal(CPU cpu, Instruction instruction) {
            Console.ForegroundColor = ConsoleColor.Red; 
            Console.WriteLine("[CPU] Illegal instruction: " + instruction.getfull().ToString("X").PadLeft(8,'0') + " at PC: " + cpu.pc.ToString("x"));
            Console.ForegroundColor = ConsoleColor.Green;

            //exception(cpu, IllegalInstruction);

        }

        private static void swc3(CPU cpu, Instruction instruction) {
            exception(cpu,CoprocessorError); //StoreWord is not supported in this cop
        }
        private static void swc2(CPU cpu, Instruction instruction) {

            uint address = cpu.regs[instruction.get_rs()] + instruction.signed_imm();

            if (address % 4 != 0) {
                exception(cpu,LoadAddressError);
                return;
            }

            uint rt = instruction.get_rt();
            uint word = cpu.gte.read(rt);
            cpu.bus.store32(address, word);

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

            uint address = cpu.regs[instruction.get_rs()] + instruction.signed_imm();

            if (address % 4 != 0) {
                exception(cpu,LoadAddressError);
                return;
            }

            uint word = cpu.bus.load32(address);
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
            UInt32 final_address = cpu.regs[base_] + addressRegPos;


            UInt32 value = cpu.regs[instruction.get_rt()];                           //Bypass load delay
            UInt32 current_value = cpu.bus.load32((UInt32)(final_address & (~3)));     //Last 2 bits are for alignment position only 

            UInt32 finalValue;
            UInt32 pos = final_address & 3;

            switch (pos) {

                case 0:

                    finalValue = ((current_value & 0x00000000) | (value << 0));
                    break;

                case 1:

                    finalValue = ((current_value & 0x000000ff) | (value << 8));
                    break;

                case 2:

                    finalValue = ((current_value & 0x0000ffff) | (value << 16));
                    break;

                case 3:

                    finalValue = ((current_value & 0x00ffffff) | (value << 24));
                    break;

                default:

                    throw new Exception("swl instruction error, pos:" + pos);
            }

            cpu.bus.store32((UInt32)(final_address & (~3)), finalValue);
        }

        private static void swl(CPU cpu, Instruction instruction) {
            UInt32 addressRegPos = instruction.signed_imm();
            UInt32 base_ = instruction.get_rs();
            UInt32 final_address = cpu.regs[base_] + addressRegPos;


            UInt32 value = cpu.regs[instruction.get_rt()];                           //Bypass load delay
            UInt32 current_value = cpu.bus.load32((UInt32)(final_address&(~3)));     //Last 2 bits are for alignment position only 

            UInt32 finalValue;
            UInt32 pos = final_address & 3;

            switch (pos) {
                
                case 0:

                    finalValue = ((current_value & 0xffffff00) | (value >> 24));
                    break;

                case 1:

                    finalValue = ((current_value & 0xffff0000) | (value >> 16));
                    break;

                case 2:

                    finalValue = ((current_value & 0xff000000) | (value >> 8));
                    break;

                case 3:

                    finalValue = ((current_value & 0x00000000) | (value >> 0));
                    break;

                default:

                    throw new Exception("swl instruction error, pos:" + pos);
            }

            cpu.bus.store32((UInt32)(final_address & (~3)), finalValue);


        }

        private static void lwr(CPU cpu, Instruction instruction) {

            UInt32 addressRegPos = instruction.signed_imm();
            UInt32 base_ = instruction.get_rs();
            UInt32 final_address = cpu.regs[base_] + addressRegPos;


            UInt32 current_value = cpu.outRegs[instruction.get_rt()];       //Bypass load delay
            UInt32 word = cpu.bus.load32((UInt32)(final_address & (~3)));     //Last 2 bits are for alignment position only 

            UInt32 finalValue;
            UInt32 pos = final_address & 3;

            switch (pos) {

                case 0:
                    finalValue = ((current_value & 0x00000000) | (word >> 0));

                    break;

                case 1:
                    finalValue = ((current_value & 0xff000000) | (word >> 8));

                    break;

                case 2:
                    finalValue = ((current_value & 0xffff0000) | (word >> 16));

                    break;

                case 3:
                    finalValue = ((current_value & 0xffffff00) | (word >> 24));

                    break;

                default:

                    throw new Exception("lwr instruction error, pos:" + pos);
            }
            //If we are loading to a register which was loaded in a delay slot, the first load is completely calnceled 
            if (cpu.regs[instruction.get_rt()] != cpu.outRegs[instruction.get_rt()]) {
                cpu.outRegs[instruction.get_rt()] = cpu.regs[instruction.get_rt()];
            }

            cpu.pendingload.Item1 = instruction.get_rt();  //Position
            cpu.pendingload.Item2 = finalValue;           //Value

        }

        private static void lwl(CPU cpu, Instruction instruction) {

            UInt32 addressRegPos = instruction.signed_imm();
            UInt32 base_ = instruction.get_rs();
            UInt32 final_address = cpu.regs[base_] + addressRegPos;


            UInt32 current_value = cpu.outRegs[instruction.get_rt()];       //Bypass load delay
            UInt32 word = cpu.bus.load32((UInt32)(final_address&(~3)));     //Last 2 bits are for alignment position only 

            UInt32 finalValue;
            UInt32 pos = final_address & 3;

            switch (pos) {
                
                case 0:

                    finalValue = ((current_value & 0x00ffffff) | (word << 24));
                    break;

                case 1:

                    finalValue = ((current_value & 0x0000ffff) | (word << 16));
                    break;

                case 2:

                    finalValue = ((current_value & 0x000000ff) | (word << 8));
                    break;

                case 3:

                    finalValue = ((current_value & 0x00000000) | (word << 0));
                    break;

                default:

                    throw new Exception("lwl instruction error, pos:" + pos);
            }

            //If we are loading to a register which was loaded in a delay slot, the first load is completely calnceled 
            if (cpu.regs[instruction.get_rt()] != cpu.outRegs[instruction.get_rt()]) {
                cpu.outRegs[instruction.get_rt()] = cpu.regs[instruction.get_rt()];
            }

            cpu.pendingload.Item1 = instruction.get_rt();  //Position
            cpu.pendingload.Item2 = finalValue;           //Value

        }

        private static void cop2(CPU cpu, Instruction instruction) {

            if (((instruction.get_rs() >> 4) & 1) == 1) {    //COP2 imm25 command
                /*if (gte.currentCommand == null) {

                    gte.loadCommand(instruction);

                }
                else {
                    stall();
                }*/

                cpu.gte.execute(instruction);

                return;
            }

            //GTE registers reads/writes have delay of 1 (?) instruction

            switch (instruction.get_rs()) {
                
                case 0b00000:   //MFC
                    /*if (gte.currentCommand == null) {
                        //If we are loading to a register which was loaded in a delay slot, the first load is completely calnceled 
                        if (regs[instruction.get_rt()] != outRegs[instruction.get_rt()]) {
                            outRegs[instruction.get_rt()] = regs[instruction.get_rt()];
                        }

                        pendingload.Item1 = instruction.get_rt();
                        pendingload.Item2 = gte.read(instruction.get_rd());
                    }
                    else {
                        stall();
                    }*/

                    cpu.pendingload.Item1 = instruction.get_rt();
                    cpu.pendingload.Item2 = cpu.gte.read(instruction.get_rd());
                    break;

                case 0b00010:   //CFC
                    /*if (gte.currentCommand == null) {

                        //If we are loading to a register which was loaded in a delay slot, the first load is completely calnceled 
                        if (regs[instruction.get_rt()] != outRegs[instruction.get_rt()]) {
                            outRegs[instruction.get_rt()] = regs[instruction.get_rt()];
                        }

                        pendingload.Item1 = instruction.get_rt();
                        pendingload.Item2 = gte.read(instruction.get_rd() + 32);
                    }
                    else {
                        stall();
                    }*/

                    cpu.pendingload.Item1 = instruction.get_rt();
                    cpu.pendingload.Item2 = cpu.gte.read(instruction.get_rd() + 32);
                    break;

                case 0b00110:  //CTC 

                    uint rd = instruction.get_rd();
                    uint value = cpu.regs[instruction.get_rt()];
                    cpu.gte.write(rd + 32,value);

                    break;

                case 0b00100:  //MTC 

                    rd = instruction.get_rd();
                    value = cpu.regs[instruction.get_rt()];
                    cpu.gte.write(rd, value);   //Same as CTC but without adding 32 to the position

                    break;


                default:

                    throw new Exception("Unhandled GTE opcode: " + instruction.get_rs().ToString("X"));
            }
        }


        private static void cop3(CPU cpu, Instruction instruction) {

            exception(cpu,CoprocessorError);

        }

        private static void cop1(CPU cpu, Instruction instruction) {

            exception(cpu,CoprocessorError);

        }

        private static void xori(CPU cpu, Instruction instruction) {
            UInt32 targetReg = instruction.get_rt();
            UInt32 value = instruction.getImmediateValue();
            UInt32 rs = instruction.get_rs();

            cpu.outRegs[targetReg] = cpu.regs[rs] ^ value;
        }

        private static void lh(CPU cpu, Instruction instruction) {
            UInt32 addressRegPos = instruction.signed_imm();
            UInt32 base_ = instruction.get_rs();
            UInt32 final_address = cpu.regs[base_] + addressRegPos;

            //aligned?
            Int16 hw = (Int16)cpu.bus.load16(final_address);
            if (final_address % 2 == 0) {

                //If we are loading to a register which was loaded in a delay slot, the first load is completely calnceled 
                if (cpu.regs[instruction.get_rt()] != cpu.outRegs[instruction.get_rt()]) {
                    cpu.outRegs[instruction.get_rt()] = cpu.regs[instruction.get_rt()];
                }

                cpu.pendingload.Item1 = instruction.get_rt();  //Position
                cpu.pendingload.Item2 = (UInt32)(hw);           //Value

            }
            else {
                exception(cpu,LoadAddressError);
            }


        }

        private static void lhu(CPU cpu, Instruction instruction) {

            UInt32 addressRegPos = instruction.signed_imm();
            UInt32 base_ = instruction.get_rs();
            UInt32 final_address = cpu.regs[base_] + addressRegPos;

            if (final_address % 2 == 0) {
                UInt32 hw = (UInt32)cpu.bus.load16(final_address);

                //If we are loading to a register which was loaded in a delay slot, the first load is completely calnceled 
                if (cpu.regs[instruction.get_rt()] != cpu.outRegs[instruction.get_rt()]) {
                    cpu.outRegs[instruction.get_rt()] = cpu.regs[instruction.get_rt()];
                }

                cpu.pendingload.Item1 = instruction.get_rt();  //Position
                cpu.pendingload.Item2 = hw;                    //Value

            }

            else {
                exception(cpu,LoadAddressError);
            }


        }

        private static void sltiu(CPU cpu, Instruction instruction) {

            if (cpu.regs[instruction.get_rs()] < instruction.signed_imm()) {

                cpu.outRegs[instruction.get_rt()] = 1;
            }
            else {

                cpu.outRegs[instruction.get_rt()] = 0;

            }


        }

      

        private static void sub(CPU cpu, Instruction instruction) {

            Int32 reg1 = (Int32)cpu.regs[instruction.get_rs()];
            Int32 reg2 = (Int32)cpu.regs[instruction.get_rt()];

            try {
                Int32 value = checked(reg1 - reg2);        //Check for signed integer overflow 

                cpu.outRegs[instruction.get_rd()] = (UInt32)value;

            }
            catch (OverflowException) {
                exception(cpu,Overflow);

            }



        }

        private static void mult(CPU cpu, Instruction instruction) {
            //Sign extend
            Int64 a = (Int64) ((Int32)cpu.regs[instruction.get_rs()]);
            Int64 b = (Int64) ((Int32)cpu.regs[instruction.get_rt()]);


            UInt64 v = (UInt64)(a * b);

            cpu.HI = (UInt32)(v >> 32);
            cpu.LO = (UInt32)(v);

        }

        private static void break_(CPU cpu, Instruction instruction) {

            exception(cpu,Break);

        }

        private static void xor(CPU cpu, Instruction instruction) {

            cpu.outRegs[instruction.get_rd()] = cpu.regs[instruction.get_rs()] ^ cpu.regs[instruction.get_rt()];


        }

        private static void multu(CPU cpu, Instruction instruction) {

            UInt64 a = (UInt64)cpu.regs[instruction.get_rs()];
            UInt64 b = (UInt64)cpu.regs[instruction.get_rt()];


            UInt64 v = a * b;

            cpu.HI = (UInt32)(v >> 32);
            cpu.LO = (UInt32)(v);


        }

        private static void srlv(CPU cpu, Instruction instruction) {

            cpu.outRegs[instruction.get_rd()] = cpu.regs[instruction.get_rt()] >> ((Int32)(cpu.regs[instruction.get_rs()] & 0x1f));

        }

        private static void srav(CPU cpu, Instruction instruction) {

            Int32 val = ((Int32)cpu.regs[instruction.get_rt()]) >> ((Int32)(cpu.regs[instruction.get_rs()] & 0x1f));

            cpu.outRegs[instruction.get_rd()] = (UInt32)val;


        }

        private static void nor(CPU cpu, Instruction instruction) {

            cpu.outRegs[instruction.get_rd()] = ~(cpu.regs[instruction.get_rs()] | cpu.regs[instruction.get_rt()]);


        }

        private static void sllv(CPU cpu, Instruction instruction) {                             //take 5 bits from register rs

            cpu.outRegs[instruction.get_rd()] = cpu.regs[instruction.get_rt()] << ((Int32)(cpu.regs[instruction.get_rs()] & 0x1f));

        }

        private static void mthi(CPU cpu, Instruction instruction) {

            cpu.HI = cpu.regs[instruction.get_rs()];

        }

        private static void mtlo(CPU cpu, Instruction instruction) {

            cpu.LO = cpu.regs[instruction.get_rs()];

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
         

                if (((Int32)cpu.regs[instruction.get_rs()]) < ((Int32)cpu.regs[instruction.get_rt()])) {

                    cpu.outRegs[instruction.get_rd()] = 1;

                }
                else {
                    cpu.outRegs[instruction.get_rd()] = 0;

                }

            }


        private static void divu(CPU cpu, Instruction instruction) {

            UInt32 numerator = cpu.regs[instruction.get_rs()];
            UInt32 denominator = cpu.regs[instruction.get_rt()];

            if (denominator == 0) {
                cpu.LO = 0xffffffff;
                cpu.HI = (UInt32)numerator;
                return;
            }

            cpu.LO = (UInt32)(numerator / denominator);
            cpu.HI = (UInt32)(numerator % denominator);


        }

        private static void srl(CPU cpu, Instruction instruction) {


            //Right Shift (Logical)

            UInt32 val = cpu.regs[instruction.get_rt()];
            UInt32 shift = instruction.get_sa();

            cpu.outRegs[instruction.get_rd()] = (val >> (Int32)shift);

        }

        private static void mflo(CPU cpu, Instruction instruction) { //LO -> GPR[rd]

            cpu.outRegs[instruction.get_rd()] = cpu.LO;

        }
        private static void mfhi(CPU cpu, Instruction instruction) {        //HI -> GPR[rd]
            cpu.outRegs[instruction.get_rd()] = cpu.HI;
        }

        private static void div(CPU cpu, Instruction instruction) { // GPR[rs] / GPR[rt] -> (HI, LO) 

            Int32 numerator = (Int32)cpu.regs[instruction.get_rs()];
            Int32 denominator = (Int32)cpu.regs[instruction.get_rt()];

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
            

        }

        private static void sra(CPU cpu, Instruction instruction) {

            //Right Shift (Arithmetic)


            Int32 val = (Int32)cpu.regs[instruction.get_rt()];
            Int32 shift = (Int32)instruction.get_sa();

            cpu.outRegs[instruction.get_rd()] = ((UInt32)(val >> shift)); 


        }

        private static void slti(CPU cpu, Instruction instruction) {

            Int32 si = (Int32)instruction.signed_imm();
            Int32 rg = (Int32)cpu.regs[instruction.get_rs()];
          
                if (rg<si) {

                cpu.outRegs[instruction.get_rt()] = 1;
              
                 }
            else {

                cpu.outRegs[instruction.get_rt()] = 0;
              

            }
        }

        private static void bxx(CPU cpu,Instruction instruction) {         //*
            uint value = (uint)instruction.getfull();
            
            if (((value >> 17) & 0xF) == 0x8) {

                cpu.outRegs[31] = cpu.next_pc;         //Store return address if the value of bits [20:17] == 0x80
            }


            if (((value >> 16) & 1) == 1) {
                //BGEZ

                if ((Int32)cpu.regs[instruction.get_rs()] >= 0) {
                    branch(cpu,instruction.signed_imm());
                }

            }
            else {
                //BLTZ

                if ((Int32)cpu.regs[instruction.get_rs()] < 0) {
                    branch(cpu,instruction.signed_imm());
                }

            }


        }

        private static void lbu(CPU cpu, Instruction instruction) {
           
            UInt32 addressRegPos = instruction.signed_imm();
            UInt32 base_ = instruction.get_rs();

            byte b = cpu.bus.load8(cpu.regs[base_] + addressRegPos);

            //If we are loading to a register which was loaded in a delay slot, the first load is completely calnceled 
            if (cpu.regs[instruction.get_rt()] != cpu.outRegs[instruction.get_rt()]) {
                cpu.outRegs[instruction.get_rt()] = cpu.regs[instruction.get_rt()];
            }

            cpu.pendingload.Item1 = instruction.get_rt();  //Position
            cpu.pendingload.Item2 = (UInt32)b;    //Value

        }

        private static void blez(CPU cpu, Instruction instruction) {

            Int32 signedValue = (Int32)cpu.regs[instruction.get_rs()];

            if (signedValue <= 0) {

                branch(cpu,instruction.signed_imm());

            }


        }

        private static void bgtz(CPU cpu, Instruction instruction) {     //Branch if > 0

            Int32 signedValue = (Int32)cpu.regs[instruction.get_rs()];      

            if (signedValue > 0) {

                branch(cpu,instruction.signed_imm());

            }

          
        }


        private static void subu(CPU cpu, Instruction instruction) {

            cpu.outRegs[instruction.get_rd()] = cpu.regs[instruction.get_rs()] - cpu.regs[instruction.get_rt()];
        }

        private static void jalr(CPU cpu, Instruction instruction) {

            // Store return address in reg rd
            cpu.outRegs[instruction.get_rd()] = cpu.next_pc;

            // Jump to address in reg rs
            cpu.next_pc = cpu.regs[instruction.get_rs()];
            cpu._branch = true;
        }

        private static void beq(CPU cpu, Instruction instruction) {
          
            if (cpu.regs[instruction.get_rs()].Equals(cpu.regs[instruction.get_rt()])) {
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

            sbyte sb = (sbyte)cpu.bus.load8(cpu.regs[base_] + addressRegPos);

            //If we are loading to a register which was loaded in a delay slot, the first load is completely calnceled 
            if (cpu.regs[instruction.get_rt()] != cpu.outRegs[instruction.get_rt()]) {
                cpu.outRegs[instruction.get_rt()] = cpu.regs[instruction.get_rt()];
            }

            cpu.pendingload.Item1 = instruction.get_rt();  //Position
            cpu.pendingload.Item2 = (UInt32)sb;           //Value

        }

        private static void sb(CPU cpu, Instruction instruction) {

            if ((cpu.SR & 0x10000) != 0) {

               // Debug.WriteLine("store ignored, cache is isolated");      //Ignore write when cache is isolated 
                return;
            }

            UInt32 targetReg = instruction.get_rt();

            UInt32 addressRegPos = instruction.signed_imm();
            UInt32 base_ = instruction.get_rs();

            cpu.bus.store8(cpu.regs[base_] + addressRegPos, (byte)cpu.regs[targetReg]);



        }

        private static void andi(CPU cpu,Instruction instruction) {
            UInt32 targetReg = instruction.get_rt();
            UInt32 value = instruction.getImmediateValue();
            UInt32 rs = instruction.get_rs();


            cpu.outRegs[targetReg] = cpu.regs[rs] & value;
            
        }

        private static void jal(CPU cpu, Instruction instruction) {

            cpu.outRegs[31] = cpu.next_pc;             //Jump and link, store the PC to return to it later

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
            UInt32 final_address = cpu.regs[base_] + addressRegPos;

            //Address must be 16 bit aligned
            if (final_address % 2 == 0) {
                cpu.bus.store16(final_address, (UInt16)cpu.regs[targetReg]);
            }
            else {
                exception(cpu,StoreAddressError);
            }

        }

        private static void addi(CPU cpu, Instruction instruction) {

            Int32 imm = (Int32)(instruction.signed_imm());
            Int32 s = (Int32)(cpu.regs[instruction.get_rs()]);
            try {
                Int32 value = checked(imm + s);        //Check for signed integer overflow 

                cpu.outRegs[instruction.get_rt()] = (UInt32)value;
            }
            catch (OverflowException) {
                exception(cpu, Overflow);
            }

        }

        public static void lui(CPU cpu, Instruction instruction) {
            UInt32 targetReg = instruction.get_rt();
            UInt32 value = instruction.getImmediateValue();


            cpu.outRegs[targetReg] = value << 16;
         
        }

        public static void ori(CPU cpu, Instruction instruction) {
            UInt32 targetReg = instruction.get_rt();
            UInt32 value = instruction.getImmediateValue();
            UInt32 rs = instruction.get_rs();

            cpu.outRegs[targetReg] = cpu.regs[rs] | value;
           

        }
        public static void or(CPU cpu, Instruction instruction) {

            cpu.outRegs[instruction.get_rd()] = cpu.regs[instruction.get_rs()] | cpu.regs[instruction.get_rt()];
           

        }
        private static void and(CPU cpu, Instruction instruction) {
            cpu.outRegs[instruction.get_rd()] = cpu.regs[instruction.get_rs()] & cpu.regs[instruction.get_rt()];
           
        }
        public static void sw(CPU cpu, Instruction instruction) {
           
            if ((cpu.SR & 0x10000) != 0) {

               // Debug.WriteLine("store ignored, cache is isolated");      //Ignore write, the writing should be on the cache 
                return; 
            }       

            UInt32 targetReg = instruction.get_rt();

            UInt32 addressRegPos = instruction.signed_imm();
            UInt32 base_ = instruction.get_rs();
            UInt32 final_address = cpu.regs[base_] + addressRegPos;

            //Address must be 32 bit aligned
            if (final_address % 4 == 0) {

                //if (final_address == 0x80083C58) {Debug.WriteLine("loaded " + regs[targetReg].ToString("x") + " from reg: " + targetReg); }

                cpu.bus.store32(final_address, cpu.regs[targetReg]);
            }
            else {
                exception(cpu,StoreAddressError);
            }

        }
        public static void lw(CPU cpu, Instruction instruction) {
            
            if ((cpu.SR & 0x10000) != 0) {     //Can be removed?

                //Debug.WriteLine("loading from memory ignored, cache is isolated");      
                return;
            }

            UInt32 addressRegPos = instruction.signed_imm();
            UInt32 base_ = instruction.get_rs();
            UInt32 final_address = cpu.regs[base_] + addressRegPos;


            //Address must be 32 bit aligned
            if (final_address % 4 == 0) {

                //If we are loading to a register which was loaded in a delay slot, the first load is completely calnceled 
                if (cpu.regs[instruction.get_rt()] != cpu.outRegs[instruction.get_rt()]) {
                    cpu.outRegs[instruction.get_rt()] = cpu.regs[instruction.get_rt()];
                }

                cpu.pendingload.Item1 = instruction.get_rt();          //Position
                cpu.pendingload.Item2 = cpu.bus.load32(final_address);    //Value

              
            }
            else {
                exception(cpu,LoadAddressError);
            }
           
        }
        
        private static void add(CPU cpu, Instruction instruction) {

            Int32 reg1 = (Int32)cpu.regs[instruction.get_rs()];       
            Int32 reg2 = (Int32)cpu.regs[instruction.get_rt()];

            try {
                Int32 value = checked(reg1 + reg2);        //Check for signed integer overflow 

                cpu.outRegs[instruction.get_rd()] = (UInt32)value;

            }
            catch (OverflowException) {
                exception(cpu,Overflow);    
            
            }

          
        }

        private static void jr(CPU cpu, Instruction instruction) {

            cpu.next_pc = cpu.regs[instruction.get_rs()];      //Return or Jump to address in register 
            cpu._branch = true;
           
        }

        private static void addu(CPU cpu, Instruction instruction) {


            cpu.outRegs[instruction.get_rd()] = cpu.regs[instruction.get_rs()] + cpu.regs[instruction.get_rt()];
          
        }

        private static void sltu(CPU cpu, Instruction instruction) {
            if (cpu.regs[instruction.get_rs()] < cpu.regs[instruction.get_rt()]) { //Int32 ?

                cpu.outRegs[instruction.get_rd()] = (UInt32) 1;

            }
            else {
                cpu.outRegs[instruction.get_rd()] = (UInt32) 0;

            }

          
        }

        public static void sll(CPU cpu,Instruction instruction) {

            cpu.outRegs[instruction.get_rd()] = cpu.regs[instruction.get_rt()] << (Int32)instruction.get_sa();
           
        }
        private static void addiu(CPU cpu, Instruction instruction) {

            cpu.outRegs[instruction.get_rt()] = cpu.regs[instruction.get_rs()] + instruction.signed_imm();
            

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
            //If we are loading to a register which was loaded in a delay slot, the first load is completely calnceled 
            if (cpu.regs[instruction.get_rt()] != cpu.outRegs[instruction.get_rt()]) {
                cpu.outRegs[instruction.get_rt()] = cpu.regs[instruction.get_rt()];
            }

            cpu.pendingload.Item1 = instruction.get_rt();

            switch (instruction.get_rd()) {
                //MFC has load delay

                case 12:

                    cpu.pendingload.Item2 = cpu.SR;

                    break;

                case 13:

                    cpu.pendingload.Item2 = cpu.cause;

                    break;

                case 14:

                    cpu.pendingload.Item2 = cpu.epc;

                    break;

                default:
                    return;
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

                    if (cpu.regs[instruction.get_rt()] != 0) {

                        throw new Exception("Unhandled write to cop0 register: " + instruction.get_rd());

                    }

                    break;

                case 12:

                    cpu.SR = cpu.regs[instruction.get_rt()];            //Setting the status register's 16th bit

                    break;

                case 13:
                    //cause register, mostly read-only data describing the
                    //cause of an exception. Apparently only bits[9:8] are writable
                    if (cpu.regs[instruction.get_rt()] != 0) { 

                        throw new Exception("Unhandled write to CAUSE register: " + instruction.get_rd());

                    }

                    break;

                default:
                    
                    throw new Exception("Unhandled cop0 register: " + instruction.get_rd());
            }
          



        }
        private static void bne(CPU cpu, Instruction instruction) {

            if (!cpu.regs[instruction.get_rs()].Equals(cpu.regs[instruction.get_rt()])) {
                branch(cpu,instruction.signed_imm());
            }


        }

        private static void branch(CPU cpu, UInt32 offset) {
            offset = offset << 2;
            cpu.next_pc = cpu.next_pc + offset;
            cpu.next_pc = cpu.next_pc - 4;        //Cancel the +4 from the emu cycle 
            cpu._branch = true;
                    
            
        }

       
    }
}
