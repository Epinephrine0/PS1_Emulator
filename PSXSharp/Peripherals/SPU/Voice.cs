using NAudio.Wave;
using System;

namespace PSXSharp.Peripherals.SPU {
    public class Voice {
        public short volumeLeft;
        public short volumeRight;

        public ushort ADPCM_Pitch;
        public ADSR adsr = new ADSR();
        public ushort ADPCM;            //Start address of sound in Sound buffer (in 8-byte units)
        public ushort ADPCM_RepeatAdress;
        public ushort current_address;
        public uint pitchCounter;     //Used for interpolation and current sample index

        public short old;
        public short older;
        public short lastSample;
        public uint ENDX = 1;

        public bool isLoaded = false;
        public bool hit_IRQ_Address = false;

        short[] decodedSamples = new short[31]; //28 samples + 3 
        byte[] samples = new byte[16];

        private int[] pos_xa_adpcm_table = { 0, +60, +115, +98, +122 };
        private int[] neg_xa_adpcm_table = { 0, 0, -52, -55, -60 };

        private short[] gaussTable = new short[] {
                -0x001, -0x001, -0x001, -0x001, -0x001, -0x001, -0x001, -0x001,
                -0x001, -0x001, -0x001, -0x001, -0x001, -0x001, -0x001, -0x001,
                0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0001,
                0x0001, 0x0001, 0x0001, 0x0002, 0x0002, 0x0002, 0x0003, 0x0003,
                0x0003, 0x0004, 0x0004, 0x0005, 0x0005, 0x0006, 0x0007, 0x0007,
                0x0008, 0x0009, 0x0009, 0x000A, 0x000B, 0x000C, 0x000D, 0x000E,
                0x000F, 0x0010, 0x0011, 0x0012, 0x0013, 0x0015, 0x0016, 0x0018,
                0x0019, 0x001B, 0x001C, 0x001E, 0x0020, 0x0021, 0x0023, 0x0025,
                0x0027, 0x0029, 0x002C, 0x002E, 0x0030, 0x0033, 0x0035, 0x0038,
                0x003A, 0x003D, 0x0040, 0x0043, 0x0046, 0x0049, 0x004D, 0x0050,
                0x0054, 0x0057, 0x005B, 0x005F, 0x0063, 0x0067, 0x006B, 0x006F,
                0x0074, 0x0078, 0x007D, 0x0082, 0x0087, 0x008C, 0x0091, 0x0096,
                0x009C, 0x00A1, 0x00A7, 0x00AD, 0x00B3, 0x00BA, 0x00C0, 0x00C7,
                0x00CD, 0x00D4, 0x00DB, 0x00E3, 0x00EA, 0x00F2, 0x00FA, 0x0101,
                0x010A, 0x0112, 0x011B, 0x0123, 0x012C, 0x0135, 0x013F, 0x0148,
                0x0152, 0x015C, 0x0166, 0x0171, 0x017B, 0x0186, 0x0191, 0x019C,
                0x01A8, 0x01B4, 0x01C0, 0x01CC, 0x01D9, 0x01E5, 0x01F2, 0x0200,
                0x020D, 0x021B, 0x0229, 0x0237, 0x0246, 0x0255, 0x0264, 0x0273,
                0x0283, 0x0293, 0x02A3, 0x02B4, 0x02C4, 0x02D6, 0x02E7, 0x02F9,
                0x030B, 0x031D, 0x0330, 0x0343, 0x0356, 0x036A, 0x037E, 0x0392,
                0x03A7, 0x03BC, 0x03D1, 0x03E7, 0x03FC, 0x0413, 0x042A, 0x0441,
                0x0458, 0x0470, 0x0488, 0x04A0, 0x04B9, 0x04D2, 0x04EC, 0x0506,
                0x0520, 0x053B, 0x0556, 0x0572, 0x058E, 0x05AA, 0x05C7, 0x05E4,
                0x0601, 0x061F, 0x063E, 0x065C, 0x067C, 0x069B, 0x06BB, 0x06DC,
                0x06FD, 0x071E, 0x0740, 0x0762, 0x0784, 0x07A7, 0x07CB, 0x07EF,
                0x0813, 0x0838, 0x085D, 0x0883, 0x08A9, 0x08D0, 0x08F7, 0x091E,
                0x0946, 0x096F, 0x0998, 0x09C1, 0x09EB, 0x0A16, 0x0A40, 0x0A6C,
                0x0A98, 0x0AC4, 0x0AF1, 0x0B1E, 0x0B4C, 0x0B7A, 0x0BA9, 0x0BD8,
                0x0C07, 0x0C38, 0x0C68, 0x0C99, 0x0CCB, 0x0CFD, 0x0D30, 0x0D63,
                0x0D97, 0x0DCB, 0x0E00, 0x0E35, 0x0E6B, 0x0EA1, 0x0ED7, 0x0F0F,
                0x0F46, 0x0F7F, 0x0FB7, 0x0FF1, 0x102A, 0x1065, 0x109F, 0x10DB,
                0x1116, 0x1153, 0x118F, 0x11CD, 0x120B, 0x1249, 0x1288, 0x12C7,
                0x1307, 0x1347, 0x1388, 0x13C9, 0x140B, 0x144D, 0x1490, 0x14D4,
                0x1517, 0x155C, 0x15A0, 0x15E6, 0x162C, 0x1672, 0x16B9, 0x1700,
                0x1747, 0x1790, 0x17D8, 0x1821, 0x186B, 0x18B5, 0x1900, 0x194B,
                0x1996, 0x19E2, 0x1A2E, 0x1A7B, 0x1AC8, 0x1B16, 0x1B64, 0x1BB3,
                0x1C02, 0x1C51, 0x1CA1, 0x1CF1, 0x1D42, 0x1D93, 0x1DE5, 0x1E37,
                0x1E89, 0x1EDC, 0x1F2F, 0x1F82, 0x1FD6, 0x202A, 0x207F, 0x20D4,
                0x2129, 0x217F, 0x21D5, 0x222C, 0x2282, 0x22DA, 0x2331, 0x2389,
                0x23E1, 0x2439, 0x2492, 0x24EB, 0x2545, 0x259E, 0x25F8, 0x2653,
                0x26AD, 0x2708, 0x2763, 0x27BE, 0x281A, 0x2876, 0x28D2, 0x292E,
                0x298B, 0x29E7, 0x2A44, 0x2AA1, 0x2AFF, 0x2B5C, 0x2BBA, 0x2C18,
                0x2C76, 0x2CD4, 0x2D33, 0x2D91, 0x2DF0, 0x2E4F, 0x2EAE, 0x2F0D,
                0x2F6C, 0x2FCC, 0x302B, 0x308B, 0x30EA, 0x314A, 0x31AA, 0x3209,
                0x3269, 0x32C9, 0x3329, 0x3389, 0x33E9, 0x3449, 0x34A9, 0x3509,
                0x3569, 0x35C9, 0x3629, 0x3689, 0x36E8, 0x3748, 0x37A8, 0x3807,
                0x3867, 0x38C6, 0x3926, 0x3985, 0x39E4, 0x3A43, 0x3AA2, 0x3B00,
                0x3B5F, 0x3BBD, 0x3C1B, 0x3C79, 0x3CD7, 0x3D35, 0x3D92, 0x3DEF,
                0x3E4C, 0x3EA9, 0x3F05, 0x3F62, 0x3FBD, 0x4019, 0x4074, 0x40D0,
                0x412A, 0x4185, 0x41DF, 0x4239, 0x4292, 0x42EB, 0x4344, 0x439C,
                0x43F4, 0x444C, 0x44A3, 0x44FA, 0x4550, 0x45A6, 0x45FC, 0x4651,
                0x46A6, 0x46FA, 0x474E, 0x47A1, 0x47F4, 0x4846, 0x4898, 0x48E9,
                0x493A, 0x498A, 0x49D9, 0x4A29, 0x4A77, 0x4AC5, 0x4B13, 0x4B5F,
                0x4BAC, 0x4BF7, 0x4C42, 0x4C8D, 0x4CD7, 0x4D20, 0x4D68, 0x4DB0,
                0x4DF7, 0x4E3E, 0x4E84, 0x4EC9, 0x4F0E, 0x4F52, 0x4F95, 0x4FD7,
                0x5019, 0x505A, 0x509A, 0x50DA, 0x5118, 0x5156, 0x5194, 0x51D0,
                0x520C, 0x5247, 0x5281, 0x52BA, 0x52F3, 0x532A, 0x5361, 0x5397,
                0x53CC, 0x5401, 0x5434, 0x5467, 0x5499, 0x54CA, 0x54FA, 0x5529,
                0x5558, 0x5585, 0x55B2, 0x55DE, 0x5609, 0x5632, 0x565B, 0x5684,
                0x56AB, 0x56D1, 0x56F6, 0x571B, 0x573E, 0x5761, 0x5782, 0x57A3,
                0x57C3, 0x57E2, 0x57FF, 0x581C, 0x5838, 0x5853, 0x586D, 0x5886,
                0x589E, 0x58B5, 0x58CB, 0x58E0, 0x58F4, 0x5907, 0x5919, 0x592A,
                0x593A, 0x5949, 0x5958, 0x5965, 0x5971, 0x597C, 0x5986, 0x598F,
                0x5997, 0x599E, 0x59A4, 0x59A9, 0x59AD, 0x59B0, 0x59B2, 0x59B3,
        };
        public Voice() {
            adsr.adsrLOW = 0;
            adsr.adsrHI = 0;
            adsr.adsrVolume = 0;
            adsr.setPhase(ADSR.Phase.Off);
            volumeLeft = 0;
            volumeRight = 0;
            lastSample = 0;
            ADPCM = 0;
            ADPCM_Pitch = 0;
        }
        public void setADSR_LOW(ushort v) {
            adsr.adsrLOW = v;
        }
        public void setADSR_HI(ushort v) {
            adsr.adsrHI = v;
        }
        public void loadSamples(ref byte[] SPU_RAM, uint IRQ_Address) {
            hit_IRQ_Address = (IRQ_Address >= current_address << 3) && (IRQ_Address <= ((current_address << 3) + samples.Length - 1));
            //Possible optimization using Span
            for (int i = 0; i < samples.Length; i++) {
                int index = (current_address << 3) + i;
                samples[i] = SPU_RAM[index];
            }

            isLoaded = true;

            //Handle Loop Start/End/Repeat flags
            uint flags = samples[1];

            if (((flags >> 2) & 1) != 0) {     //Loop Start bit
                ADPCM_RepeatAdress = current_address;
            }

        }
        public void decodeADPCM() {

            //save the last 3 samples from the last decoded block
            decodedSamples[2] = decodedSamples[decodedSamples.Length - 1];
            decodedSamples[1] = decodedSamples[decodedSamples.Length - 2];
            decodedSamples[0] = decodedSamples[decodedSamples.Length - 3];

            int headerShift = samples[0] & 0xF;
            if (headerShift > 12) { headerShift = 9; }

            int shift = 12 - headerShift;
            int filter = (samples[0] & 0x70) >> 4;            //3 bits, unlike XA-ADPCM where filter is 2 bits
            if (filter > 4) { filter = 4; }

            int f0 = pos_xa_adpcm_table[filter];
            int f1 = neg_xa_adpcm_table[filter];
            int t;
            int s;
            int position = 2; //skip shift and flags
            int nibble = 1;

            for (int i = 0; i < 28; i++) {
                nibble = (nibble + 1) & 0x1;        //sample number inside the byte (either 0 or 1)

                t = signed4bits((byte)(samples[position] >> (nibble << 2) & 0x0F));
                s = (t << shift) + (old * f0 + older * f1 + 32) / 64;

                short decoded = (short)Math.Clamp(s, -0x8000, 0x7FFF);
                decodedSamples[3 + i] = decoded;    //Skip 3 (last 3 of previous block)
                older = old;
                old = decoded;

                position += nibble;

            }

        }

