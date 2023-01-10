using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace PS1_Emulator {
    internal class IRQ_CONTROL {
        public static Range range = new Range(0x1f801070, 8);

         static UInt32 I_STAT = 0;  //IRQ Status 
         static UInt32 I_MASK = 0;  //IRQ Mask 

        public static ushort read16(uint offset) {

            switch (offset) { 
                case 0:
                    //ebug.WriteLine("IRQ read STAT: " + Convert.ToString(I_STAT, 2).PadLeft(16, '0'));
                    return (ushort)(I_STAT);       //& mask?

                case 4:

                    //Debug.WriteLine("IRQ read MASK: " + Convert.ToString(I_MASK, 2).PadLeft(16, '0'));
                    return (ushort)(I_MASK);  

                default:

                    throw new Exception("unhandled IRQ read at offset " + offset);
            
            }




        }
        public static uint read32(uint offset) {

            switch (offset) {
                case 0:
                    return (I_STAT);       //& mask?

                case 4:

                    return (I_MASK);

                default:

                    throw new Exception("unhandled IRQ read at offset " + offset);

            }




        }

        public static void write(uint offset, ushort value) {
          

            switch (offset) {
                case 0:
                    //if ((I_STAT & 1) == 1 && ((value & 1) == 0)) { Debug.WriteLine("Vblank acknowledge "); }

                    I_STAT = (I_STAT & value);
                    //Debug.WriteLine("IRQ write STAT: " + Convert.ToString(value, 2).PadLeft(16, '0'));
            
                    break;

                case 4:
                    I_MASK = value;
                    //Debug.WriteLine("IRQ write MASK: " + Convert.ToString(I_MASK, 2).PadLeft(16, '0'));

                    break;

                default:

                    throw new Exception("unhandled IRQ write at offset " + offset);

            }

           

        }

        public static void IRQsignal(int bitNumber) {

           
            I_STAT = (I_STAT | (ushort)(1 << bitNumber));
           // if ((I_STAT & I_MASK) != 0) {
           //     CPU.RequestIRQ();
           // }
        }

        public static bool isRequestingIRQ() {

            return (I_STAT & I_MASK) != 0;  
        }


        public static int readIRQbit(int bitNumber) {


            return (int)(I_STAT >> bitNumber) & 1; 

        }


    }
}
