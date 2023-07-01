using System;

namespace PSXEmulator {

    public class MemoryControl {
        public Range range = new Range(0x1f801000, 36);
        public uint CDROM_delay_read;
        public uint CDROM_delay_write;

        public void storeWord(uint address, uint value) {
            uint offset = range.start - address;
            switch (offset) {
                case 0:
                    if (value != 0x1f000000) {
                        throw new Exception("Bad expansion 1 base address: " + value.ToString("X"));
                    }
                    break;

                case 4:
                    if (value != 0x1f802000) {
                        throw new Exception("Bad expansion 1 base address: " + value.ToString("X"));
                    }
                    break;

                case 0x18:

                    CDROM_delay_write = (byte)((value & 0xF) + 1);  //testing
                    CDROM_delay_read = (byte)(((value >> 4) & 0xF) + 1);  //testing

                    break;
                default:
                    //Debug.WriteLine("Unhandled write to MEMCONTROL register, address: " + address.ToString("X"));
                    break;
            }

        }
    }
}
