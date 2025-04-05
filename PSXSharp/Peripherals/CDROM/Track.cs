using System.IO;

namespace PSXSharp.Peripherals.CDROM {
    public class Track {
        public int TrackNumber;
        public bool IsAudioTrack;       //CD-DA
        public string FilePath;         //Direct path to binary
        public byte[] Data; 
        public string Index01MSF;       //Initial
        public int M;   //Actual
        public int S;   //Actual
        public int F;   //Actual
        public int Start => ((M * 60 * 75) + (S * 75) + F) * 0x930;
        public int RoundedStart => ((M * 60 * 75) + (S * 75)) * 0x930;  //Ignore F

        public int Length;  

        public Track(string path, bool isAudio, int trackNumber, string index1) {
            FilePath = path;
            IsAudioTrack = isAudio;
            TrackNumber = trackNumber;
            Index01MSF = index1;
            Data = File.ReadAllBytes(path); 
        }
    }
}
