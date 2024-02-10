using PSXEmulator.Peripherals;
using PSXEmulator.PS1_Emulator;
using System;
using System.Net.NetworkInformation;
using System.Windows;

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
        public CD_ROM CDROM;
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
        public Range Timer0 = new Range(0x1F801100, 0xF+1);        //Assumption 
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
            this.CDROM = CDROM;
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

        public UInt32 LoadWord(UInt32 address) {           
            uint physicalAddress = Mask(address);
            //CPU.cycles++;
            switch (physicalAddress) {
                case uint when RAM.range.Contains(physicalAddress): return RAM.LoadWord(physicalAddress);
                case uint when BIOS.range.Contains(physicalAddress): return BIOS.LoadWord(physicalAddress);
                case uint when IRQ_CONTROL.range.Contains(physicalAddress): return IRQ_CONTROL.Read(physicalAddress);
                case uint when DMA.range.Contains(physicalAddress): return DMA.Read(physicalAddress);
                case uint when GPU.range.Contains(physicalAddress): return GPU.LoadWord(physicalAddress);
                case uint when Timer0.Contains(physicalAddress): return 0x0;    //TODO
                case uint when Timer1.range.Contains(physicalAddress): return Timer1.Read(physicalAddress);
                case uint when Timer2.range.Contains(physicalAddress): return Timer2.Read(physicalAddress);
                case uint when Scratchpad.range.Contains(physicalAddress): return Scratchpad.LoadWord(physicalAddress);
                case uint when IO_PORTS.range.Contains(physicalAddress): return IO_PORTS.LoadWord(physicalAddress);
                case uint when MemoryControl.range.Contains(physicalAddress): return MemoryControl.Read(physicalAddress);
                case uint when MDEC.range.Contains(physicalAddress): return 0x0;
                case uint when RamSize.range.Contains(physicalAddress): return RamSize.LoadWord();
                default: throw new Exception("Unhandled LoadWord from: " + address.ToString("X"));
            }
        }

        public void StoreWord(UInt32 address,UInt32 value) {
            uint physicalAddress = Mask(address);
            //CPU.cycles++;
            switch (physicalAddress) {
                case uint when RAM.range.Contains(physicalAddress): RAM.StoreWord(physicalAddress, value); break;
                case uint when RamSize.range.Contains(physicalAddress): RamSize.StoreWord(value); break;
                case uint when MemoryControl.range.Contains(physicalAddress): MemoryControl.Write(physicalAddress, value); break;
                case uint when IRQ_CONTROL.range.Contains(physicalAddress): IRQ_CONTROL.Write(physicalAddress, (ushort)value); break; //Cast? could be wrong
                case uint when GPU.range.Contains(physicalAddress): GPU.StoreWord(physicalAddress, value); break;
                case uint when CacheControl.range.Contains(physicalAddress): break; //...?
                case uint when Timer0.Contains(physicalAddress): break;     //TODO
                case uint when Timer1.range.Contains(physicalAddress): Timer1.Write(physicalAddress, value); break;
                case uint when Timer2.range.Contains(physicalAddress): Timer2.Write(physicalAddress, value); break;
                case uint when Scratchpad.range.Contains(physicalAddress): Scratchpad.StoreWord(physicalAddress, value); break;
                case uint when MDEC.range.Contains(physicalAddress): break;
                case uint when DMA.range.Contains(physicalAddress):
                    DMA.Write(physicalAddress, value);
                    DMAChannel activeCH = DMA.is_active(physicalAddress);  //Handle active DMA transfer (if any)
                    if (activeCH != null) {
                        if (activeCH.GetSync() == ((uint)DMAChannel.Sync.LinkedList)) {
                            HandleDMALinkedList(ref activeCH);
                        } else {
                            HandleDMA(ref activeCH);
                        }
                    }
                    break;
                default: throw new Exception("Unhandled StoreWord to: " + address.ToString("X"));
            }
        }

        public UInt16 LoadHalf(UInt32 address) {
            uint physicalAddress = Mask(address);
            //CPU.cycles++;
            switch (physicalAddress) {
                case uint when RAM.range.Contains(physicalAddress): return RAM.LoadHalf(physicalAddress);
                case uint when BIOS.range.Contains(physicalAddress): return BIOS.LoadHalf(physicalAddress);
                case uint when SPU.range.Contains(physicalAddress): return SPU.LoadHalf(physicalAddress);
                case uint when IRQ_CONTROL.range.Contains(physicalAddress): return (ushort)IRQ_CONTROL.Read(physicalAddress);
                case uint when DMA.range.Contains(physicalAddress): return (ushort)DMA.Read(physicalAddress); //DMA only 32-bits?
                case uint when Timer0.Contains(physicalAddress): return 0x0;      //TODO
                case uint when Timer1.range.Contains(physicalAddress): return (ushort)Timer1.Read(physicalAddress);
                case uint when Timer2.range.Contains(physicalAddress): return (ushort)Timer2.Read(physicalAddress);
                case uint when IO_PORTS.range.Contains(physicalAddress): return IO_PORTS.LoadHalf(physicalAddress);
                case uint when Scratchpad.range.Contains(physicalAddress): return Scratchpad.LoadHalf(physicalAddress);
                case uint when MemoryControl.range.Contains(physicalAddress): return (ushort)MemoryControl.Read(physicalAddress);
                default: throw new Exception("Unhandled LoadHalf from: " + address.ToString("X"));

            }    
        }

        public void StoreHalf(UInt32 address, UInt16 value) {
            uint physicalAddress = Mask(address);
            //CPU.cycles++;
            switch (physicalAddress) {
                case uint when RAM.range.Contains(physicalAddress): RAM.StoreHalf(physicalAddress, value); break;
                case uint when SPU.range.Contains(physicalAddress): SPU.StoreHalf(physicalAddress, value); break;
                case uint when Timer0.Contains(physicalAddress): break;     //TODO
                case uint when Timer1.range.Contains(physicalAddress): Timer1.Write(physicalAddress, value); break;
                case uint when Timer2.range.Contains(physicalAddress): Timer2.Write(physicalAddress, value); break;
                case uint when IRQ_CONTROL.range.Contains(physicalAddress): IRQ_CONTROL.Write(physicalAddress, value); break;
                case uint when IO_PORTS.range.Contains(physicalAddress): IO_PORTS.StoreHalf(physicalAddress, value); break;
                case uint when Scratchpad.range.Contains(physicalAddress): Scratchpad.StoreHalf(physicalAddress, value); break;
                case uint when MemoryControl.range.Contains(physicalAddress): MemoryControl.Write(physicalAddress, value); break;
                case uint when DMA.range.Contains(physicalAddress):
                    DMA.Write(physicalAddress, value);
                    DMAChannel activeCH = DMA.is_active(physicalAddress);  //Handle active DMA transfer (if any)
                    if (activeCH != null) {
                        if (activeCH.GetSync() == ((uint)DMAChannel.Sync.LinkedList)) {
                            HandleDMALinkedList(ref activeCH);
                        } else {
                            HandleDMA(ref activeCH);
                        }
                    }
                    break;
                case 0x1f802082: Console.WriteLine("Redux-Expansion Exit code: " + value.ToString("x")); break;
                default: throw new Exception("Unhandled StoreHalf from: " + address.ToString("X"));
            }
        }

        public byte LoadByte(UInt32 address) {
            uint physicalAddress = Mask(address);
            //CPU.cycles++;
            switch (physicalAddress) {
                case uint when RAM.range.Contains(physicalAddress): return RAM.LoadByte(physicalAddress);
                case uint when BIOS.range.Contains(physicalAddress): return BIOS.LoadByte(physicalAddress);
                case uint when CDROM.range.Contains(physicalAddress): return CDROM.LoadByte(physicalAddress);
                case uint when MemoryControl.range.Contains(physicalAddress): return (byte)MemoryControl.Read(physicalAddress);
                case uint when Scratchpad.range.Contains(physicalAddress): return Scratchpad.LoadByte(physicalAddress);
                case uint when IO_PORTS.range.Contains(physicalAddress): return IO_PORTS.LoadByte(physicalAddress);
                case uint when Expansion1.range.Contains(physicalAddress):   
                case uint when Expansion2.range.Contains(physicalAddress): return 0xFF;   //Ignore Expansions 1 and 2 
                default: throw new Exception("Unhandled LoadByte from: " + address.ToString("X"));
            }
        }

        public void StoreByte(uint address, byte value) {
            uint physicalAddress = Mask(address);
            //CPU.cycles++;
            switch (physicalAddress) {
                case uint when RAM.range.Contains(physicalAddress): RAM.StoreByte(physicalAddress, value); break;
                case uint when Scratchpad.range.Contains(physicalAddress): Scratchpad.StoreByte(physicalAddress, value); break;
                case uint when CDROM.range.Contains(physicalAddress): CDROM.StoreByte(physicalAddress, value); break;
                case uint when IO_PORTS.range.Contains(physicalAddress): IO_PORTS.StoreHalf(physicalAddress, value); break; //Should store byte..?
                case uint when MemoryControl.range.Contains(physicalAddress): MemoryControl.Write(physicalAddress, value); break;
                case uint when Expansion1.range.Contains(physicalAddress):
                case uint when Expansion2.range.Contains(physicalAddress): break;   //Ignore Expansions 1 and 2
                default: throw new Exception("Unhandled StoreByte to: " + address.ToString("X"));
            }           
        }

        public UInt32 Mask(UInt32 address) { 
            UInt32 index = address >> 29;
            UInt32 physical_address = address & RegionMask[index];
            return physical_address;
        }
        private void HandleDMALinkedList(ref DMAChannel activeCH) {     
            DMAChannel ch = activeCH;
         
            if (ch.get_direction() == ((uint)DMAChannel.Direction.ToRam)) {
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

        private void HandleDMA(ref DMAChannel activeCH) {
            DMAChannel ch = activeCH;
            int step;
            if (ch.get_step() == ((uint)DMAChannel.Step.Increment)) {
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

                if (ch.get_direction() == ((uint)DMAChannel.Direction.FromRam)) {

                    UInt32 data = RAM.LoadWord(current_address);

                    switch (ch.get_portnum()) {
                        case 0:
                            //Console.WriteLine("[BUS] MDEC DMA write - value: " + data.ToString("x"));
                            //MDEC.CommandAndParameters(data);
                            break;   //MDECin  (RAM to MDEC)
                        case 2: GPU.write_GP0(data); break;
                        case 4: SPU.DMAtoSPU(data);  break;
                        default: throw new Exception("Unhandled DMA destination port: " + ch.get_portnum());
                    }
                } else {
                    
                    switch (ch.get_portnum()) {
                        case 1:
                            //uint w = MDEC.ReadCurrentMacroblock();                           
                            RAM.StoreWord(current_address, 0xFFFFFFFF);
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

                        case 3: RAM.StoreWord(current_address, CDROM.DataController.ReadWord()); break;  //CD-ROM
                        case 4: RAM.StoreWord(current_address, SPU.SPUtoDMA()); break;                     //SPU

                        case 6:
                            switch (transfer_size) {
                                case 1: RAM.StoreWord(current_address, 0xffffff); break;
                                default: RAM.StoreWord(current_address, (base_address - 4) & 0x1fffff); break;
                            }
                            break;

                        default: throw new Exception("Unhandled DMA copy port: " + ch.get_portnum());

                    }
                }

                base_address = (uint)(base_address + step);
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

