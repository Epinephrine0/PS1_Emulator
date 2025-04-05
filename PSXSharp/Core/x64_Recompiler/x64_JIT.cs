using System;
using PSXSharp.Core.Common;
using Iced.Intel;
using System.IO;
using static Iced.Intel.AssemblerRegisters;
using static PSXSharp.Core.x64_Recompiler.AddressGetter;
using System.Collections.Generic;
using System.Security.Permissions;
using OpenTK.Graphics.ES20;

namespace PSXSharp.Core.x64_Recompiler {

    //Implements R3000 MIPS instructions in x64 assembly
    public static unsafe class x64_JIT {
        //Register usage:
        //EAX, ECX, EDX, EDI, ESI, R8 -> General Calculations
        //R15 -> Loading 64 bit immediate addresses
        //R12 - R14 Callee-saved registers

        //Prints a register value to the console
        //Destroys ecx!
        private static void EmitPrintReg(Assembler asm, AssemblerRegister32 src) {      
            asm.sub(rsp, 40);                       //Shadow space
            asm.mov(ecx, src);
            asm.mov(r15, GetPrintAddress());
            asm.call(r15);
            asm.add(rsp, 40);                       //Undo Shadow space 
        }

        //Destroys r15!
        private static void EmitRegisterRead(Assembler asm, AssemblerRegister32 dst, int srcNumber) {
            asm.mov(r15, GetGPRAddress(srcNumber));     //We use r15 ONLY for holding 64-bit addresses
            asm.mov(dst, __dword_ptr[r15]);
        }

        //Destroys r15!
        private static void EmitRegisterWrite(Assembler asm, int dstNumber, AssemblerRegister32 src, bool delayed) {
            ulong regNumber;
            ulong regValue;

            if (delayed) {
                regNumber = GetDelayedRegisterLoadNumberAddress();
                regValue = GetDelayedRegisterLoadValueAddress();
            } else {
                regNumber = GetDirectWriteNumberAddress();
                regValue = GetDirectWriteValueAddress();
            }

            asm.mov(r15, regNumber);                                              
            asm.mov(__dword_ptr[r15], dstNumber);
            asm.mov(r15, regValue);
            asm.mov(__dword_ptr[r15], src);
        }

        //Destroys eax!
        private static void EmitCheckCacheIsolation(Assembler asm) {
            //IscIsolateCache => (Cop0.SR & 0x10000) != 0
            asm.mov(r15, GetCOP0SRAddress());
            asm.mov(eax, __dword_ptr[r15]);
            asm.and(eax, 0x10000);
        }

        public static void EmitSyscall(Assembler asm) {
            //Call Exception function with Syscall Value (0x8) 
            asm.sub(rsp, 40);                       //Shadow space
            asm.mov(r15, GetExceptionAddress());    //Load function pointer
            asm.mov(rcx, GetCPUStructAddress());    //First Parameter
            asm.mov(edx, 0x8);                      //Second Parameter
            asm.call(r15);                          //Call Exception
            asm.add(rsp, 40);                       //Undo Shadow space 
        }

        public static void EmitBreak(Assembler asm) {
            //Call Exception function with Break Value (0x9) 
            asm.sub(rsp, 40);                       //Shadow space
            asm.mov(r15, GetExceptionAddress());    //Load function pointer
            asm.mov(rcx, GetCPUStructAddress());    //First Parameter
            asm.mov(edx, 0x9);                      //Second Parameter
            asm.call(r15);                          //Call Exception
            asm.add(rsp, 40);                       //Undo Shadow space 
        }

        public static void EmitSlti(int rs, int rt, uint imm, bool signed, Assembler asm) {    
            asm.mov(eax, 0);                                //Set result to 0 initially
            EmitRegisterRead(asm, ecx, rs);                 //Load GPR[rs]
            asm.mov(edx, imm);                              //Load imm
            asm.cmp(ecx, edx);                              //Compare 

            if (signed) {
                asm.setl(al);                               //Setl for signed
            } else {
                asm.setb(al);                               //Setb for unsigned
            }

            asm.movzx(eax, al);

            //Write to GPR[rt]
            EmitRegisterWrite(asm, rt, eax, false);
        }

        public static void EmitSlt(int rs, int rt, int rd, bool signed, Assembler asm) {      
            asm.mov(eax, 0);                                //Set result to 0 initially
            EmitRegisterRead(asm, ecx, rs);                 //Load GPR[rs]
            EmitRegisterRead(asm, edx, rt);                 //Load GPR[rt]
            asm.cmp(ecx, edx);                              //Compare 

            if (signed) {
                asm.setl(al);                               //Setl for signed
            } else {
                asm.setb(al);                               //Setb for unsigned
            }

            asm.movzx(eax, al);

            //Write to GPR[rd]
            EmitRegisterWrite(asm, rd, eax, false);
        }

        public static void EmitBranchIf(int rs, int rt, uint imm, int type, Assembler asm) {
            Label skipBranch = asm.CreateLabel();

            EmitRegisterRead(asm, ecx, rs);                                 //Load GPR[rs]

            if (type > 1) {
                asm.mov(edx, 0);                                            //Load 0, ignore the rt parameter 
            } else {
                EmitRegisterRead(asm, edx, rt);                             //Load GPR[rt]
            }

            asm.cmp(ecx, edx);                                              //Compare

            //The inverse means we don't branch
            switch (type) {
                //0,1 are comparing with GPR[rt] 
                case BranchIf.BEQ: asm.jne(skipBranch);  break;
                case BranchIf.BNE: asm.je(skipBranch); break;

                //2,3 are comparing with constant 0 
                case BranchIf.BLEZ: asm.jg(skipBranch); break;
                case BranchIf.BGTZ: asm.jle(skipBranch); break;

                default: throw new Exception("Invalid Type: " + type);
            }

            //If no jump happens we branch
            EmitBranch(asm, imm);

            //if we jump to this we continue normally
            asm.Label(ref skipBranch);

            //No link for these instructions
        }

        public static void EmitJalr(int rs, int rd, Assembler asm) {
            //Store return address in GRR[rd]
            asm.mov(r15, GetNextPCAddress());
            asm.mov(ecx, __dword_ptr[r15]);
            EmitRegisterWrite(asm, rd, ecx, false);

            //Jump to address in GRR[rs]
            EmitRegisterRead(asm, ecx, rs);
            asm.mov(r15, GetNextPCAddress());
            asm.mov(__dword_ptr[r15], ecx);
            asm.mov(r15, GetBranchFlagAddress());
            asm.mov(__dword_ptr[r15], 1);
        }

        public static void EmitJR(int rs, Assembler asm) {
            //Jump to address in GRR[rs]
            EmitRegisterRead(asm, ecx, rs);
            asm.mov(r15, GetNextPCAddress());
            asm.mov(__dword_ptr[r15], ecx);
            asm.mov(r15, GetBranchFlagAddress());
            asm.mov(__dword_ptr[r15], 1);
        }

