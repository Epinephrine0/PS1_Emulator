using OpenTK.Graphics.ES20;
using PSXEmulator.Peripherals.GPU;
using PSXEmulator.Peripherals.Timers;
using PSXEmulator.PS1_Emulator;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using static PSXEmulator.CDROMDataController;

namespace PSXEmulator {
    public class GPU {
        public Range range = new Range(0x1f801810, 5);
        public Renderer window;

        public enum VerticalRes {
            Y240Lines = 0,
            Y480Lines = 1
        }
        public enum DmaDirection {
            Off = 0,
            Fifo = 1,
            CpuToGp0 = 2,
            VRamToCpu = 3
        }
        public enum TransferType { //My own states, because the CPU fucks up the main DMA direction above in the middle of a transfer
            Off = 0x0,
            VramFill = 0x2,
            VramToVram = 0x4,     
            CpuToVram = 0x5,
            VramToCpu = 0x6,
        }
        enum VMode {
            NTSC = 0,
            PAL = 1
        }
        enum DisplayDepth {
            D15Bits = 0,
            D24Bits = 1
        }
        
        public VerticalRes verticalRes;
        VMode vmode;
        DisplayDepth displayDepth;
        DmaDirection dmaDirection;

        uint gpuInfoSelcted; //For GPUREAD

        byte page_base_x;
        byte page_base_y;
        byte semi_transparency;
        byte texture_depth;
        bool draw_to_display;
        bool dithering;
        public bool force_set_mask_bit;
        public bool preserve_masked_pixels;
        byte field;
        bool texture_disable;
        public HorizontalRes horizontalRes;
        bool interlaced;
        bool display_disabled;
        bool interrupt;
        int currentLine = 0; //Even/Odd flipping

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
        public UInt16 display_vram_x_start;
        public UInt16 display_vram_y_start;
        public UInt16 display_horiz_start;
        public UInt16 display_horiz_end;
        public UInt16 display_line_start;
        public UInt16 display_line_end;
        Primitive primitive = null;
        public enum GPUState {
            LoadingPrimitive,
            Idle,
            Transferring
        }
        /*public struct GPUTransfer {
            public uint[] parameters;
            public ushort[] data;
            public int dataPtr;
            public int paramPtr;
            public bool paramsReady => paramPtr == parameters.Length;
            private uint resolution => parameters[2];


            //Xsiz=((Xsiz AND 3FFh)+0Fh) AND (NOT 0Fh) for vram fill?
            public ushort width => (ushort)(transferType == TransferType.VramFill?
                ((resolution & 0x3FF) + 0x0F) & (~ 0x0F) : ((((resolution & 0x3FFF) - 1) & 0x3FF) + 1));

            public ushort height => (ushort)(transferType == TransferType.VramFill?
              ((resolution >> 16) & 0x1FF) : ((((resolution >> 16) - 1) & 0x1FF) + 1));

            public uint size => (uint)((((width > 0 ? (uint)width : 0x400) * (height > 0 ? (uint)height : 0x200)) + 1) & ~1);
            public bool dataEnd => size == dataPtr;
            public ushort destination_X => (ushort)(parameters[1] & (transferType == TransferType.VramFill ? 0x3F0 : 0x3FF));
            public ushort destination_Y => (ushort)((parameters[1] >> 16) & 0x1FF);
            public float fillColor_R => (float)(((parameters[0] >> 3) & 0x1F) / 31.0f);
            public float fillColor_G => (float)(((parameters[0] >> 11) & 0x1F) / 31.0f);
            public float fillColor_B => (float)(((parameters[0] >> 19) & 0x1F) / 31.0f);

            public TransferType transferType;        //CPU seems to set the main direction to off in the middle
                                                    //of the transfer, this should keep the direction saved
        }
        public GPUTransfer gpuTransfer;*/

        public GPU_MemoryTransfer CurrentTransfare;
        public GPUState currentState = GPUState.Idle;
        
