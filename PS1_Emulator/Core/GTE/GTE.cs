using System;

namespace PSXEmulator {
    internal class GTE {

        public Command currentCommand;

        private readonly byte[] unr_table = {
            0xFF, 0xFD, 0xFB, 0xF9, 0xF7, 0xF5, 0xF3, 0xF1, 0xEF, 0xEE, 0xEC, 0xEA, 0xE8, 0xE6, 0xE4, 0xE3,
            0xE1, 0xDF, 0xDD, 0xDC, 0xDA, 0xD8, 0xD6, 0xD5, 0xD3, 0xD1, 0xD0, 0xCE, 0xCD, 0xCB, 0xC9, 0xC8,
            0xC6, 0xC5, 0xC3, 0xC1, 0xC0, 0xBE, 0xBD, 0xBB, 0xBA, 0xB8, 0xB7, 0xB5, 0xB4, 0xB2, 0xB1, 0xB0,
            0xAE, 0xAD, 0xAB, 0xAA, 0xA9, 0xA7, 0xA6, 0xA4, 0xA3, 0xA2, 0xA0, 0x9F, 0x9E, 0x9C, 0x9B, 0x9A,
            0x99, 0x97, 0x96, 0x95, 0x94, 0x92, 0x91, 0x90, 0x8F, 0x8D, 0x8C, 0x8B, 0x8A, 0x89, 0x87, 0x86,
            0x85, 0x84, 0x83, 0x82, 0x81, 0x7F, 0x7E, 0x7D, 0x7C, 0x7B, 0x7A, 0x79, 0x78, 0x77, 0x75, 0x74,
            0x73, 0x72, 0x71, 0x70, 0x6F, 0x6E, 0x6D, 0x6C, 0x6B, 0x6A, 0x69, 0x68, 0x67, 0x66, 0x65, 0x64,
            0x63, 0x62, 0x61, 0x60, 0x5F, 0x5E, 0x5D, 0x5D, 0x5C, 0x5B, 0x5A, 0x59, 0x58, 0x57, 0x56, 0x55,
            0x54, 0x53, 0x53, 0x52, 0x51, 0x50, 0x4F, 0x4E, 0x4D, 0x4D, 0x4C, 0x4B, 0x4A, 0x49, 0x48, 0x48,
            0x47, 0x46, 0x45, 0x44, 0x43, 0x43, 0x42, 0x41, 0x40, 0x3F, 0x3F, 0x3E, 0x3D, 0x3C, 0x3C, 0x3B,
            0x3A, 0x39, 0x39, 0x38, 0x37, 0x36, 0x36, 0x35, 0x34, 0x33, 0x33, 0x32, 0x31, 0x31, 0x30, 0x2F,
            0x2E, 0x2E, 0x2D, 0x2C, 0x2C, 0x2B, 0x2A, 0x2A, 0x29, 0x28, 0x28, 0x27, 0x26, 0x26, 0x25, 0x24,
            0x24, 0x23, 0x22, 0x22, 0x21, 0x20, 0x20, 0x1F, 0x1E, 0x1E, 0x1D, 0x1D, 0x1C, 0x1B, 0x1B, 0x1A,
            0x19, 0x19, 0x18, 0x18, 0x17, 0x16, 0x16, 0x15, 0x15, 0x14, 0x14, 0x13, 0x12, 0x12, 0x11, 0x11,
            0x10, 0x0F, 0x0F, 0x0E, 0x0E, 0x0D, 0x0D, 0x0C, 0x0C, 0x0B, 0x0A, 0x0A, 0x09, 0x09, 0x08, 0x08,
            0x07, 0x07, 0x06, 0x06, 0x05, 0x05, 0x04, 0x04, 0x03, 0x03, 0x02, 0x02, 0x01, 0x01, 0x00, 0x00,
            0x00
        };

        private Vector3[] V = new Vector3[3];
        private Vector3[] S = new Vector3[4];
        private int[] TR = new int[3];
        private int[] OF = new int[2];

        Matrix3 RT = new Matrix3();
        Matrix3 LLM = new Matrix3();
        Matrix3 LCM = new Matrix3();

        private uint RGBC;
        private ushort OTZ;
        private short[] IR = new short[4];
        private uint[] Color = new uint[3];
        private int[] BK_Color = new int[3];
        private int[] far_Color = new int[3];
        private int[] MAC = new int[4];
        private ushort IRGB, ORGB;
        private int LZCS, LZCR;
        private ushort H;
        private short DQA;
        private int DQB;
        private short ZSF3, ZSF4;
        private uint RES1;
        private uint FLAG;

        bool lm;
        uint sf;
        uint mx;
        uint vx;
        uint tx;

        public GTE() {
            for (int i = 0; i < 3; i++) {
                V[i] = new Vector3();
                S[i] = new Vector3();
            }
            S[3] = new Vector3();
        }

        public uint read(uint rt) {

            switch (rt) {
                case 0:  return V[0].XY;
                case 1:  return (uint)V[0].Z;
                case 2:  return V[1].XY;
                case 3:  return (uint)V[1].Z;
                case 4:  return V[2].XY;
                case 5:  return (uint)V[2].Z;
                case 6:  return RGBC;
                case 7:  return OTZ;
                case 8:  return (uint)IR[0];
                case 9:  return (uint)IR[1];
                case 10: return (uint)IR[2];
                case 11: return (uint)IR[3];
                case 12: return S[0].XY;
                case 13: return S[1].XY;
                case 14:
                case 15: return S[2].XY;
                case 16: return (ushort)S[0].Z;
                case 17: return (ushort)S[1].Z;
                case 18: return (ushort)S[2].Z;
                case 19: return (ushort)S[3].Z;
                case 20: return Color[0];
                case 21: return Color[1];
                case 22: return Color[2];
                case 23: return RES1;
                case 24: return (uint)MAC[0];
                case 25: return (uint)MAC[1];
                case 26: return (uint)MAC[2];
                case 27: return (uint)MAC[3];
                case 28:
                case 29: return (uint)((Math.Clamp(IR[1] / 0x80, 0, +0x1F)) | (Math.Clamp(IR[2] / 0x80, 0, +0x1F)) << 5 
                        | ((Math.Clamp(IR[3] / 0x80, 0, +0x1F)) << 10));

                case 30: return (uint)LZCS;
                case 31: return countSignBit((uint)LZCS);
                case 32: return ((ushort)RT.GetElement(1, 1)) | ((uint)RT.GetElement(1, 2) << 16);
                case 33: return ((ushort)RT.GetElement(1, 3)) | ((uint)RT.GetElement(2, 1) << 16);
                case 34: return ((ushort)RT.GetElement(2, 2)) | ((uint)RT.GetElement(2, 3) << 16);
                case 35: return ((ushort)RT.GetElement(3, 1)) | ((uint)RT.GetElement(3, 2) << 16);
                case 36: return (uint)RT.GetElement(3, 3);
                case 37: return (uint)TR[0];
                case 38: return (uint)TR[1];
                case 39: return (uint)TR[2];
                case 40: return ((ushort)LLM.GetElement(1, 1)) | ((uint)LLM.GetElement(1, 2) << 16);
                case 41: return ((ushort)LLM.GetElement(1, 3)) | ((uint)LLM.GetElement(2, 1) << 16); 
                case 42: return ((ushort)LLM.GetElement(2, 2)) | ((uint)LLM.GetElement(2, 3) << 16);
                case 43: return ((ushort)LLM.GetElement(3, 1)) | ((uint)LLM.GetElement(3, 2) << 16); 
                case 44: return (uint)LLM.GetElement(3, 3);
                case 45: return (uint)BK_Color[0];
                case 46: return (uint)BK_Color[1];
                case 47: return (uint)BK_Color[2];
                case 48: return ((ushort)LCM.GetElement(1, 1)) | ((uint)LCM.GetElement(1, 2) << 16);
                case 49: return ((ushort)LCM.GetElement(1, 3)) | ((uint)LCM.GetElement(2, 1) << 16); 
                case 50: return ((ushort)LCM.GetElement(2, 2)) | ((uint)LCM.GetElement(2, 3) << 16); 
                case 51: return ((ushort)LCM.GetElement(3, 1)) | ((uint)LCM.GetElement(3, 2) << 16); 
                case 52: return (uint)LCM.GetElement(3, 3);
                case 53: return (uint)far_Color[0];
                case 54: return (uint)far_Color[1];
                case 55: return (uint)far_Color[2];
                case 56: return (uint)OF[0];
                case 57: return (uint)OF[1];
                case 58: return (uint)(short)H;
                case 59: return (uint)DQA;
                case 60: return (uint)DQB;
                case 61: return (uint)ZSF3;
                case 62: return (uint)ZSF4;
                case 63: return flagRegister();
            }

            throw new Exception("We should not reach here");

        }
        
