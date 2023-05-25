using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace PS1_Emulator {
    public class RAM {
                    
        public Range range = new Range(0x00000000, 8*1024*1024);        //2MB RAM can be mirrored to the first 8MB (strangely, enabled by default)
        byte[] data;

        public RAM() {

            this.data = new byte[2*1024*1024];
            
        }

        public UInt32 read(UInt32 offset) {

            CPU.cycles++;
            int start = 0;
            int end = data.Length;
            uint final = (uint)(start + ((offset - start) % (end - start)));

            UInt32 b0 = data[final + 0];
            UInt32 b1 = data[final + 1];
            UInt32 b2 = data[final + 2];
            UInt32 b3 = data[final + 3];

            return (b0 | (b1 << 8) | (b2 << 16) | (b3 << 24));
        }
        public void write(UInt32 offset, UInt32 value) {
            CPU.cycles++;
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

        internal void store8(UInt32 offset, byte value) {
            CPU.cycles++;
            int start = 0;
            int end = data.Length;
            uint final = (uint)(start + ((offset - start) % (end - start)));

            data[final] = value;
        }
        internal void store16(UInt32 offset, UInt16 value) {
            CPU.cycles++;
            int start = 0;
            int end = data.Length;
            uint final = (uint)(start + ((offset - start) % (end - start)));

            byte b0 = (byte)value;
            byte b1 = (byte)(value >> 8);

            data[final + 0] = b0;
            data[final + 1] = b1;
        }
        internal byte load8(UInt32 offset) {

            CPU.cycles++;
            int start = 0;
            int end = data.Length;
            uint final = (uint)(start + ((offset - start) % (end - start)));

            return data[final];
        }

        internal UInt16 load16(UInt32 offset) {

            CPU.cycles++;
            int start = 0;
            int end = data.Length;
            uint final = (uint)(start + ((offset - start) % (end - start)));

            UInt16 b0 = data[final + 0];
            UInt16 b1 = data[final + 1];

            return ((UInt16)(b0 | (b1 << 8)));
        }

    }
}