        Timer0 TIMER0;
        Timer1 TIMER1;

        public GPU(Renderer rederingWindow, ref Timer0 timer0, ref Timer1 timer1) {
            this.page_base_x = 0;
            this.page_base_y = 0;
            this.semi_transparency = 0;
            this.dithering = false;
            this.force_set_mask_bit = false;
            this.draw_to_display = false;
            this.preserve_masked_pixels = false;
            this.texture_disable = false;
            this.horizontalRes = new HorizontalRes(0, 0);
            this.verticalRes = VerticalRes.Y240Lines;
            this.vmode = VMode.NTSC;
            this.displayDepth = DisplayDepth.D15Bits;
            this.interlaced = false;
            this.display_disabled = true;
            this.interrupt = false;
            this.dmaDirection = DmaDirection.Off;
            this.window = rederingWindow;
            this.TIMER0 = timer0;
            this.TIMER1 = timer1;
        }

        public UInt32 read_GPUSTAT() { //TODO make this more accurate depending on GPU state
            UInt32 status = 0;

            status |= ((uint)page_base_x) << 0;
            status |= ((uint)page_base_y) << 4;
            status |= ((uint)semi_transparency) << 5;
            status |= ((uint)texture_depth) << 7;
            status |= ((uint)(dithering ? 1 : 0)) << 9;
            status |= ((uint)(draw_to_display ? 1 : 0)) << 10;
            status |= ((uint)(force_set_mask_bit ? 1 : 0)) << 11;
            status |= ((uint)(preserve_masked_pixels ? 1 : 0)) << 12;
            status |= ((uint)field) << 13;

            //Bit 14 "Reverseflag" is not supported, or is it? [Insert Vsauce music]

            status |= ((uint)(texture_disable ? 1 : 0)) << 15;
            status |= horizontalRes.intoStatus();

            //Bit 19 locks the bios if we don't emulate bit 31
            status |= ((uint)verticalRes) << 19; 

            status |= ((uint)vmode) << 20;
            status |= (((uint)displayDepth) << 21);
            status |= ((uint)(interlaced ? 1 : 0)) << 22;
            status |= ((uint)(display_disabled ? 1 : 0)) << 23;
            status |= ((uint)(interrupt ? 1 : 0)) << 24;

            //Ready to recive command
            status |= ((uint)(currentState == GPUState.Idle ? 1 : 0)) << 26;   

            //Ready to send VRam to CPU; via GPUREAD
            status |= 1 << 27;

            /*
             Bit28: Normally, this bit gets cleared when the command execution is busy (ie. once when the command and all of its parameters are received), however, for Polygon and Line Rendering commands, the bit gets cleared immediately after receiving the command word (ie. before receiving the vertex parameters).
             */

            //bool notReady = currentState == GPUState.LoadingPrimitive || (gpuTransfer.transferType != TransferType.Off && gpuTransfer.paramsReady);

            /*if (notReady) {
                status &= (~((uint)1 << 28));   //Ready to recive DMA block, this is wrong so I hardcoded 1 
            } else {
                status |= (1 << 28);
            }*/

            status |= (1 << 28);

            status |= ((uint)dmaDirection) << 29;

            status |= ((uint)currentLine) << 31;

            UInt32 dma_request;
            switch (dmaDirection) {
                case DmaDirection.Off: dma_request = 0;  break;
                case DmaDirection.Fifo: dma_request = 1;break;      //(if full it should be 0)
                case DmaDirection.CpuToGp0: dma_request = (status >> 28) & 1; break;
                case DmaDirection.VRamToCpu: dma_request = (status >> 27) & 1; break;
                default: throw new Exception("Invalid DMA direction: " + this.dmaDirection);
            }

            status |= ((uint)dma_request) << 25;

            //return 0b01011110100000000000000000000000;
            return status;
        }
        
