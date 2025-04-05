namespace PSXSharp.Core.Common {
    public static class Utility {
        public static bool IsPowerOf2(ulong value) {
            return (value != 0) && (value & (value - 1)) == 0;
        }

        public static bool IsPowerOf2(long value) {
            return (value != 0) && (value & (value - 1)) == 0;
        }
    }
}
