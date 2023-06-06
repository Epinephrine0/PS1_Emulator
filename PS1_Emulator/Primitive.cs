using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PS1_Emulator {
    public interface Primitive {
        public void add(uint value);
        public void draw(ref Renderer window);

        public bool isReady();

    }
}
