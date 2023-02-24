using OpenTK.Graphics.ES20;
using PS1_Emulator.PS1_Emulator;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace PS1_Emulator {
    public class GPU {
        public Range range = new Range(0x1f801810, 5);            //Assumption  

        GP0_Command? currentCommand = null;

        Window window;

        ushort[] dummy = null;

        UInt32 img_count = 0;       //Number of words for CPU -> VRAM transfer
        List<ushort> vramData = new List<ushort>();


        public UInt16[] TexData;    //Array of texture content for VRAM -> CPU transfer
        public List<uint> tmp = new List<uint>();   //N number of vertices for lines

        UInt16 vram_x;              
        UInt16 vram_y;
        UInt16 vram_img_w;
        UInt16 vram_img_h;

        //Depth of the pixel values in a texture page
        public Dictionary<string, byte> TextureDepth = new Dictionary<string, byte>{
         { "T4Bit", 0 },
         { "T8Bit", 1 },
         { "T15Bit",2 }
        };

        //Interlaced splits each frame into 2 fields 
        public Dictionary<string, byte> Field = new Dictionary<string, byte>{
         { "Top", 1 }, //Odd lines
         { "Bottom",0 } //Even lines
        };

        public Dictionary<string, byte> VerticalRes = new Dictionary<string, byte>{
         { "Y240Lines", 0 },
         { "Y480Lines", 1 } //Only available for interlaced output
        };

        public Dictionary<string, byte> VMode = new Dictionary<string, byte>{
         { "Ntsc", 0 }, //480i 60H
         { "Pal", 1 }  //576i 50H
        };

        public Dictionary<string, byte> DisplayDepth = new Dictionary<string, byte>{
         { "D15Bits", 0 },
         { "D24Bits", 1 }
        };
        public Dictionary<string, byte> DmaDirection = new Dictionary<string, byte>{
         { "Off", 0 },
         { "Fifo", 1 },
         { "CpuToGp0",2 },
         { "VRamToCpu",3 }
        };

 

        readonly byte[] NoBlendColors = new[] {          //Colors to blend with if the command does not use blending 
                                                        //The 0x80 will be cancelled in the bledning formula, so they don't change anything

                (byte)0x80, (byte)0x80 , (byte)0x80,
                (byte)0x80, (byte)0x80 , (byte)0x80,
                (byte)0x80, (byte)0x80 , (byte)0x80,

                (byte)0x80, (byte)0x80 , (byte)0x80,
                (byte)0x80, (byte)0x80 , (byte)0x80,
                (byte)0x80, (byte)0x80 , (byte)0x80,
                 
        };


        byte page_base_x;
        byte page_base_y;
        byte semi_transparency;
        byte texture_depth;
        bool draw_to_display;
        bool dithering;
        bool force_set_mask_bit;
        bool preserve_masked_pixels;
        byte field;
        bool texture_disable;
        public HorizontalRes hrez;
        public byte vrez;
        byte vmode;
        byte display_depth;
        bool interlaced;
        bool display_disabled;
        bool interrupt;
        byte dma_direction;

        bool rectangle_texture_x_flip;
        bool rectangle_texture_y_flip;

        UInt32 texture_window_x_mask;
        UInt32 texture_window_y_mask;
        UInt32 texture_window_x_offset;
        UInt32 texture_window_y_offset;
        UInt16 drawing_area_left;
        UInt16 drawing_area_right;
        UInt16 drawing_area_top;
        UInt16 drawing_area_bottom;
        Int16 drawing_x_offset;
        Int16 drawing_y_offset;
        UInt16 display_vram_x_start;
        UInt16 display_vram_y_start;
        public UInt16 display_horiz_start;
        public UInt16 display_horiz_end;
        public UInt16 display_line_start;
        public UInt16 display_line_end;


        TIMER1 TIMER1;

        public GPU(Window w, ref TIMER1 t1) {
            this.page_base_x = 0;
            this.page_base_y = 0;
            this.semi_transparency = 0;
            this.texture_depth = TextureDepth["T4Bit"];
            this.dithering = false;
            this.force_set_mask_bit = false;
            this.draw_to_display = false;
            this.preserve_masked_pixels = false;
            this.field = Field["Top"];
            this.texture_disable = false;
            this.hrez = new HorizontalRes(0, 0);
            this.vrez = VerticalRes["Y240Lines"];
            this.vmode = VMode["Ntsc"];
            this.display_depth = DisplayDepth["D15Bits"];
            this.interlaced = false;
            this.display_disabled = true;
            this.interrupt = false;
            this.dma_direction = DmaDirection["Off"];
            this.window = w;
            this.TIMER1 = t1;


        }


        public UInt32 read_GPUSTAT() {
            UInt32 value = 0;

            value |= (((UInt32)this.page_base_x) << 0);
            value |= (((UInt32)this.page_base_y) << 4);
            value |= (((UInt32)this.semi_transparency) << 5);
            value |= (((UInt32)this.texture_depth) << 7);
            value |= ((Convert.ToUInt32(this.dithering)) << 9);
            value |= ((Convert.ToUInt32(this.draw_to_display)) << 10);
            value |= ((Convert.ToUInt32(this.force_set_mask_bit)) << 11);
            value |= ((Convert.ToUInt32(this.preserve_masked_pixels)) << 12);
            value |= (((UInt32)this.field) << 13);

            //Bit 14 is not supported 

            value |= ((Convert.ToUInt32(this.texture_disable)) << 15);
            value |= this.hrez.intoStatus();

            //Bit 19 locks the bios currently
            //value |= (((UInt32)this.vrez) << 19); 

            value |= (((UInt32)this.vmode) << 20);
            value |= (((UInt32)this.display_depth) << 21);
            value |= ((Convert.ToUInt32(this.interlaced)) << 22);
            value |= ((Convert.ToUInt32(this.display_disabled)) << 23);
            value |= ((Convert.ToUInt32(this.interrupt)) << 24);



            value |= 1 << 26;   //Ready to recive command
            value |= 1 << 27;   //Ready to send VRam to CPU
            value |= 1 << 28;   //Ready to recive DMA block

            value |= (((UInt32)this.dma_direction) << 29);

            value |= 0 << 31;   //Depends on the current line...


            UInt32 dma_request;
            switch (this.dma_direction) {
                case 0: //Off
                    dma_request = 0;
                    break;

                case 1: //Fifo (if full it should be 0)
                    dma_request = 1;
                    break;
                case 2: //CpuToGp0
                    dma_request = (value >> 28) & 1;
                    break;

                case 3: //VRam to CPU
                    dma_request = (value >> 27) & 1;
                    break;

                default:
                    throw new Exception("Invalid DMA direction: " + this.dma_direction);
            }

            value |= ((UInt32)dma_request) << 25;


            //return 0b01011110100000000000000000000000;
            return value;

        }

        double scanlines = 0;
        double scanlines_per_frame = 0;
        double video_cycles = 0;
        double video_cycles_per_scanline = 0;

        public void tick(int cycles) {
            video_cycles += cycles;
            //Console.WriteLine("GPU: " + cycles);

            if (video_cycles >= video_cycles_per_scanline && video_cycles_per_scanline > 0) {
                video_cycles -= video_cycles_per_scanline;
                scanlines++;

                if (scanlines >= scanlines_per_frame && scanlines_per_frame > 0) {
                    scanlines -= scanlines_per_frame;

                    if (!display_disabled) {
                        window.display();
                    }

                    IRQ_CONTROL.IRQsignal(0);     //VBLANK
                    //IRQ_CONTROL.IRQsignal(9);   //Fake SPU IRQ

                    this.TIMER1.GPUinVblank = true;
                }

                if (this.TIMER1.isUsingHblank()) {
                    this.TIMER1.tick();
                }

            }


        }

        public void write_GP0(UInt32 value) {

            UInt32 opcode = (value >> 24) & 0xff;
            if (currentCommand != null) {
                opcode = currentCommand.opcode;

            } else if (tmp.Count > 0) {
                opcode = (tmp[0] >> 24) & 0xff; //Lines
            }
            

            if (img_count > 0) {
                ushort pixel0 = (ushort)(value & 0xFFFF);
                ushort pixel1 = (ushort)(value >> 16);

                vramData.Add(pixel0);
                vramData.Add(pixel1);

                img_count--;

                if(img_count == 0) {
                    window.update_vram(vram_x, vram_y, vram_img_w, vram_img_h, vramData.ToArray());
                    ClearMemory(ref vramData);
                }

                return;

            }
            
            switch (opcode) {

                case 0x00:
                    //NOP
                    break;

                case 0x01:
                    //Clear cache (ignored for now)
                    break;

                case 0x2c:
                case 0x2f:
                case 0x2e:
                case 0x2d:

                    if (currentCommand == null) {
                        currentCommand = new GP0_Command(opcode, 9);
                    }
                    
                    currentCommand.add_parameter(value);

                    if (currentCommand.num_of_parameters == currentCommand.parameters_ptr) {

                        gp0_quad_texture_opaque();
                        currentCommand = null;
 
                    }

                    break;

                case 0xa0:
                    if (currentCommand == null) {
                        currentCommand = new GP0_Command(opcode, 3);
                    }

                    currentCommand.add_parameter(value);

                    if (currentCommand.num_of_parameters == currentCommand.parameters_ptr) {
                        img_count = gp0_CPUToVram_Copy();
                        currentCommand = null;
                    }

                    

                    break;

                case 0xc0:
                    if (currentCommand == null) {
                        currentCommand = new GP0_Command(opcode, 3);
                    }

                    currentCommand.add_parameter(value);

                    if (currentCommand.num_of_parameters == currentCommand.parameters_ptr) {
                        gp0_VramToCPU_Copy();
                        currentCommand = null;
                    }

                    break;

                case 0x80:
                    if (currentCommand == null) {
                        currentCommand = new GP0_Command(opcode, 4);
                    }

                    currentCommand.add_parameter(value);

                    if (currentCommand.num_of_parameters == currentCommand.parameters_ptr) {
                        gp0_VramToVram_Copy();
                        currentCommand = null;
                    }

                    break;

                case 0xe1:

                    gp0_draw_mode(value);
                    break;

                case 0xe2:

                    gp0_texture_window(value);
                    break;

                case 0xe3:

                    gp0_drawing_area_TopLeft(value);
                    break;

                case 0xe4:

                    gp0_drawing_area_BottomRight(value);
                    break;

                case 0xe5:

                    gp0_drawing_offset(value);
                    break;

                case 0xe6:

                    gp0_mask_bit(value);
                    break;


                case 0x20:  //PS logo (uses gte)?
                case 0x22:
                    if (currentCommand == null) {
                        currentCommand = new GP0_Command(opcode, 4);
                    }

                    currentCommand.add_parameter(value);

                    if (currentCommand.num_of_parameters == currentCommand.parameters_ptr) {

                        gp0_triangle_shaded_opaque();

                        currentCommand = null;

                    }

                    break;

                case 0x28:
                case 0x2A:

                    if (currentCommand == null) {
                        currentCommand = new GP0_Command(opcode, 5);
                    }

                    currentCommand.add_parameter(value);

                    if (currentCommand.num_of_parameters == currentCommand.parameters_ptr) {

                        gp0_quad_mono_opaque();
                        currentCommand = null;

                    }

                    break;

                case 0x3E:  //Transparenet (not handled) 
                case 0x3C: //opaque

                    if (currentCommand == null) {
                        currentCommand = new GP0_Command(opcode, 12);
                    }

                    currentCommand.add_parameter(value);

                    if (currentCommand.num_of_parameters == currentCommand.parameters_ptr) {

                        gp0_quad_textured_shaded(); //NEW
                        currentCommand = null;

                    }


                    break;

                case 0x34:  //Transparenet (not handled)
                case 0x36:  //opaque
                    if (currentCommand == null) {
                        currentCommand = new GP0_Command(opcode, 9);
                    }

                    currentCommand.add_parameter(value);

                    if (currentCommand.num_of_parameters == currentCommand.parameters_ptr) {

                        gp0_triangle_textured_shaded(); //NEW
                        currentCommand = null;

                    }


                    break;

                case 0x30:

                    if (currentCommand == null) {
                        currentCommand = new GP0_Command(opcode, 6);
                    }

                    currentCommand.add_parameter(value);

                    if (currentCommand.num_of_parameters == currentCommand.parameters_ptr) {

                        gp0_triangle_shaded_opaque();
                        currentCommand = null;

                    }

                    break;

                case 0x38: //opaque
                case 0x3A: //semi-transparent
                    if (currentCommand == null) {
                        currentCommand = new GP0_Command(opcode, 8);
                    }

                    currentCommand.add_parameter(value);

                    if (currentCommand.num_of_parameters == currentCommand.parameters_ptr) {

                        gp0_quad_shaded_opaque();
                        currentCommand = null;

                    }

             
                    break;

                case 0x02:  //Fill command
                case 0x60: //Monochrome Rectangle (variable size) command
                case 0x62: //Monochrome Rectangle (variable size) (semi-transparent) command
               
                    if (currentCommand == null) {
                        currentCommand = new GP0_Command(opcode, 3);
                    }

                    currentCommand.add_parameter(value);

                    if (currentCommand.num_of_parameters == currentCommand.parameters_ptr) {

                        gp0_fill_rectangle();
                        currentCommand = null;

                    }
                    break;

                case 0x68:  //1x1 Monochrome Rectangle
                    if (currentCommand == null) {
                        currentCommand = new GP0_Command(opcode, 2);
                    }

                    currentCommand.add_parameter(value);

                    if (currentCommand.num_of_parameters == currentCommand.parameters_ptr) {

                        gp0_fill_rectangle();
                        currentCommand = null;

                    }
                    break;

                case 0x64:
                case 0x65:
                case 0x66:
                case 0x67:

                    if (currentCommand == null) {
                        currentCommand = new GP0_Command(opcode, 4);
                    }

                    currentCommand.add_parameter(value);

                    if (currentCommand.num_of_parameters == currentCommand.parameters_ptr) {

                        gp0_textured_rectangle();
                        currentCommand = null;

                    }
                    break;

                case 0x74:
                case 0x75:
                case 0x7C:

                    if (currentCommand == null) {
                        currentCommand = new GP0_Command(opcode, 3);
                    }

                    currentCommand.add_parameter(value);

                    if (currentCommand.num_of_parameters == currentCommand.parameters_ptr) {

                        gp0_textured_rectangle();
                        currentCommand = null;

                    }

                    break;

                case 0x40: //opaque
                case 0x42: //semi-transparent

                    if (currentCommand == null) {
                        currentCommand = new GP0_Command(opcode, 3);
                    }

                    currentCommand.add_parameter(value);

                    if (currentCommand.num_of_parameters == currentCommand.parameters_ptr) {

                        gp0_mono_line(currentCommand);
                        tmp.Clear();
                        currentCommand = null;
                        
                        return;
                    }

                    break;

                case 0x48:  //opaque
                case 0x4A: //semi-transparent
                    if (value == 0x55555555) {

                        currentCommand = new GP0_Command(tmp);
                        gp0_mono_line(currentCommand);
                        tmp.Clear();
                        currentCommand = null;
                        return;
                    }
                    tmp.Add(value);

                    break;

                case 0x50: //opaque
                case 0x52: //semi-transparent
                    if (currentCommand == null) {
                        currentCommand = new GP0_Command(opcode, 4);
                    }

                    currentCommand.add_parameter(value);

                    if (currentCommand.num_of_parameters == currentCommand.parameters_ptr) {

                        gp0_shaded_line(currentCommand);
                        currentCommand = null;
                        return;
                    }

                    break;

                case 0x58:  //opaque
                case 0x5A: //semi-transparent
                    if (value == 0x55555555) {

                        currentCommand = new GP0_Command(tmp);
                        gp0_shaded_line(currentCommand);
                        tmp.Clear();
                        currentCommand = null;
                        return;
                    }
                    tmp.Add(value);
                    break;

                //Undocumented/Nonsense
                case 0x21:
                case 0x23:
                case 0x29:
                case 0x2B:
                case 0x31:
                case 0x33:
                case 0x39:
                case 0x3B:

                    break;

                default:

                    throw new Exception("Unhandled GP0 opcode :" + opcode.ToString("X")); 
            
            }


        }

        private void gp0_mono_line(GP0_Command currentCommand) {

            Int16[] vertices = new Int16[(currentCommand.num_of_parameters - 1) * 3];   //X,Y,Z
            byte[] colors = new byte[(currentCommand.num_of_parameters - 1) * 3];       //R,G,B
            

            for (int i = 0; i < colors.Length; i += 3) {
                colors[i] = (byte)currentCommand.buffer[0];
                colors[i+1] = (byte)(currentCommand.buffer[0] >> 8);
                colors[i+2] = (byte)(currentCommand.buffer[0] >> 16);

            }

            int j = 0;
            for (int i = 1; i < currentCommand.num_of_parameters; i++) {
                    vertices[j++] = (Int16)currentCommand.buffer[i];
                    vertices[j++] = (Int16)(currentCommand.buffer[i] >> 16);
                    vertices[j++] = 0;

            }
            
            
            window.drawLines(ref vertices, ref colors);

        }

        private void gp0_shaded_line(GP0_Command currentCommand) {
            Int16[] vertices = new Int16[(currentCommand.num_of_parameters / 2) * 3];   //X,Y,Z
            byte[] colors = new byte[(currentCommand.num_of_parameters / 2) * 3];       //R,G,B
            int j = 0;

            for (int i = 0; i < currentCommand.num_of_parameters; i+=2) {
                colors[j++] = (byte)currentCommand.buffer[i];
                colors[j++] = (byte)(currentCommand.buffer[i] >> 8);
                colors[j++] = (byte)(currentCommand.buffer[i] >> 16);

            }

            j = 0;

            for (int i = 1; i < currentCommand.num_of_parameters; i += 2) {
                vertices[j++] = (Int16)currentCommand.buffer[i];
                vertices[j++] = (Int16)(currentCommand.buffer[i] >> 16);
                vertices[j++] = 0;

            }
            
            window.drawLines(ref vertices,ref colors);

        }

        private void gp0_triangle_textured_shaded() {

            UInt32 opcode = currentCommand.opcode;

            ushort clut = (ushort)(currentCommand.buffer[2] >> 16);
            ushort page = (ushort)((currentCommand.buffer[5] >> 16) & 0x3fff);
            int texmode = (page >> 7) & 3;

            ushort tx1 = (ushort)(currentCommand.buffer[2] & 0xff);
            ushort ty1 = (ushort)((currentCommand.buffer[2] >> 8) & 0xff);

            ushort tx2 = (ushort)(currentCommand.buffer[5] & 0xff);
            ushort ty2 = (ushort)((currentCommand.buffer[5] >> 8) & 0xff);

            ushort tx3 = (ushort)(currentCommand.buffer[8] & 0xff);
            ushort ty3 = (ushort)((currentCommand.buffer[8] >> 8) & 0xff);


            ushort[] uv = {
                tx1, ty1,        //First triangle
                tx2, ty2,
                tx3, ty3,

             };


            Int16[] vertices = { //x,y,z

                (Int16)currentCommand.buffer[1], (Int16)(currentCommand.buffer[1] >> 16) , 0,
                (Int16)currentCommand.buffer[4], (Int16)(currentCommand.buffer[4] >> 16) , 0,
                (Int16)currentCommand.buffer[7], (Int16)(currentCommand.buffer[7] >> 16) , 0,



            };

            byte[] colors;

            colors = new[]  {         //r,g,b

                (byte)currentCommand.buffer[0], (byte)(currentCommand.buffer[0] >> 8) , (byte)(currentCommand.buffer[0] >> 16),
                (byte)currentCommand.buffer[3], (byte)(currentCommand.buffer[3] >> 8) , (byte)(currentCommand.buffer[3] >> 16),
                (byte)currentCommand.buffer[6], (byte)(currentCommand.buffer[6] >> 8) , (byte)(currentCommand.buffer[6] >> 16),


                 };

            window.draw(ref vertices, ref colors, ref uv, clut, page, texmode);









        }

        private void gp0_quad_textured_shaded() { 

            UInt32 opcode = currentCommand.opcode;

            ushort clut = (ushort)(currentCommand.buffer[2] >> 16);
            ushort page = (ushort)((currentCommand.buffer[5] >> 16) & 0x3fff);
            int texmode = (page >> 7) & 3;

            ushort tx1 = (ushort)(currentCommand.buffer[2] & 0xff);
            ushort ty1 = (ushort)((currentCommand.buffer[2] >> 8) & 0xff);

            ushort tx2 = (ushort)(currentCommand.buffer[5] & 0xff);
            ushort ty2 = (ushort)((currentCommand.buffer[5] >> 8) & 0xff);

            ushort tx3 = (ushort)(currentCommand.buffer[8] & 0xff);
            ushort ty3 = (ushort)((currentCommand.buffer[8] >> 8) & 0xff);

            ushort tx4 = (ushort)(currentCommand.buffer[11] & 0xff);
            ushort ty4 = (ushort)((currentCommand.buffer[11] >> 8) & 0xff);


            ushort[] uv = {
                tx1, ty1,        //First triangle
                tx2, ty2,
                tx3, ty3,

                tx2, ty2,       //Second triangle
                tx3, ty3,
                tx4, ty4
             };


            Int16[] vertices = { //x,y,z

                (Int16)currentCommand.buffer[1], (Int16)(currentCommand.buffer[1] >> 16) , 0,
                (Int16)currentCommand.buffer[4], (Int16)(currentCommand.buffer[4] >> 16) , 0,
                (Int16)currentCommand.buffer[7], (Int16)(currentCommand.buffer[7] >> 16) , 0,

                (Int16)currentCommand.buffer[4], (Int16)(currentCommand.buffer[4] >> 16) , 0,
                (Int16)currentCommand.buffer[7], (Int16)(currentCommand.buffer[7] >> 16) , 0,
                (Int16)currentCommand.buffer[10], (Int16)(currentCommand.buffer[10] >> 16) , 0


            };

            byte[] colors;

            colors = new[]  {         //r,g,b

                (byte)currentCommand.buffer[0], (byte)(currentCommand.buffer[0] >> 8) , (byte)(currentCommand.buffer[0] >> 16),
                (byte)currentCommand.buffer[3], (byte)(currentCommand.buffer[3] >> 8) , (byte)(currentCommand.buffer[3] >> 16),
                (byte)currentCommand.buffer[6], (byte)(currentCommand.buffer[6] >> 8) , (byte)(currentCommand.buffer[6] >> 16),

                (byte)currentCommand.buffer[3], (byte)(currentCommand.buffer[3] >> 8) , (byte)(currentCommand.buffer[3] >> 16),
                (byte)currentCommand.buffer[6], (byte)(currentCommand.buffer[6] >> 8) , (byte)(currentCommand.buffer[6] >> 16),
                (byte)currentCommand.buffer[9], (byte)(currentCommand.buffer[9] >> 8) , (byte)(currentCommand.buffer[9] >> 16),

                 };

            window.draw(ref vertices, ref colors, ref uv, clut, page, texmode);
        }

        private void gp0_VramToVram_Copy() {
            UInt32 yx_source = currentCommand.buffer[1];
            UInt32 yx_dest = currentCommand.buffer[2];
            UInt32 res = currentCommand.buffer[3];  


            //this is just saying if it's odd add 1
            vram_img_w = (ushort)((((res & 0xFFFF) - 1) & 0x3FF) + 1);
            vram_img_h = (ushort)((((res >> 16) - 1) & 0x1FF) + 1);
            vram_x = (UInt16)(yx_source & 0x3FF);        //0xffff ?
            vram_y = (UInt16)((yx_source >> 16) & 0x1FF);

            UInt16 vram_x_dest = (UInt16)(yx_dest & 0x3FF);        //0xffff ?
            UInt16 vram_y_dest = (UInt16)((yx_dest >> 16) & 0x3FF);        //0xffff ?

            window.VramToVramCopy(vram_x, vram_y, vram_x_dest, vram_y_dest, vram_img_w, vram_img_h);

        }

        public void ClearMemory<T>(ref List<T> list) {

            list.Clear();
            list.Capacity = 0;
            list.TrimExcess();
            
        }
        private void gp0_quad_shaded_opaque() {

            
            Int16[] vertices = { //x,y,z

           (Int16)currentCommand.buffer[1], (Int16)(currentCommand.buffer[1] >> 16) , 0,
           (Int16)currentCommand.buffer[3], (Int16)(currentCommand.buffer[3] >> 16) , 0,
           (Int16)currentCommand.buffer[5], (Int16)(currentCommand.buffer[5] >> 16) , 0,

           (Int16)currentCommand.buffer[3], (Int16)(currentCommand.buffer[3] >> 16) , 0,
           (Int16)currentCommand.buffer[5], (Int16)(currentCommand.buffer[5] >> 16) , 0,
           (Int16)currentCommand.buffer[7], (Int16)(currentCommand.buffer[7] >> 16) , 0


            };

            byte[] colors = { //r,g,b

           (byte)currentCommand.buffer[0], (byte)(currentCommand.buffer[0] >> 8) , (byte)(currentCommand.buffer[0] >> 16),
           (byte)currentCommand.buffer[2], (byte)(currentCommand.buffer[2] >> 8) , (byte)(currentCommand.buffer[2] >> 16),
           (byte)currentCommand.buffer[4], (byte)(currentCommand.buffer[4] >> 8) , (byte)(currentCommand.buffer[4] >> 16),

           (byte)currentCommand.buffer[2], (byte)(currentCommand.buffer[2] >> 8) , (byte)(currentCommand.buffer[2] >> 16),
           (byte)currentCommand.buffer[4], (byte)(currentCommand.buffer[4] >> 8) , (byte)(currentCommand.buffer[4] >> 16),
           (byte)currentCommand.buffer[6], (byte)(currentCommand.buffer[6] >> 8) , (byte)(currentCommand.buffer[6] >> 16),
            };


            window.draw(ref vertices,ref colors,ref dummy,0,0,-1);

        }

        private void gp0_VramToCPU_Copy() {

            UInt32 yx = currentCommand.buffer[1];
            UInt32 res = currentCommand.buffer[2];

            //this is just saying if it's odd add 1
            vram_img_w = (ushort)((((res & 0xFFFF) - 1) & 0x3FF) + 1);
            vram_img_h = (ushort)((((res >> 16) - 1) & 0x1FF) + 1);

            UInt16 size = (UInt16)(((vram_img_w * vram_img_h) + 1) & ~1);

            //UInt16 num_of_words = (UInt16)(size / 2);

            vram_x = (UInt16)(yx & 0x3FF);        //0xffff ?
            vram_y = (UInt16)((yx >> 16) & 0x1FF);

            TexData = new UInt16[size];

            window.readBackTexture(vram_x, vram_y, vram_img_w, vram_img_h, ref TexData);

        }

        private UInt16 gp0_CPUToVram_Copy() {
            UInt32 res = currentCommand.buffer[2];
            UInt32 yx = currentCommand.buffer[1];

            //this is just saying if it's odd add 1
            vram_img_w = (ushort)((((res & 0xFFFF) - 1) & 0x3FF) + 1);
            vram_img_h = (ushort)((((res >> 16) - 1) & 0x1FF) + 1);

            UInt16 size = (UInt16)(((vram_img_w * vram_img_h) + 1) & ~1);

            UInt16 num_of_words = (UInt16)(size / 2);


             vram_x = (UInt16)(yx & 0x3FF);        //0xffff ?
             vram_y = (UInt16)((yx >> 16) & 0x1FF);


            return num_of_words;
        }

        private void gp0_quad_mono_opaque() {
           // Debug.WriteLine("Draw quad command!");

            Int16[] vertices = { //x,y,z

           (Int16)currentCommand.buffer[1], (Int16)(currentCommand.buffer[1] >> 16) , 0,
           (Int16)currentCommand.buffer[2], (Int16)(currentCommand.buffer[2] >> 16) , 0,
           (Int16)currentCommand.buffer[3], (Int16)(currentCommand.buffer[3] >> 16) , 0,

           (Int16)currentCommand.buffer[2], (Int16)(currentCommand.buffer[2] >> 16) , 0,
           (Int16)currentCommand.buffer[3], (Int16)(currentCommand.buffer[3] >> 16) , 0,
           (Int16)currentCommand.buffer[4], (Int16)(currentCommand.buffer[4] >> 16) , 0

            };

            byte[] colors = { //r,g,b

           (byte)currentCommand.buffer[0], (byte)(currentCommand.buffer[0] >> 8) , (byte)(currentCommand.buffer[0] >> 16),
           (byte)currentCommand.buffer[0], (byte)(currentCommand.buffer[0] >> 8) , (byte)(currentCommand.buffer[0] >> 16),
           (byte)currentCommand.buffer[0], (byte)(currentCommand.buffer[0] >> 8) , (byte)(currentCommand.buffer[0] >> 16),

           (byte)currentCommand.buffer[0], (byte)(currentCommand.buffer[0] >> 8) , (byte)(currentCommand.buffer[0] >> 16),
           (byte)currentCommand.buffer[0], (byte)(currentCommand.buffer[0] >> 8) , (byte)(currentCommand.buffer[0] >> 16),
           (byte)currentCommand.buffer[0], (byte)(currentCommand.buffer[0] >> 8) , (byte)(currentCommand.buffer[0] >> 16),
          
            };


            window.draw(ref vertices, ref colors, ref dummy, 0, 0, -1);

        }

        private void gp0_mask_bit(UInt32 value) {

            this.force_set_mask_bit = ((value & 1) != 0);
            this.preserve_masked_pixels = ((value & 2) != 0);

        }

        private void gp0_texture_window(UInt32 value) {

            value &= 0xfffff;   //20 bits

            //in 8 pixel steps
            this.texture_window_x_mask = (value & 0x1f) * 8;
            this.texture_window_y_mask = ((value >> 5) & 0x1f) * 8;

            this.texture_window_x_offset = ((value >> 10) & 0x1f) * 8;
            this.texture_window_y_offset = ((value >> 15) & 0x1f) * 8;

            window.sewTextureWindow((ushort)~texture_window_x_mask, (ushort)~texture_window_y_mask, (ushort)(texture_window_x_offset & texture_window_x_mask), (ushort)(texture_window_y_offset & texture_window_y_mask));

        }
   
        private void gp0_drawing_offset(UInt32 value) {
           

            UInt16 x = (UInt16)(value & 0x7ff);
            UInt16 y = (UInt16)((value>>11) & 0x7ff);

            //Signed 11 bits, need to extend to signed 16 then shift down to 11
            this.drawing_x_offset = (Int16)(((Int16)(x << 5)) >> 5);
            this.drawing_y_offset = (Int16)(((Int16)(y << 5)) >> 5);

            window.setOffset(drawing_x_offset, drawing_y_offset,0);
           

        }

        private void gp0_drawing_area_BottomRight(UInt32 value) {
            this.drawing_area_bottom = (UInt16)((value >> 10) & 0x3ff);
            this.drawing_area_right = (UInt16)(value & 0x3ff);


            window.ScissorBox(drawing_area_left, drawing_area_top, drawing_area_right-drawing_area_left, drawing_area_bottom-drawing_area_top);
       

        }

        private void gp0_drawing_area_TopLeft(UInt32 value) {
            this.drawing_area_top = (UInt16)((value >> 10) & 0x3ff);
            this.drawing_area_left = (UInt16)(value & 0x3ff);

        }

        uint gpuInfoSelcted;
        public void write_GP1(UInt32 value) 
{
            UInt32 opcode = (value >> 24) & 0xff;

            switch (opcode) {

                case 0x00:
                    gp1_reset(value);
                    break;

                case 0x01:
                    //Clear Fifo

                    gp1_reset_command_buffer(value);

                    break;

                case 0x02:

                    gp1_acknowledge_irq(value);
                    break;


                case 0x03:

                    gp1_display_enable(value);
                    break;

                case 0x04:

                    gp1_dma_direction(value);
                    break;

                case 0x05:

                    gp1_display_VRam_start(value);
                    break;

                case 0x06:

                    gp1_display_horizontal_range(value);
                    break;

                case 0x07:

                    gp1_display_vertical_range(value);
                    break;

                case 0x08:

                    gp1_display_mode(value);
                    break;

                case 0x10:
                    gpuInfoSelcted = value;
                    break;


                default:

                    throw new Exception("Unhandled GP1 command :" + value.ToString("X") + " Opcode: " + opcode.ToString("x"));

            }


        }

        private void gp1_reset_command_buffer(uint value) {
            currentCommand = null;

            //Reset Fifo
            
        }

        private void gp1_acknowledge_irq(uint value) {

            this.interrupt = false;

        }

        private void gp1_display_enable(UInt32 value) {

            this.display_disabled = (value & 1) != 0;

        }

        private void gp1_display_vertical_range(UInt32 value) {
            this.display_line_start = (UInt16)(value & 0x3ff);
            this.display_line_end = (UInt16)((value>>10) & 0x3ff);

        }

        private void gp1_display_horizontal_range(UInt32 value) {
            this.display_horiz_start = (UInt16)(value & 0xfff);
            this.display_horiz_end = (UInt16)((value>>12) & 0xfff);

        }

        private void gp1_display_VRam_start(UInt32 value) {
            this.display_vram_x_start = (UInt16)(value & 0x3fe);
            this.display_vram_y_start = (UInt16)((value>>10) & 0x1ff);

        }

        private void gp1_dma_direction(UInt32 value) {
            switch (value & 3) {
                case 0:
                    this.dma_direction = DmaDirection["Off"];

                    break;
                case 1:
                    this.dma_direction = DmaDirection["Fifo"];

                    break;
                case 2:
                    this.dma_direction = DmaDirection["CpuToGp0"];

                    break;

                case 3:
                    this.dma_direction = DmaDirection["VRamToCpu"];

                    break;

                default:
                    throw new Exception("Unkown DMA direction: " + (value & 3));
            }




        }

        private void gp1_display_mode(UInt32 value) {
            byte hr1 = ((byte)(value & 3));
            byte hr2 = ((byte)((value >> 6) & 1));

            this.hrez = new HorizontalRes(hr1, hr2);
            
            switch ((value&0x4)!= 0) {

                case true:

                    this.vrez = VerticalRes["Y480Lines"];
                    break;

                case false:

                    this.vrez = VerticalRes["Y240Lines"];
                    break;
            }
           

            switch ((value & 0x8) != 0) {

                case true:

                    this.vmode = VMode["Pal"];
                    scanlines_per_frame = 314;
                    video_cycles_per_scanline = 3406.1;

                    break;

                case false:

                    this.vmode = VMode["Ntsc"];
                    scanlines_per_frame = 263;
                    video_cycles_per_scanline = 3413.6;
                    
                    break;
            }

            switch ((value & 0x10) != 0) {

                case true:

                    this.display_depth = DisplayDepth["D15Bits"];
                    break;

                case false:

                    this.display_depth = DisplayDepth["D24Bits"];
                    break;
            }

            this.interlaced = (value & 0x20) != 0;

            if ((value & 0x80) !=0) {
                throw new Exception("Unsupported display mode: " + value.ToString("X"));
            }

           
        }

        private void gp0_draw_mode(UInt32 value) {

            this.page_base_x = (byte)(value & 0xf);
            this.page_base_y = (byte)((value >> 4) & 1);
            this.semi_transparency = (byte)((value >> 5) & 3);
            this.texture_depth = (byte)((value >> 7) & 3);

            this.dithering = (((value >> 9) & 1) != 0);
            this.draw_to_display = (((value >> 10) & 1) != 0);
            this.texture_disable = (((value >> 11) & 1) != 0);
            this.rectangle_texture_x_flip = (((value >> 12) & 1) != 0);
            this.rectangle_texture_y_flip = (((value >> 13) & 1) != 0);


        }
        private void gp1_reset(UInt32 value) {
            this.interrupt = false;
            this.page_base_x = 0;
            this.page_base_y = 0;
            this.semi_transparency = 0;
            this.texture_depth = TextureDepth["T4Bit"];
            this.texture_window_x_mask = 0;
            this.texture_window_y_mask = 0;
            this.texture_window_x_offset = 0;
            this.texture_window_y_offset = 0;
            this.dithering = false;
            this.draw_to_display = false;
            this.texture_disable = false;
            this.rectangle_texture_x_flip = false;
            this.rectangle_texture_y_flip = false;
            this.drawing_area_bottom = 0;
            this.drawing_area_top = 0;
            this.drawing_area_left = 0;
            this.drawing_area_right = 0;
            this.drawing_x_offset = 0;
            this.drawing_y_offset = 0;
            this.force_set_mask_bit = false;
            this.preserve_masked_pixels = false;
            this.dma_direction = DmaDirection["Off"];
            this.display_disabled = true;
            this.display_vram_x_start = 0;
            this.display_vram_y_start = 0;
            this.hrez = new HorizontalRes(0, 0);
            this.vrez = VerticalRes["Y240Lines"];
            this.vmode = VMode["Ntsc"];
            this.interlaced = true;

            this.display_horiz_start = 0x200;
            this.display_horiz_end = 0xc00;

            this.display_line_start = 0x10;
            this.display_line_end = 0x100;

            this.display_depth = DisplayDepth["D15Bits"];


            //...clear Fifo

        }


        private void gp0_textured_rectangle() {

            ushort clut = (ushort)(currentCommand.buffer[2] >> 16);
            ushort page = (ushort)(page_base_x  | (page_base_y << 4));
            
            int texmode = texture_depth;
           
            ushort tx1 = (ushort)(currentCommand.buffer[2] & 0xff);
            ushort ty1 = (ushort)((currentCommand.buffer[2] >> 8) & 0xff);

            Int16 vertX = (Int16)(currentCommand.buffer[1] & 0xffff);
            Int16 vertY = (Int16)((currentCommand.buffer[1] >> 16) & 0xffff);

            Int16 w; 
            Int16 h;
            
            if (currentCommand.opcode == 0x7C) {
                h = 16; 
                w = 16;

            }
            else if (currentCommand.opcode == 0x74 || currentCommand.opcode == 0x75) {
                h = 8;
                w = 8;
            }
            else {  //(max 1023x511)
                w = (Int16)(currentCommand.buffer[3] & 0x3ff); 
                h = (Int16)((currentCommand.buffer[3] >> 16) & 0x1ff);
            }

            byte[] colors;

            switch (currentCommand.opcode) {
                case 0x64:
                case 0x66:
                case 0x74:
                case 0x7C:

                    //Bleding Color 
                    colors = new[]  {         //r,g,b

                (byte)currentCommand.buffer[0], (byte)(currentCommand.buffer[0] >> 8) , (byte)(currentCommand.buffer[0] >> 16),
                (byte)currentCommand.buffer[0], (byte)(currentCommand.buffer[0] >> 8) , (byte)(currentCommand.buffer[0] >> 16),
                (byte)currentCommand.buffer[0], (byte)(currentCommand.buffer[0] >> 8) , (byte)(currentCommand.buffer[0] >> 16),

                (byte)currentCommand.buffer[0], (byte)(currentCommand.buffer[0] >> 8) , (byte)(currentCommand.buffer[0] >> 16),
                (byte)currentCommand.buffer[0], (byte)(currentCommand.buffer[0] >> 8) , (byte)(currentCommand.buffer[0] >> 16),
                (byte)currentCommand.buffer[0], (byte)(currentCommand.buffer[0] >> 8) , (byte)(currentCommand.buffer[0] >> 16),

                 };
                    break;

                case 0x65:
                case 0x67:
                case 0x75:

                    colors = NoBlendColors;

                    break;

                default:
                    throw new Exception("opcode: " + currentCommand.opcode.ToString("x"));
            }

            Int16[] vertices = { //x,y,z

                 vertX, vertY , 0,
                (short)(vertX+w),vertY, 0,
                 vertX, (short)(vertY+h) ,0,

                (short)(vertX+w),vertY, 0,
                (short)(vertX+w),(short)(vertY+h),0,
                vertX, (short)(vertY+h) ,0,


            };

            ushort[] uv = {
                tx1, ty1,        //First triangle
                (ushort)(tx1+w), ty1,
                tx1, (ushort)(ty1+h),

                (ushort)(tx1+w), ty1,       //Second triangle
                (ushort)(tx1+w), (ushort)(ty1+h),
                tx1, (ushort)(ty1+h)
             };

            
            window.draw(ref vertices, ref colors, ref uv, clut, page, texmode);



        }

        private void gp0_fill_rectangle() {
            uint color = (uint)currentCommand.buffer[0];             //First command contains color

            ushort x = (ushort)(currentCommand.buffer[1] & 0x3FF);
            ushort y = (ushort)((currentCommand.buffer[1] >> 16) & 0x1FF);

            //only vram fill?
            //int width = (int)(((currentCommand.buffer[2] & 0x3FF) + 0x0F) & (~0x0F));
            //int height = (int)((currentCommand.buffer[2] >> 16) & 0x1FF);

            float r = ((float)(color & 0xff)) / 255.0f;                  //Scale to float of range [0,1]
            float g = ((float)((color >> 8) & 0xff)) / 255.0f;           //because it is going to be passed 
            float b = ((float)((color >> 16) & 0xff)) / 255.0f;          //directly to GL.clear()

            ushort width;
            ushort height;

            switch (currentCommand.opcode) {
                case 0x68:
                    width = height = 1;

                    window.rectFill(r, g, b, x, y, width, height);
                    return;

                
                case 0x60:  //0x60 causes problem in ridge racer
                case 0x62:
                    width = (ushort)(currentCommand.buffer[2] & 0x3FF);
                    height = (ushort)((currentCommand.buffer[2] >> 16) & 0x1FF);
                    window.rectFill(r, g, b, x, y, width, height);
                    return;

                case 0x02:

                    width = (ushort)(currentCommand.buffer[2] & 0x3FF);
                    height = (ushort)((currentCommand.buffer[2] >> 16) & 0x1FF);
                    window.vramFill(r, g, b, x, y, width, height);

                    break;

                default:
                    throw new Exception("GP0 opcode: " + currentCommand.opcode.ToString("x"));
            }
         

           

        }

        private void gp0_quad_texture_opaque() {
            //Puzzle bubble 2 background bugged here 
            UInt32 opcode = currentCommand.opcode;

            ushort clut = (ushort)(currentCommand.buffer[2] >> 16);
            ushort page = (ushort)((currentCommand.buffer[4] >> 16) & 0x3fff);
            int texmode = (page >> 7) & 3;

            ushort tx1 = (ushort)(currentCommand.buffer[2] & 0xff);
            ushort ty1 = (ushort)((currentCommand.buffer[2] >> 8) & 0xff);

            ushort tx2 = (ushort)(currentCommand.buffer[4] & 0xff);
            ushort ty2 = (ushort)((currentCommand.buffer[4] >> 8) & 0xff);

            ushort tx3 = (ushort)(currentCommand.buffer[6] & 0xff);
            ushort ty3 = (ushort)((currentCommand.buffer[6] >> 8) & 0xff);

            ushort tx4 = (ushort)(currentCommand.buffer[8] & 0xff);
            ushort ty4 = (ushort)((currentCommand.buffer[8] >> 8) & 0xff);


            ushort[] uv = {
                tx1, ty1,        //First triangle
                tx2, ty2, 
                tx3, ty3,

                tx2, ty2,       //Second triangle
                tx3, ty3,
                tx4, ty4
             };


            Int16[] vertices = { //x,y,z

                (Int16)currentCommand.buffer[1], (Int16)(currentCommand.buffer[1] >> 16) , 0, 
                (Int16)currentCommand.buffer[3], (Int16)(currentCommand.buffer[3] >> 16) , 0,
                (Int16)currentCommand.buffer[5], (Int16)(currentCommand.buffer[5] >> 16) , 0,

                (Int16)currentCommand.buffer[3], (Int16)(currentCommand.buffer[3] >> 16) , 0,
                (Int16)currentCommand.buffer[5], (Int16)(currentCommand.buffer[5] >> 16) , 0,
                (Int16)currentCommand.buffer[7], (Int16)(currentCommand.buffer[7] >> 16) , 0


            };

            byte[] colors;

            switch (opcode) {
                case 0x2c:
                case 0x2e:
                    //Bleding Color 
                 colors = new[]  {         //r,g,b

                (byte)currentCommand.buffer[0], (byte)(currentCommand.buffer[0] >> 8) , (byte)(currentCommand.buffer[0] >> 16),
                (byte)currentCommand.buffer[0], (byte)(currentCommand.buffer[0] >> 8) , (byte)(currentCommand.buffer[0] >> 16),
                (byte)currentCommand.buffer[0], (byte)(currentCommand.buffer[0] >> 8) , (byte)(currentCommand.buffer[0] >> 16),

                (byte)currentCommand.buffer[0], (byte)(currentCommand.buffer[0] >> 8) , (byte)(currentCommand.buffer[0] >> 16),
                (byte)currentCommand.buffer[0], (byte)(currentCommand.buffer[0] >> 8) , (byte)(currentCommand.buffer[0] >> 16),
                (byte)currentCommand.buffer[0], (byte)(currentCommand.buffer[0] >> 8) , (byte)(currentCommand.buffer[0] >> 16),

                 };
                    break;

                case 0x2f:
                case 0x2d:
                    colors = NoBlendColors;
                    break;

                default:
                    throw new Exception("opcode: " + opcode.ToString("x"));
            }

            window.draw(ref vertices, ref colors, ref uv, clut, page, texmode);
           
        }

      

        private void gp0_triangle_shaded_opaque() {


            Int16[] vertices;
            byte[] colors;

            switch (currentCommand.opcode) {
                case 0x20:
                case 0x22:
                    vertices = new Int16[]{ //x,y,z

                    (Int16)currentCommand.buffer[1], (Int16)(currentCommand.buffer[1] >> 16) , 0,
                    (Int16)currentCommand.buffer[2], (Int16)(currentCommand.buffer[2] >> 16) , 0,
                    (Int16)currentCommand.buffer[3], (Int16)(currentCommand.buffer[3] >> 16) , 0

                     };
                    colors = new byte[]{ //r,g,b

                    (byte)currentCommand.buffer[0], (byte)(currentCommand.buffer[0] >> 8) , (byte)(currentCommand.buffer[0] >> 16),
                    (byte)currentCommand.buffer[0], (byte)(currentCommand.buffer[0] >> 8) , (byte)(currentCommand.buffer[0] >> 16),
                    (byte)currentCommand.buffer[0], (byte)(currentCommand.buffer[0] >> 8) , (byte)(currentCommand.buffer[0] >> 16)

                    };

                    break;

                case 0x30:
                    vertices = new Int16[]{ //x,y,z

                    (Int16)currentCommand.buffer[1], (Int16)(currentCommand.buffer[1] >> 16) , 0,
                    (Int16)currentCommand.buffer[3], (Int16)(currentCommand.buffer[3] >> 16) , 0,
                    (Int16)currentCommand.buffer[5], (Int16)(currentCommand.buffer[5] >> 16) , 0

                     };

                    colors = new byte[]{ //r,g,b

                    (byte)currentCommand.buffer[0], (byte)(currentCommand.buffer[0] >> 8) , (byte)(currentCommand.buffer[0] >> 16),
                    (byte)currentCommand.buffer[2], (byte)(currentCommand.buffer[2] >> 8) , (byte)(currentCommand.buffer[2] >> 16),
                    (byte)currentCommand.buffer[4], (byte)(currentCommand.buffer[4] >> 8) , (byte)(currentCommand.buffer[4] >> 16)

                    };
                    break;

                default:
                    throw new Exception((currentCommand.opcode).ToString("x"));
                   
            }

            window.draw(ref vertices, ref colors, ref dummy, 0, 0, -1);


        }

        internal uint gpuReadReg() {
            uint tmp = gpuInfoSelcted;
            while (tmp > 0x07) {
                tmp -= 0x8;
            }
 
            switch (tmp) {
               
                case 0x2:

                    return (texture_window_x_mask | (texture_window_y_mask << 5) | (texture_window_x_offset << 10) | (texture_window_y_offset << 15));
                
                case 0x3:

                    return ((uint)(drawing_area_left | drawing_area_top << 10));

                case 0x4:

                    return ((uint)(drawing_area_right | drawing_area_bottom << 10));

                case 0x5:
                    uint y = (uint)drawing_y_offset;
                    uint x = (uint)drawing_x_offset;

                    return (uint)(x | (y << 11));

                default:
                    return 0x808080;

        }
            
        }
    }

    public class HorizontalRes {
        byte HR;
        byte HR1;
        byte HR2;
    public HorizontalRes(byte hr1, byte hr2) { 

        this.HR = (byte) ((hr2 & 1) | ((hr1 & 3) << 1));
        this.HR1 = hr1;
        this.HR2 = hr2;

        }

        public float getHR() {
            if (HR2 == 1) {
                return 368f;
            }
            else {
                switch (HR1) {
                    case 0: return 256f;
                    case 1: return 320f;
                    case 2: return 512f;
                    case 3: return 640f;

                }

            }

            return 0f;  

        }
        public UInt32 intoStatus() {      //Bits [18:16] of GPUSTAT
            return ((UInt32)this.HR) << 16;
        }

    }

}
