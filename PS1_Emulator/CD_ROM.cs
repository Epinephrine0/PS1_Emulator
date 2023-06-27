using NAudio.CoreAudioApi;
using SixLabors.ImageSharp;
using System;
using System.Collections.Generic;
using System.IO;

namespace PSXEmulator {
    public unsafe class CD_ROM  {   
        public Range range = new Range(0x1F801800, 4);

        public const byte INT0 = 0; //INT0 No response received(no interrupt request)
        public const byte INT1 = 1; //INT1 Received SECOND(or further) response to ReadS/ReadN(and Play+Report)
        public const byte INT2 = 2; //INT2 Received SECOND response(to various commands)
        public const byte INT3 = 3; //INT3 Received FIRST response(to any command)
        public const byte INT4 = 4; //INT4 DataEnd(when Play/Forward reaches end of disk) (maybe also for Read?)
        public const byte INT5 = 5; //INT5 Received error-code(in FIRST or SECOND response)
                                    //INT5 also occurs on SECOND GetID response, on unlicensed disks
                                    //INT5 also occurs when opening the drive door(even if no command
                                    //was sent, ie.even if no read-command or other command is active)
                                    //INT6 N/A
                                    //INT7 N/A

        //0 - Status register
        byte Index;                 //0-1
        byte ADPBUSY = 0;           //2
        byte PRMEMPT = 1;           //3
        byte PRMWRDY = 1;           //4
        byte RSLRRDY = 0;           //5
        public byte DRQSTS = 0;     //6
        byte BUSYSTS = 0;           //7

        //1 - Response FIFO

        Queue<Byte> responseBuffer = new Queue<Byte>();
        Queue<Byte> parameterBuffer = new Queue<Byte>();

        Queue<DelayedInterrupt> interrupts = new Queue<DelayedInterrupt>();

        Byte[] seekParameters = new byte[3];

        //2
        byte IRQ_enable; //0-7

        //3
        byte IRQ_flag;  //0-7

        byte requestRegister;
        byte stat = 0b00000010;

        //Setmode
        byte mode;
        uint lastSize;
        bool autoPause;
        bool report;

        uint m; //Minutes
        uint s; //Seconds
        uint f; //Sectors

        public enum Command {   //This could be removed?
            GetStat,
            Init,
            GetID,
            Pause,
            Stop,
            Other,
            Seek,
            None,
            Play
        }

        public enum State {    //TODO: Enhance/Add states
            Idle,
            RespondingToCommand,
            ReadingSectors
        }

        /* CD Audio Volume */
        byte LeftCD_toLeft_SPU_Volume;
        byte LeftCD_toRight_SPU_Volume;
        byte RightCD_toRight_SPU_Volume;
        byte RightCD_toLeft_SPU_Volume;

        bool doubleSpeed;

        State CDROM_State;
        Command command;

        public byte padding;
        public byte currentCommand;
        public bool hasDisk = true;       //A game disk, audio disks are not supported yet 
        public bool ledOpen = false;
        public string path =
            @"C:\Users\Old Snake\Desktop\PS1\ROMS\Crash Team Racing [U] [SCUS-94426]\CTR - Crash Team Racing (USA).bin";
        byte[] disk;
        private delegate*<CD_ROM, void>[] lookUpTable = new delegate*<CD_ROM, void>[0xFF + 1];
        public CDROMDataController DataController;

        uint currentIndex;      //Offset in bytes
        int counter = 0;        //Delay
        uint sectorOffset = 0;  //Skip headers, etc

        public CD_ROM() {
            disk = hasDisk? File.ReadAllBytes(path) : null;

            //Fill the functions lookUpTable with illegal first, to be safe
            for (int i = 0; i< lookUpTable.Length; i++) {
                lookUpTable[i] = &illegal;
            }
            DataController = new CDROMDataController(ref disk);
            //Add whatever I implemented manually
            lookUpTable[0x01] = &getStat;
            lookUpTable[0x02] = &setloc;
            lookUpTable[0x03] = &play;
            lookUpTable[0x06] = &readN_S;
            lookUpTable[0x08] = &stop;
            lookUpTable[0x09] = &pause;
            lookUpTable[0x0A] = &init;
            lookUpTable[0x0B] = &mute;
            lookUpTable[0x0C] = &demute;
            lookUpTable[0x0D] = &setFilter;
            lookUpTable[0x0E] = &setMode;
            lookUpTable[0x13] = &getTN;
            lookUpTable[0x14] = &getTD;
            lookUpTable[0x15] = &seekl;
            lookUpTable[0x16] = &seekP;
            lookUpTable[0x19] = &test;
            lookUpTable[0x1A] = &getID;
            lookUpTable[0x1B] = &readN_S;
        }
        private static byte DecToBcd(byte value) {
            return (byte)(value + 6 * (value / 10));
        }

