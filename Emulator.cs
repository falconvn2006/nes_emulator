using ImGuiNET;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

using nes_emulator.src;
using System.Numerics;

namespace nes_emulator
{
	public class Emulator : GameWindow
	{
		Bus _nes;
		Dictionary<ushort, string> _mapAsm;

		ImGuiController controller;

		private bool p_RamOpen = false;

		public Emulator(int width, int height, string title) : base(GameWindowSettings.Default, new NativeWindowSettings() { ClientSize = (width, height), Title = title})
		{
			NESSetup();
		}


		private void NESSetup()
		{
			_nes = new Bus();
			_mapAsm = new Dictionary<ushort, string>();

			string program = "A2 0A 8E 00 00 A2 03 8E 01 00 AC 00 00 A9 00 18 6D 01 00 88 D0 FA 8D 02 00 EA EA EA";
			ushort nOffset = 0x8000;

			string[] hexBytes = program.Split(' ');
			foreach (string bytes in hexBytes)
			{
				_nes.ram[nOffset++] = byte.Parse(bytes, System.Globalization.NumberStyles.HexNumber);
			}

			_nes.ram[0xFFFC] = 0x00;
			_nes.ram[0xFFFD] = 0x80;

			_mapAsm = _nes.cpu6502.Disassemble(0x0000, 0xFFFF);

			_nes.cpu6502.Reset();
		}

		protected override void OnLoad()
		{
			base.OnLoad();

			controller = new ImGuiController(ClientSize.X, ClientSize.Y);
		}

		protected override void OnResize(ResizeEventArgs e)
		{
			base.OnResize(e);

			GL.Viewport(0, 0, ClientSize.X, ClientSize.Y);

			controller.WindowResized(ClientSize.X, ClientSize.Y);
		}

		protected override void OnUpdateFrame(FrameEventArgs args)
		{
			base.OnUpdateFrame(args);

			if(KeyboardState.IsKeyDown(Keys.Escape))
			{
				Close();
			}

			if(KeyboardState.IsKeyPressed(Keys.Space))
			{
				do
				{
					_nes.cpu6502.Clock();
				}
				while(!_nes.cpu6502.Complete());
			}

			if(KeyboardState.IsKeyPressed(Keys.R))
			{
				_nes.cpu6502.Reset();
			}

			if(KeyboardState.IsKeyPressed(Keys.I))
			{
				_nes.cpu6502.IRQ();
			}

			if(KeyboardState.IsKeyPressed(Keys.N))
			{
				_nes.cpu6502.NMI();
			}
		}

		protected override void OnRenderFrame(FrameEventArgs args)
		{
			base.OnRenderFrame(args);

			controller.Update(this, (float)args.Time);

			GL.ClearColor(0.2f, 0.3f, 0.3f, 1.0f);
			GL.Clear(ClearBufferMask.ColorBufferBit);

			ImGui.DockSpaceOverViewport();
			ImGui.StyleColorsDark();

			ImGuiDebug();

			controller.Render();

			ImGuiController.CheckGLError("End of frame");

			SwapBuffers();
		}

