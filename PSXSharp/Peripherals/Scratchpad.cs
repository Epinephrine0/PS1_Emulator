using System;

namespace PSXSharp {
    public class Scratchpad {
        public Range range = new Range(0x1F800000, 0x3FF+1);
        byte[] data = new byte[1024];       

        public UInt32 LoadWord(UInt32 address) {
            uint offset = address - range.start;

            UInt32 b0 = data[offset + 0];
            UInt32 b1 = data[offset + 1];
            UInt32 b2 = data[offset + 2];
            UInt32 b3 = data[offset + 3];

            return b0 | (b1 << 8) | (b2 << 16) | (b3 << 24);
        }
        public void StoreWord(UInt32 address, UInt32 value) {
            uint offset = address - range.start;

            byte b0 = (byte)value;
            byte b1 = (byte)(value >> 8);
            byte b2 = (byte)(value >> 16);
            byte b3 = (byte)(value >> 24);

            data[offset + 0] = b0;
            data[offset + 1] = b1;
            data[offset + 2] = b2;
            data[offset + 3] = b3;

        }
        internal UInt16 LoadHalf(UInt32 address) {
            uint offset = address - range.start;

            UInt16 b0 = data[offset + 0];
            UInt16 b1 = data[offset + 1];

            return (UInt16)(b0 | (b1 << 8));
        }
        internal void StoreHalf(UInt32 address, UInt16 value) {
            uint offset = address - range.start;

            byte b0 = (byte)value;
            byte b1 = (byte)(value >> 8);

            data[offset + 0] = b0;
            data[offset + 1] = b1;
        }
        internal byte LoadByte(UInt32 address) {
            uint offset = address - range.start;
            return data[offset];
        }
        internal void StoreByte(UInt32 address, byte value) {
            uint offset = address - range.start;
            data[offset] = value;
        }
    }
}
