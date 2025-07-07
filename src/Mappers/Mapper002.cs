using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace nes_emulator.src.Mappers
{
	public class Mapper002 : Mapper
	{
		private byte prgBankSelectLO = 0x00;
		private byte prgBankSelectHI = 0x00;

		public Mapper002(byte prgBanks, byte chrBanks) : base(prgBanks, chrBanks) 
		{
			
		}

		public override bool CPUMapRead(ushort addr, ref uint mapped_addr, ref byte data)
		{
			if (addr >= 0x8000 && addr <= 0xBFFF)
			{
				mapped_addr = (uint)(prgBankSelectLO * 0x4000 + (addr & 0x3FFF));
				return true;
			}

			if (addr >= 0xC000 && addr <= 0xFFFF)
			{
				mapped_addr = (uint)(prgBankSelectHI * 0x4000 + (addr & 0x3FFF));
				return true;
			}

			return false;
		}

		public override bool CPUMapWrite(ushort addr, ref uint mapped_addr, byte data)
		{
			if(addr >= 0x8000 && addr <= 0xFFFF)
			{
				prgBankSelectLO = (byte)(data & 0x0F);
			}

			// Mapper has handled write, but does not update ROMs
			return false;
		}

		public override bool PPUMapRead(ushort addr, ref uint mapped_addr)
		{
			if (addr < 0x2000)
			{
				mapped_addr = addr;
				return true;
			}

			return false;
		}

		public override bool PPUMapWrite(ushort addr, ref uint mapped_addr)
		{
			if (addr < 0x2000)
			{
				if (nCHRBanks == 0)
				{
					// Treated as ram
					mapped_addr = addr;
					return true;
				}
			}

			return false;
		}

		public override void Reset()
		{
			prgBankSelectLO = 0;
			prgBankSelectHI = (byte)(nPRGBanks - 1);
		}
	}
}