        private static int BcdToDec(byte value) {
            return value - 6 * (value >> 4);
        }

        private static void illegal(CD_ROM cdrom) {
            //Console.WriteLine("[CDROM] Ignoring command 0x" +  cdrom.currentCommand.ToString("X"));
            throw new Exception("Unknown CDROM command: " + cdrom.currentCommand.ToString("x"));
        }
        public void controller(byte command) {
            currentCommand = command;
            interrupts.Clear();
            responseBuffer.Clear();
            DataController.dataFifo.Clear();
            DataController.sectorQueue.Clear();
            lookUpTable[command](this);
            parameterBuffer.Clear();
            //Console.WriteLine("CD-ROM: 0x" + command.ToString("x"));
        }
        private byte CDROM_Status() {
            DRQSTS = (byte)((DataController.dataFifo.Count > 0) ? 1 : 0);
            RSLRRDY = (byte)((responseBuffer.Count > 0) ? 1 : 0);
            byte status = (byte)((BUSYSTS << 7) | (DRQSTS << 6) 
                | (RSLRRDY << 5) | (PRMWRDY << 4) | (PRMEMPT << 3) | (ADPBUSY << 2) | Index);
            //Console.WriteLine("[CDROM] Reading Status: " + status.ToString("x"));
            return status;
        }

        public void storeByte(uint address, byte value) {
            uint offset = address - range.start;
            switch (offset) {
                case 0: Index = (byte)(value & 3); break; //Status register, all mirrors
                case 1:
                    switch (Index) {
                        case 0: controller(value); break;
                        case 3: RightCD_toRight_SPU_Volume = value; break;
                        default: throw new Exception("Unknown Index (" + Index + ")" + " at CRROM command register");
                    }
                    break;

                case 2:
                    switch (Index) {
                        case 0: parameterBuffer.Enqueue(value); break;
                        case 1: IRQ_enable = value; break;
                        case 2: LeftCD_toLeft_SPU_Volume = value; break;
                        case 3: RightCD_toLeft_SPU_Volume = value; break;
                        default: throw new Exception("Unknown Index (" + Index + ")" + " at CRROM IRQ enable register");
                    }
                    break;

                case 3:
                    switch (Index) {
                        case 0: RequestRegister(value); break;
                        case 1: InterruptFlagRegister(value); break;
                        case 2: LeftCD_toRight_SPU_Volume = value; break;
                        case 3: applyVolume(value); break;
                        default:  throw new Exception("Unknown Index (" + Index + ")" + " at CRROM IRQ flag register");
                    }
                    break;

                default: throw new Exception("Unhandled store at CRROM offset: " + offset + " index: " + Index);
            }
        }

