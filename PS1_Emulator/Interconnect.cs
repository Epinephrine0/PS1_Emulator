using PS1_Emulator.PS1_Emulator;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PS1_Emulator {

    public class BUS {      //Main BUS, connects the CPU to everything
        UInt32 offset;
        public BIOS BIOS;
        public MemoryControl memoryControl;
        public RAM_SIZE ram_size;
        public CACHECONTROL cacheControl;
        public RAM RAM;
        public SPU SPU;
        public EXPANSION1 expansion1;
        public EXPANSION2 expansion2;
        public DMA DMA;
        public GPU GPU;
        public CD_ROM CD_ROM;
        public TIMER1 TIMER1;
        public TIMER2 TIMER2;
        public IO_PORTS IO_PORTS;
        public Scratchpad scratchpad;
        public MDEC MDEC;

        private readonly UInt32[] region_Mask = { 
                    // KUSEG: 2048MB
                       0xffffffff, 0xffffffff, 0xffffffff , 0xffffffff,
                    // KSEG0: 512MB
                       0x7fffffff,
                    // KSEG1: 512MB
                       0x1fffffff,
                    // KSEG2: 1024MB
                       0xffffffff, 0xffffffff
        };

        //No class for now
        public Range TIMER0 = new Range(0x1F801100, 0xF+1);        //Assumption 
        public bool debug = false;

        public BUS(Renderer mainWindow) {
            try {
                BIOS = new BIOS(@"BIOS\PSX - SCPH1001.BIN");
                mainWindow.Title += BIOS.ID.Contains("1001") ? (" - BIOS: " + BIOS.ID) : "";
            }
            catch (FileNotFoundException e) {
                Console.WriteLine("ERROR: PSX BIOS WAS NOT FOUND!");
                Console.WriteLine("Press any key to exit");
                Console.ReadKey();
                mainWindow.Close();
            }
            this.memoryControl = new MemoryControl();   
            this.ram_size = new RAM_SIZE(); 
            this.cacheControl = new CACHECONTROL(); 
            this.RAM = new RAM();
            this.SPU = new SPU();
            this.expansion1 = new EXPANSION1();
            this.expansion2 = new EXPANSION2();
            this.DMA = new DMA();
            this.TIMER1 = new TIMER1();
            this.TIMER2 = new TIMER2();
            this.GPU = new GPU(mainWindow, ref TIMER1);
            this.CD_ROM = new CD_ROM();
            this.IO_PORTS = new IO_PORTS();
            this.scratchpad = new Scratchpad();
            this.MDEC = new MDEC(); 
            
        }
       

        public UInt32 loadWord(UInt32 address) {
            uint physical_address = mask(address);

            if (BIOS.range.contains(physical_address) != null) {

                offset = (UInt32) BIOS.range.contains(physical_address);


                return BIOS.fetch(offset);


            } else if (RAM.range.contains(physical_address) != null) {

                offset = (UInt32)RAM.range.contains(physical_address);
                
                return RAM.read(offset);                        

            }
            else if (IRQ_CONTROL.range.contains(physical_address) != null) {
                offset = (UInt32)IRQ_CONTROL.range.contains(physical_address);

                return IRQ_CONTROL.read32(offset);

            }
            else if (DMA.range.contains(physical_address) != null) {

                offset = (UInt32)DMA.range.contains(physical_address);

                return DMA.read_dma_reg(offset);

            }
            else if (GPU.range.contains(physical_address) != null) {


                offset = (UInt32)GPU.range.contains(physical_address);

                switch (offset) {
                    case 0:
                        //Skip for now
                        //Debug.WriteLine("Ignoring GPU read offset: " + offset);
                        return 0xFF; // this.GPU.gpuReadReg();
                       
                    case 4:

                        return this.GPU.read_GPUSTAT();
     
                    default:
                        throw new Exception("Unhandled read to offset " + offset);
                }

            }


            else if (this.TIMER0.contains(physical_address) != null) { 

                //Console.WriteLine("Unhandled read to TIMER0 register at address: " + address.ToString("X"));
                return 0;  
            }
            else if (this.TIMER1.range.contains(physical_address) != null) {
                offset = (UInt32)TIMER1.range.contains(physical_address);

                return TIMER1.read(offset);  

            }
            else if (this.TIMER2.range.contains(physical_address) != null) {
                offset = (UInt32)TIMER2.range.contains(physical_address);
                return TIMER2.read(offset);

            }
            else if (address == 0x1F801060) {
                //Memory Control 2
                return 0X00000B88;

            }else if (address >= 0x1F801014 && address < 0x1F801018) {  //SPU delay 
                return 0x200931E1;
            }
            else if (scratchpad.range.contains(physical_address) != null) {
                offset = (UInt32)scratchpad.range.contains(physical_address);

                return scratchpad.read(offset);
            }
            else if (this.IO_PORTS.range.contains(physical_address) != null) {
                offset = (UInt32)IO_PORTS.range.contains(physical_address);

                return IO_PORTS.read32(offset);
             }

            //MDEC
            else if (address >= 0x1F801820 && address <= 0x1F801824) {
                return 0;
            }

            else {
                throw new Exception("cannot find address: " + address.ToString("X") + " in memory map");

            }
        }
        int CDROM_delay_read;
        int CDROM_delay_write; 
        public void storeWord(UInt32 address,UInt32 value) {
            uint physical_address = mask(address);


            if (memoryControl.range.contains(physical_address) != null) {
                switch (memoryControl.range.contains(physical_address)) {

                    case 0:
                        if (value != 0x1f000000) {
                            throw new Exception("Bad expansion 1 base address: " + value.ToString("X"));  
                        }
                        break;

                    case 4:
                        if (value != 0x1f802000) {
                            throw new Exception("Bad expansion 1 base address: " + value.ToString("X"));
                        }
                        break;

                    case 0x18:

                        CDROM_delay_write = (byte)((value & 0xF) + 1);  //testing
                        CDROM_delay_read = (byte)(((value >> 4) & 0xF) + 1);  //testing
                    
                        break;
                    default:
                        //Debug.WriteLine("Unhandled write to MEMCONTROL register, address: " + address.ToString("X"));
                        break;
                }


            }
            else if (ram_size.range.contains(physical_address) != null) {        

                offset = (UInt32)ram_size.range.contains(physical_address);
                this.ram_size.set_Size(offset, value);                  //Configure ram size (not necessary)


            }
            else if (RAM.range.contains(physical_address) != null) {             //Write to RAM

                offset = (UInt32)RAM.range.contains(physical_address);

                this.RAM.write(offset, value);

            }

            else if (cacheControl.range.contains(physical_address) != null) {

                Debug.WriteLine("Unhandled write to CACHECONTROL register, address: " + address.ToString("X"));
            }

            else if (IRQ_CONTROL.range.contains(physical_address) != null) {

                offset = (UInt32)IRQ_CONTROL.range.contains(physical_address);

                IRQ_CONTROL.write(offset, (ushort)value);   

            }
            else if (DMA.range.contains(physical_address) != null) {

                offset = (UInt32)DMA.range.contains(physical_address);
                DMA.set_dma_reg(offset, value);

                DMAChannel activeCH = DMA.is_active(offset);

                if (activeCH != null) {
                    if(activeCH.get_sync() == activeCH.Sync["LinkedList"]) {
                        dma_LinkedList_transfer(ref activeCH);
                    }
                    else {
                        dma_transfer(ref activeCH);
                    }
                    

                }

            }
            else if (GPU.range.contains(physical_address) != null) {

                offset = (UInt32)GPU.range.contains(physical_address);

                switch (offset) {

                    case 0:

                        this.GPU.write_GP0(value);
                        break;

                    case 4:

                        this.GPU.write_GP1(value);
                        break;

                    default:
                        throw new Exception("Unhandled write to offset: " + offset + " val: " + value.ToString("x")
                                            + "Physical address: " + physical_address.ToString("x"));
                }




            }
            else if (this.TIMER0.contains(physical_address) != null) {   

                Debug.WriteLine("Unhandled write to TIMER0 register at address: " + address.ToString("X"));
                
            }
            else if (this.TIMER1.range.contains(physical_address) != null) {
                offset = (UInt32)TIMER1.range.contains(physical_address);

                TIMER1.write(offset, value);

            }
            else if (this.TIMER2.range.contains(physical_address) != null) {
                offset = (UInt32)TIMER2.range.contains(physical_address);

                TIMER2.write(offset, value);    
            }else if (this.scratchpad.range.contains(physical_address) != null) {  
                offset = (UInt32)scratchpad.range.contains(physical_address);
                scratchpad.write(offset,value);
            }


            //MDEC
            else if (address >= 0x1F801820 && address <= 0x1F801824) {
                return;
            }

            else {

                throw new Exception("unknown address: " + address.ToString("X") + " - " + " Physical: " + physical_address.ToString("x"));

            }
        }

      

        private UInt32 mask(UInt32 address) {

            UInt32 index = address >> 29;
            UInt32 physical_address = address & this.region_Mask[index];

            return physical_address;


        }

        internal byte load8(UInt32 address) {
            uint physical_address = mask(address);

            if (debug) {
               // Debug.WriteLine("ADDR:" + address.ToString("x"));
            }
            if (this.BIOS.range.contains(physical_address) != null) {
                offset = (UInt32)BIOS.range.contains(physical_address);

                return BIOS.load8(offset);

            }else if (this.expansion1.range.contains(physical_address) != null) {
               
                return (byte)0xff;

            }else if (this.RAM.range.contains(physical_address) != null) {
                offset = (UInt32)RAM.range.contains(physical_address);


                return this.RAM.load8(offset);

            }else if (this.CD_ROM.range.contains(physical_address) != null) {
                offset = (UInt32)CD_ROM.range.contains(physical_address);

                /*if (CDROM_delay_read > 0) {
                    //return 0;
                    
                }*/
                return this.CD_ROM.load8(offset);

            }
            else if (this.IO_PORTS.range.contains(physical_address) != null) {
                offset = (UInt32)IO_PORTS.range.contains(physical_address);

                return IO_PORTS.read(offset);
            }
            else if (this.scratchpad.range.contains(physical_address) != null) {
                offset = (UInt32)scratchpad.range.contains(physical_address);

                return scratchpad.load8(offset);
            }
            else if (address == 0x1f8010f6) { //I don't even know what I am ignoring 

                return 0;
            }


            throw new Exception("Unhandled load8 at address : " + address.ToString("x"));


        }

        internal UInt16 load16(UInt32 address) {
            uint physical_address = mask(address);


            if (this.SPU.range.contains(physical_address) != null) {

                offset = (UInt32)SPU.range.contains(physical_address);

                return this.SPU.load16(offset);
            }
            else if (this.RAM.range.contains(physical_address) != null) {

                offset = (UInt32)RAM.range.contains(physical_address);

                return this.RAM.load16(offset);
            }
            else if (IRQ_CONTROL.range.contains(physical_address) != null) {

                offset = (UInt32)IRQ_CONTROL.range.contains(physical_address);

                return IRQ_CONTROL.read16(offset);
            }
            else if (this.TIMER0.contains(physical_address) != null) { 

                Debug.WriteLine("Unhandled read to TIMER0 register at address: " + address.ToString("X"));
                return 0;
            }
            else if (this.TIMER1.range.contains(physical_address) != null) {
                offset = (UInt32)TIMER1.range.contains(physical_address);

                return (ushort)TIMER1.read(offset);

            }
            else if (this.TIMER2.range.contains(physical_address) != null) {
                offset = (UInt32)TIMER2.range.contains(physical_address);

                return (ushort)TIMER2.read(offset);

            }
            else if (this.IO_PORTS.range.contains(physical_address) != null) {
                offset = (UInt32)IO_PORTS.range.contains(physical_address);

                return IO_PORTS.read16(offset);
            }
            else if (this.BIOS.range.contains(physical_address) != null){
                offset = (UInt32)BIOS.range.contains(physical_address);

                return this.BIOS.load16(offset);
            }
            else if (this.scratchpad.range.contains(physical_address) != null) {
                offset = (UInt32)scratchpad.range.contains(physical_address);
                
                return this.scratchpad.load16(offset);
            }
            else if (this.expansion1.range.contains(physical_address) != null) {    //ignore expansion 1

                return 0xFF;
            }
            

            Console.WriteLine("[BUS] Ignored load16 from address: " + address.ToString("x"));    

            
            throw new Exception("Unhandled load16 at address : " + address.ToString("x") + "\n" + 
                                "Physical address: " + physical_address.ToString("x")
                                 );


        }

        public void storeHalf(UInt32 address, UInt16 value) {
            uint physical_address = mask(address);

            if (this.SPU.range.contains(physical_address) != null) {
                offset = (UInt32)SPU.range.contains(physical_address);

                this.SPU.store16(offset, value);

                return;
            }

            else if (this.TIMER0.contains(physical_address)!= null) {

                Debug.WriteLine("Unhandled write to TIMER0 register at address: " + address.ToString("X"));
                return;
            }
            else if (this.TIMER1.range.contains(physical_address) != null) {
                offset = (UInt32)TIMER1.range.contains(physical_address);

                TIMER1.write(offset, value);
                return;
            }
            else if (this.TIMER2.range.contains(physical_address) != null) {
                offset = (UInt32)TIMER2.range.contains(physical_address);

                TIMER2.write(offset, value);
                return;

            }
            else if (this.RAM.range.contains(physical_address) != null) {

                offset = (UInt32)RAM.range.contains(physical_address);

                this.RAM.store16(offset,value);
                return;
            }
            else if (IRQ_CONTROL.range.contains(physical_address) != null) {

                offset = (UInt32)IRQ_CONTROL.range.contains(physical_address);
                IRQ_CONTROL.write(offset, value);

                return;

            }
            else if (this.IO_PORTS.range.contains(physical_address) != null){

                offset = (UInt32)IO_PORTS.range.contains(physical_address);

                IO_PORTS.write(offset, value);  
                return;
            }
            else if (this.scratchpad.range.contains(physical_address) != null) {
                offset = (UInt32)scratchpad.range.contains(physical_address);

                this.scratchpad.store16(offset, value);

                //Debug.WriteLine("ignoring scratchpad");

                return;
            }

            throw new Exception("Unhandled store16 at address : " + address.ToString("x"));


        }
        public void storeByte(UInt32 address, byte value) {
            uint physical_address = mask(address);

            if (this.expansion2.range.contains(physical_address) != null) {
                Debug.WriteLine("Unhandled write to EXPANTION2 at address : " + address.ToString("x"));
                return;
            }
            else if (this.RAM.range.contains(physical_address) != null) {
                offset = (UInt32)RAM.range.contains(physical_address);

                this.RAM.store8(offset, value);
                return;

            }else if (this.CD_ROM.range.contains(physical_address) != null) {
                offset = (UInt32)CD_ROM.range.contains(physical_address);

                /*if (CDROM_delay_write > 0) {
                 //   return;

                }
                else {
                    this.CD_ROM.store8(offset, value);

                }*/
                this.CD_ROM.store8(offset, value);

                return;

            }
            else if (this.IO_PORTS.range.contains(physical_address) != null) {

                offset = (UInt32)IO_PORTS.range.contains(physical_address);

                this.IO_PORTS.write(offset, value);

                return;

            }else if (this.scratchpad.range.contains(physical_address) != null) {

                offset = (UInt32)scratchpad.range.contains(physical_address);

                this.scratchpad.store8(offset, value);
                //Debug.WriteLine("Ignoring store8 to Scratchpad");
                return;
            }
            else if (address == 0x1f8010f6) { //I don't even know what I am ignoring 

                return;
            }


            throw new Exception("Unhandled store8 at address : " + address.ToString("x"));


        }
        private void dma_LinkedList_transfer(ref DMAChannel activeCH) {     
            DMAChannel ch = activeCH;

            if (ch.get_direction() == ch.Direction["ToRam"]) {
                throw new Exception("Invalid direction for LinkedList transfer");
            }

            if (ch.get_portnum() != 2) {
                throw new Exception("Attempt to use LinkedList mode in DMA port: " + ch.get_portnum());
            }


            UInt32 address = ch.read_base_addr() & 0x1ffffc;

          
            while (true) {


                UInt32 header = this.RAM.read(address);
                UInt32 num_of_words = header >> 24;

                while (num_of_words > 0) {
                    address = (address + 4) & 0x1ffffc;

                    UInt32 command = this.RAM.read(address);
                    this.GPU.write_GP0(command);

                    num_of_words -= 1;

                }

                if ((header & 0x800000) != 0) {
                    break;
                }

                address = header & 0x1ffffc;
            }

            ch.done();

          


        }

        private void dma_transfer(ref DMAChannel activeCH) {
            DMAChannel ch = activeCH;
            int arrPtr = 0;
            int step;

            if (ch.get_step() == ch.Step["Increment"]) {
                step = 4;
            }
            else {
                step = -4;
            }

            UInt32 base_address = ch.read_base_addr();
            UInt32? transfer_size = ch.get_transfer_size();
            
            if (transfer_size == null) {
                throw new Exception("transfer size is null, LinkedList mode?");

            }

            while (transfer_size > 0) {
                UInt32 current_address = base_address & 0x1ffffc;

                if (ch.get_direction() == ch.Direction["FromRam"]) {

                    UInt32 data = this.RAM.read(current_address);

                    switch (ch.get_portnum()) {
                        case 0: //MDECin  (RAM to MDEC)
                            break;


                        case 2:

                            this.GPU.write_GP0(data);

                            break;
                        case 4:
                            this.SPU.DMAtoSPU(data);

                            if(transfer_size - 1 <= 0) {
                                this.SPU.DMA_Read_Request = 0;
                                
                            }

                            break;

                        default:

                            throw new Exception("Unhandled DMA destination port: " + ch.get_portnum());

                    }
                }

                else {
                    switch (ch.get_portnum()) {
                        case 1: //MDECout (MDEC to RAM)
                            break;

                        case 2:
                            
                            UInt16 pixel0 = GPU.TexData[arrPtr];
                            UInt16 pixel1 = GPU.TexData[arrPtr + 1]; 
                            
                            UInt32 merged_Pixels = (uint)(pixel0 | (pixel1 << 16));

                            this.RAM.write(current_address, merged_Pixels);

                            break;

                      case 3:

                          /*  if (CD_ROM.currentSector.Count == 0) {
                                this.ram.write(current_address, (uint)((CD_ROM.padding) | (CD_ROM.padding << 8) | (CD_ROM.padding << 16) | (CD_ROM.padding << 24)));
                                break;
                            }
                          */

                            uint byte0 = CD_ROM.currentSector.Dequeue();
                            uint byte1 = CD_ROM.currentSector.Dequeue();
                            uint byte2 = CD_ROM.currentSector.Dequeue();
                            uint byte3 = CD_ROM.currentSector.Dequeue();

                            UInt32 merged_bytes = (byte0 | (byte1 << 8) | (byte2 << 16) | (byte3 << 24));
                           
                            this.RAM.write(current_address, merged_bytes);

                            break;

                        case 6:
                            switch (transfer_size) {

                                case 1:
                                    this.RAM.write(current_address, 0xffffff);
                                    break;


                                default:
                                    this.RAM.write(current_address, (base_address - 4) & 0x1fffff);
                                    break;

                            }
                            break;



                        default:

                            throw new Exception("Unhandled DMA copy port: " + ch.get_portnum());


                    }
                }

                base_address = (UInt32)(base_address + step);
                transfer_size -= 1;
                arrPtr+=2;
            }
            
            ch.done();
            IRQ_CONTROL.IRQsignal(3);

        }

        public void CDROM_tick(int cycles) {
            
            this.CD_ROM.tick(cycles);
            //CDROM_delay_read -= cycles;
            //CDROM_delay_write -= cycles;

        }
    }
}

