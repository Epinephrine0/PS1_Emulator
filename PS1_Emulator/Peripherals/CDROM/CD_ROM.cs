using PSXEmulator.Peripherals.CDROM;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Shapes;
using static PSXEmulator.CDROMDataController;

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

        //Status Register
        byte Index;                 //0-1
        byte ADPBUSY = 0;           //2
        byte PRMEMPT = 1;           //3
        byte PRMWRDY = 1;           //4
        byte RSLRRDY = 0;           //5
        byte DRQSTS = 0;            //6
        byte BUSYSTS = 0;           //7

        Queue<Byte> ResponseBuffer = new Queue<Byte>();
        Queue<Byte> ParameterBuffer = new Queue<Byte>();
        Queue<DelayedInterrupt> Interrupts = new Queue<DelayedInterrupt>();

        byte[] SeekParameters = new byte[3];

        byte IRQ_enable; //0-7
        byte IRQ_flag;  //0-7

        byte stat = 0x2;  //Motor On

        //Mode
        byte Mode;
        uint LastSize;
        bool AutoPause;  //TODO
        bool CDDAReport; //TODO

        uint M, S, F;

        public enum CDROMState {   
            Idle,
            ReadingData,
            PlayingCDDA
        }

        /* CD Audio Volume */
        byte LeftCD_toLeft_SPU_Volume;
        byte LeftCD_toRight_SPU_Volume;
        byte RightCD_toRight_SPU_Volume;
        byte RightCD_toLeft_SPU_Volume;

        bool DoubleSpeed;

        CDROMState State;

        public byte Padding;
        public byte CurrentCommand;
        public bool HasCue;
        public bool LedOpen = false;

        private delegate*<CD_ROM, void>[] LookUpTable = new delegate*<CD_ROM, void>[0xFF + 1];
        public CDROMDataController DataController;
        public Track[] CDTracks;
        uint CurrentIndex;      //Offset in bytes
        int Counter = 0;        //Delay
        uint SectorOffset = 0;  //Skip headers, etc
        Disk Disk;
        public CD_ROM(string path, bool isDirectFile) {
            DataController = new CDROMDataController();
            LoadLUT();
            if (path != null) {
                Disk = new Disk(path);
                if (Disk.IsValid) {
                    DataController.Tracks = Disk.Tracks;
                    DataController.SelectedTrack = File.ReadAllBytes(Disk.Tracks[0].FilePath);
                    HasCue = Disk.HasCue;
                } else {
                    Console.WriteLine("[CDROM] Invalid Disk! will abort and boot the Shell");
                }
                if (Disk.IsAudioDisk) {
                    Console.WriteLine("[CDROM] Audio Disk detected");
                }
            } else {
                Console.WriteLine("[CDROM] No game path provided");
                Console.WriteLine("[CDROM] Proceeding to boot without a game");
            }
        }
        public CD_ROM() {   //Overload for when booting EXEs
            //Stub for the CDROM Tests
            LoadLUT();
            Disk = new Disk(@"C:\Users\Old Snake\Desktop\Archive");
            DataController = new CDROMDataController();
            DataController.Tracks = Disk.Tracks;
            DataController.SelectedTrack = File.ReadAllBytes(Disk.Tracks[0].FilePath);
        }
        private void LoadLUT() {
            //Fill the functions lookUpTable with illegal first, to be safe
            for (int i = 0; i < LookUpTable.Length; i++) {
                LookUpTable[i] = &Illegal;
            }
            //Add whatever I implemented manually
            LookUpTable[0x01] = &GetStat;
            LookUpTable[0x02] = &Setloc;
            LookUpTable[0x03] = &Play;
            LookUpTable[0x06] = &ReadNS;
            LookUpTable[0x08] = &Stop;
            LookUpTable[0x09] = &Pause;
            LookUpTable[0x0A] = &Init;
            LookUpTable[0x0B] = &Mute;
            LookUpTable[0x0C] = &Demute;
            LookUpTable[0x0D] = &SetFilter;
            LookUpTable[0x0E] = &SetMode;
            LookUpTable[0x11] = &GetLocP;
            LookUpTable[0x13] = &GetTN;
            LookUpTable[0x14] = &GetTD;
            LookUpTable[0x15] = &SeekL;
            LookUpTable[0x16] = &SeekP;
            LookUpTable[0x19] = &Test;
            LookUpTable[0x1A] = &GetID;
            LookUpTable[0x1B] = &ReadNS;
        }
        private static (int,int,int) BytesToMSF(int totalSize) {
            int totalFrames = totalSize / 2352;
            int M = totalFrames / (60 * 75);
            int S = (totalFrames % (60 * 75)) / 75;
            int F = (totalFrames % (60 * 75)) % 75;
            return (M,S,F);
        }
        private static byte DecToBcd(byte value) {
            return (byte)(value + 6 * (value / 10));
        }
        private static int BcdToDec(byte value) {
            return value - 6 * (value >> 4);
        }
        private static void Illegal(CD_ROM cdrom) {
            throw new Exception("Unknown CDROM command: " + cdrom.CurrentCommand.ToString("x"));
        }
        public void Controller(byte command) {      
            CurrentCommand = command;
            Interrupts.Clear();
            ResponseBuffer.Clear();
            DataController.DataFifo.Clear();
            DataController.SectorBuffer.Clear();
            LookUpTable[command](this);
            ParameterBuffer.Clear();
            Console.WriteLine("[CD-ROM] Command: 0x" + command.ToString("x"));
        }
        private byte CDROM_Status() {
            DRQSTS = (byte)((DataController.DataFifo.Count > 0) ? 1 : 0);
            RSLRRDY = (byte)((ResponseBuffer.Count > 0) ? 1 : 0);
            byte status = (byte)((BUSYSTS << 7) | (DRQSTS << 6) 
                | (RSLRRDY << 5) | (PRMWRDY << 4) | (PRMEMPT << 3) | (ADPBUSY << 2) | Index);
            //Console.WriteLine("[CDROM] Reading Status: " + status.ToString("x"));
            return status;
        }

        public void StoreByte(uint address, byte value) {
            uint offset = address - range.start;
            //Console.WriteLine(value.ToString("x") + " at: " + address.ToString("x"));

            switch (offset) {
                case 0: Index = (byte)(value & 3); break; //Status register, all mirrors
                case 1:
                    switch (Index) {
                        case 0: Controller(value); break;
                        case 3: RightCD_toRight_SPU_Volume = value; break;
                        default: throw new Exception("Unknown Index (" + Index + ")" + " at CRROM command register");
                    }
                    break;

                case 2:
                    switch (Index) {
                        case 0: ParameterBuffer.Enqueue(value); break;
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
                        case 3: ApplyVolume(value); break;
                        default:  throw new Exception("Unknown Index (" + Index + ")" + " at CRROM IRQ flag register");
                    }
                    break;

                default: throw new Exception("Unhandled store at CRROM offset: " + offset + " index: " + Index);
            }
        }

        public byte LoadByte(uint address) {
            uint offset = address - range.start;
            //Console.WriteLine("Read at:" + address.ToString("x"));

            switch (offset) {
                case 0: return CDROM_Status();                          //Status register, all indexes are mirrors
                case 1:
                    byte r = ResponseBuffer.Dequeue();
                    Console.WriteLine("Response: " + r.ToString("X"));
                    return r;                //Response fifo, all indexes are mirrors
                case 2: return DataController.ReadByte();               //Data fifo, all indexes are mirrors
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
            if ((value & 0x80) != 0) {  //Request data
                if (DataController.DataFifo.Count > 0) { return; }
                DataController.MoveSectorToDataFifo();
            }
            else {
                DataController.DataFifo.Clear();
            }
        }
        private void InterruptFlagRegister(byte value) {
            IRQ_flag &= (byte)~(value & 0x1F);
            if (Interrupts.Count > 0 && Interrupts.Peek().delay <= 0) {
                IRQ_flag |= Interrupts.Dequeue().interrupt;
            }
            if (((value >> 6) & 1) == 1) {
                ParameterBuffer.Clear();
            }
        }
        private void ApplyVolume(byte value) {
            bool isMute = (value & 1) != 0;
            bool applyVolume = ((value >> 5) & 1) != 0;
            if (isMute) {
                DataController.CurrentVolume.LtoL = 0;
                DataController.CurrentVolume.LtoR = 0;
                DataController.CurrentVolume.RtoL = 0;
                DataController.CurrentVolume.RtoR = 0;
            }else if (applyVolume) {
                DataController.CurrentVolume.LtoL = LeftCD_toLeft_SPU_Volume;
                DataController.CurrentVolume.LtoR = LeftCD_toRight_SPU_Volume;
                DataController.CurrentVolume.RtoL = RightCD_toLeft_SPU_Volume;
                DataController.CurrentVolume.RtoR = RightCD_toRight_SPU_Volume;
            }
        }
        private static void Test(CD_ROM cdrom) {
            byte parameter = cdrom.ParameterBuffer.Dequeue();
            switch (parameter) {
                case 0x20: GetDateAndVersion(cdrom); break;
                default: throw new Exception("[CDROM] Test command: unknown parameter: " + parameter.ToString("x"));
            }
        }
        private static void GetLocP(CD_ROM cdrom) { //Subchannel Q ?
            //GetlocP - Command 11h - INT3(track,index,mm,ss,sect,amm,ass,asect) all BCD

            byte track = DecToBcd((byte)cdrom.DataController.SelectedTrackNumber);
            byte index = DecToBcd(0x01);    //Usually 01, stub temporarily (I will probably forget about it lol)
            byte mm = DecToBcd((byte)(cdrom.M - cdrom.DataController.Tracks[cdrom.DataController.SelectedTrackNumber - 1].M));
            byte ss = DecToBcd((byte)(cdrom.S - cdrom.DataController.Tracks[cdrom.DataController.SelectedTrackNumber - 1].S));
            byte ff = DecToBcd((byte)(cdrom.F - cdrom.DataController.Tracks[cdrom.DataController.SelectedTrackNumber - 1].F));
            byte amm = DecToBcd((byte)cdrom.M); //Relative to whole disk
            byte ass = DecToBcd((byte)cdrom.S);
            byte aff = DecToBcd((byte)cdrom.F);
            cdrom.ResponseBuffer.Enqueue(track);
            cdrom.ResponseBuffer.Enqueue(index);
            cdrom.ResponseBuffer.Enqueue(mm);
            cdrom.ResponseBuffer.Enqueue(ss);
            cdrom.ResponseBuffer.Enqueue(ff);
            cdrom.ResponseBuffer.Enqueue(amm);
            cdrom.ResponseBuffer.Enqueue(ass);
            cdrom.ResponseBuffer.Enqueue(aff);
            cdrom.Interrupts.Enqueue(new DelayedInterrupt(0x000c4e1, INT3));
        }
        private static void SetFilter(CD_ROM cdrom) {
            cdrom.DataController.Filter.fileNumber = cdrom.ParameterBuffer.Dequeue();
            cdrom.DataController.Filter.channelNumber = cdrom.ParameterBuffer.Dequeue();

            cdrom.ResponseBuffer.Enqueue(cdrom.stat);
            cdrom.Interrupts.Enqueue(new DelayedInterrupt(0x000c4e1, INT3));
        }
        private static void SeekP(CD_ROM cdrom) {
            cdrom.State = CDROMState.Idle;

            cdrom.CurrentIndex = ((cdrom.M * 60 * 75) + (cdrom.S * 75) + cdrom.F - 150) * 0x930;
            cdrom.stat = 0x42; //Seek

            //Response 1
            cdrom.ResponseBuffer.Enqueue(cdrom.stat);
            cdrom.Interrupts.Enqueue(new DelayedInterrupt(0x000c4e1, INT3));

            //Response 2
            cdrom.stat = (byte)(cdrom.stat & (~0x40));
            cdrom.ResponseBuffer.Enqueue(cdrom.stat);
            cdrom.Interrupts.Enqueue(new DelayedInterrupt(0x0004a00, INT2));
        }

        private static void Play(CD_ROM cdrom) {   //CD-DA
            cdrom.State = CDROMState.PlayingCDDA;

            cdrom.CurrentIndex = ((cdrom.M * 60 * 75) + (cdrom.S * 75) + cdrom.F) * 0x930;      //No 150 offset?  
            
            cdrom.stat = 0x2;           
            cdrom.stat |= (1 << 7);   //Playing CDDA

            //Response 1
            cdrom.ResponseBuffer.Enqueue(cdrom.stat);
            cdrom.Interrupts.Enqueue(new DelayedInterrupt(0x000c4e1, INT3));

            if (cdrom.ParameterBuffer.Count > 0 && cdrom.ParameterBuffer.Peek() > 0) {
                int trackNumber = cdrom.ParameterBuffer.Dequeue();
                if (cdrom.DataController.SelectedTrackNumber != trackNumber) {
                    cdrom.DataController.SelectTrack(trackNumber);      //Change Binary to trackNumber (if it isn't already selected)
                }
                cdrom.CurrentIndex = (uint)cdrom.DataController.Tracks[cdrom.DataController.SelectedTrackNumber - 1].Start; //Start at the beginning
                Console.WriteLine("[CDROM] Play CD-DA, track: " + trackNumber);
            } else {
                Console.WriteLine("[CDROM] Play CD-DA, no track specified, at MSF: " + cdrom.M + ":" + cdrom.S + ":" + cdrom.F);
            }
        }
        private static void Stop(CD_ROM cdrom) {
            //The first response returns the current status (this already with bit5 cleared)
            //The second response returns the new status (with bit1 cleared)
            cdrom.State = CDROMState.Idle;
            cdrom.stat = 0x2;
            cdrom.ResponseBuffer.Enqueue(cdrom.stat);
            cdrom.Interrupts.Enqueue(new DelayedInterrupt(0x0013cce, INT3));

            cdrom.stat = 0x0;
            cdrom.ResponseBuffer.Enqueue(cdrom.stat);

            cdrom.Interrupts.Enqueue(new DelayedInterrupt(cdrom.DoubleSpeed ? 0x18a6076 : 0x0d38aca, INT2));
            //TODO: Timing for stop while stopped? stop while paused? 
        }

        private static void GetTD(CD_ROM cdrom) {
            //GetTD - Command 14h,track --> INT3(stat,mm,ss) ;BCD
            /*For a disk with NN tracks, parameter values 01h..NNh return the start of the specified track, 
             *parameter value 00h returns the end of the last track, and parameter values bigger than NNh return error code 10h.*/

            cdrom.ResponseBuffer.Enqueue(cdrom.stat);

            int N = BcdToDec(cdrom.ParameterBuffer.Dequeue());
            int last = cdrom.DataController.Tracks.Length - 1;
            if (N >= 1 && N <= cdrom.DataController.Tracks[last].TrackNumber) { // 01h..NNh
       
                //We only care about M and S
                cdrom.ResponseBuffer.Enqueue(DecToBcd((byte)cdrom.DataController.Tracks[N - 1].M));
                cdrom.ResponseBuffer.Enqueue(DecToBcd((byte)cdrom.DataController.Tracks[N - 1].S));
                Console.WriteLine("[CDROM] GETTD: Track " + N + ", Response: " + cdrom.DataController.Tracks[N - 1].M + ":" + cdrom.DataController.Tracks[N - 1].S);

            } else if (N == 0) {
                //Returns the end of the last track (it's start, which is comulitave, + it's length)

                (int M, int S, int F) = BytesToMSF(cdrom.DataController.Tracks[last].Length);

                M += cdrom.DataController.Tracks[last].M;
                S += cdrom.DataController.Tracks[last].S;

                cdrom.ResponseBuffer.Enqueue((byte)M);
                cdrom.ResponseBuffer.Enqueue((byte)S);

                Console.WriteLine("[CDROM] GETTD: Track 0, Response: " + M + ":" + S);

            }else if (N > cdrom.DataController.Tracks[last].TrackNumber) {
                Console.WriteLine("[CDROM] Unhandled GETTD: Track " + N + " > " + cdrom.DataController.Tracks[last].TrackNumber);
            }

            cdrom.Interrupts.Enqueue(new DelayedInterrupt(0x0013cce, INT3));
        }
        private static void GetTN(CD_ROM cdrom) {
            //GetTN - Command 13h --> INT3(stat,first,last) ;BCD

            int lastIndex = cdrom.DataController.Tracks.Length - 1;
            byte firstTrack = DecToBcd((byte)cdrom.DataController.Tracks[0].TrackNumber);
            byte lastTrack = DecToBcd((byte)cdrom.DataController.Tracks[lastIndex].TrackNumber);

            cdrom.ResponseBuffer.Enqueue(cdrom.stat);
            cdrom.ResponseBuffer.Enqueue(firstTrack);
            cdrom.ResponseBuffer.Enqueue(lastTrack);
            cdrom.Interrupts.Enqueue(new DelayedInterrupt(0x0013cce, INT3));

            Console.WriteLine("[CDROM] GETTN, Response (BCD): " + firstTrack + " and " + lastTrack);
        }
        private static void Mute(CD_ROM cdrom) {
            //Response 1
            cdrom.ResponseBuffer.Enqueue(cdrom.stat);
            cdrom.Interrupts.Enqueue(new DelayedInterrupt(0x0013cce, INT3));
        }

        private static void Demute(CD_ROM cdrom) {
            //Response 1
            cdrom.ResponseBuffer.Enqueue(cdrom.stat);
            cdrom.Interrupts.Enqueue(new DelayedInterrupt(0x0013cce, INT3));
        }

        private static void Pause(CD_ROM cdrom) {
            cdrom.State = CDROMState.Idle;

            //Response 1
            cdrom.ResponseBuffer.Enqueue(cdrom.stat);
            cdrom.Interrupts.Enqueue(new DelayedInterrupt(0x0013cce, INT3));

            //Response 2
            cdrom.stat = 0x2;
            cdrom.ResponseBuffer.Enqueue(cdrom.stat);

            cdrom.Interrupts.Enqueue(new DelayedInterrupt(cdrom.DoubleSpeed ? 0x010bd93 : 0x021181c, INT2));
            //Todo: Pause while paused timings?
        }
        private static void Init(CD_ROM cdrom) {
            //Multiple effects at once. Sets mode=00h (or not ALL bits cleared?), activates drive motor, Standby, abort all commands.
            cdrom.Mode = 0;         //Reset other stuff?
            cdrom.stat = 0x2;

            //Response 1
            cdrom.ResponseBuffer.Enqueue(cdrom.stat);
            cdrom.Interrupts.Enqueue(new DelayedInterrupt(0x0013cce, INT3));

            //Response 2
            cdrom.ResponseBuffer.Enqueue(cdrom.stat);
            cdrom.Interrupts.Enqueue(new DelayedInterrupt(0x0004a00, INT2));
        }

        private static void ReadNS(CD_ROM cdrom) {
            cdrom.State = CDROMState.ReadingData;

            cdrom.CurrentIndex = ((cdrom.M * 60 * 75) + (cdrom.S * 75) + cdrom.F - 150) * 0x930;

            cdrom.stat = 0x2;   
            cdrom.stat |= 0x20; //Read

            //Response 1
            cdrom.ResponseBuffer.Enqueue(cdrom.stat);
            cdrom.Interrupts.Enqueue(new DelayedInterrupt(0x000c4e1, INT3));

            //Further responses [INT1] are added in tick() 

            if (cdrom.DataController.SelectedTrackNumber != 1) {
                cdrom.DataController.SelectTrack(1);      //Change Binary to main (only if it isn't already on main)
            }
        }

        private static void SetMode(CD_ROM cdrom) {        
            cdrom.Mode = cdrom.ParameterBuffer.Dequeue();

            if (((cdrom.Mode >> 4) & 1) == 0) {
                if (((cdrom.Mode >> 5) & 1) == 0) {
                    cdrom.LastSize = 0x800;
                    cdrom.SectorOffset = 24;
                }
                else {
                    cdrom.LastSize = 0x924;
                    cdrom.SectorOffset = 12;
                }
            }

            //Test
            cdrom.DataController.BytesToSkip = cdrom.SectorOffset;
            cdrom.DataController.SizeOfDataSegment = cdrom.LastSize;
            cdrom.DataController.Filter.IsEnabled = ((cdrom.Mode >> 3) & 1) != 0;

            cdrom.DoubleSpeed = ((cdrom.Mode >> 7) & 1) != 0;
            cdrom.AutoPause = ((cdrom.Mode >> 1) & 1) != 0; //For audio play only
            cdrom.CDDAReport = ((cdrom.Mode >> 2) & 1) != 0; //For audio play only

            cdrom.DataController.XA_ADPCM_En = ((cdrom.Mode >> 6) & 1) != 0; //(0=Off, 1=Send XA-ADPCM sectors to SPU Audio Input)

            //Response 1
            cdrom.ResponseBuffer.Enqueue(cdrom.stat);
            cdrom.Interrupts.Enqueue(new DelayedInterrupt(0x000c4e1, INT3));
        }
        private static void SeekL(CD_ROM cdrom) {
            cdrom.State = CDROMState.Idle;

            cdrom.CurrentIndex = ((cdrom.M * 60 * 75) + (cdrom.S * 75) + cdrom.F - 150) * 0x930;

            cdrom.stat = 0x42; //Seek

            //Response 1
            cdrom.ResponseBuffer.Enqueue(cdrom.stat);
            cdrom.Interrupts.Enqueue(new DelayedInterrupt(0x000c4e1, INT3));

            //Response 2
            cdrom.stat = (byte)(cdrom.stat & (~0x40));
            cdrom.ResponseBuffer.Enqueue(cdrom.stat);
            cdrom.Interrupts.Enqueue(new DelayedInterrupt(0x0004a00, INT2));
        }
        private static void Setloc(CD_ROM cdrom) {
            cdrom.SeekParameters[0] = cdrom.ParameterBuffer.Dequeue();  //Minutes
            cdrom.SeekParameters[1] = cdrom.ParameterBuffer.Dequeue();  //Seconds 
            cdrom.SeekParameters[2] = cdrom.ParameterBuffer.Dequeue();  //Sectors (Frames)

            cdrom.M = (uint)((cdrom.SeekParameters[0] & 0xF) * 1 + ((cdrom.SeekParameters[0] >> 4) & 0xF) * 10);
            cdrom.S = (uint)((cdrom.SeekParameters[1] & 0xF) * 1 + ((cdrom.SeekParameters[1] >> 4) & 0xF) * 10);
            cdrom.F = (uint)((cdrom.SeekParameters[2] & 0xF) * 1 + ((cdrom.SeekParameters[2] >> 4) & 0xF) * 10);

            cdrom.ResponseBuffer.Enqueue(cdrom.stat);
            cdrom.Interrupts.Enqueue(new DelayedInterrupt(0x000c4e1, INT3));
            //Console.WriteLine("[CDROM] Setloc:" + cdrom.M.ToString().PadLeft(2,'0') + ":" + cdrom.S.ToString().PadLeft(2, '0') + ":" + cdrom.F.ToString().PadLeft(2, '0'));
        }

        private static void GetID(CD_ROM cdrom) {          
            cdrom.ResponseBuffer.Enqueue(cdrom.stat);
            cdrom.Interrupts.Enqueue(new DelayedInterrupt(0x000c4e1, INT3));
            if (cdrom.Disk != null && cdrom.Disk.IsValid) {
                if (cdrom.Disk.IsAudioDisk) {
                    cdrom.Interrupts.Enqueue(new DelayedInterrupt(0x0004a00, INT5));
                    cdrom.ResponseBuffer.Enqueue(0x0A);
                    cdrom.ResponseBuffer.Enqueue(0x90);
                    cdrom.ResponseBuffer.Enqueue(0x00);
                    cdrom.ResponseBuffer.Enqueue(0x00);
                    cdrom.ResponseBuffer.Enqueue(0x00);
                    cdrom.ResponseBuffer.Enqueue(0x00);
                    cdrom.ResponseBuffer.Enqueue(0x00);
                    cdrom.ResponseBuffer.Enqueue(0x00);
                } else {
                    cdrom.Interrupts.Enqueue(new DelayedInterrupt(0x0004a00, INT2));
                    cdrom.ResponseBuffer.Enqueue(0x02);       //STAT
                    cdrom.ResponseBuffer.Enqueue(0x00);       //Flags (Licensed, Missing, Audio or Data CD) 
                    cdrom.ResponseBuffer.Enqueue(0x20);       //Disk type 
                    cdrom.ResponseBuffer.Enqueue(0x00);       //Usually 0x00
                    cdrom.ResponseBuffer.Enqueue(0x53);       //From here and down it is ASCII, this is "SCEA"
                    cdrom.ResponseBuffer.Enqueue(0x43);
                    cdrom.ResponseBuffer.Enqueue(0x45);
                    cdrom.ResponseBuffer.Enqueue(0x41);
                    
                }
            } else {
                cdrom.Interrupts.Enqueue(new DelayedInterrupt(0x0004a00, INT5));
                cdrom.ResponseBuffer.Enqueue(0x08); //No disk inserted
                cdrom.ResponseBuffer.Enqueue(0x40);
                cdrom.ResponseBuffer.Enqueue(0x00);
                cdrom.ResponseBuffer.Enqueue(0x00);
                cdrom.ResponseBuffer.Enqueue(0x00);
                cdrom.ResponseBuffer.Enqueue(0x00);
                cdrom.ResponseBuffer.Enqueue(0x00);
                cdrom.ResponseBuffer.Enqueue(0x00);
            }
        }
        private static void GetStat(CD_ROM cdrom) {
            if (!cdrom.LedOpen) {     //Reset shell open unless it is still opened, TODO: add disk swap
                cdrom.stat = (byte)(cdrom.stat & (~0x18));
                cdrom.stat |= 0x2;
            }
            cdrom.Interrupts.Enqueue(new DelayedInterrupt(0x000c4e1, INT3));
            cdrom.ResponseBuffer.Enqueue(cdrom.stat);
        }

        public static void GetDateAndVersion(CD_ROM cdrom) {
            //0x94, 0x09, 0x19, 0xC0
            //We can set anything, but these are the original values
            cdrom.ResponseBuffer.Enqueue(0x94);
            cdrom.ResponseBuffer.Enqueue(0x09);
            cdrom.ResponseBuffer.Enqueue(0x19);
            cdrom.ResponseBuffer.Enqueue(0xc0);

            cdrom.Interrupts.Enqueue(new DelayedInterrupt(0x000c4e1, INT3));
        }
        void IncrementIndex(byte offset) {
            F++;
            if (F >= 75) {
                F = 0;
                S++;
            }
            if (S >= 60) {
                S = 0;
                M++;
            }
            CurrentIndex = ((M * 60 * 75) + (S * 75) + F - offset) * 0x930; 
        }

        internal void tick(int cycles) {
            Counter += cycles;

            if (Interrupts.Count > 0) {
                Interrupts.Peek().delay -= cycles;
            }
            if (Interrupts.Count > 0 && IRQ_flag == 0 && Interrupts.Peek().delay <= 0) {    //Make sure the previous INT has been ACKed, INTs are queued not ORed
                IRQ_flag |= Interrupts.Dequeue().interrupt;
                if ((IRQ_enable & IRQ_flag) != 0) {
                    IRQ_CONTROL.IRQsignal(2);
                    //Console.WriteLine("[CDROM] IRQ!");
                }
            }

            switch (State) {
                case CDROMState.Idle:
                    Counter = 0;
                    return;

                case CDROMState.ReadingData:
                    if (Counter < (33868800 / (DoubleSpeed ? 150 : 75)) || Interrupts.Count != 0) {
                        return;
                    }
                    Counter = 0;
                    //Console.WriteLine("[CDROM] Data Read at " + M.ToString().PadLeft(2,'0') + ":" + S.ToString().PadLeft(2, '0') + ":" + F.ToString().PadLeft(2, '0'));
                    bool sendToCPU = DataController.LoadNewSector(CurrentIndex);
                    IncrementIndex(150);
                    if (sendToCPU) {
                        ResponseBuffer.Enqueue(stat);
                        if (M < 74) {
                            Interrupts.Enqueue(new DelayedInterrupt(DoubleSpeed ? 0x0036cd2 : 0x006e1cd, INT1));
                        } else {
                            Interrupts.Enqueue(new DelayedInterrupt(50000, INT4));    //Data end, but what should be the index?
                        }
                    }
                    break;

                case CDROMState.PlayingCDDA:
                    if (Counter < (33868800 / (DoubleSpeed ? 150 : 75)) || Interrupts.Count != 0) {
                        return;
                    }
                    Counter = 0;

                    if (HasCue) {
                        DataController.PlayCDDA(CurrentIndex);
                    } else {
                        Console.WriteLine("[CD-ROM] Ignoring play command (No cue)");
                    }
                    IncrementIndex(0);

                    if (CDDAReport) {
                        ResponseBuffer.Enqueue(0xFF);   //Garbage Report 
                        Interrupts.Enqueue(new DelayedInterrupt(DoubleSpeed ? 0x0036cd2 : 0x006e1cd, INT1));
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
