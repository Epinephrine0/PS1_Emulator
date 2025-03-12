using PSXEmulator.Core.Common;
using System;
using System.Runtime.CompilerServices;

namespace PSXEmulator.Core.Recompiler {
    public static unsafe class MSIL_LUT {

        public static readonly delegate*<CPURecompiler, Instruction, void>[] MainLookUpTable = new delegate*<CPURecompiler, Instruction, void>[] {
                &special,   &bxx,       &jump,      &jal,       &beq,        &bne,       &blez,      &bgtz,
                &addi,      &addiu,     &slti,      &sltiu,     &andi,       &ori,       &xori,      &lui,
                &cop0,      &cop1,      &cop2,      &cop3,      &illegal,    &illegal,   &illegal,   &illegal,
                &illegal,   &illegal,   &illegal,   &illegal,   &illegal,    &illegal,   &illegal,   &illegal,
                &lb,        &lh,        &lwl,       &lw,        &lbu,        &lhu,       &lwr,       &illegal,
                &sb,        &sh,        &swl,       &sw,        &illegal,    &illegal,   &swr,       &illegal,
                &lwc0,      &lwc1,      &lwc2,      &lwc3,      &illegal,    &illegal,   &illegal,   &illegal,
                &swc0,      &swc1,      &swc2,      &swc3,      &illegal,    &illegal,   &illegal,   &illegal
        };

        public static readonly delegate*<CPURecompiler, Instruction, void>[] SpecialLookUpTable = new delegate*<CPURecompiler, Instruction, void>[] {
                &sll,       &illegal,   &srl,       &sra,       &sllv,      &illegal,   &srlv,      &srav,
                &jr,        &jalr,      &illegal,   &illegal,   &syscall,   &break_,    &illegal,   &illegal,
                &mfhi,      &mthi,      &mflo,      &mtlo,      &illegal,   &illegal,   &illegal,   &illegal,
                &mult,      &multu,     &div,       &divu,      &illegal,   &illegal,   &illegal,   &illegal,
                &add,       &addu,      &sub,       &subu,      &and,       &or,        &xor,       &nor,
                &illegal,   &illegal,   &slt,       &sltu,      &illegal,   &illegal,   &illegal,   &illegal,
                &illegal,   &illegal,   &illegal,   &illegal,   &illegal,   &illegal,   &illegal,   &illegal,
                &illegal,   &illegal,   &illegal,   &illegal,   &illegal,   &illegal,   &illegal,   &illegal
        };

        private static void illegal(CPURecompiler cpu, Instruction instruction) {
            throw new Exception("Illegal");
        }

        private static void special(CPURecompiler cpu, Instruction instruction) {
            SpecialLookUpTable[instruction.Get_Subfunction()](cpu, instruction);
        }

        private static void bxx(CPURecompiler cpu, Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint imm = instruction.GetSignedImmediate();
            bool bgez = ((instruction.FullValue >> 16) & 1) == 1;
            bool link = ((instruction.FullValue >> 17) & 0xF) == 0x8;
            MSIL_JIT.EmitBXX(rs, imm, link, bgez, cpu.CurrentBlock);
        }

        private static void jump(CPURecompiler cpu, Instruction instruction) {
            uint target = (cpu.Next_PC & 0xf0000000) | (instruction.GetImmediateJumpAddress() << 2);
            MSIL_JIT.EmitJump(target, cpu.CurrentBlock);
        }

        private static void jal(CPURecompiler cpu, Instruction instruction) {
            uint target = (cpu.Next_PC & 0xf0000000) | (instruction.GetImmediateJumpAddress() << 2);
            MSIL_JIT.EmitJal(target, cpu.CurrentBlock);
        }

        private static void beq(CPURecompiler cpu, Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            uint imm = instruction.GetSignedImmediate();
            MSIL_JIT.EmitBranchIf(rs, rt, imm, BranchIf.BEQ, cpu.CurrentBlock);
        }

        private static void bne(CPURecompiler cpu, Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            uint imm = instruction.GetSignedImmediate();
            MSIL_JIT.EmitBranchIf(rs, rt, imm, BranchIf.BNE, cpu.CurrentBlock);
        }

        private static void blez(CPURecompiler cpu, Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint imm = instruction.GetSignedImmediate();
            MSIL_JIT.EmitBranchIf(rs, 0, imm, BranchIf.BLEZ, cpu.CurrentBlock);
        }

        private static void bgtz(CPURecompiler cpu, Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint imm = instruction.GetSignedImmediate();
            MSIL_JIT.EmitBranchIf(rs, 0, imm, BranchIf.BGTZ, cpu.CurrentBlock);
        }

