using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PSXEmulator {
    public class DMAChannel {
         UInt32 portnum;

        //Control register
         UInt32 enable;
         UInt32 direction;
         UInt32 step;
         UInt32 sync;
         UInt32 trigger;
         UInt32 chop;
         byte chop_DMA_window_size;
         byte chop_CPU_window_size;
         byte read_write;

        //Base address register
         UInt32 base_addr;


        //Block Control register
         UInt16 block_size;              //[15:0]
         UInt16 block_count;             //[16:31]



        public Dictionary<string, UInt32> Direction = new Dictionary<string, UInt32>();
        public Dictionary<string, UInt32> Step = new Dictionary<string, UInt32>();
        public Dictionary<string, UInt32> Sync = new Dictionary<string, UInt32>();

        public DMAChannel() {

            this.Direction.Add("ToRam",0);
            this.Direction.Add("FromRam", 1);
            this.Step.Add("Increment", 0);
            this.Step.Add("Decrement", 1);
            this.Sync.Add("Manual",0);
            this.Sync.Add("Request", 1);
            this.Sync.Add("LinkedList", 2);


            this.enable = 0;
            this.direction = this.Direction["ToRam"];
            this.step = this.Step["Increment"];
            this.sync = this.Sync["Manual"];
            this.trigger = 0;
            this.chop = 0;
            this.chop_DMA_window_size = 0;
            this.chop_CPU_window_size = 0;
            this.read_write = 0;

        }
        public void set_portnum(UInt32 num) {

           this.portnum = num;

        }
        public UInt32 get_portnum() {

            return this.portnum;
        }
        public UInt32 get_direction() {

            return this.direction;
        }
        public UInt32 get_step() {

            return this.step;
        }
        public UInt32 get_sync() {

            return this.sync;
        }

        public bool is_active() {

            switch (this.sync) {
                case 0:
                    if ((this.trigger & this.enable) == 1) {
                        return true;
                    }

                    break;

                default:
                    if ((this.enable) == 1) {
                        return true;
                    }
                    break;
            }


            return false;

        }
        public void set_block_control(UInt32 value) {
            this.block_size = (UInt16)value;
            this.block_count = (UInt16)(value>>16);

        }
        public UInt32 read_block_control() {
            UInt32 bc = block_count;
            UInt32 bs = block_size;

            return (bc << 16) | bs;

        }
        public UInt32 read_base_addr() {
            return this.base_addr;  
        }
        public void set_base_addr(UInt32 value) {
             this.base_addr = (value & 0xffffff);   //Only bits [23:0]
        }
        public UInt32 read_control() {
            UInt32 c = 0;

            c = c | (this.direction << 0);
            c = c | (this.step << 1);
            c = c | (this.chop << 8);
            c = c | (this.sync << 9);
            c = (c | ((UInt32)this.chop_DMA_window_size << 16));
            c = (c | ((UInt32)this.chop_CPU_window_size << 20));
            c = c | (this.enable << 24);
            c = c | (this.trigger << 28);
            c = c | ((UInt32)this.read_write << 29);

            return c;

        }
        public void set_control(UInt32 value) {

            if ((value & 1) != 0) {
                this.direction = Direction["FromRam"];
            }
            else {
                this.direction = Direction["ToRam"];
            }

            if (((value >> 1) & 1) !=0) {
                this.step = Step["Decrement"];
            }
            else {
                this.step = Step["Increment"];
            }

            this.chop = ((value >> 8) & 1);


            UInt32 s = ((value >> 9) & 3);
            switch (s) {
                case 0:
                    this.sync = Sync["Manual"];
                    break;

                case 1:
                    this.sync = Sync["Request"];
                    break;

                case 2:
                    this.sync = Sync["LinkedList"];
                    break;

                default:
                    throw new Exception("Unkown DMA sync mode: " + s);
            }

            this.chop_DMA_window_size = (byte)((value >> 16) & 7);
            this.chop_CPU_window_size = (byte)((value >> 20) & 7);

            this.enable = (value >> 24) & 1;
            this.trigger = (value >> 28) & 1;
            this.read_write = (byte)((value >> 29) & 3);

        }
        public UInt32? get_transfer_size() {
            if (this.sync == Sync["Manual"]) {

                return this.block_size;

            }else if (this.sync == Sync["Request"]) {

                return (UInt32?)(this.block_size * this.block_count);

            }
            else {
                return null;
            }

        }
        public bool finished; 
        internal void done() {
            this.enable = 0;
            this.trigger = 0;
            finished = true;   
        }
    }
}