        double scanlines = 0;
        double video_cycles = 0;
        double scanlines_per_frame = 263;           //NTSC
        double video_cycles_per_scanline = 3413;    //NTSC
        int cyclesPerPixel = 4;                     //x=640 (maybe wrong lol)
        double dotClock = 0;
        public void tick(double cycles) {
            video_cycles += cycles;
            dotClock += cycles;
            if (dotClock > cyclesPerPixel) {
                dotClock -= cyclesPerPixel;
                TIMER0.DotClock();
            }

            TIMER0.HblankOut();
            TIMER1.VblankOut();

            if (video_cycles >= video_cycles_per_scanline) {
                video_cycles -= video_cycles_per_scanline;
                scanlines++;

                if (scanlines >= scanlines_per_frame) {
                    scanlines -= scanlines_per_frame;

                    if (!display_disabled) {
                        window.Display();
                    }
                    if (verticalRes == VerticalRes.Y480Lines) {
                        currentLine = (currentLine + 1) & 1;
                    }

                    IRQ_CONTROL.IRQsignal(0);     //VBLANK
                    interrupt = true;
                    TIMER1.VblankTick();
                }
                
                TIMER0.HblankTick();
                TIMER1.HblankTick();
    
                if (verticalRes == VerticalRes.Y240Lines) {
                    currentLine = (currentLine + 1) & 1;
                }
            }
        }  
        
        public void write_GP0(UInt32 value) {

            switch (currentState) {
                case GPUState.Idle: gp0_decode(value); break;

                case GPUState.LoadingPrimitive:
                    primitive.add(value);
                    if (primitive.isReady()) {
                        primitive.draw(ref window);
                        currentState = GPUState.Idle;
                    }
                    break;

                case GPUState.Transferring:
                    CurrentTransfare.Add(value);
                    if (CurrentTransfare.ReadyToExecute) {
                        HandleTransfer();
                    }
                    break;
            }
        }

        private void gp0_decode(uint value) {
            //Depending on the value of these 3 bits, further decoding of the other bits can be done.
            UInt32 opcode = value >> 29;

            switch (opcode) {
                case 0x00: misc(value); break;

                case 0x01:      //Polygon Commands
                    primitive = new Polygon(value, semi_transparency, dithering);
                    currentState = GPUState.LoadingPrimitive;
                    break;

                case 0x02:      //Line Commands
                    primitive = new Line(value, semi_transparency, dithering);
                    currentState = GPUState.LoadingPrimitive;
                    break;

                case 0x03:      //Rectangle Commands
                    ushort page = (ushort)(page_base_x | (((uint)page_base_y) << 4) | (((uint)texture_depth) << 7));
                    primitive = new Rectangle(value, page, semi_transparency);
                    currentState = GPUState.LoadingPrimitive;
                    break;

                case 0x04:      //Transfer Commands
                case 0x05:
                case 0x06:
                    currentState = GPUState.Transferring;
                    CurrentTransfare = new GPU_MemoryTransfer(opcode == (uint)TransferType.VramToVram ? 4 : 3, opcode);
                    CurrentTransfare.Add(value);    
                    break;

                                //Environment commands
                case 0x07: environment(value); break;

                default: throw new Exception("GP0: " + opcode.ToString("x") + " - " + (value >> 29));
            }
        }

        private void HandleTransfer() {
            switch (CurrentTransfare.Type) {
                case (uint)TransferType.VramFill:
                    window.VramFillRectangle(ref CurrentTransfare);
                    CurrentTransfare = null;
                    currentState = GPUState.Idle;
                    break;

                case (uint)TransferType.VramToVram:
                    window.VramToVramCopy(ref CurrentTransfare);
                    CurrentTransfare = null;
                    currentState = GPUState.Idle;
                    break;

                case (uint)TransferType.CpuToVram:
                    window.CpuToVramCopy(ref CurrentTransfare);
                    CurrentTransfare = null;
                    currentState = GPUState.Idle;
                    break;

                case (uint)TransferType.VramToCpu:
                    window.VramToCpuCopy(ref CurrentTransfare);
                    break;

                default: throw new NotImplementedException();
            }
        }

