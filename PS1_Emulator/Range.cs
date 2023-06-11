using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PSXEmulator {
    public class Range {
        UInt32 start;
        UInt32 length;

        public Range(uint start, uint length) {
            this.start = start;
            this.length = length;
            }

        public UInt32? contains(UInt32 address) {
            if (address >= start && address < start+length) {

                return address - start;            
            }
            else {
                return null;
            }
           

        }

    }
}
