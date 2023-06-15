using System;
using System.IO;

namespace PSXEmulator {
    public class BIOS {
        byte[] data;
        UInt32 size = 512 * 1024;
        public Range range = new Range(0x1fc00000, 512 * 1024);
        public string ID { get; set; }  

        public BIOS(string path) {
            data = File.ReadAllBytes(path);

            if (data.Length!=size) {

                throw new Exception("BIOS file is not valid");
            }

            for (int i = 0x12C; i < 0x13E; i++) {
                ID += (char)data[i];
            }
        }
        

        public UInt32 loadWord(UInt32 address) {
            uint offset = address - range.start;
            CPU.cycles += 20;

            UInt32 b0 = data[offset + 0];
            UInt32 b1 = data[offset + 1];
            UInt32 b2 = data[offset + 2];
            UInt32 b3 = data[offset + 3];

            return (b0 | (b1 << 8) | (b2 << 16) | (b3 << 24));
        }

        public byte loadByte(UInt32 address) {
            CPU.cycles++;
            uint offset = address - range.start;
            return data[offset];
        }

        internal UInt16 loadHalf(uint address) {
            CPU.cycles += 2;
            uint offset = address - range.start;

            UInt32 b0 = data[offset + 0];
            UInt32 b1 = data[offset + 1];
       
            return (ushort)(b0 | (b1 << 8));
        }

    }
}
