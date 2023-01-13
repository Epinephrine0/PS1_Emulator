using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PS1_Emulator {
    internal class DMA {
        public Range range = new Range(0x1f801080, 0x80);
        UInt32 control = 0x07654321;

        //Interrupt register
        UInt32 mastet_enabled;     //Bit 23
        byte ch_irq_en;            //irq enable for indivisual channels Bits [22:16]
        byte ch_irq_flags;         //Bits [30:24] indivisual channels (reset by setting 1)
        UInt32 force_irq;          //Bit 15 (higher priority than Bit 23)
        byte read_write;           //Bits [5:0]
        public DMAChannel[] channels;

        private DMAChannel reject = null;

        public DMA() {
         channels = new DMAChannel[7];
            for (int i = 0; i<channels.Length; i++) {
                channels[i] = new DMAChannel();
                channels[i].set_portnum((UInt32)i);
            }   
        }

    public UInt32 read_dma_reg(UInt32 offset) {
            UInt32 ch = (offset & 0x70) >> 4;       //Bits [7:5] for the channel number
            UInt32 field = (offset & 0xf);              //Bits [3:0] for the field number



            switch (ch) {               //Reading a field for a specific channel
                case 0:
                case 1:
                case 2:
                case 3:
                case 4:
                case 5:
                case 6:

                    switch (field) {
                        case 0:
                            return channels[ch].read_base_addr();
                            

                        case 4:

                            return channels[ch].read_block_control();

                        case 8:
                            return channels[ch].read_control();

                        default:
                            throw new Exception("Unhandled DMA read at offset: " + offset.ToString("X"));
                    }

                   
                case 7:         //case 7 is general fields 
                    switch (field) {
                        case 0:
                            return control;

                        case 4:
                            return read_interrupt_reg();

                        default:
                            throw new Exception("Unhandled DMA read at offset: " + offset.ToString("X"));
                    }

                default:
                    throw new Exception("Unhandled DMA read at offset: " + offset.ToString("X"));

            }


        }
        public void set_dma_reg(UInt32 offset, UInt32 value) {


            UInt32 ch = (offset & 0x70) >> 4;       //Bits [7:5] for the channel number
            UInt32 field = (offset & 0xf);              //Bits [3:0] for the field number



            switch (ch) {               //writing a field for a specific channel
                case 0:
                case 1:
                case 2:
                case 3:
                case 4:
                case 5:
                case 6:

                    switch (field) {
                        case 0:
                            channels[ch].set_base_addr(value);
                            break;

                        case 4:
                            channels[ch].set_block_control(value);  
                            break;

                        case 8:
                            channels[ch].set_control(value);
                            break;

                        default:
                            throw new Exception("Unhandled DMA write at offset: " + offset.ToString("X") + " field: " + field + " val: " + value.ToString("X"));
                    }

                break;



                case 7:         //case 7 is general fields 
                    switch (field) {
                        case 0:
                            this.control = value;
                            break;
                        case 4:
                            set_interrupt_reg(value);
                            break;

                        default:
                            throw new Exception("Unhandled DMA write at offset: " + offset.ToString("X") + " val: " + value.ToString("X"));
                    }
                    break;

                default:
                    throw new Exception("Unhandled DMA write at offset: " + offset.ToString("X") + " val: " + value.ToString("X"));

            }

           

        }

        private UInt32 read_interrupt_reg() {
            UInt32 v = 0;
            for (int i = 0; i < channels.Length; i++) {
                if (channels[i].finished) {
                    if ((ch_irq_en >> i & 1) == 1) {
                        ch_irq_flags = (byte)(ch_irq_flags | (1 << i));
                    }
                   
                }
            }

            v = (v | read_write);
            v = (v | (force_irq << 15));
            v = (v | (((UInt32)ch_irq_en) << 16));
            v = (v | (mastet_enabled << 23));
            v = (v | (((UInt32)ch_irq_flags) << 24));
            v = (v | (irq() << 31));

            return v;
        }
        


        private void set_interrupt_reg(UInt32 value) {
            this.read_write = (byte)(value & 0x3f);
            this.force_irq = (value >> 15) & 1;
            this.ch_irq_en = (byte)((value >> 16) & 0x7f);
            this.mastet_enabled = (value >> 23) & 1;
            this.ch_irq_flags = (byte)(this.ch_irq_flags & ~((value >> 24) & 0x3f));      //0x7f??

            for (int i = 0; i < channels.Length; i++) {
                channels[i].finished = ch_irq_flags >> i != 0;
            }

        }
        public UInt32 irq() {
            UInt32 irq = (UInt32)(ch_irq_flags & ch_irq_en);
            if ((irq != 0 && (mastet_enabled != 0)) || force_irq != 0) {
                return 1;
            }
            else {
                return 0;
            }
        }

        internal ref DMAChannel is_active(uint offset) {
            UInt32 ch = (offset & 0x70) >> 4;
            if (ch<=6 && channels[ch].is_active()) {
                return ref channels[ch];
            }
            return ref reject;
        }
    }
}