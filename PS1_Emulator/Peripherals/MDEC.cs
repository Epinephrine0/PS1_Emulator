using System;
using System.Collections.Generic;
using System.Linq;

namespace PSXEmulator.Peripherals {
    public class MDEC { //Macroblock Decoder

        public Range range = new Range(0x1F801820, 5);

        //Status Register
        const uint InitialStatus = 0x80040000;

        uint DataOutFifoEmpty;   //31    Data-Out Fifo Empty (0=No, 1=Empty)
        uint DataInFifoFull;     //30    Data-In Fifo Full   (0=No, 1=Full, or Last word received)
        uint CommandBusy;        //29    Command Busy  (0=Ready, 1=Busy receiving or processing parameters)
        uint DataInRequest;      //28    Data-In Request  (set when DMA0 enabled and ready to receive data)
        uint DataOutRequest;     //27    Data-Out Request (set when DMA1 enabled and ready to send data)
        uint DataOutputDepth;    //26-25 Data Output Depth  (0=4bit, 1=8bit, 2=24bit, 3=15bit)      ;CMD.28-27
        uint DataOutputSigned;   //24    Data Output Signed (0=Unsigned, 1=Signed)                  ;CMD.26
        uint DataOutputBit15;    //23    Data Output Bit15  (0=Clear, 1=Set) (for 15bit depth only) ;CMD.25
                                 //22-19 Not used (seems to be always zero)
        uint CurrentBlock;       //18-16 Current Block (0..3=Y1..Y4, 4=Cr, 5=Cb) (or for mono: always 4=Y)     
        ushort WordsRemaining;   //15-0  Number of Parameter Words remaining minus 1  (FFFFh=None)  ;CMD.Bit0-15

        //Control
        uint DataInRequestEnabled;
        uint DataOutRequestEnabled;

        //Store required tables, could use arrays with so many pointers maybe
        private List<byte> LuminanceQuantTable = new List<byte>(64);
        private List<byte> ColorQuantTable = new List<byte>(64);
        private List<short> ScaleTable = new List<short>(64);

        public Queue<ushort> Compresssed = new Queue<ushort>();
        public short[][] blk = {
            new short[64], new short[64], new short[64],
            new short[64], new short[64], new short[64]
        };
        short[] dst = new short[64];

        //Final Colors
        public List<byte> OutputBuffer = new List<byte>();

        private int[] ZigZag = {
            0 ,1 ,5 ,6 ,14,15,27,28,
            2 ,4 ,7 ,13,16,26,29,42,
            3 ,8 ,12,17,25,30,41,43,
            9 ,11,18,24,31,40,44,53,
            10,19,23,32,39,45,52,54,
            20,22,33,38,46,51,55,60,
            21,34,37,47,50,56,59,61,
            35,36,48,49,57,58,62,63
        };

        public enum MDECState {
            Idle,
            LoadingLuminanceQuantTable,
            LoadingLuminanceAndColorQuantTable,
            LoadingScaleTable,
            LoadingData
        }
        MDECState CurrentState = MDECState.Idle;

