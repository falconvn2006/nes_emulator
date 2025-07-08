using ImGuiNET;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

using nes_emulator.src;
using System.Numerics;
using nes_emulator.src.ImGuiHelper;

namespace nes_emulator
{
    public class Emulator : GameWindow
	{
		static Emulator Instance;

		Bus nes;
		Dictionary<ushort, string> mapAsm;
		Cartridge cart;

		bool emulationRun = false;
		float residualTime = 0.0f;

		byte nSelectedPalette = 0x00;

		private int nesWindowMultiplier = 3;

		ImGuiController controller;
		ImGuiFileBrowser fileBrowser;

		public Emulator(int width, int height, string title) : base(GameWindowSettings.Default, new NativeWindowSettings() { ClientSize = (width, height), Title = title})
		{
			NESSetup();

			Instance = this;
		}

		static float SoundOut(int channels, float globalTime, float timeStep)
		{
			if (channels == 0)
			{
				while (!Instance.nes.Clock()) { }
				return (float)Instance.nes.audioSample;
			}
			else
				return 0.0f;
		}

		private void NESSetup()
		{
			nes = new Bus();
			mapAsm = new Dictionary<ushort, string>();

			cart = new Cartridge(NESConfig.DEFAULT_NES_CARTRIDGE_FILE);

			nes.InsertCartridge(ref cart);

			mapAsm = nes.cpu6502.Disassemble(0x0000, 0xFFFF);

			nes.Reset();
		}

		protected override void OnLoad()
		{
			base.OnLoad();

			GL.ClearColor(0.2f, 0.3f, 0.3f, 1.0f);

			controller = new ImGuiController(ClientSize.X, ClientSize.Y);
			fileBrowser = new ImGuiFileBrowser(ImGuiFileBrowserFlags.EditPathString);

			fileBrowser.SetTypeFilters([".nes"]);

			nes.SetSampleFrequency(44100);
			SoundEngine.InitialiseAudio(44100, 1, 8, 512);
			SoundEngine.SetUserSynthFunction(SoundOut);
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

			UpdateWithAudio(args);
			//UpdateWithoutAudio(args);

			if(fileBrowser.HasSelected())
			{
				//Console.WriteLine("Selected the file: " + fileBrowser.GetSelected().ToString());
				//fileBrowser.ClearSelected();

				nes.Reset();

				cart = new Cartridge(fileBrowser.GetSelected().ToString());
				nes.InsertCartridge(ref cart);
				fileBrowser.ClearSelected();

				mapAsm = nes.cpu6502.Disassemble(0x0000, 0xFFFF);

				nes.Reset();

				fileBrowser.Close();
			}
		}

		private void UpdateWithAudio(FrameEventArgs args)
		{
			if (nes.ppu2C02.readyToBuffer)
			{
				nes.ppu2C02.UploadBuffer(nes.ppu2C02.textureScreenID, nes.ppu2C02.bufferScreen, NESConfig.NES_WIDTH, NESConfig.NES_HEIGHT);
				nes.ppu2C02.readyToBuffer = false;
			}

			if (KeyboardState.IsKeyDown(Keys.Escape))
			{
				Close();
			}

			ControllerUpdate();

			if (KeyboardState.IsKeyDown(Keys.L))
				base.WindowState = base.WindowState == WindowState.Normal ? WindowState.Fullscreen : WindowState.Normal;
			if (KeyboardState.IsKeyPressed(Keys.R))
				nes.Reset();
			if (KeyboardState.IsKeyPressed(Keys.P))
				nSelectedPalette = (byte)((++nSelectedPalette) & 0x07);
		}

		private void UpdateWithoutAudio(FrameEventArgs args)
		{
			if (KeyboardState.IsKeyDown(Keys.Escape))
			{
				Close();
			}

			ControllerUpdate();

			if (emulationRun)
			{
				if (residualTime > 0.0f)
					residualTime -= (float)args.Time;
				else
				{
					residualTime += (1.0f / 60.0f) - (float)args.Time;
					do { nes.Clock(); } while (!nes.ppu2C02.frameCompleted);
					nes.ppu2C02.frameCompleted = false;
				}
			}
			else
			{
				// Emulate step by step
				if (KeyboardState.IsKeyPressed(Keys.C))
				{
					do { nes.Clock(); } while (!nes.cpu6502.Complete());

					do { nes.Clock(); } while (nes.cpu6502.Complete());
				}

				// Emulate frame by frame
				if (KeyboardState.IsKeyPressed(Keys.F))
				{
					do { nes.Clock(); } while (!nes.ppu2C02.frameCompleted);
					do { nes.Clock(); } while (!nes.cpu6502.Complete());
					nes.ppu2C02.frameCompleted = false;
				}
			}

			if (KeyboardState.IsKeyDown(Keys.L))
				base.WindowState = base.WindowState == WindowState.Normal ? WindowState.Fullscreen : WindowState.Normal;
			if (KeyboardState.IsKeyPressed(Keys.R))
				nes.Reset();
			if (KeyboardState.IsKeyPressed(Keys.Space))
				emulationRun = !emulationRun;
			if (KeyboardState.IsKeyPressed(Keys.P))
				nSelectedPalette = (byte)((++nSelectedPalette) & 0x07);
		}