        private static void addi(CPURecompiler cpu, Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            uint imm = instruction.GetSignedImmediate();
            MSIL_JIT.EmitArithmetic_i(rs, rt, imm, ArithmeticSignals.ADD, cpu.CurrentBlock);
        }

        private static void addiu(CPURecompiler cpu, Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            uint imm = instruction.GetSignedImmediate();
            MSIL_JIT.EmitArithmeticI_U(rs, rt, imm, ArithmeticSignals.ADD, cpu.CurrentBlock);
        }

        private static void slti(CPURecompiler cpu, Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            uint imm = instruction.GetSignedImmediate();
            MSIL_JIT.EmitSlti(rs, rt, imm, true, cpu.CurrentBlock);
        }

        private static void sltiu(CPURecompiler cpu, Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            uint imm = instruction.GetSignedImmediate();
            MSIL_JIT.EmitSlti(rs, rt, imm, false, cpu.CurrentBlock);
        }

        private static void andi(CPURecompiler cpu, Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            uint imm = instruction.GetImmediate();
            MSIL_JIT.EmitLogic_i(rs, rt, imm, LogicSignals.AND, cpu.CurrentBlock);
        }

        private static void ori(CPURecompiler cpu, Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            uint imm = instruction.GetImmediate();
            MSIL_JIT.EmitLogic_i(rs, rt, imm, LogicSignals.OR, cpu.CurrentBlock);
        }

        private static void xori(CPURecompiler cpu, Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            uint imm = instruction.GetImmediate();
            MSIL_JIT.EmitLogic_i(rs, rt, imm, LogicSignals.XOR, cpu.CurrentBlock);
        }

        private static void lui(CPURecompiler cpu, Instruction instruction) {
            uint rt = instruction.Get_rt();
            uint imm = instruction.GetImmediate();
            MSIL_JIT.EmitLUI(rt, imm, cpu.CurrentBlock);
        }

        private static void cop0(CPURecompiler cpu, Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            uint rd = instruction.Get_rd();

            switch (rs) {
                case 0b00100:
                    MSIL_JIT.EmitMTC0(rt, rd, cpu.CurrentBlock);
                    break;

                case 0b00000:
                    MSIL_JIT.EmitMFC0(rt, rd, cpu.CurrentBlock);
                    break;

                case 0b10000:
                    MSIL_JIT.EmitRFE(cpu.CurrentBlock);
                    break;
                default: throw new Exception("Unhandled cop0 instruction: " + instruction.FullValue.ToString("X"));
            }
        }

        private static void cop1(CPURecompiler cpu, Instruction instruction) {
            throw new Exception("Illegal");
        }

        private static void cop2(CPURecompiler cpu, Instruction instruction) {
            if ((instruction.FullValue >> 25) == 0b0100101) {    //COP2 imm25 command
                MSIL_JIT.EmitCOP2Command(instruction.FullValue, cpu.CurrentBlock);
                return;
            }

            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            uint rd = instruction.Get_rd();

            switch (rs) {
                case 0b00000:   //MFC
                    MSIL_JIT.EmitMFC2_CFC2(rt, rd, cpu.CurrentBlock);
                    break;

                case 0b00010:   //CFC                 
                    MSIL_JIT.EmitMFC2_CFC2(rt, rd + 32, cpu.CurrentBlock);
                    break;

                case 0b00110:  //CTC                  
                    MSIL_JIT.EmitMTC2_CTC2(rt, rd + 32, cpu.CurrentBlock);
                    break;

                case 0b00100:  //MTC                  
                    MSIL_JIT.EmitMTC2_CTC2(rt, rd, cpu.CurrentBlock);
                    break;

                default: throw new Exception("Unhandled GTE opcode: " + instruction.Get_rs().ToString("X"));
            }
        }

        private static void cop3(CPURecompiler cpu, Instruction instruction) {
            throw new Exception("Illegal");
        }

        private static void lb(CPURecompiler cpu, Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            uint imm = instruction.GetSignedImmediate();
            MSIL_JIT.EmitMemoryLoad(rs, rt, imm, MemoryReadWriteSize.BYTE, true, cpu.CurrentBlock);
        }

        private static void lh(CPURecompiler cpu, Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            uint imm = instruction.GetSignedImmediate();
            MSIL_JIT.EmitMemoryLoad(rs, rt, imm, MemoryReadWriteSize.HALF, true, cpu.CurrentBlock);
        }

        private static void lw(CPURecompiler cpu, Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            uint imm = instruction.GetSignedImmediate();
            MSIL_JIT.EmitMemoryLoad(rs, rt, imm, MemoryReadWriteSize.WORD, false, cpu.CurrentBlock);
        }

