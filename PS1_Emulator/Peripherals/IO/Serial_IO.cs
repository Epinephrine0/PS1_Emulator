namespace PSXEmulator.Peripherals.IO {
    public abstract class Serial_IO {
        //Data (R/W) -> 1/4 word    (This is actually a FIFO)
        protected byte Data;

        //Status (R) -> 4           This register comes after 4 bytes of Data address, although the Data length is only 1 byte
        protected struct StatusRegister {
           public byte TX_FIFO_Not_Full;    //0
           public byte RX_FIFO_Not_Empty;   //1
           public byte TX_Idle;             //2
           public byte RX_Parity_Error;     //3
           public byte RX_FIFO_Overrun;     //4     SIO1
           public byte RX_BadStopBit;       //5     SIO1
           public byte RX_InputLevel;       //6     SIO1
           public byte DSR_InputLevel;      //7
           public byte CST_InputLevel;      //8
           public byte InterruptRequest;    //9
           //Unknown - Always 0             //10
           public int Baud;                 //11 - 31
        }

        protected StatusRegister Status;


        //Mode (R/W) -> 2
        protected struct ModeRegister {
            public byte BaudrateReloadFactor;//0 - 1
            public byte CharacterLength;     //2 - 3
            public byte ParityEnable;        //4
            public byte ParityType;          //5
            public byte StopBitLength;       //6 - 7     SIO1
            public byte ClockPolarity;       //8         SIO0
            //Not used                       //9 - 15
        }

        protected ModeRegister Mode;


        //Control (R/W) -> 2
        protected struct CtrlRegister {
            public byte TX_Enable;            //0
            public byte DTR_OutputLevel;      //1
            public byte RX_Enable;            //2
            public byte TX_OutputLevel;       //3     SIO1
            public byte Acknowledge;          //4     
            public byte RTS_OutputLevel;      //5     SIO1
            public byte Reset;                //6     
           //SIO1 Unknown                     //7
            public byte RX_InterruptMode;     //8 - 9
            public byte TX_InterruptEnable;   //10
            public byte RX_InterruptEnable;   //11
            public byte DSR_InterruptEnable;  //12
            public byte PortSelect;           //13    SIO0
            //Not used                        //14 - 15
        }

        protected CtrlRegister Ctrl;              

        public abstract void StoreWord(uint address, uint data);
        public abstract uint LoadWord(uint address);

        public abstract void StoreHalf(uint address, ushort data);
        public abstract ushort LoadHalf(uint address);

        public abstract void StoreByte(uint address, byte data);
        public abstract byte LoadByte(uint address);

        protected abstract void Tick(int cycles);

    }
}
