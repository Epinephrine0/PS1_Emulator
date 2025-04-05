using System;

namespace PSXSharp.Core.Common {
    public class Instruction {  //This should probably be a struct instead of a class
        public uint FullValue;                                                       //Bits  [31:0]
        public uint GetOpcode() => FullValue >> 26;                                  //Bits  [31:26]
        public uint Get_rt() => FullValue >> 16 & 0x1F;                              //Bits  [20:16]
        public uint GetImmediate() => FullValue & 0xFFFF;                            //Bits  [15:0]
        public uint GetSignedImmediate() {                                           //Bits  [15:0] but sign extended to 32-bits
           short num_s = (short)(FullValue & 0xFFFF);
           return (uint) num_s;                      
        }
        public uint Get_rs() => FullValue >> 21 & 0x1F;                              //Bits  [25:21]

        public uint Get_rd() => FullValue >> 11 & 0x1F;                              //Bits  [15:11]

        public uint Get_sa() => FullValue >> 6 & 0x1F;                               //Bits  [10:6]

        public uint Get_Subfunction() => FullValue & 0x3f;                           //Bits  [5:0]

        public uint GetImmediateJumpAddress() => FullValue & 0x3ffffff;              //Bits  [26:0]
    }
}
