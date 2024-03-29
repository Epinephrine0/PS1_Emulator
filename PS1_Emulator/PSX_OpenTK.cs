﻿using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Common.Input;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp;
using System;
using System.Threading;
using System.IO;
using PSXEmulator.Peripherals;
using PSXEmulator.PS1_Emulator;
using System.Text;

namespace PSXEmulator {
    public class PSX_OpenTK {
        public Renderer mainWindow;
        public PSX_OpenTK(string biosPath, string bootPath, bool isBootingEXE) { 
            var nativeWindowSettings = new NativeWindowSettings() {
                Size = new Vector2i(1024, 512),
                Title = "OpenGL",
                Flags = ContextFlags.ForwardCompatible,
                APIVersion = Version.Parse("4.6.0"),
                WindowBorder = WindowBorder.Resizable,               
            };

            var Gws = GameWindowSettings.Default;
            Gws.RenderFrequency = 60;   
            Gws.UpdateFrequency = 60;
            nativeWindowSettings.Location = new Vector2i((1980 - nativeWindowSettings.Size.X) / 2, (1080 - nativeWindowSettings.Size.Y) / 2);

            try {
                var windowIcon = new WindowIcon(new OpenTK.Windowing.Common.Input.Image(300, 300, ImageToByteArray(@"PSX logo.jpg")));
                nativeWindowSettings.Icon = windowIcon;
            }
            catch (FileNotFoundException ex) { 
                Console.WriteLine("Warning: PSX logo not found!");
            }


            mainWindow = new Renderer(Gws, nativeWindowSettings);

            Console.OutputEncoding = Encoding.UTF8;

            //Create everything here, pass relevant user settings
            RAM Ram = new RAM();
            BIOS Bios = new BIOS(biosPath);
            Scratchpad Scratchpad = new Scratchpad();
            CD_ROM cdrom = isBootingEXE? new CD_ROM() : new CD_ROM(bootPath, false);
            SPU Spu = new SPU(ref cdrom.DataController);         //Needs to read CD-Audio
            DMA Dma = new DMA();
            IO_PORTS IO = new IO_PORTS();
            MemoryControl MemoryControl = new MemoryControl();   //useless ?
            RAM_SIZE RamSize = new RAM_SIZE();                   //useless ?
            CACHECONTROL CacheControl = new CACHECONTROL();      //useless ?
            Expansion1 Ex1 = new Expansion1();
            Expansion2 Ex2 = new Expansion2();
            TIMER1 Timer1 = new TIMER1();
            TIMER2 Timer2 = new TIMER2();
            MDEC Mdec = new MDEC();
            GPU Gpu = new GPU(mainWindow, ref Timer1);

            BUS Bus = new BUS(          
                Bios,Ram,Scratchpad,cdrom,Spu,Dma,
                IO,MemoryControl,RamSize,CacheControl,
                Ex1,Ex2,Timer1,Timer2,Mdec,Gpu
                );
            CPU CPU = new CPU(isBootingEXE, bootPath, Bus);

            mainWindow.CPU = CPU;
            mainWindow.Title += " | ";
            if (bootPath != null) {
                mainWindow.Title += Path.GetFileName(bootPath);
            } else {
                mainWindow.Title += "PSX-BIOS";
            }
            mainWindow.Title += " | ";

            mainWindow.Run();

            mainWindow.Dispose();   //Will reach this if the render window closes   
            
        }
 
        public byte[] ImageToByteArray(string Icon) {
            var image = (Image<Rgba32>)SixLabors.ImageSharp.Image.Load(Configuration.Default, Icon);
            var pixels = new byte[4 * image.Width * image.Height];
            image.CopyPixelDataTo(pixels);

            return pixels;
        }
    }

    public class Renderer : GameWindow {    //Now it gets really messy 
        public CPU CPU;
        const int VRAM_WIDTH = 1024;
        const int VRAM_HEIGHT = 512;

        private int vertexArrayObject;
        private int vertexBufferObject;
        private int colorsBuffer;
        private int fullVram;
        private int vram_texture;
        private int sample_texture;
        private int texCoords;
        private int texWindow;
        private int texModeLoc;
        private int maskBitSettingLoc;
        private int clutLoc;
        private int texPageLoc;
        private int display_areay_X_Loc;
        private int display_areay_Y_Loc;
        private int display_area_X_Offset_Loc;
        private int display_area_Y_Offset_Loc;
        private int vramFrameBuffer;
        private int transparencyModeLoc;
        private int isDitheredLoc;

        //Signed 11 bits
        private short drawOffsetX = 0;
        private short drawOffsetY = 0;
       
        public bool isUsingMouse = false;
        public bool showTextures = true;
        public bool isFullScreen = false;

        //This is going to contain blocks that are either clean (0) or dirty (1) for texture invalidation 
        const int IntersectionBlockLength = 64;
        private int[,] IntersectionTable = new int[VRAM_HEIGHT / IntersectionBlockLength, VRAM_WIDTH / IntersectionBlockLength];

        Shader shader;
        string vertixShader = @"
            #version 330 

            layout(location = 0) in ivec2 vertixInput;
            layout(location = 1) in uvec3 vColors;
            layout(location = 2) in vec2 inUV;


