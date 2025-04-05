using System;

namespace PSXSharp.Peripherals.Timers {
    public class Timer0 : Timer {
        bool GotHblank = false;
        public Timer0() {
            Range = new Range(0x1F801100, 12);
        }
        protected override void Tick(int cycles) {
            if (IsPaused) { return; }
            if (CurrentValue >= Target) {
                CounterReachedTarget = true;
                if (IRQWhenReachedTarget) {
                    IRQRequest = true;
                    IRQ_CONTROL.IRQsignal(4);
                }
                if (ResetWhenReachedTarget) {   //Shouldn't be instant
                    Reset();
                }
            } else if (CurrentValue >= 0xFFFF) {
                CounterOverflowed = true;
                Reset();
                if (IRQWhenOverflow) {
                    IRQRequest = true;
                    IRQ_CONTROL.IRQsignal(4);
                }
            }

            CurrentValue += cycles;
        }

        public override void SystemClockTick(int cycles) {
            if (ClockSource == 0 || ClockSource == 2) {
                Tick(1);
            }
        }

        public void DotClock() {
            if (ClockSource == 1 || ClockSource == 3) {
                Tick(1);
            }
        }

        public void HblankTick() {
            if (Synchronize) {
                switch (SyncMode) {
                    case 0: Console.WriteLine("[Timer0] Unimplemented Pause During Hblank"); break;
                    case 1:
                    case 2: Reset(); break;
                    case 3: GotHblank = true; break;
                }
            }
        }
        public void HblankOut() {
            if (Synchronize) {
                if (SyncMode == 3) {
                    if (GotHblank) {
                        IsPaused = false;
                        Synchronize = false;
                    } else {
                        IsPaused = true;
                    }
                }
                GotHblank = false;
            }
        }
    }
}