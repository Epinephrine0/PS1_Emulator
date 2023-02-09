using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;

namespace PS1_Emulator {
    public class MemoryCard {       
        byte[] data;
        byte FLAG = 0x8;
        byte MSB_address;
        byte LSB_address;
        ushort address;
        char accessType = 'X';
        int sequenceNumber;
        byte CHK;
        public bool ack;

       


        public MemoryCard(byte[] data) {

            this.data = data;
         


        }


        internal byte response(uint command) {
            sequenceNumber++;
            ack = true;
            //Console.WriteLine("SEQ: " + (sequenceNumber) + " Command: " + command.ToString("x"));

            switch (sequenceNumber) {
                case 1:
                    switch (accessType) {
                        case 'X':
                            accessType = (char)command;     //1 Setting accsess type 'R', 'W', 'S'
                            return FLAG;


                        default:

                            throw new Exception("Unknown access type (check PSX-SPX for this case)");
                    }

                default:


                    switch (accessType) {
                        case 'R':

                            return readingPath(command);

                        case 'W':

                            return writingPath(command);

                        case 'S':

                            return idPath(command);

                        default: throw new Exception("Unknown access type: " + accessType);
                    }




            }



        }

        private byte idPath(uint command) {

            switch (sequenceNumber) {

                case 2: return 0x5A;    //Receive Memory Card ID1
                case 3: return 0x5D;    //Receive Memory Card ID2
                case 4: return 0x5C;    //Receive Command Acknowledge 1 
                case 5: return 0x5D;    //Receive Command Acknowledge 2 
                case 6: return 0x04;    //Receive 04h
                case 7: return 0x00;    //Receive 00h
                case 8: return 0x00;    //Receive 00h
                case 9:
                    reset();
                    return 0x80;        //Receive 80h

                default:

                    throw new Exception("Unkown sequence number: " + sequenceNumber);
            }


        }
        private byte readingPath(uint command) {


            switch (sequenceNumber) {

                case 2: return 0x5A;    //Receive Memory Card ID1
                case 3: return 0x5D;    //Receive Memory Card ID2

                case 4:
                    MSB_address = (byte)(command);        //Send Address MSB  ;\sector number (0..3FFh)
                    return 0x0;

                case 5:
                    LSB_address = (byte)command;       //Send Address LSB  
                    CHK = (byte)(MSB_address ^ LSB_address);
                    address = (ushort)(MSB_address << 8 | LSB_address);

                    if (address > 0x3FF) {
                        Console.Write("[MEMORYCARD] Wrong read address! : " + address.ToString("x"));
                        FLAG |= 0x4;                //Error
                        address &= 0x3FF;
                         
                    }

                    //Console.Write(" Reading Sector: " + address.ToString("x") + "  ");
                    return 0x0;
                
                case 6: return 0x5C;                   //Receive Command Acknowledge 1  ;<-- late /ACK after this byte-pair
                case 7: return 0x5D;                   //Receive Command Acknowledge 2
                case 8: return MSB_address;            //Receive Confirmed Address MSB
                case 9: return LSB_address;            //Receive Confirmed Address LSB

                case int num when (sequenceNumber >= 10 && sequenceNumber < 10 + 128):
                    byte dataSector = data[(address * 128) + sequenceNumber - 10];

                    CHK ^= dataSector;
                    return dataSector;

                case 10 + 128: return CHK;              //Receive Checksum (MSB xor LSB xor Data bytes)
                case 11 + 128:
                    reset();
                    //Console.WriteLine("\n [MEMORYCARD] DONE READING!");

                    return 0x47;                        //Receive Memory End Byte (should be always 47h="G"=Good for Read)

                default:

                    throw new Exception("Unkown sequence number: " + sequenceNumber);
            }


        }

        private byte writingPath(uint command) {

            switch (sequenceNumber) {

                case 2: return 0x5A;    //Receive Memory Card ID1
                case 3: return 0x5D;    //Receive Memory Card ID2

                case 4:
                    MSB_address = (byte)(command);        //Send Address MSB  ;\sector number (0..3FFh)
                    return 0x0;

                case 5:
                    LSB_address = (byte)command;       //Send Address LSB  
                    CHK = (byte)(MSB_address ^ LSB_address);
                    address = (ushort)((MSB_address) << 8 | LSB_address);

                    if (address > 0x3FF) {
                        Console.Write("[MEMORYCARD] Wrong write address! : " + address.ToString("x"));
                        FLAG |= 0x4;                //Error
                        address &= 0x3ff;
                    }
                    return 0x0;


                case int num when (sequenceNumber >= 6 && sequenceNumber < 134):

                    data[(address*128) + sequenceNumber - 6] = (byte)command;
                    CHK = (byte)(CHK ^ (byte)command);

                    return 0x0;                         //Don't care

                case 134:                           //Receive Checksum (MSB xor LSB xor Data bytes)
                   
                    if (CHK != (byte)command) {
                        FLAG |= 0x4;
                        Console.Write("[MEMORYCARD] Wrong CHK! : " + CHK.ToString("x") + " (Expected: " + command.ToString("x") + ")");

                    };
                    return 0x0;                         //Don't care
                 

                case 135: return 0x5C;              //Receive Command Acknowledge 1
                case 136: return 0x5D;              //Receive Command Acknowledge 2

                case 137:                         //Receive Memory End Byte (47h=Good, 4Eh=BadChecksum, FFh=BadSector)

                    writeToMemoryCard();
                    reset();

                    return 0x47;


                default:

                    throw new Exception("Unkown sequence number: " + sequenceNumber);
            }



        }

        private void writeToMemoryCard() {

            File.WriteAllBytes("memoryCard.mcd",data);
            FLAG  = (byte)(FLAG & (~0x8));
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("[MEMORYCARD] SAVED!");
            Console.ForegroundColor = ConsoleColor.Green;

        }
        public void reset() {
            sequenceNumber = 0;
            accessType = 'X';
            ack = false;
            CHK = 0;
        }

    }
}