            out vec3 color_in;
            out vec2 texCoords;
            flat out ivec2 clutBase;
            flat out ivec2 texpageBase;

            flat out int isScreenQuad;

            uniform int fullVram;

            uniform int inClut;
            uniform int inTexpage;

            uniform float display_area_x = 1024.0;
            uniform float display_area_y = 512.0;

            uniform float display_area_x_offset = 0.0;
            uniform float display_area_y_offset = 0.0;

            void main()
            {
    
            //Convert x from [0,1023] and y from [0,511] coords to [-1,1]

            float xpos = ((float(vertixInput.x) + 0.5) / 512.0) - 1.0;
            float ypos = ((float(vertixInput.y) - 0.5) / 256.0) - 1.0;
	

            if(fullVram == 1){		//This is for displaying a full screen quad with the entire vram texture 

                vec4 positions[4] = vec4[](
                vec4(-1.0 + display_area_x_offset, 1.0 - display_area_y_offset, 1.0, 1.0),    // Top-left
                vec4(1.0 - display_area_x_offset, 1.0 - display_area_y_offset, 1.0, 1.0),     // Top-right
                vec4(-1.0 + display_area_x_offset, -1.0 + display_area_y_offset, 1.0, 1.0),   // Bottom-left
                vec4(1.0 - display_area_x_offset, -1.0 + display_area_y_offset, 1.0, 1.0)     // Bottom-right
            );
 
                vec2 texcoords[4] = vec2[](		//Inverted in Y because PS1 Y coords are inverted
                vec2(0.0, 0.0),   			// Top-left

                vec2(display_area_x/1024.0, 0.0),   // Top-right

                vec2(0.0, display_area_y/512.0),   // Bottom-left

                vec2(display_area_x/1024.0, display_area_y/512.0)    // Bottom-right
            );
 
            gl_Position = positions[gl_VertexID];
            texCoords = texcoords[gl_VertexID];
            isScreenQuad = 1;

            return;

            }else{

            gl_Position.xyzw = vec4(xpos,ypos,0.0, 1.0);
            isScreenQuad = 0;
            }

            texpageBase = ivec2((inTexpage & 0xf) * 64, ((inTexpage >> 4) & 0x1) * 256);
            clutBase = ivec2((inClut & 0x3f) * 16, inClut >> 6);
            texCoords = inUV;

            color_in = vec3(
            float(vColors.r)/255.0,
            float(vColors.g)/255.0,
            float(vColors.b)/255.0);

            }";

        string fragmentShader = @"
            #version 330 

            in vec3 color_in;
            in vec2 texCoords;
            flat in ivec2 clutBase;
            flat in ivec2 texpageBase;
            uniform int TextureMode;

            uniform int isDithered;
            uniform int transparencyMode;       //4 = disabled
            uniform int maskBitSetting;

            flat in int isScreenQuad;

            uniform ivec4 u_texWindow;

            uniform sampler2D u_vramTex;

            //out vec4 outputColor;
            layout(location = 0, index = 0) out vec4 outputColor;
            layout(location = 0, index = 1) out vec4 outputBlendColor;

         
           mat4 ditheringTable = mat4(
               -4,  0, -3,  1,
                2, -2,  3, -1,
               -3,  1, -4,  0,
                3, -1,  2, -2
            );

            vec3 dither(vec3 colors, vec2 poistion) {

               int x = int((poistion.x * 1023.0) - (4 * (floor((poistion.x * 1023.0)/4))));
               int y = int((poistion.y * 511.0) - (4 * (floor((poistion.y * 511.0)/4))));
               int ditherOffset = int(ditheringTable[y][x]);

               colors = colors * vec3(255.0, 255.0, 255.0);
               colors = colors + vec3(ditherOffset, ditherOffset ,ditherOffset);

               //Clamp to [0,255] (could have probably used a built in funcion)
               if(colors.r < 0.0){ colors.r = 0.0; }
               if(colors.g < 0.0){ colors.g = 0.0; }
               if(colors.b < 0.0){ colors.b = 0.0; }
               if(colors.r > 255.0){ colors.r = 255.0; }
               if(colors.g > 255.0){ colors.g = 255.0; }
               if(colors.b > 255.0){ colors.b = 255.0; }

               return colors / vec3(255.0, 255.0, 255.0);
              }

            vec4 grayScale(vec4 color) {
                   float x = 0.299*(color.r) + 0.587*(color.g) + 0.114*(color.b);
                   return vec4(x,x,x,1);
              }

               int floatToU5(float f) {				
                        return int(floor(f * 31.0 + 0.5));
                    }

            vec4 sampleVRAM(ivec2 coords) {
                   coords &= ivec2(1023, 511); // Out-of-bounds VRAM accesses wrap
                   return texelFetch(u_vramTex, coords, 0);
              }

            int sample16(ivec2 coords) {
                   vec4 colour = sampleVRAM(coords);
                   int r = floatToU5(colour.r);
                   int g = floatToU5(colour.g);
                   int b = floatToU5(colour.b);
                   int msb = int(ceil(colour.a)) << 15;
                   return r | (g << 5) | (b << 10) | msb;
                }

