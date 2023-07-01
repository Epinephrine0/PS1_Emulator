using NAudio.Wave;
using System;
using System.Collections.Generic;

namespace PSXEmulator {
    public class CDROMDataController {      //Inspects and directs data/audio sectors to CPU/SPU
        public byte padding;
        public Queue<byte> sectorQueue = new Queue<byte>();
        public Queue<byte> dataFifo = new Queue<byte>();
        public uint bytesToSkip = 12;
        public uint sizeOfDataSegment = 0x800;
        public bool XA_ADPCM_En = false;
        public Filter filter;
        public Volume currentVolume;
        byte[] disk;
        XA_ADPCM adpcmDecoder = new XA_ADPCM();
        public Queue<short> CD_AudioSamples = new Queue<short>();   
        
        public struct Filter {
            public bool isEnabled;
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
        public CDROMDataController(ref byte[] disk) {
            this.disk = disk;
            currentVolume.RtoL = 0x40;
            currentVolume.RtoR = 0x40;
            currentVolume.LtoL = 0x40;
            currentVolume.RtoR = 0x40;
        }
        public uint readWord() {
            uint b0 = dataFifo.Dequeue();
            uint b1 = dataFifo.Dequeue();
            uint b2 = dataFifo.Dequeue();
            uint b3 = dataFifo.Dequeue();
            uint word = b0 | (b1 << 8) | (b2 << 16) | (b3 << 24);
            return word;
        }
        public byte readByte() {
            return dataFifo.Dequeue();
        }
        public void moveSectorToDataFifo() {
            for (int i = 0; i < sizeOfDataSegment; i++) {
                dataFifo.Enqueue(sectorQueue.Dequeue());
            }
        }
        public bool loadNewSector(uint currentIndex) {
            uint offset = 0x10;
            byte fileNumber = disk[currentIndex + offset++];            //First Subheader byte
            byte channelNumber = disk[currentIndex + offset++];         //Second Subheader byte
            byte subMode = disk[currentIndex + offset++];               //Third Subheader byte
            byte codingInfo = disk[currentIndex + offset++];            //Fourth Subheader byte

            ReadOnlySpan<byte> fullSector = new ReadOnlySpan<byte>(disk, (int)currentIndex, 0x930);
            SectorType sectorType = (SectorType)((subMode >> 1) & 0x7);

            if (XA_ADPCM_En && sectorType == SectorType.Audio) {

                if (filter.isEnabled) { 
                    if (filter.fileNumber == fileNumber && filter.channelNumber == channelNumber) {
                        adpcmDecoder.handle_XA_ADPCM(fullSector, codingInfo, currentVolume, ref CD_AudioSamples);   
                    }
                } else {
                    adpcmDecoder.handle_XA_ADPCM(fullSector, codingInfo, currentVolume, ref CD_AudioSamples);
                }
                return false;
            } else {
                for (uint i = bytesToSkip; i < bytesToSkip + sizeOfDataSegment; i++) {      //Data sector (or audio but disabled)
                    sectorQueue.Enqueue(disk[i + currentIndex]);                            //Should continue to data path
                }
                return true;
            }
        }
    }
}
