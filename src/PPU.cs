using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace nes_emulator.src
{
	public class PPU
	{
		private Cartridge cartridge;
		private byte[,] tblName = new byte[2, 1024];
		private byte[]	tblPalette = new byte[32];
		//private byte[,] tblPattern = new byte[2, 4096]; 

		public PPU() { }

		public byte CPURead(ushort addr, bool readOnly = false)
		{
			byte data = 0x00;

			switch (addr)
			{
				case 0x0000: // Control
					break;
				case 0x0001: // Mask
					break;
				case 0x0002: // Status
					break;
				case 0x0003: // OAM Address
					break;
				case 0x0004: // OAM Data
					break;
				case 0x0005: // Scroll
					break;
				case 0x0006: // PPU Address
					break;
				case 0x0007: // PPU Data
					break;
			}

			return data;
		}

		public void CPUWrite(ushort addr, byte data)
		{
			switch (addr)
			{
				case 0x0000: // Control
					break;
				case 0x0001: // Mask
					break;
				case 0x0002: // Status
					break;
				case 0x0003: // OAM Address
					break;
				case 0x0004: // OAM Data
					break;
				case 0x0005: // Scroll
					break;
				case 0x0006: // PPU Address
					break;
				case 0x0007: // PPU Data
					break;
			}
		}

		public byte PPURead(ushort addr, bool readOnly = false)
		{
			byte data = 0x00;
			addr &= 0x3FFF;

			if(cartridge.PPURead(addr, readOnly))
			{

			}

			return data;
		}

		public void PPUWrite(ushort addr, byte data)
		{
			addr &= 0x3FFF;

			if(cartridge.PPUWrite(addr, data))
			{

			}
		}

		#region Interface

		public void ConnectCartridge(ref Cartridge _cartridge)
		{
			this.cartridge = _cartridge;
		}

		public void Clock()
		{

		}

		#endregion
	}
}