        internal void write(uint rd, uint value) {
           
            switch (rd) {
                case 0:  V[0].XY = value; break;
                case 1:  V[0].Z = (short)value; break;
                case 2:  V[1].XY = value; break;
                case 3:  V[1].Z = (short)value; break;
                case 4:  V[2].XY = value; break;
                case 5:  V[2].Z = (short)value; break;
                case 6:  RGBC = value; break;
                case 7:  OTZ = (ushort)value; break;
                case 8:  IR[0] = (short)value; break;
                case 9:  IR[1] = (short)value; break;
                case 10: IR[2] = (short)value; break;
                case 11: IR[3] = (short)value; break;
                case 12: S[0].XY = value; break;
                case 13: S[1].XY = value; break;
                case 14: S[2].XY = value; break;
                
                case 15:
                    S[3].XY = value;
                    S[0].XY = S[1].XY;
                    S[1].XY = S[2].XY;
                    S[2].XY = value;
                    break;

                case 16: S[0].Z = (short)value; break;
                case 17: S[1].Z = (short)value; break;
                case 18: S[2].Z = (short)value; break;
                case 19: S[3].Z = (short)value; break;
                case 20: Color[0] = value; break;
                case 21: Color[1] = value; break;
                case 22: Color[2] = value; break;
                case 23: RES1 = value; break;
                case 24: MAC[0] = (int)value; break;
                case 25: MAC[1] = (int)value; break;
                case 26: MAC[2] = (int)value; break;
                case 27: MAC[3] = (int)value; break;

                case 28:
                    IR[1] = (short)((value & 0x1f) * 0x80);
                    IR[2] = (short)(((value >> 5) & 0x1f) * 0x80);
                    IR[3] = (short)(((value >> 10) & 0x1f) * 0x80);
                    break;

                case 29: ORGB = (ushort)value; break;
                case 30: LZCS = (int)value; break;
                case 31: LZCR = (int)value; break;

                case 32:
                    RT.SetElement(1, 1, (short)value);
                    RT.SetElement(1, 2, (short)(value >> 16));
                    break;

                case 33:
                    RT.SetElement(1, 3, (short)value);
                    RT.SetElement(2, 1, (short)(value >> 16));
                    break;

                case 34:
                    RT.SetElement(2, 2, (short)value);
                    RT.SetElement(2, 3, (short)(value >> 16));
                    break;

                case 35:
                    RT.SetElement(3, 1, (short)value);
                    RT.SetElement(3, 2, (short)(value >> 16));
                    break;

                case 36: RT.SetElement(3, 3, (short)value); break;
                case 37: TR[0] = (int)value; break;
                case 38: TR[1] = (int)value; break;
                case 39: TR[2] = (int)value; break;

                case 40:
                    LLM.SetElement(1, 1, (short)value);
                    LLM.SetElement(1, 2, (short)(value >> 16));
                    break;

                case 41:
                    LLM.SetElement(1, 3, (short)value);
                    LLM.SetElement(2, 1, (short)(value >> 16));
                    break;

                case 42:
                    LLM.SetElement(2, 2, (short)value);
                    LLM.SetElement(2, 3, (short)(value >> 16));
                    break;

                case 43:
                    LLM.SetElement(3, 1, (short)value);
                    LLM.SetElement(3, 2, (short)(value >> 16));
                    break;

                case 44: LLM.SetElement(3, 3, (short)value); break;
                case 45: BK_Color[0] = (int)value; break;
                case 46: BK_Color[1] = (int)value; break;
                case 47: BK_Color[2] = (int)value; break;

                case 48:
                    LCM.SetElement(1, 1, (short)value);
                    LCM.SetElement(1, 2, (short)(value >> 16));
                    break;

                case 49:
                    LCM.SetElement(1, 3, (short)value);
                    LCM.SetElement(2, 1, (short)(value >> 16));
                    break;

                case 50:
                    LCM.SetElement(2, 2, (short)value);
                    LCM.SetElement(2, 3, (short)(value >> 16));
                    break;

                case 51:
                    LCM.SetElement(3, 1, (short)value);
                    LCM.SetElement(3, 2, (short)(value >> 16));
                    break;

                case 52: LCM.SetElement(3, 3, (short)value); break;
                case 53: far_Color[0] = (int)value; break;
                case 54: far_Color[1] = (int)value; break;
                case 55: far_Color[2] = (int)value; break;
                case 56: OF[0] = (int)value; break;
                case 57: OF[1] = (int)value; break;
                case 58: H = (ushort)value; break;
                case 59: DQA = (short)value; break;
                case 60: DQB = (int)value; break;
                case 61: ZSF3 = (short)value; break;
                case 62: ZSF4 = (short)value; break;
                case 63: FLAG = value; break;

                default: throw new Exception("we should not reach here");
            }



        }
        private uint flagRegister() {

            bool Bits30_to_23_HaveErrors = ((FLAG >> 23) & 0XFF) != 0;
            bool Bits18_to_13_HaveErrors = ((FLAG >> 13) & 0X3F) != 0;

            if (Bits30_to_23_HaveErrors || Bits18_to_13_HaveErrors) {
                FLAG = (uint)(FLAG | (1 << 31));
            }
            else {
                FLAG = (uint)(FLAG & (0x7FFFFFFF));
            }

            return FLAG & 0xFFFFF000;
        }
        private uint countSignBit(uint value) {

            uint leadingBit = ((value >> 31) & 1);
            uint counter = 0;

            while ((value & 0x80000000) >> 31 == leadingBit && counter < 32) {
                value = value << 1;
                counter++;
            }

            return counter;
        }
        public void execute(Instruction currentCommand) {
            uint opcode = currentCommand.Get_Subfunction();        //0-5
            FLAG = 0;

            sf = (uint)((currentCommand.Getfull() >> 19) & 1);
            lm = (currentCommand.Getfull() >> 10 & 1) == 1;
            tx = currentCommand.Getfull() >> 13 & 3;
            vx = currentCommand.Getfull() >> 15 & 3;
            mx = currentCommand.Getfull() >> 17 & 3;

            switch (opcode) {
                case 0x01:
                    RTPS(0, true);
                    CPU.Cycles += 15;
                    break;

                case 0x10:
                    DPCS(false);
                    CPU.Cycles += 8;
                    break;

                case 0x30:
                    RTPS(0, false);
                    RTPS(1, false);
                    RTPS(2, true);
                    CPU.Cycles += 23;
                    break;

                case 0x6:
                    NCLIP();
                    CPU.Cycles += 8;
                    break;

                case 0x11:
                    INTPL();
                    CPU.Cycles += 8;
                    break;

                case 0x12:
                    MVMVA(mx, vx, tx);
                    CPU.Cycles += 8;
                    break;

                case 0x13:
                    NCDS(0);
                    CPU.Cycles += 19;
                    break;

                case 0x14:
                    CDP();
                    CPU.Cycles += 13;
                    break;

                case 0x16:
                    NCDS(0);
                    NCDS(1);
                    NCDS(2);
                    CPU.Cycles += 44;
                    break;

                case 0x2D:
                    AVSZ3();
                    CPU.Cycles += 5;
                    break;

                case 0x2E:
                    AVSZ4();
                    CPU.Cycles += 6;
                    break;


                case 0xC:
                    OP();
                    CPU.Cycles += 6;
                    break;

                case 0x1B:
                    NCCS(0);
                    CPU.Cycles += 17;
                    break;

                case 0x1C:
                    CC();
                    CPU.Cycles += 11;
                    break;

                case 0x1E:
                    NCS(0);
                    CPU.Cycles += 14;
                    break;

                case 0x20:
                    NCS(0);
                    NCS(1);
                    NCS(2);
                    CPU.Cycles += 30;
                    break;

                case 0x28:
                    SQR();
                    CPU.Cycles += 5;
                    break;

                case 0x29:
                    DCPL();
                    CPU.Cycles += 8;
                    break;

                case 0x2A:
                    DPCS(true);
                    DPCS(true);
                    DPCS(true);
                    CPU.Cycles += 17;
                    break;

                case 0x3D:
                    GPF();
                    CPU.Cycles += 5;
                    break;

                case 0x3E:
                    GPL();
                    CPU.Cycles += 5;
                    break;

                case 0x3F:
                    NCCS(0);
                    NCCS(1);
                    NCCS(2);
                    CPU.Cycles += 39;
                    break;

                default: throw new Exception("Unimplemented GTE Opcode: " + opcode.ToString("x"));
            }
        }
        private void GPL() {
            /*
             * [MAC1,MAC2,MAC3] = [0,0,0]                            ;<--- for GPF only
               [MAC1,MAC2,MAC3] = [MAC1,MAC2,MAC3] SHL (sf*12)       ;<--- for GPL only
               [MAC1,MAC2,MAC3] = (([IR1,IR2,IR3] * IR0) + [MAC1,MAC2,MAC3]) SAR (sf*12)
               Color FIFO = [MAC1/16,MAC2/16,MAC3/16,CODE], [IR1,IR2,IR3] = [MAC1,MAC2,MAC3]
             * 
             */

            //Need to keep the long, as casting to int here gives wrong values in the final result

            long mac1 = (long)MAC[1] << (int)sf * 12; 
            long mac2 = (long)MAC[2] << (int)sf * 12;
            long mac3 = (long)MAC[3] << (int)sf * 12;

            MAC[1] = (int)(MAC_Check(1, IR[1] * IR[0] + mac1) >> (int)sf * 12);
            MAC[2] = (int)(MAC_Check(2, IR[2] * IR[0] + mac2) >> (int)sf * 12);
            MAC[3] = (int)(MAC_Check(3, IR[3] * IR[0] + mac3) >> (int)sf * 12);


            IR[1] = IR_Check(1, Math.Clamp(MAC[1], lm ? 0 : -0x8000, +0x7FFF), MAC[1]);
            IR[2] = IR_Check(2, Math.Clamp(MAC[2], lm ? 0 : -0x8000, +0x7FFF), MAC[2]);
            IR[3] = IR_Check(3, Math.Clamp(MAC[3], lm ? 0 : -0x8000, +0x7FFF), MAC[3]);

            Color[0] = Color[1];
            Color[1] = Color[2];
            Color[2] = RGB_Check(1, MAC[1] >> 4) | RGB_Check(2, MAC[2] >> 4) << 8 | RGB_Check(3, MAC[3] >> 4) << 16 | ((uint)(RGBC >> 24)) << 24;
        }



