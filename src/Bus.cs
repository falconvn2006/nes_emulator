using System;
using System.Collections.Generic;
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
        public Cartridge cartridge;
        public byte[] cpuRam = new byte[2048];

        private uint nSystemClockCounter = 0;

        public Bus()
        {
            for (int i = 0; i < cpuRam.Length; i++)
            {
                cpuRam[i] = 0x00;
            }

            cpu6502 = new CPU();
            ppu2C02 = new PPU();

            cpu6502.ConnectBus(this);
        }

		#region Bus read and write
		public void CPUWrite(ushort addr, byte data)
        {
            if(cartridge.CPUWrite(addr, data)) { }
            else if (addr >= 0x0000 && addr <= 0x1FFF)
                cpuRam[addr & 0x07FF] = data;
            else if(addr >= 0x2000 && addr <= 0x3FFF)
                ppu2C02.CPUWrite((ushort)(addr & 0x0007), data);
        }

        public ushort CPURead(ushort addr, bool bReadOnly = false)
        {
            byte data = 0x00;
            if(cartridge.CPURead(addr, ref data)) { }
            if (addr >= 0x0000 && addr <= 0x1FFF)
                data = cpuRam[addr & 0x07FF];
			else if (addr >= 0x2000 && addr <= 0x3FFF)
				data = ppu2C02.CPURead((ushort)(addr & 0x0007));

			return data;
        }
		#endregion

		#region System Interface

        public void InsertCartridge(ref Cartridge _cartridge)
        {
            this.cartridge = _cartridge;
            ppu2C02.ConnectCartridge(ref _cartridge);
        }

        public void Reset()
        {
            // Call reset on the cartridge
            cpu6502.Reset();
            ppu2C02.Reset();
            nSystemClockCounter = 0;
        }

        public void Clock()
        {
            ppu2C02.Clock();
            if(nSystemClockCounter % 3 == 0)
            {
                cpu6502.Clock();
            }

            if(ppu2C02.nmi)
            {
                ppu2C02.nmi = false;
                cpu6502.NMI();
            }

            nSystemClockCounter++;
        }

		#endregion
	}
}
