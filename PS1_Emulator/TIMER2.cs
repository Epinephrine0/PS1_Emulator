using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace PS1_Emulator {
    internal class TIMER2 {
        public Range range = new Range(0x1F801120, 0xF + 1);        //Assumption 
        uint mode;
        uint currentValue;
        uint target = 0xFFFF;
        uint step;
        bool oneShot = false;
        bool pulse = false;
        bool reachedTarget = false;
        bool synchronization;
        bool pause;
        ClockSource clockSource = ClockSource.SystemClock;
        enum ClockSource {
            SystemClock,
            SystemClockOver8
        }

        /* 
          TODO handle bits 6/7
          6 IRQ Once/Repeat Mode    (0=One-shot, 1=Repeatedly)
          7 IRQ Pulse/Toggle Mode   (0=Short Bit10=0 Pulse, 1=Toggle Bit10 on/off)
        */


        public void set(uint offset, uint value) {
          

            switch (offset) {

                case 0:
                    currentValue = value;
                    break;

                case 4:
                    mode = value;
                    synchronization = (mode & 1) != 0;
                    pause = false;
                    counter = 0;
                    switch ((mode >> 8) & 3) {
                        case 0:
                        case 1:
                            clockSource = ClockSource.SystemClock;
                            break;
                        case 2:
                        case 3:
                            clockSource = ClockSource.SystemClockOver8;
                            break;

                    }

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

        int counter;

        public void tick(int cycles) {
            if(pause) { return; }

            switch (clockSource) {
                case ClockSource.SystemClock:

                    currentValue += (uint)cycles;

                    break;

                case ClockSource.SystemClockOver8:

                    counter += cycles;
                    if(counter >= 8) {
                        counter -= 8;
                        currentValue++;
                    }

                    break;
            }

            //Pause?
            if (synchronization) {
                switch ((mode >> 1) & 3) {
                    case 0:
                    case 3:
                         pause= true;
                        break;

                }
            }

            //Reached target?
            if (((mode >> 3) & 1) == 1) {       //(0=After Counter=FFFFh, 1=After Counter=Target)
                reachedTarget = currentValue >= target;
            }
            else {
                reachedTarget = currentValue >= 0xffff;
            }

            //IRQ?
            if (reachedTarget) {
                if (currentValue >= target && ((mode >> 4) & 1) == 1) {
                    IRQ();
                    mode |= (1 << 11);

                }
                if (currentValue >= 0xffff && ((mode >> 5) & 1) == 1) {

                    IRQ();
                    mode |= (1 << 12);
                }
                currentValue = 0;
            }

        }

        public void IRQ() {
            mode = (uint)(mode & (~(1 << 10))); //IRQ request (bit 10 = 0)
            IRQ_CONTROL.IRQsignal(6);

        }



    }
}