        public static void EmitJal(uint targetAddress, Assembler asm) {            
            //Link to reg 31
            asm.mov(r15, GetNextPCAddress());
            asm.mov(ecx, __dword_ptr[r15]);
            EmitRegisterWrite(asm, (int)CPU.Register.ra, ecx, false);

            //Jump to target
            asm.mov(r15, GetNextPCAddress());
            asm.mov(__dword_ptr[r15], targetAddress);
            asm.mov(r15, GetBranchFlagAddress());
            asm.mov(__dword_ptr[r15], 1);      
        }

        public static void EmitJump(uint targetAddress, Assembler asm) {
            //Jump to target
            asm.mov(r15, GetNextPCAddress());
            asm.mov(__dword_ptr[r15], targetAddress);

            asm.mov(r15, GetBranchFlagAddress());
            asm.mov(__dword_ptr[r15], 1);
        }

        public static void EmitBXX(int rs, uint imm, bool link, bool bgez, Assembler asm) {
            Label skipBranch = asm.CreateLabel();

            asm.mov(r15, GetNextPCAddress());
            asm.mov(r8d, __dword_ptr[r15]);        //Save a copy of next pc
            EmitRegisterRead(asm, ecx, rs);                       //Read GPR[rs]
            asm.mov(edx, 0);                                      //Load 0
            asm.cmp(ecx, edx);                                    //Compare

            //Test the inverse, if true then we don't branch
            if (bgez) {                       
                asm.jl(skipBranch);
            } else {                      
                asm.jge(skipBranch);
            }

            EmitBranch(asm, imm);

            asm.Label(ref skipBranch);

            if (link) {
                //link to reg 31
                EmitRegisterWrite(asm, (int)CPU.Register.ra, r8d, false);
            }
        }

        public static void EmitArithmeticU(int rs, int rt, int rd, int type, Assembler asm) {
            //We just emit normal add since we don't check for overflows anyway
            EmitArithmetic(rs, rt, rd, type, asm);
        }

        public static void EmitArithmeticI_U(int rs, int rt, uint imm, int type, Assembler asm) {
            //We just emit normal addi since we don't check for overflows anyway
            EmitArithmetic_i(rs, rt, imm, type, asm);
        }

        public static void EmitArithmetic_i(int rs, int rt, uint imm, int type, Assembler asm) {
            //This should check for signed overflow, but it can be ignored as no games rely on this 
            EmitRegisterRead(asm, eax, rs);

            switch (type) {
                case ArithmeticSignals.ADD: asm.add(eax, imm); break;
                case ArithmeticSignals.SUB: asm.sub(eax, imm); break;
                default: throw new Exception("JIT: Unknown Arithmetic_i : " + type);
            }

            EmitRegisterWrite(asm, rt, eax, false);
        }

        public static void EmitArithmetic(int rs, int rt, int rd, int type, Assembler asm) {
            //This should check for signed overflow, but it can be ignored as no games rely on this 
            EmitRegisterRead(asm, eax, rs);
            EmitRegisterRead(asm, ecx, rt);

            switch (type) {
                case ArithmeticSignals.ADD: asm.add(eax, ecx); break;
                case ArithmeticSignals.SUB: asm.sub(eax, ecx); break;
                default: throw new Exception("JIT: Unknown Arithmetic_i : " + type);
            }

            EmitRegisterWrite(asm, rd, eax, false);
        }

        public static void EmitLogic_i(int rs, int rt, uint imm, int type, Assembler asm) {
            EmitRegisterRead(asm, eax, rs);

            //Emit the required op
            switch (type) {
                case LogicSignals.AND: asm.and(eax, imm); break;
                case LogicSignals.OR: asm.or(eax, imm);   break;
                case LogicSignals.XOR: asm.xor(eax, imm); break;
                //There is no NORI instruction
                default: throw new Exception("JIT: Unknown Logic_i : " + type);
            }

            EmitRegisterWrite(asm, rt, eax, false);
        }

        public static void EmitLogic(int rs, int rt, int rd, int type, Assembler asm) {
            EmitRegisterRead(asm, eax, rs);
            EmitRegisterRead(asm, ecx, rt);

            //Emit the required op
            switch (type) {
                case LogicSignals.AND: asm.and(eax, ecx); break;
                case LogicSignals.OR: asm.or(eax, ecx); break;
                case LogicSignals.XOR: asm.xor(eax, ecx); break;
                case LogicSignals.NOR:
                    asm.or(eax, ecx);
                    asm.not(eax);
                    break;
                default: throw new Exception("JIT: Unknown Logic: " + type);
            }

            EmitRegisterWrite(asm, rd, eax, false);
        }

        public static void EmitShift(int rt, int rd, uint amount, uint direction, Assembler asm) {
            EmitRegisterRead(asm, eax, rt);

            switch (direction) {
                case ShiftSignals.LEFT: asm.shl(eax, (byte)amount); break;
                case ShiftSignals.RIGHT: asm.shr(eax, (byte)amount); break;
                case ShiftSignals.RIGHT_ARITHMETIC: asm.sar(eax, (byte)amount); break;
                default: throw new Exception("Unknown Shift direction");
            }

            EmitRegisterWrite(asm, rd, eax, false);
        }

        public static void EmitShift_v(int rs, int rt, int rd, int direction, Assembler asm) {
            EmitRegisterRead(asm, eax, rt);
            EmitRegisterRead(asm, ecx, rs);
            asm.and(ecx, 0x1F);            //The shift amount (rs value) has to be masked with 0x1F

            //register cl contains low byte of ecx
            switch (direction) {
                case ShiftSignals.LEFT: asm.shl(eax, cl); break;            
                case ShiftSignals.RIGHT: asm.shr(eax, cl); break;
                case ShiftSignals.RIGHT_ARITHMETIC: asm.sar(eax, cl); break;
                default: throw new Exception("Unknown Shift direction");
            }

            EmitRegisterWrite(asm, rd, eax, false);
        }

