using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Common.Input;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp;
using System;
using System.Diagnostics;
using System.Threading;
using System.Collections.Generic;
using System.Linq.Expressions;
using static System.Net.Mime.MediaTypeNames;
using System.IO;
using NAudio.Wave.SampleProviders;

namespace PS1_Emulator {
    public class Renderer {

        public Window window;
        
        public Renderer() {

            var nativeWindowSettings = new NativeWindowSettings() {
                Size = new Vector2i(1280, 720),
                Title = "PS1 Emulator",
                // This is needed to run on macos
                Flags = ContextFlags.ForwardCompatible,
                APIVersion = Version.Parse("4.6.0"),
                WindowBorder = WindowBorder.Resizable,
               
            };

            var Gws = GameWindowSettings.Default;
            Gws.RenderFrequency = 60;
            Gws.UpdateFrequency = 60;
            nativeWindowSettings.Location = new Vector2i((1980 - nativeWindowSettings.Size.X) / 2, (1080 - nativeWindowSettings.Size.Y) / 2);

            var windowIcon = new WindowIcon(new OpenTK.Windowing.Common.Input.Image(300, 300, ImageToByteArray(@"C:\Users\Old Snake\Desktop\PS1\PSX logo.jpg")));
            nativeWindowSettings.Icon = windowIcon;
            window = new Window(Gws, nativeWindowSettings);
            window.Run();

        }

        public byte[] ImageToByteArray(string Icon) {
            var image = (Image<Rgba32>)SixLabors.ImageSharp.Image.Load(Configuration.Default, Icon);
            var pixels = new byte[4 * image.Width * image.Height];
            image.CopyPixelDataTo(pixels);

            return pixels;
        }



    }

    public class Window : GameWindow {
        CPU cpu;
        bool cpuPaused = false;
        const uint CYCLES_PER_FRAME = 33868800 / 60;
        const int VRAM_WIDTH = 1024;
        const int VRAM_HEIGHT = 512;

        private int vertexArrayObject;
        private int vertexBufferObject;
        private int colorsBuffer;
        private int uniform_offset;
        private int fullVram;
        private int vram_texture;
        private int sample_texture;
        private int texCoords;
        private int texWindow;
        private int texModeLoc;
        private int clutLoc;
        private int texPageLoc;
        private int display_areay_X_Loc;
        private int display_areay_Y_Loc;
        private int display_area_X_Offset_Loc;
        private int display_area_Y_Offset_Loc;
        private int vramFrameBuffer;

        public bool isUsingMouse = false;
        public bool showTextures = false;
        public bool isFullScreen = false;

        Shader shader;

        //Map my button indexes to the corrosponding bits in the PS1 controller
        public static Dictionary<int, int> buttons_Dictionary = new Dictionary<int, int>()
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

