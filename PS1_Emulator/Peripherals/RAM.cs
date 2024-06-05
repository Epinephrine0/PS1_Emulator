using System;
using System.Runtime.InteropServices;

namespace PSXEmulator {
    public class RAM {
        //2MB RAM can be mirrored to the first 8MB (strangely, enabled by default)
        public Range range = new Range(0x00000000, 8*1024*1024);
        byte[] data = new byte[2 * 1024 * 1024];

        public uint LoadWord(uint address) {
            uint offset = address - range.start;
            uint final = Mirror(offset);

            byte b0 = data[final + 0];
            byte b1 = data[final + 1];
            byte b2 = data[final + 2];
            byte b3 = data[final + 3];

            return (uint)(b0 | (b1 << 8) | (b2 << 16) | (b3 << 24));
        }

        public void StoreWord(uint address, uint value) {
            uint offset = address - range.start;
            uint final = Mirror(offset);

            byte b0 = (byte)value;
            byte b1 = (byte)(value >> 8);
            byte b2 = (byte)(value >> 16);
            byte b3 = (byte)(value >> 24);

            data[final + 0] = b0;
            data[final + 1] = b1;
            data[final + 2] = b2;
            data[final + 3] = b3;
        }

        public ushort LoadHalf(uint address) {
            uint offset = address - range.start;
            uint final = Mirror(offset);

            ushort b0 = data[final + 0];
            ushort b1 = data[final + 1];

            return ((ushort)(b0 | (b1 << 8)));
        }

        public void StoreHalf(uint address, ushort value) {
            uint offset = address - range.start;
            uint final = Mirror(offset);

            byte b0 = (byte)value;
            byte b1 = (byte)(value >> 8);

            data[final + 0] = b0;
            data[final + 1] = b1;
        }

        public byte LoadByte(uint address) {
            uint offset = address - range.start;
            uint final = Mirror(offset);

            return data[final];
        }

        public void StoreByte(uint address, byte value) {
            uint offset = address - range.start;
            uint final = Mirror(offset);

            data[final] = value;
        }

        public uint Mirror(uint address) {
            //Handle memory mirror, but without %  
            //x % (2^n) is equal to x & ((2^n)-1)
            //So x % 2MB = x & ((2^21)-1)
            return address & ((1 << 21) - 1);
        }

        public byte[] GetMemory() {
            return data;
        }      
    }
}