        private void GPF() {
            /*
             * [MAC1,MAC2,MAC3] = [0,0,0]                            ;<--- for GPF only
               [MAC1,MAC2,MAC3] = [MAC1,MAC2,MAC3] SHL (sf*12)       ;<--- for GPL only
               [MAC1,MAC2,MAC3] = (([IR1,IR2,IR3] * IR0) + [MAC1,MAC2,MAC3]) SAR (sf*12)
               Color FIFO = [MAC1/16,MAC2/16,MAC3/16,CODE], [IR1,IR2,IR3] = [MAC1,MAC2,MAC3]
             * 
             */


            MAC[1] = MAC[2] = MAC[3] = 0;

            MAC[1] = (int)MAC_Check(1, (long)IR[1] * IR[0] + MAC[1]) >> (int)sf * 12;
            MAC[2] = (int)MAC_Check(2, (long)IR[2] * IR[0] + MAC[2]) >> (int)sf * 12;
            MAC[3] = (int)MAC_Check(3, (long)IR[3] * IR[0] + MAC[3]) >> (int)sf * 12;


            IR[1] = IR_Check(1, Math.Clamp(MAC[1], lm ? 0 : -0x8000, +0x7FFF), MAC[1]);
            IR[2] = IR_Check(2, Math.Clamp(MAC[2], lm ? 0 : -0x8000, +0x7FFF), MAC[2]);
            IR[3] = IR_Check(3, Math.Clamp(MAC[3], lm ? 0 : -0x8000, +0x7FFF), MAC[3]);

            Color[0] = Color[1];
            Color[1] = Color[2];
            Color[2] = RGB_Check(1, MAC[1] >> 4) | RGB_Check(2, MAC[2] >> 4) << 8 | RGB_Check(3, MAC[3] >> 4) << 16 | ((uint)(RGBC >> 24)) << 24;

        }

        private void DCPL() {

            MAC[1] = (int)MAC_Check(1, ((byte)RGBC * (long)IR[1])) << 4;
            MAC[2] = (int)MAC_Check(2, ((byte)(RGBC >> 8) * (long)IR[2])) << 4;
            MAC[3] = (int)MAC_Check(3, ((byte)(RGBC >> 16) * (long)IR[3]) << 4);

            interpolateColor(MAC[1], MAC[2], MAC[3]);

            Color[0] = Color[1];
            Color[1] = Color[2];
            Color[2] = RGB_Check(1, MAC[1] >> 4) | RGB_Check(2, MAC[2] >> 4) << 8 | RGB_Check(3, MAC[3] >> 4) << 16 | ((uint)(RGBC >> 24)) << 24;
        }

        private void SQR() {
            /*
                 [MAC1,MAC2,MAC3] = [IR1*IR1,IR2*IR2,IR3*IR3] SHR (sf*12)
                  [IR1,IR2,IR3]    = [MAC1,MAC2,MAC3]    ;IR1,IR2,IR3 saturated to max 7FFFh
             */

            MAC[1] = (int)(MAC_Check(1, (long)IR[1] * IR[1]) >> (int)sf * 12);
            MAC[2] = (int)(MAC_Check(2, (long)IR[2] * IR[2]) >> (int)sf * 12);
            MAC[3] = (int)(MAC_Check(3, (long)IR[3] * IR[3]) >> (int)sf * 12);

            IR[1] = IR_Check(1, Math.Clamp(MAC[1], 0, +0x7FFF), MAC[1]);
            IR[2] = IR_Check(2, Math.Clamp(MAC[2], 0, +0x7FFF), MAC[2]);
            IR[3] = IR_Check(3, Math.Clamp(MAC[3], 0, +0x7FFF), MAC[3]);


        }