        public byte loadByte(uint address) {
            uint offset = address - range.start;

            switch (offset) {
                case 0: return CDROM_Status();              //Status register, all indexes are mirrors
                case 1: return responseBuffer.Dequeue();    //Response fifo, all indexes are mirrors
                case 2: return DataController.readByte();           //Data fifo, all indexes are mirrors
                case 3:
                    switch (Index) {
                        case 0:
                        case 2:
                            return (byte)(IRQ_enable | 0xe0);   //0-4 > INT enable , the rest are 1s
                        case 1:
                        case 3:
                            return (byte)(IRQ_flag | 0xe0);   //0-4 > INT flag , the rest are 1s

                        default: throw new Exception("Unknown Index (" + Index + ")" + " at CRROM IRQ flag register");
                    }
                default: throw new Exception("Unhandled read at CRROM register: " + offset + " index: " + Index);
            }
        }
        private void RequestRegister(byte value) {
            if ((value & 0x80) != 0) { //Request data
                if (DataController.dataFifo.Count > 0) { return; }
                DataController.moveSectorToDataFifo();
            }
            else {
                DataController.dataFifo.Clear();
            }
        }
        private void InterruptFlagRegister(byte value) {
            IRQ_flag &= (byte)~(value & 0x1F);

            if (interrupts.Count > 0 && interrupts.Peek().delay <= 0) {
                IRQ_flag |= interrupts.Dequeue().interrupt;
            }
            if (((value >> 6) & 1) == 1) {
                parameterBuffer.Clear();
            }
        }
        private void applyVolume(byte value) {
            bool isMute = (value & 1) != 0;
            bool applyVolume = ((value >> 5) & 1) != 0;
            if (isMute) {
                DataController.currentVolume.LtoL = 0;
                DataController.currentVolume.LtoR = 0;
                DataController.currentVolume.RtoL = 0;
                DataController.currentVolume.RtoR = 0;
            }
            else if (applyVolume) {
                DataController.currentVolume.LtoL = LeftCD_toLeft_SPU_Volume;
                DataController.currentVolume.LtoR = LeftCD_toRight_SPU_Volume;
                DataController.currentVolume.RtoL = RightCD_toLeft_SPU_Volume;
                DataController.currentVolume.RtoR = RightCD_toRight_SPU_Volume;
            }
        }
        private static void test(CD_ROM cdrom) {
            byte parameter = cdrom.parameterBuffer.Dequeue();
            switch (parameter) {
                case 0x20: getDateAndVersion(cdrom); break;
                default: throw new Exception("[CDROM] Test command: unknown parameter: " + parameter.ToString("x"));
            }
        }
        private static void setFilter(CD_ROM cdrom) {
            cdrom.DataController.filter.fileNumber = cdrom.parameterBuffer.Dequeue();
            cdrom.DataController.filter.channelNumber = cdrom.parameterBuffer.Dequeue();

            cdrom.responseBuffer.Enqueue(cdrom.stat);
            cdrom.interrupts.Enqueue(new DelayedInterrupt(0x000c4e1, INT3));
        }
        private static void seekP(CD_ROM cdrom) {
            cdrom.CDROM_State = State.RespondingToCommand;
            cdrom.command = Command.Seek;

            cdrom.currentIndex = ((cdrom.m * 60 * 75) + (cdrom.s * 75) + cdrom.f - 150) * 0x930;

            cdrom.stat = 0x42; //Seek

            //Response 1
            cdrom.responseBuffer.Enqueue(cdrom.stat);
            cdrom.interrupts.Enqueue(new DelayedInterrupt(0x000c4e1, INT3));

            //Response 2
            cdrom.stat = (byte)(cdrom.stat & (~0x40));
            cdrom.responseBuffer.Enqueue(cdrom.stat);
            cdrom.interrupts.Enqueue(new DelayedInterrupt(0x0004a00, INT2));
        }

        private static void play(CD_ROM cdrom) {   //CD-DA ? TODO
            cdrom.CDROM_State = State.ReadingSectors;
            cdrom.command = Command.Play;

            Console.WriteLine("[CDROM] Play command ignored");
            //currentIndex = (((m * 60 * 75) + (s * 75) + f - 150)) * 0x930 + sectorOffset;

            cdrom.stat = 0x2;
            cdrom.stat |= (1 << 7);   //Play

            //Response 1
            cdrom.responseBuffer.Enqueue(cdrom.stat);
            cdrom.interrupts.Enqueue(new DelayedInterrupt(0x000c4e1, INT3));
        }

            private static void stop(CD_ROM cdrom) {
            //The first response returns the current status (this already with bit5 cleared)
            //The second response returns the new status (with bit1 cleared)
            cdrom.stat = 0x2;
            cdrom.responseBuffer.Enqueue(cdrom.stat);
            cdrom.interrupts.Enqueue(new DelayedInterrupt(0x0013cce, INT3));

            cdrom.stat = 0x0;
            cdrom.responseBuffer.Enqueue(cdrom.stat);

            if (cdrom.command == Command.Stop) {
                cdrom.interrupts.Enqueue(new DelayedInterrupt(0x0001d7b, INT2));
            }
            else {
                cdrom.interrupts.Enqueue(new DelayedInterrupt(cdrom.doubleSpeed ? 0x18a6076 : 0x0d38aca, INT2));
            }

            cdrom.command = Command.Stop;
        }

        private static void getTD(CD_ROM cdrom) {
            cdrom.responseBuffer.Enqueue(cdrom.stat);
            cdrom.responseBuffer.Enqueue(DecToBcd((byte)cdrom.m));
            cdrom.responseBuffer.Enqueue(DecToBcd((byte)cdrom.s));
            cdrom.interrupts.Enqueue(new DelayedInterrupt(0x0013cce, INT3));
        }