        public static void EmitDIV(int rs, int rt, bool signed, Assembler asm) {
            Label check2 = asm.CreateLabel();
            Label check3 = asm.CreateLabel();
            Label normalCase = asm.CreateLabel();
            Label end = asm.CreateLabel();

            EmitRegisterRead(asm, eax, rs);     //numerator
            EmitRegisterRead(asm, ecx, rt);     //denominator

            //This could be optimized
            if (signed) {
                //If numerator >= 0 && denominator == 0:
                //Check the inverse
                asm.mov(esi, 0);
                asm.cmp(eax, esi);
                asm.jl(check2);
                asm.cmp(ecx, esi);
                asm.jne(check2);

                //LO = 0xffffffff;
                //HI = (uint)numerator;
                asm.mov(r15, GetLOAddress());
                asm.mov(__dword_ptr[r15], 0xffffffff);
                asm.mov(r15, GetHIAddress());
                asm.mov(__dword_ptr[r15], eax);
                asm.jmp(end);


                asm.Label(ref check2);

                //If numerator < 0 && denominator == 0
                //Check the inverse
                //esi already 0
                asm.cmp(eax, esi);
                asm.jge(check3);
                asm.cmp(ecx, esi);
                asm.jne(check3);

                //LO = 1;
                //HI = (uint)numerator;
                asm.mov(r15, GetLOAddress());
                asm.mov(__dword_ptr[r15], 1);
                asm.mov(r15, GetHIAddress());
                asm.mov(__dword_ptr[r15], eax);
                asm.jmp(end);

                asm.Label(ref check3);

                //If numerator == 0x80000000 && denominator == 0xffffffff
                //Check the inverse
                asm.mov(esi, 0x80000000);
                asm.cmp(eax, esi);
                asm.jne(normalCase);
                asm.mov(esi, 0xffffffff);
                asm.cmp(ecx, esi);
                asm.jne(normalCase);

                //LO = 0x80000000;
                //HI = 0;
                asm.mov(r15, GetLOAddress());
                asm.mov(__dword_ptr[r15], 0x80000000);
                asm.mov(r15, GetHIAddress());
                asm.mov(__dword_ptr[r15], 0);
                asm.jmp(end);

                asm.Label(ref normalCase);
                asm.cdq();
                asm.idiv(ecx);                               //Divide  edx:eax / ecx

            } else {

                //Only one check, if denominator == 0
                //Check the inverse
                asm.mov(esi, 0); 
                asm.cmp(ecx, esi);
                asm.jne(normalCase);

                //LO = 0xffffffff;
                //HI = numerator;
                asm.mov(r15, GetLOAddress());
                asm.mov(__dword_ptr[r15], 0xffffffff);
                asm.mov(r15, GetHIAddress());
                asm.mov(__dword_ptr[r15], eax);
                asm.jmp(end);

                asm.Label(ref normalCase);
                asm.mov(edx, 0);
                asm.div(ecx);                               //Divide  edx:eax / ecx
            }

            //Quotient in eax and remainder in edx
            asm.mov(r15, GetLOAddress());
            asm.mov(__dword_ptr[r15], eax);  //LO = numerator / denominator;
            asm.mov(r15, GetHIAddress());
            asm.mov(__dword_ptr[r15], edx);  //HI = numerator % denominator;

            asm.Label(ref end);
        }

        public static void EmitMULT(int rs, int rt, bool signed, Assembler asm) {
            EmitRegisterRead(asm, eax, rs);     
            EmitRegisterRead(asm, ecx, rt);    

            if (signed) {
                asm.imul(ecx);   // edx:eax = signed eax * signed ecx

            } else {            
                asm.mul(ecx);   //edx:eax = eax * ebx
            }

            asm.mov(r15, GetLOAddress());
            asm.mov(__dword_ptr[r15], eax);
            asm.mov(r15, GetHIAddress());
            asm.mov(__dword_ptr[r15], edx);
        }

        public static void EmitLUI(int rt, uint imm, Assembler asm) {
            asm.mov(r15, GetGPRAddress(rt));
            asm.mov(__dword_ptr[r15], imm << 16);
        }

        public static void EmitMTC0(int rt, int rd, Assembler asm) {
            if (rd == 12) {
                //cpu.Cop0.SR = cpu.GPR[instruction.Get_rt()]; -> That's what we care about for now
                EmitRegisterRead(asm, ecx, rt);
                asm.mov(r15, GetCOP0SRAddress());
                asm.mov(__dword_ptr[r15], ecx);
            }
        }

        public static void EmitMFC0(int rt, int rd, Assembler asm) {
            switch (rd) {
                case 12: asm.mov(r15, GetCOP0SRAddress()); break;
                case 13: asm.mov(r15, GetCOP0CauseAddress()); break;
                case 14: asm.mov(r15, GetCOP0EPCAddress()); break;
                case 15: asm.mov(ecx, 0x00000002); break; //COP0 R15 (PRID)
                default: rt = 0; Console.WriteLine("Unhandled cop0 Register Read: " + rd);  break;
            }

            if (rd != 15) {
                asm.mov(ecx, __dword_ptr[r15]);
            }

            //MFC has load delay!
            EmitRegisterWrite(asm, rt, ecx, true);
        }

        public static void EmitRFE(Assembler asm) {
            /* 
            uint temp = cpu.Cop0.SR;
            cpu.Cop0.SR = (uint)(cpu.Cop0.SR & (~0xF));
            cpu.Cop0.SR |= ((temp >> 2) & 0xF);
            */
            asm.mov(r15, GetCOP0SRAddress());
            asm.mov(ecx, __dword_ptr[r15]);                 //SR
            asm.mov(edx, ecx);                              //Copy of SR (temp)
            asm.and(ecx, ~0xF);                             //SR = SR & (~0xF)
            asm.shr(edx, 2);                                //temp = temp >> 2
            asm.and(edx, 0xF);                              //temp = temp & 0xF
            asm.or(ecx, edx);                               //SR = SR | temp
            asm.mov(__dword_ptr[r15], ecx);                 //Write back
        }


        public static void EmitMF(int rd, bool isHI, Assembler asm) {      
            ulong address = isHI ? GetHIAddress() : GetLOAddress();
            asm.mov(r15, address);
            asm.mov(ecx, __dword_ptr[r15]);
            EmitRegisterWrite(asm, rd, ecx, false);
        }

        public static void EmitMT(int rs, bool isHI, Assembler asm) {
            ulong address = isHI ? GetHIAddress() : GetLOAddress();
            EmitRegisterRead(asm, ecx, rs);
            asm.mov(r15, address);
            asm.mov(__dword_ptr[r15], ecx);
        }

        public static void EmitCOP2Command(uint instruction, Assembler asm) {
            //Call GTE.execute(instruction);
            asm.sub(rsp, 40);                       //Shadow space
            asm.mov(r15, GetGTExecuteAddress());    //Load function pointer
            asm.mov(ecx, instruction);              //Parameter in ecx
            asm.call(r15);                          //Call GTE Execute
            asm.add(rsp, 40);                       //Undo Shadow space 
        }

        public static void EmitMFC2_CFC2(int rt, int rd, Assembler asm) {
            //Call GTE.read(rd);
            asm.sub(rsp, 40);                       //Shadow space
            asm.mov(r15, GetGTEReadAddress());      //Load function pointer
            asm.mov(ecx, rd);                       //Parameter in rcx
            asm.call(r15);                          //Call GTE Read, result is written to eax
            asm.add(rsp, 40);                       //Undo Shadow space 

            //There is a delay slot
            EmitRegisterWrite(asm, rt, eax, true);
        }

        public static void EmitMTC2_CTC2(int rt, int rd, Assembler asm) {
            //Call GTE.write(rd, value);
            asm.sub(rsp, 40);                       //Shadow space
            asm.mov(ecx, rd);                       //Parameter in ecx
            EmitRegisterRead(asm, edx, rt);         //Parameter in edx
            asm.mov(r15, GetGTEWriteAddress());     //Load function pointer
            asm.call(r15);                          //Call GTE Write
            asm.add(rsp, 40);                       //Undo Shadow space 
        }

        public static void EmitLWC2(int rs, int rt, uint imm, Assembler asm) {
            //Call bus.loadword(address);
            asm.sub(rsp, 40);                           //Shadow space
            EmitCalculateAddress(asm, ecx, rs, imm);    //Address -> ecx
            asm.mov(r15, GetBUSReadWordAddress());     //Load function pointer
            asm.call(r15);                             //Call BUS ReadWord, result is written to eax

            //Call cpu.GTE.write(rd, value);
            //Shadow space is already set
            asm.mov(ecx, rt);                           //Move rt to ecx (parameter 1)
            asm.mov(edx, eax);                          //Move loaded value to edx (parameter 2)
            asm.mov(r15, GetGTEWriteAddress());         //Load function pointer
            asm.call(r15);                              //Call GTE write
            asm.add(rsp, 40);                           //Undo Shadow space 
        }

