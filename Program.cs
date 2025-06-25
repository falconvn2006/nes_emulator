using nes_emulator.src;

namespace nes_emulator
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Bus nes = new Bus();
            Dictionary<ushort, string> mapAsm = new Dictionary<ushort, string>();

            string program = "A2 0A 8E 00 00 A2 03 8E 01 00 AC 00 00 A9 00 18 6D 01 00 88 D0 FA 8D 02 00 EA EA EA";
            ushort nOffset = 0x8000;

            string[] hexBytes = program.Split(' ');
            foreach (string bytes in hexBytes)
            {
                nes.ram[nOffset++] = byte.Parse(bytes, System.Globalization.NumberStyles.HexNumber);
            }

            nes.ram[0xFFFC] = 0x00;
            nes.ram[0xFFFD] = 0x80;

            mapAsm = nes.cpu6502.Disassemble(0x0000, 0xFFFF);

            nes.cpu6502.Reset();

            bool run = true;

            while (run)
            {
                Console.Clear();
                Console.WriteLine("Press any key to continue the program");
                Console.WriteLine("Type Exit to stop the program");
                string cmd = Console.ReadLine();

                if (string.IsNullOrEmpty(cmd) || cmd == "Continue")
                {
                    do
                    {
                        nes.cpu6502.Clock();
                    }
                    while (!nes.cpu6502.Complete());
                }
                else if (cmd == "Exit")
                {
                    Console.WriteLine("End!");
                    run = false;
                }
                else if (cmd == "Reset")
                {
                    nes.cpu6502.Reset();
                }

                // RAM Info
                Console.WriteLine("RAM INFO:");
                PrintRam(0x0000, 16, 16, nes);
                Console.WriteLine();
                PrintRam(0x8000, 16, 16, nes);

                Console.WriteLine();

                // CPU Info
                Console.WriteLine("CPU INFO:");
                Console.WriteLine($"Program Counter: ${nes.cpu6502.pc.ToString("X4")}");
                Console.WriteLine($"A: ${nes.cpu6502.a.ToString("X2")} [{nes.cpu6502.a}]");
                Console.WriteLine($"X: ${nes.cpu6502.x.ToString("X2")} [{nes.cpu6502.x}]");
                Console.WriteLine($"Y: ${nes.cpu6502.y.ToString("X2")} [{nes.cpu6502.y}]");
                Console.WriteLine($"Stack P: ${nes.cpu6502.stkp.ToString("X4")}");

                Console.WriteLine();

                // Instructions Info
                //Console.WriteLine("INSTRUCTIONS INFO:");
                //PrintInstructions(72, nes, mapAsm);

                Console.ReadLine();
            }

            Console.WriteLine("Hello, World!");
        }

        static void PrintRam(ushort nAddr, int nRows, int nColumns, Bus nes)
        {
            for (int row = 0; row < nRows; row++)
            {
                string sOffset = "$" + nAddr.ToString("X4") + ":";
                for (int col = 0; col < nColumns; col++)
                {
                    sOffset += " " + nes.Read(nAddr, true).ToString("X2");
                    nAddr += 1;
                }

                Console.WriteLine(sOffset);
            }
        }

        static void PrintInstructions(int nLines, Bus nes, Dictionary<ushort, string> mapAsm)
        {
            if (!mapAsm.TryGetValue(nes.cpu6502.pc, out var currentLine))
                return;

            var it_a = mapAsm.Keys.OrderBy(k => k).ToList();
            int index = it_a.IndexOf(nes.cpu6502.pc);

            int nLineY = (nLines >> 1) * 10;

            // Indicate the execution line
            Console.WriteLine(currentLine + " [EXECUTING] ");

            // Draw next instructions
            int yOffset = nLineY;
            int forward = index;
            while (yOffset < nLines * 10 && ++forward < it_a.Count)
            {
                yOffset += 10;
                Console.WriteLine(mapAsm[it_a[forward]]);
            }

            // Draw previous instructions
            //yOffset = nLineY;
            //int backward = index;
            //while (--backward >= 0)
            //{
            //	yOffset -= 10;
            //	Console.WriteLine(mapAsm[it_a[backward]]);
            //}
        }
    }
}
