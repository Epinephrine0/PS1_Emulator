using System;
using System.Windows;

namespace PSXEmulator {
    internal class Program {

        [STAThread]
        static void Main(string[] args) {

            //PSX_OpenTK emu = new PSX_OpenTK();

            Application app = new Application();
            app.Run(new UI());    //Launch UI

        }


    }
}


