using System;
using static PSXEmulator.CDROMDataController;

namespace PSXEmulator {
    namespace PS1_Emulator {
        public class TIMER1 {
            public Range range = new Range(0x1F801110, 0xF + 1);        //Assumption 
           
            uint mode;
            uint currentValue;
            uint target;
            bool synchronization;
            public enum ClockSource {
                   SystemClock,
                   Hblank   //GPU
            }
            public ClockSource clockSource = ClockSource.SystemClock;

            public bool GPUinVblank = false;
            public bool GPUGotVblankOnce = false;

            public void Write(uint address, uint value) {
                uint offset = address - range.start; 

                switch (offset) {

                    case 0: currentValue = value; break;

                    case 4:
                        currentValue = 0;   
                        mode = value;
                        synchronization = (mode & 1) != 0;
                        mode |= 1 << 10;     //Set bit 10
                        switch ((mode >> 8) & 3) {
                            case 0:
                            case 2:
                                clockSource = ClockSource.SystemClock;
                                break;
                            case 1:
                            case 3:
                                clockSource = ClockSource.Hblank;
                                break;
                        }
                        break;

                    case 8: /*Console.WriteLine("[TIMER1] Write Target: " + value.ToString("x"));*/ target = value; break;

                    default: throw new Exception("Unknown TIMER1 offset: " + offset);

                }
            }
            public uint Read(uint address) {
                uint offset = address - range.start;

                switch (offset) {

                    case 0:
                        //Console.WriteLine("[TIMER1] Reading current value: " + currentValue.ToString("x"));
                        return currentValue;        

                    case 4:
                        uint temp = mode;
                        mode &= 0b0000_0111_1111_1111;    //Reset bits 11-12 (above 12 are garbage)
                        //Console.WriteLine("[TIMER1] Reading current Mode: " + temp.ToString("x"));

                        return temp;

                    case 8: return target;


                    default: throw new Exception("Unknown TIMER1 offset: " + offset);

                }

            }
            bool pause;
            bool reachedTarget;
            private bool reset;

            public void tick(int cycles) {
                if (reset) { currentValue = 0; reset = false; }
                if (!pause) { currentValue += (uint)cycles;}
                
                if (synchronization) {
                    switch ((mode >> 1) & 3) {
                        case 0:
                            if (GPUinVblank) { 
                                pause = true; 
                            } 
                            else {
                                pause = false;
                            }
                            break;

                        case 1:
                            if (GPUinVblank) {
                                currentValue = 0;
                            }
                            break;

                        case 2:
                            if (GPUinVblank) {
                                currentValue = 0;
                            }
                            else {
                                pause = true;
                            }
                            break;

                        case 3:
                            pause = true;
                            if (GPUGotVblankOnce) {
                                mode = (uint)(mode & ~1);
                                pause = false;
                            }
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
                    reset = true;   //On next cycle

                }

            }

            public void IRQ() {
                mode = (uint)(mode & (~(1 << 10))); //IRQ request (bit 10 = 0)
                IRQ_CONTROL.IRQsignal(5);

            }

            internal bool isUsingSystemClock() {
                return clockSource == ClockSource.SystemClock;
            }
        }
    }

}
