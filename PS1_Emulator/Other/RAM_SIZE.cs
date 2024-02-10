using System;

namespace PSXEmulator {
    public class RAM_SIZE {      //Configured by bios
        public Range range = new Range(0x1f801060, 4);
        uint RamSize = 0x00000B88; //(usually 00000B88h) (or 00000888h)

        public void StoreWord(UInt32 size) {
            RamSize = size;
        }

        public uint LoadWord() {
            return RamSize;
        }
    }
}