             vec4 texBlend(vec4 colour1, vec4 colour2) {
                        vec4 ret = (colour1 * colour2) / (128.0 / 255.0);
                        ret.a = 1.0;
                        return ret;
                    }

              vec4 handleAlphaValues() {
                        vec4 alpha;

                        switch (transparencyMode) {
                            case 0:                     // B/2 + F/2
                                alpha.xyzw = vec4(0.5, 0.5, 0.5, 0.5);      
                                break;

                            case 1:                     // B + F
                            case 2:                     // B - F (Function will change to reverse subtract)
                                alpha.xyzw = vec4(1.0, 1.0, 1.0, 1.0);      
                                break;

                            case 3:                     // B + F/4
                                alpha.xyzw = vec4(0.25, 0.25, 0.25, 1.0);      
                                break; 
                              }

                        return alpha;

                    }

            void main()
            {

	            if(isScreenQuad == 1){		//Drawing a full screen quad case 
	  
	              ivec2 coords = ivec2(texCoords * vec2(1024.0, 512.0)); 
                  outputColor = texelFetch(u_vramTex, coords, 0);
	              return;
	            }

                //Fix up UVs and apply texture window
                  ivec2 UV = ivec2(floor(texCoords + vec2(0.0001, 0.0001))) & ivec2(0xff);
                  UV = (UV & ~(u_texWindow.xy * 8)) | ((u_texWindow.xy & u_texWindow.zw) * 8); //XY contain Mask, ZW contain Offset  


  	            if(TextureMode == -1){		//No texture, for now i am using my own flag (TextureMode) instead of (inTexpage & 0x8000) 
    		             outputColor.rgb = vec3(color_in.r, color_in.g, color_in.b);
                         outputBlendColor = handleAlphaValues();
                            if((maskBitSetting & 1) == 1){
                                outputColor.a = 1.0;
                            }else{
                                outputColor.a = 0.0;
                            }
                            
                         /*if(((maskBitSetting >> 1) & 1) == 1){
                            int currentPixel = sample16(ivec2((gl_FragCoord.xy - vec2(0.5,0.5)) * vec2(1024.0, 512.0)));
                            if(((currentPixel >> 15) & 1) == 1){
                                discard;
                            }
                         }*/

                 }else if(TextureMode == 0){  //4 Bit texture
 		               ivec2 texelCoord = ivec2(UV.x >> 2, UV.y) + texpageBase;
               
       	               int sample = sample16(texelCoord);
                       int shift = (UV.x & 3) << 2;
                       int clutIndex = (sample >> shift) & 0xf;

                       ivec2 sampleCoords = ivec2(clutBase.x + clutIndex, clutBase.y);

                       outputColor = texelFetch(u_vramTex, sampleCoords, 0);

		                if (outputColor.rgba == vec4(0.0, 0.0, 0.0, 0.0) || 
                            ((outputColor.rgba == vec4(0.0, 0.0, 0.0, 1.0)) && (transparencyMode != 4))) { discard; }

                        outputColor = texBlend(outputColor, vec4(color_in,1.0));

                        //Check if pixel is transparent depending on bit 15 of the final color value

                        bool isTransparent = (((sample16(sampleCoords) >> 15) & 1) == 1);     

                        if(isTransparent && transparencyMode != 4){
                           outputBlendColor = handleAlphaValues();
                        }else{
                          outputBlendColor = vec4(1.0, 1.0, 1.0, 0.0);
                        }

                        //Handle Mask Bit setting

                            /*if(((maskBitSetting >> 1) & 1) == 1){
                                int currentPixel = sample16(ivec2((gl_FragCoord.xy - vec2(0.5,0.5)) * vec2(1024.0, 512.0)));
                                if(((currentPixel >> 15) & 1) == 1){
                                    discard;
                                }
                            } */

		                  if((maskBitSetting & 1) == 1){
                                outputColor.a = 1.0;
                            }

	            }else if (TextureMode == 1) { // 8 bit texture

                           ivec2 texelCoord = ivec2(UV.x >> 1, UV.y) + texpageBase;
               
                           int sample = sample16(texelCoord);
                           int shift = (UV.x & 1) << 3;
                           int clutIndex = (sample >> shift) & 0xff;
                           ivec2 sampleCoords = ivec2(clutBase.x + clutIndex, clutBase.y);
                           outputColor = texelFetch(u_vramTex, sampleCoords, 0);

                            if (outputColor.rgba == vec4(0.0, 0.0, 0.0, 0.0) || 
                            ((outputColor.rgba == vec4(0.0, 0.0, 0.0, 1.0)) && (transparencyMode != 4))) { discard; }

                           outputColor = texBlend(outputColor, vec4(color_in,1.0));

                           //Check if pixel is transparent depending on bit 15 of the final color value

                            bool isTransparent = (((sample16(sampleCoords) >> 15) & 1) == 1);     
                        
                            if(isTransparent && transparencyMode != 4){
                                 outputBlendColor = handleAlphaValues();

                            }else{
                                outputBlendColor = vec4(1.0, 1.0, 1.0, 0.0);
                            }

                        
                            //Handle Mask Bit setting

                              /*if(((maskBitSetting >> 1) & 1) == 1){
                                int currentPixel = sample16(ivec2((gl_FragCoord.xy - vec2(0.5,0.5)) * vec2(1024.0, 512.0)));
                                if(((currentPixel >> 15) & 1) == 1){
                                    discard;
                                }
                            }*/

                        if((maskBitSetting & 1) == 1){
                                outputColor.a = 1.0;
                           }

                    }

	            else {  //16 Bit texture

 		               ivec2 texelCoord = UV + texpageBase;
                       outputColor = sampleVRAM(texelCoord);

                       if (outputColor.rgba == vec4(0.0, 0.0, 0.0, 0.0) || 
                       ((outputColor.rgba == vec4(0.0, 0.0, 0.0, 1.0)) && (transparencyMode != 4))) { discard; }

                       outputColor = texBlend(outputColor, vec4(color_in,1.0));	
                            
                       //Check if pixel is transparent depending on bit 15 of the final color value

                        bool isTransparent = (((sample16(texelCoord) >> 15) & 1) == 1);     

                            if(isTransparent && transparencyMode != 4){
                                outputBlendColor = handleAlphaValues();

                        }else{
                               outputBlendColor  = vec4(1.0, 1.0, 1.0, 0.0);
                        }

                        /*//Handle Mask Bit setting
                        if(((maskBitSetting >> 1) & 1) == 1){
                                int currentPixel = sample16(ivec2((gl_FragCoord.xy - vec2(0.5,0.5)) * vec2(1024.0, 512.0)));
                                if(((currentPixel >> 15) & 1) == 1){
                                    discard;
                                }
                        } */
                        if((maskBitSetting & 1) == 1){
                                outputColor.a = 1.0;
                        }                  
	                }

                    //Dithering is the same for all modes 
                    if(isDithered == 1){    
                         outputColor.rgb = dither(outputColor.rgb, gl_FragCoord.xy - vec2(0.5, 0.5));
                    }
        
            }";

