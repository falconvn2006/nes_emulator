using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace nes_emulator.src.Mappers
{
	public class Mapper003 : Mapper
	{

		private byte CHRBankSelect = 0x00;

		public Mapper003(byte prgBanks, byte chrBanks) : base(prgBanks, chrBanks)
		{

		}


		public override bool CPUMapRead(ushort addr, ref uint mapped_addr, ref byte data)
		{
			if(addr >= 0x8000 && addr <= 0xFFFF)
			{
				if (nPRGBanks == 1) // 16K ROM
					mapped_addr = (uint)(addr & 0x3FFF);
				if (nPRGBanks == 2) // 32K ROM
					mapped_addr = (uint)(addr & 0x7FFF);

				return true;
			}

			return false;
		}

		public override bool CPUMapWrite(ushort addr, ref uint mapped_addr, byte data)
		{
			if(addr >= 0x8000 && addr <= 0xFFFF)
			{
				CHRBankSelect = (byte)(data & 0x03);
				mapped_addr = addr;
			}

			// Mapper has handled write, but does not update ROM
			return false;
		}

		public override bool PPUMapRead(ushort addr, ref uint mapped_addr)
		{
			if(addr < 0x2000)
			{
				mapped_addr = (uint)(CHRBankSelect * 0x2000 + addr);
				return true;
			}

			return false;
		}

		public override bool PPUMapWrite(ushort addr, ref uint mapped_addr)
		{
			return false;
		}

		public override void Reset()
		{
			CHRBankSelect = 0;
		}
	}
}
