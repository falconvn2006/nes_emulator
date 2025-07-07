using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;

namespace nes_emulator.src.Mappers
{
	public class Mapper001 : Mapper
	{

		private byte CHRBankSelect4LO = 0x00;
		private byte CHRBankSelect4HI = 0x00;
		private byte CHRBankSelect8 = 0x00;

		private byte PRGBankSelect16LO = 0x00;
		private byte PRGBankSelect16HI = 0x00;
		private byte PRGBankSelect32 = 0x00;

		private byte loadRegister = 0x00;
		private byte loadRegisterCount = 0x00;
		private byte controlRegister = 0x00;

		private Cartridge.MIRROR mirrorMode = Cartridge.MIRROR.HORIZONTAL;

		private List<byte> RAMStaticList;

		public Mapper001(byte prgBanks, byte chrBanks) : base(prgBanks, chrBanks)
		{
			RAMStaticList = new List<byte>(32 * 1024);
			RAMStaticList.AddRange(Enumerable.Repeat((byte)0, 32 * 1024));
		}

		public override bool CPUMapRead(ushort addr, ref uint mapped_addr, ref byte data)
		{
			if(addr >= 0x6000 && addr <= 0x7FFF)
			{
				// Read is from static RAM on the cartridge
				mapped_addr = 0xFFFFFFFF;

				// Read data from RAM
				data = RAMStaticList[addr & 0x1FFF];

				// Signal mapper has handled the request
				return true;
			}
			
			if(addr >= 0x8000)
			{
				if((controlRegister & 0b01000) != 0)
				{
					// 16K mode
					if(addr >= 0x8000 && addr <= 0xBFFF)
					{
						mapped_addr = (uint)(PRGBankSelect16LO * 0x4000 + (addr & 0x3FFF));
						return true;
					}

					if(addr >= 0xC000 && addr <= 0xFFFF)
					{
						mapped_addr = (uint)(PRGBankSelect16HI * 0x4000 + (addr & 0x3FFF));
						return true;
					}
				}
				else
				{
					// 32K mode
					mapped_addr = (uint)(PRGBankSelect32 * 0x8000 + (addr & 0x7FFF));
					return true;
				}
			}

			return false;
		}

