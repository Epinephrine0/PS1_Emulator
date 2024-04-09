using System;
using System.Threading;
using System.Windows;

namespace PSXEmulator {
    internal class MainProgram {
        const int CONSOLE_WIDTH = 80;
        const int CONSOLE_HEIGHT = 20;

        [STAThread]
        static void Main(string[] args) {
           
            //PSX_OpenTK emu = new PSX_OpenTK();
            Console.Title = "TTY Console";
            Console.SetWindowSize(CONSOLE_WIDTH, CONSOLE_HEIGHT);
            Application app = new Application();
            app.Run(new UserInterface());    //Launch UI
            Environment.Exit(0);
        }
    }
}