        uint sampleIndex;

        public short interpolate() {
            int interpolated;
            uint interpolationIndex = getInterpolationIndex();
            sampleIndex = getCurrentSampleIndex();

            interpolated = gaussTable[0x0FF - interpolationIndex] * decodedSamples[sampleIndex + 0];
            interpolated += gaussTable[0x1FF - interpolationIndex] * decodedSamples[sampleIndex + 1];
            interpolated += gaussTable[0x100 + interpolationIndex] * decodedSamples[sampleIndex + 2];
            interpolated += gaussTable[0x000 + interpolationIndex] * decodedSamples[sampleIndex + 3];
            interpolated = interpolated >> 15;

            return (short)interpolated;
        }
        public void checkSamplesIndex() {

            sampleIndex = getCurrentSampleIndex();

            if (sampleIndex >= 28) {
                changeCurrentSampleIndex(-28);

                current_address += 2;
                isLoaded = false;

                uint flags = samples[1];

                if ((flags & 0x1) != 0) {       //Loop End bit
                    ENDX = 1;

                    if ((flags & 0x2) != 0) {     //Loop Repeat bit
                        current_address = ADPCM_RepeatAdress;

                    } else {
                        adsr.setPhase(ADSR.Phase.Off);
                        adsr.adsrVolume = 0;
                        adsr.adsrCounter = 0;
                    }
                }
            }
        }
        private void changeCurrentSampleIndex(int value) {
            uint old = getCurrentSampleIndex();
            int newIndex = (int)(value + old);
            pitchCounter = (ushort)(pitchCounter & 0xFFF);
            pitchCounter |= (ushort)(newIndex << 12);
        }
        internal void keyOn() {
            adsr.adsrVolume = 0;
            adsr.adsrCounter = 0;
            adsr.setPhase(ADSR.Phase.Attack);
            ADPCM_RepeatAdress = ADPCM;
            current_address = ADPCM;
            old = 0;
            older = 0;
            ENDX = 0;
            isLoaded = false;
        }
        public uint getCurrentSampleIndex() {
            return (pitchCounter >> 12) & 0x1F;
        }
        public uint getInterpolationIndex() {
            return (pitchCounter >> 4) & 0xFF;
        }
        int signed4bits(byte value) {
            return value << 28 >> 28;
        }
        public short getVolumeLeft() {
            short vol;
            if (((volumeLeft >> 15) & 1) == 0) {
                vol = (short)(volumeLeft << 1);
                return vol;
            } else {
                return 0x7FFF;
            }
        }
        public short getVolumeRight() {
            short vol;

            if (((volumeRight >> 15) & 1) == 0) {
                vol = (short)(volumeRight << 1);
                return vol;
            } else {
                return 0x7FFF;
            }
        }
        public void keyOff() {
            adsr.setPhase(ADSR.Phase.Release);
            adsr.adsrCounter = 0;
        }
        public class ADSR {        //ADSR Generator/Handler 
            public enum Phase {
                Attack,
                Decay,
                Sustain,
                Release,
                Off
            }
            enum Mode {
                Linear,
                Exponential
            }
            enum Direction {
                Increase,
                Decrease
            }