        public static void EmitSWC2(int rs, int rt, uint imm, Assembler asm) {
            //Call cpu.GTE.read(rt);
            asm.sub(rsp, 40);                           //Shadow space
            asm.mov(ecx, rt);                           //Parameter in rcx
            asm.mov(r15, GetGTEReadAddress());          //Load function pointer
            asm.call(r15);                              //Call GTE Read, result is written to eax

            //Write the value to the memory
            EmitCalculateAddress(asm, ecx, rs, imm);    //Address -> ecx
            asm.mov(edx, eax);                          //Move eax to edx (parameter 2)
            asm.mov(r15, GetBUSWriteWordAddress());     //Load function pointer
            asm.call(r15);                              //Call bus writeword
            asm.add(rsp, 40);                           //Undo Shadow space 
        }

        public static void EmitMemoryLoad(int rs, int rt, uint imm, int size, bool signed, Assembler asm) {
            Label end = asm.CreateLabel();

            //Check cache isolation!
            EmitCheckCacheIsolation(asm);   //result in eax
            asm.jnz(end);                   //if not zero we exit

            EmitCalculateAddress(asm, ecx, rs, imm);    //Address -> ecx

            asm.sub(rsp, 40);                           //Shadow space

            switch (size) {
                case MemoryReadWriteSize.BYTE:
                    asm.mov(r15, GetBUSReadByteAddress());     //Load function pointer
                    asm.call(r15);                             //Result in eax
                    if (signed) {                       
                        asm.movsx(eax, al);             //Sign-extend 8-bit al to 32-bit eax
                    }
                    break;

                case MemoryReadWriteSize.HALF:
                    asm.mov(r15, GetBUSReadHalfAddress());     //Load function pointer
                    asm.call(r15);                             //Result in eax
                    if (signed) {                      
                        asm.movsx(eax, ax);             //Sign-extend 16-bit ax to 32-bit eax
                    }
                    break;

                case MemoryReadWriteSize.WORD:
                    asm.mov(r15, GetBUSReadWordAddress());     //Load function pointer
                    asm.call(r15);                             //Result in eax, there is not signed 32-bits version
                    break;
            }

            asm.add(rsp, 40);                           //Undo Shadow space 

            //There is a delay slot
            EmitRegisterWrite(asm, rt, eax, true);
            asm.Label(ref end);
        }

        public static void EmitMemoryStore(int rs, int rt, uint imm, int size, Assembler asm) {
            Label end = asm.CreateLabel();

            //Check cache isolation!
            EmitCheckCacheIsolation(asm);   //result in eax
            asm.jnz(end);                   //if not zero we exit

            EmitCalculateAddress(asm, ecx, rs, imm);    //Address -> ecx 

            asm.sub(rsp, 40);                           //Shadow space


            //Load GPR[rt]
            EmitRegisterRead(asm, edx, rt);            //Value -> edx


            switch (size) {
                case MemoryReadWriteSize.BYTE:
                    asm.and(edx, 0xFF);                         //Mask to one byte
                    asm.mov(r15, GetBUSWriteByteAddress());     //Load function pointer
                    asm.call(r15);
                    break;

                case MemoryReadWriteSize.HALF:
                    asm.and(edx, 0xFFFF);                       //Mask to 2 bytes
                    asm.mov(r15, GetBUSWriteHalfAddress());     //Load function pointer
                    asm.call(r15);
                    break;

                case MemoryReadWriteSize.WORD:
                    asm.mov(r15, GetBUSWriteWordAddress());     //Load function pointer
                    asm.call(r15);
                    break;
            }

            asm.add(rsp, 40);                           //Undo Shadow space 

            asm.Label(ref end);
        }

        public static void EmitLWL(int rs, int rt, uint imm, Assembler asm) {
            Label loadPos = asm.CreateLabel();
            Label finalStep = asm.CreateLabel();
            Label end = asm.CreateLabel();

            Label case1 = asm.CreateLabel();
            Label case2 = asm.CreateLabel();
            Label case3 = asm.CreateLabel();

            //Check cache isolation!
            EmitCheckCacheIsolation(asm);   //result in eax
            asm.jnz(end);                   //if not zero we exit

            EmitCalculateAddress(asm, ecx, rs, imm);    //Address -> ecx
            asm.mov(r8d, ecx);                          //Copy of address -> r8d

            EmitRegisterRead(asm, edx, rt);            //current_value -> edx

            //Bypass load delay if rt == ReadyRegisterLoad.RegisterNumber
            asm.mov(esi, rt);
            asm.mov(r15, GetReadyRegisterLoadNumberAddress());
            asm.mov(edi, __dword_ptr[r15]);
            asm.cmp(esi, edi);
            asm.jne(loadPos);                               //Skip if they are not equal
            asm.mov(r15, GetReadyRegisterLoadValueAddress());
            asm.mov(edx, __dword_ptr[r15]);                 //Overwrite current_value (edx)


            asm.Label(ref loadPos);
            asm.and(ecx, ~3);                       //ecx &= ~3

            asm.sub(rsp, 40);                       //Shadow space

            //Copy edx and r8d to callee-saved registers
            asm.mov(r13d, edx);
            asm.mov(r14d, r8d);

            asm.mov(r15, GetBUSReadWordAddress());  //Load function pointer
            asm.call(r15);                          //Load word from Address & ~3, result is in eax

            asm.mov(edx, r13d);
            asm.mov(r8d, r14d);

            asm.add(rsp, 40);                       //Undo shadow space
       
            asm.and(r8d, 3);                        //pos in r8d

           
            //edx -> current_value
            //eax -> word

            //Switch:
            //Case 0: finalValue = current_value & 0x00ffffff | word << 24; break;
            asm.cmp(r8d, 0);
            asm.jne(case1);

            asm.and(edx, 0x00ffffff);
            asm.shl(eax, 24);
            asm.or(edx, eax);

            asm.jmp(finalStep);

            //case 1: finalValue = current_value & 0x0000ffff | word << 16; break;
            asm.Label(ref case1);
            asm.cmp(r8d, 1);
            asm.jne(case2);

            asm.and(edx, 0x0000ffff);
            asm.shl(eax, 16);
            asm.or(edx, eax);

            asm.jmp(finalStep);

            //case 2: finalValue = current_value & 0x000000ff | word << 8; break;
            asm.Label(ref case2);
            asm.cmp(r8d, 2);
            asm.jne(case3);

            asm.and(edx, 0x000000ff);
            asm.shl(eax, 8);
            asm.or(edx, eax);

            asm.jmp(finalStep);

            //case 3:  finalValue = current_value & 0x00000000 | word << 0; break;
            asm.Label(ref case3);

            asm.and(edx, 0x00000000);
            asm.shl(eax, 0);
            asm.or(edx, eax);



            asm.Label(ref finalStep);

            //Write to finalValue (edx) to GPR[rt]
            EmitRegisterWrite(asm, rt, edx, true);  //There is a load delay

            asm.Label(ref end);
        }

