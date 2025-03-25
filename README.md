# PSXSharp

PSX# is a PlayStation 1 emulator written in C#.

The project is a personal project for the purpose of learning more about emulation development and computer architecture.

## What Has Been Implemented

- CPU (MIPS R3000)
  - Interpreter
  - MSIL Recompiler (Experimental)
  - x64 Recompiler (Experimental)
  - COP0 
  - GTE
- GPU
  - OpenGL Renderer
  - All drawing commands
  - All texture and shading modes
  - Dithering
  - 24 bit display mode 
  - All transfer commands
- SPU
  - SPU-ADPCM
  - SPU Reverb
- CDROM
  - Most commands
  - XA-ADPCM and CDDA support
  - Audio disks support
- MDEC
  - All modes (4/8/15/24) BPP
- Controller and Memory Cards
- Timers
- DMA

## What Are The Next Goals 
- Optimize CPU recompilers
- Implement CPU I-Cache
- Implement accurate instruction timing
- Fix GPU semi-transparency bugs
- Implement SPU noise generator 
- Implement the remaining CDROM Commands
- Implement CDROM Seek time
- Implement MDEC timing
- Implement DMA timing
- Implement a nice UI
- General code refactoring

The list goes on...




