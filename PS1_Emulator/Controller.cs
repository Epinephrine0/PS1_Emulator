using OpenTK.Windowing.GraphicsLibraryFramework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PS1_Emulator {
    public class Controller {
        public bool ack;
        public bool isConnected;
        public ushort buttons = 0xffff;
        public int sequenceNum;
        public static readonly Dictionary<int, int> dualSense_Dictionary = new Dictionary<int, int>() //This is for PS5, I need to handle having a PS4 controller 
        {
           {0, 15},      //Square
           {1, 14},      //X
           {2, 13},      //Circle
           {3, 12},      //Triangle
           {4, 10},      //L1
           {5, 11},      //R1
           {6, 8},       //L2
           {7, 9},       //R2
           {8, 0},       //Select
           {9, 3},       //Start
           {10, 1},      //L3
           {11, 2},      //R3
           {15, 4},      //Pad up
           {16, 5},      //Pad right
           {17, 6},      //Pad down
           {18, 7},      //Pad Left
        };
       /* public static readonly Dictionary<int, int> x360_Dictionary = new Dictionary<int, int>()
        {
           {2, 15},      //Square
           {0, 14},      //X
           {1, 13},      //Circle
           {3, 12},      //Triangle
           {4, 10},      //L1
           {5, 11},      //R1
           //{6, 8},       //L2   (wrong)
           //{7, 9},       //R2   (wrong)
           {6, 0},       //Select
           {7, 3},       //Start
           {8, 1},      //L3
           {9, 2},      //R3
           {10, 4},      //Pad up
           {11, 5},      //Pad right
           {12, 6},      //Pad down
           {13, 7},      //Pad Left
        };*/

        public byte response(uint data) {

            if (!isConnected) {
                ack = false;
                return 0xFF;
            }

            ack = true;
         
            switch (sequenceNum++) {
                case 0:
                    if (data == 0x43) { 
                        ack = false;
                        sequenceNum = 0;
                        return 0xFF;
                    }

                    return 0x41;
                case 1: return 0x5A;
                case 2: return (byte)(buttons & 0xff);
                case 3:
                    ack = false;
                    sequenceNum = 0;
                    return (byte)((buttons >> 8) & 0xff);
                default:
                    Console.WriteLine("Unkown sequence number for controller communication: " + sequenceNum);
                    ack = false;
                    sequenceNum = 0;
                    return 0xFF;
            }

        }
        public void readInput(JoystickState externalController) {
            if (externalController == null) { 
                isConnected = false;
                return;
            }
            else {
                isConnected = true;
            }

            for (int j = 0; j < externalController.ButtonCount; j++) {
                if (dualSense_Dictionary.ContainsKey(j)) {
                    if (externalController.IsButtonDown(j)) {
                        int bit = ~(1 << dualSense_Dictionary[j]);
                        buttons &= (ushort)(bit);
                    }
                    else {
                        int bit = (1 << dualSense_Dictionary[j]);
                        buttons |= (ushort)(bit);
                    }

                }

            }

        }


    }
}
