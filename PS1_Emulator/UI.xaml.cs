using SixLabors.ImageSharp.Formats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

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
