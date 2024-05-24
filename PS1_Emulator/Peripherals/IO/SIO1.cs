using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using static PSXEmulator.CDROMDataController;

namespace PSXEmulator.Peripherals.IO {
    public class SIO1 {               //Generic Serial interface

        public Range Range = new Range(0x1F801050, 16);

        //Data (R/W) -> 1/4 word    (This is actually a FIFO)
        public byte TX_Data;
        public byte RX_Data;

        //Status (R) -> 4           This register comes after 4 bytes of Data address, although the Data length is only 1 byte
         struct StatusRegister {
            public byte TX_FIFO_Not_Full;    //0
            public byte RX_FIFO_Not_Empty;   //1
            public byte TX_Idle;             //2
            public byte RX_Parity_Error;     //3
            public byte RX_FIFO_Overrun;     //4     SIO1
            public byte RX_BadStopBit;       //5     SIO1
            public byte RX_InputLevel;       //6     SIO1
            public byte DSR_InputLevel;      //7
            public byte CTS_InputLevel;      //8
            public byte InterruptRequest;    //9
            //Unknown - Always 0             //10
            public int Baud;                 //11 - 31
        }

         StatusRegister Status;

        //Mode (R/W) -> 2
         struct ModeRegister {
            public byte BaudrateReloadFactor;//0 - 1
            public byte CharacterLength;     //2 - 3
            public byte ParityEnable;        //4
            public byte ParityType;          //5
            public byte StopBitLength;       //6 - 7     SIO1
            public byte ClockPolarity;       //8         SIO0
            //Not used                       //9 - 15
        }

         ModeRegister Mode;


        //Control (R/W) -> 2
         struct CtrlRegister {
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

        //A command is always of 2 bytes
       public struct Command {
            public byte address;
            public byte value;
        }


        CtrlRegister Ctrl;
        Socket Socket;
        public SIO1() {
            //Setup TCP Connection
            Socket = new Server();
            Socket.AcceptClientConnection();      //Wait for client to connect
            Console.WriteLine("TCP Client connected");

            //In theory, I only need to change the above 2 lines in the client version
        }

        public uint LoadWord(uint address) {
            switch (address & 0xF) {
                case 0x0: return (uint)(RX_Data & 0xFF);
                case 0x4: return GetStatus();
                default: throw new Exception("Attempting to load word from: " + address.ToString("x"));
            }
        }
        public ushort LoadHalf(uint address) {
            //case 0x4?
            switch (address & 0xF) {
                case 0x0: return (ushort)(RX_Data & 0xFF);
                case 0x4: return (ushort)(GetStatus() & 0xFFFF);
                case 0x8: return GetMode();
                case 0xA: return GetCtrl();
                case 0xE: return (ushort)(Status.Baud & 0xFFFF);
                default: throw new Exception("Attempting to load word from: " + address.ToString("x"));
            }
        }
        public void StoreHalf(uint address, ushort data) {
            switch (address & 0xF) {
                case 0x0: Transfare((byte)data); break;
                case 0x8: WriteMode(data); break;
                case 0xA: WriteCtrl(data); break;
                case 0xE: Status.Baud = data; break;
                default: throw new Exception("Attempting to load word from: " + address.ToString("x"));
            }
        }
        public byte LoadByte(uint address) {
            switch (address & 0xF) {
                case 0x00: Console.WriteLine("Reading RX"); return 0xFF;
                case 0x04: return (byte)GetStatus();
                default: throw new Exception("[SIO1] Unhandled reading from: " + address.ToString("x"));
            }
        }
        public void StoreByte(uint address, byte data) {
            StoreHalf(address, data); //Could be very wrong
        }
        private void Transfare(byte data) {
            //Handle data sending
            Console.WriteLine("Sending TX" + data.ToString("x"));

            RX_Data = 0xFF;
            TX_Data = data;
            //Status.RX_FIFO_Not_Empty = 1;
            if (Status.CTS_InputLevel == 1) {           //CST is set by the other side 
                byte address = 0x00;
                Socket.Send(new byte[] { address, data });     //Send it to the other side
                Console.WriteLine("sent addr: " + address.ToString("x") + " val: " + data.ToString("x"));

            }

        }

        private uint GetStatus() {
            uint stat = 0;
            stat |= (byte)(Status.TX_FIFO_Not_Full & 1);
            stat |= (byte)((Status.RX_FIFO_Not_Empty & 1) << 1);
            stat |= (byte)((Status.TX_Idle & 1) << 2);
            stat |= (byte)((Status.RX_Parity_Error & 1) << 3);
            stat |= (byte)((Status.RX_FIFO_Overrun & 1) << 4);
            stat |= (byte)((Status.RX_BadStopBit & 1) << 5);
            stat |= (byte)((Status.RX_InputLevel & 1) << 6);
            stat |= (byte)((Status.DSR_InputLevel & 1) << 7);
            stat |= (byte)((Status.CTS_InputLevel & 1) << 8);
            stat |= (byte)((Status.InterruptRequest & 1) << 9);
            stat |= (ushort)((Status.Baud & 0xFFFF) << 11);
            return stat;
        }

        private ushort GetMode() {
            ushort mode = 0;
            mode |= (byte)(Mode.BaudrateReloadFactor & 3);
            mode |= (byte)((Mode.CharacterLength & 3) << 2);
            mode |= (byte)((Mode.ParityEnable & 1) << 4);
            mode |= (byte)((Mode.ParityType & 1) << 5);
            mode |= (byte)((Mode.StopBitLength & 3) << 6);
            mode |= (byte)((Mode.ClockPolarity & 1) << 8);
            return mode;
        }

