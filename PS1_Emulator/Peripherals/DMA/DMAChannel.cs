using System;
using System.Collections.Generic;

namespace PSXEmulator {
    public class DMAChannel {
         uint portnum;

        //Control register
         uint enable;
         uint direction;
         uint step;
         uint sync;
         uint trigger;
         uint chop;
         byte chop_DMA_window_size;
         byte chop_CPU_window_size;
         byte read_write;

        //Base address register
         uint base_addr;


        //Block Control register
         uint block_size;              //[15:0]
         uint block_count;             //[16:31]



        //public Dictionary<string, UInt32> Direction = new Dictionary<string, UInt32>();
       // public Dictionary<string, UInt32> Step = new Dictionary<string, UInt32>();
       // public Dictionary<string, UInt32> Sync = new Dictionary<string, UInt32>();
        public enum Direction { ToRam = 0, FromRam = 1 }
        public enum Step { Increment = 0, Decrement = 1 }

        public enum Sync { Manual = 0, Request = 1, LinkedList = 2 }

        public DMAChannel() {


            enable = 0;
            direction = (uint)Direction.ToRam;
            step = (uint)Step.Increment;
            sync = (uint)Sync.Manual;
            chop = 0;
            chop_DMA_window_size = 0;
            chop_CPU_window_size = 0;
            read_write = 0;

        }
        public void set_portnum(uint num) {

           portnum = num;

        }
        public uint get_portnum() {

            return portnum;
        }
        public uint get_direction() {

            return direction;
        }
        public uint get_step() {

            return step;
        }
        public uint get_sync() {

            return sync;
        }

        public bool is_active() {

            switch (sync) {
                case 0:
                    if ((trigger & enable) == 1) {
                        return true;
                    }

                    break;

                default:
                    if ((enable) == 1) {
                        return true;
                    }
                    break;
            }


            return false;

        }
        public void set_block_control(uint value) {
            //BC/BS/BA can be in range 0001h..FFFFh (or 0=10000h)
            this.block_size = value & 0xFFFF;
            this.block_count = (value>>16);
            Console.WriteLine("[DMA] Size: " + block_size.ToString("X") + " --- Count: " + block_count.ToString("x"));
            Console.WriteLine("[DMA] SizeV: " + value.ToString("X"));
            if(block_count == 0 && block_size > 0) {  block_count = 1; }
        }
        public uint read_block_control() {
            uint bc = block_count;
            uint bs = block_size;
            return (bc << 16) | bs;
        }
        public uint read_base_addr() {
            return this.base_addr;  
        }
        public void set_base_addr(uint value) {
             this.base_addr = (value & 0xffffff);   //Only bits [23:0]
        }
        public uint read_control() {
            uint c = 0;

            c = c | (this.direction << 0);
            c = c | (this.step << 1);
            c = c | (this.chop << 8);
            c = c | (this.sync << 9);
            c = (c | ((uint)this.chop_DMA_window_size << 16));
            c = (c | ((uint)this.chop_CPU_window_size << 20));
            c = c | (this.enable << 24);
            c = c | (this.trigger << 28);
            c = c | ((uint)this.read_write << 29);

            return c;

        }
        public void set_control(uint value) {
            this.direction = value & 1;
            this.step = ((value >> 1) & 1);
            this.chop = ((value >> 8) & 1);
            this.sync = ((value >> 9) & 3);
            if (sync == 3) {
                throw new Exception("Reserved DMA sync mode: 3");
            }

            this.chop_DMA_window_size = (byte)((value >> 16) & 7);
            this.chop_CPU_window_size = (byte)((value >> 20) & 7);

            this.enable = (value >> 24) & 1;
            this.trigger = (value >> 28) & 1;
            this.read_write = (byte)((value >> 29) & 3);
            Console.WriteLine("[DMA] Set Setting: " + value.ToString("X"));

        }
        public uint? get_transfer_size() {
            if (this.sync == ((uint)Sync.Manual)) {
                Console.WriteLine("[DMA] Reading size (manual): " + this.block_size.ToString("X"));

                return this.block_size;

            }else if (this.sync == ((uint)Sync.Request)) {
                Console.WriteLine("[DMA] Reading size (req): " + this.block_size.ToString("X"));

                return (uint?)(this.block_count * this.block_size);
            }
            else {
                return null;
            }
        }
        public bool finished; 
        internal void done() {
            enable = 0;
            trigger = 0;
            finished = true;   
        }
    }
}