        public Renderer(GameWindowSettings gameWindowSettings, NativeWindowSettings nativeWindowSettings)
             : base(gameWindowSettings, nativeWindowSettings) {

            //Clear the window
            GL.ClearColor(0.0f, 0.0f, 0.0f, 1.0f);
            GL.Clear(ClearBufferMask.ColorBufferBit);
            SwapBuffers();
        }

        protected override void OnLoad() {
            
            //Load shaders 
            shader = new Shader(vertixShader, fragmentShader);
            shader.Use();

            GL.Viewport(0, 0, this.Size.X, this.Size.Y);
            GL.ClearColor(0.0f, 0.0f, 0.0f, 1.0f);      //This can be ignored as the PS1 BIOS will initially draw a black quad clearing the buffer anyway
            GL.Clear(ClearBufferMask.ColorBufferBit);  
            SwapBuffers();
            
            //Get Locations
            fullVram = GL.GetUniformLocation(shader.Program, "fullVram");
            texWindow = GL.GetUniformLocation(shader.Program, "u_texWindow");
            texModeLoc = GL.GetUniformLocation(shader.Program, "TextureMode");
            clutLoc = GL.GetUniformLocation(shader.Program, "inClut");
            texPageLoc = GL.GetUniformLocation(shader.Program, "inTexpage");

            transparencyModeLoc = GL.GetUniformLocation(shader.Program, "transparencyMode");
            maskBitSettingLoc = GL.GetUniformLocation(shader.Program, "maskBitSetting");
            isDitheredLoc = GL.GetUniformLocation(shader.Program, "isDithered");

            display_areay_X_Loc = GL.GetUniformLocation(shader.Program, "display_area_x");
            display_areay_Y_Loc = GL.GetUniformLocation(shader.Program, "display_area_y");
            display_area_X_Offset_Loc = GL.GetUniformLocation(shader.Program, "display_area_x_offset");
            display_area_Y_Offset_Loc = GL.GetUniformLocation(shader.Program, "display_area_y_offset");

            //Create VAO/VBO/Buffers and Textures
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
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, VRAM_WIDTH, VRAM_HEIGHT, 0, PixelFormat.Bgra, PixelType.UnsignedShort1555Reversed, (IntPtr)null);