        private void misc(uint command) {
            uint opcode = command >> 24;

            switch (opcode) {
                case 0x00: break;      //NOP
                case 0x01: break;      //Clear Cache
                case 0x03: break;      //Unknown?
                case 0xE0: break;      //NOP?
                case uint when opcode >= 0x04 && opcode <= 0x1E: break; //NOP?
                case uint when opcode >= 0xE7 && opcode <= 0xEF: break; //NOP?

                case 0x02:  //Vram Fill
                    currentState = GPUState.Transferring;
                    CurrentTransfare = new GPU_MemoryTransfer(3, 2);
                    CurrentTransfare.Add(command);
                    break;

                default: throw new Exception("Unknown GP0 misc command: " + opcode.ToString("x"));
            }
        }

        private void environment(uint command) {
            uint opcode = command >> 24 ;
            switch (opcode) {
                case 0xE0: break; //NOP
                case 0xE1: gp0_draw_mode(command); break;
                case 0xE2: gp0_texture_window(command); break;
                case 0xE3: gp0_drawing_area_TopLeft(command); break;
                case 0xE4: gp0_drawing_area_BottomRight(command); break;
                case 0xE5: gp0_drawing_offset(command); break;
                case 0xE6: gp0_mask_bit(command); break;
                case uint when opcode >= 0xE7 && opcode <= 0xEF: break; //NOP
                default: throw new Exception("Unknown GP0 Environment command: " + opcode.ToString("x"));
            }
        }

        public void ClearMemory<T>(ref List<T> list) {
            list.Clear();
            list.Capacity = 0;
            list.TrimExcess();
        }
       
        private void gp0_mask_bit(UInt32 value) {
            this.force_set_mask_bit = ((value & 1) != 0);
            this.preserve_masked_pixels = (((value >> 1) & 1) != 0);
            window.MaskBitSetting((int)value);
        }

        private void gp0_texture_window(UInt32 value) {
            value &= 0xfffff;   //20 bits

            //in 8 pixel steps, handled in GLSL
            this.texture_window_x_mask = (value & 0x1f);
            this.texture_window_y_mask = ((value >> 5) & 0x1f);

            this.texture_window_x_offset = ((value >> 10) & 0x1f);
            this.texture_window_y_offset = ((value >> 15) & 0x1f);

            window.SetTextureWindow((ushort)texture_window_x_mask, (ushort)texture_window_y_mask, 
                (ushort)texture_window_x_offset, (ushort)texture_window_y_offset);
        }
   
        private void gp0_drawing_offset(UInt32 value) {
            UInt16 x = (UInt16)(value & 0x7ff);
            UInt16 y = (UInt16)((value >> 11) & 0x7ff);

            //Signed 11 bits, need to extend to signed 16 then shift down to 11
            this.drawing_x_offset = (Int16)(((Int16)(x << 5)) >> 5);
            this.drawing_y_offset = (Int16)(((Int16)(y << 5)) >> 5);

            window.SetOffset(drawing_x_offset, drawing_y_offset);
        }

        private void gp0_drawing_area_BottomRight(UInt32 value) {
            this.drawing_area_bottom = (UInt16)((value >> 10) & 0x3ff);
            this.drawing_area_right = (UInt16)(value & 0x3ff);
            window.SetScissorBox(drawing_area_left, drawing_area_top,
                drawing_area_right - drawing_area_left, drawing_area_bottom - drawing_area_top);
        }

        private void gp0_drawing_area_TopLeft(UInt32 value) {
            this.drawing_area_top = (UInt16)((value >> 10) & 0x3ff);
            this.drawing_area_left = (UInt16)(value & 0x3ff);
            window.SetScissorBox(drawing_area_left, drawing_area_top,
                drawing_area_right - drawing_area_left, drawing_area_bottom - drawing_area_top);
        }

