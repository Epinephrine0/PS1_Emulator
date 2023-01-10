using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PS1_Emulator {
    public class Instruction {
        UInt32 op;

        public Instruction(UInt32 value) { 
        this.op = value;   
        
        }
        public UInt32 getfull() {                        //Bits  [31:0]

            return this.op;

        }
        public UInt32 getType() {                        //Bits  [31:26]

            return op >> 26;

        }

        public UInt32 get_rt() {                    //Bits  [20:16]

            return (op >> 16) & 0x1F;        //Can also AND it with 31 (decimal)

        }

        public UInt32 getImmediateValue() {            //Bits  [15:0]

            return (op & 0xFFFF);       

        }
        public UInt32 signed_imm() {            //Bits  [15:0]

            Int16 num_s = ((Int16)(op & 0xFFFF));

            return (UInt32) num_s;          //Sign extended 


        }

        public UInt32 get_rs() {              //Bits  [25:21]

            return (op >> 21) & 0x1F;

        }

        public UInt32 get_rd() {              //Bits  [15:11]

            return (op >> 11) & 0x1F;

        }
        public UInt32 get_sa() {              //Bits  [10:6]

            return (op >> 6) & 0x1F;

        }
        public UInt32 get_subfunction() {              //Bits  [5:0]

            return op & 0x3f;

        }
        public UInt32 imm_jump() {              //Bits  [26:0]

            return op & 0x3ffffff;     

        }
    }
}
