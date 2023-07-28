namespace PSXEmulator {
    public class Response {
        public byte[] values;
        public int interrupt;
        public long delay;
        public CD_ROM.CDROMState NextState;
        public bool FinishedProcessing;
        public Response(byte[] values, CD_ROM.Delays delay, CD_ROM.Flags INT, CD_ROM.CDROMState nextState) {
            this.NextState = nextState;
            this.values = values;
            this.delay = (long)delay;
            this.interrupt = (int)INT;
        }
        public Response(byte[] values, long delay, int INT, CD_ROM.CDROMState nextState) {    //Overload
            this.NextState = nextState;
            this.values = values;
            this.delay = delay;
            this.interrupt = INT;
        }
    }
}