        private void write_GP1(UInt32 value) {
            UInt32 opcode = (value >> 24) & 0xff;

            switch (opcode) {
                case 0x00: gp1_reset(); break;
                case 0x01: gp1_reset_command_buffer(); break;
                case 0x02: gp1_acknowledge_irq();  break;
                case 0x03: gp1_display_enable(value); break;
                case 0x04: gp1_dma_direction(value); break;
                case 0x05: gp1_display_VRam_start(value); break;
                case 0x06: gp1_display_horizontal_range(value); break;
                case 0x07: gp1_display_vertical_range(value); break;
                case 0x08: gp1_display_mode(value); break;
                case uint when opcode >= 0x10 && opcode <= 0x1F: gpuInfoSelcted = value; break; //Get GPU info

                default: throw new Exception("Unhandled GP1 command :" + value.ToString("X") + " Opcode: " + opcode.ToString("x"));
            }
        }

        private void gp1_reset_command_buffer() {
            //Reset Fifo, which I don't emulate
            primitive = null;
            currentState = GPUState.Idle;
            CurrentTransfare = null;
        }

        private void gp1_acknowledge_irq() {
            this.interrupt = false;
        }

        private void gp1_display_enable(UInt32 value) {
            this.display_disabled = (value & 1) != 0;
        }

        private void gp1_display_vertical_range(UInt32 value) {
            this.display_line_start = (UInt16)(value & 0x3ff);
            this.display_line_end = (UInt16)((value>>10) & 0x3ff);
            UpdateVerticalRange();
        }

        private void gp1_display_horizontal_range(UInt32 value) {
            this.display_horiz_start = (UInt16)(value & 0xfff);
            this.display_horiz_end = (UInt16)((value>>12) & 0xfff);
            UpdateHorizontalRange();
        }

        private void gp1_display_VRam_start(UInt32 value) {
            this.display_vram_x_start = (UInt16)(value & 0x3fe);
            this.display_vram_y_start = (UInt16)((value>>10) & 0x1ff);
        }

        private void gp1_dma_direction(UInt32 value) {
            dmaDirection = (DmaDirection)(value & 3);
        }

        private void gp1_display_mode(UInt32 value) {
            byte hr1 = ((byte)(value & 3));
            byte hr2 = ((byte)((value >> 6) & 1));
            byte interlace = (byte)((value >> 5) & 1);

            horizontalRes = new HorizontalRes(hr1, hr2);
            verticalRes = (VerticalRes)(((value >> 2) & 1) & interlace);
            vmode = (VMode)((value >> 3) & 1);

            if (vmode == VMode.PAL) {
                scanlines_per_frame = 314;
                video_cycles_per_scanline = 3406.1;
            }
            else {
                scanlines_per_frame = 263;
                video_cycles_per_scanline = 3413.6;
            }
           
            this.interlaced = (value & 0x20) != 0;

            if ((value & 0x80) != 0) {
                throw new Exception("Unsupported display mode: " + value.ToString("X"));
            }

            switch (horizontalRes.getHR()) {
                case 320f: cyclesPerPixel = 8; break;
                case 640f: cyclesPerPixel = 4; break;
                case 256f: cyclesPerPixel = 10; break;
                case 512f: cyclesPerPixel = 5; break;
                case 368f: cyclesPerPixel = 7; break;
            }

            UpdateHorizontalRange();
            UpdateVerticalRange();

            uint depth = (uint)((value >> 4) & 1);
            displayDepth = (DisplayDepth)(depth);       //Not needed
            window.Is24bpp = depth == 1;
        }