            public Phase phase = Phase.Off;
            public Phase nextphase = Phase.Off;

            Mode mode;
            Direction direction;
            public int shift;         //(0..1Fh = Fast..Slow) - for decay it is up to 0x0F becuase it is just 4 bits instead of 5
            int step;           //(0..3 = "+7,+6,+5,+4")


            public ushort adsrLOW;
            public ushort adsrHI;

            public ushort adsrVolume;
            public int limit;


            int[] positiveSteps = { +7, +6, +5, +4 };
            int[] negativeSteps = { -8, -7, -6, -5 };

            //For Envelope Operation
            int adsrCycles;
            int adsrStep;

            public void setPhase(Phase phase) {
                this.phase = phase;

                switch (phase) {

                    case Phase.Attack:

                        switch ((adsrLOW >> 15) & 1) {
                            case 0:
                                mode = Mode.Linear;
                                break;
                            case 1:
                                mode = Mode.Exponential;
                                break;

                            default:
                                throw new Exception("Unknown mode value");
                        }

                        direction = Direction.Increase; //Fixed for attack mode
                        shift = (adsrLOW >> 10) & 0X1F;
                        step = positiveSteps[(adsrLOW >> 8) & 0x3];
                        limit = 0x7FFF;       //Maximum for attack mode
                        nextphase = Phase.Decay;
                        break;

                    case Phase.Decay:
                        //Debug.WriteLine("decay!");

                        mode = Mode.Exponential;          //Fixed for decay mode
                        direction = Direction.Decrease;   //Fixed, always Decrease until sustain lever
                        shift = (adsrLOW >> 4) & 0x0F;   //Only 4 bits, not 5
                        step = -8;      //Fixed for decay mode
                        limit = ((adsrLOW & 0xF) + 1) * 0x800; //Level=(N+1)*800h
                        nextphase = Phase.Sustain;

                        break;

                    case Phase.Sustain:
                        //Debug.WriteLine("sustain!");

                        switch ((adsrHI >> 15) & 1) {
                            case 0: mode = Mode.Linear; break;
                            case 1: mode = Mode.Exponential; break;
                            default: throw new Exception("Unknown mode value");
                        }

                        switch ((adsrHI >> 14) & 1) {
                            case 0:
                                direction = Direction.Increase;
                                step = positiveSteps[(adsrHI >> 6) & 0x3];
                                break;

                            case 1:
                                direction = Direction.Decrease;
                                step = negativeSteps[(adsrHI >> 6) & 0x3];
                                break;

                            default:
                                throw new Exception("Unknown mode value");
                        }


                        shift = (adsrHI >> 8) & 0x1F;
                        limit = 0x0000;         //Questionable 
                        nextphase = Phase.Sustain;

                        break;

                    case Phase.Release:

                        switch ((adsrHI >> 5) & 1) {
                            case 0:
                                mode = Mode.Linear;
                                break;
                            case 1:
                                mode = Mode.Exponential;
                                break;

                            default:
                                throw new Exception("Unknown mode value");
                        }

                        direction = Direction.Decrease; //(Fixed, always Decrease) (until Level 0000h)
                        limit = 0x0000;
                        shift = adsrHI & 0x1F;
                        step = -8;                      //Fixed for release mode

                        nextphase = Phase.Off;
                        break;

                    case Phase.Off:

                        limit = 0x0000;
                        shift = 0;
                        step = 0;
                        nextphase = Phase.Off;

                        break;

                    default:
                        throw new Exception("Unknown phase: " + phase);
                }



            }
            public int adsrCounter = 0;