        private void NCS(int r) {
            MAC[1] = ((int)(MAC_Check(1, ((long)LLM.GetElement(1, 1) * V[r].X +
               (long)LLM.GetElement(1, 2) * V[r].Y +
               (long)LLM.GetElement(1, 3) * V[r].Z)) >> (int)sf * 12));


            MAC[2] = ((int)(MAC_Check(2, ((long)LLM.GetElement(2, 1) * V[r].X +
                    (long)LLM.GetElement(2, 2) * V[r].Y +
                    (long)LLM.GetElement(2, 3) * V[r].Z)) >> (int)sf * 12));


            MAC[3] = ((int)(MAC_Check(3, ((long)LLM.GetElement(3, 1) * V[r].X +
                    (long)LLM.GetElement(3, 2) * V[r].Y +
                    (long)LLM.GetElement(3, 3) * V[r].Z)) >> (int)sf * 12));



            IR[1] = IR_Check(1, Math.Clamp(MAC[1], lm ? 0 : -0x8000, +0x7FFF), MAC[1]);
            IR[2] = IR_Check(2, Math.Clamp(MAC[2], lm ? 0 : -0x8000, +0x7FFF), MAC[2]);
            IR[3] = IR_Check(3, Math.Clamp(MAC[3], lm ? 0 : -0x8000, +0x7FFF), MAC[3]);


            // [IR1, IR2, IR3] = [MAC1, MAC2, MAC3] = (BK * 1000h + LCM * IR) SAR(sf * 12)

            MAC[1] = (int)(MAC_Check(1, MAC_Check(1, MAC_Check(1,
                    (long)BK_Color[0] * 0x1000 + (long)LCM.GetElement(1, 1) * IR[1]) +
                    (long)LCM.GetElement(1, 2) * IR[2]) + (long)LCM.GetElement(1, 3) * IR[3]) >> (int)sf * 12);

            MAC[2] = (int)(MAC_Check(2, MAC_Check(2, MAC_Check(2,
                   (long)BK_Color[1] * 0x1000 + (long)LCM.GetElement(2, 1) * IR[1]) +
                   (long)LCM.GetElement(2, 2) * IR[2]) + (long)LCM.GetElement(2, 3) * IR[3]) >> (int)sf * 12);

            MAC[3] = (int)(MAC_Check(3, MAC_Check(3, MAC_Check(3,
                   (long)BK_Color[2] * 0x1000 + (long)LCM.GetElement(3, 1) * IR[1]) +
                   (long)LCM.GetElement(3, 2) * IR[2]) + (long)LCM.GetElement(3, 3) * IR[3]) >> (int)sf * 12);

            IR[1] = IR_Check(1, Math.Clamp(MAC[1], lm ? 0 : -0x8000, +0x7FFF), MAC[1]);
            IR[2] = IR_Check(2, Math.Clamp(MAC[2], lm ? 0 : -0x8000, +0x7FFF), MAC[2]);
            IR[3] = IR_Check(3, Math.Clamp(MAC[3], lm ? 0 : -0x8000, +0x7FFF), MAC[3]);


            Color[0] = Color[1];
            Color[1] = Color[2];
            Color[2] = RGB_Check(1, MAC[1] >> 4) | RGB_Check(2, MAC[2] >> 4) << 8 | RGB_Check(3, MAC[3] >> 4) << 16 | ((uint)(RGBC >> 24)) << 24;

        }

        private void CC() {


            // [IR1, IR2, IR3] = [MAC1, MAC2, MAC3] = (BK * 1000h + LCM * IR) SAR(sf * 12)

            MAC[1] = (int)(MAC_Check(1, MAC_Check(1, MAC_Check(1,
                    (long)BK_Color[0] * 0x1000 + (long)LCM.GetElement(1, 1) * IR[1]) +
                    (long)LCM.GetElement(1, 2) * IR[2]) + (long)LCM.GetElement(1, 3) * IR[3]) >> (int)sf * 12);

            MAC[2] = (int)(MAC_Check(2, MAC_Check(2, MAC_Check(2,
                   (long)BK_Color[1] * 0x1000 + (long)LCM.GetElement(2, 1) * IR[1]) +
                   (long)LCM.GetElement(2, 2) * IR[2]) + (long)LCM.GetElement(2, 3) * IR[3]) >> (int)sf * 12);

            MAC[3] = (int)(MAC_Check(3, MAC_Check(3, MAC_Check(3,
                   (long)BK_Color[2] * 0x1000 + (long)LCM.GetElement(3, 1) * IR[1]) +
                   (long)LCM.GetElement(3, 2) * IR[2]) + (long)LCM.GetElement(3, 3) * IR[3]) >> (int)sf * 12);

            IR[1] = IR_Check(1, Math.Clamp(MAC[1], lm ? 0 : -0x8000, +0x7FFF), MAC[1]);
            IR[2] = IR_Check(2, Math.Clamp(MAC[2], lm ? 0 : -0x8000, +0x7FFF), MAC[2]);
            IR[3] = IR_Check(3, Math.Clamp(MAC[3], lm ? 0 : -0x8000, +0x7FFF), MAC[3]);



            MAC[1] = (int)MAC_Check(1, ((byte)RGBC * (long)IR[1])) << 4;
            MAC[2] = (int)MAC_Check(2, ((byte)(RGBC >> 8) * (long)IR[2])) << 4;
            MAC[3] = (int)MAC_Check(3, ((byte)(RGBC >> 16) * (long)IR[3]) << 4);


            MAC[1] = (int)(MAC_Check(1, MAC[1]) >> (int)sf * 12);
            MAC[2] = (int)(MAC_Check(2, MAC[2]) >> (int)sf * 12);
            MAC[3] = (int)(MAC_Check(3, MAC[3]) >> (int)sf * 12);


            IR[1] = IR_Check(1, Math.Clamp(MAC[1], lm ? 0 : -0x8000, +0x7FFF), MAC[1]);
            IR[2] = IR_Check(2, Math.Clamp(MAC[2], lm ? 0 : -0x8000, +0x7FFF), MAC[2]);
            IR[3] = IR_Check(3, Math.Clamp(MAC[3], lm ? 0 : -0x8000, +0x7FFF), MAC[3]);


            Color[0] = Color[1];
            Color[1] = Color[2];
            Color[2] = RGB_Check(1, MAC[1] >> 4) | RGB_Check(2, MAC[2] >> 4) << 8 | RGB_Check(3, MAC[3] >> 4) << 16 | ((uint)(RGBC >> 24)) << 24;


        }