		protected override void OnRenderFrame(FrameEventArgs args)
		{
			base.OnRenderFrame(args);
			GL.Clear(ClearBufferMask.ColorBufferBit);

			controller.Update(this, (float)args.Time);

			ImGui.DockSpaceOverViewport();

			ImGuiDebug();

			ImGui.Begin("NES View", ImGuiWindowFlags.MenuBar);

			if(ImGui.BeginMenuBar())
			{
				if (ImGui.BeginMenu("File"))
				{
					if (ImGui.MenuItem("Open Game"))
					{
						fileBrowser.Open();
					}

					if(ImGui.MenuItem("Save State"))
					{
						
					}

					if(ImGui.MenuItem("Load State"))
					{

					}

					if (ImGui.MenuItem("Exit"))
					{
						Close();
					}

					ImGui.EndMenu();
				}

				ImGui.EndMenuBar();
			}

			ImGui.Image(nes.ppu2C02.textureScreenID, new Vector2(NESConfig.NES_WIDTH * nesWindowMultiplier, NESConfig.NES_HEIGHT * nesWindowMultiplier));

			ImGui.End();
			fileBrowser.Display();

			controller.Render();
			ImGuiController.CheckGLError("End of frame");
			SwapBuffers();
		}

		private void ControllerUpdate()
		{
			nes.controller[0] = 0x00;
			nes.controller[0] |= KeyboardState.IsKeyDown(Keys.X) ? (byte)0x80 : (byte)0x00; // A Button
			nes.controller[0] |= KeyboardState.IsKeyDown(Keys.Z) ? (byte)0x40 : (byte)0x00; // B Button
			nes.controller[0] |= KeyboardState.IsKeyDown(Keys.A) ? (byte)0x20 : (byte)0x00; // Select
			nes.controller[0] |= KeyboardState.IsKeyDown(Keys.S) ? (byte)0x10 : (byte)0x00; // Start
			nes.controller[0] |= KeyboardState.IsKeyDown(Keys.Up) ? (byte)0x08 : (byte)0x00;
			nes.controller[0] |= KeyboardState.IsKeyDown(Keys.Down) ? (byte)0x04 : (byte)0x00;
			nes.controller[0] |= KeyboardState.IsKeyDown(Keys.Left) ? (byte)0x02 : (byte)0x00;
			nes.controller[0] |= KeyboardState.IsKeyDown(Keys.Right) ? (byte)0x01 : (byte)0x00;
		}

		private void ImGuiDebug()
		{
			ImGuiRamDebug();
			ImGuiCPUDebug();
			ImGuiInstructsDebug();
			ImGuiSpriteDebug();
			ImGuiPatternTableDebug();
			//ImGui.ShowDemoWindow();
		}

		private void ImGuiRamDebug()
		{
			ImGui.Begin("RAM Info");

			ushort nAddr = 0x0000;
			int nRows = 16;
			int nColumns = 16;

			for (int row = 0; row < nRows; row++)
			{
				string sOffset = "$" + nAddr.ToString("X4") + ":";
				for (int col = 0; col < nColumns; col++)
				{
					sOffset += " " + nes.CPURead(nAddr, true).ToString("X2");
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
					sOffset += " " + nes.CPURead(nAddr, true).ToString("X2");
					nAddr += 1;
				}

				ImGui.Text(sOffset);
			}

			ImGui.End();
		}

