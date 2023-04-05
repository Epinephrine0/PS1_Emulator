using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace PS1_Emulator {
    public class IO_PORTS {
        //TODO:
        //Baudrate timer
        //..?

        public Range range = new Range(0x1F801040, 0x1E+1);      
        UInt16 JOY_CTRL = 0;
        UInt16 JOY_BAUD = 0;
        UInt16 JOY_MODE = 0;
        UInt32 JOY_STAT = 0b11;

        bool TXlatch = false;
        bool transfer = false;

        UInt32 JOY_TX_DATA;     
        UInt32 JOY_RX_DATA;
        bool fifoFull = false;

        public static int delay = 0;
        MemoryCard memoryCard;
        public Controller controller1;
        public Controller controller2;

        //JOYSTAT
        bool TXREADY1 = true;
        bool TXREADY2 = true;
        bool RXParityError;
        bool ACKLevel;
        bool IRQ;
        int Timer;


        //JOYCTRL
        bool TXEN;
        bool JOYoutput;
        bool RXEN;
        bool Acknowledge;
        bool Reset;
        uint RXIRQMode;
        bool TX_IRQ_EN;
        bool RX_IRQ_EN;
        bool Acknowledge_IRQ_EN;
        uint SlotNum;

        uint SIO_CTRL;
        uint SIO_MODE;
        uint SIO_BAUD;
        uint SIO_DATA;

        //JOYMODE
         uint baudrateReloadFactor;
         uint characterLength;
         bool parityEnable;
         bool parityTypeOdd;
         bool clkOutputPolarity;


        //public static UInt16 dPadButtons = 0xffff;
        public static UInt16 MouseButtons = 0xffff & (0b00 << 8);
        public static UInt16 MouseSensors = 0;


        enum SelectedDevice {
            Controller, MemoryCard, None
        }
        SelectedDevice selectedDevice = SelectedDevice.None;
        public IO_PORTS() {

            byte[] memoryCardData;

            try{

                memoryCardData = File.ReadAllBytes("MemoryCard.mcd");

            }catch(FileNotFoundException e) {

                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Could not find memory card, will create a new one.");
                Console.ForegroundColor = ConsoleColor.Green;
                memoryCardData = new byte[128*1024];
                File.WriteAllBytes("MemoryCard.mcd", memoryCardData);
            }


            memoryCard = new MemoryCard(memoryCardData);
            controller1 = new Controller();
            controller2 = new Controller();


        }
        public byte read(uint offset) {

            switch (offset) {

                 case 0:
                   
                    fifoFull = false;
                    counter = 0;

                    if (JOYoutput) {
                        TXREADY2 = true;
                        if (SlotNum == 1 && selectedDevice == SelectedDevice.MemoryCard) {                     //Second memory card is not connected
                            ACKLevel = false;
                            return 0xff;
                        }
                    }
                   

                    return (byte)JOY_RX_DATA; 


                default:

                    throw new Exception(offset.ToString("x"));
            }

        }
        public UInt16 read16(uint offset) {

            switch (offset) {

                       case 4:
                           return getStatu();        //3


                       case 0xA:
                           
                          return getCTRL();

                       case 0xE:

                       return 0xFFFF;
                       
                        
                default:

                       throw new Exception("Unkown JOY port: " + offset.ToString("X"));



            }


        }


        public int counter = 0;
        bool en = false;
        public void tick(int cycles) {
            //counter += ACKLevel ? cycles : 0;
            counter -= cycles;

            if (counter < 0) {
                ACKLevel = false;
                IRQ = true;
                IRQ_CONTROL.IRQsignal(7);  
                
            }

        }
        public void write(uint offset, uint value) {

            
            switch (offset) {
                case 0:

                    JOY_TX_DATA = value;        //Data sent 
                    JOY_RX_DATA = 0xFF;
                    fifoFull = true;
                    TXREADY1 = true;
                    
                    if (JOYoutput) {
                        //en = true;
                        TXREADY2 = true;
                        if (selectedDevice == SelectedDevice.Controller) {
                            JOY_RX_DATA = SlotNum == 0 ? controller1.response(JOY_TX_DATA) : controller2.response(JOY_TX_DATA);
                            ACKLevel = SlotNum == 0 ? controller1.ack : controller2.ack;

                            //Console.WriteLine("Sent: " + JOY_TX_DATA.ToString("x"));
                            //Console.WriteLine("Response: " + JOY_RX_DATA.ToString("x") + " ACK: " + ACKLevel);

                        }
                        else if(selectedDevice == SelectedDevice.MemoryCard) {

                            JOY_RX_DATA = memoryCard.response(JOY_TX_DATA);
                            ACKLevel = memoryCard.ack;

                            //Console.WriteLine("Sent: " + JOY_TX_DATA.ToString("x"));
                            //Console.WriteLine("Response: " + JOY_RX_DATA.ToString("x") + " ACK: " + ACKLevel);

                        }
                        else {
                            if (JOY_TX_DATA == 0x01) {
                                selectedDevice = SelectedDevice.Controller;
                                JOY_RX_DATA = 0xFF;
                                ACKLevel = true;

                            }
                            else if (JOY_TX_DATA == 0x81) {
                                selectedDevice = SelectedDevice.MemoryCard;
                                JOY_RX_DATA = 0xFF;
                                ACKLevel = true;
                            }
                            
                        }

                    }
                    else {
                        ACKLevel = false;

                    }

                    if (!ACKLevel) {
                        controller1.sequenceNum = 0;
                        memoryCard.reset();
                        counter = -1;
                        selectedDevice = SelectedDevice.None;

                    }
                    else {
                        counter = 1500;
                    }

                    break;

                

                case 8:
                    set_joy_mode((ushort)value);
                    break;

                case 0xA:

                    set_joy_ctrl((ushort)value);

                    break;

                case 0xE:
                    JOY_BAUD = (ushort)value;
                    break;

                case 0x1A:
                    SIO_CTRL = value;
                    break;

                case 0x18:
                    SIO_MODE = value;
                    break;

                case 0x1E:

                    SIO_BAUD = value;
                    break;

                case 0x10:
                    SIO_DATA = value;
                    break; 

                default:
                    throw new Exception("Unkown JOY port: " + offset.ToString("X"));
                    
            }

        }
        private ushort getCTRL() {
            uint joy_ctrl = 0;
            joy_ctrl |= TXEN ? 1u : 0u;
            joy_ctrl |= (JOYoutput ? 1u : 0u) << 1; 
            joy_ctrl |= (RXEN ? 1u : 0u) << 2;
            // bit 4 only writeable
            // bit 6 only writeable
            //bit 7 allways 0
            //unkown (3,5) are ignored
            joy_ctrl |= RXIRQMode << 8;
            joy_ctrl |= (TX_IRQ_EN ? 1u : 0u) << 10;
            joy_ctrl |= (RX_IRQ_EN ? 1u : 0u) << 11;
            joy_ctrl |= (Acknowledge_IRQ_EN ? 1u : 0u) << 12;
            joy_ctrl |= SlotNum << 13;

            return (ushort)joy_ctrl;

        }

        private ushort getStatu() {

            uint joy_stat = 0;

            joy_stat |= TXREADY1 ? 1u : 0u;
            joy_stat |= (fifoFull ? 1u : 0u) << 1;
            joy_stat |= (TXREADY2 ? 1u : 0u) << 2;
            joy_stat |= (RXParityError ? 1u : 0u) << 3;
            joy_stat |= (ACKLevel ? 1u : 0u) << 7;
            joy_stat |= (IRQ ? 1u : 0u) << 9;
            joy_stat |= (uint)Timer << 11;

            ACKLevel = false;

            return (ushort)joy_stat;



        }
        public void set_joy_ctrl(ushort value) {
            TXEN = (value & 1) != 0;
            JOYoutput = ((value >> 1) & 1) != 0;                 //JOYn Output      (0=High, 1=Low/Select) (/JOYn as defined in Bit13)
            RXEN = ((value >> 2) & 1) != 0;
            Acknowledge = ((value >> 4) & 1) != 0;  
            Reset = ((value >> 6) & 1) != 0;
            RXIRQMode = ((uint)((value >> 8) & 3));
            TX_IRQ_EN = ((value >> 10) & 1) != 0;
            RX_IRQ_EN = ((value >> 11) & 1) != 0;
            Acknowledge_IRQ_EN = ((value >> 12) & 1) != 0;
            SlotNum = ((uint)((value >> 13) & 1));              //Desired Slot Number  (0=/JOY1, 1=/JOY2) (set to LOW when Bit1=1)

            if (Acknowledge) {
                RXParityError = false;
                IRQ = false;
                Acknowledge = false;
            }

            if (!JOYoutput) {
                memoryCard.reset();
                selectedDevice = SelectedDevice.None;
                controller1.sequenceNum = 0;

            }

            if (Reset) {
                reset();
                memoryCard.reset();
                selectedDevice = SelectedDevice.None;
                controller1.sequenceNum = 0;
            }

        }
        public void reset() {
            set_joy_ctrl((ushort)0);
            set_joy_mode((ushort)0);

            JOY_RX_DATA = 0xff;
            JOY_TX_DATA = 0;
            TXREADY1 = true;
            TXREADY2 = true;
           
            JOY_BAUD = 0;

            Reset = false;

        }

        private void set_joy_mode(ushort value) {
            baudrateReloadFactor = (uint)(value & 0x3);
            characterLength = (uint)((value >> 2) & 0x3);
            parityEnable = ((value >> 4) & 0x1) != 0;
            parityTypeOdd = ((value >> 5) & 0x1) != 0;
            clkOutputPolarity = ((value >> 8) & 0x1) != 0;

        }

       
       /* public uint response(UInt32 command) {
            if (command == 0x81) {
                memoryCard.state = MemoryCard.State.Active;
                return 0xff;
            }
            if (command == 0x01) {
                PADS_Communication = true;
                pads.Clear();
                pads.Enqueue(0x41);
                pads.Enqueue(0x5A);
                pads.Enqueue((byte)(dPadButtons & 0xff));
                pads.Enqueue((byte)((dPadButtons >> 8) & 0xff));
                return 0xff;
            }


            if (memoryCard.state == MemoryCard.State.Active) {
                //Console.WriteLine("[IO] " + command.ToString("x"));
                byte r = memoryCard.response(command);
                Console.WriteLine(" Response: " + r.ToString("x"));
                return r;

            }

            if (PADS_Communication) {

                if (pads.Count == 0) {
                    return 0xff;
                }

                return pads.Dequeue();

            }
            return 0xff;

           *//* switch (command) {
                case 0x0:

                    if (queue.Count == 0) {
                        return 0xff;
                    }

                    return queue.Dequeue();

                case 0x1:                       //Controller 

                    queue.Clear();
                    queue.Enqueue(0x41);
                    queue.Enqueue(0x5A);
                    queue.Enqueue((byte)(dPadButtons & 0xff));
                    queue.Enqueue((byte)((dPadButtons >> 8) & 0xff));
                    //resetDPads();

                    return 0xff;

                case 0x42:

                    return queue.Dequeue();

                case 0x81:                 //Memory Card
                    //Console.WriteLine("[IO] " + command.ToString("x"));

                    //memoryCard.reset();
                    memoryCard.state = MemoryCard.State.Active;

                    return 0xff;

                default:

                    return 0xff;

            }*//*




        }*/

      

    }

   
}