        private static void getTN(CD_ROM cdrom) {
            // throw new Exception("Check game cue");  //Todo: read and parse .cue files

            cdrom.CDROM_State = State.RespondingToCommand;
            cdrom.command = Command.Other;

            cdrom.responseBuffer.Enqueue(cdrom.stat);
            cdrom.responseBuffer.Enqueue(0x1);
            cdrom.responseBuffer.Enqueue(0x14);

            cdrom.interrupts.Enqueue(new DelayedInterrupt(0x0013cce, INT3));

        }

        private static void mute(CD_ROM cdrom) {
            // Console.WriteLine("[CDROM] Mute");

            cdrom.CDROM_State = State.RespondingToCommand;
            cdrom.command = Command.Other;

            //Response 1
            cdrom.responseBuffer.Enqueue(cdrom.stat);
            cdrom.interrupts.Enqueue(new DelayedInterrupt(0x0013cce, INT3));
        }

        private static void demute(CD_ROM cdrom) {

            // Console.WriteLine("[CDROM] Demute");

            cdrom.CDROM_State = State.RespondingToCommand;
            cdrom.command = Command.Other;

            //Response 1
            cdrom.responseBuffer.Enqueue(cdrom.stat);
            cdrom.interrupts.Enqueue(new DelayedInterrupt(0x0013cce, INT3));
        }

        private static void pause(CD_ROM cdrom) {
            cdrom.CDROM_State = State.RespondingToCommand;

            //Response 1
            cdrom.responseBuffer.Enqueue(cdrom.stat);
            cdrom.interrupts.Enqueue(new DelayedInterrupt(0x0013cce, INT3));

            //Response 2
            cdrom.stat = 0x2;
            cdrom.responseBuffer.Enqueue(cdrom.stat);

            if(cdrom.command == Command.Pause) {
                cdrom.interrupts.Enqueue(new DelayedInterrupt(0x0001df2, INT2));
            }
            else {
                cdrom.interrupts.Enqueue(new DelayedInterrupt(cdrom.doubleSpeed ? 0x010bd93 : 0x021181c, INT2));
            }
            cdrom.command = Command.Pause;
        }
        private static void init(CD_ROM cdrom) {
            // Console.WriteLine("[CDROM] Init");
            cdrom.CDROM_State = State.RespondingToCommand;
            cdrom.command = Command.Init;

            cdrom.mode = 0;
            cdrom.stat = 0x2;

            //Response 1
            cdrom.responseBuffer.Enqueue(cdrom.stat);
            cdrom.interrupts.Enqueue(new DelayedInterrupt(0x0013cce, INT3));

            //Response 2
            cdrom.responseBuffer.Enqueue(cdrom.stat);
            cdrom.interrupts.Enqueue(new DelayedInterrupt(0x0004a00, INT2));

        }

        private static void readN_S(CD_ROM cdrom) {
            cdrom.CDROM_State = State.ReadingSectors;

            cdrom.currentIndex = ((cdrom.m * 60 * 75) + (cdrom.s * 75) + cdrom.f - 150) * 0x930;

            cdrom.stat = 0x2; //Read
            cdrom.stat |= 0x20;

            //Response 1
            cdrom.responseBuffer.Enqueue(cdrom.stat);
            cdrom.interrupts.Enqueue(new DelayedInterrupt(0x000c4e1, INT3));

            //Further responses [INT1] are added in tick() 
        }
  
        private static void setMode(CD_ROM cdrom) {
            // Console.WriteLine("[CDROM] seekl");
            cdrom.CDROM_State = State.RespondingToCommand;
            cdrom.command = Command.Other;

            cdrom.mode = cdrom.parameterBuffer.Dequeue();
           // Console.WriteLine("[CDROM] Setmode: " + mode.ToString("x"));

            if (((cdrom.mode >> 4) & 1) == 0) {
                if (((cdrom.mode >> 5) & 1) == 0) {
                    cdrom.lastSize = 0x800;
                    cdrom.sectorOffset = 24;
                }
                else {
                    cdrom.lastSize = 0x924;
                    cdrom.sectorOffset = 12;
                }
            }

            //Test
            cdrom.DataController.bytesToSkip = cdrom.sectorOffset;
            cdrom.DataController.sizeOfDataSegment = cdrom.lastSize;
            cdrom.DataController.filter.isEnabled = ((cdrom.mode >> 3) & 1) != 0;

            cdrom.doubleSpeed = ((cdrom.mode >> 7) & 1) != 0;
            cdrom.autoPause = ((cdrom.mode >> 1) & 1) != 0; //For audio play only
            cdrom.report = ((cdrom.mode >> 2) & 1) != 0; //For audio play only

            cdrom.DataController.XA_ADPCM_En = ((cdrom.mode >> 6) & 1) != 0; //(0=Off, 1=Send XA-ADPCM sectors to SPU Audio Input)

            //Response 1
            cdrom.responseBuffer.Enqueue(cdrom.stat);
            cdrom.interrupts.Enqueue(new DelayedInterrupt(0x000c4e1, INT3));

        }