        private static void lbu(CPURecompiler cpu, Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            uint imm = instruction.GetSignedImmediate();
            MSIL_JIT.EmitMemoryLoad(rs, rt, imm, MemoryReadWriteSize.BYTE, false, cpu.CurrentBlock);
        }

        private static void lhu(CPURecompiler cpu, Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            uint imm = instruction.GetSignedImmediate();
            MSIL_JIT.EmitMemoryLoad(rs, rt, imm, MemoryReadWriteSize.HALF, false, cpu.CurrentBlock);
        }

        private static void sb(CPURecompiler cpu, Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            uint imm = instruction.GetSignedImmediate();
            MSIL_JIT.EmitMemoryStore(rs, rt, imm, MemoryReadWriteSize.BYTE, cpu.CurrentBlock);
        }

        private static void sh(CPURecompiler cpu, Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            uint imm = instruction.GetSignedImmediate();
            MSIL_JIT.EmitMemoryStore(rs, rt, imm, MemoryReadWriteSize.HALF, cpu.CurrentBlock);
        }

        private static void sw(CPURecompiler cpu, Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            uint imm = instruction.GetSignedImmediate();
            MSIL_JIT.EmitMemoryStore(rs, rt, imm, MemoryReadWriteSize.WORD, cpu.CurrentBlock);
        }

        private static void swl(CPURecompiler cpu, Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            uint imm = instruction.GetSignedImmediate();
            MSIL_JIT.EmitSWL_SWR(rs, rt, imm, true, cpu.CurrentBlock);
        }

        private static void swr(CPURecompiler cpu, Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            uint imm = instruction.GetSignedImmediate();
            MSIL_JIT.EmitSWL_SWR(rs, rt, imm, false, cpu.CurrentBlock);
        }

        private static void lwl(CPURecompiler cpu, Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            uint imm = instruction.GetSignedImmediate();
            MSIL_JIT.EmitLWL_LWR(rs, rt, imm, true, cpu.CurrentBlock);
        }

        private static void lwr(CPURecompiler cpu, Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            uint imm = instruction.GetSignedImmediate();
            MSIL_JIT.EmitLWL_LWR(rs, rt, imm, false, cpu.CurrentBlock);
        }

        private static void lwc0(CPURecompiler cpu, Instruction instruction) {
            throw new Exception("Illegal");
        }

        private static void lwc1(CPURecompiler cpu, Instruction instruction) {
            throw new Exception("Illegal");
        }

        private static void lwc2(CPURecompiler cpu, Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            uint imm = instruction.GetSignedImmediate();
            MSIL_JIT.EmitLWC2(rs, rt, imm, cpu.CurrentBlock);
        }

        private static void lwc3(CPURecompiler cpu, Instruction instruction) {
            throw new Exception("Illegal");
        }

        private static void swc0(CPURecompiler cpu, Instruction instruction) {
            throw new Exception("Illegal");
        }

        private static void swc1(CPURecompiler cpu, Instruction instruction) {
            throw new Exception("Illegal");
        }

        private static void swc2(CPURecompiler cpu, Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            uint imm = instruction.GetSignedImmediate();
            MSIL_JIT.EmitSWC2(rs, rt, imm, cpu.CurrentBlock);
        }

        private static void swc3(CPURecompiler cpu, Instruction instruction) {
            throw new Exception("Illegal");
        }

        private static void sll(CPURecompiler cpu, Instruction instruction) {
            uint rt = instruction.Get_rt();
            uint rd = instruction.Get_rd();
            uint sa = instruction.Get_sa();
            MSIL_JIT.EmitShift(rt, rd, sa, ShiftSignals.LEFT, cpu.CurrentBlock);
        }

        private static void srl(CPURecompiler cpu, Instruction instruction) {
            uint rt = instruction.Get_rt();
            uint rd = instruction.Get_rd();
            uint sa = instruction.Get_sa();
            MSIL_JIT.EmitShift(rt, rd, sa, ShiftSignals.RIGHT, cpu.CurrentBlock);
        }

        private static void sra(CPURecompiler cpu, Instruction instruction) {
            uint rt = instruction.Get_rt();
            uint rd = instruction.Get_rd();
            uint sa = instruction.Get_sa();
            MSIL_JIT.EmitShift(rt, rd, sa, ShiftSignals.RIGHT_ARITHMETIC, cpu.CurrentBlock);
        }

        private static void sllv(CPURecompiler cpu, Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            uint rd = instruction.Get_rd();
            MSIL_JIT.EmitShift_v(rs, rt, rd, ShiftSignals.LEFT, cpu.CurrentBlock);
        }

        private static void srlv(CPURecompiler cpu, Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            uint rd = instruction.Get_rd();
            MSIL_JIT.EmitShift_v(rs, rt, rd, ShiftSignals.RIGHT, cpu.CurrentBlock);
        }

