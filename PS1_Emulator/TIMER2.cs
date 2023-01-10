using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace PS1_Emulator {
    internal class TIMER2 {
        public Range range = new Range(0x1F801120, 0xF + 1);        //Assumption 
        uint mode;
        uint currentValue;
        uint target;
        uint step = 0;
        uint stopValue = (uint)0xffff;
        bool oneShot = false;
        bool pulse = false;
        bool finished = false;
        public void set(uint offset, uint value) {
          

            switch (offset) {

                case 0:
                    currentValue = value;
                    break;

                case 4:
                    mode = value;
                    timerSettings(mode);
                    break;

                case 8:
                    target = value;
                    break;

                default:
                    throw new Exception("Unknown TIMER2 offset: " + offset);

            }

         

        }
        public uint get(uint offset) {
          
            switch (offset) {
                
                case 0:

                    return currentValue;        //0x000016b0 random value that works for entering shell

                case 4:                        

                    uint temp = mode;
                    mode &= 0b0011111111111;    //Reset bits 11-12
                    return mode;
           
                case 8:

                    return target;
                

                default:
                    throw new Exception("Unknown TIMER2 offset: " + offset);

            }

            


        }
        public void tick() {
            if (!pulse && finished) {
                mode ^= (1 << 10);

            }

            if (!oneShot && finished) {
                IRQ_CONTROL.IRQsignal(4);

            }

            currentValue +=step;

            if (currentValue == target) {
                IRQ();
            }
           
            
        }

        public void IRQ() {
            if(finished && oneShot) { return; }

           
                if (((mode >> 4) & 1) != 0) {
                    IRQ_CONTROL.IRQsignal(4);
                    mode |= (1 << 11);      //Reached target
                    mode &= 0b1101111111111;  //IRQ request (bit 10 = 0)

                }
                else if (((mode >> 5) & 1) != 0 && currentValue == 0xffff) {
                    IRQ_CONTROL.IRQsignal(4);
                    mode |= (1 << 12);      //Reached 0xffff
                    mode &= 0b1101111111111;  //IRQ request (bit 10 = 0)
                }


            if (((mode >> 6) & 1) != 0) {
                oneShot = false;

            }
            else {
                oneShot = true;
            }

            if (((mode >> 7) & 1) != 0) {
                pulse = false;

            }
            else {
                pulse = true;   
            }

            currentValue = 0;
            finished = true;
        }


        public void timerSettings(uint mode) {  //Mising many things
            currentValue = 0;
            oneShot = false;
            pulse = false;
            finished = false;

            if ((mode & 1) != 0) {
               
                switch ((mode>>1) & 3) {
                    
                    case 0:
                    case 3:
                        step = 0;
                        return;

                    case 1:
                    case 2:
                        stopValue = (uint)0xffff;
                       
                        switch ((mode >> 8) & 3) {
                            case 0:
                            case 1:
                                step = 1;
                                break;

                            case 2:
                            case 3:
                                step = 8;
                                break;


                        }
                        return;

                }
                

            }
            else {



                switch ((mode >> 3) & 1) {
                    case 0:
                        stopValue = 0xffff;
                        break;
                    case 1:
                        stopValue = target;
                        break;



                }

                switch ((mode >> 8) & 3) {
                    case 0:
                    case 1:
                        step = 1;
                        break;

                    case 2:
                    case 3:
                        step = 8;
                        break;


                }


            }

          


        }


    }
}
