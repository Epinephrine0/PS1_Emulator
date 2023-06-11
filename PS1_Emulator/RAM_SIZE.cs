using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PSXEmulator {
    public class RAM_SIZE {      //Configured by bios

        public Range range = new Range(0x1f801060, 4);
        UInt32[] data = new UInt32[4];


        public void set_Size(UInt32 offset ,UInt32 size) {
            //data[offset] = size; 
        }

    }
}