            public void ADSREnvelope() {

                if (adsrCounter > 0) {
                    adsrCounter--;
                    return;
                }

                int shiftAmount = 0;


                shiftAmount = Math.Max(0, shift - 11);
                adsrCycles = 1 << shiftAmount;

                shiftAmount = Math.Max(0, 11 - shift);
                adsrStep = step << shiftAmount;

                if (mode == Mode.Exponential && direction == Direction.Increase && adsrVolume > 0x6000) {
                    adsrCycles = adsrCycles << 2;
                }

                if (mode == Mode.Exponential && direction == Direction.Decrease) {
                    adsrStep = adsrStep * adsrVolume >> 15;

                }

                adsrVolume = (ushort)Math.Clamp(adsrVolume + adsrStep, 0, 0x7FFF);

                adsrCounter = adsrCycles;

                switch (phase) {
                    case Phase.Attack:

                        if (adsrVolume >= limit) {
                            setPhase(nextphase);
                            adsrCounter = 0;

                        }

                        break;

                    case Phase.Decay:
                        if (adsrVolume <= limit) {
                            setPhase(nextphase);
                            adsrCounter = 0;
                        }

                        break;

                    case Phase.Sustain:

                        break;

                    case Phase.Release:
                        if (adsrVolume <= limit) {
                            setPhase(nextphase);
                            adsrVolume = 0;
                            adsrCounter = 0;
                        }

                        break;

                    case Phase.Off:
                        break;
                }

            }

        }
    }


}
