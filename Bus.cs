using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace nes_emulator
{
	public class Bus
	{
		public CPU cpu6502;
		public byte[] ram = new byte[64 * 1024];

		public Bus()
		{
			for (int i = 0; i < ram.Length; i++)
			{
				ram[i] = 0x00;
			}

			cpu6502 = new CPU();

			cpu6502.ConnectBus(this);
		}

		public void Write(ushort addr, byte data)
		{
			if(addr >= 0x0000 && addr <= 0xFFFF)
				ram[addr] = data;
		}

		public ushort Read(ushort addr, bool bReadOnly = false)
		{
			if (addr >= 0x0000 && addr <= 0xFFFF)
				return ram[addr];

			return 0x00;
		}
	}
}
