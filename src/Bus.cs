using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace nes_emulator.src
{
    public class Bus
    {
        public CPU cpu6502;
        public PPU ppu2C02;
        public APU apu2A03;
        public Cartridge cartridge;
        public byte[] cpuRam = new byte[2048];
        public byte[] controller = new byte[2];

        private uint nSystemClockCounter = 0;

        private byte[] controllerState = new byte[2];

        // DMA (Direct Memory Access) stuff
        private byte dmaPage = 0x00;
        private byte dmaAddr = 0x00;
        private byte dmaData = 0x00;

        private bool dmaTransfer = false;
        private bool dmaDummy = true;

        // Audio stuff
        public double audioSample = 0.0;
        private double audioTimePerSystemSample = 0.0f;
        private double audioTimePerNESClock = 0.0;
        private double audioTime = 0.0;
        private double audioGlobalTime = 0.0;

        public Bus()
        {
            //for (int i = 0; i < cpuRam.Length; i++)
            //{
            //    cpuRam[i] = 0x00;
            //}

            cpu6502 = new CPU();
            ppu2C02 = new PPU();
            apu2A03 = new APU();

            cpu6502.ConnectBus(this);
        }

		#region Bus read and write
		public void CPUWrite(ushort addr, byte data)
        {
            if (cartridge.CPUWrite(addr, data)) { }
            else if (addr >= 0x0000 && addr <= 0x1FFF)
                cpuRam[addr & 0x07FF] = data;
            else if (addr >= 0x2000 && addr <= 0x3FFF)
                ppu2C02.CPUWrite((ushort)(addr & 0x0007), data);
            else if ((addr >= 0x4000 && addr <= 0x4013) || addr == 0x4015 || addr == 0x4017)
                apu2A03.CPUWrite(addr, data);
            else if (addr == 0x4014)
            {
                dmaPage = data;
                dmaAddr = 0x00;
                dmaTransfer = true;
            }
            else if (addr >= 0x4016 && addr <= 0x4017)
                // "Lock In" controller state at this time
                controllerState[addr & 0x0001] = controller[addr & 0x0001];
        }

        public byte CPURead(ushort addr, bool bReadOnly)
        {
            byte data = 0x00;
            if (cartridge.CPURead(addr, ref data)) { }
            else if (addr >= 0x0000 && addr <= 0x1FFF)
                // System RAM Address Range, mirrored every 2048
                data = cpuRam[addr & 0x07FF];
            else if (addr >= 0x2000 && addr <= 0x3FFF)
                // PPU Address range, mirrored every 8
                data = ppu2C02.CPURead((ushort)(addr & 0x0007), bReadOnly);
            else if (addr == 0x4015)
                data = apu2A03.CPURead(addr);
            else if (addr >= 0x4016 && addr <= 0x4017)
            {
                // Read out the MSB of the controller status word
                data = (controllerState[addr & 0x0001] & 0x80) > 0 ? (byte)1 : (byte)0;
                controllerState[addr & 0x0001] <<= 1;
            }

			return data;
        }
		#endregion

		#region System Interface

        public void InsertCartridge(ref Cartridge _cartridge)
        {
            this.cartridge = _cartridge;
            ppu2C02.ConnectCartridge(_cartridge);
        }

        public void Reset()
        {
            cartridge.Reset();
            cpu6502.Reset();
            ppu2C02.Reset();
            nSystemClockCounter = 0;
            dmaPage = 0x00;
            dmaAddr = 0x00;
            dmaData = 0x00;
            dmaDummy = true;
            dmaTransfer = false;
        }

        public bool Clock()
        {
            if (!cartridge.ImageValid)
                return true;

            ppu2C02.Clock();

            apu2A03.Clock();

			// The CPU runs 3 times slower than the PPU so we only call its
			// clock() function every 3 times this function is called. We
			// have a global counter to keep track of this.
			if (nSystemClockCounter % 3 == 0)
            {
                if(dmaTransfer)
                {
                    if(dmaDummy)
                    {
                        if(nSystemClockCounter % 2 == 1)
                        {
                            dmaDummy = false;
                        }
                    }
                    else
                    {
                        if(nSystemClockCounter % 2 == 0)
                        {
                            dmaData = CPURead((ushort)(dmaPage << 8 | dmaAddr), false);
                        }
                        else
                        {
							ppu2C02.oam[dmaAddr] = dmaData;
							dmaAddr++;

							if (dmaAddr == 0x00)
							{
								dmaTransfer = false;
								dmaDummy = true;
							}
						}
                    }
                }
                else
                {
                    cpu6502.Clock();
                }
            }

            // Synchronizing with Audio
            bool audioSampleReady = false;
            audioTime += audioTimePerNESClock;
            if(audioTime >= audioTimePerSystemSample)
            {
                audioTime -= audioTimePerSystemSample;
                audioSample = apu2A03.GetOutputSample();
                audioSampleReady = true;
            }

            if(ppu2C02.nmi)
            {
                ppu2C02.nmi = false;
                cpu6502.NMI();
            }

            if(cartridge.GetMapper().IRQState())
            {
                cartridge.GetMapper().IRQClear();
                cpu6502.IRQ();
            }

            nSystemClockCounter++;

            return audioSampleReady;
        }

		#endregion

        public void SetSampleFrequency(uint sampleRate)
        {
            audioTimePerSystemSample = 1.0 / (double)sampleRate;
            audioTimePerNESClock = 1.0 / 5369318.0; // PPU Clock Frequency
        }
	}
}
