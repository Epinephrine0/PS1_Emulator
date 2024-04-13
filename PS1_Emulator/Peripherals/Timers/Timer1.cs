using System;

namespace PSXEmulator.Peripherals.Timers {
    public class Timer1 : Timer {
        bool GotVblank;
        public Timer1() {
            Range = new Range(0x1F801110, 12);
        }

        protected override void Tick(int cycles) {
            if (IsPaused) { return; }
            if (CurrentValue >= Target) {
                CounterReachedTarget = true;
                if (IRQWhenReachedTarget) {
                    IRQRequest = true;
                    IRQ_CONTROL.IRQsignal(5);
                }
                if (ResetWhenReachedTarget) {   //Shouldn't be instant
                    Reset();
                }
            } else if (CurrentValue >= 0xFFFF) {
                CounterOverflowed = true;
                Reset();
                if (IRQWhenOverflow) {
                    IRQRequest = true;
                    IRQ_CONTROL.IRQsignal(5);
                }
            }

            CurrentValue += cycles;
        }
        public override void SystemClockTick(int cycles) {
            if (ClockSource == 0 || ClockSource == 2) {
                Tick(cycles);
            }
        }
        public void HblankTick() {
            if (ClockSource == 1 || ClockSource == 3) {
                Tick(1);
            }
        }
        public void VblankTick() {
            if (Synchronize) {
                switch (SyncMode) {
                    case 0: Console.WriteLine("[Timer1] Unimplemented Pause During Vblank"); break;
                    case 1:
                    case 2: Reset(); break;
                    case 3: GotVblank = true; break;
                }
            }
        }
        public void VblankOut() {
            if (Synchronize) {
                if (SyncMode == 3) {
                    if (GotVblank) {
                        IsPaused = false;
                        Synchronize = false;
                    } else {
                        IsPaused = true;
                    }
                }
                GotVblank = false;
            }
        }
    }
}
