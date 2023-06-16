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

        //Mode from Setmode
        byte mode;
        uint lastSize;
        bool autoPause;
        bool report;

        uint m; //Minutes
        uint s; //Seconds
        uint f; //Sectors

        public enum Command {
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

        public enum State {
            Idle,
            RespondingToCommand,
            ReadingSectors
        }

        byte LeftCD_toLeft_SPU_Volume;
        byte LeftCD_toRight_SPU_Volume;
        byte RightCD_toRight_SPU_Volume;
        byte RightCD_toLeft_SPU_Volume;
        byte audioApplyChanges;

        State CDROM_State;
        Command command;

        public byte padding;
        public byte currentCommand;
        public bool hasDisk = false;       //A game disk, audio disks are not supported yet 
        public bool ledOpen = false;
        public string path = @"C:\Users\Old Snake\Desktop\PS1\ROMS\Metal Gear Solid (USA) (Disc 1) (v1.0)\Metal Gear Solid (USA) (Disc 1).bin";
        byte[] disk;
        private delegate*<CD_ROM, void>[] lookUpTable = new delegate*<CD_ROM, void>[0xFF + 1];

        public CD_ROM() {
            disk = hasDisk? File.ReadAllBytes(path) : null;

            //Fill the functions lookUpTable with illegal first, to be safe
            for (int i = 0; i< lookUpTable.Length; i++) {
                lookUpTable[i] = &illegal;
            }

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

        private static void illegal(CD_ROM cdrom) {
            //Console.WriteLine("[CDROM] Ignoring command 0x" +  cdrom.currentCommand.ToString("X"));
            throw new Exception("Unknown CDROM command: " + cdrom.currentCommand.ToString("x"));
        }
        public void controller(byte command) {
            //BUSYSTS = 1;    //Command busy flag
            //busyDelay = 1000;
            currentCommand = command;
            interrupts.Clear();
            responseBuffer.Clear();
            currentSector.Clear();
            lastReadSector.Clear();
            lookUpTable[command](this);
            parameterBuffer.Clear();
            //Console.WriteLine("CD-ROM: 0x" + command.ToString("x"));

        }
        private byte CDROM_Status() {
            DRQSTS = (byte)((currentSector.Count > 0) ? 1 : 0);
            RSLRRDY = (byte)((responseBuffer.Count > 0) ? 1 : 0);
            byte status = (byte)((BUSYSTS << 7) | (DRQSTS << 6) | (RSLRRDY << 5) | (PRMWRDY << 4) | (PRMEMPT << 3) | (ADPBUSY << 2) | Index);
            //Console.WriteLine("[CDROM] Reading Status: " + status.ToString("x"));
            return status;
        }

        public void storeByte(uint address, byte value) {
            uint offset = address - range.start;

            switch (offset) {
                case 0: Index = (byte)(value & 3); break; //Status register
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
                        case 0: //This could be wrong
                            requestRegister = value;
                            
                            if((requestRegister & 0x80) != 0) {
                                if (currentSector.Count > 0) { return; }
                                int negativeOffset=0;

                                switch (lastSize) {
                                    case 0x800:
                                        negativeOffset = -8;
                                        break;

                                    case 0x924:
                                        negativeOffset = -4;
                                        break;
                                }
                                
                                for (int i = 0; i < lastSize; i++) {
                                    if (i==(lastSize+negativeOffset)) {
                                        padding = lastReadSector.Peek();
                                    }
                                    currentSector.Enqueue(lastReadSector.Dequeue());

                                }
                                

                            }
                            else {
                                currentSector.Clear();

                            }
                            break;

                        case 1:
                            IRQ_flag &= (byte) ~(value & 0x1F);

                            if(interrupts.Count > 0 && interrupts.Peek().delay <= 0) {
                                IRQ_flag |= interrupts.Dequeue().interrupt;
                            }
                            if (((value >> 6) & 1) == 1) {
                                parameterBuffer.Clear();
                            }
                            break;

                        case 2:
                            LeftCD_toRight_SPU_Volume = value;
                            break;

                        case 3:
                            audioApplyChanges = value;
                            break;

                        default:
                            throw new Exception("Unknown Index (" + Index + ")" + " at CRROM IRQ flag register");
                    }
                    break;


                default:
                    throw new Exception("Unhandled store at CRROM offset: " + offset + " index: " + Index);

            }

        }

        public byte loadByte(uint address) {
            uint offset = address - range.start;

            switch (offset) {
                case 0: return CDROM_Status();   //Status register 

                case 1:            //Response FIFO, all indexes are mirrors
                    if (responseBuffer.Count > 0) {

                        return responseBuffer.Dequeue();

                    }
                    //Console.WriteLine("[CDROM] Responding..");
                    return 0xFF;

                case 3:
                    switch (Index) {

                        case 0:
                        case 2:
                            return (byte)(IRQ_enable | 0xe0);   //0-4 > INT enable ,the rest are 1s

                        case 1:
                        case 3:
                            //Console.WriteLine("[CDROM] Reading IRQ FLAG: "+ IRQ_flag);
                            return (byte)(IRQ_flag | 0xe0);   //0-4 > INT flag ,the rest are 1s

                        default:  throw new Exception("Unknown Index (" + Index + ")" + " at CRROM IRQ flag register");
                    }


                default:
                    throw new Exception("Unhandled read at CRROM register: " + offset + " index: " + Index);

            }

        }
        private static void test(CD_ROM cdrom) {
            byte parameter = cdrom.parameterBuffer.Dequeue();
            switch (parameter) {
                case 0x20: getDateAndVersion(cdrom); break;
                default: throw new Exception("Unknown parameter: " + parameter.ToString("x"));
            }
        }
        private static void setFilter(CD_ROM cdrom) {
            Console.WriteLine("[CDROM] Ignoring setFilter");
            cdrom.responseBuffer.Enqueue(cdrom.stat);
            cdrom.interrupts.Enqueue(new DelayedInterrupt(0x000c4e1, INT3));
        }
        private static void seekP(CD_ROM cdrom) {
            cdrom.CDROM_State = State.RespondingToCommand;
            cdrom.command = Command.Seek;

            cdrom.currentIndex = (((cdrom.m * 60 * 75) + (cdrom.s * 75) + cdrom.f - 150)) * 0x930 + cdrom.sectorOffset;

            //Console.WriteLine("[CDROM] seekl");

            cdrom.stat = 0x42; //Seek

            //Response 1
            cdrom.responseBuffer.Enqueue(cdrom.stat);
            cdrom.interrupts.Enqueue(new DelayedInterrupt(0x000c4e1, INT3));

            //Response 2
            cdrom.stat = (byte)(cdrom.stat & (~0x40));
            cdrom.responseBuffer.Enqueue(cdrom.stat);
            cdrom.interrupts.Enqueue(new DelayedInterrupt(0x0004a00, INT2));
        }

        private static void play(CD_ROM cdrom) {   //Subbed

            cdrom.CDROM_State = State.ReadingSectors;
            cdrom.command = Command.Play;

            Console.WriteLine("[CDROM] Play command ignored");
            //currentIndex = (((m * 60 * 75) + (s * 75) + f - 150)) * 0x930 + sectorOffset;

            cdrom.stat = 0x2;
            cdrom.stat |= (1 << 7);   //Play

            //Response 1
            cdrom.responseBuffer.Enqueue(cdrom.stat);
            cdrom.interrupts.Enqueue(new DelayedInterrupt(0x000c4e1, INT3));

            /*//Hardcoded as fuck

            if (parameterBuffer.Count > 0 && parameterBuffer.Dequeue() != 0) {
                //disk = File.ReadAllBytes(@"C:\Users\Old Snake\Desktop\PS1\ROMS\Puzzle Bobble 2 (Japan)\Puzzle Bobble 2 (Japan) (Track 02).bin");
                currentIndex = 0;
            }*/


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

        private static byte DecToBcd(byte value) {
            return (byte)(value + 6 * (value / 10));
        }

        private static int BcdToDec(byte value) {
            return value - 6 * (value >> 4);
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

           
           // Console.WriteLine("[CDROM] Pause, next MSF: " + m.ToString().PadLeft(2, '0') + ":" + s.ToString().PadLeft(2, '0') + ":" + f.ToString().PadLeft(2, '0'));

            
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
            // Console.WriteLine("[CDROM] ReadN at MSF: " + m.ToString().PadLeft(2, '0') + ":" + s.ToString().PadLeft(2, '0') + ":" + f.ToString().PadLeft(2, '0'));

            cdrom.currentIndex = (((cdrom.m * 60 * 75) + (cdrom.s * 75) + cdrom.f - 150)) * 0x930 + cdrom.sectorOffset;

            cdrom.stat = 0x2; //Read
            cdrom.stat |= 0x20;

            //Response 1
            cdrom.responseBuffer.Enqueue(cdrom.stat);
            cdrom.interrupts.Enqueue(new DelayedInterrupt(0x000c4e1, INT3));

            //Further responses [INT1] are added in tick() 
        }
        bool doubleSpeed;
        private static void setMode(CD_ROM cdrom) {
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

            cdrom.doubleSpeed = ((cdrom.mode >> 7) & 1) != 0;
            cdrom.autoPause = ((cdrom.mode >> 1) & 1) != 0; //For audio play only
            cdrom.report = ((cdrom.mode >> 2) & 1) != 0; //For audio play only

            //Response 1
            cdrom.responseBuffer.Enqueue(cdrom.stat);
            cdrom.interrupts.Enqueue(new DelayedInterrupt(0x000c4e1, INT3));

        }

        private static void seekl(CD_ROM cdrom) {
            cdrom.CDROM_State = State.RespondingToCommand;
            cdrom.command = Command.Seek;

            cdrom.currentIndex = (((cdrom.m * 60 * 75) + (cdrom.s * 75) + cdrom.f - 150)) * 0x930 + cdrom.sectorOffset;

            // Console.WriteLine("[CDROM] seekl");

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

            /*Console.WriteLine(
                "[CDROM] setloc: " + m.ToString().PadLeft(2, '0') + ":" + s.ToString().PadLeft(2, '0') + ":" + f.ToString().PadLeft(2, '0')
                );*/

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
            cdrom.CDROM_State = State.RespondingToCommand;
            cdrom.command = Command.GetID;
            //Console.WriteLine("[CDROM] GetId");

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


            //0x94, 0x09, 0x19, 0xC0        We can set anything, but this is the original values
            cdrom.responseBuffer.Enqueue(0x94);
            cdrom.responseBuffer.Enqueue(0x09);
            cdrom.responseBuffer.Enqueue(0x19);
            cdrom.responseBuffer.Enqueue(0xc0);

            cdrom.interrupts.Enqueue(new DelayedInterrupt(0x000c4e1, INT3));

        }

     
        uint currentIndex;
        public Queue<byte> currentSector = new Queue<byte>();
        public Queue<byte> lastReadSector = new Queue<byte>();
        int counter = 0;
        uint sectorOffset = 0;

        internal void tick(int cycles) {
            counter += cycles;

            if (interrupts.Count > 0) {
                interrupts.Peek().delay -= cycles;
            }

            if (interrupts.Count > 0 && IRQ_flag == 0 && interrupts.Peek().delay <= 0) {

                IRQ_flag |= interrupts.Dequeue().interrupt;

                if ((IRQ_enable & IRQ_flag) != 0) {
                    IRQ_CONTROL.IRQsignal(2);
                    //Console.WriteLine("[CDROM] IRQ Fired");
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
                    //lastReadSector.Clear();

                    uint size;
                    if ((mode >> 4 & 1) == 0) {
                        if (((mode >> 5) & 1) == 0) {
                            size = 0x800;
                            lastSize = size;
                            sectorOffset = 24;

                        } else {
                            size = 0x924;
                            lastSize = size;
                            sectorOffset = 12;

                          }

                     } else {
                        size = lastSize;
                      }

                     
                    
                    if(command != Command.Play) {
                        string xa = "";
                        for (uint i = 0; i < size; i++) {
                            lastReadSector.Enqueue(disk[i + currentIndex]);
                            if(((i) >= 0x400) && ((i) <= 0x407)) {
                                xa = xa + (Char)disk[i + currentIndex];
                            }
                        }
                        if (xa.Equals("CD-XA001")) {
                            Console.WriteLine("CD-XA Sector!"); //TODO
                        }
                        xa = "";
                        f++;

                        if (f >= 75) {
                            f = 0;
                            s++;
                        }

                        if (s >= 60) {
                            s = 0;
                            m++;
                        }

                        currentIndex = (((m * 60 * 75) + (s * 75) + f - 150)) * 0x930 + sectorOffset;
                        responseBuffer.Enqueue(stat);
                        if (m < 74) {

                            interrupts.Enqueue(new DelayedInterrupt(doubleSpeed ? 0x0036cd2 : 0x006e1cd, INT1));

                        }
                        else {
                            interrupts.Enqueue(new DelayedInterrupt(50000, INT4));    //Data end
                            //Console.WriteLine("[CDROM] Data end INT4 issued!");
                        }
                    }
                    else {
                        lastReadSector.Clear();

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
