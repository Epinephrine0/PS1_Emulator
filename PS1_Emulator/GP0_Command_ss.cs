using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PS1_Emulator {
    public interface GP0_Command_ss {
        bool isReady { get; }
        public void add(uint value);
        public void execute(ref Renderer window);

    }
}
