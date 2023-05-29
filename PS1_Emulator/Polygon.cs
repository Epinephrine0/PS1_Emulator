using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PS1_Emulator {
    public class Polygon : GP0_Command_ss {
        /*
        GPU Render Polygon Commands

        First command decoding:
        bit number     value   meaning
           31-29        001    polygon render
           28           1/0    gouraud / flat shading
           27           1/0    4 / 3 vertices
           26           1/0    textured / untextured
           25           1/0    semi-transparent / opaque
           24           1/0    raw texture / modulation
           23-0         rgb    first color value.

        */

        bool isGouraud;
        bool isQuad;
        bool isTextured;
        bool isSemiTransparent;
        bool isRawTextured;

        short[] vertices;
        byte[] colors;
        ushort[] uv;
        List<uint> buffer = new List<uint>();
        uint pointer;
        uint opcode;
        ushort clut;
        ushort page;
        int texMode;

        int[] numberOfParameters = {
            -1, -1, -1, -1, -1, -1, -1, -1, 
            -1, -1, -1, -1, -1, -1, -1, -1, 
            -1, -1, -1, -1, -1, -1, -1, -1, 
            -1, -1, -1, -1, -1, -1, -1, -1,
            4, -1, 4, -1, 7, 7, 7, 7, 5, -1,
            5, -1, 9, 9, 9, 9, 6, -1, 6, -1, 
            9, -1, 9, -1, 8, -1, 8, -1, 12, 
            -1, 12};

        readonly byte[] NoBlendColors = new[] {          //Colors to blend with if the command does not use blending 
                                                        //The 0x80 will be cancelled in the bledning formula, so they don't change anything

                (byte)0x80, (byte)0x80 , (byte)0x80,
                (byte)0x80, (byte)0x80 , (byte)0x80,
                (byte)0x80, (byte)0x80 , (byte)0x80,

                (byte)0x80, (byte)0x80 , (byte)0x80,
                (byte)0x80, (byte)0x80 , (byte)0x80,
                (byte)0x80, (byte)0x80 , (byte)0x80,

        };

        public bool isReady => buffer.Count == numberOfParameters[opcode];

        public Polygon(uint value) {
            opcode = (value >> 24) & 0xff; 
            isGouraud = (value >> 28 & 1) == 1;
            isQuad = (value >> 27 & 1) == 1;
            isTextured = (value >> 26 & 1) == 1;
            isSemiTransparent = (value >> 25 & 1) == 1;
            isRawTextured = (value >> 24 & 1) == 1;

            vertices = new short[3 * (isQuad ? 6 : 3)];
            colors = new byte[3 * (isQuad ? 6 : 3)];
            uv = isTextured ? new ushort[2 * (isQuad ? 6 : 3)] : null;

            buffer.Add(value);

        }

      
        public void add(uint value) {
            buffer.Add(value);
        }
        public void setup() {
            switch (opcode) {

                case uint x when opcode >= 0x20 && opcode <= 0x2A:

                    for (int i = 0; i < colors.Length; i += 3) {
                        colors[i] = (byte)buffer[0];
                        colors[i + 1] = (byte)(buffer[0] >> 8);
                        colors[i + 2] = (byte)(buffer[0] >> 16);
                    }
                    loadVertices(1, 4, 1, 0);

                    if (isQuad) {
                        loadVertices(2,5,1,9);

                    }
                    texMode = -1;
                    break;


                case uint x when opcode >= 0x24 && opcode <= 0x2F:

                    if (isRawTextured) {
                        for (int i = 0; i < colors.Length; i++) {
                            colors[i] = 0x80;
                        }
                    }
                    else {
                        for (int i = 0; i < colors.Length; i += 3) {
                            colors[i] = (byte)buffer[0];
                            colors[i + 1] = (byte)(buffer[0] >> 8);
                            colors[i + 2] = (byte)(buffer[0] >> 16);
                        }
                    }

                    loadVertices(1,6,2,0);
                    loadUV(2,7,2,0);

                    if (isQuad) {
                        loadVertices(3,8,2,9);
                        loadUV(4,9,2,6);
                    }

                    clut = (ushort)(buffer[2] >> 16);
                    page = (ushort)((buffer[4] >> 16) & 0x3fff);
                    texMode = (page >> 7) & 3;
                    break;

                case uint x when opcode >= 0x30 && opcode <= 0x3A:
                    loadColors(0,5,2,0);
                    loadVertices(1,6,2,0);

                    if (isQuad) {
                        loadColors(2,7,2,9);
                        loadVertices(3,8,2,9);
                    }
                    texMode = -1;
                    break;

                case uint x when opcode >= 0x34 && opcode <= 0x3E:
                    loadColors(0,7,3,0);
                    loadVertices(1,8,3,0);
                    loadUV(2,9,3,0);

                    if (isQuad) {
                        loadColors(3,9,3,9);
                        loadVertices(4, 11, 3, 9);
                        loadUV(5, 12, 3, 6);
                    }
                    clut = (ushort)(buffer[2] >> 16);
                    page = (ushort)((buffer[5] >> 16) & 0x3fff);
                    texMode = (page >> 7) & 3;
                    break;

                default : throw new Exception("ugh");
            }

        }
        public void loadVertices(int start, int end, int step, int pointer) {
            for (int i = start; i < end ; i += step) {
                vertices[pointer++] = (short)buffer[i];
                vertices[pointer++] = (short)(buffer[i] >> 16);
                vertices[pointer++] = 0;
            }

        }
        public void loadColors(int start, int end, int step, int pointer) {
            for (int i = start; i < end; i += step) {
                colors[pointer++] = (byte)buffer[i];
                colors[pointer++] = (byte)(buffer[i] >> 8);
                colors[pointer++] = (byte)(buffer[i] >> 16);
            }

        }
        public void loadUV(int start, int end, int step, int pointer) {
            for (int i = start; i < end; i += step) {
                uv[pointer++] = (byte)buffer[i];
                uv[pointer++] = (byte)(buffer[i] >> 8);
            }

        }
        public void execute(ref Renderer window) {
            setup();

            window.draw(ref vertices,ref colors,ref uv,clut,page,texMode);
        }

      
    }
}