        private void NCCS(int r) {

            MAC[1] = ((int)(MAC_Check(1, ((long)LLM.GetElement(1, 1) * V[r].X +
                   (long)LLM.GetElement(1, 2) * V[r].Y +
                   (long)LLM.GetElement(1, 3) * V[r].Z)) >> (int)sf * 12));


            MAC[2] = ((int)(MAC_Check(2, ((long)LLM.GetElement(2, 1) * V[r].X +
                    (long)LLM.GetElement(2, 2) * V[r].Y +
                    (long)LLM.GetElement(2, 3) * V[r].Z)) >> (int)sf * 12));


            MAC[3] = ((int)(MAC_Check(3, ((long)LLM.GetElement(3, 1) * V[r].X +
                    (long)LLM.GetElement(3, 2) * V[r].Y +
                    (long)LLM.GetElement(3, 3) * V[r].Z)) >> (int)sf * 12));



            IR[1] = IR_Check(1, Math.Clamp(MAC[1], lm ? 0 : -0x8000, +0x7FFF), MAC[1]);
            IR[2] = IR_Check(2, Math.Clamp(MAC[2], lm ? 0 : -0x8000, +0x7FFF), MAC[2]);
            IR[3] = IR_Check(3, Math.Clamp(MAC[3], lm ? 0 : -0x8000, +0x7FFF), MAC[3]);


            // [IR1, IR2, IR3] = [MAC1, MAC2, MAC3] = (BK * 1000h + LCM * IR) SAR(sf * 12)

            MAC[1] = (int)(MAC_Check(1, MAC_Check(1, MAC_Check(1,
                    (long)BK_Color[0] * 0x1000 + (long)LCM.GetElement(1, 1) * IR[1]) +
                    (long)LCM.GetElement(1, 2) * IR[2]) + (long)LCM.GetElement(1, 3) * IR[3]) >> (int)sf * 12);

            MAC[2] = (int)(MAC_Check(2, MAC_Check(2, MAC_Check(2,
                   (long)BK_Color[1] * 0x1000 + (long)LCM.GetElement(2, 1) * IR[1]) +
                   (long)LCM.GetElement(2, 2) * IR[2]) + (long)LCM.GetElement(2, 3) * IR[3]) >> (int)sf * 12);

            MAC[3] = (int)(MAC_Check(3, MAC_Check(3, MAC_Check(3,
                   (long)BK_Color[2] * 0x1000 + (long)LCM.GetElement(3, 1) * IR[1]) +
                   (long)LCM.GetElement(3, 2) * IR[2]) + (long)LCM.GetElement(3, 3) * IR[3]) >> (int)sf * 12);

            IR[1] = IR_Check(1, Math.Clamp(MAC[1], lm ? 0 : -0x8000, +0x7FFF), MAC[1]);
            IR[2] = IR_Check(2, Math.Clamp(MAC[2], lm ? 0 : -0x8000, +0x7FFF), MAC[2]);
            IR[3] = IR_Check(3, Math.Clamp(MAC[3], lm ? 0 : -0x8000, +0x7FFF), MAC[3]);



            MAC[1] = (int)MAC_Check(1, ((byte)RGBC * (long)IR[1])) << 4;
            MAC[2] = (int)MAC_Check(2, ((byte)(RGBC >> 8) * (long)IR[2])) << 4;
            MAC[3] = (int)MAC_Check(3, ((byte)(RGBC >> 16) * (long)IR[3]) << 4);


            MAC[1] = (int)(MAC_Check(1, MAC[1]) >> (int)sf * 12);
            MAC[2] = (int)(MAC_Check(2, MAC[2]) >> (int)sf * 12);
            MAC[3] = (int)(MAC_Check(3, MAC[3]) >> (int)sf * 12);


            IR[1] = IR_Check(1, Math.Clamp(MAC[1], lm ? 0 : -0x8000, +0x7FFF), MAC[1]);
            IR[2] = IR_Check(2, Math.Clamp(MAC[2], lm ? 0 : -0x8000, +0x7FFF), MAC[2]);
            IR[3] = IR_Check(3, Math.Clamp(MAC[3], lm ? 0 : -0x8000, +0x7FFF), MAC[3]);


            Color[0] = Color[1];
            Color[1] = Color[2];
            Color[2] = RGB_Check(1, MAC[1] >> 4) | RGB_Check(2, MAC[2] >> 4) << 8 | RGB_Check(3, MAC[3] >> 4) << 16 | ((uint)(RGBC >> 24)) << 24;

        }

        private void AVSZ4() {
            /*
               MAC0 =  ZSF3*(SZ1+SZ2+SZ3)       ;for AVSZ3
               MAC0 =  ZSF4*(SZ0+SZ1+SZ2+SZ3)   ;for AVSZ4
               OTZ  =  MAC0/1000h               ;for both (saturated to 0..FFFFh)
                         
             */

            long avg = (long)ZSF4 * ((ushort)S[0].Z + (ushort)S[1].Z + (ushort)S[2].Z + (ushort)S[3].Z);

            MAC[0] = (int)MAC_Check(0, avg);

            OTZ = (ushort)Math.Clamp(avg >> 12, 0, 0xFFFF);

            if ((avg >> 12) > 0xFFFF || (avg >> 12) < 0) {
                FLAG |= 1 << 18;
            }


        }

        private void CDP() {

            // [IR1, IR2, IR3] = [MAC1, MAC2, MAC3] = (BK * 1000h + LCM * IR) SAR(sf * 12)

            MAC[1] = (int)(MAC_Check(1, MAC_Check(1, MAC_Check(1,
                    (long)BK_Color[0] * 0x1000 + (long)LCM.GetElement(1, 1) * IR[1]) +
                    (long)LCM.GetElement(1, 2) * IR[2]) + (long)LCM.GetElement(1, 3) * IR[3]) >> (int)sf * 12);

            MAC[2] = (int)(MAC_Check(2, MAC_Check(2, MAC_Check(2,
                   (long)BK_Color[1] * 0x1000 + (long)LCM.GetElement(2, 1) * IR[1]) +
                   (long)LCM.GetElement(2, 2) * IR[2]) + (long)LCM.GetElement(2, 3) * IR[3]) >> (int)sf * 12);

            MAC[3] = (int)(MAC_Check(3, MAC_Check(3, MAC_Check(3,
                   (long)BK_Color[2] * 0x1000 + (long)LCM.GetElement(3, 1) * IR[1]) +
                   (long)LCM.GetElement(3, 2) * IR[2]) + (long)LCM.GetElement(3, 3) * IR[3]) >> (int)sf * 12);

            IR[1] = IR_Check(1, Math.Clamp(MAC[1], lm ? 0 : -0x8000, +0x7FFF), MAC[1]);
            IR[2] = IR_Check(2, Math.Clamp(MAC[2], lm ? 0 : -0x8000, +0x7FFF), MAC[2]);
            IR[3] = IR_Check(3, Math.Clamp(MAC[3], lm ? 0 : -0x8000, +0x7FFF), MAC[3]);


            MAC[1] = (int)MAC_Check(1, ((byte)RGBC * (long)IR[1])) << 4;
            MAC[2] = (int)MAC_Check(2, ((byte)(RGBC >> 8) * (long)IR[2])) << 4;
            MAC[3] = (int)MAC_Check(3, ((byte)(RGBC >> 16) * (long)IR[3]) << 4);


            interpolateColor(MAC[1], MAC[2], MAC[3]);

            Color[0] = Color[1];
            Color[1] = Color[2];
            Color[2] = RGB_Check(1, MAC[1] >> 4) | RGB_Check(2, MAC[2] >> 4) << 8 | RGB_Check(3, MAC[3] >> 4) << 16 | ((uint)(RGBC >> 24)) << 24;
        }

