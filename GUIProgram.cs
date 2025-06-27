using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.Threading.Tasks;
using OpenTK.Windowing.Common;

using nes_emulator.demo_programs;

namespace nes_emulator
{
	public class GUIProgram
	{
		static void Main(string[] args)
		{
			using (Emulator emulator = new Emulator(1600, 1200, "NES Emulator"))
			{
				emulator.Run();
			}

			Console.WriteLine("DONE!");
		}
	}
}
