using System;

namespace PSXEmulator.UI {
    [Serializable]  
    public class Settings { 
        public string? BIOSPath { set; get; }
        public string? GamesFolderPath { set; get; }

        public bool HasBios => BIOSPath != null;
        public bool HasGames => GamesFolderPath != null;

        public string? SelectedGameName { set; get; }
        public string? SelectedGameFolder { set; get; }

        public int FirstTrackIndex = -1;

        public bool isEXE;
        public string? EXEPath;

        //More settings later

        //Show Textures
        //Show Console
        //CPI
        //Sound stuff
        //Fast boot

    }
}