        private void MVMVA(uint mx, uint vx, uint tx) {

            //Calculate the 44 bits (43 + 1 sign) products and detect overflow
            Matrix3 matrix; 
            Vector3[] vector; 
            int[] T;


            switch (mx) {
                case 0x0:

                    matrix = RT;
                    break;
                case 0x1:

                    matrix = LLM;
                    break;

                case 0x2:

                    matrix = LCM;
                    break;

                default:
                    //Mx=3 selects a garbage matrix (with elements -60h, +60h, IR0,
                    //                                              RT13, RT13, RT13,
                    //                                              RT22, RT22, RT22).
                    //-60h,+60h ? nope, that's appearently wrong!

                    Vector3[] garbage = new Vector3[] {
                    new Vector3((short)-((byte)RGBC << 4), RT.GetElement(1,3), RT.GetElement(2,2)),
                    new Vector3((short)((byte)RGBC << 4) , RT.GetElement(1,3), RT.GetElement(2,2)),
                    new Vector3(IR[0], RT.GetElement(1,3), RT.GetElement(2,2)),
                  

                    };
          
                    matrix = new Matrix3(garbage);
                    break;

            }

            if (vx == 3) {
                vector = new Vector3[] { new Vector3(IR[1], IR[2], IR[3]) };
                vx = 0;
            }
            else {
                vector = V;
            }

            switch (tx) {
                case 0x0:
                    T = TR;
                    break;
                case 0x1:

                    T = BK_Color;
                    break;

                case 0x2:

                    T = far_Color;
                    farColorMVMVA(matrix,vector,T, vx);
                    return;


                default:   

                    T = new int[] { 0, 0, 0 };
                    break;
            }


            MAC[1] = (int)(
                MAC_Check(1, MAC_Check(1, MAC_Check(1, (long)T[0] * 0x1000 +
               (long)matrix.GetElement(1, 1) * vector[vx].X) + (long)matrix.GetElement(1, 2) * vector[vx].Y) +
               (long)matrix.GetElement(1, 3) * vector[vx].Z) >> (int)(sf * 12));


            MAC[2] = (int)(
                 MAC_Check(2, MAC_Check(2, MAC_Check(2, (long)T[1] * 0x1000 +
                (long)matrix.GetElement(2, 1) * vector[vx].X) + (long)matrix.GetElement(2, 2) * vector[vx].Y) +
                (long)matrix.GetElement(2, 3) * vector[vx].Z) >> (int)(sf * 12));


            MAC[3] = (int)(
                 MAC_Check(3, MAC_Check(3, MAC_Check(3, (long)T[2] * 0x1000 +
                (long)matrix.GetElement(3, 1) * vector[vx].X) + (long)matrix.GetElement(3, 2) * vector[vx].Y) +
                (long)matrix.GetElement(3, 3) * vector[vx].Z) >> (int)(sf * 12));

            IR[1] = IR_Check(1, Math.Clamp(MAC[1], lm ? 0 : -0x8000, +0x7FFF), MAC[1]);
            IR[2] = IR_Check(2, Math.Clamp(MAC[2], lm ? 0 : -0x8000, +0x7FFF), MAC[2]);
            IR[3] = IR_Check(3, Math.Clamp(MAC[3], lm ? 0 : -0x8000, +0x7FFF), MAC[3]);



        }
        private void farColorMVMVA(Matrix3 matrix, Vector3[] vector, int[] T,uint vx) {
            //What PSX-SPX states about the values being reduced to the last portion of the formula, ie.
            //MAC1=(Mx13*Vx3) SAR (sf*12) seems to be wrong
            //we should use the last 2 portions of the formula !
            //However the first portion is also checked in the FLAG register 

            
            //Calculate first portion to check for errors only
            long MAC1_ = MAC_Check(1, (long)T[0] * 0x1000 + (long)matrix.GetElement(1, 1) * vector[vx].X);
            long MAC2_ = MAC_Check(2, (long)T[1] * 0x1000 + (long)matrix.GetElement(2, 1) * vector[vx].X);
            long MAC3_ = MAC_Check(3, (long)T[2] * 0x1000 + (long)matrix.GetElement(3, 1) * vector[vx].X);
           
            IR_Check(1, Math.Clamp((int)(MAC1_ >> (int)sf * 12), -0x8000, +0x7FFF), (int)(MAC1_ >> (int)sf * 12));
            IR_Check(2, Math.Clamp((int)(MAC2_ >> (int)sf * 12), -0x8000, +0x7FFF), (int)(MAC2_ >> (int)sf * 12));
            IR_Check(3, Math.Clamp((int)(MAC3_ >> (int)sf * 12), -0x8000, +0x7FFF), (int)(MAC3_ >> (int)sf * 12));

            //Now calculate the real (buggy) values, and check for errors ofcourse

             MAC1_ = MAC_Check(1, MAC_Check(1, (long)matrix.GetElement(1, 2) * vector[vx].Y) + 
                     (long)matrix.GetElement(1, 3) * vector[vx].Z);

             MAC2_ = MAC_Check(2, MAC_Check(2, (long)matrix.GetElement(2, 2) * vector[vx].Y) + 
                     (long)matrix.GetElement(2, 3) * vector[vx].Z);

             MAC3_ = MAC_Check(3, MAC_Check(3, (long)matrix.GetElement(3, 2) * vector[vx].Y) +
                     (long)matrix.GetElement(3, 3) * vector[vx].Z);

            MAC[1] = (int)(MAC1_ >> (int)(sf * 12));
            MAC[2] = (int)(MAC2_ >> (int)(sf * 12));
            MAC[3] = (int)(MAC3_ >> (int)(sf * 12));

            IR[1] = IR_Check(1, (int)Math.Clamp(MAC[1], lm ? 0 : -0x8000, +0x7FFF), MAC[1]);
            IR[2] = IR_Check(2, (int)Math.Clamp(MAC[2], lm ? 0 : -0x8000, +0x7FFF), MAC[2]);
            IR[3] = IR_Check(3, (int)Math.Clamp(MAC[3], lm ? 0 : -0x8000, +0x7FFF), MAC[3]);

        }
        private void INTPL() {
            /* 
               [MAC1,MAC2,MAC3] = [IR1,IR2,IR3] SHL 12               ;<--- for INTPL only
               [MAC1, MAC2, MAC3] = MAC + (FC - MAC) * IR0          (done in interpolateColor method)
               [MAC1, MAC2, MAC3] = [MAC1, MAC2, MAC3] SAR(sf * 12) (done in interpolateColor method)
               [IR1, IR2, IR3] = [MAC1, MAC2, MAC3]                 (done in interpolateColor method)
               Color FIFO = [MAC1 / 16, MAC2 / 16, MAC3 / 16, CODE]
                
            
             */
            MAC[1] = (int)(MAC_Check(1, IR[1]) << 12);
            MAC[2] = (int)(MAC_Check(2, IR[2]) << 12);
            MAC[3] = (int)(MAC_Check(3, IR[3]) << 12);

            interpolateColor(MAC[1], MAC[2], MAC[3]);


            Color[0] = Color[1];
            Color[1] = Color[2];
            Color[2] = RGB_Check(1, MAC[1] >> 4) | RGB_Check(2, MAC[2] >> 4) << 8 | RGB_Check(3, MAC[3] >> 4) << 16 | ((uint)(RGBC >> 24)) << 24;


        }

        private void DPCS(bool readFromButtom) {
            /* 
               [MAC1, MAC2, MAC3] = [R, G, B] SHL 16;< --- for DPCS / DPCT
               [MAC1, MAC2, MAC3] = MAC + (FC - MAC) * IR0          (done in interpolateColor method)
               [MAC1, MAC2, MAC3] = [MAC1, MAC2, MAC3] SAR(sf * 12) (done in interpolateColor method)
               [IR1, IR2, IR3] = [MAC1, MAC2, MAC3]                 (done in interpolateColor method)
               Color FIFO = [MAC1 / 16, MAC2 / 16, MAC3 / 16, CODE]
                
            
             */

            if (readFromButtom) {
                MAC[1] = (int)(MAC_Check(1, (byte)Color[0]) << 16);
                MAC[2] = (int)(MAC_Check(2, (byte)(Color[0] >> 8)) << 16);
                MAC[3] = (int)(MAC_Check(3, (byte)(Color[0] >> 16)) << 16);
            }
            else {

                MAC[1] = (int)(MAC_Check(1, (byte)RGBC) << 16);
                MAC[2] = (int)(MAC_Check(2, (byte)(RGBC >> 8)) << 16);
                MAC[3] = (int)(MAC_Check(3, (byte)(RGBC >> 16)) << 16);

            }
   

            interpolateColor(MAC[1], MAC[2], MAC[3]);
           

            Color[0] = Color[1];
            Color[1] = Color[2];
            Color[2] = RGB_Check(1, MAC[1] >> 4) | RGB_Check(2, MAC[2] >> 4) << 8 | RGB_Check(3, MAC[3] >> 4) << 16 | ((uint)(RGBC >> 24)) << 24;

        }
       