        public MDEC() {
            WriteStatus(InitialStatus);   //Reset
        }
        public uint read(uint address) {
            uint offset = address - range.start;
            switch (offset) {
                case 0:
                    Console.WriteLine("\n[MDEC] Reading Data Non-DMA");
                    return ReadCurrentMacroblock();              //Response/Data read

                case 4:
                    //Console.WriteLine("[MDEC] Reading Status!");

                    return ReadStatus();    //Status read
                default: throw new Exception("Unknown MDEC Read port: " + offset.ToString("x") + " - Full address: " + address.ToString("x"));
            }
        }
        public void write(uint address, uint value) {
            uint offset = address - range.start;
            switch (offset) {
                case 0: CommandAndParameters(value); break;
                case 4: WriteControl(value); break;
                default: throw new Exception("Unknown MDEC Write port: " + offset.ToString("x") + " - Full address: " + address.ToString("x"));
            }
        }
        private uint ReadStatus() {
            uint status = 0;
            status |= DataOutFifoEmpty << 31;
            status |= DataInFifoFull << 30;
            status |= CommandBusy << 29;
            status |= (DataInRequest & DataInRequestEnabled) << 28;
            status |= (DataOutRequest & DataOutRequestEnabled) << 27;
            status |= DataOutputDepth << 25;
            status |= DataOutputSigned << 24;
            status |= DataOutputBit15 << 23;
            status |= CurrentBlock << 16;
            status |= WordsRemaining;
            return status;
        }
        private void WriteStatus(uint value) {
            DataOutFifoEmpty = (value >> 31) & 1;
            DataInFifoFull = (value >> 30) & 1;
            CommandBusy = (value >> 29) & 1;
            DataInRequest = (value >> 28) & 1;
            DataOutRequest = (value >> 27) & 1;
            DataOutputDepth = (value >> 25) & 0x3;
            DataOutputSigned = (value >> 24) & 1;
            DataOutputBit15 = (value >> 23) & 1;
            CurrentBlock = (value >> 16) & 0x7;
            WordsRemaining = (ushort)(value & 0xF);
        }
        private void WriteControl(uint value) {
            /*  31    Reset MDEC (0=No change, 1=Abort any command, and set status=80040000h)
                30    Enable Data-In Request  (0=Disable, 1=Enable DMA0 and Status.bit28)
                29    Enable Data-Out Request (0=Disable, 1=Enable DMA1 and Status.bit27)
                28-0  Unknown/Not used - usually zero */
            bool reset = ((value >> 31) & 1) == 1;
            DataInRequestEnabled = (value >> 30) & 1;
            DataOutRequestEnabled = (value >> 29) & 1;
            if (reset) {
                Console.WriteLine("\n[MDEC] Reset!");
                WriteStatus(InitialStatus);   //Reset
                LuminanceQuantTable.Clear();
                ColorQuantTable.Clear();
                ScaleTable.Clear();
                CurrentState = MDECState.Idle;
            }
        }
        public void CommandAndParameters(uint value) { //Find a better name for this thing
            switch (CurrentState) {
                case MDECState.LoadingScaleTable:
                    //WordsRemaining -= 2;
                    ScaleTable.AddRange(new short[] { (short)value, (short)(value >> 16) });
                    if (ScaleTable.Count == 64) {
                        Console.WriteLine("\n[MDEC] Finished loading Scale Table!");
                        CurrentState = MDECState.Idle;
                    }
                    return;

                case MDECState.LoadingLuminanceQuantTable:
                    //WordsRemaining -= 4;
                    LuminanceQuantTable.AddRange(new byte[] { (byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24) });

                    if (LuminanceQuantTable.Count == 64) {
                        Console.WriteLine("\n[MDEC] Finished loading Luminance Quant Table!");
                        CurrentState = MDECState.Idle;
                    }
                    return;
                case MDECState.LoadingLuminanceAndColorQuantTable:
                    //WordsRemaining -= 4;

                    if (LuminanceQuantTable.Count < 64) {
                        LuminanceQuantTable.AddRange(new byte[] { (byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24) });
                        return;
                    } else if (ColorQuantTable.Count < 64) {
                        ColorQuantTable.AddRange(new byte[] { (byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24) });
                        return;
                    } else {
                        //WordsRemaining = 0xFFFF;
                        Console.WriteLine("\n[MDEC] Finished loading both Tables!, R: " + WordsRemaining.ToString("x"));
                        CurrentState = MDECState.Idle;
                        break;  //Break out of the switch instead of returning 
                    }

                case MDECState.LoadingData:
                    Compresssed.Enqueue((ushort)value);
                    Compresssed.Enqueue((ushort)(value >> 16));
                    Console.WriteLine("Enq: " + value.ToString("x") + " Size: " + Compresssed.Count);
                    WordsRemaining--;

                    if (WordsRemaining == 0xFFFF) {
                        Console.WriteLine("\nLoaded Compressed Data!");
                        CurrentState = MDECState.Idle;
                        DataInRequest = 0;
                        DataInFifoFull = 1;
                        DataOutRequest = 1;
                        DataOutFifoEmpty = 0;
                        CommandBusy = 0;
                        CurrentBlock = 0;

                        if (DataOutputDepth > 1) {
                            Console.WriteLine("\n15/24 bit");
                            

                        } else {
                            while(Compresssed.Count > 0) {
                                rl_decode_block(ref blk[CurrentBlock], ref Compresssed, LuminanceQuantTable);
                                if (k < 64) {
                                    return;     //Incomplete block
                                }
                                idct_core(ref blk[CurrentBlock]);
                                y_to_mono(ref blk[CurrentBlock], DataOutputSigned == 1);
                                CurrentBlock = (CurrentBlock + 1) % 6;
                            }
                        }
                    }
                    return;
            }

            //If we reach here then the value isn't a parameter of anything
            //Decode and Execute
            ExecuteCommand(value);
        }

