using System;

namespace PSXSharp {
    public class CircularBuffer {
        private byte[] Buffer;
        private int Length;
        private int Pointer;
        private int ActualLength;
        public bool HasUnreadData = false;

        public CircularBuffer(int size) {
            this.Buffer = new byte[size];   
            this.Length = size;
            this.Pointer = 0;
        }
        public byte ReadNext() {
            byte data = Buffer[Pointer];
            if ((Pointer + 1) >= ActualLength) {
                HasUnreadData = false;
            }
            Pointer = (Pointer + 1) % Length;
            return data;
        }
        public void WriteBuffer(ref byte[] data) {
            if(data.Length > this.Length) { throw new Exception("Buffer does not fit the data length"); }
            Pointer = 0;
            ActualLength = data.Length;
            Clear();

            for (int i = 0; i < data.Length; i++) {
                Buffer[i] = data[i];
            }
            HasUnreadData = true;
        }
        private void Clear() {
            for (int i = 0; i < Buffer.Length; i++) {
                Buffer[i] = 0;
            }
        }
    }
}
