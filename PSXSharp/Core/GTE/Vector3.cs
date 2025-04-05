using System;

namespace PSXSharp {
    public class Vector3{   //Should be a struct
        public Vector3() {  //Overload

        }
        public Vector3(short x,short y, short z) {
            this.X = x;
            this.Y = y;
            this.Z = z;
        }
        public short X {
            get { return (short)XY; }   
            set { 
                XY &= 0xFFFF0000;
                XY |= ((ushort)value);
            }
        }
        public short Y {
            get { return (short)(XY >> 16); }
            set {
                XY &= 0x0000FFFF;
                XY = XY | ((uint)value << 16);
            }
        }
        public short Z {
            set; get;
        }
        public uint XY {
            set; get;
        }
        public short GetElement(int Num) {
            switch (Num) {
                case 1: return X;
                case 2: return Y;
                case 3: return Z;
                default: throw new Exception("We shouldn't reach here : " + Num);
            }
        }
        public void SetElement(int Num, short value) {
            switch (Num) {
                case 1: X = value; break;
                case 2: Y = value; break;
                case 3: Z = value; break;
                default: throw new Exception("We shouldn't reach here : " + Num);
            }
        }
    }
}
