using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace nes_emulator.src.Mappers
{
	public class Mapper004 : Mapper
	{
		// Control variables
		private byte targetRegister = 0x00;
		private bool prgBankMode = false;
		private bool chrInversion = false;
		private Cartridge.MIRROR mirrorMode = Cartridge.MIRROR.HORIZONTAL;

		private uint[] register = new uint[8];
		private uint[] chrBank = new uint[8];
		private uint[] prgBank = new uint[4];

		private bool irqActive = false;
		private bool irqEnabled = false;
		private bool irqUpdate = false;
		private ushort irqCounter = 0x0000;
		private ushort irqReload = 0x0000;

		private List<byte> RAMStaticList;

		public Mapper004(byte prgBanks, byte chrBanks) : base(prgBanks, chrBanks)
		{
			RAMStaticList = new List<byte>(32 * 1024);
			RAMStaticList.AddRange(Enumerable.Repeat((byte)0, 32 * 1024));
		}

		public override bool CPUMapRead(ushort addr, ref uint mapped_addr, ref byte data)
		{
			if(addr >= 0x6000 && addr <= 0x7FFF)
			{
				// Read is from static ram on the cartridge
				mapped_addr = 0xFFFFFFFF;

				// Read data from ram
				data = RAMStaticList[addr & 0x1FFF];

				// Signal the mapper has handled request
				return true;
			}

			if(addr >= 0x8000 && addr <= 0x9FFF)
			{
				mapped_addr = (uint)(prgBank[0] + (addr & 0x1FFF));
				return true;
			}

			if (addr >= 0xA000 && addr <= 0xBFFF)
			{
				mapped_addr = (uint)(prgBank[1] + (addr & 0x1FFF));
				return true;
			}

			if (addr >= 0xC000 && addr <= 0xDFFF)
			{
				mapped_addr = (uint)(prgBank[2] + (addr & 0x1FFF));
				return true;
			}

			if (addr >= 0xE000 && addr <= 0xFFFF)
			{
				mapped_addr = (uint)(prgBank[3] + (addr & 0x1FFF));
				return true;
			}

			return false;
		}

		public override bool CPUMapWrite(ushort addr, ref uint mapped_addr, byte data)
		{
			if(addr >= 0x6000 && addr <= 0x7FFF)
			{
				// Write to the static ram on the cartridge
				mapped_addr = 0xFFFFFFFF;

				// Write data to the ram
				RAMStaticList[addr & 0x1FFF] = data;

				// Signal the mapper has handled the request
				return true;
			}

			if(addr >= 0x8000 && addr <= 0x9FFF)
			{
				// Bank select
				if((addr & 0x0001) == 0)
				{
					targetRegister = (byte)(data & 0x07);
					prgBankMode = (data & 0x40) != 0;
					chrInversion = (data & 0x80) != 0;
				}
				else
				{
					// Update target register
					register[targetRegister] = data;

					// Update pointer table
					if (chrInversion)
					{
						chrBank[0] = (uint)(register[2] * 0x0400);
						chrBank[1] = (uint)(register[3] * 0x0400);
						chrBank[2] = (uint)(register[4] * 0x0400);
						chrBank[3] = (uint)(register[5] * 0x0400);
						chrBank[4] = (uint)((register[0] & 0xFE) * 0x0400);
						chrBank[5] = (uint)(register[0] * 0x0400 + 0x0400);
						chrBank[6] = (uint)((register[1] & 0xFE) * 0x0400);
						chrBank[7] = (uint)(register[1] * 0x0400 + 0x0400);
					}
					else
					{
						chrBank[0] = (uint)((register[0] & 0xFE) * 0x0400);
						chrBank[1] = (uint)(register[0] * 0x0400 + 0x0400);
						chrBank[2] = (uint)((register[1] & 0xFE) * 0x0400);
						chrBank[3] = (uint)(register[1] * 0x0400 + 0x0400);
						chrBank[4] = (uint)(register[2] * 0x0400);
						chrBank[5] = (uint)(register[3] * 0x0400);
						chrBank[6] = (uint)(register[4] * 0x0400);
						chrBank[7] = (uint)(register[5] * 0x0400);
					}

					if(prgBankMode)
					{
						prgBank[2] = (uint)((register[6] & 0x3F) * 0x2000);
						prgBank[0] = (uint)((nPRGBanks * 2 - 2) * 0x2000);
					}
					else
					{
						prgBank[0] = (uint)((register[6] & 0x3F) * 0x2000);
						prgBank[2] = (uint)((nPRGBanks * 2 - 2) * 0x2000);
					}

					prgBank[1] = (uint)((register[7] & 0x3F) * 0x2000);
					prgBank[3] = (uint)((nPRGBanks * 2 - 1) * 0x2000);
				}

				return false;
			}

			if(addr >= 0xA000 && addr <= 0xBFFF)
			{
				if((data & 0x0001) == 0)
				{
					// Mirroring
					if ((data & 0x01) != 0)
						mirrorMode = Cartridge.MIRROR.HORIZONTAL;
					else
						mirrorMode = Cartridge.MIRROR.VERTICAL;
				}
				else
				{
					// PRG RAM Protect
				}

				return false;
			}

			if(addr >= 0xC000 && addr <= 0xDFFF)
			{
				if((addr & 0x0001) == 0)
				{
					irqReload = data;
				}
				else
				{
					irqCounter = 0x0000;
				}

				return false;
			}

			if(addr >= 0xE000 && addr <= 0xFFFF)
			{
				if((addr & 0x0001) == 0)
				{
					irqEnabled = false;
					irqActive = false;
				}
				else
				{
					irqEnabled = true;
				}

				return false;
			}

			return false;
		}

		public override bool PPUMapRead(ushort addr, ref uint mapped_addr)
		{
			if(addr >= 0x0000 && addr <= 0x03FF)
			{
				mapped_addr = (uint)(chrBank[0] + (addr & 0x03FF));
				return true;
			}

			if (addr >= 0x0400 && addr <= 0x07FF)
			{
				mapped_addr = (uint)(chrBank[1] + (addr & 0x03FF));
				return true;
			}

			if (addr >= 0x0800 && addr <= 0x0BFF)
			{
				mapped_addr = (uint)(chrBank[2] + (addr & 0x03FF));
				return true;
			}

			if (addr >= 0x0C00 && addr <= 0x0FFF)
			{
				mapped_addr = (uint)(chrBank[3] + (addr & 0x03FF));
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
			targetRegister = 0x00;
			prgBankMode = false;
			chrInversion = false;
			mirrorMode = Cartridge.MIRROR.HORIZONTAL;

			irqActive = false;
			irqEnabled = false;
			irqUpdate = false;
			irqCounter = 0x0000;
			irqReload = 0x0000;

			for (int i = 0; i < 4; i++) prgBank[i] = 0;
			for(int i = 0; i < 8; i++) { chrBank[i] = 0; register[i] = 0; }

			prgBank[0] = 0 * 0x2000;
			prgBank[1] = 1 * 0x2000;
			prgBank[2] = (uint)((nPRGBanks * 2 - 2) * 0x2000);
			prgBank[3] = (uint)((nPRGBanks * 2 - 1) * 0x2000);
		}

		public override Cartridge.MIRROR GetMirror()
		{
			return base.GetMirror();
		}

		public override bool IRQState()
		{
			return irqActive;
		}

		public override void IRQClear()
		{
			irqActive = false;
		}

		public override void Scanline()
		{
			if (irqCounter == 0)
			{
				irqCounter = irqReload;
			}
			else
				irqCounter--;

			if(irqCounter == 0 && irqEnabled)
				irqActive = true;
		}
	}
}
