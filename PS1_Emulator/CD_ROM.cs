using NAudio.Dmo.Effect;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO;
using System.Linq;
using static PS1_Emulator.CD_ROM;

namespace PS1_Emulator {
    public class CD_ROM {
        public Range range = new Range(0x1F801800, 4);  

        //INT0 No response received(no interrupt request)
        //INT1 Received SECOND(or further) response to ReadS/ReadN(and Play+Report)
        //INT2 Received SECOND response(to various commands)
        //INT3 Received FIRST response(to any command)
        //INT4 DataEnd(when Play/Forward reaches end of disk) (maybe also for Read?)
        //INT5 Received error-code(in FIRST or SECOND response)
        //INT5 also occurs on SECOND GetID response, on unlicensed disks
        //INT5 also occurs when opening the drive door(even if no command
        //was sent, ie.even if no read-command or other command is active)
        //INT6 N/A
        //INT7 N/A

        public const byte INT0 = 0;
        public const byte INT1 = 1;
        public const byte INT2 = 2;
        public const byte INT3 = 3;
        public const byte INT4 = 4;
        public const byte INT5 = 5;


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

        int number_of_Responses = 0;
        int responseLength = 0;

        byte stat = 0b00000010;

        //Mode from Setmode
        byte mode;
        uint lastSize;
        bool autoPause;
        bool report;

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


        bool gamePresent = true;
        bool ledOpen = false;

        byte[] disk = File.ReadAllBytes(@"C:\Users\Old Snake\Desktop\PS1\ROMS\Crash Bandicoot\Crash Bandicoot.bin");

