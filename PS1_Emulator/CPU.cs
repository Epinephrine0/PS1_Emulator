using OpenTK.Graphics.OpenGL;
using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace PS1_Emulator {
    public class CPU {
        private UInt32 pc;                //32-bit program counter
        private UInt32 next_pc;
        private UInt32 current_pc;
        public Interconnect bus;
        private UInt32[] regs;
        private UInt32[] outRegs;
        Instruction current;
        private UInt32 SR;           //cop0 reg12 , the status register 
        private UInt32 cause;        //cop0 reg13 , the cause register 
        private UInt32 epc;          //cop0 reg14, epc

        (UInt32, UInt32) pendingload; //to emulate the load delay

        private UInt32 HI;           //Remainder of devision
        private UInt32 LO;           //Quotient of devision

        private bool _branch;
        private bool delay_slot;



        public static int sync = 0;

        //Exception codes
        private const UInt32 IRQ = 0x0;
        private const UInt32 LoadAddressError = 0x4;
        private const UInt32 StoreAddressError = 0x5;
        private const UInt32 SysCall = 0x8;
        private const UInt32 Break = 0x9;
        private const UInt32 IllegalInstruction = 0xa;
        private const UInt32 CoprocessorError = 0xb;
        private const UInt32 Overflow = 0xc;
        byte[] testRom;

        bool openEvent = false;
        bool fastBoot = false;


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
            testRom = File.ReadAllBytes(@"C:\Users\Old Snake\Desktop\PS1\tests\gpu\triangle\triangle.exe");
        }

       
        public void emu_cycle() {   
         
            this.current_pc = pc;   //Save current pc In case of an exception


            intercept(pc);

            if (fastBoot) {       //Skip Sony logo, doesn't work 
                if (pc == 0x80030000) {
                    pc = regs[31];
                    next_pc = pc+4;
                    fastBoot = false;

                }
            }
           
            this.bus.TIMER1_tick();   
            this.bus.TIMER2_tick();    

            //PC must be 32 bit aligned 
            if (this.current_pc % 4 !=0) {
                exception(LoadAddressError);
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


            if (IRQ_CONTROL.isRequestingIRQ()) {                          //Interrupt check 
                cause |= 1 << 10;

                if (((SR & 1) != 0) && (((SR >> 10) & 1) != 0)) {
                    exception(IRQ);
                    return;
                }


            }

            decode_execute(current);
            if (openEvent) {
                Debug.WriteLine("$v0: " + outRegs[2].ToString("x"));
                openEvent= false;
            }
           

            outRegs[0] = 0;


            for (int i = 0; i < regs.Length; i++) {       
                regs[i] = outRegs[i];
            }

        }

        private void intercept(uint pc) {

            switch (pc) {
          /*      case 0x80030000:   //For executing EXEs
                    uint addressInRAM = (uint)(testRom[0x018] | (testRom[0x018 + 1] << 8) | (testRom[0x018 + 2] << 16) | (testRom[0x018 + 3] << 24));

                    for (int i = 0x800; i < testRom.Length; i++) {

                        bus.store8((uint)(addressInRAM), testRom[i]);
                        addressInRAM++;
                    }
                    pc = (uint)(testRom[0x10] | (testRom[0x10 + 1] << 8) | (testRom[0x10 + 2] << 16) | (testRom[0x10 + 3] << 24));
                    next_pc = pc + 4;

                    break;
          */
              
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
                            CDROM_trace(regs[9]);
                            break;

                        default:
                            if (bus.print) {
                                Debug.WriteLine("Function A: " + regs[9].ToString("x"));
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
                                Debug.WriteLine("TestEvent");
                                Debug.WriteLine("$a0: " + regs[4].ToString("X"));
                                Debug.WriteLine("$a1: " + regs[5].ToString("X"));
                                Debug.WriteLine("$a2: " + regs[6].ToString("X"));
                                Debug.WriteLine("$a3: " + regs[7].ToString("X"));
                               

                            }
                            
                            break;

                     
                        case 0x08:
                            //  if (bus.print) {
                            Debug.WriteLine("OpenEvent");
                            Debug.WriteLine("$a0: " + regs[4].ToString("X"));
                            Debug.WriteLine("$a1: " + regs[5].ToString("X"));
                            Debug.WriteLine("$a2: " + regs[6].ToString("X"));
                            Debug.WriteLine("$a3: " + regs[7].ToString("X"));

                            openEvent = true;
                            if (regs[4] == 0xF0000003) {
                                //cdevent = true;
                            }

                            // }


                            break;

                        default:
                            if (bus.print) {
                                Debug.WriteLine("Function B: " + regs[9].ToString("x"));
                            }
                            break;


                    }

                    break;
                case 0xC0:
                    if (bus.print) {
                        Debug.WriteLine("Function C: " + regs[9].ToString("x"));
                    }
                    break;
            }
        }
     
        public void CDROM_trace(uint func) {
            Debug.Write("CDROM: ");

            switch (func) {
                case 0xA4:
                    Debug.WriteLine("CdGetLbn");
                    break;

                case 0xA5:
                    Debug.WriteLine("CdReadSector");
                    break;

                case 0xA6:
                    Debug.WriteLine("CdGetStatus");
                    break;

                case 0x78:
                    Debug.WriteLine("CdAsyncSeekL");
                    break;

                case 0x7C:
                    Debug.WriteLine("CdAsyncGetStatus");
                    break;

                case 0x7E:
                    Debug.WriteLine("CdAsyncReadSector");
                    break;

                case 0x81:
                    Debug.WriteLine("CdAsyncSetMode");
                    break;

                case 0x94:
                    Debug.WriteLine("CdromGetInt5errCode");
                    break;

                case 0x54:
                case 0x71:
                    Debug.WriteLine("_96_init");
                    break;

                case 0x56:
                case 0x72:
                    Debug.WriteLine(" _96_remove");
                    break;

                case 0x90:
                    Debug.WriteLine("CdromIoIrqFunc1");
                    break;

                case 0x91:
                    Debug.WriteLine("CdromDmaIrqFunc1");
                    break;

                case 0x92:
                    Debug.WriteLine("CdromIoIrqFunc2");
                    break; 

                case 0x93:
                    Debug.WriteLine("CdromDmaIrqFunc2");
                    break;

                case 0x95:
                    Debug.WriteLine("CdInitSubFunc");
                    break;

                case 0x9E:
                    Debug.WriteLine("SetCdromIrqAutoAbort");
                    break;

                case 0xA2:
                    Debug.WriteLine("EnqueueCdIntr");
                    break;

                case 0xA3:
                    Debug.WriteLine("DequeueCdIntr");
                    break;

                default:
                    Debug.WriteLine("Unknown function: A(0x" + func.ToString("x")+")");
                    break;



            }



        }
        public void SPUtick() {

            this.bus.SPU_Tick(sync);        //SPU Clock


        }
        public void IOtick() {
            bus.IOports_tick();
        }

        public void GPUtick() {
           double cycles = (double)sync * (double)11 / 7;
            
           bus.GPU_tick((int)cycles);
        }
        internal void CDROMtick() {
            bus.CDROM_tick(sync);

        }

        public static void incrementSynchronizer() {
            CPU.sync++;

        }
       
        public void decode_execute(Instruction instruction) {
            //TODO: Use function pointers instead of the switch

            switch (instruction.getType()) {

                case 0b001111:

                    op_lui(instruction);
                    break;

                case 0b001101:

                    op_ori(instruction);
                    break;

                case 0b101011:

                    op_sw(instruction);
                    break;

                case 0b000000:

                    op_special(instruction);
                    break;

                case 0b001001:

                    op_addiu(instruction);
                    break;

                case 0b000010:

                    op_jump(instruction);
                    break;

                case 0b010000:

                    op_cop0(instruction);
                    break;

                case 0b000101:

                    op_bne(instruction);
                    break;

                case 0b001000:

                    op_addi(instruction);
                    break;

                case 0b100011:

                    op_lw(instruction);
                    break;

                case 0b101001:

                    op_sh(instruction);

                    break;

                case 0b000011:

                    op_jal(instruction);
                    break;

                case 0b001100:

                    op_andi(instruction);
                    break;

                case 0b101000:

                    op_sb(instruction);
                    break;

                case 0b100000:

                    op_lb(instruction);
                    break;


                case 0b000100:

                    op_beq(instruction);
                    break;

                case 0b000111:

                    op_bgtz(instruction);
                    break;

                case 0b000110:

                    op_blez(instruction);

                    break;

                case 0b100100:

                    op_lbu(instruction);
                    break;

                case 0b000001:

                    op_bxx(instruction);
                    break;

                case 0b001010:

                    op_slti(instruction);
                    break;

                case 0b001011:

                    op_sltiu(instruction);
                    break;

                case 0b100101:

                    op_lhu(instruction);
                    break;

                case 0b100001:

                    op_lh(instruction);
                    break;

                case 0xe:

                    op_xori(instruction);
                    break;

                case 0x11:

                    op_cop1(instruction);
                    break;

                case 0x12:

                    op_cop2(instruction);
                    break;

                case 0x13:

                    op_cop3(instruction);
                    break;

                case 0x22:

                    op_lwl(instruction);
                    break;

                case 0x26:

                    op_lwr(instruction);
                    break;

                case 0x2a:

                    op_swl(instruction);
                    break;

                case 0x2e:

                    op_swr(instruction);
                    break;

                case 0x30:

                    op_lwc0(instruction);
                    break;

                case 0x31:

                    op_lwc1(instruction);
                    break;

                case 0x32:

                    op_lwc2(instruction);
                    break;

                case 0x33:

                    op_lwc3(instruction);
                    break;

                case 0x38:

                    op_swc0(instruction);
                    break;

                case 0x39:

                    op_swc1(instruction);
                    break;

                case 0x40:

                    op_swc2(instruction);
                    break;

                case 0x41:

                    op_swc3(instruction);

                    break;

                case 0x3A:

                    op_swc2_pre_release6(instruction);
                    break;

                default:

                    op_illegal(instruction);
                    break;

            }


        }


        private void op_special(Instruction instruction) {
            switch (instruction.get_subfunction()) {

                case 0b000000:

                    op_sll(instruction);

                    break;

                case 0b100101:

                    op_OR(instruction);

                    break;

                case 0b100100:

                    op_AND(instruction);

                    break;

                case 0b101011:

                    op_stlu(instruction);

                    break;

                case 0b100001:

                    op_addu(instruction);
                    break;

                case 0b001000:

                    op_jr(instruction);
                    break;

                case 0b100000:

                    op_add(instruction);
                    break;


                case 0b001001:

                    op_jalr(instruction);
                    break;

                case 0b100011:

                    op_subu(instruction);
                    break;

                case 0b000011:


                    op_sra(instruction);
                    break;

                case 0b011010:

                    op_div(instruction);
                    break;

                case 0b010010:

                    mflo(instruction);
                    break;

                case 0b000010:

                    srl(instruction);
                    break;

                case 0b011011:

                    op_divu(instruction);
                    break;

                case 0b010000:

                    op_mfhi(instruction);
                    break;

                case 0b101010:

                    op_slt(instruction);
                    break;

                case 0b001100:

                    op_syscall(instruction);
                    break;

                case 0b010011:

                    op_mtlo(instruction);

                    break;

                case 0b010001:

                    op_mthi(instruction);

                    break;

                case 0b000100:

                    op_sllv(instruction);
                    break;

                case 0b100111:

                    op_nor(instruction);
                    break;

                case 0b000111:

                    op_srav(instruction);
                    break;

                case 0b000110:

                    op_srlv(instruction);
                    break;

                case 0b011001:

                    op_multu(instruction);
                    break;

                case 0b100110:

                    op_xor(instruction);
                    break;

                case 0xd:

                    op_break(instruction);
                    break;

                case 0x18:

                    op_mult(instruction);
                    break;

                case 0x22:

                    op_sub(instruction);

                    break;

                case 0x0E:
                    op_illegal(instruction);

                    break;


                default:

                    throw new Exception("Unhandeled special instruction [ " + instruction.getfull().ToString("X").PadLeft(8, '0') + " ] with subfunction: " + Convert.ToString(instruction.get_subfunction(), 2).PadLeft(6, '0'));

            }

        }
        private void op_cop0(Instruction instruction) {

            switch (instruction.get_rs()) {

                case 0b00100:

                    op_mtc0(instruction);

                    break;


                case 0b00000:

                    op_mfc0(instruction);

                    break;

                case 0b10000:

                    op_rfe(instruction);


                    break;

                default:

                    throw new Exception("Unhandled cop0 instruction: " + instruction.getfull().ToString("X"));
            }


        }
        private void op_illegal(Instruction instruction) {
            Debug.WriteLine("Illegal instruction: " + instruction.getfull().ToString("X").PadLeft(8,'0'));
            Debug.WriteLine("PC: " + pc.ToString("x"));

            exception(IllegalInstruction);

        }

        private void op_swc3(Instruction instruction) {
            exception(CoprocessorError); //StoreWord is not supported in this cop
        }
        private void op_swc2_pre_release6(Instruction instruction) {
            //Debug.WriteLine("StoreWord cop2 [GTE] instruction ignored");
        }

        private void op_swc2(Instruction instruction) {
            //Debug.WriteLine("StoreWord cop2 [GTE] instruction ignored");
        }


        private void op_swc1(Instruction instruction) {
            exception(CoprocessorError); //StoreWord is not supported in this cop
        }

        private void op_swc0(Instruction instruction) {
            exception(CoprocessorError); //StoreWord is not supported in this cop
        }

        private void op_lwc3(Instruction instruction) {
            exception(CoprocessorError); //LoadWord is not supported in this cop
        }

        private void op_lwc2(Instruction instruction) {

          //  Debug.WriteLine("LoadWord cop2 [GTE] instruction ignored");
        }

        private void op_lwc1(Instruction instruction) {
            exception(CoprocessorError); //LoadWord is not supported in this cop
        }

        private void op_lwc0(Instruction instruction) {
            exception(CoprocessorError); //LoadWord is not supported in this cop
        }

        private void op_swr(Instruction instruction) {
            UInt32 addressRegPos = instruction.signed_imm();
            UInt32 base_ = instruction.get_rs();
            UInt32 final_address = regs[base_] + addressRegPos;


            UInt32 value = regs[instruction.get_rt()];                           //Bypass load delay
            UInt32 current_value = bus.load32((UInt32)(final_address & (~3)));     //Last 2 bits are for alignment position only 

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

            bus.store32((UInt32)(final_address & (~3)), finalValue);
        }

        private void op_swl(Instruction instruction) {
            UInt32 addressRegPos = instruction.signed_imm();
            UInt32 base_ = instruction.get_rs();
            UInt32 final_address = regs[base_] + addressRegPos;


            UInt32 value = regs[instruction.get_rt()];                           //Bypass load delay
            UInt32 current_value = bus.load32((UInt32)(final_address&(~3)));     //Last 2 bits are for alignment position only 

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

            bus.store32((UInt32)(final_address & (~3)), finalValue);


        }

        private void op_lwr(Instruction instruction) {

            UInt32 addressRegPos = instruction.signed_imm();
            UInt32 base_ = instruction.get_rs();
            UInt32 final_address = regs[base_] + addressRegPos;


            UInt32 current_value = outRegs[instruction.get_rt()];       //Bypass load delay
            UInt32 word = bus.load32((UInt32)(final_address & (~3)));     //Last 2 bits are for alignment position only 

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

            pendingload.Item1 = instruction.get_rt();  //Position
            pendingload.Item2 = finalValue;           //Value

        }

        private void op_lwl(Instruction instruction) {

            UInt32 addressRegPos = instruction.signed_imm();
            UInt32 base_ = instruction.get_rs();
            UInt32 final_address = regs[base_] + addressRegPos;


            UInt32 current_value = outRegs[instruction.get_rt()];       //Bypass load delay
            UInt32 word = bus.load32((UInt32)(final_address&(~3)));     //Last 2 bits are for alignment position only 

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

            pendingload.Item1 = instruction.get_rt();  //Position
            pendingload.Item2 = finalValue;           //Value

        }

        private void op_cop2(Instruction instruction) {
            //throw new NotImplementedException("Cop2 instruction");
            //Debug.WriteLine("ignoring Cop2 (GTE) instruction");
        }

        private void op_cop3(Instruction instruction) {

            exception(CoprocessorError);

        }

        private void op_cop1(Instruction instruction) {

            exception(CoprocessorError);

        }

        private void op_xori(Instruction instruction) {
            UInt32 targetReg = instruction.get_rt();
            UInt32 value = instruction.getImmediateValue();
            UInt32 rs = instruction.get_rs();

            outRegs[targetReg] = regs[rs] ^ value;
        }

        private void op_lh(Instruction instruction) {
            UInt32 addressRegPos = instruction.signed_imm();
            UInt32 base_ = instruction.get_rs();
            UInt32 final_address = regs[base_] + addressRegPos;

            //aligned?
            Int16 hw = (Int16)bus.load16(final_address);

            pendingload.Item1 = instruction.get_rt();  //Position
            pendingload.Item2 = (UInt32) hw;           //Value
            

           
        }

        private void op_lhu(Instruction instruction) {

            UInt32 addressRegPos = instruction.signed_imm();
            UInt32 base_ = instruction.get_rs();
            UInt32 final_address = regs[base_] + addressRegPos;

            if (final_address % 2 == 0) {
                UInt32 hw = (UInt32) bus.load16(final_address);

                pendingload.Item1 = instruction.get_rt();  //Position
                pendingload.Item2 = hw;                    //Value

            }

            else {
                exception(LoadAddressError);
            }


        }

        private void op_sltiu(Instruction instruction) {

            if (this.regs[instruction.get_rs()] < instruction.signed_imm()) {

                this.outRegs[instruction.get_rt()] = 1;
            }
            else {

                this.outRegs[instruction.get_rt()] = 0;

            }


        }

      

        private void op_sub(Instruction instruction) {

            Int32 reg1 = (Int32)regs[instruction.get_rs()];
            Int32 reg2 = (Int32)regs[instruction.get_rt()];

            try {
                Int32 value = checked(reg1 - reg2);        //Check for signed integer overflow 

                outRegs[instruction.get_rd()] = (UInt32)value;

            }
            catch (OverflowException) {
                exception(Overflow);

            }



        }

        private void op_mult(Instruction instruction) {
            //Sign extend
            Int64 a = (Int64) ((Int32)regs[instruction.get_rs()]);
            Int64 b = (Int64) ((Int32)regs[instruction.get_rt()]);


            UInt64 v = (UInt64)(a * b);

            this.HI = (UInt32)(v >> 32);
            this.LO = (UInt32)(v);

        }

        private void op_break(Instruction instruction) {

            exception(Break);

        }

        private void op_xor(Instruction instruction) {

            outRegs[instruction.get_rd()] = regs[instruction.get_rs()] ^ regs[instruction.get_rt()];


        }

        private void op_multu(Instruction instruction) {

            UInt64 a = (UInt64)regs[instruction.get_rs()];
            UInt64 b = (UInt64)regs[instruction.get_rt()];


            UInt64 v = a * b;

            this.HI = (UInt32)(v >> 32);
            this.LO = (UInt32)(v);


        }

        private void op_srlv(Instruction instruction) {

         outRegs[instruction.get_rd()] = regs[instruction.get_rt()] >> ((Int32)(regs[instruction.get_rs()] & 0x1f));

        }

        private void op_srav(Instruction instruction) {

            Int32 val = ((Int32)regs[instruction.get_rt()]) >> ((Int32)(regs[instruction.get_rs()] & 0x1f));

            outRegs[instruction.get_rd()] = (UInt32)val;


        }

        private void op_nor(Instruction instruction) {

            outRegs[instruction.get_rd()] = ~(regs[instruction.get_rs()] | regs[instruction.get_rt()]);


        }

        private void op_sllv(Instruction instruction) {                             //take 5 bits from register rs

            outRegs[instruction.get_rd()] = regs[instruction.get_rt()] << ((Int32)(regs[instruction.get_rs()] & 0x1f));

        }

        private void op_mthi(Instruction instruction) {

            this.HI = regs[instruction.get_rs()];

        }

        private void op_mtlo(Instruction instruction) {

            this.LO = regs[instruction.get_rs()];

        }

        private void op_syscall(Instruction instruction) {
            exception(SysCall);
        }

        private void exception(UInt32 cause){

           
            UInt32 handler;                                         //Get the handler

            if ((this.SR & (1 << 22)) != 0) {
                handler = 0xbfc00180;

            }
            else {
                handler = 0x80000080;
            }
  

            UInt32 mode = this.SR & 0x3f;                          //Disable interrupts 

            this.SR = (UInt32)(this.SR & ~0x3f);

            this.SR = this.SR | ((mode << 2) & 0x3f);

            
            this.cause = cause << 2;                    //Update cause register

            this.epc = this.current_pc;                 //Save the current PC in register EPC

            if (delay_slot) {                   //in case an exception occurs in a delay slot
                this.epc = this.epc - 4;
                this.cause = (UInt32)(this.cause | (1 << 31));
            }

            this.pc = handler;                          //Jump to the handler address (no delay)
            this.next_pc = pc + 4;

        }

        private void op_slt(Instruction instruction) {
         

                if (((Int32)regs[instruction.get_rs()]) < ((Int32)regs[instruction.get_rt()])) {

                    outRegs[instruction.get_rd()] = 1;

                }
                else {
                    outRegs[instruction.get_rd()] = 0;

                }

            }


        private void op_divu(Instruction instruction) {

            UInt32 numerator = this.regs[instruction.get_rs()];
            UInt32 denominator = this.regs[instruction.get_rt()];

            if (denominator == 0) {
                this.LO = 0xffffffff;
                this.HI = (UInt32)numerator;
                return;
            }
            
            this.LO = (UInt32)(numerator / denominator);
            this.HI = (UInt32)(numerator % denominator);


        }

        private void srl(Instruction instruction) {


            //Right Shift (Logical)

            UInt32 val = this.regs[instruction.get_rt()];
            UInt32 shift = instruction.get_sa();

            this.outRegs[instruction.get_rd()] = (val >> (Int32)shift);

        }

        private void mflo(Instruction instruction) { //LO -> GPR[rd]

            this.outRegs[instruction.get_rd()] = this.LO;

        }
        private void op_mfhi(Instruction instruction) {        //HI -> GPR[rd]
            this.outRegs[instruction.get_rd()] = this.HI;
        }

        private void op_div(Instruction instruction) { // GPR[rs] / GPR[rt] -> (HI, LO) 

            Int32 numerator = (Int32)this.regs[instruction.get_rs()];
            Int32 denominator = (Int32)this.regs[instruction.get_rt()];

            if (numerator >= 0 && denominator == 0) {
                this.LO = 0xffffffff;
                this.HI = (UInt32)numerator;
                return;
            }
            else if (numerator < 0 && denominator == 0) {
                this.LO = 1;
                this.HI = (UInt32)numerator;
                return;
            }
            else if (numerator == 0x80000000 && denominator == 0xffffffff) {
            
                this.LO = 0x80000000;
                this.HI = 0;

                return;
            }

  
           this.LO = (UInt32) (numerator / denominator);
           this.HI = (UInt32) (numerator % denominator);


        }

        private void op_sra(Instruction instruction) {

            //Right Shift (Arithmetic)


            Int32 val = (Int32)this.regs[instruction.get_rt()];
            Int32 shift = (Int32)instruction.get_sa();

            this.outRegs[instruction.get_rd()] = ((UInt32)(val >> shift)); 


        }

        private void op_slti(Instruction instruction) {

            Int32 si = (Int32)instruction.signed_imm();
            Int32 rg = (Int32)this.regs[instruction.get_rs()];
          
                if (rg<si) {

                this.outRegs[instruction.get_rt()] = 1;
              
                 }
            else {

                this.outRegs[instruction.get_rt()] = 0;
              

            }
        }

        private void op_bxx(Instruction instruction) {         //*
            Int32 value = (Int32)instruction.getfull();
            
            if (((value >> 17) & 0xF) == 0x80) {

                this.outRegs[31] = this.next_pc;         //Store return address if the value of bits [20:17] == 0x80
            }


            if (((value >> 16) & 1) == 1) {
                //BGEZ

                if ((Int32)regs[instruction.get_rs()] >= 0) {
                    branch(instruction.signed_imm());
                }

            }
            else {
                //BLTZ

                if ((Int32)regs[instruction.get_rs()] < 0) {
                    branch(instruction.signed_imm());
                }

            }


        }

        private void op_lbu(Instruction instruction) {
           
            UInt32 addressRegPos = instruction.signed_imm();
            UInt32 base_ = instruction.get_rs();

            byte b = bus.load8(regs[base_] + addressRegPos);

            pendingload.Item1 = instruction.get_rt();  //Position
            pendingload.Item2 = (UInt32)b;    //Value

        }

        private void op_blez(Instruction instruction) {

            Int32 signedValue = (Int32)regs[instruction.get_rs()];

            if (signedValue <= 0) {

                branch(instruction.signed_imm());

            }


        }

        private void op_bgtz(Instruction instruction) {     //Branch if > 0

            Int32 signedValue = (Int32)regs[instruction.get_rs()];      

            if (signedValue > 0) {

                branch(instruction.signed_imm());

            }

          
        }


        private void op_subu(Instruction instruction) {

            this.outRegs[instruction.get_rd()] = regs[instruction.get_rs()] - regs[instruction.get_rt()];
        }

        private void op_jalr(Instruction instruction) {
            
            // Store return address in reg rd
            outRegs[instruction.get_rd()] = this.next_pc;

            // Jump to address in reg rs
            this.next_pc = regs[instruction.get_rs()];
            this._branch = true;
        }

        private void op_beq(Instruction instruction) {
          
            if (regs[instruction.get_rs()].Equals(regs[instruction.get_rt()])) {
                branch(instruction.signed_imm());
               
            }
            
        }

        private void op_lb(Instruction instruction) {

            if ((this.SR & 0x10000) != 0) {

               // Debug.WriteLine("loading from memory ignored, cache is isolated");
                return;
            }

            UInt32 addressRegPos = instruction.signed_imm();
            UInt32 base_ = instruction.get_rs();

            sbyte sb = (sbyte)bus.load8(regs[base_] + addressRegPos);

            pendingload.Item1 = instruction.get_rt();  //Position
            pendingload.Item2 = (UInt32) sb;    //Value



        }

        private void op_sb(Instruction instruction) {

            if ((this.SR & 0x10000) != 0) {

               // Debug.WriteLine("store ignored, cache is isolated");      //Ignore write when cache is isolated 
                return;
            }

            UInt32 targetReg = instruction.get_rt();

            UInt32 addressRegPos = instruction.signed_imm();
            UInt32 base_ = instruction.get_rs();

            bus.store8(regs[base_] + addressRegPos, (byte)regs[targetReg]);



        }

        private void op_andi(Instruction instruction) {
            UInt32 targetReg = instruction.get_rt();
            UInt32 value = instruction.getImmediateValue();
            UInt32 rs = instruction.get_rs();


            outRegs[targetReg] = regs[rs] & value;
            
        }

        private void op_jal(Instruction instruction) {

            this.outRegs[31] = this.next_pc;             //Jump and link, store the PC to return to it later

            op_jump(instruction);
        }

        private void op_sh(Instruction instruction) {

            if ((this.SR & 0x10000) != 0) {

               // Debug.WriteLine("store ignored, cache is isolated");      //Ignore write, the writing should be on the cache 
                return;
            }

            UInt32 targetReg = instruction.get_rt();

            UInt32 addressRegPos = instruction.signed_imm();
            UInt32 base_ = instruction.get_rs();
            UInt32 final_address = regs[base_] + addressRegPos;

            //Address must be 16 bit aligned
            if (final_address % 2 == 0) {
                bus.store16(final_address, (UInt16)regs[targetReg]);
            }
            else {
                exception(StoreAddressError);
            }

        }

        private void op_addi(Instruction instruction) {

            Int32 imm = (Int32)(instruction.signed_imm());
            Int32 s = (Int32)(regs[instruction.get_rs()]);
            try {
                Int32 value = checked(imm + s);        //Check for signed integer overflow 

                outRegs[instruction.get_rt()] = (UInt32)value;
            }
            catch (OverflowException) {
                exception(Overflow);
            }

        }

        public void op_lui(Instruction instruction) {
            UInt32 targetReg = instruction.get_rt();
            UInt32 value = instruction.getImmediateValue();


            outRegs[targetReg] = value << 16;
         
        }

        public void op_ori(Instruction instruction) {
            UInt32 targetReg = instruction.get_rt();
            UInt32 value = instruction.getImmediateValue();
            UInt32 rs = instruction.get_rs();

            outRegs[targetReg] = regs[rs] | value;
           

        }
        public void op_OR(Instruction instruction) {

            outRegs[instruction.get_rd()] = regs[instruction.get_rs()] | regs[instruction.get_rt()];
           

        }
        private void op_AND(Instruction instruction) {
            outRegs[instruction.get_rd()] = regs[instruction.get_rs()] & regs[instruction.get_rt()];
           
        }
        public void op_sw(Instruction instruction) {
           
            if ((this.SR & 0x10000) != 0) {

               // Debug.WriteLine("store ignored, cache is isolated");      //Ignore write, the writing should be on the cache 
                return; 
            }       

            UInt32 targetReg = instruction.get_rt();

            UInt32 addressRegPos = instruction.signed_imm();
            UInt32 base_ = instruction.get_rs();
            UInt32 final_address = regs[base_] + addressRegPos;

            //Address must be 32 bit aligned
            if (final_address % 4 == 0) {

                //if (final_address == 0x80083C58) {Debug.WriteLine("loaded " + regs[targetReg].ToString("x") + " from reg: " + targetReg); }

                bus.store32(final_address, regs[targetReg]);
            }
            else {
                exception(StoreAddressError);
            }

        }
        public void op_lw(Instruction instruction) {
            
            if ((this.SR & 0x10000) != 0) {     //Can be removed?

                Debug.WriteLine("loading from memory ignored, cache is isolated");      
                return;
            }

            UInt32 addressRegPos = instruction.signed_imm();
            UInt32 base_ = instruction.get_rs();
            UInt32 final_address = regs[base_] + addressRegPos;


            //Address must be 32 bit aligned
            if (final_address % 4 == 0) {
                pendingload.Item1 = instruction.get_rt();          //Position
                pendingload.Item2 = bus.load32(final_address);    //Value

              

            }
            else {
                exception(LoadAddressError);
            }
           
        }
        
        private void op_add(Instruction instruction) {

            Int32 reg1 = (Int32)regs[instruction.get_rs()];       
            Int32 reg2 = (Int32)regs[instruction.get_rt()];

            try {
                Int32 value = checked(reg1 + reg2);        //Check for signed integer overflow 

                outRegs[instruction.get_rd()] = (UInt32)value;

            }
            catch (OverflowException) {
                exception(Overflow);    
            
            }



          
        }

        private void op_jr(Instruction instruction) {

            this.next_pc = regs[instruction.get_rs()];      //Return or Jump to address in register 
            this._branch = true;
           
        }

        private void op_addu(Instruction instruction) {


            outRegs[instruction.get_rd()] = regs[instruction.get_rs()] + regs[instruction.get_rt()];
          
        }

        private void op_stlu(Instruction instruction) {
            if (regs[instruction.get_rs()] < regs[instruction.get_rt()]) { //Int32 ?

                outRegs[instruction.get_rd()] = (UInt32) 1;

            }
            else {
                outRegs[instruction.get_rd()] = (UInt32) 0;

            }

          
        }

        public void op_sll(Instruction instruction) {

            outRegs[instruction.get_rd()] = regs[instruction.get_rt()] << (Int32)instruction.get_sa();
           
        }
        private void op_addiu(Instruction instruction) {

            outRegs[instruction.get_rt()] = regs[instruction.get_rs()] + instruction.signed_imm();
            

        }

        private void op_jump(Instruction instruction) {

            next_pc = (next_pc & 0xf0000000) | (instruction.imm_jump() << 2);
            this._branch = true;


        }


        private void op_rfe(Instruction instruction) {
            if (instruction.get_subfunction() != 0b010000) {    //Check bits [5:0]
                throw new Exception("Invalid cop0 instruction: " + instruction.getfull().ToString("X"));
            }

            UInt32 mode = this.SR & 0x3f;                   //Enable interrupts
            this.SR = (uint)(this.SR & ~0x3f);
            this.SR = this.SR | (mode >> 2);
        }

        private void op_mfc0(Instruction instruction) {
            pendingload.Item1 = instruction.get_rt();

            switch (instruction.get_rd()) {
                //MFC has load delay

                case 12:
                    
                    pendingload.Item2 = this.SR;

                    break;

                case 13:

                    pendingload.Item2 = this.cause;

                    break;

                case 14:

                    pendingload.Item2 = this.epc;

                    break;

                default:

                    throw new Exception("Unhandled cop0 register: " + instruction.get_rd());
            }



        }

        private void op_mtc0(Instruction instruction) {

            switch (instruction.get_rd()) {

                case 3:
                case 5:                          //Breakpoints registers
                case 6:
                case 7:
                case 9:
                case 11:

                    if (regs[instruction.get_rt()] != 0) {

                        throw new Exception("Unhandled write to cop0 register: " + instruction.get_rd());

                    }

                    break;

                case 12:

                    this.SR = regs[instruction.get_rt()];            //Setting the status register's 16th bit

                    break;

                case 13:

                    if (regs[instruction.get_rt()] != 0) {

                        throw new Exception("Unhandled write to CAUSE register: " + instruction.get_rd());

                    }

                    break;

                default:
                    
                    throw new Exception("Unhandled cop0 register: " + instruction.get_rd());
            }
          



        }
        private void op_bne(Instruction instruction) {

            if (!regs[instruction.get_rs()].Equals(regs[instruction.get_rt()])) {
                branch(instruction.signed_imm());
            }


        }

        private void branch(UInt32 offset) {
            offset = offset << 2;
            this.next_pc = this.next_pc + offset;
            this.next_pc = this.next_pc - 4;        //Cancel the +4 from the emu cycle 
            this._branch = true;
                    
            
        }

       
    }
}