        public uint ReadCurrentMacroblock() {
            uint data = (uint)(OutputBuffer[0] | (OutputBuffer[1] << 8) | (OutputBuffer[2] << 16) | (OutputBuffer[3] << 24));
            OutputBuffer.RemoveRange(0,4);

            //if (WithinBlockIndex >= 64) { WithinBlockIndex = 0; BlkNumber++; }
            //if (BlkNumber >= 6) { BlkNumber = 0; WithinBlockIndex = 0; DataOutFifoEmpty = 1; Console.WriteLine("Reached Last Block"); }

            return data;
        }

        private void y_to_mono(ref short[] yblk, bool IsSigned) {
            short y;
            int signedY;

            for (int i = 0; i < 64; i++) {
                y = yblk[i];
                y &= 0x1FF;
                signedY = signed9Bits((int)y);
                signedY = Math.Clamp(signedY, -128, 127);
                if (!IsSigned) {
                    signedY += 128;
                }
                OutputBuffer.Add((byte)signedY);
            }
        }

        private void yuv_to_rgb(ref uint[] cblks, bool IsSigned, int xx, int yy) {
            double R,G,B,C_Y;   //Idk what to call Y, there is already small y
            ushort outR,outG,outB;

            for (int y = 0; y <= 7; y++) {
                for (int x = 0; x <= 7; x++) {
                    R = blk[0][((x + xx) / 2) + ((y + yy) / 2) * 8];
                    B = blk[1][((x + xx) / 2) + ((y + yy) / 2) * 8];
                    G = (-0.3437 * B) + (-0.7143 * R); R = (1.402 * R); B = (1.772 * B);
                    C_Y = cblks[(x) + (y) * 8];
                    outR = (ushort)Math.Clamp(C_Y + R,- 128, 127);
                    outG = (ushort)Math.Clamp(C_Y + G, -128, 127);
                    outB = (ushort)Math.Clamp(C_Y + B, -128, 127);
                    if (!IsSigned) {
                        outR ^= 0x80; 
                        outG ^= 0x80;
                        outB ^= 0x80;
                        //Output[(x + xx) + (y + yy) * 16] = ((uint)outB << 16) | ((uint)outG << 8) | outR;
                    }
                }
            }
        }
        ushort k = 64;
        ushort n;
        ushort q_scale;
        int val;

        private void rl_decode_block(ref short[] blk, ref Queue<ushort> src, List<byte> qt) {   //Passed by refrence so that changes affect the actual input

            if(k >= blk.Length) {
                for (int i = 0; i < blk.Length; i++) { blk[i] = 0; }
                k = 0;
                n = src.Dequeue();
                q_scale = (ushort)((n >> 10) & 0x3F);
                val = signed10bits(signed10bits((n & 0x3FF) * qt[k]) & 0x3FF);
                k = (ushort)(k + ((n >> 10) & 0x3F) + 1);
            }

            while(src.Count > 0 && src.Peek() == 0xFE00) { src.Dequeue(); }
            if (src.Count == 0) { return; }

         
            Console.WriteLine("First:" + n.ToString("x") + " --- K: " + k);
            while (k < 64) {
                if (q_scale == 0) { val = signed10bits(n & 0x3FF) * 2; }
                val = Math.Clamp(val, -0x400, +0x3FF);
                if (q_scale > 0) { blk[ZigZag[k]] = (short)val; }
                if (q_scale == 0) { blk[k] = (short)val; }
                if(src.Count == 0) { return; }
                n = src.Dequeue();

                k = (ushort)(k + ((n >> 10) & 0x3F) + 1);

                Console.WriteLine("Next:" + n.ToString("x") + " --- K: " + k);

                val = (signed10bits(n & 0x3FF) * qt[k & 0x3F] * q_scale + 4) / 8;
            }
        }

