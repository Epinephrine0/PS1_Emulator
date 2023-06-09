using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PS1_Emulator {
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
        

        public UInt32 fetch(UInt32 offset) {
            CPU.cycles += 20;

            UInt32 b0 = data[offset + 0];
            UInt32 b1 = data[offset + 1];
            UInt32 b2 = data[offset + 2];
            UInt32 b3 = data[offset + 3];

            return (b0 | (b1 << 8) | (b2 << 16) | (b3 << 24));
        }

        public byte load8(UInt32 offset) {
            CPU.cycles++;


            return data[offset];
        }

        internal UInt16 load16(uint offset) {
            CPU.cycles += 2;

            UInt32 b0 = data[offset + 0];
            UInt32 b1 = data[offset + 1];
       

            return ((ushort)(b0 | (b1 << 8)));
        }

    }
}
