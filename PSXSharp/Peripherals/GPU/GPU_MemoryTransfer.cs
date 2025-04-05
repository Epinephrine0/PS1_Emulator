using System;

namespace PSXSharp.Peripherals.GPU {
    public class GPU_MemoryTransfer {
        public uint[] Parameters;
        public ushort[] Data;
        public int DataPtr;
        public int ParamPtr;     
        public uint Type;
        public uint Width;
        public uint Height;


        public bool ParametersReady => ParamPtr == Parameters.Length;
        public bool DataEnd => DataPtr == Data.Length;

        public bool ReadyToExecute;

        public GPU_MemoryTransfer(int numberOfParameters, uint type) {
            Parameters = new uint[numberOfParameters];
            Type = type;
        }

        public void Add(uint value) {
            if (!ParametersReady) {
                Parameters[ParamPtr++] = value;
                if (ParametersReady) {
                    CalculateDimensions();
                    uint size = Width * Height;
                    if (Type == 0x5) {
                        Data = new ushort[size + (size & 1)];       //Size must be even!
                    } else if (Type == 0x6) {
                        Data = new ushort[size + (size & 1)];       //Size must be even!
                        ReadyToExecute = true;
                    } else {
                        ReadyToExecute = true;
                    }
                }
            } else {
                Data[DataPtr++] = (ushort)value;
                Data[DataPtr++] = ((ushort)(value >> 16));
                if (DataPtr >= Data.Length) {
                    ReadyToExecute = true;
                    DataPtr = 0;
                }
            }
        }

        public void CalculateDimensions() {
            switch (Type) {
                case 0x2:
                    //Fill does NOT occur when Xsiz=0 or Ysiz=0...
                    if ((Parameters[2] & 0xFFFF) == 0x400) { Width = 0; Height = 0; break; }

                    if ((Parameters[2] & 0xFFFF) >= 0x3F1) {
                        Width = 0x400;
                    } else {
                        Width = (uint)((((Parameters[2] & 0xFFFF) & 0x3FF) + 0x0F) & (~0x0F));       //;range 0..400h, in steps of 10h
                    }
                    Height = ((Parameters[2] >> 16) & 0xFFFF) & 0x1FF;                              //;range 0..1FFh
                    break;

                default:
                    int location;
                    if (Type == 0x4) {
                        location = 3;
                    }else {
                        location = 2;
                    }

                    if ((Parameters[location] & 0xFFFF) == 0) { 
                        Width = 0x400;
                    } else {
                        Width = (((Parameters[location] & 0xFFFF) - 1) & 0x3FF) + 1;                //;range 1..400h
                    }

                    if (((Parameters[location] >> 16) & 0xFFFF) == 0) { 
                        Height = 0x200;
                    } else {
                        Height = ((((Parameters[location] >> 16) & 0xFFFF) - 1) & 0x1FF) + 1;       //;range 1..200h
                    }
                    break;
            }
        }

        public ushort[] GetDataArray() {
            return Data;
        }

        public uint ReadWord() {
            if (DataPtr + 1 >= Data.Length) {
                throw new Exception("This shouldn't happen");
            }
            ushort half0 = Data[DataPtr++];
            ushort half1 = Data[DataPtr++];
            return (uint)(half0 | (half1 << 16));
        }

    }
}