        private static void seekl(CD_ROM cdrom) {
            cdrom.CDROM_State = State.RespondingToCommand;
            cdrom.command = Command.Seek;

            cdrom.currentIndex = ((cdrom.m * 60 * 75) + (cdrom.s * 75) + cdrom.f - 150) * 0x930;

            cdrom.stat = 0x42; //Seek

            //Response 1
            cdrom.responseBuffer.Enqueue(cdrom.stat);
            cdrom.interrupts.Enqueue(new DelayedInterrupt(0x000c4e1, INT3));

            //Response 2
            cdrom.stat = (byte)(cdrom.stat & (~0x40));
            cdrom.responseBuffer.Enqueue(cdrom.stat);
            cdrom.interrupts.Enqueue(new DelayedInterrupt(0x0004a00, INT2));
        }


        private static void setloc(CD_ROM cdrom) {
            cdrom.CDROM_State = State.RespondingToCommand;
            cdrom.command = Command.Other;

            cdrom.seekParameters[0] = cdrom.parameterBuffer.Dequeue();  //Minutes
            cdrom.seekParameters[1] = cdrom.parameterBuffer.Dequeue();  //Seconds 
            cdrom.seekParameters[2] = cdrom.parameterBuffer.Dequeue();  //Sectors 

            cdrom.m = (uint)((cdrom.seekParameters[0] & 0xF) * 1 + ((cdrom.seekParameters[0] >> 4) & 0xF) * 10);
            cdrom.s = (uint)((cdrom.seekParameters[1] & 0xF) * 1 + ((cdrom.seekParameters[1] >> 4) & 0xF) * 10);
            cdrom.f = (uint)((cdrom.seekParameters[2] & 0xF) * 1 + ((cdrom.seekParameters[2] >> 4) & 0xF) * 10);

            cdrom.responseBuffer.Enqueue(cdrom.stat);
            cdrom.interrupts.Enqueue(new DelayedInterrupt(0x000c4e1, INT3));

        }

        private static void getID(CD_ROM cdrom) {
            //Console.WriteLine("[CDROM] GetId");
            cdrom.CDROM_State = State.RespondingToCommand;
            cdrom.command = Command.GetID;

            cdrom.responseBuffer.Enqueue(cdrom.stat);
            cdrom.stat = 0x40;  //0x40 seek
            cdrom.stat |= 0x2;

            cdrom.interrupts.Enqueue(new DelayedInterrupt(0x000c4e1, INT3));

            if (cdrom.hasDisk) {
                cdrom.interrupts.Enqueue(new DelayedInterrupt(0x0004a00, INT2));

                cdrom.responseBuffer.Enqueue(0x02);       //STAT
                cdrom.responseBuffer.Enqueue(0x00);       //Flags (Licensed, Missing, Audio or Data CD) 
                cdrom.responseBuffer.Enqueue(0x20);       //Disk type 
                cdrom.responseBuffer.Enqueue(0x00);       //Usually 0x00
                cdrom.responseBuffer.Enqueue(0x53);       //From here and down it is ASCII, this is "SCEA"
                cdrom.responseBuffer.Enqueue(0x43);
                cdrom.responseBuffer.Enqueue(0x45);
                cdrom.responseBuffer.Enqueue(0x41);

            } else {
                cdrom.interrupts.Enqueue(new DelayedInterrupt(0x0004a00, INT5));

                cdrom.responseBuffer.Enqueue(0x08);       //No disk, this leads to the Shell 
                cdrom.responseBuffer.Enqueue(0x40);
                cdrom.responseBuffer.Enqueue(0x00);
                cdrom.responseBuffer.Enqueue(0x00);
                cdrom.responseBuffer.Enqueue(0x00);
                cdrom.responseBuffer.Enqueue(0x00);
                cdrom.responseBuffer.Enqueue(0x00);
                cdrom.responseBuffer.Enqueue(0x00);
              }
        }
        private static void getStat(CD_ROM cdrom) {
            // Console.WriteLine("[CDROM] GetStat");

            cdrom.CDROM_State = State.RespondingToCommand;
            cdrom.command = Command.GetStat;

            if (!cdrom.ledOpen) {     //Reset shell open unless it is still opened, TODO: add disk swap
                cdrom.stat = (byte)(cdrom.stat & (~0x18));
                cdrom.stat |= 0x2;
            }

            cdrom.interrupts.Enqueue(new DelayedInterrupt(0x000c4e1, INT3));
            cdrom.responseBuffer.Enqueue(cdrom.stat);
        }