        private void idct_core(ref short[] src) {

            int sum;

            for (int pass = 0; pass < 2; pass++) { 
                for (int x = 0; x <= 7; x++) {
                    for (int y = 0; y <= 7; y++) {
                        sum = 0;
                        for (int z = 0; z <= 7; z++) {
                            sum = (int)(sum + ((int)src[y + z * 8]) * (ScaleTable[x + z * 8] / 8));
                        }
                        dst[x + y * 8] = (short)((sum + 0x0fff) / 0x2000);
                    }
                }
                (src, dst) = (dst, src);
            }
        }

        private int signed10bits(int val) {
            return (val << 22) >> 22;
        }

        private int signed9Bits(int val) {
            return (val << 23) >> 23;
        }

        private void ExecuteCommand(uint value) {
            uint opcode = value >> 29;
            switch (opcode) {
                case 0x01: DecodeMacroblock(value); break;
                case 0x02: SetQuantTable(value); break;
                case 0x03: SetScaleTable(value); break;

                case 0x00:
                case 0x04:
                case 0x05:
                case 0x06:
                case 0x07:
                    Nop(value); break;

                default: throw new Exception("Unknown MDEC Command: 0x" + opcode.ToString("x"));
            }
        }
        private void Nop(uint value) {
            /* This command has no function. Command bits 25-28 are reflected to Status bits 23-26 as usually. 
            Command bits 0-15 are reflected to Status bits 0-15 (similar as the "number of parameter words" for MDEC(1),
            but without the "minus 1" effect, and without actually expecting any parameters). */
            Console.WriteLine("\n[MDEC] NOP");
            DataOutputBit15 = (value >> 25) & 1;
            DataOutputSigned = (value >> 26) & 1;
            DataOutputDepth = (value >> 27) & 0x3;
            //WordsRemaining = (ushort)(value & 0xFFFF);
        }
        private void SetQuantTable(uint command) {
            LuminanceQuantTable.Clear();
            bool loadingBoth = ((command & 1) == 1);
            if (loadingBoth) {
                Console.WriteLine("\n[MDEC] Loading Both Tables");
                CurrentState = MDECState.LoadingLuminanceAndColorQuantTable;
                ColorQuantTable.Clear();

            } else {
                Console.WriteLine("\n[MDEC] Loading Luminance Quant Table ");
                CurrentState = MDECState.LoadingLuminanceQuantTable;
            }
            //Bit25-28 are copied to STAT.23-26
            DataOutputBit15 = (command >> 23) & 1;
            DataOutputSigned = (command >> 24) & 1;
            DataOutputDepth = (command >> 25) & 0x3;
            //WordsRemaining = (ushort)((64 << ((int)command & 1)) - 1);
        }
        private void SetScaleTable(uint command) {
            ScaleTable.Clear();
            //Bit25-28 are copied to STAT.23-26
            DataOutputBit15 = (command >> 23) & 1;
            DataOutputSigned = (command >> 24) & 1;
            DataOutputDepth = (command >> 25) & 0x3;
            //WordsRemaining = 64 - 1;
            CurrentState = MDECState.LoadingScaleTable;
        }
        private void DecodeMacroblock(uint value) {
            uint depth = (value >> 27) & 0x3;
            uint signed = (value >> 26) & 1;
            uint outBit15 = (value >> 25) & 1;
            ushort numberOfParameters = ((ushort)(value & 0xFFFF));

            this.DataOutputDepth = depth;
            this.DataOutputSigned = signed;
            this.DataOutputBit15 = outBit15;
            this.WordsRemaining = (ushort)(numberOfParameters - 1);

            CurrentState = MDECState.LoadingData;

            DataInRequest = 1;
            DataOutFifoEmpty = 0;
            DataInFifoFull = 0;
            CommandBusy = 1;

            for (int i = 0; i < blk.Length; i++) {
                for (int j = 0; j < 64; j++) {
                    blk[i][j] = 0;
                }
            }

            Console.WriteLine("\nDecode Macroblock -> " + value.ToString("x"));
            Console.WriteLine("Data Output Depth: " + depth);
            Console.WriteLine("Data Output Sign: " + signed);
            Console.WriteLine("Data Output Bit15: " + outBit15);
            Console.WriteLine("Number of parameters: " + numberOfParameters);

        }
    }
}
