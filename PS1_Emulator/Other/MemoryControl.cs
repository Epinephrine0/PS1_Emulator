using System;

namespace PSXEmulator {

    public class MemoryControl {
        public Range range = new Range(0x1f801000, 0x24);

        //Addresses
        const uint EXPANSION1_BASE = 0x1F801000;
        const uint EXPANSION2_BASE = 0x1F801004;
        const uint EXPANSION1_DELAY = 0x1F801008;
        const uint EXPANSION3_DELAY = 0x1F80100C;
        const uint BIOS_ROM = 0x1F801010;
        const uint SPU_DELAY = 0x1F801014;
        const uint CDROM_DELAY = 0x1F801018;
        const uint EXPANSION2_DELAY = 0x1F80101C;
        const uint COMMON_DELAY = 0x1F801020;

        public uint Read(uint address) {
            //Return the "usually" value based on PSX-SPX
            //Some of them have multiple "usually" value..
            switch (address) {
                case EXPANSION1_BASE: return 0x1F000000;
                case EXPANSION2_BASE: return 0x1F802000;
                case EXPANSION1_DELAY: return 0x0013243F;
                case EXPANSION3_DELAY: return 0x00003022;
                case BIOS_ROM: return 0x0013243F;
                case SPU_DELAY: return 0x200931E1;
                case CDROM_DELAY: return 0x00020843;
                case EXPANSION2_DELAY: return 0x00070777;
                case COMMON_DELAY: return 0x00031125;
                default: throw new Exception("Memory Control load at address: " + address.ToString("x"));
            }
         }
        public void Write(uint address, uint value) {
            //Ignore writing
            //TODO: Implement the actual delays?
            //Console.WriteLine("[MemoryControl1] Ignored writing to: " + address.ToString("x"));
        }

        internal byte Read() {
            throw new NotImplementedException();
        }
    }
}
