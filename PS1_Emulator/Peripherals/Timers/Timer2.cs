namespace PSXEmulator.Peripherals.Timers {
    public class Timer2 : Timer {
        int delay;

        public Timer2() {
            Range = new Range(0x1F801120, 12);
        }

        protected override void Tick(int cycles) {
            if (IsPaused) { return; }

            if (Synchronize) {
                switch (SyncMode) {
                    case 0:
                    case 3:
                        IsPaused = true;
                        return;

                    case 1:
                    case 2:
                        Synchronize = false;
                        break;
                }
            }

            if (CurrentValue >= Target) {
                CounterReachedTarget = true;
                if (IRQWhenReachedTarget) {
                    IRQRequest = true;
                    IRQ_CONTROL.IRQsignal(6);
                }
                if (ResetWhenReachedTarget) {   //Shouldn't be instant
                    Reset();
                }
            } else if (CurrentValue >= 0xFFFF) {
                CounterOverflowed = true;
                Reset();
                if (IRQWhenOverflow) {
                    IRQRequest = true;
                    IRQ_CONTROL.IRQsignal(6);
                }
            }

            CurrentValue += cycles;
        }

        public override void SystemClockTick(int cycles) {
            switch (ClockSource) {
                case 0:
                case 1:
                    Tick(cycles);
                    break;

                case 2:
                case 3:
                    delay += cycles;
                    if (delay > 8) {
                        delay -= 8;
                        Tick(1);
                    }
                    break;
            }
        }
    }
}
