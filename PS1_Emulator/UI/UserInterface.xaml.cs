using PSXEmulator.UI;
using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Forms;

namespace PSXEmulator {
    /// <summary>
    /// Interaction logic for UserInterface.xaml
    /// </summary>
    public partial class UserInterface : System.Windows.Window {
        PSX_OpenTK MainEmu;

        string[] GamesFolders; 
        string[] SelectedGameFiles;
        string SystemID = "PLAYSTATION";
        string CopyRight = "Sony Computer Entertainment Inc.";
        bool HasValidBios;  
        Settings UserSettings;  //The way I pass this object around is pretty Bullshit and needs to be changed

        public UserInterface() {
            InitializeComponent();
            Console.ForegroundColor = ConsoleColor.White;

            this.Title = "PSX Emulator";
            this.ResizeMode = ResizeMode.NoResize;

            LoadSettings();

            if (!UserSettings.HasBios) {
                loadBios();
            } 

            HasValidBios = IsValidBios(UserSettings.BIOSPath);  

            if (UserSettings.HasGames) {
                Console.WriteLine("Found game folder");
                listGames(UserSettings.GamesFolderPath);
            } else {
                Console.WriteLine("No game folder found, please import your game folder");
            }
        }

        private void LoadSettings() {
            try {
                byte[] target = File.ReadAllBytes("Settings.bin");
                UserSettings = JsonSerializer.Deserialize<Settings>(new ReadOnlySpan<byte>(target));
                Console.WriteLine("Found Settings");
            } catch (Exception ex) {
                switch (ex) {
                    case FileNotFoundException: //Not found
                    case JsonException:         //Invalid
                        Console.WriteLine("Creating new settings");
                        UserSettings = new Settings();
                        break;
                    default:
                        Console.WriteLine("Fatal: " + ex.ToString());
                        DisableAll();
                        return;
                }
            }
        }
        private void ConfigButton_Click(object sender, RoutedEventArgs e) {
            //Later
        }
        private void DisableAll() {
            GameList.IsHitTestVisible = false;
            PlayButton.IsHitTestVisible = false;   
            ConfigButton.IsHitTestVisible = false;
            ImportButton.IsHitTestVisible = false;
        }
        private void loadBios() {
            using (OpenFileDialog openFileDialog = new OpenFileDialog()) {
                openFileDialog.Title = "Select a PSX BIOS file";
                if (openFileDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK && 
                    !string.IsNullOrWhiteSpace(openFileDialog.FileName)) {
                    UserSettings.BIOSPath = openFileDialog.FileName;
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
                return true;
            } else {
                Console.WriteLine("Selected BIOS is invalid");
                return false;
            }
        }
  
        private void PlayButton_Click(object sender, RoutedEventArgs e) {

            if (!HasValidBios) {
                loadBios();
                HasValidBios = IsValidBios(UserSettings.BIOSPath);
                if (!HasValidBios) {
                    return; //Refuse to run if not valid
                }
            }

            if(GameList.SelectedIndex < 0 || GameList.SelectedItem.Equals("Games go here")) {
                Console.WriteLine("No game selected");
                Console.WriteLine("Proceeding to boot without a game");
                Boot();
                return; //No need to check games and bullshit when booting the Shell
            }

            UserSettings.SelectedGameFolder = GamesFolders[GameList.SelectedIndex];
            SelectedGameFiles = Directory.GetFiles(UserSettings.SelectedGameFolder);

            int index = findFirstValidBinary(SelectedGameFiles);    //Make sure the game folder contains at least one valid bin
            UserSettings.TrackIndex = index;

            if (index >= 0) {
                UserSettings.SelectedGameName = Path.GetFileName(SelectedGameFiles[index]);
                Console.WriteLine("Found valid binary!");
                Console.WriteLine("Booting: " + UserSettings.SelectedGameName);
            }
            else {
                Console.WriteLine("Could not find a valid binary for the selected game");
                Console.WriteLine("Proceeding to boot without a game");
            }

            Boot();
        }
        private void ImportButton_Click(object sender, RoutedEventArgs e) {
            using (var fbd = new FolderBrowserDialog()) {
                DialogResult result = fbd.ShowDialog();
                if (result == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(fbd.SelectedPath)) {
                    UserSettings.GamesFolderPath = fbd.SelectedPath;
                    Console.WriteLine("Reading games folder...");
                    listGames(UserSettings.GamesFolderPath);
                }
            }
        }
        private void listGames(string path) {
            GameList.Items.Clear();
            if (Directory.Exists(path)) {
                GamesFolders = Directory.GetDirectories(path);
                Console.WriteLine("Found " + GamesFolders.Length + " games");
                foreach (string gameFolder in GamesFolders) {
                    GameList.Items.Add(Path.GetFileName(gameFolder));
                }
            } else {
                GameList.Items.Add("Games go here");
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
        private void OnClose(object sender, EventArgs e) {
            byte[] serialized = JsonSerializer.SerializeToUtf8Bytes(UserSettings,
                 new JsonSerializerOptions { WriteIndented = false, IgnoreNullValues = false });
            File.WriteAllBytes("Settings.bin", serialized);
            Console.WriteLine("Saved settings");
        }

        private void Boot() {
            this.Visibility = Visibility.Hidden; //Hide UI

            MainEmu = new PSX_OpenTK(UserSettings);            /* Game loop */


            Console.Clear();
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("Renderer Closed");
            this.Visibility = Visibility.Visible;
        }

        private void Window_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e) {
            GameList.UnselectAll();
        }
    }
}
