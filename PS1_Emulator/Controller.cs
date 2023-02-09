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

        public byte response(uint data) {
            if (!isConnected) {
                ack = false;
                return 0xFF;
            }

            ack = true;
            switch (sequenceNum++) {
                case 0: return 0x41;
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

    }
}
