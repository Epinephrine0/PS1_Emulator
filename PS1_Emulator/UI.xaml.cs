using System.IO;
using System.Windows;

namespace PSXEmulator {
    /// <summary>
    /// Interaction logic for UI.xaml
    /// </summary>
    public partial class UI : Window {
        PSX_OpenTK mainEmu;
        string gamesFolder = "C:\\Users\\Old Snake\\Desktop\\PS1\\ROMS";
        public UI() {
            InitializeComponent();
            this.Title = "PSX Emulator";
            listGames(gamesFolder);

        }

        private void PlayButton_Click(object sender, RoutedEventArgs e) {
            //this.Visibility = Visibility.Hidden;

        }
        private void listGames(string path) {
            if (Directory.Exists(path)) {
                string[] games = Directory.GetDirectories(path);
                foreach (string game in games) {
                    gameList.Items.Add(System.IO.Path.GetFileName(game));
                }
            }
            else {
                gameList.Items.Add("Fuck");
            }
        }

    }
}