        private void interpolateColor(int mac1,int mac2, int mac3) {    //Thanks to ProjectPSX by BlueStorm
            /* 
               Details on "MAC+(FC-MAC)*IR0" :

               [IR1,IR2,IR3] = (([RFC,GFC,BFC] SHL 12) - [MAC1,MAC2,MAC3]) SAR (sf*12)
               [MAC1,MAC2,MAC3] = (([IR1,IR2,IR3] * IR0) + [MAC1,MAC2,MAC3])
               [MAC1, MAC2, MAC3] = [MAC1, MAC2, MAC3] SAR(sf * 12)
               [IR1, IR2, IR3] = [MAC1, MAC2, MAC3] 

               Note: Above "[IR1,IR2,IR3]=(FC-MAC)" is saturated to -8000h..+7FFFh (ie. as if lm=0),
               anyways, further writes to [IR1,IR2,IR3] (within the same command) are saturated as usually (ie. depening on lm setting).
             
             */
            int IR1_Saturated;
            int IR2_Saturated;
            int IR3_Saturated;

            MAC[1] = (int)(MAC_Check(1, ((long)far_Color[0] << 12) - mac1) >> (int)sf * 12);
            MAC[2] = (int)(MAC_Check(2, ((long)far_Color[1] << 12) - mac2) >> (int)sf * 12);
            MAC[3] = (int)(MAC_Check(3, ((long)far_Color[2] << 12) - mac3) >> (int)sf * 12);

            IR1_Saturated = Math.Clamp(MAC[1], -0x8000, +0x7FFF);
            IR2_Saturated = Math.Clamp(MAC[2], -0x8000, +0x7FFF);
            IR3_Saturated = Math.Clamp(MAC[3], -0x8000, +0x7FFF);

            IR[1] = IR_Check(1, IR1_Saturated, MAC[1]);
            IR[2] = IR_Check(2, IR2_Saturated, MAC[2]);
            IR[3] = IR_Check(3, IR3_Saturated, MAC[3]);

            MAC[1] = (int)(MAC_Check(1, ((long)IR[1] * IR[0]) + mac1) >> (int)sf * 12);
            MAC[2] = (int)(MAC_Check(2, ((long)IR[2] * IR[0]) + mac2) >> (int)sf * 12);
            MAC[3] = (int)(MAC_Check(3, ((long)IR[3] * IR[0]) + mac3) >> (int)sf * 12);

            IR1_Saturated = Math.Clamp(MAC[1], lm ? 0 : -0x8000, +0x7FFF);
            IR2_Saturated = Math.Clamp(MAC[2], lm ? 0 : -0x8000, +0x7FFF);
            IR3_Saturated = Math.Clamp(MAC[3], lm ? 0 : -0x8000, +0x7FFF);

            IR[1] = IR_Check(1, IR1_Saturated, MAC[1]);
            IR[2] = IR_Check(2, IR2_Saturated, MAC[2]);
            IR[3] = IR_Check(3, IR3_Saturated, MAC[3]);
        }

        private void OP() {
            // Outer product of 2 vectors
            short D1 = RT.GetElement(1, 1);
            short D2 = RT.GetElement(2, 2);
            short D3 = RT.GetElement(3, 3);

            MAC[1] = (int)MAC_Check(1, (long)IR[3] * D2 - (long)IR[2] * D3) >> (int)sf * 12;
            MAC[2] = (int)MAC_Check(2, (long)IR[1] * D3 - (long)IR[3] * D1) >> (int)sf * 12;
            MAC[3] = (int)MAC_Check(3, (long)IR[2] * D1 - (long)IR[1] * D2) >> (int)sf * 12;

            uint IR1_Saturated = (uint)Math.Clamp(MAC[1], lm ? 0 : -0x8000, +0x7FFF);
            IR[1] = IR_Check(1, (int)IR1_Saturated, MAC[1]);


            uint IR2_Saturated = (uint)Math.Clamp(MAC[2], lm ? 0 : -0x8000, +0x7FFF);
            IR[2] = IR_Check(2, (int)IR2_Saturated, MAC[2]);


            uint IR3_Saturated = (uint)Math.Clamp(MAC[3], lm ? 0 : -0x8000, +0x7FFF);
            IR[3] = IR_Check(3, (int)IR3_Saturated, MAC[3]);

        }

        private short IR_Check(int IR_number, int IR_saturated_value, long IR_long_value) {

            if (IR_saturated_value != (int)IR_long_value) { //long vs uint?
                if (IR_number != 0) {
                    FLAG = FLAG | (uint)(1 << (25 - IR_number));
                }
                else {
                    FLAG = FLAG | (uint)(1 << 12);

                }
              

            }

            return (short)IR_saturated_value;
        }


        private void AVSZ3() {

            /*
                MAC0 =  ZSF3*(SZ1+SZ2+SZ3)       ;for AVSZ3
                MAC0 =  ZSF4*(SZ0+SZ1+SZ2+SZ3)   ;for AVSZ4
                OTZ  =  MAC0/1000h               ;for both (saturated to 0..FFFFh)
              
             */

            long avg = (long)ZSF3 * ((ushort)S[1].Z + (ushort)S[2].Z + (ushort)S[3].Z);
            
            MAC[0] = (int)MAC_Check(0, avg);

            OTZ = (ushort)Math.Clamp(avg >> 12, 0, 0xFFFF);

            if ((avg >> 12) > 0xFFFF || (avg >> 12) < 0) {           
                FLAG |= 1 << 18;
            }

        }

        private void NCDS(int r) {


            MAC[1] = (int)(MAC_Check(1, ((long)LLM.GetElement(1,1) * V[r].X + 
                    (long)LLM.GetElement(1, 2) * V[r].Y + 
                    (long)LLM.GetElement(1, 3) * V[r].Z)) >> (int)sf * 12);


            MAC[2] = (int)(MAC_Check(2, ((long)LLM.GetElement(2, 1) * V[r].X +
                    (long)LLM.GetElement(2, 2) * V[r].Y +
                    (long)LLM.GetElement(2, 3) * V[r].Z)) >> (int)sf * 12);


            MAC[3] = (int)(MAC_Check(3, ((long)LLM.GetElement(3, 1) * V[r].X +
                    (long)LLM.GetElement(3, 2) * V[r].Y +
                    (long)LLM.GetElement(3, 3) * V[r].Z)) >> (int)sf * 12);

   

            IR[1] = IR_Check(1, Math.Clamp(MAC[1], lm ? 0 : -0x8000, +0x7FFF), MAC[1]);
            IR[2] = IR_Check(2, Math.Clamp(MAC[2], lm ? 0 : -0x8000, +0x7FFF), MAC[2]);
            IR[3] = IR_Check(3, Math.Clamp(MAC[3], lm ? 0 : -0x8000, +0x7FFF), MAC[3]);


            // [IR1, IR2, IR3] = [MAC1, MAC2, MAC3] = (BK * 1000h + LCM * IR) SAR(sf * 12)

            MAC[1] = (int)(MAC_Check(1, MAC_Check(1, MAC_Check(1, 
                    (long)BK_Color[0] * 0x1000 + (long)LCM.GetElement(1,1) * IR[1]) + 
                    (long)LCM.GetElement(1, 2) * IR[2]) + (long)LCM.GetElement(1, 3) * IR[3]) >> (int)sf * 12);

            MAC[2] = (int)(MAC_Check(2, MAC_Check(2, MAC_Check(2,
                   (long)BK_Color[1] * 0x1000 + (long)LCM.GetElement(2, 1) * IR[1]) +
                   (long)LCM.GetElement(2, 2) * IR[2]) + (long)LCM.GetElement(2, 3) * IR[3]) >> (int)sf * 12);

            MAC[3] = (int)(MAC_Check(3, MAC_Check(3, MAC_Check(3,
                   (long)BK_Color[2] * 0x1000 + (long)LCM.GetElement(3, 1) * IR[1]) +
                   (long)LCM.GetElement(3, 2) * IR[2]) + (long)LCM.GetElement(3, 3) * IR[3]) >> (int)sf * 12);

            IR[1] = IR_Check(1, Math.Clamp(MAC[1], lm ? 0 : -0x8000, +0x7FFF), MAC[1]);
            IR[2] = IR_Check(2, Math.Clamp(MAC[2], lm ? 0 : -0x8000, +0x7FFF), MAC[2]);
            IR[3] = IR_Check(3, Math.Clamp(MAC[3], lm ? 0 : -0x8000, +0x7FFF), MAC[3]);



            MAC[1] = (int)MAC_Check(1, (byte)RGBC  * (long)IR[1]) << 4;
            MAC[2] = (int)MAC_Check(2, (byte)(RGBC >> 8)  * (long)IR[2]) << 4;
            MAC[3] = (int)MAC_Check(3, (byte)(RGBC >> 16)  * (long)IR[3]) << 4;


            interpolateColor(MAC[1], MAC[2], MAC[3]);

            Color[0] = Color[1];
            Color[1] = Color[2];
            Color[2] = RGB_Check(1, MAC[1] >> 4) | RGB_Check(2, MAC[2] >> 4) << 8 | RGB_Check(3, MAC[3] >> 4) << 16 | ((uint)(RGBC >> 24)) << 24;

        }

