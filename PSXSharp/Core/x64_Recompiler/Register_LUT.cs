﻿using Iced.Intel;
using PSXSharp.Core.Common;
using PSXSharp.Core.x64_Recompiler;
using System;
using Instruction = PSXSharp.Core.Common.Instruction;

namespace PSXSharp.Core.MSIL_Recompiler {
    public static unsafe class Register_LUT {

        public static readonly delegate*<Instruction, uint[]>[] MainLookUpTable = [
                &special,   &bxx,       &jump,      &jal,       &beq,        &bne,       &blez,      &bgtz,
                &addi,      &addiu,     &slti,      &sltiu,     &andi,       &ori,       &xori,      &lui,
                &cop0,      &cop1,      &cop2,      &cop3,      &illegal,    &illegal,   &illegal,   &illegal,
                &illegal,   &illegal,   &illegal,   &illegal,   &illegal,    &illegal,   &illegal,   &illegal,
                &lb,        &lh,        &lwl,       &lw,        &lbu,        &lhu,       &lwr,       &illegal,
                &sb,        &sh,        &swl,       &sw,        &illegal,    &illegal,   &swr,       &illegal,
                &lwc0,      &lwc1,      &lwc2,      &lwc3,      &illegal,    &illegal,   &illegal,   &illegal,
                &swc0,      &swc1,      &swc2,      &swc3,      &illegal,    &illegal,   &illegal,   &illegal
        ];

        public static readonly delegate*<Instruction, uint[]>[] SpecialLookUpTable = [
                &sll,       &illegal,   &srl,       &sra,       &sllv,      &illegal,   &srlv,      &srav,
                &jr,        &jalr,      &illegal,   &illegal,   &syscall,   &break_,    &illegal,   &illegal,
                &mfhi,      &mthi,      &mflo,      &mtlo,      &illegal,   &illegal,   &illegal,   &illegal,
                &mult,      &multu,     &div,       &divu,      &illegal,   &illegal,   &illegal,   &illegal,
                &add,       &addu,      &sub,       &subu,      &and,       &or,        &xor,       &nor,
                &illegal,   &illegal,   &slt,       &sltu,      &illegal,   &illegal,   &illegal,   &illegal,
                &illegal,   &illegal,   &illegal,   &illegal,   &illegal,   &illegal,   &illegal,   &illegal,
                &illegal,   &illegal,   &illegal,   &illegal,   &illegal,   &illegal,   &illegal,   &illegal
        ];

        private static uint[] illegal(Instruction instruction) {
            throw new Exception("Illegal instruction!");
        }

        private static uint[] special(Instruction instruction) {
            return SpecialLookUpTable[instruction.Get_Subfunction()](instruction);
        }

        private static uint[] bxx(Instruction instruction) {
            uint rs = instruction.Get_rs();
            return [rs];
        }

        private static uint[] jump(Instruction instruction) {
            return null;
        }

        private static uint[] jal(Instruction instruction) {
            return null;
        }

        private static uint[] beq(Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            return [rt, rs];
        }

        private static uint[] bne(Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            return [rt, rs];
        }

        private static uint[] blez(Instruction instruction) {
            uint rs = instruction.Get_rs();
            return [rs];
        }

        private static uint[] bgtz(Instruction instruction) {
            uint rs = instruction.Get_rs();
            return [rs];
        }

        private static uint[] addi(Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            return [rt, rs];
        }

        private static uint[] addiu(Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            return [rt, rs];
        }

        private static uint[] slti(Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            return [rt, rs];
        }

        private static uint[] sltiu(Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            return [rt, rs];
        }

        private static uint[] andi(Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            return [rt, rs];
        }

        private static uint[] ori(Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            return [rt, rs];
        }

        private static uint[] xori(Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            return [rt, rs];
        }

        private static uint[] lui(Instruction instruction) {
            uint rt = instruction.Get_rt();
            return [rt];
        }

        private static uint[] cop0(Instruction instruction) {
            uint rt = instruction.Get_rt();
            uint rd = instruction.Get_rd();
            uint rs = instruction.Get_rs();

            switch (rs) {
                case 0b00100:  // MTC0
                case 0b00000:  // MFC0
                    return [rd, rt];
                case 0b10000:   // RFE
                    return null;
                default:
                    throw new Exception("Unhandled cop0 instruction: " + instruction.FullValue.ToString("X"));
            }
        }

        private static uint[] cop1(Instruction instruction) {
            throw new Exception("Illegal");
        }

        private static uint[] cop2(Instruction instruction) {
            if ((instruction.FullValue >> 25) == 0b0100101) {
                return null;
            }

            uint rt = instruction.Get_rt();
            uint rd = instruction.Get_rd();
            uint rs = instruction.Get_rs();

            switch (rs) {
                case 0b00000:   // MFC
                case 0b00010:   // CFC                 
                case 0b00110:   // CTC                  
                case 0b00100:   // MTC
                    return [rd, rt];
                default:
                    throw new Exception("Unhandled GTE opcode: " + instruction.Get_rs().ToString("X"));
            }
        }

