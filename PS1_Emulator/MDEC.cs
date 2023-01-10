using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PS1_Emulator {
    internal class MDEC {
        public Range range = new Range(0x1F801820,5);
        UInt32 cotrol;

        internal uint read(uint offset) {
            return 0;
        }

        internal void write(uint offset, uint value) {
            switch (offset) { 
            
                case 4:
                    cotrol = value;
                break;
            
               // default:

               //     throw new Exception("Unhandled MDEC write to offset: " + offset.ToString("x"));
            }

        }
    }
}
