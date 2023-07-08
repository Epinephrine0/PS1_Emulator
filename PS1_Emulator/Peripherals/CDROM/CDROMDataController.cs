﻿using Microsoft.VisualBasic.Logging;
using NAudio.Wave;
using OpenTK.Graphics.OpenGL;
using PSXEmulator.Peripherals.CDROM;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace PSXEmulator {
    public class CDROMDataController {      //Inspects and directs data/audio sectors to CPU/SPU
        public byte Padding;
        public Queue<byte> SectorBuffer = new Queue<byte>();
        public Queue<byte> DataFifo = new Queue<byte>();
        public uint BytesToSkip = 12;
        public uint SizeOfDataSegment = 0x800;
        public bool XA_ADPCM_En = false;
        public XAFilter Filter;
        public Volume CurrentVolume;
        public Track[] Tracks;
        XA_ADPCM ADPCMDecoder = new XA_ADPCM();
        public Queue<short> CDAudioSamples = new Queue<short>();
        public byte[] SelectedTrack;
        public int SelectedTrackNumber = 0;
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
        public CDROMDataController() {
           
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
            for (int i = 0; i < SizeOfDataSegment; i++) {
                DataFifo.Enqueue(SectorBuffer.Dequeue());
            }
        }
        public bool LoadNewSector(uint currentIndex) {  //Only for CD-XA tracks, i.e. read command, not play command
            uint offset = 0x10;
            byte fileNumber = SelectedTrack[currentIndex + offset++];            //First Subheader byte
            byte channelNumber = SelectedTrack[currentIndex + offset++];         //Second Subheader byte
            byte subMode = SelectedTrack[currentIndex + offset++];               //Third Subheader byte
            byte codingInfo = SelectedTrack[currentIndex + offset++];            //Fourth Subheader byte

            ReadOnlySpan<byte> fullSector = new ReadOnlySpan<byte>(SelectedTrack, (int)currentIndex, 0x930);
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
                for (uint i = BytesToSkip; i < BytesToSkip + SizeOfDataSegment; i++) {               //Data sector (or audio but disabled)
                    SectorBuffer.Enqueue(SelectedTrack[i + currentIndex]);                            //Should continue to data path
                }
                return true;
            }
        }
        public void PlayCDDA(uint currentIndex) {   //Handles play command
            uint offset = (uint)(currentIndex - Tracks[SelectedTrackNumber - 1].RoundedStart);

            if ((offset + 0x930) >= SelectedTrack.Length) {
                int newTrack = FindTrack(currentIndex);
                SelectTrack(newTrack);
                offset = (uint)(currentIndex - Tracks[SelectedTrackNumber - 1].RoundedStart);
                Console.WriteLine("[CDROM] CD-DA playing track: " + SelectedTrackNumber);
            }

            //Ridge Racer setloc at position BEFORE the track starting point, I igonre this
            if (((int)offset) < 0) {
                Console.WriteLine("[CDROM] CD-DA Index < Track start");    
                return;
            }
            //Add CD-DA Samples to the queue
            ReadOnlySpan<byte> fullSector = new ReadOnlySpan<byte>(SelectedTrack).Slice((int)offset, 0x930);  
            
            for (int i = 0; i < fullSector.Length; i += 4) {                   //Stereo, 16-bit, at 44.1Khz ...always?
                short L = MemoryMarshal.Read<short>(fullSector.Slice(i,2));
                short R = MemoryMarshal.Read<short>(fullSector.Slice(i + 2,2));
                CDAudioSamples.Enqueue(L);   
                CDAudioSamples.Enqueue(R);    
            }
        }
        public void SelectTrack(int trackNumber) {  //Should be called on Read and Play Commands to switch Files
            SelectedTrack = File.ReadAllBytes(Tracks[trackNumber - 1].FilePath);
            SelectedTrackNumber = Tracks[trackNumber - 1].TrackNumber;
        }

        //To figure in what track does the required MSF fall in, if the track is not specified by the play command
        public int FindTrack(uint index) { 
            for (int i = 0; i < Tracks.Length; i++) {
                if (index < (Tracks[i].RoundedStart + Tracks[i].Length)) {
                    return Tracks[i].TrackNumber;
                }
            }
            Console.WriteLine("Shit, couldn't find a suitable track");
            return 1;
        }
    }
}
