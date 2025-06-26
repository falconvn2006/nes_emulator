using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// Rendering Stuff
using ImGuiNET;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace nes_emulator.src
{
	public class PPU
	{
		public struct Pixel
		{
			public byte r, g, b, a;
			public Pixel(byte _r = 0, byte _g = 0, byte _b = 0, byte _a = 255)
			{
				this.r = _r; this.g = _g;
				this.b = _b; this.a = _a;
			}
		}


		private Cartridge cartridge;
		private byte[,] tblName = new byte[2, 1024];
		private byte[] tblPalette = new byte[32];
		//private byte[,] tblPattern = new byte[2, 4096]; 

		public bool frame_complete = false;

		private short scanline = 0;
		private short cycle = 0;

		private Pixel[] palleteScreen = new Pixel[0x40];

		private int bufferNameTableID1 = 0;
		private int bufferNameTableID2 = 1;
		private Pixel[][] bufferNameTable = new Pixel[2][];

		private int bufferPatternTableID1 = 0;
		private int bufferPatternTableID2 = 1;
		private Pixel[][] bufferPatternTable = new Pixel[2][];

		// Rendering stuff
		public int textureScreenID;
		private Pixel[] bufferScreen;

		public PPU() 
		{
			InitializePalette();

			// Create the texture showing the NES contents that will be display
			CreateTexture(ref textureScreenID, NESConfig.NES_WIDTH, NESConfig.NES_HEIGHT, out bufferScreen);

			//
			CreateTexture(ref bufferNameTableID1, 256, 240, out bufferNameTable[bufferNameTableID1]);
			CreateTexture(ref bufferNameTableID2, 256, 240, out bufferNameTable[bufferNameTableID2]);

			//
			CreateTexture(ref bufferPatternTableID1, 128, 128, out bufferPatternTable[bufferPatternTableID1]);
			CreateTexture(ref bufferPatternTableID2, 128, 128, out bufferPatternTable[bufferPatternTableID2]);
		}

		private void InitializePalette()
		{
			palleteScreen[0x00] = new Pixel(84, 84, 84);
			palleteScreen[0x01] = new Pixel(0, 30, 116);
			palleteScreen[0x02] = new Pixel(8, 16, 144);
			palleteScreen[0x03] = new Pixel(48, 0, 136);
			palleteScreen[0x04] = new Pixel(68, 0, 100);
			palleteScreen[0x05] = new Pixel(92, 0, 48);
			palleteScreen[0x06] = new Pixel(84, 4, 0);
			palleteScreen[0x07] = new Pixel(60, 24, 0);
			palleteScreen[0x08] = new Pixel(32, 42, 0);
			palleteScreen[0x09] = new Pixel(8, 58, 0);
			palleteScreen[0x0A] = new Pixel(0, 64, 0);
			palleteScreen[0x0B] = new Pixel(0, 60, 0);
			palleteScreen[0x0C] = new Pixel(0, 50, 60);
			palleteScreen[0x0D] = new Pixel(0, 0, 0);
			palleteScreen[0x0E] = new Pixel(0, 0, 0);
			palleteScreen[0x0F] = new Pixel(0, 0, 0);

			palleteScreen[0x10] = new Pixel(152, 150, 152);
			palleteScreen[0x11] = new Pixel(8, 76, 196);
			palleteScreen[0x12] = new Pixel(48, 50, 236);
			palleteScreen[0x13] = new Pixel(92, 30, 228);
			palleteScreen[0x14] = new Pixel(136, 20, 176);
			palleteScreen[0x15] = new Pixel(160, 20, 100);
			palleteScreen[0x16] = new Pixel(152, 34, 32);
			palleteScreen[0x17] = new Pixel(120, 60, 0);
			palleteScreen[0x18] = new Pixel(84, 90, 0);
			palleteScreen[0x19] = new Pixel(40, 114, 0);
			palleteScreen[0x1A] = new Pixel(8, 124, 0);
			palleteScreen[0x1B] = new Pixel(0, 118, 40);
			palleteScreen[0x1C] = new Pixel(0, 102, 120);
			palleteScreen[0x1D] = new Pixel(0, 0, 0);
			palleteScreen[0x1E] = new Pixel(0, 0, 0);
			palleteScreen[0x1F] = new Pixel(0, 0, 0);

			palleteScreen[0x20] = new Pixel(236, 238, 236);
			palleteScreen[0x21] = new Pixel(76, 154, 236);
			palleteScreen[0x22] = new Pixel(120, 124, 236);
			palleteScreen[0x23] = new Pixel(176, 98, 236);
			palleteScreen[0x24] = new Pixel(228, 84, 236);
			palleteScreen[0x25] = new Pixel(236, 88, 180);
			palleteScreen[0x26] = new Pixel(236, 106, 100);
			palleteScreen[0x27] = new Pixel(212, 136, 32);
			palleteScreen[0x28] = new Pixel(160, 170, 0);
			palleteScreen[0x29] = new Pixel(116, 196, 0);
			palleteScreen[0x2A] = new Pixel(76, 208, 32);
			palleteScreen[0x2B] = new Pixel(56, 204, 108);
			palleteScreen[0x2C] = new Pixel(56, 180, 204);
			palleteScreen[0x2D] = new Pixel(60, 60, 60);
			palleteScreen[0x2E] = new Pixel(0, 0, 0);
			palleteScreen[0x2F] = new Pixel(0, 0, 0);

			palleteScreen[0x30] = new Pixel(236, 238, 236);
			palleteScreen[0x31] = new Pixel(168, 204, 236);
			palleteScreen[0x32] = new Pixel(188, 188, 236);
			palleteScreen[0x33] = new Pixel(212, 178, 236);
			palleteScreen[0x34] = new Pixel(236, 174, 236);
			palleteScreen[0x35] = new Pixel(236, 174, 212);
			palleteScreen[0x36] = new Pixel(236, 180, 176);
			palleteScreen[0x37] = new Pixel(228, 196, 144);
			palleteScreen[0x38] = new Pixel(204, 210, 120);
			palleteScreen[0x39] = new Pixel(180, 222, 120);
			palleteScreen[0x3A] = new Pixel(168, 226, 144);
			palleteScreen[0x3B] = new Pixel(152, 226, 180);
			palleteScreen[0x3C] = new Pixel(160, 214, 228);
			palleteScreen[0x3D] = new Pixel(160, 162, 160);
			palleteScreen[0x3E] = new Pixel(0, 0, 0);
			palleteScreen[0x3F] = new Pixel(0, 0, 0);
		}

		public void CreateTexture(ref int textureID, int width, int height, out Pixel[] buffer)
		{
			buffer = new Pixel[width * height];

			textureID = GL.GenTexture();
			GL.BindTexture(TextureTarget.Texture2D, textureID);
			GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, width, height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);

			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
		}

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

			if(cartridge.PPURead(addr, ref data))
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
			int x = cycle;
			int y = scanline;
			if (x >= 0 && x < NESConfig.NES_WIDTH && y >= 0 && y < NESConfig.NES_HEIGHT)
			{
				bufferScreen[y * NESConfig.NES_WIDTH + x] = palleteScreen[(Random.Shared.Next(2) == 1) ? 0x3F : 0x30];
			}

			cycle++;

			if(cycle >= 341)
			{
				cycle = 0;
				scanline++;
				if(scanline >=261)
				{
					scanline = -1;
					frame_complete = true;

					UploadBuffer(textureScreenID, bufferScreen, NESConfig.NES_WIDTH, NESConfig.NES_HEIGHT);
				}
			}
		}

		//
		private void UploadBuffer(int textureID, Pixel[] buffer, int width, int height)
		{
			GL.BindTexture(TextureTarget.Texture2D, textureID);
			unsafe
			{
				fixed (Pixel* ptr = buffer)
				{
					GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, width, height,
						PixelFormat.Rgba, PixelType.UnsignedByte, (IntPtr)ptr);
				}
			}
		}

		#endregion

		#region Debugging Stuff

		public ref Pixel[] GetScreen()
		{
			return ref bufferScreen;
		}

		public ref Pixel[] GetNameTable(byte i)
		{
			return ref bufferNameTable[i];
		}

		public ref Pixel[] GetPatternTable(byte i)
		{
			return ref bufferPatternTable[i];
		}

		#endregion
	}
}