        private static void srav(CPURecompiler cpu, Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            uint rd = instruction.Get_rd();
            MSIL_JIT.EmitShift_v(rs, rt, rd, ShiftSignals.RIGHT_ARITHMETIC, cpu.CurrentBlock);
        }

        private static void jr(CPURecompiler cpu, Instruction instruction) {
            uint rs = instruction.Get_rs();
            MSIL_JIT.EmitJR(rs, cpu.CurrentBlock);
        }

        private static void jalr(CPURecompiler cpu, Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rd = instruction.Get_rd();
            MSIL_JIT.EmitJalr(rs, rd, cpu.CurrentBlock);
        }

        private static void syscall(CPURecompiler cpu, Instruction instruction) {
            MSIL_JIT.EmitSyscall(cpu.CurrentBlock);
        }

        private static void break_(CPURecompiler cpu, Instruction instruction) {
            MSIL_JIT.EmitBreak(cpu.CurrentBlock);
        }

        private static void mfhi(CPURecompiler cpu, Instruction instruction) {
            uint rd = instruction.Get_rd();
            MSIL_JIT.EmitMF(rd, true, cpu.CurrentBlock);
        }

        private static void mthi(CPURecompiler cpu, Instruction instruction) {
            uint rs = instruction.Get_rs();
            MSIL_JIT.EmitMT(rs, true, cpu.CurrentBlock);
        }

        private static void mflo(CPURecompiler cpu, Instruction instruction) {
            uint rd = instruction.Get_rd();
            MSIL_JIT.EmitMF(rd, false, cpu.CurrentBlock);
        }

        private static void mtlo(CPURecompiler cpu, Instruction instruction) {
            uint rs = instruction.Get_rs();
            MSIL_JIT.EmitMT(rs, false, cpu.CurrentBlock);
        }

        private static void mult(CPURecompiler cpu, Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            MSIL_JIT.EmitMULT(rs, rt, true, cpu.CurrentBlock);
        }

        private static void multu(CPURecompiler cpu, Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            MSIL_JIT.EmitMULT(rs, rt, false, cpu.CurrentBlock);
        }

        private static void div(CPURecompiler cpu, Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            MSIL_JIT.EmitDIV(rs, rt, true, cpu.CurrentBlock);
        }

        private static void divu(CPURecompiler cpu, Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            MSIL_JIT.EmitDIV(rs, rt, false, cpu.CurrentBlock);
        }

        private static void add(CPURecompiler cpu, Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            uint rd = instruction.Get_rd();
            MSIL_JIT.EmitArithmetic(rs, rt, rd, ArithmeticSignals.ADD, cpu.CurrentBlock);
        }

        private static void addu(CPURecompiler cpu, Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            uint rd = instruction.Get_rd();
            MSIL_JIT.EmitArithmeticU(rs, rt, rd, ArithmeticSignals.ADD, cpu.CurrentBlock);
        }

        private static void subu(CPURecompiler cpu, Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            uint rd = instruction.Get_rd();
            MSIL_JIT.EmitArithmeticU(rs, rt, rd, ArithmeticSignals.SUB, cpu.CurrentBlock);
        }

        private static void sub(CPURecompiler cpu, Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            uint rd = instruction.Get_rd();
            MSIL_JIT.EmitArithmetic(rs, rt, rd, ArithmeticSignals.SUB, cpu.CurrentBlock);
        }

        public static void or(CPURecompiler cpu, Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            uint rd = instruction.Get_rd();
            MSIL_JIT.EmitLogic(rs, rt, rd, LogicSignals.OR, cpu.CurrentBlock);
        }

        private static void and(CPURecompiler cpu, Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            uint rd = instruction.Get_rd();
            MSIL_JIT.EmitLogic(rs, rt, rd, LogicSignals.AND, cpu.CurrentBlock);
        }

        private static void xor(CPURecompiler cpu, Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            uint rd = instruction.Get_rd();
            MSIL_JIT.EmitLogic(rs, rt, rd, LogicSignals.XOR, cpu.CurrentBlock);
        }

        private static void nor(CPURecompiler cpu, Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            uint rd = instruction.Get_rd(); 
            MSIL_JIT.EmitLogic(rs, rt, rd, LogicSignals.NOR, cpu.CurrentBlock);
        }

        private static void slt(CPURecompiler cpu, Instruction instruction) {
            uint rd = instruction.Get_rd();
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            MSIL_JIT.EmitSlt(rs, rt, rd, true, cpu.CurrentBlock);
        }

        private static void sltu(CPURecompiler cpu, Instruction instruction) {
            uint rd = instruction.Get_rd();
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            MSIL_JIT.EmitSlt(rs, rt, rd, false, cpu.CurrentBlock);
        }
    }
}