        public static void EmitLWR(int rs, int rt, uint imm, Assembler asm) {
            Label loadPos = asm.CreateLabel();
            Label finalStep = asm.CreateLabel();
            Label end = asm.CreateLabel();

            Label case1 = asm.CreateLabel();
            Label case2 = asm.CreateLabel();
            Label case3 = asm.CreateLabel();

            //Check cache isolation!
            EmitCheckCacheIsolation(asm);   //result in eax
            asm.jnz(end);                   //if not zero we exit

            EmitCalculateAddress(asm, ecx, rs, imm);    //Address -> ecx

            asm.mov(r8d, ecx);                          //Copy of address -> r8d

            EmitRegisterRead(asm, edx, rt);            //current_value -> edx

            //Bypass load delay if rt == ReadyRegisterLoad.RegisterNumber
            asm.mov(esi, rt);
            asm.mov(r15, GetReadyRegisterLoadNumberAddress());
            asm.mov(edi, __dword_ptr[r15]);
            asm.cmp(esi, edi);
            asm.jne(loadPos);                               //Skip if they are not equal
            asm.mov(r15, GetReadyRegisterLoadValueAddress());
            asm.mov(edx, __dword_ptr[r15]);                 //Overwrite current_value (edx)


            asm.Label(ref loadPos);
            asm.and(ecx, ~3);                       //ecx &= ~3

            asm.sub(rsp, 40);                       //Shadow space
            
            asm.mov(r13d, edx);
            asm.mov(r14d, r8d);

            asm.mov(r15, GetBUSReadWordAddress());  //Load function pointer
            asm.call(r15);                          //Load word from Address & ~3, result is in eax

            asm.mov(edx, r13d);
            asm.mov(r8d, r14d);

            asm.add(rsp, 40);                       //Undo Shadow space

            asm.and(r8d, 3);                        //pos in r8d

            //edx -> current_value
            //eax -> word

            //Switch:
            //Case 0: finalValue = current_value & 0x00000000 | word >> 0;
            asm.cmp(r8d, 0);
            asm.jne(case1);

            asm.and(edx, 0x00000000);
            asm.shr(eax, 0);
            asm.or(edx, eax);

            asm.jmp(finalStep);

            //case 1: finalValue = current_value & 0xff000000 | word >> 8;
            asm.Label(ref case1);
            asm.cmp(r8d, 1);
            asm.jne(case2);

            asm.and(edx, 0xff000000);
            asm.shr(eax, 8);
            asm.or(edx, eax);

            asm.jmp(finalStep);

            //case 2: finalValue = current_value & 0xffff0000 | word >> 16;
            asm.Label(ref case2);
            asm.cmp(r8d, 2);
            asm.jne(case3);

            asm.and(edx, 0xffff0000);
            asm.shr(eax, 16);
            asm.or(edx, eax);

            asm.jmp(finalStep);

            //case 3:  finalValue = current_value & 0xffffff00 | word >> 24; 
            asm.Label(ref case3);

            asm.and(edx, 0xffffff00);
            asm.shr(eax, 24);
            asm.or(edx, eax);


            asm.Label(ref finalStep);

            //Write to finalValue (edx) to GPR[rt]
            EmitRegisterWrite(asm, rt, edx, true);  //There is a load delay

            asm.Label(ref end);
        }

        public static void EmitSWL(int rs, int rt, uint imm, Assembler asm) {
            Label finalStep = asm.CreateLabel();
            Label end = asm.CreateLabel();

            Label case1 = asm.CreateLabel();
            Label case2 = asm.CreateLabel();
            Label case3 = asm.CreateLabel();

            //Check cache isolation!
            EmitCheckCacheIsolation(asm);   //result in eax
            asm.jnz(end);                   //if not zero we exit

            EmitCalculateAddress(asm, ecx, rs, imm);    //final_address -> ecx
            asm.mov(r8d, ecx);                          //Copy of final_address -> r8d
            asm.mov(esi, ecx);                          //Another copy of final_address -> esi

            EmitRegisterRead(asm, edx, rt);             //value -> edx

            asm.and(ecx, ~3);                           //final_address &= ~3

            asm.sub(rsp, 40);                           //Shadow space

            //Copy esi and edx and r8d to callee-saved registers
            asm.mov(r12d, esi);
            asm.mov(r13d, edx);
            asm.mov(r14d, r8d);

            asm.mov(r15, GetBUSReadWordAddress());  //Load function pointer
            asm.call(r15);                          //current_value -> eax

            asm.mov(esi, r12d);
            asm.mov(edx, r13d);
            asm.mov(r8d, r14d);

            asm.and(r8d, 3);                            //pos -> r8d


            //edx -> value
            //eax -> current_value

            //Switch:
            //case 0: finalValue = current_value & 0xffffff00 | value >> 24;
            asm.cmp(r8d, 0);
            asm.jne(case1);

            asm.and(eax, 0xffffff00);
            asm.shr(edx, 24);
            asm.or(edx, eax);
            
            asm.jmp(finalStep);

            //case 1: finalValue = current_value & 0xffff0000 | value >> 16;
            asm.Label(ref case1);
            asm.cmp(r8d, 1);
            asm.jne(case2);

            asm.and(eax, 0xffff0000);
            asm.shr(edx, 16);
            asm.or(edx, eax);

            asm.jmp(finalStep);

            //case 2: finalValue = current_value & 0xff000000 | value >> 8;
            asm.Label(ref case2);
            asm.cmp(r8d, 2);
            asm.jne(case3);

            asm.and(eax, 0xff000000);
            asm.shr(edx, 8);
            asm.or(edx, eax);

            asm.jmp(finalStep);

            //case 3: finalValue = current_value & 0x00000000 | value >> 0;
            asm.Label(ref case3);

            asm.and(eax, 0x00000000);
            asm.shr(edx, 0);
            asm.or(edx, eax);

            
            asm.Label(ref finalStep);

            //final_address & ~3 -> ecx, and final value is already in edx, shadow space is already added
            asm.mov(ecx, esi);
            asm.and(ecx, ~3);
            asm.mov(r15, GetBUSWriteWordAddress());     //Load function pointer
            asm.call(r15);
            asm.add(rsp, 40);                           //Undo shadow space

            asm.Label(ref end);
        }

