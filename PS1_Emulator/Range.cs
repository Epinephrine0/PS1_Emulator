namespace PSXEmulator {
    public class Range {
        public uint start;
        public uint length;

        public Range(uint start, uint length) {
            this.start = start;
            this.length = length;
        }

        public bool Contains(uint address) {
            return address >= start && address < start + length;
        }

    }
}
