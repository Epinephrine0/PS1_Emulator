using PSXEmulator.Peripherals.CDROM;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace PSXEmulator {
    public class CDROMDataController {      //Inspects and directs data/audio sectors to CPU/SPU
        public byte Padding;
        public Stack<Sector> SectorBuffer = new Stack<Sector>();
        public Queue<byte> DataFifo = new Queue<byte>();
        public uint BytesToSkip = 12;
        public uint SizeOfDataSegment = 0x800;
        public bool XA_ADPCM_En = false;
        public XAFilter Filter;
        public Volume CurrentVolume;
        public Disk Disk;
        XA_ADPCM ADPCMDecoder = new XA_ADPCM();
        public Queue<short> CDAudioSamples = new Queue<short>();
        public byte[] SelectedTrack;
        public int SelectedTrackNumber = 1;
        public byte[] LastSectorHeader = new byte[0x4];
        public byte[] LastSectorSubHeader = new byte[0x4];
        public int EndOfDisk => Disk.Tracks[Disk.Tracks.Length - 1].Start + Disk.Tracks[Disk.Tracks.Length - 1].Length; 
        public struct XAFilter {
            public bool IsEnabled;
            public byte fileNumber;
            public byte channelNumber;
        }
        public struct Volume {   //Would recive new values depending on "apply volume" register (1F801803h.Index3)  
            public byte LtoL;   //..the hardware does have some incomplete saturation support, to be checked 
            public byte LtoR;  
            public byte RtoL;
            public byte RtoR;
        }
        enum SectorType {
            Video = 1, Audio = 2, Data = 4
        }
        public CDROMDataController(string diskPath = null) {
            if (diskPath != null) {
                Disk = new Disk(diskPath);
                if (Disk.IsValid) {
                    SelectTrack(1);
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
            CurrentVolume.RtoL = 0x40;
            CurrentVolume.RtoR = 0x40;
            CurrentVolume.LtoL = 0x40;
            CurrentVolume.RtoR = 0x40;
        }
        public uint ReadWord() {
            uint b0 = DataFifo.Dequeue();
            uint b1 = DataFifo.Dequeue();
            uint b2 = DataFifo.Dequeue();
            uint b3 = DataFifo.Dequeue();
            uint word = b0 | (b1 << 8) | (b2 << 16) | (b3 << 24);
            return word;
        }
        public byte ReadByte() {
            return DataFifo.Dequeue();
        }
        public void MoveSectorToDataFifo() {
            Sector recentSector = SectorBuffer.Pop();
            for (int i = 0; i < SizeOfDataSegment; i++) {
                DataFifo.Enqueue(recentSector.data[i]);
            }
        }
        public bool LoadNewSector(int currentIndex) {  //Only for CD-XA tracks, i.e. read command, not play command
            LastSectorHeader[0] = SelectedTrack[currentIndex + 0x0C];
            LastSectorHeader[1] = SelectedTrack[currentIndex + 0x0D];
            LastSectorHeader[2] = SelectedTrack[currentIndex + 0x0E];
            LastSectorHeader[3] = SelectedTrack[currentIndex + 0x0F];
            byte fileNumber = LastSectorHeader[0] =  SelectedTrack[currentIndex + 0x10];                
            byte channelNumber = LastSectorHeader[1] = SelectedTrack[currentIndex + 0x11];
            byte subMode = LastSectorHeader[2] = SelectedTrack[currentIndex + 0x12];
            byte codingInfo = LastSectorHeader[3] = SelectedTrack[currentIndex + 0x13];

            ReadOnlySpan<byte> fullSector = new ReadOnlySpan<byte>(SelectedTrack, currentIndex, 0x930);
            SectorType sectorType = (SectorType)((subMode >> 1) & 0x7);

            if (XA_ADPCM_En && sectorType == SectorType.Audio) {

                if (Filter.IsEnabled) { 
                    if (Filter.fileNumber == fileNumber && Filter.channelNumber == channelNumber) {
                        ADPCMDecoder.handle_XA_ADPCM(fullSector, codingInfo, CurrentVolume, ref CDAudioSamples);   
                    }
                } else {
                    ADPCMDecoder.handle_XA_ADPCM(fullSector, codingInfo, CurrentVolume, ref CDAudioSamples);
                }
                return false;
            } else {
                uint size = SizeOfDataSegment;
                Span<byte> dataSpan = new Span<byte>(SelectedTrack, (int)(BytesToSkip + currentIndex), (int)size);
                Sector sector = new Sector(dataSpan.ToArray()); //To array is bad
                SectorBuffer.Push(sector);
                /*for (uint i = 0; i < size; i++) {                                                               //Data sector (or audio but disabled)
                    SectorBuffer.Enqueue(SelectedTrack[BytesToSkip + i + currentIndex]);                            //Should continue to data path
                }*/
                return true;
            }
        }
        public void PlayCDDA(int currentIndex) {   //Handles play command
            int offset = currentIndex - Disk.Tracks[SelectedTrackNumber - 1].RoundedStart;

            if ((offset + 0x930) >= SelectedTrack.Length) {
                int newTrack = FindTrack(currentIndex);
                SelectTrack(newTrack);
                offset = (currentIndex - Disk.Tracks[SelectedTrackNumber - 1].RoundedStart);
                Console.WriteLine("[CDROM] CD-DA Moving to track: " + SelectedTrackNumber);
            }

            if (offset < 0) {  
                int newTrack = FindTrack(currentIndex);
                if (newTrack == SelectedTrackNumber) {
                    Console.WriteLine("[CDROM] CD-DA Pregap!");
                    return;
                } else {
                    SelectTrack(newTrack);
                }
            }
            //Add CD-DA Samples to the queue
            ReadOnlySpan<byte> fullSector = new ReadOnlySpan<byte>(SelectedTrack).Slice(offset, 0x930);  
            
            for (int i = 0; i < fullSector.Length; i += 4) {                   //Stereo, 16-bit, at 44.1Khz ...always?
                short L = MemoryMarshal.Read<short>(fullSector.Slice(i,2));
                short R = MemoryMarshal.Read<short>(fullSector.Slice(i + 2,2));
                CDAudioSamples.Enqueue(L);   
                CDAudioSamples.Enqueue(R);    
            }
        }
        public void SelectTrack(int trackNumber) {  //Should be called on Read and Play Commands to switch Files
            SelectedTrack = File.ReadAllBytes(Disk.Tracks[trackNumber - 1].FilePath);
            SelectedTrackNumber = Disk.Tracks[trackNumber - 1].TrackNumber;
        }

        //To figure in what track does the required MSF fall in, if the track is not specified by the play command
        public int FindTrack(int index) { 
            for (int i = 0; i < Disk.Tracks.Length; i++) {
                if (index < (Disk.Tracks[i].RoundedStart + Disk.Tracks[i].Length)) {
                    return Disk.Tracks[i].TrackNumber;
                }
            }
            Console.WriteLine("Shit, couldn't find a suitable track");
            return 1;
        }
    }
   public class Sector {
        public byte[] data;
        public Sector( byte[] data) {
            this.data = data;
        }
    }
}