		private void ImGuiCPUDebug()
		{
			ImGui.Begin("CPU Info");

			ImGui.Text("Status:");
			// CPU Statuses
			ImGui.TextColored(Convert.ToBoolean(nes.cpu6502.status & CPU.FLAGS6502.N) ? new Vector4(0.0f, 1.0f, 0.68f, 1) : new Vector4(1.0f, 0.0f, 0.0f, 1), "N");
			ImGui.TextColored(Convert.ToBoolean(nes.cpu6502.status & CPU.FLAGS6502.V) ? new Vector4(0.0f, 1.0f, 0.68f, 1) : new Vector4(1.0f, 0.0f, 0.0f, 1), "V");
			ImGui.TextColored(Convert.ToBoolean(nes.cpu6502.status & CPU.FLAGS6502.B) ? new Vector4(0.0f, 1.0f, 0.68f, 1) : new Vector4(1.0f, 0.0f, 0.0f, 1), "B");
			ImGui.TextColored(Convert.ToBoolean(nes.cpu6502.status & CPU.FLAGS6502.D) ? new Vector4(0.0f, 1.0f, 0.68f, 1) : new Vector4(1.0f, 0.0f, 0.0f, 1), "D");
			ImGui.TextColored(Convert.ToBoolean(nes.cpu6502.status & CPU.FLAGS6502.I) ? new Vector4(0.0f, 1.0f, 0.68f, 1) : new Vector4(1.0f, 0.0f, 0.0f, 1), "I");
			ImGui.TextColored(Convert.ToBoolean(nes.cpu6502.status & CPU.FLAGS6502.Z) ? new Vector4(0.0f, 1.0f, 0.68f, 1) : new Vector4(1.0f, 0.0f, 0.0f, 1), "Z");
			ImGui.TextColored(Convert.ToBoolean(nes.cpu6502.status & CPU.FLAGS6502.C) ? new Vector4(0.0f, 1.0f, 0.68f, 1) : new Vector4(1.0f, 0.0f, 0.0f, 1), "C");

			ImGui.Text($"Program Counter: ${nes.cpu6502.pc.ToString("X4")}");
			ImGui.Text($"A: ${nes.cpu6502.a.ToString("X2")} [{nes.cpu6502.a}]");
			ImGui.Text($"X: ${nes.cpu6502.x.ToString("X2")} [{nes.cpu6502.x}]");
			ImGui.Text($"Y: ${nes.cpu6502.y.ToString("X2")} [{nes.cpu6502.y}]");
			ImGui.Text($"Stack P: ${nes.cpu6502.stkp.ToString("X4")}");

			ImGui.End();
		}

		private void ImGuiInstructsDebug()
		{
			List<string> sInstruction = new List<string>();

			ImGui.Begin("Instruction Info");

			if (mapAsm.TryGetValue(nes.cpu6502.pc, out var currentLine))
			{
				var it_a = mapAsm.Keys.OrderBy(k => k).ToList();
				int index = it_a.IndexOf(nes.cpu6502.pc);

				int nLineY = (26 >> 1) * 10 + 72;

				// Indicate the execution line
				//ImGui.TextColored(new Vector4(0.0f, 1.87f, 1.0f, 1.0f), currentLine + " [EXECUTING] ");
				sInstruction.Add(currentLine + " [EXECUTING] ");

				// Draw next instructions
				//ImGui.Text("Next Instructions:");
				int yOffset = nLineY;
				int forward = index;
				Stack<string> nextInsStk = new Stack<string>();
				while (yOffset < (26 * 10) + 72 && ++forward < it_a.Count)
				{
					yOffset += 10;
					//ImGui.Text(_mapAsm[it_a[forward]]);
					nextInsStk.Push(mapAsm[it_a[forward]]);
				}

				string nextIns = "";
				while (nextInsStk.Count > 0)
				{
					nextIns += nextInsStk.Pop() + "\n";
				}

				sInstruction.Add(nextIns);

				// Draw previous instructions
				//ImGui.Text("Previous Instructions:");
				yOffset = nLineY;
				int backward = index;
				string prevIns = "";
				while (yOffset > 72 && --backward >= 0)
				{
					yOffset -= 10;
					//ImGui.Text(_mapAsm[it_a[backward]]);
					prevIns += mapAsm[it_a[backward]] + "\n";
				}
				sInstruction.Insert(0, prevIns);
			}

			ImGui.Text(sInstruction.Count >= 3 ? sInstruction[2] : "There isn't any next instructions");
			ImGui.TextColored(new Vector4(0.0f, 1.87f, 1.0f, 1.0f), sInstruction.Count >= 2 ? sInstruction[1] : "No current instruction to execute");
			ImGui.Text(sInstruction.Count > 0 ? sInstruction[0] : "There isn't any history of previous instructions");

			ImGui.End();
		}

		private unsafe void ImGuiSpriteDebug()
		{
			ImGui.Begin("Sprite Info");

			for (int i = 0; i < 26; i++)
			{
				string s = i.ToString("X2") + ": (" + nes.ppu2C02.oam[i * 4 + 3].ToString()
					+ ", " + nes.ppu2C02.oam[i*4+0].ToString() + ") "
					+ "ID: " + nes.ppu2C02.oam[i*4+1].ToString("X2")
					+ " AT: " + nes.ppu2C02.oam[i*4+2].ToString("X2");
				ImGui.Text(s);
			}

			ImGui.End();
		}

		private void ImGuiPatternTableDebug()
		{
			ImGui.Begin("Pattern tables");

			ImGui.TextColored(new Vector4(0.0f, 1.87f, 1.0f, 1.0f), "Current palette index: " + nSelectedPalette);

			nes.ppu2C02.GetPatternTable(0, nSelectedPalette);
			ImGui.Image(nes.ppu2C02.bufferPatternTableID[0], new Vector2(128 * 3, 128 * 3));
			ImGui.SameLine();
			nes.ppu2C02.GetPatternTable(1, nSelectedPalette);
			ImGui.Image(nes.ppu2C02.bufferPatternTableID[1], new Vector2(128 * 3, 128 * 3));

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

		protected override void OnUnload()
		{
			base.OnUnload();

			SoundEngine.DestroyAudio();
		}
	}
}