        public static void EmitSWR(int rs, int rt, uint imm, Assembler asm) {
            Label finalStep = asm.CreateLabel();
            Label end = asm.CreateLabel();

            Label case1 = asm.CreateLabel();
            Label case2 = asm.CreateLabel();
            Label case3 = asm.CreateLabel();

            //Check cache isolation!
            EmitCheckCacheIsolation(asm);   //result in eax
            asm.jnz(end);                   //if not zero we exit

            EmitCalculateAddress(asm, ecx, rs, imm);    //final_address -> ecx
            asm.mov(r8d, ecx);                          //Copy of final_address -> r8d
            asm.mov(esi, ecx);                          //Another copy of final_address -> esi

            EmitRegisterRead(asm, edx, rt);             //value -> edx

            asm.and(ecx, ~3);                           //final_address &= ~3

            asm.sub(rsp, 40);                           //Shadow space

            //Copy esi and edx and r8d to callee-saved registers
            asm.mov(r12d, esi);
            asm.mov(r13d, edx);
            asm.mov(r14d, r8d);

            asm.mov(r15, GetBUSReadWordAddress());  //Load function pointer
            asm.call(r15);                          //current_value -> eax

            asm.mov(esi, r12d);
            asm.mov(edx, r13d);
            asm.mov(r8d, r14d);


            asm.and(r8d, 3);                            //pos -> r8d


            //edx -> value
            //eax -> current_value

            //Switch:
            //case 0: finalValue = current_value & 0x00000000 | value << 0;
            asm.cmp(r8d, 0);
            asm.jne(case1);

            asm.and(eax, 0x00000000);
            asm.shl(edx, 0);
            asm.or(edx, eax);

            asm.jmp(finalStep);

            //case 1: finalValue = current_value & 0x000000ff | value << 8;
            asm.Label(ref case1);
            asm.cmp(r8d, 1);
            asm.jne(case2);

            asm.and(eax, 0x000000ff);
            asm.shl(edx, 8);
            asm.or(edx, eax);

            asm.jmp(finalStep);

            //case 2: finalValue = current_value & 0x0000ffff | value << 16;
            asm.Label(ref case2);
            asm.cmp(r8d, 2);
            asm.jne(case3);

            asm.and(eax, 0x0000ffff);
            asm.shl(edx, 16);
            asm.or(edx, eax);

            asm.jmp(finalStep);

            //case 3: finalValue = current_value & 0x00ffffff | value << 24;
            asm.Label(ref case3);

            asm.and(eax, 0x00ffffff);
            asm.shl(edx, 24);
            asm.or(edx, eax);


            asm.Label(ref finalStep);

            //final_address & ~3 -> ecx, and final value is already in edx, shadow space is already added
            asm.mov(ecx, esi);
            asm.and(ecx, ~3);
            asm.mov(r15, GetBUSWriteWordAddress());     //Load function pointer
            asm.call(r15);
            asm.add(rsp, 40);                           //Undo shadow space

            asm.Label(ref end);
        }


        private static void EmitCalculateAddress(Assembler asm, AssemblerRegister32 dst, int sourceReg, uint imm) {
            EmitRegisterRead(asm, dst, sourceReg);  //Read GPR
            asm.add(dst, imm);                      //Add to it imm
        }

        public static void EmitCallRegisterTransfare(Assembler asm) {
            asm.sub(rsp, 40);                           //shadow space
            asm.mov(r15, GetRegisterTransfareAddress());
            asm.call(r15);
            asm.add(rsp, 40);                           //Undo shadow space
            return;
        }

        public static void EmitRegisterTransfare(Assembler asm) {      
            /*
             if (cpu.ReadyRegisterLoad.RegisterNumber != cpu.DelayedRegisterLoad.RegisterNumber) {
                cpu.GPR[cpu.ReadyRegisterLoad.RegisterNumber] = cpu.ReadyRegisterLoad.Value;
            }
            */

            Label skip = asm.CreateLabel();

            asm.mov(r15, GetReadyRegisterLoadNumberAddress());
            asm.mov(ecx, __dword_ptr[r15]);
            asm.mov(r15, GetDelayedRegisterLoadNumberAddress());
            asm.mov(edx, __dword_ptr[r15]);
            asm.cmp(ecx, edx);
            asm.je(skip);

            asm.mov(r15, GetReadyRegisterLoadValueAddress());
            asm.mov(edx, __dword_ptr[r15]);                                     //edx = ReadyRegisterLoad.Value
            asm.shl(ecx, 2);                                                    //ecx = ecx * 4 (offset)
            //Note: we need 64-bit regs for the pointer + offset calculation
            asm.mov(r8, GetGPRAddress(0));                                      //Load r8 with base address of GPR
            asm.add(rcx, r8);                                                   //Add base address to offset, now rcx contains the register location                          
            asm.mov(__dword_ptr[rcx], edx);                                     //Write ReadyRegisterLoad.Value to the register


            asm.Label(ref skip);

            /*
             cpu.ReadyRegisterLoad.Value = cpu.DelayedRegisterLoad.Value;
             cpu.ReadyRegisterLoad.RegisterNumber = cpu.DelayedRegisterLoad.RegisterNumber;
            */

            asm.mov(r15, GetDelayedRegisterLoadValueAddress());
            asm.mov(ecx, __dword_ptr[r15]);

            asm.mov(r15, GetReadyRegisterLoadValueAddress());
            asm.mov(__dword_ptr[r15], ecx);

            asm.mov(r15, GetDelayedRegisterLoadNumberAddress());
            asm.mov(ecx, __dword_ptr[r15]);

            asm.mov(r15, GetReadyRegisterLoadNumberAddress());
            asm.mov(__dword_ptr[r15], ecx);

            /*
             cpu.DelayedRegisterLoad.Value = 0;
             cpu.DelayedRegisterLoad.RegisterNumber = 0;
            */

            asm.mov(r15, GetDelayedRegisterLoadValueAddress());
            asm.mov(__dword_ptr[r15], 0);
            asm.mov(r15, GetDelayedRegisterLoadNumberAddress());
            asm.mov(__dword_ptr[r15], 0);

            /*
             //Last step is direct register write, so it can overwrite any memory load on the same register
             cpu.GPR[cpu.DirectWrite.RegisterNumber] = cpu.DirectWrite.Value;
            */

            asm.mov(r15, GetDirectWriteValueAddress());
            asm.mov(edx, __dword_ptr[r15]);                                     //edx = DirectWrite.Value
            asm.mov(r15, GetDirectWriteNumberAddress());
            asm.mov(ecx, __dword_ptr[r15]);                                     //ecx = DirectWrite.RegisterNumber
            asm.shl(ecx, 2);                                                    //ecx = ecx * 4 (offset)

            //Note: we need 64-bit regs for the pointer + offset calculation
            asm.mov(r8, GetGPRAddress(0));                                      //Load r8 with base address of GPR
            asm.add(rcx, r8);                                                   //Add base address to offset, now rcx contains the register location                          
            asm.mov(__dword_ptr[rcx], edx);                                     //Write ReadyRegisterLoad.Value to the register

            /*
            cpu.DirectWrite.RegisterNumber = 0;
            cpu.DirectWrite.Value = 0;
            cpu.GPR[0] = 0;
            */

            asm.mov(r15, GetDirectWriteValueAddress());
            asm.mov(__dword_ptr[r15], 0);
            asm.mov(r15, GetDirectWriteNumberAddress());
            asm.mov(__dword_ptr[r15], 0);
            asm.mov(__dword_ptr[r8], 0);
        }

