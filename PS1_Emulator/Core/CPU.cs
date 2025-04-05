﻿namespace PSXEmulator.Core {
    public interface CPU {
        public abstract void Reset();
        public abstract void TickFrame();
        public abstract long GetSpeed();

        public ref BUS GetBUS();

        //R3000 Registers
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

        //Exception codes
        public enum Exceptions {
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
    }
}