        private void gp0_draw_mode(UInt32 value) {

            this.page_base_x = (byte)(value & 0xf);
            this.page_base_y = (byte)((value >> 4) & 1);
            this.semi_transparency = (byte)((value >> 5) & 3);
            this.texture_depth = (byte)((value >> 7) & 3);
            this.dithering = ((value >> 9) & 1) != 0;
            this.draw_to_display = ((value >> 10) & 1) != 0;
            this.texture_disable = ((value >> 11) & 1) != 0;                    //Texture page Y Base 2 (N*512) (only for 2MB VRAM)
            this.rectangle_texture_x_flip = ((value >> 12) & 1) != 0;
            this.rectangle_texture_y_flip = ((value >> 13) & 1) != 0;
            
        }
        private void gp1_reset() {
            this.interrupt = false;
            this.page_base_x = 0;
            this.page_base_y = 0;
            this.semi_transparency = 0;
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
            this.dmaDirection = DmaDirection.Off;
            this.display_disabled = true;
            this.display_vram_x_start = 0;
            this.display_vram_y_start = 0;
            this.horizontalRes = new HorizontalRes(0, 0);
            this.verticalRes = VerticalRes.Y240Lines;
            this.vmode = VMode.NTSC;
            this.interlaced = true;

            this.display_horiz_start = 0x200;
            this.display_horiz_end = 0xc00;

            this.display_line_start = 0x10;
            this.display_line_end = 0x100;

            this.displayDepth = DisplayDepth.D15Bits;

            this.currentState = GPUState.Idle;
            this.currentLine = 0;

            //...Clear Fifo

            window.DisableBlending();
            //Probably more window reset stuff

        }

        uint lastGPURead = 0x0;
        internal uint GPU_ReadReg() {
            //Handle responding to GP0(C0h) (Vram to CPU), if it exists 
            if (CurrentTransfare != null && CurrentTransfare.Type == (uint)TransferType.VramToCpu) {
                uint data = CurrentTransfare.ReadWord();
                if (CurrentTransfare.DataEnd) {
                    CurrentTransfare = null;
                    currentState = GPUState.Idle;
                }
                lastGPURead = data;
                return data;
            }

            int start = 0;
            int end = 0x8;  //Excluded
            uint final = (uint)(start + ((gpuInfoSelcted - start) % (end - start)));

            switch (final) {
                case 0x0:
                case 0x1:
                case 0x6:
                case 0x7:
                    return lastGPURead;

                case 0x2: 
                    lastGPURead = texture_window_x_mask | (texture_window_y_mask << 5) | 
                        (texture_window_x_offset << 10) | (texture_window_y_offset << 15);
                    break;
                
                case 0x3: lastGPURead = (uint)(drawing_area_left | drawing_area_top << 10); break;
                case 0x4: lastGPURead = (uint)(drawing_area_right | drawing_area_bottom << 10); break;
                case 0x5:
                    uint y = (uint)drawing_y_offset;
                    uint x = (uint)drawing_x_offset;
                    lastGPURead = (uint)(x | (y << 11));
                    break;

                default:
                    throw new Exception("Unknown GPU read info selected: ");

            }
            return lastGPURead;

        }

        public float HorizontalRange = 640f;
        public float VerticalRange = 240f;

        public void UpdateHorizontalRange() {
            HorizontalRange = (((display_horiz_end - display_horiz_start) / cyclesPerPixel) + 2) & (~3);
        }

        public void UpdateVerticalRange() {
            float verticalRange = display_line_end - display_line_start;
            VerticalRange = interlaced? verticalRange * 2 : verticalRange;
        }

        public uint LoadWord(uint address) {
            uint offset = address - range.start;
            switch (offset) {
                case 0: return GPU_ReadReg();
                case 4: return read_GPUSTAT();
                default: throw new Exception("Unhandled read to offset " + offset);
            }
        }

        public void StoreWord(uint address, uint value) {
            uint offset = address - range.start;
            switch (offset) {
                case 0: write_GP0(value); break;
                case 4: write_GP1(value); break;
                default:
                    throw new Exception("Unhandled write to offset: " + offset + " val: " + Convert.ToUInt32(value).ToString("x")
                                        + "Physical address: " + offset.ToString("x"));
            }
        }
    }

    //Not needed, should be handeled in the GPU
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

        public uint intoStatus() {      //Bits [18:16] of GPUSTAT
            return ((uint)this.HR) << 16;
        }
    }
}
