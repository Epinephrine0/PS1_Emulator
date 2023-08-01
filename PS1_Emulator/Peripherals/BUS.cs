using PSXEmulator.Peripherals;
using PSXEmulator.PS1_Emulator;
using System;

namespace PSXEmulator {
    public class BUS {      //Main BUS, connects the CPU to everything
        public BIOS BIOS;
        public MemoryControl MemoryControl;
        public RAM_SIZE RamSize;
        public CACHECONTROL CacheControl;
        public RAM RAM;
        public SPU SPU;
        public Expansion1 Expansion1;
        public Expansion2 Expansion2;
        public DMA DMA;
        public GPU GPU;
        public CD_ROM CD_ROM;
        public TIMER1 Timer1;
        public TIMER2 Timer2;
        public IO_PORTS IO_PORTS;
        public Scratchpad Scratchpad;
        public MDEC MDEC;
        private uint[] RegionMask = { 
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

        //TODO: Imeplement ignored stuff 

        public BUS(
            BIOS BIOS, RAM RAM, Scratchpad Scratchpad,
            CD_ROM CDROM, SPU SPU, DMA DMA, IO_PORTS IO, MemoryControl MemCtrl, 
            RAM_SIZE RamSize, CACHECONTROL CacheControl, Expansion1 Ex1, Expansion2 Ex2,
            TIMER1 Timer1, TIMER2 Timer2, MDEC MDEC, GPU GPU
            ) {
            this.BIOS = BIOS;
            this.RAM = RAM;
            this.Scratchpad = Scratchpad;
            this.CD_ROM = CDROM;
            this.SPU = SPU;
            this.DMA = DMA;
            this.IO_PORTS = IO;
            this.MemoryControl = MemCtrl;       //useless ?
            this.RamSize = RamSize;             //useless ?
            this.CacheControl = CacheControl;   //useless ?
            this.Expansion1 = Ex1;
            this.Expansion2 = Ex2;
            this.Timer1 = Timer1;
            this.Timer2 = Timer2;
            this.MDEC = MDEC;
            this.GPU = GPU;
        }

        public UInt32 loadWord(UInt32 address) {
            uint physical_address = mask(address);
            //CPU.cycles++;
            if (RAM.range.contains(physical_address)) {
                return RAM.LoadWord(physical_address);

            }else if (BIOS.range.contains(physical_address)) {                
                return BIOS.loadWord(physical_address);                   
                
            }else if (IRQ_CONTROL.range.contains(physical_address)) {
                return IRQ_CONTROL.read(physical_address);

            }else if (DMA.range.contains(physical_address)) {
                return DMA.loadWord(physical_address);

            }else if (GPU.range.contains(physical_address)) {
                return GPU.loadWord(physical_address);

            }else if (TIMER0.contains(physical_address)) { 
                return 0;  

            }else if (Timer1.range.contains(physical_address)) {
                return Timer1.read(physical_address);  

            }else if (Timer2.range.contains(physical_address)) {
                return Timer2.read(physical_address);

            }else if (address == 0x1F801060) {                          //Memory Control 2
                return 0X00000B88;

            }else if (address >= 0x1F801014 && address < 0x1F801018) {  //SPU delay 
                return 0x200931E1;
            }
            else if (Scratchpad.range.contains(physical_address)) {
                return Scratchpad.loadWord(physical_address);

            }else if (IO_PORTS.range.contains(physical_address)) {
                return IO_PORTS.loadWord(physical_address);

            } else if (MemoryControl.range.contains(physical_address)) {
                return 0x00070777;

            } else if (MDEC.range.contains(physical_address)) {
                return 0x808080; // MDEC.read(physical_address);

            }else {
                throw new Exception("cannot find address: " + address.ToString("X") + " in memory map");
            }
        }

        public void storeWord(UInt32 address,UInt32 value) {
            uint physical_address = mask(address);
            //CPU.cycles++;

            if (MemoryControl.range.contains(physical_address)) {
                MemoryControl.storeWord(physical_address, value);

            }else if (RamSize.range.contains(physical_address)) {        
                RamSize.storeWord(physical_address, value);                         

            }else if (RAM.range.contains(physical_address)) {             
               RAM.StoreWord(physical_address, value);

            }else if (CacheControl.range.contains(physical_address)) {
                //Console.WriteLine("Unhandled write to CACHECONTROL register, address: " + address.ToString("X"));

            }else if (IRQ_CONTROL.range.contains(physical_address)) {
                IRQ_CONTROL.write(physical_address, (ushort)value);   

            }else if (DMA.range.contains(physical_address)) {
                DMA.storeWord(physical_address, value);
                DMAChannel activeCH = DMA.is_active(physical_address);  //Handle active DMA transfer (if any)
                if (activeCH != null) {
                    if(activeCH.get_sync() == activeCH.Sync["LinkedList"]) {
                        dma_LinkedList_transfer(ref activeCH);
                    }
                    else {
                        dma_transfer(ref activeCH);
                    }
                }
            }else if (GPU.range.contains(physical_address)) {
                GPU.storeWord(physical_address, value);

            }else if (TIMER0.contains(physical_address)) {   
                //Console.WriteLine("Unhandled write to TIMER0 register at address: " + address.ToString("X"));
                
            }else if (Timer1.range.contains(physical_address)) {
                Timer1.write(physical_address, value);

            }else if (Timer2.range.contains(physical_address)) {
                Timer2.write(physical_address, value);    

            }else if (Scratchpad.range.contains(physical_address)) {  
                Scratchpad.storeWord(physical_address, value);

            }else if (MDEC.range.contains(physical_address)) {
                //MDEC.write(physical_address, value);

            } else {
                throw new Exception("unknown address: " + address.ToString("X") + " - " + " Physical: " + physical_address.ToString("x"));
            }
        }

        internal UInt16 loadHalf(UInt32 address) {
            uint physical_address = mask(address);
            //CPU.cycles++;

            if (RAM.range.contains(physical_address)) {
                return RAM.LoadHalf(physical_address);

            }else if (BIOS.range.contains(physical_address)) {
                return BIOS.loadHalf(physical_address);

            }else if (SPU.range.contains(physical_address)) {
                return SPU.loadHalf(physical_address);

            }else if (IRQ_CONTROL.range.contains(physical_address)) {
                return (ushort)IRQ_CONTROL.read(physical_address);

            } else if (DMA.range.contains(physical_address)) {  //DMA only 32-bits?
                return (ushort)DMA.loadWord(physical_address);

            } else if (TIMER0.contains(physical_address)) {
                //Console.WriteLine("Unhandled read to TIMER0 register at address: " + address.ToString("X"));
                return 0;

            }else if (Timer1.range.contains(physical_address)) {
                return (ushort)Timer1.read(physical_address);

            }else if (Timer2.range.contains(physical_address)) {
                return (ushort)Timer2.read(physical_address);

            }else if (IO_PORTS.range.contains(physical_address)) {
                return IO_PORTS.loadHalf(physical_address);

            }else if (Scratchpad.range.contains(physical_address)) {
                return Scratchpad.loadHalf(physical_address);

            }else if (Expansion1.range.contains(physical_address)) {    //Ignore expansion 1
                return 0xFF;

            } else if (address >= 0x1F801014 && address < 0x1F801018) {  //SPU delay 
                return 0x31E1;
            } else {
                throw new Exception("Unhandled loadHalf at address : " + address.ToString("x") + "\n" +
                               "Physical address: " + physical_address.ToString("x"));
            }
        }

        public void storeHalf(UInt32 address, UInt16 value) {
            uint physical_address = mask(address);
            //CPU.cycles++;

            if (RAM.range.contains(physical_address)) {
                RAM.StoreHalf(physical_address, value);

            }else if (SPU.range.contains(physical_address)) {
                SPU.storeHalf(physical_address, value);

            }else if (TIMER0.contains(physical_address)) {
                //Console.WriteLine("Unhandled write to TIMER0 register at address: " + address.ToString("X"));

            }else if (Timer1.range.contains(physical_address)) {
                Timer1.write(physical_address, value);

            }else if (Timer2.range.contains(physical_address)) {
                Timer2.write(physical_address, value);

            }else if (IRQ_CONTROL.range.contains(physical_address)) {
                IRQ_CONTROL.write(physical_address, value);

            } else if (DMA.range.contains(physical_address)) {  
                DMA.storeWord(physical_address, value);         //DMA only 32-bits?
                DMAChannel activeCH = DMA.is_active(physical_address);  //Handle active DMA transfer (if any)
                if (activeCH != null) {
                    if (activeCH.get_sync() == activeCH.Sync["LinkedList"]) {
                        dma_LinkedList_transfer(ref activeCH);
                    } else {
                        dma_transfer(ref activeCH);
                    }
                }

            } else if (IO_PORTS.range.contains(physical_address)) {
                IO_PORTS.storeHalf(physical_address, value);

            }else if (Scratchpad.range.contains(physical_address)) {
                Scratchpad.storeHalf(physical_address, value);
            
            } else if (address == 0x1f802082) {
                Console.WriteLine("Redux-Expansion Exit code: " + value.ToString("x"));
            } else if (address >= 0x1F801014 && address < 0x1F801018) {  //SPU delay 

            } else {
                throw new Exception("Unhandled store16 at address : " + address.ToString("x"));
            }
        }
        internal byte loadByte(UInt32 address) {
            uint physical_address = mask(address);
            //CPU.cycles++;
  
            if (RAM.range.contains(physical_address)) {
                return RAM.LoadByte(physical_address);

            }else if (BIOS.range.contains(physical_address)) {
                return BIOS.loadByte(physical_address);

            }else if (Expansion1.range.contains(physical_address)) {
                return 0xff;

            }else if (CD_ROM.range.contains(physical_address)) {
                return CD_ROM.LoadByte(physical_address);

            }else if (IO_PORTS.range.contains(physical_address)) {
                return IO_PORTS.loadByte(physical_address);

            }else if (Scratchpad.range.contains(physical_address)) {
                return Scratchpad.loadByte(physical_address);

            }else if (address == 0x1f8010f6) {
                //Weird DMA register
                //throw new Exception();

                return 0;
            }else {
                throw new Exception("Unhandled load8 at address : " + address.ToString("x"));
            } 
        }

        public void storeByte(UInt32 address, byte value) {
            uint physical_address = mask(address);
            //CPU.cycles++;

            if (RAM.range.contains(physical_address)) {
                RAM.StoreByte(physical_address, value);

            }else if (CD_ROM.range.contains(physical_address)) {
                CD_ROM.StoreByte(physical_address, value);

            }else if (IO_PORTS.range.contains(physical_address)) {
                IO_PORTS.storeHalf(physical_address, value);

            }else if (Scratchpad.range.contains(physical_address)) {
                Scratchpad.storeByte(physical_address, value);

            }else if (Expansion2.range.contains(physical_address)) {
                //Console.WriteLine("Unhandled write to EXPANTION2 at address : " + address.ToString("x"));

            }else if (address == 0x1f8010f6) {
                //Weird DMA register
                //throw new Exception();
            }else {
                throw new Exception("Unhandled store8 at address : " + address.ToString("x"));
            }
            //address 0x1f8010f6 ?? 
        }

        public UInt32 mask(UInt32 address) { 
            UInt32 index = address >> 29;
            UInt32 physical_address = address & RegionMask[index];
            return physical_address;
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
                UInt32 header = RAM.LoadWord(address);
                UInt32 num_of_words = header >> 24;

                while (num_of_words > 0) {
                    address = (address + 4) & 0x1ffffc;

                    UInt32 command = RAM.LoadWord(address);
                    GPU.write_GP0(command);
                    num_of_words -= 1;

                }
                if ((header & 0x800000) != 0) {
                    break;
                }
                address = header & 0x1ffffc;
            }
            ch.done();
            DMA.ch_irq_flags |= (1 << 2);
            if (DMA.IRQRequest() == 1) {
                IRQ_CONTROL.IRQsignal(3);
            };
        }