        public static void EmitBranchDelayHandler(Assembler asm) {
            /*
            cpu.DelaySlot = cpu.Branch;   //Branch delay 
            cpu.Branch = false;
            cpu.PC = cpu.Next_PC;
            cpu.Next_PC = cpu.Next_PC + 4;
            */

            asm.mov(r15, GetBranchFlagAddress());
            asm.mov(ecx, __dword_ptr[r15]);
            asm.mov(r15, GetDelaySlotAddress());
            asm.mov(__dword_ptr[r15], ecx);

            asm.mov(r15, GetBranchFlagAddress());
            asm.mov(__dword_ptr[r15], 0);

            asm.mov(r15, GetNextPCAddress());
            asm.mov(ecx, __dword_ptr[r15]);

            asm.mov(r15, GetPCAddress());
            asm.mov(__dword_ptr[r15], ecx);

            asm.add(ecx, 4);
            asm.mov(r15, GetNextPCAddress());
            asm.mov(__dword_ptr[r15], ecx);
        }

        public static void EmitSavePC(Assembler asm) {
            //Current_PC = PC;
            asm.mov(r15, GetPCAddress());
            asm.mov(eax, __dword_ptr[r15]);
            asm.mov(r15, GetCurrentPCAddress());
            asm.mov(__dword_ptr[r15], eax);
        }

        public static void TerminateBlock(Assembler asm, ref Label endOfBlock) {
            asm.ret();
            asm.Label(ref endOfBlock);
            asm.nop();                      //This nop will not be included, but we need an instruction to define a label
        }

        public static void EmitBranch(Assembler asm, uint offset) {
            asm.mov(r15, GetNextPCAddress());
            asm.add(__dword_ptr[r15], (offset << 2) - 4);
            asm.mov(r15, GetBranchFlagAddress());
            asm.mov(__dword_ptr[r15], 1);
        }

        public static void EmitSaveNonVolatileRegisters(Assembler asm) {
            asm.push(rbx);
            asm.push(rdi);
            asm.push(rsi);
            asm.push(rbp);
            asm.push(r12);
            asm.push(r13);
            asm.push(r14);
            asm.push(r15);
        }

        public static void EmitRestoreNonVolatileRegisters(Assembler asm) {
            asm.pop(r15);
            asm.pop(r14);
            asm.pop(r13);
            asm.pop(r12);
            asm.pop(rbp);
            asm.pop(rsi);
            asm.pop(rdi);
            asm.pop(rbx);
        }

        public static Span<byte> EmitDispatcher() {
            Assembler asm = new Assembler(64);
            Label dispatcherBody = asm.CreateLabel();
            Label executeBlock = asm.CreateLabel();

            Label isBIOSBlock = asm.CreateLabel();
            Label validate = asm.CreateLabel();
            Label recompile = asm.CreateLabel();

            Label endOfFunction = asm.CreateLabel();

            const int CYCLES_PER_FRAME = 33868800 / 60;
            const int BIOS_START = 0x1FC00000;
            int sizeOfBlock = sizeof(x64CacheBlockInternalStruct);

            EmitSaveNonVolatileRegisters(asm);
            asm.mov(rbp, rsp);                          //Copy stack pointer

            asm.mov(r12d, 0);                           //Counter

            asm.sub(rsp, 40);                           //Prepare shadow space

            asm.Label(ref dispatcherBody);
            asm.mov(__qword_ptr[rbp - 8], r12d);        //Save r12

            //Two Big branches: BIOS or RAM
            asm.mov(rax, __dword_ptr[GetPCAddress()]);
            asm.and(rax, 0x1FFFFFFF);                   //Mask PC
            asm.cmp(rax, BIOS_START);
            asm.jge(isBIOSBlock);                       //if >= go to BIOS 

            //Else is ram block
            asm.and(rax, ((1 << 21) - 1));  //%2 MB
            asm.shr(rax, 2);                //>> 2

            if (Utility.IsPowerOf2(sizeOfBlock)) {
                byte power = (byte)Math.Log2(sizeOfBlock);
                asm.shl(rax, power);
            } else {
                asm.mov(rcx, sizeOfBlock);
                asm.mul(rcx);                  //rdx:rax = rax*rcx
            }

            asm.mov(r15, GetRAMCacheBlockAddress());    //Get the base address for ram blocks
            asm.add(rax, r15);
            asm.mov(rbx, rax);                          //Save block address in callee saved-reg rbx

            //Check if this RAM block needs recompilation
            asm.mov(rax, __dword_ptr[rax + 4]);         //Read "IsCompiled"
            asm.cmp(eax, 1);
            asm.je(validate);

            //Call recompile function
            asm.Label(ref recompile);
            asm.mov(rcx, rbx);                          //Parameter 1 is block pointer
            asm.mov(rdx, GetPCAddress());               //Parameter 2 is PC pointer
            asm.mov(r15, GetRecompileBlockAddress());
            asm.call(r15);
            asm.jmp(executeBlock);                     

            asm.Label(ref validate);
            //Call invalidate function  --> This happens on every compiled RAM block and is slowing down the dispatcher
            //TODO: Inline this
            asm.mov(rcx, rbx);
            asm.mov(r15, GetInvalidateBlockAddress());
            asm.call(r15);
            asm.cmp(rax, 0);
            asm.je(recompile);
            asm.jmp(executeBlock);

            /////////////////////////////////////////////////////////////////////////
            asm.Label(ref isBIOSBlock);
            asm.sub(rax, BIOS_START);       //-= BIOS Start
            asm.shr(rax, 2);                //>> 2

            if (Utility.IsPowerOf2(sizeOfBlock)) {
                byte power = (byte)Math.Log2(sizeOfBlock);
                asm.shl(rax, power);
            } else {
                asm.mov(rcx, sizeOfBlock);
                asm.mul(rcx);                  //rdx:rax = rax*rcx
            }

            asm.mov(r15, GetBIOSCacheBlockAddress());    //Get the base address for BIOS blocks
            asm.add(rax, r15);
            asm.mov(rbx, rax);                           //Save block address in callee saved-reg rbx

            //Check if this BIOS block is compiled
            asm.mov(rax, __dword_ptr[rax + 4]);
            asm.cmp(eax, 1);
            asm.je(executeBlock);

            //else call recompile function
            asm.mov(rcx, rbx);                          //Parameter 1 is block pointer
            asm.mov(rdx, GetPCAddress());               //Parameter 2 is PC pointer
            asm.mov(r15, GetRecompileBlockAddress());
            asm.call(r15);
            asm.jmp(executeBlock);

            //////////////////////////////////////////////////////////////////////////
            asm.Label(ref executeBlock);

            //Block pointer in rbx
            //Now the block is ready for execution
            asm.mov(r15, __qword_ptr[rbx + 24]);        //Get function pointer in r15

            asm.mov(__qword_ptr[rbp - 16], rbx);        //Save rbx

            asm.call(r15);                              //Execute block -- Assume all registers are destroyed except rbp and rsp

            asm.mov(rbx, __qword_ptr[rbp - 16]);        //Restore rbx

            //Call BUS.Tick
            asm.mov(rcx, __dword_ptr[rbx + 12]);        //Get total cycles in rcx
            asm.mov(r15, GetBUSTickddress());
            asm.call(r15);

            //Call IRQ Check
            asm.mov(r15, GetIRQCheckAddress());
            asm.call(r15);

            asm.mov(r12d, __qword_ptr[rbp - 8]);        //Restore r12
            asm.add(r12d, __dword_ptr[rbx + 12]);       //Add counter by total cycles
        
            asm.cmp(r12d, CYCLES_PER_FRAME);
            
            asm.jb(dispatcherBody);

            asm.add(rsp, 40);                           //undo shadow space
            asm.mov(eax, r12d);                         //Return cycles done (32-bit int)

            EmitRestoreNonVolatileRegisters(asm);
            asm.ret();

            asm.Label(ref endOfFunction);
            asm.nop();


            MemoryStream stream = new MemoryStream();
            AssemblerResult result = asm.Assemble(new StreamCodeWriter(stream), 0, BlockEncoderOptions.ReturnNewInstructionOffsets);
            int endOfBlock = (int)result.GetLabelRIP(endOfFunction);
            Span<byte> emittedCode = new Span<byte>(stream.GetBuffer()).Slice(0, endOfBlock);
            return emittedCode;

            //Debug
            /*asm.mov(rcx, r12);
            asm.mov(r15, GetPrintAddress());
            asm.call(r15);*/
        }

