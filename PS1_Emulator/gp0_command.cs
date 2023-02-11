using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PS1_Emulator {
    internal class GP0_Command {
        public int num_of_parameters;
        public uint opcode;
        public uint[] buffer;
        public int parameters_ptr;
        public GP0_Command(uint opcode, int num_of_paramerters) { 
        
            this.num_of_parameters = num_of_paramerters;
            this.opcode = opcode;
            this.buffer = new uint[num_of_paramerters];

        }
        public void add_parameter(uint parameter) {
            buffer[parameters_ptr++] = parameter;
        }



    }
}
