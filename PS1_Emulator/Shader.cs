using OpenTK.Graphics.OpenGL4;
using System;

namespace PSXEmulator {
    public class Shader {
        public int Handle;
        public Shader(string vert, string frag) {
            int vertexShader = GL.CreateShader(ShaderType.VertexShader);    //Create a vertex shader and ger a pointer
            GL.ShaderSource(vertexShader, vert);                            //Bind the source code text
            CompileShader(vertexShader);                                    //Compile

            int fragmentShader = GL.CreateShader(ShaderType.FragmentShader);    //Same thing for fragment shader
            GL.ShaderSource(fragmentShader, frag);
            CompileShader(fragmentShader);

            //Create a program and attach both shaders to it
            Handle = GL.CreateProgram();
            GL.AttachShader(Handle, vertexShader);
            GL.AttachShader(Handle, fragmentShader);
            LinkProgram(Handle);

            // When the shader program is linked, it no longer needs the individual shaders attached to it;
            // the compiled code is copied into the shader program.
            // Detach them, and then delete them.
            GL.DetachShader(Handle, vertexShader);
            GL.DetachShader(Handle, fragmentShader);
            GL.DeleteShader(fragmentShader);
            GL.DeleteShader(vertexShader);
        }
        private static void CompileShader(int shader) {
            GL.CompileShader(shader);
            GL.GetShader(shader, ShaderParameter.CompileStatus, out int code);  //Check for compilation errors
            if (code != (int)All.True) {
                string infoLog = GL.GetShaderInfoLog(shader);
                throw new Exception($"Error occurred whilst compiling Shader({shader}).\n\n{infoLog}");
            } else {
                Console.WriteLine("[OpenGL] Shader compiled!");
            }
        }
        private static void LinkProgram(int program) {
            GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out var code);    // Check for linking errors
            if (code != (int)All.True) {
                throw new Exception($"Error occurred whilst linking Program({program})");
            }
        }
        public void Use() {
            GL.UseProgram(Handle);
        }
    }
}
