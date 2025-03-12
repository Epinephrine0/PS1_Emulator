/*using System;
using System.IO;

namespace PSXEmulator.Core.Recompiler {
   public static class CacheManager {
        private const string FileName = "cache.bin";
        private const uint BIOS_START = 0x1FC00000;
        private const uint BIOS_SIZE = 512 * 1024;            //512 KB
        private const uint RAM_SIZE = 2 * 1024 * 1024;        //2 MB
        private const uint CACHE_SIZE = (BIOS_SIZE + RAM_SIZE) / 4;

        public static void SaveCache(CacheBlock[] cacheBlocks) {
            using (FileStream fs = new FileStream(FileName, FileMode.Create, FileAccess.Write))
            using (BinaryWriter writer = new BinaryWriter(fs)) {
                foreach (var block in cacheBlocks) {
                    if (block.IsCompiled) {
                        writer.Write(block.Address);
                        writer.Write(block.Total);
                        writer.Write(block.Instructions.Length);    //128
                        foreach (var instruction in block.Instructions) {
                            writer.Write(instruction);
                        }
                    } else {
                        writer.Write(0);
                        writer.Write(0);
                        writer.Write(128);
                        for (int i = 0; i < 128; i++) {
                            writer.Write(0);
                        }
                    }
                }
            }
        }

        public static CacheBlock[] LoadCache(CPURecompiler cpu) {
            CacheBlock[] cacheBlocks = new CacheBlock[CACHE_SIZE];

            if (!File.Exists(FileName)) {
                Console.WriteLine("[CacheManager] Could not find cache");

                //Create and return array of empty blocks
                for (int i = 0; i < CACHE_SIZE; i++) {
                    cacheBlocks[i] = new CacheBlock();
                }

                return cacheBlocks;

            } else {
                Console.WriteLine("[CacheManager] Found cache!");
            }

            //Load the cache and compile it from the file
            using (FileStream fs = new FileStream(FileName, FileMode.Open, FileAccess.Read))
            using (BinaryReader reader = new BinaryReader(fs)) {

                for (int i = 0; i < CACHE_SIZE; i++) {
                    uint address = reader.ReadUInt32();
                    uint total = reader.ReadUInt32();
                    int instructionsLength = reader.ReadInt32();
                    uint[] instructions = new uint[instructionsLength];

                    for (int j = 0; j < instructionsLength; j++) {
                        instructions[j] = reader.ReadUInt32();
                    }

          
                    cacheBlocks[i] = new CacheBlock();
                    cacheBlocks[i].Init(address);
          
                    //If this block contains instructions, compile them
                    if (total != 0) {
                        Instruction instruction = new Instruction();
                        cpu.CurrentBlock = cacheBlocks[i];
                        
                        for (int j = 0; j < total; j++) {
                            instruction.FullValue = instructions[j];
                            cpu.EmitInstruction(instruction);
                        }

                        MSIL_JIT.EmitRet(cpu.CurrentBlock);
                        cacheBlocks[i].Compile();
                    }
                }

                return cacheBlocks;
            }
        }
    }
}
*/