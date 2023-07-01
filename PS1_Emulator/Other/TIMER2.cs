using System;

namespace PSXEmulator {
    public class TIMER2 {
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


        public void write(uint address, uint value) {
            uint offset = address - range.start;

            switch (offset) {

                case 0: currentValue = value; break;
                case 4:
                    mode = value;
                    mode |= 1 << 10;     //Set bit 10
                    synchronization = (mode & 1) != 0;
                    counter = 0;
                    //Clock source
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

                case 8: target = value; break;

                default: throw new Exception("Unknown TIMER2 offset: " + offset);

            }

         

        }
        public uint read(uint address) {
            uint offset = address - range.start;

            switch (offset) {
                
                case 0:  return currentValue;        //0x000016b0 random value that works for entering shell

                case 4:                        
                    uint temp = mode;
                    mode &= 0b0000_0111_1111_1111;    //Reset bits 11-12 (above 12 are garbage)
                    return temp;
           
                case 8: return target;
                

                default: throw new Exception("Unknown TIMER2 offset: " + offset);

            }

            


        }

        int counter;
        bool reset = false;
        public void tick(int cycles) {
            if(reset) { currentValue = 0; counter = 0; reset = false; }
            if (!pause) {
                switch (clockSource) {
                    case ClockSource.SystemClock:

                        currentValue += (uint)cycles;

                        break;

                    case ClockSource.SystemClockOver8:

                        counter += cycles;
                        if (counter >= 8) {
                            counter -= 8;
                            currentValue++;
                        }

                        break;
                }
            }

            //Pause?
            if (synchronization) {
                switch ((mode >> 1) & 3) {
                    case 0:
                    case 3:
                         pause = true;
                        break;

                    default:
                        pause = false;
                        break;
                }
            }
            else {
                pause = false;
            }

            uint flag = 0;
            bool IRQenabled = false;

            //Reached target?
            if (((mode >> 3) & 1) == 1) {       //(0=After Counter=FFFFh, 1=After Counter=Target)
                reachedTarget = currentValue >= target;

                if (((mode >> 4) & 1) == 1) {
                    IRQenabled = true;
                }
                flag = 1 << 11;

            }
            else {
                reachedTarget = currentValue >= 0xffff;
                if (((mode >> 5) & 1) == 1) {
                    IRQenabled = true;
                }
                flag = 1 << 12;

            }

            //IRQ?
            if (reachedTarget) {
                mode |= flag;
                if (IRQenabled) {
                    IRQ();
                }
                reset = true;

            }

        }

        public void IRQ() {
            mode = (uint)(mode & (~(1 << 10))); //IRQ request (bit 10 = 0)
            IRQ_CONTROL.IRQsignal(6);

        }



    }
}
