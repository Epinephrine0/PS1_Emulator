using OpenTK.Windowing.GraphicsLibraryFramework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Forms;

namespace PSXEmulator {
    /// <summary>
    /// Interaction logic for UI.xaml
    /// </summary>
    public partial class UI : System.Windows.Window {
        PSX_OpenTK MainEmu;
        static string BiosPath;
        static string GamesPath;
        string[] GamesFolders; 
        string[] SelectedGameFiles;
        string SystemID = "PLAYSTATION";
        string CopyRight = "Sony Computer Entertainment Inc.";
        bool HasValidBios;
        public UI() {
            InitializeComponent();
            this.Title = "PSX Emulator";
            this.ResizeMode = ResizeMode.NoResize;
            try {
                BiosPath = File.ReadAllText("BIOSPath.txt");  
            }
            catch (FileNotFoundException ex) {
                loadBios();
            }
            HasValidBios = IsValidBios(BiosPath);

            try {
                GamesPath = File.ReadAllText("GamesPath.txt");
            }catch(FileNotFoundException ex) {
                GamesPath = "";
            }
            listGames(GamesPath);
            
        }
        private void loadBios() {
            using (OpenFileDialog openFileDialog = new OpenFileDialog()) {
                openFileDialog.Title = "Select a PSX BIOS file";

                if (openFileDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK && 
                    !string.IsNullOrWhiteSpace(openFileDialog.FileName)) {
                    BiosPath = openFileDialog.FileName;
                }
            }
        }
        private bool IsValidBios(string BIOSpath) {
            //All valid BIOSes should, hopefully, contain "Sony Computer Entertainment Inc." copyright string
            ReadOnlySpan<byte> data;
            try {
                data = File.ReadAllBytes(BIOSpath);
                data = data.Slice(0x108, CopyRight.Length);
            }
            catch (ArgumentOutOfRangeException ex) {
                return false;                   //Invalid file
            }
            catch (ArgumentNullException ex) {  //When Hit cancel on dialog 
                return false;
            }
            string biosString = Encoding.ASCII.GetString(data);
            if (biosString.Equals(CopyRight)) {
                Console.WriteLine("Found a valid BIOS!");
                File.WriteAllText("BIOSPath.txt", BiosPath);
                return true;
            } else {
                Console.WriteLine("Selected BIOS is invalid");
                return false;
            }
        }
        private void UpdatePathes() {
        }
        private void PlayButton_Click(object sender, RoutedEventArgs e) {
            //this.Visibility = Visibility.Hidden;
            if (!HasValidBios) {
                loadBios();
                HasValidBios = IsValidBios(BiosPath);
                if (!HasValidBios) {
                    return;
                }
            }
            if(gameList.SelectedIndex < 0 || gameList.SelectedItem.Equals("Games go here")) {
                Console.WriteLine("No game selected");
                Console.WriteLine("Proceeding to boot without a game");
                return;
            }
            SelectedGameFiles = Directory.GetFiles(GamesFolders[gameList.SelectedIndex]);
            int index = findFirstValidBinary(SelectedGameFiles);
            if (index >= 0) {
                Console.WriteLine("Found valid binary!");
                Console.WriteLine("Booting: " + Path.GetFileName(SelectedGameFiles[index]));
            }
            else {
                Console.WriteLine("Could not find a valid binary for the selected game");
                Console.WriteLine("Proceeding to boot without a game");
            }
        }
        private void ImportButton_Click(object sender, RoutedEventArgs e) {
            using (var fbd = new FolderBrowserDialog()) {
                DialogResult result = fbd.ShowDialog();
                if (result == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(fbd.SelectedPath)) {
                    GamesPath = fbd.SelectedPath;
                    Console.WriteLine("Reading games folder...");
                    listGames(GamesPath);
                    File.WriteAllText("GamesPath.txt", GamesPath);
                }
            }
        }

        private void ConfigButton_Click(object sender, RoutedEventArgs e) {
            //Later
        }
        private void listGames(string path) {
            gameList.Items.Clear();
            if (Directory.Exists(path)) {
                GamesFolders = Directory.GetDirectories(GamesPath);
                Console.WriteLine("Found " + GamesFolders.Length + " games");
                foreach (string gameFolder in GamesFolders) {
                    gameList.Items.Add(Path.GetFileName(gameFolder));
                }
            } else {
                gameList.Items.Add("Games go here");
            }
        }
        private int findFirstValidBinary(string[] selectedGameFiles) {
            ReadOnlySpan<byte> data;
            for (int i = 0; i < selectedGameFiles.Length; i++) {
                string extension = Path.GetExtension(selectedGameFiles[i]).ToLower();
                if (extension.Equals(".bin") || extension.Equals(".iso")) {     //Check PLAYSTAION String for a valid CD-XA track
                    data = File.ReadAllBytes(selectedGameFiles[i]);     //Slow
                    try {
                        data = data.Slice((16 * 0x930) + 0x20, 11);    
                    }
                    catch (ArgumentOutOfRangeException ex) {
                        continue;
                    }
                    string ID = Encoding.ASCII.GetString(data);
                    if (ID.Equals(SystemID)) {
                        return i;
                    }
                }
            }
            return -1;
        }
    }
}