        public static Span<byte> PrecompileRegisterTransfare() {
            Assembler asm = new Assembler(64);

            /*
             if (cpu.ReadyRegisterLoad.RegisterNumber != cpu.DelayedRegisterLoad.RegisterNumber) {
                cpu.GPR[cpu.ReadyRegisterLoad.RegisterNumber] = cpu.ReadyRegisterLoad.Value;
            }
            */

            Label skip = asm.CreateLabel();

            asm.mov(r15, GetReadyRegisterLoadNumberAddress());
            asm.mov(ecx, __dword_ptr[r15]);
            asm.mov(r15, GetDelayedRegisterLoadNumberAddress());
            asm.mov(edx, __dword_ptr[r15]);
            asm.cmp(ecx, edx);
            asm.je(skip);

            asm.mov(r15, GetReadyRegisterLoadValueAddress());
            asm.mov(edx, __dword_ptr[r15]);                                     //edx = ReadyRegisterLoad.Value
            asm.shl(ecx, 2);                                                    //ecx = ecx * 4 (offset)
            //Note: we need 64-bit regs for the pointer + offset calculation
            asm.mov(r8, GetGPRAddress(0));                                      //Load r8 with base address of GPR
            asm.add(rcx, r8);                                                   //Add base address to offset, now rcx contains the register location                          
            asm.mov(__dword_ptr[rcx], edx);                                     //Write ReadyRegisterLoad.Value to the register


            asm.Label(ref skip);

            /*
             cpu.ReadyRegisterLoad.Value = cpu.DelayedRegisterLoad.Value;
             cpu.ReadyRegisterLoad.RegisterNumber = cpu.DelayedRegisterLoad.RegisterNumber;
            */

            asm.mov(r15, GetDelayedRegisterLoadValueAddress());
            asm.mov(ecx, __dword_ptr[r15]);

            asm.mov(r15, GetReadyRegisterLoadValueAddress());
            asm.mov(__dword_ptr[r15], ecx);

            asm.mov(r15, GetDelayedRegisterLoadNumberAddress());
            asm.mov(ecx, __dword_ptr[r15]);

            asm.mov(r15, GetReadyRegisterLoadNumberAddress());
            asm.mov(__dword_ptr[r15], ecx);

            /*
             cpu.DelayedRegisterLoad.Value = 0;
             cpu.DelayedRegisterLoad.RegisterNumber = 0;
            */

            asm.mov(r15, GetDelayedRegisterLoadValueAddress());
            asm.mov(__dword_ptr[r15], 0);
            asm.mov(r15, GetDelayedRegisterLoadNumberAddress());
            asm.mov(__dword_ptr[r15], 0);

            /*
             //Last step is direct register write, so it can overwrite any memory load on the same register
             cpu.GPR[cpu.DirectWrite.RegisterNumber] = cpu.DirectWrite.Value;
            */

            asm.mov(r15, GetDirectWriteValueAddress());
            asm.mov(edx, __dword_ptr[r15]);                                     //edx = DirectWrite.Value
            asm.mov(r15, GetDirectWriteNumberAddress());
            asm.mov(ecx, __dword_ptr[r15]);                                     //ecx = DirectWrite.RegisterNumber
            asm.shl(ecx, 2);                                                    //ecx = ecx * 4 (offset)

            //Note: we need 64-bit regs for the pointer + offset calculation
            asm.mov(r8, GetGPRAddress(0));                                      //Load r8 with base address of GPR
            asm.add(rcx, r8);                                                   //Add base address to offset, now rcx contains the register location                          
            asm.mov(__dword_ptr[rcx], edx);                                     //Write ReadyRegisterLoad.Value to the register

            /*
            cpu.DirectWrite.RegisterNumber = 0;
            cpu.DirectWrite.Value = 0;
            cpu.GPR[0] = 0;
            */

            asm.mov(r15, GetDirectWriteValueAddress());
            asm.mov(__dword_ptr[r15], 0);
            asm.mov(r15, GetDirectWriteNumberAddress());
            asm.mov(__dword_ptr[r15], 0);
            asm.mov(__dword_ptr[r8], 0);

            Label endOfFunction = asm.CreateLabel();
            asm.ret();
            asm.Label(ref endOfFunction);
            asm.nop();

            MemoryStream stream = new MemoryStream();
            AssemblerResult result = asm.Assemble(new StreamCodeWriter(stream), 0, BlockEncoderOptions.ReturnNewInstructionOffsets);
            int endOfBlock = (int)result.GetLabelRIP(endOfFunction);
            Span<byte> emittedCode = new Span<byte>(stream.GetBuffer()).Slice(0, endOfBlock);
            return emittedCode;
        }
    }

    public unsafe class x64CacheBlock {     
        public uint Address;
        public bool IsCompiled;
        public uint TotalMIPS_Instructions;
        public uint TotalCycles;
        public uint MIPS_Checksum;
        public int SizeOfAllocatedBytes;
        public delegate* unmanaged[Stdcall]<void> FunctionPointer;

        /*public void Init(uint address) {
            //FunctionPointer = null;
            //SizeOfAllocatedBytes = 0;

            Address = address;        
            IsCompiled = false;
            TotalMIPS_Instructions = 0;
            MIPS_Checksum = 0;
        }*/

        /*public void Compile(ref NativeMemoryManager nativeMemoryManager) {
            MemoryStream stream = new MemoryStream();
            AssemblerResult result = Emitter.Assemble(new StreamCodeWriter(stream), 0, BlockEncoderOptions.ReturnNewInstructionOffsets);

            //Trim the extra zeroes and the padding in the block by including only up to the ret instruction
            //This works as long as there is no call instruction with the address being passed as 64 bit immediate
            //Otherwise, the address will be inserted at the end of the block and we need to include it in the span
            int endOfBlock = (int)result.GetLabelRIP(EndOfBlock);
            Span<byte> emittedCode = new Span<byte>(stream.GetBuffer()).Slice(0, endOfBlock);

            //Pass the old pointer and size. if the changes are small the block could be overwritten in place
            FunctionPointer = nativeMemoryManager.WriteExecutableBlock(ref emittedCode, (byte*)FunctionPointer, SizeOfAllocatedBytes);
            IsCompiled = true;
            SizeOfAllocatedBytes = emittedCode.Length;      //Update the size to the new one
        }*/
    }
}