        public Window(GameWindowSettings gameWindowSettings, NativeWindowSettings nativeWindowSettings)
             : base(gameWindowSettings, nativeWindowSettings) {

            GL.ClearColor(0.0f, 0.0f, 0.0f, 1.0f);
            GL.Clear(ClearBufferMask.ColorBufferBit);
            SwapBuffers();

            //A shitty way, and a hardcoded path 
            BIOS bios = new BIOS(@"C:\Users\Old Snake\Desktop\PS1\BIOS\PSX - SCPH1001.BIN");
            Interconnect i = new Interconnect(bios, this);
            cpu = new CPU(i);
            this.Title += bios.ID.Contains("1001") ? (" - BIOS: " + bios.ID) : "";

        }
        protected override void OnLoad() {
            
            //Load shaders 
            shader = new Shader("C:\\Users\\Old Snake\\Desktop\\PS1\\shader.vert", "C:\\Users\\Old Snake\\Desktop\\PS1\\shader.frag");
            shader.Use();

            GL.Viewport(0, 0, this.Size.X, this.Size.Y);
            GL.ClearColor(0.0f, 0.0f, 0.0f, 1.0f);      //This can be ignored as the PS1 BIOS will initially draw a black quad clearing the buffer anyway
            GL.Clear(ClearBufferMask.ColorBufferBit);  
            SwapBuffers();

            uniform_offset = GL.GetUniformLocation(shader.Handle, "offset");
            fullVram = GL.GetUniformLocation(shader.Handle, "fullVram");
            texWindow = GL.GetUniformLocation(shader.Handle, "u_texWindow");
            texModeLoc = GL.GetUniformLocation(shader.Handle, "TextureMode");
            clutLoc = GL.GetUniformLocation(shader.Handle, "inClut");
            texPageLoc = GL.GetUniformLocation(shader.Handle, "inTexpage");

            display_areay_X_Loc = GL.GetUniformLocation(shader.Handle, "display_area_x");
            display_areay_Y_Loc = GL.GetUniformLocation(shader.Handle, "display_area_y");
            display_area_X_Offset_Loc = GL.GetUniformLocation(shader.Handle, "display_area_x_offset");
            display_area_Y_Offset_Loc = GL.GetUniformLocation(shader.Handle, "display_area_y_offset");

            vertexArrayObject = GL.GenVertexArray();
            vertexBufferObject = GL.GenBuffer();                 
            colorsBuffer = GL.GenBuffer();
            texCoords = GL.GenBuffer();
            vram_texture = GL.GenTexture();
            sample_texture = GL.GenTexture();
            vramFrameBuffer = GL.GenFramebuffer();

            GL.BindVertexArray(vertexArrayObject);

            GL.Enable(EnableCap.Texture2D);

            GL.BindTexture(TextureTarget.Texture2D, vram_texture);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, VRAM_WIDTH, VRAM_HEIGHT, 0, PixelFormat.Bgra, PixelType.UnsignedShort1555Reversed, (IntPtr)null);