        public static void getDateAndVersion(CD_ROM cdrom) {
            //Console.WriteLine("[CDROM] GetDate/Version");
            cdrom.CDROM_State = State.RespondingToCommand;
            cdrom.command = Command.Other;

            //0x94, 0x09, 0x19, 0xC0
            //We can set anything, but this is the original values
            cdrom.responseBuffer.Enqueue(0x94);
            cdrom.responseBuffer.Enqueue(0x09);
            cdrom.responseBuffer.Enqueue(0x19);
            cdrom.responseBuffer.Enqueue(0xc0);

            cdrom.interrupts.Enqueue(new DelayedInterrupt(0x000c4e1, INT3));
        }
        void IncrementIndex() {
            f++;
            if (f >= 75) {
                f = 0;
                s++;
            }
            if (s >= 60) {
                s = 0;
                m++;
            }
            currentIndex = ((m * 60 * 75) + (s * 75) + f - 150) * 0x930; 
        }

        internal void tick(int cycles) {
            counter += cycles;

            if (interrupts.Count > 0) {
                interrupts.Peek().delay -= cycles;
            }
            if (interrupts.Count > 0 && IRQ_flag == 0 && interrupts.Peek().delay <= 0) {
                IRQ_flag |= interrupts.Dequeue().interrupt;
                if ((IRQ_enable & IRQ_flag) != 0) {
                    IRQ_CONTROL.IRQsignal(2);
                }
            }

            switch (CDROM_State) {
                case State.Idle:
                    counter = 0;
                    if(command == Command.Pause) { return; }
                    command = Command.None;
                    break;

                case State.RespondingToCommand:
                    if (interrupts.Count == 0 && responseBuffer.Count == 0) {
                        CDROM_State = State.Idle;
                        RSLRRDY = 0;
                        return;
                    }
                    break;


                case State.ReadingSectors:
                    if (counter < (33868800 / (doubleSpeed ? 150 : 75)) || interrupts.Count != 0) {
                        return;
                    }

                    counter = 0;
                    
                    if(command != Command.Play) {
                        bool sendToCPU = DataController.loadNewSector(currentIndex);
                        IncrementIndex();
                        if (sendToCPU) {    
                            responseBuffer.Enqueue(stat);
                            if (m < 74) {
                                interrupts.Enqueue(new DelayedInterrupt(doubleSpeed ? 0x0036cd2 : 0x006e1cd, INT1));
                            }
                            else {
                                interrupts.Enqueue(new DelayedInterrupt(50000, INT4));    //Data end, but what should be the index?
                            }
                        }
                        else {
                            //Send to SPU
                        }
                    }
                    else {
                        DataController.sectorQueue.Clear();
                        if (report) {
                            responseBuffer.Enqueue(0xFF);   //Garbage Report 
                            interrupts.Enqueue(new DelayedInterrupt(doubleSpeed ? 0x0036cd2 : 0x006e1cd, INT1));
                        }
                        if(autoPause && m == 74) {
                            responseBuffer.Enqueue(stat);
                            interrupts.Enqueue(new DelayedInterrupt(50000, INT4));    //Data end
                            pause(this);
                            Console.WriteLine("[CDROM] Data end INT4 issued!");
                        }
                    }
                    break;
            }
        }
    }
    public class DelayedInterrupt {     //Interrupts from the CDROM need to be delayed with an average number of cycles  
        public int delay;
        public byte interrupt;

        public DelayedInterrupt(int delay, byte interrupt) {
            this.delay = delay;
            this.interrupt = interrupt;
        }
    }
}