        private static uint[] cop3(Instruction instruction) {
            throw new Exception("Illegal");
        }

        private static uint[] lb(Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            return [rt, rs];
        }

        private static uint[] lh(Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            return [rt, rs];
        }

        private static uint[] lw(Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            return [rt, rs];
        }

        private static uint[] lbu(Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            return [rt, rs];
        }

        private static uint[] lhu(Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            return [rt, rs];
        }

        private static uint[] sb(Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            return [rt, rs];
        }

        private static uint[] sh(Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            return [rt, rs];
        }

        private static uint[] sw(Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            return [rt, rs];
        }

        private static uint[] swl(Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            return [rt, rs];
        }

        private static uint[] swr(Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            return [rt, rs];
        }

        private static uint[] lwl(Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            return [rt, rs];
        }

        private static uint[] lwr(Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            return [rt, rs];
        }

        private static uint[] lwc0(Instruction instruction) {
            throw new Exception("Illegal");
        }

        private static uint[] lwc1(Instruction instruction) {
            throw new Exception("Illegal");
        }

        private static uint[] lwc2(Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            return [rt, rs];
        }

        private static uint[] lwc3(Instruction instruction) {
            throw new Exception("Illegal");
        }

        private static uint[] swc0(Instruction instruction) {
            throw new Exception("Illegal");
        }

        private static uint[] swc1(Instruction instruction) {
            throw new Exception("Illegal");
        }

        private static uint[] swc2(Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            return [rt, rs];
        }

        private static uint[] swc3(Instruction instruction) {
            throw new Exception("Illegal");
        }

        private static uint[] sll(Instruction instruction) {
            uint rt = instruction.Get_rt();
            uint rd = instruction.Get_rd();
            return [rd, rt];
        }

        private static uint[] srl(Instruction instruction) {
            uint rt = instruction.Get_rt();
            uint rd = instruction.Get_rd();
            return [rd, rt];
        }

        private static uint[] sra(Instruction instruction) {
            uint rt = instruction.Get_rt();
            uint rd = instruction.Get_rd();
            return [rd, rt];
        }

        private static uint[] sllv(Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            uint rd = instruction.Get_rd();
            return [rd, rt, rs];
        }

        private static uint[] srlv(Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            uint rd = instruction.Get_rd();
            return [rd, rt, rs];
        }

        private static uint[] srav(Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            uint rd = instruction.Get_rd();
            return [rd, rt, rs];
        }

        private static uint[] jr(Instruction instruction) {
            uint rs = instruction.Get_rs();
            return [rs];
        }

        private static uint[] jalr(Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rd = instruction.Get_rd();
            return [rd, rs];
        }

        private static uint[] syscall(Instruction instruction) {
            return null;
        }

        private static uint[] break_(Instruction instruction) {
            return null;
        }

        private static uint[] mfhi(Instruction instruction) {
            uint rd = instruction.Get_rd();
            return [rd];
        }

        private static uint[] mthi(Instruction instruction) {
            uint rs = instruction.Get_rs();
            return [rs];
        }

        private static uint[] mflo(Instruction instruction) {
            uint rd = instruction.Get_rd();
            return [rd];
        }

        private static uint[] mtlo(Instruction instruction) {
            uint rs = instruction.Get_rs();
            return [rs];
        }

        private static uint[] mult(Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            return [rt, rs];
        }

        private static uint[] multu(Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            return [rt, rs];
        }

        private static uint[] div(Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            return [rt, rs];
        }

        private static uint[] divu(Instruction instruction) {
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            return [rt, rs];
        }

        private static uint[] add(Instruction instruction) {
            uint rd = instruction.Get_rd();
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            return [rd, rt, rs];
        }

        private static uint[] addu(Instruction instruction) {
            uint rd = instruction.Get_rd();
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            return [rd, rt, rs];
        }

        private static uint[] subu(Instruction instruction) {
            uint rd = instruction.Get_rd();
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            return [rd, rt, rs];
        }

        private static uint[] sub(Instruction instruction) {
            uint rd = instruction.Get_rd();
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            return [rd, rt, rs];
        }

        private static uint[] or(Instruction instruction) {
            uint rd = instruction.Get_rd();
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            return [rd, rt, rs];
        }

        private static uint[] and(Instruction instruction) {
            uint rd = instruction.Get_rd();
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            return [rd, rt, rs];
        }

        private static uint[] xor(Instruction instruction) {
            uint rd = instruction.Get_rd();
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            return [rd, rt, rs];
        }

        private static uint[] nor(Instruction instruction) {
            uint rd = instruction.Get_rd();
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            return [rd, rt, rs];
        }

        private static uint[] slt(Instruction instruction) {
            uint rd = instruction.Get_rd();
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            return [rd, rt, rs];
        }

        private static uint[] sltu(Instruction instruction) {
            uint rd = instruction.Get_rd();
            uint rs = instruction.Get_rs();
            uint rt = instruction.Get_rt();
            return [rd, rt, rs];
        }
    }
}