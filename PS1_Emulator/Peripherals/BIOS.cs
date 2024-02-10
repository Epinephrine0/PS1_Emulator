using System;
using System.IO;

namespace PSXEmulator {
    public class BIOS {
        byte[] data;
        const uint size = 512 * 1024;
        public Range range = new Range(0x1fc00000, 512 * 1024);
        public string ID { get; set; }  

        public BIOS(string path) {
            data = File.ReadAllBytes(path);
            if (data.Length != size) {
                throw new Exception("BIOS file is not valid");
            }
        }
        
        public uint LoadWord(UInt32 address) {
            uint offset = address - range.start;
            uint b0 = data[offset + 0];
            uint b1 = data[offset + 1];
            uint b2 = data[offset + 2];
            uint b3 = data[offset + 3];

            return (b0 | (b1 << 8) | (b2 << 16) | (b3 << 24));
        }

        public byte LoadByte(uint address) {
            uint offset = address - range.start;
            return data[offset];
        }

        public ushort LoadHalf(uint address) {
            uint offset = address - range.start;

            uint b0 = data[offset + 0];
            uint b1 = data[offset + 1];
       
            return (ushort)(b0 | (b1 << 8));
        }

    }
}