            GL.BindTexture(TextureTarget.Texture2D, sample_texture);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, VRAM_WIDTH, VRAM_HEIGHT, 0, PixelFormat.Bgra, PixelType.UnsignedShort1555Reversed, (IntPtr)null);

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, vramFrameBuffer);
            GL.FramebufferTexture2D(FramebufferTarget.DrawFramebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, vram_texture, 0);
            if (GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != FramebufferErrorCode.FramebufferComplete) {
                Console.WriteLine("[OpenGL] Uncompleted Frame Buffer !");
            }

            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 2);
            GL.PixelStore(PixelStoreParameter.PackAlignment, 2);
            GL.Uniform1(GL.GetUniformLocation(shader.Program, "u_vramTex"), 0);

        }

        public void SetOffset(Int16 x, Int16 y) {
            //Already sign extended
            drawOffsetX = x; 
            drawOffsetY = y;   
        }

        public void setTextureWindow(ushort x, ushort y, ushort z, ushort w) {
            GL.Uniform4(texWindow, x, y, z, w);
        }

        int scissorBox_x = 0;
        int scissorBox_y = 0;
        int scissorBox_w = VRAM_WIDTH;
        int scissorBox_h = VRAM_HEIGHT;

        public void setScissorBox(int x, int y, int width, int height) {
            GL.Viewport(0, 0, VRAM_WIDTH, VRAM_HEIGHT);

            scissorBox_x = x;
            scissorBox_y = y;
            scissorBox_w = Math.Max(width + 1, 0);
            scissorBox_h = Math.Max(height + 1, 0);

            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, vramFrameBuffer);

            GL.Enable(EnableCap.ScissorTest);
            GL.Scissor(scissorBox_x, scissorBox_y, scissorBox_w, scissorBox_h);
        }

        short[] vertices;
        byte[] colors;
        ushort[] uv;

        public void drawTrinangle(
            short x1, short y1, 
            short x2, short y2,
            short x3, short y3,
            byte r1, byte g1, byte b1,
            byte r2, byte g2, byte b2,
            byte r3, byte g3, byte b3,
            ushort tx1, ushort ty1,
            ushort tx2, ushort ty2,
            ushort tx3, ushort ty3,
            bool isTextured, ushort clut, ushort page, bool isDithered
            ) {
            
            GL.Viewport(0, 0, VRAM_WIDTH, VRAM_HEIGHT);
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, vramFrameBuffer);

            vertices = new short[]{
             x1,  y1,
             x2,  y2,
             x3,  y3
            };
            colors = new byte[]{
             r1,  g1,  b1,
             r2,  g2,  b2,
             r3,  g3,  b3,
            };
            uv = new ushort[] {
             tx1, ty1,
             tx2, ty2,
             tx3, ty3
            };

            if(!ApplyDrawingOffset(ref vertices)) { return; }

            GL.BindBuffer(BufferTarget.ArrayBuffer, vertexBufferObject);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(short), vertices, BufferUsageHint.StreamDraw);
            GL.VertexAttribIPointer(0, 2, VertexAttribIntegerType.Short, 0, (IntPtr)null);  //size: 2 for x,y only!
            GL.EnableVertexAttribArray(0);

            GL.BindBuffer(BufferTarget.ArrayBuffer, colorsBuffer);
            GL.BufferData(BufferTarget.ArrayBuffer, colors.Length * sizeof(byte), colors, BufferUsageHint.StreamDraw);
            GL.VertexAttribIPointer(1, 3, VertexAttribIntegerType.UnsignedByte, 0, (IntPtr)null);
            GL.EnableVertexAttribArray(1);

            if (isTextured) {
                GL.Uniform1(clutLoc, clut);
                GL.Uniform1(texPageLoc, page);
                GL.Uniform1(texModeLoc, (page >> 7) & 3);
                GL.BindBuffer(BufferTarget.ArrayBuffer, texCoords);
                GL.BufferData(BufferTarget.ArrayBuffer, uv.Length * sizeof(ushort), uv, BufferUsageHint.StreamDraw);
                GL.VertexAttribPointer(2, 2, VertexAttribPointerType.UnsignedShort, false, 0, (IntPtr)null);
                GL.EnableVertexAttribArray(2);
               
                if (TextureInvalidate(ref uv, page, clut)) {
                    update_SamplingTexture();
                }

            }
            else {
                GL.Uniform1(texModeLoc, -1);
                GL.Uniform1(clutLoc, 0);
                GL.Uniform1(texPageLoc, 0);
                GL.DisableVertexAttribArray(2);
            }

            GL.Uniform1(isDitheredLoc, isDithered ? 1 : 0);

            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
            UpdateIntersectionTable(ref vertices);
        }

        public void drawRectangle(
            short x1, short y1,
            short x2, short y2,
            short x3, short y3,
            short x4, short y4,
            byte r1, byte g1, byte b1,
            byte r2, byte g2, byte b2,
            byte r3, byte g3, byte b3,
            byte r4, byte g4, byte b4,
            ushort tx1, ushort ty1,
            ushort tx2, ushort ty2,
            ushort tx3, ushort ty3,
            ushort tx4, ushort ty4,

            bool isTextured, ushort clut, ushort page)  {
          

            GL.Viewport(0, 0, VRAM_WIDTH, VRAM_HEIGHT);
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, vramFrameBuffer);
            
            vertices = new short[]{
             x1,  y1,
             x2,  y2,
             x3,  y3,
             x4,  y4,
            };
            colors = new byte[]{
             r1,  g1,  b1,
             r2,  g2,  b2,
             r3,  g3,  b3,
             r4,  g4,  b4,
            };
            uv = new ushort[] {
             tx1, ty1,
             tx2, ty2,
             tx3, ty3,
             tx4, ty4
            };

            if (!ApplyDrawingOffset(ref vertices)) { return; }
            GL.BindBuffer(BufferTarget.ArrayBuffer, vertexBufferObject);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(short), vertices, BufferUsageHint.StreamDraw);
            GL.VertexAttribIPointer(0, 2, VertexAttribIntegerType.Short, 0, (IntPtr)null);  //size: 2 for x,y only!
            GL.EnableVertexAttribArray(0);

            GL.BindBuffer(BufferTarget.ArrayBuffer, colorsBuffer);
            GL.BufferData(BufferTarget.ArrayBuffer, colors.Length * sizeof(byte), colors, BufferUsageHint.StreamDraw);
            GL.VertexAttribIPointer(1, 3, VertexAttribIntegerType.UnsignedByte, 0, (IntPtr)null);
            GL.EnableVertexAttribArray(1);

            if (isTextured) {
                GL.Uniform1(clutLoc, clut);
                GL.Uniform1(texPageLoc, page);
                GL.Uniform1(texModeLoc, (page >> 7) & 3);
                GL.BindBuffer(BufferTarget.ArrayBuffer, texCoords);
                GL.BufferData(BufferTarget.ArrayBuffer, uv.Length * sizeof(ushort), uv, BufferUsageHint.StreamDraw);
                GL.VertexAttribPointer(2, 2, VertexAttribPointerType.UnsignedShort, false, 0, (IntPtr)null);
                GL.EnableVertexAttribArray(2);
                if (TextureInvalidate(ref uv, page, clut)) {
                    update_SamplingTexture();
                }
            }
            else {
                GL.Uniform1(texModeLoc, -1);
                GL.Uniform1(clutLoc, 0);
                GL.Uniform1(texPageLoc, 0);
                GL.DisableVertexAttribArray(2);
            }

            GL.Uniform1(isDitheredLoc, 0);  //RECTs are NOT dithered

            GL.DrawArrays(PrimitiveType.TriangleFan, 0, 4);
            UpdateIntersectionTable(ref vertices);
        }

        public void DrawLines(ref short[] vertices, ref byte[] colors, bool isPolyLine, bool isDithered) {
            /*short firstX = vertices[0];
            short firstY = vertices[1];
            short lastX = vertices[vertices.Length - 2];
            short lastY = vertices[vertices.Length - 1];
            bool isWireFrame = (firstX == lastX) && (firstY == lastY);*/

            GL.Viewport(0, 0, VRAM_WIDTH, VRAM_HEIGHT);
            GL.Uniform1(texModeLoc, -1);
            if (!ApplyDrawingOffset(ref vertices)) { return; }

            GL.BindBuffer(BufferTarget.ArrayBuffer, vertexBufferObject);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(short), vertices, BufferUsageHint.StreamDraw);
            GL.VertexAttribIPointer(0, 2, VertexAttribIntegerType.Short, 0, (IntPtr)null);
            GL.EnableVertexAttribArray(0);

            GL.BindBuffer(BufferTarget.ArrayBuffer, colorsBuffer);
            GL.BufferData(BufferTarget.ArrayBuffer, colors.Length * sizeof(byte), colors, BufferUsageHint.StreamDraw);
            GL.VertexAttribIPointer(1, 3, VertexAttribIntegerType.UnsignedByte, 0, (IntPtr)null);
            GL.EnableVertexAttribArray(1);

            GL.Uniform1(isDitheredLoc, isDithered ? 1 : 0);

            GL.DrawArrays(isPolyLine ? PrimitiveType.LineStrip : PrimitiveType.Lines, 0, vertices.Length / 2);
            UpdateIntersectionTable(ref vertices);
        }

        public void ReadBackTexture(UInt16 x, UInt16 y, UInt16 width, UInt16 height, ref UInt16[] texData) {
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, vramFrameBuffer);
            GL.ReadPixels(x, y, width, height, PixelFormat.Rgba, PixelType.UnsignedShort1555Reversed, texData);
        }

        public void vramFill(float r, float g, float b, int x, int y, int width, int height) {
            //Fill does NOT occur when Xsiz=0 or Ysiz=0 (unlike as for Copy commands).
            //Xsiz=400h works only indirectly: Param=400h is handled as Xsiz=0, however, Param=3F1h..3FFh is rounded-up and handled as Xsiz=400h.

            //If I handle 400h as 0 jakub gpu tests don't render correctly...
            if (/* width == 0x400 || */ width == 0 || height == 0) {
                return;
            }
            if (width >= 0x3F1 && width <= 0x3FF) {
                width = 0x400;
            }

            /*The "Color" parameter is a 24bit RGB value, however, the actual fill data is 16bit: 
            The hardware automatically converts the 24bit RGB value to 15bit RGB (with bit15=0).
            Fill is NOT affected by the Mask settings (acts as if Mask.Bit0,1 are both zero).*/

            // I assume it uses dithering to convert it, the problem is that GLclear doesn't use the same renderign pipline
            //TODO: Find a solution 

            //GL.Uniform1(isDitheredLoc, 1);     

            GL.Viewport(0, 0, VRAM_WIDTH, VRAM_HEIGHT);
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, vramFrameBuffer);
            GL.ClearColor(r, g, b, 0.0f);       //alpha = 0 (bit 15)
            GL.Scissor(x,y, width, height);
            GL.Clear(ClearBufferMask.ColorBufferBit);

            short[] rectangle = new short[] {
                (short)x, (short)y,
                (short)(x+width), (short)y,
                (short)(x+width),(short)(y+height),
                (short)x, (short)(y+height)
            };

            UpdateIntersectionTable(ref rectangle);
            
            //update_SamplingTexture();
            
            GL.Scissor(scissorBox_x, scissorBox_y, scissorBox_w, scissorBox_h);
            GL.ClearColor(0, 0, 0, 1.0f);           
        }

        public void update_vram(int x, int y , int width, int height, ref ushort[] textureData) {
            if (width == 0) { width = VRAM_WIDTH; }
            if (height == 0) { height = VRAM_HEIGHT; }

            if (CPU.BUS.GPU.force_set_mask_bit) { //not sure
                for (int i = 0; i < textureData.Length; i++) { textureData[i] |= (1 << 15); }
            }

            GL.Disable(EnableCap.ScissorTest);

            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, 0);
            GL.BindTexture(TextureTarget.Texture2D, vram_texture);
            GL.TexSubImage2D(TextureTarget.Texture2D,0,x,y,width,height, 
                PixelFormat.Rgba, PixelType.UnsignedShort1555Reversed, textureData);

            short[] rectangle = new short[] {
                (short)x, (short)y,
                (short)(x+width), (short)y,
                (short)(x+width),(short)(y+height),
                (short)x, (short)(y+height)
            };

            UpdateIntersectionTable(ref rectangle);

            //update_SamplingTexture();

            GL.Enable(EnableCap.ScissorTest);
            GL.Scissor(scissorBox_x, scissorBox_y, scissorBox_w, scissorBox_h);
        }

        private void update_SamplingTexture() {
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, vramFrameBuffer);
            GL.BindTexture(TextureTarget.Texture2D, sample_texture);
            GL.CopyTexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, 0, 0, VRAM_WIDTH, VRAM_HEIGHT);
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, vramFrameBuffer);

            //Reset all blocks to clean
            for (int i = 0; i < VRAM_WIDTH / IntersectionBlockLength; i++) {
                for (int j = 0; j < VRAM_HEIGHT / IntersectionBlockLength; j++) {
                    IntersectionTable[j, i] = 0;
                }
            }
        }

        internal void VramToVramCopy(int x0_src, int y0_src, int x0_dest, int y0_dest, int width, int height) {
            //Very stupid and slow
            //But seems correct (Tested on Crash Team Racing, Sony intro)

            //Reads the vram fbo, which is the updated one
            ushort[] srcData = new ushort[width * height];
            GL.ReadPixels(x0_src, y0_src, width, height, PixelFormat.Rgba, PixelType.UnsignedShort1555Reversed, srcData);   
            if (CPU.BUS.GPU.force_set_mask_bit) { //not sure
                for (int i = 0; i < srcData.Length; i++) { srcData[i] |= (1 << 15); }
            }

            GL.Disable(EnableCap.ScissorTest);

            update_vram(x0_dest, y0_dest, width, height, ref srcData);
            //UpdateIntersectionTable(); handeled in update_vram

            GL.Enable(EnableCap.ScissorTest);
            GL.Scissor(scissorBox_x, scissorBox_y, scissorBox_w, scissorBox_h);
        }
        internal void setBlendingFunction(uint function) {
            GL.Uniform1(transparencyModeLoc, (int)function);
            //Console.WriteLine("Transparency: " +  function);

            GL.Enable(EnableCap.Blend);
            //B = Destination
            //F = Source
            GL.BlendFunc(BlendingFactor.Src1Color, BlendingFactor.Src1Alpha);        //Alpha values are handled in GLSL
            GL.BlendEquation(function == 2? BlendEquationMode.FuncReverseSubtract : BlendEquationMode.FuncAdd);
        }

        internal void maskBitSetting(int setting) {
            GL.Uniform1(maskBitSettingLoc, setting);
        }

        public bool TextureInvalidate(ref ushort[] uv, uint texPage, uint clut) {
            //Experimental 
            //Checks whether the textured primitive is reading from a dirty block

            int mode = (int)((texPage >> 7) & 3);
            uint divider = (uint)(4 >> mode);
           
            uint smallestX = 1023;
            uint smallestY = 511;
            uint largestX = 0;
            uint largestY = 0;

            for (int i = 0; i < uv.Length; i += 2) {
                if (uv[i] > largestX) {
                    largestX = uv[i];
                }
                if (uv[i] < smallestX) {
                    smallestX = uv[i];
                }
            }

            for (int i = 1; i < uv.Length; i += 2) {
                if (uv[i] > largestY) {
                    largestY = uv[i];
                }
                if (uv[i] < smallestY) {
                    smallestY = uv[i];
                }
            }

            smallestX = Math.Min(smallestX, 1023);
            smallestY = Math.Min(smallestY, 511);
            largestX = Math.Min(largestX, 1023);
            largestY = Math.Min(largestY, 511);

            uint texBaseX = (texPage & 0xF) * 64;
            uint texBaseY = ((texPage >> 4) & 1) * 256;

            uint width = (largestX - smallestX) / divider;
            uint height = (largestY - smallestY) / divider;

            uint left =  texBaseX / IntersectionBlockLength;
            uint right = ((texBaseX + width) & 0x3FF) / IntersectionBlockLength;           
            uint up = texBaseY / IntersectionBlockLength;
            uint down = ((texBaseY + height) & 0x1FF) / IntersectionBlockLength;         

            //ANDing with 7,15 take cares of vram access wrap when reading textures (same effect as mod 8,16)  
            for (uint y = up; y != ((down + 1) & 0x7); y = (y + 1) & 0x7) {
                for (uint x = left; x != ((right + 1) & 0xF); x = (x + 1) & 0xF) {
                    if (IntersectionTable[y, x] == 1) { return true; }
                }
            }

            //For 4/8 bpp modes we need to check the clut table 
            if (mode == 0 || mode == 1) {
                uint clutX = (clut & 0x3F) * 16;
                uint clutY = ((clut >> 6) & 0x1FF);
                left = clutX / IntersectionBlockLength;               
                up = clutY / IntersectionBlockLength;             //One line 
                for (uint x = left; x < VRAM_WIDTH / IntersectionBlockLength; x++) {
                    if (IntersectionTable[up, x] == 1) { return true; }
                }
            }

            return false;
        }
        public void UpdateIntersectionTable(ref short[] vertices) {
            //Mark any affected blocks as dirty
            int smallestX = 1023;
            int smallestY = 511;
            int largestX = 0;
            int largestY = 0;

            for (int i = 0; i < vertices.Length; i += 2) {
                if (vertices[i] > largestX) {
                    largestX = vertices[i];
                }
                if (vertices[i] < smallestX) {
                    smallestX = vertices[i];
                }
            }

            for (int i = 1; i < vertices.Length; i += 2) {
                if (vertices[i] > largestY) {
                    largestY = vertices[i];
                }
                if (vertices[i] < smallestY) {
                    smallestY = vertices[i];
                }
            }

            smallestX = Math.Clamp(smallestX, 0, 1023);
            smallestY = Math.Clamp(smallestY, 0, 511);
            largestX = Math.Clamp(largestX, 0, 1023);
            largestY = Math.Clamp(largestY, 0, 511);

            int left = smallestX / IntersectionBlockLength;
            int right = largestX / IntersectionBlockLength;        
            int up = smallestY  / IntersectionBlockLength;
            int down = largestY / IntersectionBlockLength;         

            //No access wrap for drawing, anything out of bounds is clamped 
            for (int y = up; y <= down; y++) {
                for (int x = left; x <= right; x++) {
                    IntersectionTable[y, x] = 1;
                }
            }
        }
        
        private short Signed11Bits(ushort input) {
            return (short)(((short)(input << 5)) >> 5);
        }

        //Applies Drawing offset and checks if final dimensions are valid (within range)
        private bool ApplyDrawingOffset(ref short[] vertices) {
            short maxX = -1025;
            short maxY = -513;
            short minX = 1024;
            short minY = 512;

            for (int i = 0; i < vertices.Length; i += 2) {
                vertices[i] = Signed11Bits((ushort)(Signed11Bits((ushort)vertices[i]) + drawOffsetX));
                if (vertices[i] > maxX) { maxX = vertices[i]; }
                if (vertices[i] < minX) { minX = vertices[i]; }
            }

            for (int i = 1; i < vertices.Length; i += 2) {
                vertices[i] = Signed11Bits((ushort)(Signed11Bits((ushort)vertices[i]) + drawOffsetY));
                if (vertices[i] > maxY) { maxY = vertices[i]; }
                if (vertices[i] < minY) { minY = vertices[i]; }
            }

            return !(((maxX - minX) > 1023) || ((maxY - minY) > 511) || (maxX < minX) || (maxY < minY));
        }

        public void display() {
            displayFrame();
            SwapBuffers();
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
        public void disableBlending() {
            ///GL.Disable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.One, BlendingFactor.Zero);
            GL.BlendEquation(BlendEquationMode.FuncAdd);
            GL.Uniform1(transparencyModeLoc, 4);    //0-3 for the functions, 4 = disabled
        }

        public void modifyAspectRatio() {
            float disp_x = CPU.BUS.GPU.HorizontalRange;
            float disp_y = CPU.BUS.GPU.VerticalRange;


            if (!showTextures) {

                GL.Uniform1(display_areay_X_Loc, disp_x);
                GL.Uniform1(display_areay_Y_Loc, disp_y);

                if (disp_x / disp_y < (float)this.Size.X / (float)this.Size.Y) {

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

            }else if (e.Key.Equals(Keys.D)) {
                Console.WriteLine("Toggle Debug");
                CPU.BUS.debug = !CPU.BUS.debug;
                Thread.Sleep(100);

            }else if (e.Key.Equals(Keys.P)) {
                CPU.IsPaused = !CPU.IsPaused;
                Thread.Sleep(100);

            }else if (e.Key.Equals(Keys.Tab)) {
                showTextures = !showTextures;
                Thread.Sleep(100);

            }else if (e.Key.Equals(Keys.F)) {
                isFullScreen = !isFullScreen;
                this.WindowState = isFullScreen ? WindowState.Fullscreen : WindowState.Normal;
                this.CursorState = isFullScreen ? CursorState.Hidden : CursorState.Normal;
                Thread.Sleep(100);
            }
        }
        
        protected override void OnUpdateFrame(FrameEventArgs args) {
            base.OnUpdateFrame(args);
            //Clock the CPU
            CPU.tick();

            //Read controller input 
            CPU.BUS.IO_PORTS.Controller1.ReadInput(JoystickStates[0]);
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
            GL.DeleteProgram(shader.Program);

            
            base.OnUnload();
        }
    }

}