            GL.BindTexture(TextureTarget.Texture2D, sample_texture);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, VRAM_WIDTH, VRAM_HEIGHT, 0, PixelFormat.Bgra, PixelType.UnsignedShort1555Reversed, (IntPtr)null);

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, vramFrameBuffer);
            GL.FramebufferTexture2D(FramebufferTarget.DrawFramebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, vram_texture, 0);
            if (GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != FramebufferErrorCode.FramebufferComplete) {
                Console.WriteLine("[OpenGL] Uncompleted Frame Buffer !");
            }

            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 2);
            GL.PixelStore(PixelStoreParameter.PackAlignment, 2);
            GL.Uniform1(GL.GetUniformLocation(shader.Handle, "u_vramTex"), 0);

        }

        Int16 offset_x;
        Int16 offset_y;
        public void setOffset(Int16 x, Int16 y, Int16 z) {
            offset_x = x;
            offset_y = y;
            GL.Uniform3(uniform_offset, x, y, z);

        }
        public void setTextureWindow(ushort x, ushort y, ushort z, ushort w) {

            GL.Uniform4(texWindow, x, y, z, w);
        }

        int scissorBox_x;
        int scissorBox_y;
        int scissorBox_w;
        int scissorBox_h;

        public void setScissorBox(int x,int y,int width,int height) {

            GL.Viewport(0, 0, VRAM_WIDTH, VRAM_HEIGHT);

            scissorBox_x = x;
            scissorBox_y = y;
            scissorBox_w = Math.Max(width + 1, 0);
            scissorBox_h = Math.Max(height + 1, 0);

            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, vramFrameBuffer);

            // GL.Enable(EnableCap.ScissorTest);
            GL.Scissor(scissorBox_x, scissorBox_y, scissorBox_w, scissorBox_h);

        }

        public void draw(ref short[] vertices, ref byte[] colors, ref ushort[] uv, ushort clut, ushort page, int texMode) {
            GL.Viewport(0, 0, VRAM_WIDTH, VRAM_HEIGHT);
            GL.Uniform1(texModeLoc, texMode);

            // GL.Enable(EnableCap.ScissorTest);
            //GL.Enable(EnableCap.Blend);

            GL.BindBuffer(BufferTarget.ArrayBuffer, vertexBufferObject);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(short), vertices, BufferUsageHint.StreamDraw);
            GL.VertexAttribIPointer(0, 3, VertexAttribIntegerType.Short, 0, (IntPtr)null);
            GL.EnableVertexAttribArray(0);

            GL.BindBuffer(BufferTarget.ArrayBuffer, colorsBuffer);
            GL.BufferData(BufferTarget.ArrayBuffer, colors.Length * sizeof(byte), colors, BufferUsageHint.StreamDraw);
            GL.VertexAttribIPointer(1, 3, VertexAttribIntegerType.UnsignedByte, 0, (IntPtr)null);
            GL.EnableVertexAttribArray(1);


            if (uv != null) {
                GL.Uniform1(clutLoc, clut);
                GL.Uniform1(texPageLoc, page);

                GL.BindBuffer(BufferTarget.ArrayBuffer, texCoords);
                GL.BufferData(BufferTarget.ArrayBuffer, uv.Length * sizeof(ushort), uv, BufferUsageHint.StreamDraw);
                GL.VertexAttribPointer(2, 2, VertexAttribPointerType.UnsignedShort, false, 2 * sizeof(ushort), (IntPtr)null);
                GL.EnableVertexAttribArray(2);
            }

            GL.DrawArrays(PrimitiveType.Triangles, 0, vertices.Length / 3);

            //Lets hope there is no need to sync and wait for the GPU 

        }


        internal void drawLines(ref short[] vertices, ref byte[] colors) {

            GL.Viewport(0, 0, VRAM_WIDTH, VRAM_HEIGHT);

            GL.Uniform1(texModeLoc, -1);

            GL.BindBuffer(BufferTarget.ArrayBuffer, vertexBufferObject);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(short), vertices, BufferUsageHint.StreamDraw);
            GL.VertexAttribIPointer(0, 3, VertexAttribIntegerType.Short, 0, (IntPtr)null);
            GL.EnableVertexAttribArray(0);

            GL.BindBuffer(BufferTarget.ArrayBuffer, colorsBuffer);
            GL.BufferData(BufferTarget.ArrayBuffer, colors.Length * sizeof(byte), colors, BufferUsageHint.StreamDraw);
            GL.VertexAttribIPointer(1, 3, VertexAttribIntegerType.UnsignedByte, 0, (IntPtr)null);
            GL.EnableVertexAttribArray(1);


            GL.DrawArrays(PrimitiveType.Lines, 0, vertices.Length / 3);

        }



        public void readBackTexture(UInt16 x, UInt16 y, UInt16 width, UInt16 height, ref UInt16[] texData) {

            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, vramFrameBuffer);
            GL.ReadPixels(x, y, width, height, PixelFormat.Rgba, PixelType.UnsignedShort1555Reversed, texData);

        }

        void displayFrame() {
            //Disable the ScissorTest and unbind the FBO to draw the entire vram texture to the screen
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, 0);

            //GL.Scissor(0,0,this.Size.X,this.Size.Y);
            GL.Disable(EnableCap.ScissorTest);

            //GL.Disable(EnableCap.Blend);
            disableBlending();

            GL.Viewport(0, 0, this.Size.X, this.Size.Y);
            GL.Enable(EnableCap.Texture2D);
            GL.BindTexture(TextureTarget.Texture2D, vram_texture);
            
            GL.Uniform1(fullVram, 1);

            GL.DisableVertexAttribArray(1);
            GL.DisableVertexAttribArray(2);

            modifyAspectRatio();

            GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);

            //Enable ScissorTest and bind FBO for next draws 
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, vramFrameBuffer);
            GL.Enable(EnableCap.ScissorTest);

            GL.BindTexture(TextureTarget.Texture2D, sample_texture);
            GL.Scissor(scissorBox_x, scissorBox_y, scissorBox_w, scissorBox_h);
            GL.Uniform1(fullVram, 0);

        }
        public void vramFill(float r, float g, float b, int x, int y, int width, int height) {
            GL.Viewport(0, 0, VRAM_WIDTH, VRAM_HEIGHT);
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, vramFrameBuffer);
            GL.ClearColor(r,g,b,1.0f);


            GL.Scissor(x,y, width, height);
            GL.Clear(ClearBufferMask.ColorBufferBit);
           
            //After that reset the Scissor box to the drawing area
            GL.Scissor(scissorBox_x, scissorBox_y, scissorBox_w, scissorBox_h);

        }
        public void rectFill(byte r, byte g, byte b, int x, int y, int width, int height) {
            GL.Viewport(0, 0, VRAM_WIDTH, VRAM_HEIGHT);
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, vramFrameBuffer);

            //Define the quad 
            Int16[] vertices = {
                (short)(x),(short)y,0,
                (short)(x+width),(short)y,0,
                (short)x,(short)(y+height),0,

                 (short)(x+width),(short)y,0,
                 (short)x,(short)(y+height),0,
                 (short)(x+width),(short)(y+height),0,

            };
            byte[] colors = {
                r,g,b,
                r,g,b,
                r,g,b,
                r,g,b,
                r,g,b,
                r,g,b
            };
            ushort[] uv = null; //Dummy
            draw(ref vertices,ref colors,ref uv,0,0,-1);

            /*GL.ClearColor(r/255.0f, g/255.0f, b/255.0f, 1.0f);

            x += offset_x;
            y += offset_y;

            //Handle clipping manually (because GL.Scissor will be changed)

            //Completely outside
            if (x > scissorBox_x + scissorBox_w || y > scissorBox_y + scissorBox_h || x + width < scissorBox_x || y + height < scissorBox_y) {
                return;
            }

            //Too wide or too tall
            if (width > scissorBox_w) {
                width = scissorBox_w;

            }
            if (height > scissorBox_h) {
                height = scissorBox_h;
            }

            //Partially outside
            if (x < scissorBox_x && x + width < scissorBox_x + scissorBox_w) {
                x = scissorBox_x;

            }

            if (y < scissorBox_y && y + height < scissorBox_y + scissorBox_h) {
                y = scissorBox_y;

            }


            GL.Scissor(x, y, width, height);
            GL.Clear(ClearBufferMask.ColorBufferBit);

            //After that reset the Scissor box to the drawing area
            GL.Scissor(scissorBox_x, scissorBox_y, scissorBox_w, scissorBox_h);*/


        }




        public void update_vram(int x, int y , int width, int height, ushort[] textureData) {
            if (width == 0) { width = VRAM_WIDTH; }
            if (height == 0) { height = VRAM_HEIGHT; }
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, 0);
            GL.BindTexture(TextureTarget.Texture2D, vram_texture);
            GL.TexSubImage2D(TextureTarget.Texture2D,0,x,y,width,height, PixelFormat.Rgba, PixelType.UnsignedShort1555Reversed, textureData);
            
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, vramFrameBuffer);
            GL.BindTexture(TextureTarget.Texture2D, sample_texture);
            GL.CopyTexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, 0, 0, VRAM_WIDTH, VRAM_HEIGHT);

         
        }
        internal void VramToVramCopy(int x0_src, int y0_src, int x0_dest, int y0_dest, int width, int height) {
            //WIP

            /*GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
            //GL.BindTexture(TextureTarget.Texture2D, sample_texture);
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, fbo);

            GL.BlitFramebuffer(x0_src,y0_src,x1_src,y1_src,x0_dest,y0_dest,x1_dest,y1_dest, (ClearBufferMask)ClearBuffer.Color,BlitFramebufferFilter.Nearest);*/
            if(width == 0) { width = VRAM_WIDTH; }
            if(height == 0) { height = VRAM_HEIGHT; }
            GL.BindTexture(TextureTarget.Texture2D, sample_texture);
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, 0); 
            GL.CopyImageSubData(sample_texture,ImageTarget.Texture2D,0,x0_src,y0_src,0,vram_texture,ImageTarget.Texture2D,0,x0_dest,y0_dest,0,width,height,0);
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, vramFrameBuffer);
            GL.CopyTexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, 0, 0, VRAM_WIDTH, VRAM_HEIGHT);

            //throw new Exception("VramToVramCopy");
        }

        public void display() {
            
            displayFrame();
            SwapBuffers();

        }

        public void modifyAspectRatio() {
            float disp_x = cpu.bus.GPU.hrez.getHR();
            float disp_y = cpu.bus.GPU.vrez == 0 ? 240f : 480f;


            if (!showTextures) {

                GL.Uniform1(display_areay_X_Loc, disp_x);
                GL.Uniform1(display_areay_Y_Loc, disp_y);

                if (disp_x / disp_y < (float)this.Size.X / this.Size.Y) {

                    float offset = (disp_x / disp_y) * (float)this.Size.Y;  //Random formula by JyAli
                    offset = ((float)this.Size.X - offset) / this.Size.X;
                    GL.Uniform1(display_area_Y_Offset_Loc, 0.0f);
                    GL.Uniform1(display_area_X_Offset_Loc, offset);


                    GL.ClearColor(0.0f, 0.0f, 0.0f, 1.0f);
                    GL.Scissor(0, 0, (int)(offset * this.Size.X), this.Size.Y);
                    GL.Clear(ClearBufferMask.ColorBufferBit);
                    GL.Scissor(scissorBox_x, scissorBox_y, scissorBox_w, scissorBox_h);

                }
                else if (disp_x / disp_y > (float)this.Size.X / this.Size.Y) {

                    float offset = (disp_y / disp_x) * (float)this.Size.X;  //Random formula by JyAli

                    GL.Uniform1(display_area_Y_Offset_Loc, ((float)this.Size.Y - offset) / this.Size.Y);
                    GL.Uniform1(display_area_X_Offset_Loc, 0.0f);

                    GL.ClearColor(0.0f, 0.0f, 0.0f, 1.0f);
                    GL.Scissor(0, 0, this.Size.X, (int)(offset * this.Size.Y));
                    GL.Clear(ClearBufferMask.ColorBufferBit);
                    GL.Scissor(scissorBox_x, scissorBox_y, scissorBox_w, scissorBox_h);

                }
                else {
                    GL.Uniform1(display_area_X_Offset_Loc, 0.0f);
                    GL.Uniform1(display_area_Y_Offset_Loc, 0.0f);

                }
            }
            else {
                GL.Uniform1(display_area_X_Offset_Loc, 0.0f);
                GL.Uniform1(display_area_Y_Offset_Loc, 0.0f);
                GL.Uniform1(display_areay_X_Loc, (float)VRAM_WIDTH);
                GL.Uniform1(display_areay_Y_Loc, (float)VRAM_HEIGHT);
            }



        }
        protected override void OnResize(ResizeEventArgs e) {
            base.OnResize(e);
            GL.Viewport(0,0,this.Size.X,this.Size.Y);
        }

        protected override void OnKeyDown(KeyboardKeyEventArgs e) {
            base.OnKeyDown(e);


            if (e.Key.Equals(Keys.Escape)) {
                Close();
            }
            else if (e.Key.Equals(Keys.D)) {
                cpu.bus.print = true;
                Thread.Sleep(100);

            }
            else if (e.Key.Equals(Keys.P)) {
                cpuPaused = !cpuPaused;
                Thread.Sleep(100);

            }
            else if (e.Key.Equals(Keys.Tab)) {
                showTextures = !showTextures;
                Thread.Sleep(100);

            }
            else if (e.Key.Equals(Keys.F)) {
                isFullScreen = !isFullScreen;
                this.WindowState = isFullScreen ? WindowState.Fullscreen : WindowState.Normal;
                this.CursorState = isFullScreen ? CursorState.Hidden : CursorState.Normal;
                Thread.Sleep(100);

            }

        }
        protected override void OnUpdateFrame(FrameEventArgs args) {
            base.OnUpdateFrame(args);

            if (cpuPaused) { return; }

            for (int i = 0; i < CYCLES_PER_FRAME;) {        //Timings are nowhere near accurate 

                /*try {
                    cpu.emu_cycle();

                }
                catch (Exception ex) {
                    File.WriteAllTextAsync("Crash.log", ex.ToString());
                    Close();
                }*/

                cpu.emu_cycle();
                CPU.cycles += 2;

                //
                if (!cpu.bus.TIMER1.isUsingHblank()) { cpu.bus.TIMER1.tick(); }
                cpu.bus.TIMER2.tick(CPU.cycles);
                // 

                cpu.bus.spu.SPU_Tick(CPU.cycles);
                cpu.bus.GPU.tick(CPU.cycles * (double)11 / 7);
                cpu.bus.IO_PORTS.tick(CPU.cycles);
               // cpu.bus.IO_PORTS.tick(CPU.cycles);
                cpu.bus.CDROM_tick(CPU.cycles);
                i += CPU.cycles;
                CPU.cycles = 0;

            }

            //Read controller input 
            cpu.bus.IO_PORTS.controller1.readInput(JoystickStates[0]);
            //cpu.bus.IO_PORTS.controller2.readInput(JoystickStates[1]);

        }


        protected override void OnUnload() {

            // Unbind all the resources by binding the targets to 0/null.
            // Unbind all the resources by binding the targets to 0/null.
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            GL.BindVertexArray(0);
            GL.UseProgram(0);

            // Delete all the resources.
            GL.DeleteBuffer(vertexBufferObject);
            GL.DeleteBuffer(colorsBuffer);
            GL.DeleteBuffer(texCoords);
            GL.DeleteVertexArray(vertexArrayObject);
            GL.DeleteFramebuffer(vramFrameBuffer);
            GL.DeleteTexture(vram_texture);
            GL.DeleteTexture(sample_texture);
            GL.DeleteProgram(shader.Handle);

            
            base.OnUnload();
        }

        internal void disableBlending() {
            ///GL.Disable(EnableCap.Blend);

            GL.BlendFunc(BlendingFactor.One, BlendingFactor.Zero);
            GL.BlendEquation(BlendEquationMode.FuncAdd);
        }
        internal void setBlendingFunction(uint function) {

            GL.Enable(EnableCap.Blend);
            
            switch (function) {
                case 0:
                    GL.BlendColor(1f, 1f, 1f,0.5f);
                    GL.BlendFunc(BlendingFactor.ConstantAlpha , BlendingFactor.ConstantAlpha);
                    GL.BlendEquation(BlendEquationMode.FuncAdd);
                    break;

                case 1:
                    GL.BlendFunc(BlendingFactor.One, BlendingFactor.One);
                    GL.BlendEquation(BlendEquationMode.FuncAdd);
                    break;

                case 2:
                    GL.BlendFunc(BlendingFactor.One, BlendingFactor.One);
                    GL.BlendEquation(BlendEquationMode.FuncReverseSubtract);
                    break;

                case 3:
                    GL.BlendColor(1f, 1f, 1f, 0.25f);
                    GL.BlendFunc(BlendingFactor.ConstantAlpha, BlendingFactor.One);
                    GL.BlendEquation(BlendEquationMode.FuncAdd);
                    break;

                default:
                    throw new Exception("Unknown blend function: " + function);
            }
        }
    }

}