        private void dma_transfer(ref DMAChannel activeCH) {
            DMAChannel ch = activeCH;
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
            bool isSPUIRQ = false;

            while (transfer_size > 0) {
                UInt32 current_address = base_address & 0x1ffffc;

                if (ch.get_direction() == ch.Direction["FromRam"]) {

                    UInt32 data = RAM.LoadWord(current_address);

                    switch (ch.get_portnum()) {
                        case 0: //MDEC.CommandAndParameters(data);
                            break;   //MDECin  (RAM to MDEC)

                        case 2: GPU.write_GP0(data); break;

                        case 4:
                            SPU.DMAtoSPU(data);
                            isSPUIRQ = true;
                            /*if(transfer_size - 1 <= 0) {
                                SPU.DMA_Read_Request = 0;
                            }*/

                            break;

                        default: throw new Exception("Unhandled DMA destination port: " + ch.get_portnum());

                    }
                }

                else {
                    switch (ch.get_portnum()) {
                        case 1:
                            //Console.WriteLine("[BUS] MDEC to RAM DMA");
                            RAM.StoreWord(current_address, 0x000000FF); 
                            break;

                        case 2:  //GPU
                            UInt16 pixel0 = GPU.gpuTransfer.data[GPU.gpuTransfer.dataPtr++];
                            UInt16 pixel1 = GPU.gpuTransfer.data[GPU.gpuTransfer.dataPtr++];
                            UInt32 merged_Pixels = (uint)(pixel0 | (pixel1 << 16));
                            if (GPU.gpuTransfer.dataEnd) {
                                GPU.gpuTransfer.transferType = GPU.TransferType.Off;
                            }
                            RAM.StoreWord(current_address, merged_Pixels);
                            break;

                        case 3: RAM.StoreWord(current_address, CD_ROM.DataController.ReadWord()); break;  //CD-ROM

                        case 4:  //SPU
                            isSPUIRQ = true;
                            RAM.StoreWord(current_address, SPU.SPUtoDMA());
                            break;

                        case 6:
                            switch (transfer_size) {
                                case 1: RAM.StoreWord(current_address, 0xffffff); break;
                                default: RAM.StoreWord(current_address, (base_address - 4) & 0x1fffff); break;
                            }
                            break;

                        default: throw new Exception("Unhandled DMA copy port: " + ch.get_portnum());

                    }
                }

                base_address = (UInt32)(base_address + step);
                transfer_size -= 1;
            }
            //if (isSPUIRQ) { SPU.SPU_IRQ(); }    //Causes problems with MGS

            ch.done();  

            //DMA IRQ 
            DMA.ch_irq_flags = (byte)(DMA.ch_irq_flags | (1 << (int)ch.get_portnum()));
            if (DMA.IRQRequest() == 1) {
                IRQ_CONTROL.IRQsignal(3);   //Instant IRQ is causing problems
            };
        }

    }
}