        private byte CDROM_Status() {
            DRQSTS = (byte)((currentSector.Count > 0)? 1 : 0);
            RSLRRDY = (byte)((responseBuffer.Count > 0) ? 1 : 0);
            byte status = (byte)((BUSYSTS << 7) | (DRQSTS << 6) | (RSLRRDY << 5) | (PRMWRDY << 4) | (PRMEMPT << 3) | (ADPBUSY << 2) | Index);
            //Console.WriteLine("[CDROM] Reading Status: " + status.ToString("x"));

            return status;

        }
        public void store8(UInt32 offset, byte value) {


            switch (offset) {

                case 0:             //Status register

                    Index = (byte)(value & 3);

                    break;


                case 1:

                    switch (Index) {
                        case 0:

                            controller(value);

                            break;

                        case 3:
                            RightCD_toRight_SPU_Volume = value;

                            break;

                        default:
                            throw new Exception("Unknown Index (" + Index + ")" + " at CRROM command register");


                    }


                    break;

                case 2:

                    switch (Index) {
                        case 0:

                            parameterBuffer.Enqueue(value);
                            
                            break;

                        case 1:

                            IRQ_enable = value;
                            break;

                        case 2:
                            LeftCD_toLeft_SPU_Volume = value;
                            break;

                        case 3:
                            RightCD_toLeft_SPU_Volume = value;

                            break;

                        default:
                            throw new Exception("Unknown Index (" + Index + ")" + " at CRROM IRQ enable register");


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
        public byte padding;

        public byte load8(UInt32 offset) {

            switch (offset) {

                case 0:                 //Status register 

                    return CDROM_Status();


                case 1:
                                     //Response FIFO, all indexes are mirrors
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


                        default:
                            throw new Exception("Unknown Index (" + Index + ")" + " at CRROM IRQ flag register");

                    }


                default:
                    throw new Exception("Unhandled read at CRROM register: " + offset + " index: " + Index);



            }

        }
        int busyDelay;
        public void controller(byte command) {

            //BUSYSTS = 1;    //Command busy flag
            //busyDelay = 1000;
   
            interrupts.Clear();
            responseBuffer.Clear();
            currentSector.Clear();
            lastReadSector.Clear();

            switch (command) {

                case 0x1:

                    getStat();

                    break;

                case 0x2:

                    setloc();

                    break;

                case 0x3:

                    play();

                    break;

                case 0x6:

                    readN();

                    break;

                case 0x8:

                    stop();

                    break;

                case 0x9:

                    pause();

                    break;

                case 0xA:

                    init();

                    break;

                case 0xC:

                    demute();

                    break;

                case 0xE:

                    setMode();

                    break;

                case 0x15:

                    seekl();
                    break;

                case 0x16:

                    seekP();

                    break;

                case 0x1A:

                    getID();

                    break;

                case 0x13:

                    getTN();

                    break;

                case 0x14:

                    getTD();

                    break;

                case 0x19:

                    byte parameter = parameterBuffer.Dequeue();

                    switch (parameter) {
                        case 0x20:

                            getDateAndVersion();
                            break;

                        default:

                            throw new Exception("Unknown parameter: " + parameter.ToString("x"));
                    }


                    break;

                case 0xB:
                    mute();

                    break;

                default:

                    throw new Exception("Unhandled CD-ROM controller command: " + command.ToString("X"));

            }
            parameterBuffer.Clear();

        }

        private void seekP() {
            CDROM_State = State.RespondingToCommand;
            command = Command.Seek;

            currentIndex = (((m * 60 * 75) + (s * 75) + f - 150)) * 0x930 + sectorOffset;

            //Console.WriteLine("[CDROM] seekl");

            stat = 0x42; //Seek

            //Response 1
            responseBuffer.Enqueue(stat);
            interrupts.Enqueue(new DelayedInterrupt(0x000c4e1, INT3));

            //Response 2
            stat = (byte)(stat & (~0x40));
            responseBuffer.Enqueue(stat);
            interrupts.Enqueue(new DelayedInterrupt(0x0004a00, INT2));
        }

        private void play() {

            CDROM_State = State.ReadingSectors;
            command = Command.Play;

            //Console.WriteLine("[CDROM] Play");
            currentIndex = (((m * 60 * 75) + (s * 75) + f - 150)) * 0x930 + sectorOffset;

            stat = 0x2; 
            stat |= (1 << 7);   //Play

            //Response 1
            responseBuffer.Enqueue(stat);
            interrupts.Enqueue(new DelayedInterrupt(0x000c4e1, INT3));

            //Hardcoded as fuck

            if (parameterBuffer.Count > 0 && parameterBuffer.Dequeue() != 0) {
                //disk = File.ReadAllBytes(@"C:\Users\Old Snake\Desktop\PS1\ROMS\Puzzle Bobble 2 (Japan)\Puzzle Bobble 2 (Japan) (Track 02).bin");
                currentIndex = 0;
            }


        }

            private void stop() {
            //The first response returns the current status (this already with bit5 cleared)
            //The second response returns the new status (with bit1 cleared)

            stat = 0x2;
            responseBuffer.Enqueue(stat);
            interrupts.Enqueue(new DelayedInterrupt(0x0013cce, INT3));

            stat = 0x0;
            responseBuffer.Enqueue(stat);

            if (command == Command.Stop) {
                interrupts.Enqueue(new DelayedInterrupt(0x0001d7b, INT2));
            }
            else {
                interrupts.Enqueue(new DelayedInterrupt(doubleSpeed ? 0x18a6076 : 0x0d38aca, INT2));
            }

            command = Command.Stop;
        }

        private static byte DecToBcd(byte value) {
            return (byte)(value + 6 * (value / 10));
        }

        private static int BcdToDec(byte value) {
            return value - 6 * (value >> 4);
        }

        private void getTD() {
            responseBuffer.Enqueue(stat);
            responseBuffer.Enqueue(DecToBcd((byte)m));
            responseBuffer.Enqueue(DecToBcd((byte)s));
            interrupts.Enqueue(new DelayedInterrupt(0x0013cce, INT3));


        }

        private void getTN() {
           // throw new Exception("Check game cue");  //Todo: read and parse .cue files

            CDROM_State = State.RespondingToCommand;
            command = Command.Other;

            responseBuffer.Enqueue(stat);
            responseBuffer.Enqueue(0x1);
            responseBuffer.Enqueue(0x14);

            interrupts.Enqueue(new DelayedInterrupt(0x0013cce, INT3));

        }

        private void mute() {
           // Console.WriteLine("[CDROM] Mute");

            CDROM_State = State.RespondingToCommand;
            command = Command.Other;

            //Response 1
            responseBuffer.Enqueue(stat);
            interrupts.Enqueue(new DelayedInterrupt(0x0013cce, INT3));
        }

        private void demute() {

           // Console.WriteLine("[CDROM] Demute");

            CDROM_State = State.RespondingToCommand;
            command = Command.Other;

            //Response 1
            responseBuffer.Enqueue(stat);
            interrupts.Enqueue(new DelayedInterrupt(0x0013cce, INT3));
        }

        private void pause() {
            CDROM_State = State.RespondingToCommand;

           
           // Console.WriteLine("[CDROM] Pause, next MSF: " + m.ToString().PadLeft(2, '0') + ":" + s.ToString().PadLeft(2, '0') + ":" + f.ToString().PadLeft(2, '0'));

            
            //Response 1
            responseBuffer.Enqueue(stat);
            interrupts.Enqueue(new DelayedInterrupt(0x0013cce, INT3));

            //Response 2
            stat = 0x2;
            responseBuffer.Enqueue(stat);

            if(command == Command.Pause) {
                interrupts.Enqueue(new DelayedInterrupt(0x0001df2, INT2));
            }
            else {
                interrupts.Enqueue(new DelayedInterrupt(doubleSpeed ? 0x010bd93 : 0x021181c, INT2));
            }

            command = Command.Pause;


        }

        private void init() {
           // Console.WriteLine("[CDROM] Init");
            CDROM_State = State.RespondingToCommand;
            command = Command.Init;

            mode = 0;
            stat = 0x2;

            //Response 1
            responseBuffer.Enqueue(stat);
            interrupts.Enqueue(new DelayedInterrupt(0x0013cce, INT3));

            //Response 2
            responseBuffer.Enqueue(stat);
            interrupts.Enqueue(new DelayedInterrupt(0x0004a00, INT2));

        }

        private void readN() {
            CDROM_State = State.ReadingSectors;
           // Console.WriteLine("[CDROM] ReadN at MSF: " + m.ToString().PadLeft(2, '0') + ":" + s.ToString().PadLeft(2, '0') + ":" + f.ToString().PadLeft(2, '0'));

            currentIndex = (((m * 60 * 75) + (s * 75) + f - 150)) * 0x930 + sectorOffset;

            stat = 0x2; //Read
            stat |= 0x20;

            //Response 1
            responseBuffer.Enqueue(stat);
            interrupts.Enqueue(new DelayedInterrupt(0x000c4e1, INT3));

            //Further responses [INT1] are added in tick() 
        }
        bool doubleSpeed;
        private void setMode() {
            CDROM_State = State.RespondingToCommand;
            command = Command.Other;

            mode = parameterBuffer.Dequeue();
           // Console.WriteLine("[CDROM] Setmode: " + mode.ToString("x"));

            if (((mode >> 4) & 1) == 0) {
                if (((mode >> 5) & 1) == 0) {
                    lastSize = 0x800;
                    sectorOffset = 24;
                }
                else {
                    lastSize = 0x924;
                    sectorOffset = 12;
                }
            }

            doubleSpeed = ((mode >> 7) & 1) != 0;
            autoPause = ((mode >> 1) & 1) != 0; //For audio play only
            report = ((mode >> 2) & 1) != 0; //For audio play only

            //Response 1
            responseBuffer.Enqueue(stat);
            interrupts.Enqueue(new DelayedInterrupt(0x000c4e1, INT3));

        }

        private void seekl() {
            CDROM_State = State.RespondingToCommand;
            command = Command.Seek;

            currentIndex = (((m * 60 * 75) + (s * 75) + f - 150)) * 0x930 + sectorOffset;

           // Console.WriteLine("[CDROM] seekl");

            stat = 0x42; //Seek

            //Response 1
            responseBuffer.Enqueue(stat);
            interrupts.Enqueue(new DelayedInterrupt(0x000c4e1, INT3));

            //Response 2
            stat = (byte)(stat & (~0x40));
            responseBuffer.Enqueue(stat);
            interrupts.Enqueue(new DelayedInterrupt(0x0004a00, INT2));


        }

        uint m; //Minutes
        uint s; //Seconds
        uint f; //Sectors

        private void setloc() {
            CDROM_State = State.RespondingToCommand;
            command = Command.Other;

            /*Console.WriteLine(
                "[CDROM] setloc: " + m.ToString().PadLeft(2, '0') + ":" + s.ToString().PadLeft(2, '0') + ":" + f.ToString().PadLeft(2, '0')
                );*/

            seekParameters[0] = parameterBuffer.Dequeue();  //Minutes
            seekParameters[1] = parameterBuffer.Dequeue();  //Seconds 
            seekParameters[2] = parameterBuffer.Dequeue();  //Sectors 

            m = (uint)((seekParameters[0] & 0xF) * 1 + ((seekParameters[0] >> 4) & 0xF) * 10);
            s = (uint)((seekParameters[1] & 0xF) * 1 + ((seekParameters[1] >> 4) & 0xF) * 10);
            f = (uint)((seekParameters[2] & 0xF) * 1 + ((seekParameters[2] >> 4) & 0xF) * 10);

            responseBuffer.Enqueue(stat);
            interrupts.Enqueue(new DelayedInterrupt(0x000c4e1, INT3));

        }

        private void getID() {
            CDROM_State = State.RespondingToCommand;
            command = Command.GetID;
            //Console.WriteLine("[CDROM] GetId");

            responseBuffer.Enqueue(stat);
            stat = 0x40;  //0x40 seek
            stat |= 0x2;

            interrupts.Enqueue(new DelayedInterrupt(0x000c4e1, INT3));

          
            if (gamePresent) {
                interrupts.Enqueue(new DelayedInterrupt(0x0004a00, INT2));

                responseBuffer.Enqueue(0x02);       //STAT
                responseBuffer.Enqueue(0x00);       //Flags (Licensed, Missing, Audio or Data CD) 
                responseBuffer.Enqueue(0x20);       //Disk type 
                responseBuffer.Enqueue(0x00);       //Usually 0x00
                responseBuffer.Enqueue(0x53);       //From here and down it is ASCII, this is "SCEA"
                responseBuffer.Enqueue(0x43);
                responseBuffer.Enqueue(0x45);
                responseBuffer.Enqueue(0x41);


            }
            else {
                interrupts.Enqueue(new DelayedInterrupt(0x0004a00, INT5));

                responseBuffer.Enqueue(0x08);       //No disk, this leads to the Shell 
                responseBuffer.Enqueue(0x40);
                responseBuffer.Enqueue(0x00);
                responseBuffer.Enqueue(0x00);
                responseBuffer.Enqueue(0x00);
                responseBuffer.Enqueue(0x00);
                responseBuffer.Enqueue(0x00);
                responseBuffer.Enqueue(0x00);
                 

            }


        }
        private void getStat() {
           // Console.WriteLine("[CDROM] GetStat");

            CDROM_State = State.RespondingToCommand;
            command = Command.GetStat;

            if (!ledOpen) {     //Reset shell openen unless it is still opened
                stat = (byte)(stat & (~0x18));
                stat |= 0x2;
            }

            interrupts.Enqueue(new DelayedInterrupt(0x000c4e1, INT3));
            responseBuffer.Enqueue(stat);

        }

        public void getDateAndVersion() {
            //Console.WriteLine("[CDROM] GetDate/Version");
            CDROM_State = State.RespondingToCommand;
            command = Command.Other;


            //0x94, 0x09, 0x19, 0xC0        We can set anything, but this is the original values
            responseBuffer.Enqueue(0x94);
            responseBuffer.Enqueue(0x09);
            responseBuffer.Enqueue(0x19);
            responseBuffer.Enqueue(0xc0);

            interrupts.Enqueue(new DelayedInterrupt(0x000c4e1, INT3));

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
                        for (uint i = 0; i < size; i++) {
                            lastReadSector.Enqueue(disk[i + currentIndex]);

                        }

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
                            //interrupts.Enqueue(new DelayedInterrupt(doubleSpeed ? 0x0036cd2 : 0x006e1cd, INT1));
                        }
                        if(autoPause && m == 74) {
                            responseBuffer.Enqueue(stat);
                            interrupts.Enqueue(new DelayedInterrupt(50000, INT4));    //Data end
                            pause();
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