        private void WriteMode(ushort value) {
            Mode.BaudrateReloadFactor = (byte)(value & 3);
            Mode.CharacterLength = (byte)((value >> 2) & 3);
            Mode.ParityEnable = (byte)((value >> 4) & 1);
            Mode.ParityType = (byte)((value >> 5) & 1);
            Mode.StopBitLength = (byte)((value >> 6) & 3);
            Mode.ClockPolarity = (byte)((value >> 8) & 1);
        }
        private ushort GetCtrl() {
            ushort control = 0;
            control |= (byte)(Ctrl.TX_Enable & 1);
            control |= (byte)((Ctrl.DTR_OutputLevel & 1) << 1);
            control |= (byte)((Ctrl.RX_Enable & 1) << 2);
            control |= (byte)((Ctrl.TX_OutputLevel & 1) << 3);
            //4 is write only
            control |= (byte)((Ctrl.RTS_OutputLevel & 1) << 5);
            //6 is write only
            //7 is unknown
            control |= (byte)((Ctrl.RX_InterruptMode & 3) << 8);
            control |= (byte)((Ctrl.TX_InterruptEnable & 1) << 10);
            control |= (byte)((Ctrl.RX_InterruptEnable & 1) << 11);
            control |= (byte)((Ctrl.DSR_InterruptEnable & 1) << 12);
            control |= (byte)((Ctrl.PortSelect & 1) << 13);
            return control;
        }

        private void WriteCtrl(ushort control) {
            Ctrl.TX_Enable = (byte)(control & 1);
            Ctrl.DTR_OutputLevel = (byte)((control >> 1) & 1);
            Ctrl.RX_Enable = (byte)((control >> 2) & 1);
            Ctrl.TX_OutputLevel = (byte)((control >> 3) & 1);
            Ctrl.Acknowledge = (byte)((control >> 4) & 1);       //Write only
            Ctrl.RTS_OutputLevel = (byte)((control >> 5) & 1);

            Ctrl.Reset = (byte)((control >> 6) & 1);             //Write only
            Ctrl.RX_InterruptMode = (byte)((control >> 8) & 3);
            Ctrl.TX_InterruptEnable = (byte)((control >> 10) & 1);
            Ctrl.RX_InterruptEnable = (byte)((control >> 11) & 1);
            Ctrl.DSR_InterruptEnable = (byte)((control >> 12) & 1);
            Ctrl.PortSelect = (byte)((control >> 13) & 1);

            Console.WriteLine("----------------------------------------");
            Console.WriteLine("Write Control:");
            Console.WriteLine("TX_Enable:" + Ctrl.TX_Enable);
            Console.WriteLine("DTR_OutputLevel:" + Ctrl.DTR_OutputLevel);
            Console.WriteLine("RX_Enable:" + Ctrl.RX_Enable);
            Console.WriteLine("TX_OutputLevel:" + Ctrl.TX_OutputLevel);
            Console.WriteLine("Acknowledge:" + Ctrl.Acknowledge);
            Console.WriteLine("RTS_OutputLevel:" + Ctrl.RTS_OutputLevel);
            Console.WriteLine("Reset:" + Ctrl.Reset);
            Console.WriteLine("RX_InterruptMode:" + Ctrl.RX_InterruptMode);
            Console.WriteLine("TX_InterruptEnable:" + Ctrl.TX_InterruptEnable);
            Console.WriteLine("RX_InterruptEnable:" + Ctrl.RX_InterruptEnable);
            Console.WriteLine("DSR_InterruptEnable:" + Ctrl.DSR_InterruptEnable);
            Console.WriteLine("PortSelect:" + Ctrl.PortSelect);


            if (Ctrl.Acknowledge == 1) {
                Ctrl.Acknowledge = 0;
                Status.InterruptRequest = 0;
                Status.RX_Parity_Error = 0;
            }

            if (Ctrl.Reset == 1) {
                Ctrl.Reset = 0;
                RX_Data = 0xFF;
                TX_Data = 0xFF;
            }

            byte address = 0x0A;
            byte value = (byte)(GetCtrl() & 0xF);            //DTR and RTS are in the first byte
            Socket.Send(new byte[] { address, value });     //Send it to the other side
            Console.WriteLine("sent addr: " + address.ToString("x") + " val: " + value.ToString("x"));
        }

        int delay = 500;
        Queue<Command> buffer = new Queue<Command>();

        public void Tick(int cycles) {
            if (delay > 0) {
                delay -= cycles;
                if (delay <= 0) {
                    Command cmd = buffer.Dequeue();
                    HandleCommand(cmd);
                    IRQ_CONTROL.IRQsignal(8); //8??
                    if (buffer.Count > 0) {
                        delay = 500;
                    }
                }
            }

            byte[] receivedData = Socket.Receive();
            if (receivedData != null) {
                Command receivedCommand = new Command();
                receivedCommand.address = receivedData[0];
                receivedCommand.value = receivedData[1];
                buffer.Enqueue(receivedCommand);
                Status.RX_FIFO_Not_Empty = 1;
                Console.WriteLine("received addr: " + receivedCommand.address.ToString("x") + " val: " + receivedCommand.value.ToString("x"));
            }
        }

        public void HandleCommand(Command cmd) {
            switch (cmd.address) {
                case 0x00: RX_Data = cmd.address; break;
                case 0x0A:
                    Status.DSR_InputLevel = (byte)(((cmd.value) >> 1) & 1); //Remote DTR
                    Status.CTS_InputLevel = (byte)(((cmd.value) >> 5) & 1); //Remote RTS
                    Status.TX_FIFO_Not_Full = Status.CTS_InputLevel;
                    break;
                default: throw new Exception("Unknown addr: " +  cmd.address.ToString("x"));
            }
        }

    }
}