		public override bool CPUMapWrite(ushort addr, ref uint mapped_addr, byte data)
		{
			if(addr >= 0x6000 & addr <= 0x7FFF)
			{
				// Write to static RAM on the cartridge
				mapped_addr = 0xFFFFFFFF;

				// Write data to the RAM
				RAMStaticList[addr & 0x1FFF] = data;

				// Signal mapper has handled the request
				return true;
			}

			if(addr >= 0x8000)
			{
				if((data & 0x80) != 0)
				{
					// MSB is set, so reset serial loading
					loadRegister = 0x00;
					loadRegisterCount = 0;
					controlRegister = (byte)(controlRegister | 0x0C);
				}
				else
				{
					// Load data in serially into the load register
					// It arrives LSB first, so implant this at
					// bit 5. After 5 writes, the register is ready
					loadRegister >>= 1;
					loadRegister |= (byte)((data & 0x01) << 4);
					loadRegisterCount++;

					if(loadRegisterCount == 5)
					{
						// Get mapper target register, by examining
						// bits 13 & 14 of the address
						byte targetRegister = (byte)((addr >> 13) & 0x03);

						if(targetRegister == 0) // 0x8000 - 0x9FFF
						{
							// Set control register
							controlRegister = (byte)(loadRegister & 0x1F);

							switch(controlRegister & 0x03)
							{
								case 0: mirrorMode = Cartridge.MIRROR.ONESCREEN_LO; break;
								case 1: mirrorMode = Cartridge.MIRROR.ONESCREEN_HI; break;
								case 2: mirrorMode = Cartridge.MIRROR.VERTICAL;		break;
								case 3: mirrorMode = Cartridge.MIRROR.HORIZONTAL;	break;
							}
						}
						else if(targetRegister == 1) // 0xA000 - 0xBFFF
						{
							// Set CHR Bank lo
							if ((controlRegister & 0b10000) != 0)
								// 4K CHR Bank at PPU 0x0000
								CHRBankSelect4LO = (byte)(loadRegister & 0x1F);
							else
								// 8K CHR Bank at PPU 0x0000
								CHRBankSelect8 = (byte)(loadRegister & 0x1E);
						}
						else if(targetRegister == 2) // 0xC000 - 0xDFFF
						{
							// Set CHR Bank hi
							if ((controlRegister & 0b10000) != 0)
								// 4K CHR Bank at PPU 0x1000
								CHRBankSelect4HI = (byte)(loadRegister & 0x1F);
						}
						else if(targetRegister == 3) // 0xE000 - 0xFFFF
						{
							// Configure PRG Banks
							byte prgMode = (byte)((controlRegister >> 2) & 0x03);

							if (prgMode == 0 || prgMode == 1)
								// Set 32K PRG Bank at CPU 0x8000
								PRGBankSelect32 = (byte)((loadRegister & 0x0E) >> 1);
							else if (prgMode == 2)
							{
								// Fixed 16KB PRG Bank at CPU 0x8000 to First bank
								PRGBankSelect16LO = 0;
								// Set 16KB PRG Bank at CPU 0xC000
								PRGBankSelect16HI = (byte)(loadRegister & 0x0F);
							}
							else if(prgMode == 3)
							{
								// Set 16KB PRG Bank at CPU 0x8000
								PRGBankSelect16LO = (byte)(loadRegister & 0x0F);
								// Fixed 16KB PRG Bank at CPU 0xC000 to Last bank
								PRGBankSelect16HI = (byte)(nPRGBanks - 1);
							}
						}

						// 5 bits were written, and decoded, so
						// reset load register
						loadRegister = 0x00;
						loadRegisterCount = 0;
					}
				}
			}

			// Mapper has handled write, but does not update ROM
			return false;
		}

		public override bool PPUMapRead(ushort addr, ref uint mapped_addr)
		{
			if(addr < 0x2000)
			{
				if(nCHRBanks == 0)
				{
					mapped_addr = addr;
					return true;
				}
				else
				{
					if ((controlRegister & 0b10000) != 0)
					{
						// 4K CHR Bank mode
						if (addr >= 0x0000 && addr <= 0x0FFF)
						{
							mapped_addr = (uint)(CHRBankSelect4LO * 0x1000 + (addr & 0x0FFF));
							return true;
						}

						if (addr >= 0x1000 && addr <= 0x1FFF)
						{
							mapped_addr = (uint)(CHRBankSelect4HI * 0x1000 + (addr & 0x0FFF));
							return true;
						}
					}
					else
					{
						// 8K CHR Bank mode
						mapped_addr = (uint)(CHRBankSelect8 * 0x2000 + (addr & 0x1FFF));
						return true;
					}
				}
			}

			return false;
		}

		public override bool PPUMapWrite(ushort addr, ref uint mapped_addr)
		{
			if (addr < 0x2000)
			{
				if(nCHRBanks == 0)
				{
					mapped_addr = addr;
					return true;
				}

				return true;
			}
			else
				return false;
		}

		public override void Reset()
		{
			controlRegister = 0x1;
			loadRegister = 0x00;
			loadRegisterCount = 0x00;

			CHRBankSelect8 = 0x00;
			CHRBankSelect4LO = 0x00;
			CHRBankSelect4HI = 0x00;

			PRGBankSelect32 = 0x00;
			PRGBankSelect16LO = 0x00;
			PRGBankSelect16HI = (byte)(nPRGBanks - 1);
		}

		public override Cartridge.MIRROR GetMirror()
		{
			return mirrorMode;
		}
	}
}
