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
    internal class GPU {
        public Range range = new Range(0x1f801810, 5);            //Assumption 

        bool mono_quadrilateral = false;
        bool textured_quadrilateral = false;
        bool shaded_triangle = false;
        bool shaded_quadrilateral = false;
        bool fill_rectangle = false;
        public bool TexturedRectangleVariable = false;
        public bool TexturedRectangleStatic = false;

        int  static_width;
        int  static_height; 

        bool MonochromeRectangleVariable = false;

        Window window;

        ushort[] dummy = null;

        UInt32 img_count = 0;

        List<UInt32> BufArray = new List<UInt32>();
        List<UInt32> ImageArray = new List<UInt32>();


        bool storing_img = false;       //VRAM TO CPU

        //For CPU -> Vram transfer 
        bool loading_img = false;
        public UInt16[] TexData;

        UInt16 vram_x;
        UInt16 vram_y;

        UInt16 vram_img_w;
        UInt16 vram_img_h;

        List<ushort> vramData = new List<ushort>();



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
        HorizontalRes hrez;
        byte vrez;
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
        UInt16 display_horiz_start;
        UInt16 display_horiz_end;
        UInt16 display_line_start;
        UInt16 display_line_end;


        UInt32 evenodd;

        private int vblank = 0;
        private int hblank = 0;

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
            this.evenodd = 0;
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

        int vblank_counter = 0;
        int hblank_counter = 0;

        public void tick(int cycles) {
            vblank_counter += cycles;
            hblank_counter += cycles;

            if (vblank_counter >= vblank && vblank > 0) {
                vblank_counter = 0;
                window.display();
                IRQ_CONTROL.IRQsignal(0);

                this.TIMER1.GPUinVblank = true;

            }

            if (hblank_counter >= hblank && hblank > 0) {
                hblank_counter = 0;
                if (this.TIMER1.isUsingHblank()) {
                     this.TIMER1.tick();
                }

            }

        }

        public void write_GP0(UInt32 value) {

            UInt32 opcode = (value >> 24) & 0xff;
            //Debug.WriteLine(opcode.ToString("x"));

            if (!readyToDecode(value)) { 
                return;
            }
            switch (opcode) {

                case 0x00:
                    //NOP
                    break;


                case 0x01:
                    //Clear cache (ignore for now)
                    break;

                case 0x2c:
                case 0x2f:
                case 0x2e:
           
                    ClearMemory(ref BufArray);
                    BufArray.Add(value);
                    this.textured_quadrilateral = true;

                    break;

                case 0xa0:

                    ClearMemory(ref ImageArray);
                    ImageArray.Add(value);
                    this.loading_img = true;
                    break;

                case 0xc0:

                    ClearMemory(ref ImageArray);
                    ImageArray.Add(value);
                    this.storing_img = true;
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

              

                case 0x28:
                case 0x2A:
                    ClearMemory(ref BufArray);
                    BufArray.Add(value);
                    this.mono_quadrilateral = true;
                    break;

                case 0x30:

                    ClearMemory(ref BufArray);
                    BufArray.Add(value);
                    this.shaded_triangle = true;
                    break;

                case 0x38:

                    ClearMemory(ref BufArray);
                    BufArray.Add(value);
                    this.shaded_quadrilateral = true;
                    break;

                case 0x02:

                    ClearMemory(ref BufArray);
                    BufArray.Add(value);
                    this.fill_rectangle = true;
                    break;

                case 0x2d:

                    ClearMemory(ref BufArray);
                    BufArray.Add(value);
                    this.textured_quadrilateral = true;
                    break;

                case 0x60:

                    ClearMemory(ref BufArray);
                    BufArray.Add(value);
                    this.MonochromeRectangleVariable = true;
                    break;

                case 0x7C:

                    ClearMemory(ref BufArray);
                    BufArray.Add(value);
                    this.TexturedRectangleStatic = true;
                    static_height = 16;
                    static_width = 16;

                    break;


                case 0x64:
                case 0x65:
                case 0x66:
                case 0x67:
                    ClearMemory(ref BufArray);
                    BufArray.Add(value);
                    this.TexturedRectangleVariable = true;
                    break;

                default:

                    throw new Exception("Unhandled GP0 opcode :" + opcode.ToString("X")); 
            
            }


        }
        public void ClearMemory<T>(ref List<T> list) {

            list.Clear();
            list.Capacity = 0;
            list.TrimExcess();
            
        }
        private void gp0_quad_shaded_opaque() {

            
            Int16[] vertices = { //x,y,z

           (Int16)BufArray[1], (Int16)(BufArray[1] >> 16) , 0,
           (Int16)BufArray[3], (Int16)(BufArray[3] >> 16) , 0,
           (Int16)BufArray[5], (Int16)(BufArray[5] >> 16) , 0,

           (Int16)BufArray[3], (Int16)(BufArray[3] >> 16) , 0,
           (Int16)BufArray[5], (Int16)(BufArray[5] >> 16) , 0,
           (Int16)BufArray[7], (Int16)(BufArray[7] >> 16) , 0


            };

            byte[] colors = { //r,g,b

           (byte)BufArray[0], (byte)(BufArray[0] >> 8) , (byte)(BufArray[0] >> 16),
           (byte)BufArray[2], (byte)(BufArray[2] >> 8) , (byte)(BufArray[2] >> 16),
           (byte)BufArray[4], (byte)(BufArray[4] >> 8) , (byte)(BufArray[4] >> 16),

           (byte)BufArray[2], (byte)(BufArray[2] >> 8) , (byte)(BufArray[2] >> 16),
           (byte)BufArray[4], (byte)(BufArray[4] >> 8) , (byte)(BufArray[4] >> 16),
           (byte)BufArray[6], (byte)(BufArray[6] >> 8) , (byte)(BufArray[6] >> 16),
            };


            window.draw(ref vertices,ref colors,ref dummy,0,0,-1);

        }

        private void gp0_image_store() {

            UInt32 yx = this.ImageArray[1];
            UInt32 res = this.ImageArray[2];

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

        private UInt16 gp0_image_Load() {
            UInt32 res = this.ImageArray[2];
            UInt32 yx = this.ImageArray[1];

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

           (Int16)BufArray[1], (Int16)(BufArray[1] >> 16) , 0,
           (Int16)BufArray[2], (Int16)(BufArray[2] >> 16) , 0,
           (Int16)BufArray[3], (Int16)(BufArray[3] >> 16) , 0,

           (Int16)BufArray[2], (Int16)(BufArray[2] >> 16) , 0,
           (Int16)BufArray[3], (Int16)(BufArray[3] >> 16) , 0,
           (Int16)BufArray[4], (Int16)(BufArray[4] >> 16) , 0

            };

            byte[] colors = { //r,g,b

           (byte)BufArray[0], (byte)(BufArray[0] >> 8) , (byte)(BufArray[0] >> 16),
           (byte)BufArray[0], (byte)(BufArray[0] >> 8) , (byte)(BufArray[0] >> 16),
           (byte)BufArray[0], (byte)(BufArray[0] >> 8) , (byte)(BufArray[0] >> 16),

           (byte)BufArray[0], (byte)(BufArray[0] >> 8) , (byte)(BufArray[0] >> 16),
           (byte)BufArray[0], (byte)(BufArray[0] >> 8) , (byte)(BufArray[0] >> 16),
           (byte)BufArray[0], (byte)(BufArray[0] >> 8) , (byte)(BufArray[0] >> 16),
          
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
            this.shaded_triangle = false;
            this.mono_quadrilateral = false;
            this.textured_quadrilateral = false;

            this.loading_img = false;

            ClearMemory(ref ImageArray);
            ClearMemory(ref BufArray);



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
            byte hr2 = ((byte)((value>>6) & 1));

            this.hrez = new HorizontalRes(hr1, hr2);

            switch ((value&0x4)!=0) {

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
                    vblank = (int)(3406 * 314);
                    hblank = 3406;


                    break;

                case false:

                    this.vmode = VMode["Ntsc"];
                    vblank = (int)(3413 * 263);
                    hblank = 3413;




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



   private bool readyToDecode(UInt32 value) {

            if (loading_img || storing_img) {
                ImageArray.Add(value);

                if (ImageArray.Count == 3) {
                    if (loading_img) {
                        this.img_count = gp0_image_Load();
                        this.loading_img = false;
                    }
                    else {
                        gp0_image_store();
                        this.storing_img = false;
                    }
                  

                }

                return false;
            }

            if (img_count != 0) { //Image data must be sent to vram 

                ushort pixel0 = (ushort)(value & 0xFFFF);
                ushort pixel1 = (ushort)(value >> 16);

                vramData.Add(pixel0);
                vramData.Add(pixel1);

                img_count--;

                if (img_count == 0) {
                    window.update_vram(vram_x, vram_y, vram_img_w, vram_img_h, vramData.ToArray());
                    ClearMemory(ref vramData);
                }


                return false;
            }



            int limit = -1;
            if (mono_quadrilateral) {

                limit = 5;
            }
            else if (textured_quadrilateral) {
                limit = 9;

            }
            else if (shaded_triangle) {
                limit = 6;
            }
            else if (shaded_quadrilateral) {
                limit = 8;
            }
            else if (fill_rectangle || MonochromeRectangleVariable || TexturedRectangleStatic) {
                limit = 3;
            }
            else if (TexturedRectangleVariable) {
                limit = 4;

            }else {
                return true;
            }

            BufArray.Add(value);

            if (BufArray.Count == limit) {
                if (mono_quadrilateral) {

                    gp0_quad_mono_opaque();
                    this.mono_quadrilateral = false;

                }
                else if (textured_quadrilateral) {

                    gp0_quad_texture_opaque();
                    this.textured_quadrilateral = false;

                }
                else if (shaded_triangle) {

                    gp0_triangle_shaded_opaque();
                    this.shaded_triangle = false;


                }
                else if (shaded_quadrilateral) {

                    gp0_quad_shaded_opaque();
                    this.shaded_quadrilateral = false;


                }
                else if (fill_rectangle) {

                    gp0_fill_rectangle();
                    this.fill_rectangle = false;


                }
                else if (TexturedRectangleVariable) {

                    gp0_textured_rectangle();
                    this.TexturedRectangleVariable = false;


                }else if (MonochromeRectangleVariable) {

                    gp0_fill_rectangle();
                    this.MonochromeRectangleVariable = false;

                }
                else if (TexturedRectangleStatic) {

                    gp0_textured_rectangle_static();
                    this.TexturedRectangleStatic = false;
                }
            }          
              

           return false;



        }

        private void gp0_textured_rectangle_static() {
            throw new Exception("Doesn't work?");

            UInt32 opcode = (BufArray[0] >> 24) & 0xff;

            ushort clut = (ushort)(BufArray[2] >> 16);
            ushort page = (ushort)(page_base_x | (page_base_y << 4));

            int texmode = texture_depth;

            ushort tx1 = (ushort)(BufArray[2] & 0xff);
            ushort ty1 = (ushort)((BufArray[2] >> 8) & 0xff);

            Int16 vertX = (Int16)(BufArray[1] & 0xffff);
            Int16 vertY = (Int16)((BufArray[1] >> 16) & 0xffff);


            Int16[] vertices = { //x,y,z

                 vertX, vertY , 0,
                (short)(vertX+static_width),vertY, 0,
                 vertX, (short)(vertY+static_height) ,0,

                (short)(vertX+static_width),vertY, 0,
                (short)(vertX+static_width),(short)(vertY+static_height),0,
                vertX, (short)(vertY+static_height) ,0,

            };
            ushort[] uv = {
                tx1, ty1,        //First triangle
                (ushort)(tx1+static_width), ty1,
                tx1, (ushort)(ty1+static_height),

                (ushort)(tx1+static_width), ty1,       //Second triangle
                (ushort)(tx1+static_width), (ushort)(ty1+static_height),
                tx1, (ushort)(ty1+static_height)
             };

            byte[] colors;

            switch (opcode) {
                case 0x7C:
                    //Bleding Color 
                    colors = new[]  {         //r,g,b

                (byte)BufArray[0], (byte)(BufArray[0] >> 8) , (byte)(BufArray[0] >> 16),
                (byte)BufArray[0], (byte)(BufArray[0] >> 8) , (byte)(BufArray[0] >> 16),
                (byte)BufArray[0], (byte)(BufArray[0] >> 8) , (byte)(BufArray[0] >> 16),

                (byte)BufArray[0], (byte)(BufArray[0] >> 8) , (byte)(BufArray[0] >> 16),
                (byte)BufArray[0], (byte)(BufArray[0] >> 8) , (byte)(BufArray[0] >> 16),
                (byte)BufArray[0], (byte)(BufArray[0] >> 8) , (byte)(BufArray[0] >> 16),

                 };
                    break;


                default:
                    throw new Exception("opcode: " + opcode.ToString("x"));
            }


            window.draw(ref vertices, ref colors, ref uv, clut, page, texmode);
        }

        private void gp0_textured_rectangle() {

            UInt32 opcode = (BufArray[0] >> 24) & 0xff;

            ushort clut = (ushort)(BufArray[2] >> 16);
            ushort page = (ushort)(page_base_x | (page_base_y << 4));

            int texmode = texture_depth;
           
            ushort tx1 = (ushort)(BufArray[2] & 0xff);
            ushort ty1 = (ushort)((BufArray[2] >> 8) & 0xff);

            Int16 vertX = (Int16)(BufArray[1] & 0xffff);
            Int16 vertY = (Int16)((BufArray[1] >> 16) & 0xffff);

            Int16 w = (Int16)(BufArray[3] & 0xffff);
            Int16 h = (Int16)((BufArray[3] >> 16) & 0xffff);

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

            byte[] colors;

            switch (opcode) {
                case 0x64:
                case 0x66:
                    //Bleding Color 
                    colors = new[]  {         //r,g,b

                (byte)BufArray[0], (byte)(BufArray[0] >> 8) , (byte)(BufArray[0] >> 16),
                (byte)BufArray[0], (byte)(BufArray[0] >> 8) , (byte)(BufArray[0] >> 16),
                (byte)BufArray[0], (byte)(BufArray[0] >> 8) , (byte)(BufArray[0] >> 16),

                (byte)BufArray[0], (byte)(BufArray[0] >> 8) , (byte)(BufArray[0] >> 16),
                (byte)BufArray[0], (byte)(BufArray[0] >> 8) , (byte)(BufArray[0] >> 16),
                (byte)BufArray[0], (byte)(BufArray[0] >> 8) , (byte)(BufArray[0] >> 16),

                 };
                    break;

                case 0x65:
                case 0x67:
                    colors = NoBlendColors;

                    break;

                default:
                    throw new Exception("opcode: " + opcode.ToString("x"));
            }


            window.draw(ref vertices, ref colors, ref uv, clut, page, texmode);



        }

        private void gp0_fill_rectangle() {
            uint color = (uint)BufArray[0];             //First command contains color

            int x = (int)(BufArray[1] & 0x3F0);
            int y = (int)((BufArray[1] >> 16) & 0x1FF);

            int width = (int)(((BufArray[2] & 0x3FF) + 0x0F) & (~0x0F));
            int height = (int)((BufArray[2] >> 16) & 0x1FF);

            float r = (float)((color & 0xff) / 255.0);                  //Scale to float of range [0,1]
            float g = (float)(((color >> 8) & 0xff) / 255.0);           //because it is going to be passed 
            float b = (float)(((color >> 16) & 0xff) / 255.0);          //directly to GL.clear()

            window.fill(r,g,b,x,y,width,height);
        }

        private void gp0_quad_texture_opaque() {
            UInt32 opcode = (BufArray[0] >> 24) & 0xff;

            ushort clut = (ushort)(BufArray[2] >> 16);
            ushort page = (ushort)((BufArray[4] >> 16) & 0x3fff);
            int texmode = (page >> 7) & 3;

            ushort tx1 = (ushort)(BufArray[2] & 0xff);
            ushort ty1 = (ushort)((BufArray[2] >> 8) & 0xff);

            ushort tx2 = (ushort)(BufArray[4] & 0xff);
            ushort ty2 = (ushort)((BufArray[4] >> 8) & 0xff);

            ushort tx3 = (ushort)(BufArray[6] & 0xff);
            ushort ty3 = (ushort)((BufArray[6] >> 8) & 0xff);

            ushort tx4 = (ushort)(BufArray[8] & 0xff);
            ushort ty4 = (ushort)((BufArray[8] >> 8) & 0xff);


            ushort[] uv = {
                tx1, ty1,        //First triangle
                tx2, ty2, 
                tx3, ty3,

                tx2, ty2,       //Second triangle
                tx3, ty3,
                tx4, ty4
             };


            Int16[] vertices = { //x,y,z

                (Int16)BufArray[1], (Int16)(BufArray[1] >> 16) , 0, 
                (Int16)BufArray[3], (Int16)(BufArray[3] >> 16) , 0,
                (Int16)BufArray[5], (Int16)(BufArray[5] >> 16) , 0,

                (Int16)BufArray[3], (Int16)(BufArray[3] >> 16) , 0,
                (Int16)BufArray[5], (Int16)(BufArray[5] >> 16) , 0,
                (Int16)BufArray[7], (Int16)(BufArray[7] >> 16) , 0


            };

            byte[] colors;

            switch (opcode) {
                case 0x2c:
                case 0x2e:
                    //Bleding Color 
                 colors = new[]  {         //r,g,b

                (byte)BufArray[0], (byte)(BufArray[0] >> 8) , (byte)(BufArray[0] >> 16),
                (byte)BufArray[0], (byte)(BufArray[0] >> 8) , (byte)(BufArray[0] >> 16),
                (byte)BufArray[0], (byte)(BufArray[0] >> 8) , (byte)(BufArray[0] >> 16),

                (byte)BufArray[0], (byte)(BufArray[0] >> 8) , (byte)(BufArray[0] >> 16),
                (byte)BufArray[0], (byte)(BufArray[0] >> 8) , (byte)(BufArray[0] >> 16),
                (byte)BufArray[0], (byte)(BufArray[0] >> 8) , (byte)(BufArray[0] >> 16),

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

            Int16[] vertices = { //x,y,z

           (Int16)BufArray[1], (Int16)(BufArray[1] >> 16) , 0,
           (Int16)BufArray[3], (Int16)(BufArray[3] >> 16) , 0,
           (Int16)BufArray[5], (Int16)(BufArray[5] >> 16) , 0

            };

            byte[] colors = { //r,g,b

           (byte)BufArray[0], (byte)(BufArray[0] >> 8) , (byte)(BufArray[0] >> 16),
           (byte)BufArray[2], (byte)(BufArray[2] >> 8) , (byte)(BufArray[2] >> 16),
           (byte)BufArray[4], (byte)(BufArray[4] >> 8) , (byte)(BufArray[4] >> 16)

            };



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

   class HorizontalRes {
        byte HR;
    public HorizontalRes(byte hr1, byte hr2) { 

        this.HR = (byte) ((hr2&1) | ((hr1 & 3) << 1));

        }

        public byte getHR() {
            return this.HR;
        }
        public UInt32 intoStatus() {      //Bits [18:16] of GPUSTAT
            return ((UInt32)this.HR) << 16;
        }

    }

}
