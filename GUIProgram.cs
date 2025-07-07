using System;
using System.Collections.Generic;
using System.Linq;

using nes_emulator.src;
using OpenTK.Graphics.OpenGL;

namespace nes_emulator
{
	public class GUIProgram
	{
		static unsafe void Main(string[] args)
		{
			using (Emulator emulator = new Emulator(1600, 1200, "NES Emulator"))
			{
				emulator.Run();
			}

			Console.WriteLine("DONE!");
		}
	}
}
