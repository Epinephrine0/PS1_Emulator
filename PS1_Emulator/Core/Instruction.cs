using System;

namespace PSXEmulator {
    public class Instruction {
        UInt32 fullValue;

        public Instruction(UInt32 value) { 
        this.fullValue = value;   
        
        }
        public UInt32 getfull() {                        //Bits  [31:0]

            return this.fullValue;

        }
        public UInt32 getOpcode() {                        //Bits  [31:26]

            return fullValue >> 26;

        }

        public UInt32 get_rt() {                    //Bits  [20:16]

            return (fullValue >> 16) & 0x1F;        //Can also AND it with 31 (decimal)

        }

        public UInt32 getImmediateValue() {            //Bits  [15:0]

            return (fullValue & 0xFFFF);       

        }
        public UInt32 signed_imm() {            //Bits  [15:0]

            Int16 num_s = ((Int16)(fullValue & 0xFFFF));

            return (UInt32) num_s;          //Sign extended 


        }

        public UInt32 get_rs() {              //Bits  [25:21]

            return (fullValue >> 21) & 0x1F;

        }

        public UInt32 get_rd() {              //Bits  [15:11]

            return (fullValue >> 11) & 0x1F;

        }
        public UInt32 get_sa() {              //Bits  [10:6]

            return (fullValue >> 6) & 0x1F;

        }
        public UInt32 get_subfunction() {              //Bits  [5:0]

            return fullValue & 0x3f;

        }
        public UInt32 imm_jump() {              //Bits  [26:0]

            return fullValue & 0x3ffffff;     

        }
    }
}
