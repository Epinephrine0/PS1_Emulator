using System;

namespace PSXEmulator {
    public class RAM {
        //2MB RAM can be mirrored to the first 8MB (strangely, enabled by default)
        public Range range = new Range(0x00000000, 8*1024*1024);
        byte[] data = new byte[2 * 1024 * 1024];

        public UInt32 LoadWord(UInt32 address) {
            //CPU.cycles++;
            uint offset = address - range.start;
            int start = 0;
            int end = data.Length;
            uint final = (uint)(start + ((offset - start) % (end - start)));

            UInt32 b0 = data[final + 0];
            UInt32 b1 = data[final + 1];
            UInt32 b2 = data[final + 2];
            UInt32 b3 = data[final + 3];

            return (b0 | (b1 << 8) | (b2 << 16) | (b3 << 24));
        }
        public void StoreWord(UInt32 address, UInt32 value) {
            //CPU.cycles++;
            uint offset = address - range.start;
            int start = 0;
            int end = data.Length;
            uint final = (uint)(start + ((offset - start) % (end - start)));

            byte b0 = (byte)value;
            byte b1 = (byte)(value >> 8);
            byte b2 = (byte)(value >> 16);
            byte b3 = (byte)(value >> 24);


            data[final + 0] = b0;
            data[final + 1] = b1;
            data[final + 2] = b2;
            data[final + 3] = b3;

        }

        internal UInt16 LoadHalf(UInt32 address) {
            //CPU.cycles++;
            uint offset = address - range.start;
            int start = 0;
            int end = data.Length;
            uint final = (uint)(start + ((offset - start) % (end - start)));

            UInt16 b0 = data[final + 0];
            UInt16 b1 = data[final + 1];

            return ((UInt16)(b0 | (b1 << 8)));
        }

        internal void StoreHalf(UInt32 address, UInt16 value) {
            //CPU.cycles++;
            uint offset = address - range.start;
            int start = 0;
            int end = data.Length;
            uint final = (uint)(start + ((offset - start) % (end - start)));

            byte b0 = (byte)value;
            byte b1 = (byte)(value >> 8);

            data[final + 0] = b0;
            data[final + 1] = b1;
        }
        internal byte LoadByte(UInt32 address) {
            //CPU.cycles++;
            uint offset = address - range.start;
            int start = 0;
            int end = data.Length;
            uint final = (uint)(start + ((offset - start) % (end - start)));

            return data[final];
        }

        internal void StoreByte(UInt32 address, byte value) {
            //CPU.cycles++;
            uint offset = address - range.start;
            int start = 0;
            int end = data.Length;
            uint final = (uint)(start + ((offset - start) % (end - start)));

            data[final] = value;
        }
       
    }
}