		private void ImGuiDebug()
		{
			List<string> sInstruction = new List<string>();

			ImGui.Begin("RAM Info");

			ushort nAddr = 0x0000;
			int nRows = 16;
			int nColumns = 16;

			for (int row = 0; row < nRows; row++)
			{
				string sOffset = "$" + nAddr.ToString("X4") + ":";
				for (int col = 0; col < nColumns; col++)
				{
					sOffset += " " + _nes.Read(nAddr, true).ToString("X2");
					nAddr += 1;
				}

				ImGui.Text(sOffset);
			}

			ImGui.Spacing();
			nAddr = 0x8000;

			for (int row = 0; row < nRows; row++)
			{
				string sOffset = "$" + nAddr.ToString("X4") + ":";
				for (int col = 0; col < nColumns; col++)
				{
					sOffset += " " + _nes.Read(nAddr, true).ToString("X2");
					nAddr += 1;
				}

				ImGui.Text(sOffset);
			}

			ImGui.End();

			ImGui.Begin("CPU Info");

			ImGui.Text("Status:");
			// CPU Statuses
			ImGui.TextColored(Convert.ToBoolean(_nes.cpu6502.status & (byte)CPU.FLAGS6502.N) ? new Vector4(0.0f, 1.0f, 0.68f, 1) : new Vector4(1.0f, 0.0f, 0.0f, 1), "N");
			ImGui.TextColored(Convert.ToBoolean(_nes.cpu6502.status & (byte)CPU.FLAGS6502.V) ? new Vector4(0.0f, 1.0f, 0.68f, 1) : new Vector4(1.0f, 0.0f, 0.0f, 1), "V");
			ImGui.TextColored(Convert.ToBoolean(_nes.cpu6502.status & (byte)CPU.FLAGS6502.B) ? new Vector4(0.0f, 1.0f, 0.68f, 1) : new Vector4(1.0f, 0.0f, 0.0f, 1), "B");
			ImGui.TextColored(Convert.ToBoolean(_nes.cpu6502.status & (byte)CPU.FLAGS6502.D) ? new Vector4(0.0f, 1.0f, 0.68f, 1) : new Vector4(1.0f, 0.0f, 0.0f, 1), "D");
			ImGui.TextColored(Convert.ToBoolean(_nes.cpu6502.status & (byte)CPU.FLAGS6502.I) ? new Vector4(0.0f, 1.0f, 0.68f, 1) : new Vector4(1.0f, 0.0f, 0.0f, 1), "I");
			ImGui.TextColored(Convert.ToBoolean(_nes.cpu6502.status & (byte)CPU.FLAGS6502.Z) ? new Vector4(0.0f, 1.0f, 0.68f, 1) : new Vector4(1.0f, 0.0f, 0.0f, 1), "Z");
			ImGui.TextColored(Convert.ToBoolean(_nes.cpu6502.status & (byte)CPU.FLAGS6502.C) ? new Vector4(0.0f, 1.0f, 0.68f, 1) : new Vector4(1.0f, 0.0f, 0.0f, 1), "C");

			ImGui.Text($"Program Counter: ${_nes.cpu6502.pc.ToString("X4")}");
			ImGui.Text($"A: ${_nes.cpu6502.a.ToString("X2")} [{_nes.cpu6502.a}]");
			ImGui.Text($"X: ${_nes.cpu6502.x.ToString("X2")} [{_nes.cpu6502.x}]");
			ImGui.Text($"Y: ${_nes.cpu6502.y.ToString("X2")} [{_nes.cpu6502.y}]");
			ImGui.Text($"Stack P: ${_nes.cpu6502.stkp.ToString("X4")}");

			ImGui.End();

			ImGui.Begin("Instruction Info");

			if (_mapAsm.TryGetValue(_nes.cpu6502.pc, out var currentLine))
			{
				var it_a = _mapAsm.Keys.OrderBy(k => k).ToList();
				int index = it_a.IndexOf(_nes.cpu6502.pc);

				int nLineY = (26 >> 1) * 10 + 72;

				// Indicate the execution line
				//ImGui.TextColored(new Vector4(0.0f, 1.87f, 1.0f, 1.0f), currentLine + " [EXECUTING] ");
				sInstruction.Add(currentLine + " [EXECUTING] ");

				// Draw next instructions
				//ImGui.Text("Next Instructions:");
				int yOffset = nLineY;
				int forward = index;
				Stack<string> nextIns = new Stack<string>();
				while (yOffset < (26 * 10) + 72 && ++forward < it_a.Count)
				{
					yOffset += 10;
					//ImGui.Text(_mapAsm[it_a[forward]]);
					nextIns.Push(_mapAsm[it_a[forward]]);
				}

				string nextIns2 = "";
				while(nextIns.Count > 0)
				{
					nextIns2 += nextIns.Pop() + "\n";
				}

				sInstruction.Add(nextIns2);

				// Draw previous instructions
				//ImGui.Text("Previous Instructions:");
				yOffset = nLineY;
				int backward = index;
				string prevIns = "";
				while (yOffset > 72 && --backward >= 0)
				{
					yOffset -= 10;
					//ImGui.Text(_mapAsm[it_a[backward]]);
					prevIns += _mapAsm[it_a[backward]] + "\n";
				}
				sInstruction.Insert(0, prevIns);
			}

			ImGui.Text(sInstruction.Count >= 3 ? sInstruction[2] : "There isn't any next instructions");
			ImGui.TextColored(new Vector4(0.0f, 1.87f, 1.0f, 1.0f), sInstruction.Count >= 2 ? sInstruction[1] : "No current instruction to execute");
			ImGui.Text(sInstruction.Count > 0 ? sInstruction[0] : "There isn't any history of previous instructions");

			ImGui.End();
		}

		protected override void OnTextInput(TextInputEventArgs e)
		{
			base.OnTextInput(e);

			controller.PressChar((char)e.Unicode);
		}

		protected override void OnMouseWheel(MouseWheelEventArgs e)
		{
			base.OnMouseWheel(e);

			controller.MouseScroll(e.Offset);
		}
	}
}
