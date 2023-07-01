namespace PSXEmulator {
    public class MDEC { //TODO
        public Range range = new Range(0x1F801820,5);

        internal uint read(uint address) {
            uint offset = address - range.start;
            return 0;
        }

        internal void write(uint address, uint value) {
            uint offset = address - range.start;
        }
    }
}
