using System;

namespace PSXEmulator {
    public class RAM_SIZE {      //Configured by bios

        public Range range = new Range(0x1f801060, 4);
        UInt32[] data = new UInt32[4];

        public void storeWord(UInt32 address ,UInt32 size) {
            uint offset = address - range.start;

            //data[offset] = size; 
        }

    }
}