        private uint RGB_Check(int MAC_number, long v) {
            if (v < 0) {

                FLAG = (FLAG | (uint)1 << (22 - MAC_number));
                return 0;

            } else if (v > +0xFF) {

                FLAG = FLAG | (uint)1 << (22 - MAC_number);
                return 0xFF;
            }

            return (uint)v;
        }


        private void NCLIP() {
            MAC[0] = (int)MAC_Check(0,
             ((long)S[0].X * S[1].Y + (long)S[1].X * S[2].Y + 
             (long)S[2].X * S[0].Y - (long)S[0].X * S[2].Y - 
             (long)S[1].X * S[0].Y - (long)S[2].X * S[1].Y)); 
        }

        //RTPS - Perspective Transformation Single
        private void RTPS(int r, bool finalResult) {

            //Calculate the 44 bits (43 + 1 sign) products and detect overflow

            MAC[1] = (int)((MAC_Check(1, MAC_Check(1, MAC_Check(1, (long)TR[0] * 0x1000 +
               (long)RT.GetElement(1,1) * V[r].X) + (long)RT.GetElement(1,2) * V[r].Y) + 
               (long)RT.GetElement(1,3) * V[r].Z)) >> (int)(sf * 12));

            uint IR1_Saturated = (uint)Math.Clamp(MAC[1], lm ? 0 : -0x8000, +0x7FFF);
            IR_Check(1, (int)IR1_Saturated, MAC[1]);
            IR[1] = (short)IR1_Saturated;


            MAC[2] = (int)((MAC_Check(2, MAC_Check(2, MAC_Check(2, (long)TR[1] * 0x1000 + 
                (long)RT.GetElement(2,1) * V[r].X) + (long)RT.GetElement(2,2) * V[r].Y) + 
                (long)RT.GetElement(2,3) * V[r].Z)) >> (int)(sf * 12));

            uint IR2_Saturated = (uint)Math.Clamp(MAC[2], lm ? 0 : -0x8000, +0x7FFF);

            IR_Check(2, (int)IR2_Saturated, MAC[2]);
            IR[2] = (short)IR2_Saturated;


            long MAC3_ = ((MAC_Check(3, MAC_Check(3, MAC_Check(3, (long)TR[2] * 0x1000 + 
                (long)RT.GetElement(3,1) * V[r].X) + (long)RT.GetElement(3,2) * V[r].Y) + 
                (long)RT.GetElement(3,3) * V[r].Z)) );

            MAC[3] = (int)(MAC3_ >> (int)(sf * 12));

            IR[3] = (short)Math.Clamp(MAC[3], lm ? 0 : -0x8000, +0x7FFF);

            if ((int)(MAC3_ >> 12) < -0x8000 || (int)(MAC3_ >> 12) > 0x7FFF) {
                FLAG = FLAG | (uint)(1 << (25 - 3));
            }

            /*  
                        if (sf == 1) {
                            IR_Check(3, (int)IR3_Saturated, MAC[3]);

                        }
                        else {
                            int MAC3 = (int)MAC[3] >> 12;
                            int MAC3_Saturated = Math.Clamp(MAC3, -0x8000, +0x7FFF);

                            if (MAC3 != MAC3_Saturated) {
                                FLAG |= 1 << 22;

                            }

                        }
            */
            //The older entries are moved one stage down

            S[0].Z = S[1].Z;
            S[1].Z = S[2].Z;
            S[2].Z = S[3].Z;
          
            uint SZ3_Saturated = (uint)Math.Clamp(MAC3_ >> 12, 0, 0xFFFF);
            S[3].Z = (short)(SZ3_Saturated);

            if ((int)(MAC3_ >> 12) < 0 || (int)(MAC3_ >> 12) > 0xFFFF) {
                FLAG |= 1 << 18;

            }

          
            //The real GTE hardware is using a fast, but less accurate division mechanism
            //(based on Unsigned Newton-Raphson (UNR) algorithm)

            long n;

            if (H < (SZ3_Saturated << 1)) {
                int z = (int)(countSignBit(SZ3_Saturated) - 16);
                n = H << z;
                uint d = (SZ3_Saturated << z);
                uint u = (uint)(unr_table[(d - 0x7FC0) >> 7] + 0x101);
                d = (0x2000080 - (d * u)) >> 8;
                d = (0x0000080 + (d * u)) >> 8;
                n = Math.Min(0x1FFFF, ((n * d) + 0x8000) >> 16);

            }
            else {
                n = 0x1FFFF;
                FLAG |= 1 << 17;

            }


            S[0].XY = S[1].XY;
            S[1].XY = S[2].XY;

            long MAC0_;

            MAC0_ = n * ((short)IR1_Saturated) + OF[0];

            int SX2 = (int)(MAC_Check(0, MAC0_) >> 16);

            MAC0_ = n * ((short)IR2_Saturated) + OF[1];

            int SY2 = (int)(MAC_Check(0, MAC0_) >> 16);

            S[2].X = (short)Math.Clamp(SX2, -0x400, +0x3FF);
            S[2].Y = (short)Math.Clamp(SY2, -0x400, +0x3FF);


            if (((short)SX2) != S[2].X) {
                FLAG |= 1 << 14;

            }
            if (((short)SY2) != S[2].Y) {
                FLAG |= 1 << 13;

            }

            S[3].XY = S[2].XY;

            if (finalResult) {
                MAC0_ = n * DQA + DQB;
                MAC_Check(0, MAC0_);
                MAC[0] = (int)MAC0_;
                uint IR0_Saturated = (uint)Math.Clamp(MAC0_ >> 12, 0, 0x1000);
                IR[0] = (short)IR0_Saturated;
                IR_Check(0, (int)IR0_Saturated, MAC0_ >> 12);
            }


        }
        private long MAC_Check(int MAC_number, long MAC_value) {

            switch (MAC_number) {
                case 0:

                    if (MAC_value > 0x7FFFFFFF) {
                        FLAG |= 1 << 16;

                     }
                    if (MAC_value < -0x80000000) {
                        FLAG |= 1 << 15;

                    }

                    break;

                case 1:
                case 2:
                case 3:
                    if (MAC_value > 0x7FFFFFFFFFF) {
                        FLAG |= (uint)(1 << (31 - MAC_number));

                     }
                    else if (MAC_value < -0x80000000000) {
                        FLAG |= (uint)(1 << (28 - MAC_number));

                    }


                    return (MAC_value << 20) >> 20; //Thanks to ProjectPSX by BlueStorm

            }

            return (MAC_value);
        }

    }

    class Command {
        public int delay;
        public Instruction value;

        public Command(Instruction value, int delay) {
            this.delay = delay;
            this.value = value;
        }


    }
 
}
