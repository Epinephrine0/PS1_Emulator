using System;

namespace PSXEmulator.Peripherals.MDEC {
    class Macroblock {
        byte[] Data;
        int Ptr = 0;

        public bool HasBeenRead => Ptr + 1 >= Data.Length;

        public Macroblock(uint bpp) {
            switch (bpp) {
                case 0: Data = new byte[8 * 8 / 2]; break;
                case 1: Data = new byte[8 * 8]; break;
                case 2: Data = new byte[16 * 16 * 3]; break;
                case 3: Data = new byte[16 * 16 * 2]; break;
                default: throw new Exception("[MDEC] Unknown BPP: " + bpp);
            }
        }

        public void Write(byte value, int position) {
            Data[position] = value;
        }

        public uint ReadNext() {
            byte data0 = Data[Ptr++];
            byte data1 = Data[Ptr++];
            byte data2 = Data[Ptr++];
            byte data3 = Data[Ptr++];
            return (uint)(data0 | data1 << 8 | data2 << 16 | data3 << 24);
        }

        public ushort Size() {  return (ushort)Data.Length; }
    }
}
