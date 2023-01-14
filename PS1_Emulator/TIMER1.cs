using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PS1_Emulator {
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Threading.Tasks;

    namespace PS1_Emulator {
        internal class TIMER1 {
            public Range range = new Range(0x1F801110, 0xF + 1);        //Assumption 
           
            uint mode;
            uint currentValue;
            uint target;
            uint step = 1;
            uint stopValue = (uint)0xffff;
            bool oneShot = false;
            bool pulse = false;
            bool finished = false;

            public bool GPUinVblank = false;
             
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
                        throw new Exception("Unknown TIMER1 offset: " + offset);

                }



            }
            public uint get(uint offset) {

                switch (offset) {

                    case 0:
                        //Console.WriteLine("Reading Timer1 current value: "+currentValue.ToString("x"));
                        return currentValue;        

                    case 4:
                        //Console.WriteLine("Reading Timer1 mode value: " + currentValue.ToString("x"));

                        uint temp = mode;
                        mode = (uint)(mode & ~(3 << 11));    //Reset bits 11-12
                        return temp;

                    case 8:
                        //Console.WriteLine("Reading Timer1 target value: " + currentValue.ToString("x"));

                        return target;


                    default:
                        throw new Exception("Unknown TIMER1 offset: " + offset);

                }




            }
            public void tick() {
                if (!pulse && finished) {
                    mode ^= (1 << 10);

                }

                if (!oneShot && finished) {
                    IRQ_CONTROL.IRQsignal(5);

                }


                currentValue+=step;

                if (currentValue >= target && target > 0) {
                    //Debug.WriteLine("Timer1 reached target: " + currentValue.ToString("x"));
                    IRQ();
                }

                if ((mode & 1) == 0) {
                    return;
                }

                switch ((mode >> 1) & 3) {
                    case 0:
                        if (IRQ_CONTROL.readIRQbit(0) == 1) { 
                            step = 0;
                        }
                        else {
                            step = 1;
                        }

                        break;

                    case 1:
                        if (IRQ_CONTROL.readIRQbit(0) == 1) {
                            currentValue = 0x0000;
                            step= 1;
                        }
                        break;

                    case 2:
                        if (IRQ_CONTROL.readIRQbit(0) == 1) {
                            currentValue = 0x0000;
                            step = 1;
                        }
                        else {
                            step= 0;
                        }
                        break;

                    case 3:
                      
                          
                        step= 0;
                        Debug.WriteLine("Paused Timer1, waiting for a vblank");
                        if (GPUinVblank) {
                            Debug.WriteLine("Switching to free run");
                            mode = (uint)(mode & (~1));
                            step = 1;
                            GPUinVblank = false;
                        }
                        break;

                }


            }

            public void IRQ() {
                if (finished && oneShot) { return; }

                switch (currentValue) {
                    case 0xffff:
                        mode |= (1 << 12);      //Reached 0xffff

                        break;

                    default:
                        mode |= (1 << 11);      //Reached target
                        break;
                }

                mode = (uint)(mode & ~(1 << 10));  //IRQ request (bit 10 = 0)

                if (((mode >> 4) & 1) != 0) {
                    IRQ_CONTROL.IRQsignal(5);

                }
                else if (((mode >> 5) & 1) != 0 && currentValue == 0xffff) {
                    IRQ_CONTROL.IRQsignal(5);
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


                switch ((mode >> 3) & 1) {
                    case 0:
                        stopValue = 0xffff;
                        break;
                    case 1:
                        stopValue = target;
                        break;

                }              



            }

            internal bool isUsingHblank() {

                return ((mode  >> 8) & 3) == 1 || ((mode >> 8) & 3) == 3;
            }
        }
    }

}
