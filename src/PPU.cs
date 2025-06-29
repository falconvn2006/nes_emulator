using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel.DataAnnotations;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

// Rendering Stuff
using ImGuiNET;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using static nes_emulator.src.PPU;

namespace nes_emulator.src
{
	public class PPU
	{
		#region Structs

		public struct Pixel
		{
			public byte r, g, b, a;
			public Pixel(byte _r = 0, byte _g = 0, byte _b = 0, byte _a = 255)
			{
				this.r = _r; this.g = _g;
				this.b = _b; this.a = _a;
			}
		}

		[StructLayout(LayoutKind.Explicit, Pack = 1)]
		public struct ObjectAttributeEntry
		{
			[FieldOffset(0)] public uint entry;         // This whole struct (union)
			[FieldOffset(0)] public byte y;				// Y position of sprite
			[FieldOffset(1)] public byte id;            // ID of tile from pattern memory
			[FieldOffset(2)] public byte attribute;		// Flags define how sprite should be rendered
			[FieldOffset(3)] public byte x;				// X position of sprite
		}

		// bit fields are not a thing in C# :(
		// Using BitVectors to emulate this behavior

		private BitVector32 status = new BitVector32(0);
		private BitVector32 mask = new BitVector32(0);
		private BitVector32 control = new BitVector32(0);
		private BitVector32 vram_addr = new BitVector32(0);
		private BitVector32 tram_addr = new BitVector32(0);

		// Helper section
		// BitVector internal data is read only and can't create a mask for the whole 32bit
		// No data structure are greater than 16 bit anyway so this is enough
		// This is actually a bug though... the biggest mask can only be 15bit >:@
		// Good thing bit 16 is never used anyway...
		private BitVector32.Section allFirst15 = BitVector32.CreateSection(short.MaxValue); // Should have been ushort.MaxValue but Section.Mask is short type... and no ushort.MinVlue doesn't work !

		// Status sections
		private BitVector32.Section statusUnused;
		private BitVector32.Section statusSpriteOverflow;
		private BitVector32.Section statusSpriteZeroHit;
		private BitVector32.Section statusVerticalBlank;

		// Mask... masks
		private int maskGrayscale;
		private int maskRenderBackgroundLeft;
		private int maskRenderSpritesLeft;
		private int maskRenderBackground;
		private int maskRenderSprites;
		private int maskEnhanceRed;
		private int maskEnhanceGreen;
		private int maskEnhanceBlue;

		// Control masks
		private int controlNametableX;
		private int controlNametableY;
		private int controlIncrementMode;
		private int controlPatternSprite;
		private int controlPatternBackground;
		private int controlSpriteSize;
		private int controlSlaveMode; // unused
		private int controlEnableNmi;

		// Loopy register sections
		// Credit to Loopy for working this out :D
		private BitVector32.Section loopyCoarseX;
		private BitVector32.Section loopyCoarseY;
		private BitVector32.Section loopyNametableX;
		private BitVector32.Section loopyNametableY;
		private BitVector32.Section loopyFineY;

		#endregion

		byte addressLatch = 0x00;
		byte ppuDataBuffer = 0x00;

		byte fineX = 0x00;

		// Background Bytes
		byte bgNextTileId = 0x00;
		byte bgNextTileAttrib = 0x00;
		byte bgNextTileLSB = 0x00;
		byte bgNextTileMSB = 0x00;

		// Background Shifters
		ushort bgShifterPatternLO = 0x0000;
		ushort bgShifterPatternHI = 0x0000;
		ushort bgShifterAttribLO = 0x0000;
		ushort bgShifterAttribHI = 0x0000;

		// Sprites stuff
		public byte[] oam = new byte[4 * 64];
		private IntPtr pOAM;
		byte oamAddr = 0x00;

		private ObjectAttributeEntry[] spriteScanline = new ObjectAttributeEntry[8];
		private byte spriteCount;

		// Sprite Shifters
		byte[] spriteShifterPatternLO = new byte[8];
		byte[] spriteShifterPatternHI = new byte[8];

		// Sprite Zero Hit Detector Stuff
		bool spriteZeroHitPossible = false;
		bool spriteZeroBeingRendered = false;

		private Cartridge cartridge;
		public byte[][] tblName = new byte[2][];
		public byte[] tblPalette = new byte[32];
		public byte[][] tblPattern = new byte[2][];

		public bool frameCompleted = false;

		private short scanline = 0;
		private short cycle = 0;
		public bool nmi;

		private Pixel[] palleteScreen = new Pixel[0x40];

		public int[] bufferNameTableID = new int[2];
		private Pixel[][] bufferNameTable = new Pixel[2][];

		public int[] bufferPatternTableID = new int[2];
		private Pixel[][] bufferPatternTable = new Pixel[2][];

		// Rendering stuff
		public int textureScreenID;
		private Pixel[] bufferScreen;

		public PPU() 
		{
			tblName[0] = new byte[1024];
			tblName[1] = new byte[1024];
			tblPattern[0] = new byte[4096];
			tblPattern[1] = new byte[4096];

			InitializePalette();

			// Init sections definitions
			statusUnused = BitVector32.CreateSection(31);
			statusSpriteOverflow = BitVector32.CreateSection(1, statusUnused);
			statusSpriteZeroHit = BitVector32.CreateSection(1, statusSpriteOverflow);
			statusVerticalBlank = BitVector32.CreateSection(1, statusSpriteZeroHit);

			maskGrayscale = BitVector32.CreateMask();
			maskRenderBackgroundLeft = BitVector32.CreateMask(maskGrayscale);
			maskRenderSpritesLeft = BitVector32.CreateMask(maskRenderBackgroundLeft);
			maskRenderBackground = BitVector32.CreateMask(maskRenderSpritesLeft);
			maskRenderSprites = BitVector32.CreateMask(maskRenderBackground);
			maskEnhanceRed = BitVector32.CreateMask(maskRenderSprites);
			maskEnhanceGreen = BitVector32.CreateMask(maskEnhanceRed);
			maskEnhanceBlue = BitVector32.CreateMask(maskEnhanceGreen);

			controlNametableX = BitVector32.CreateMask();
			controlNametableY = BitVector32.CreateMask(controlNametableX);
			controlIncrementMode = BitVector32.CreateMask(controlNametableY);
			controlPatternSprite = BitVector32.CreateMask(controlIncrementMode);
			controlPatternBackground = BitVector32.CreateMask(controlPatternSprite);
			controlSpriteSize = BitVector32.CreateMask(controlPatternBackground);
			controlSlaveMode = BitVector32.CreateMask(controlSpriteSize); // unused
			controlEnableNmi = BitVector32.CreateMask(controlSlaveMode);

			loopyCoarseX = BitVector32.CreateSection(31);
			loopyCoarseY = BitVector32.CreateSection(31, loopyCoarseX);
			loopyNametableX = BitVector32.CreateSection(1, loopyCoarseY);
			loopyNametableY = BitVector32.CreateSection(1, loopyNametableX);
			loopyFineY = BitVector32.CreateSection(7, loopyNametableY);

			var handle = GCHandle.Alloc(oam, GCHandleType.Pinned);
			pOAM = handle.AddrOfPinnedObject();

			//bufferNameTable[0] = new Pixel[256 * 240];
			//bufferNameTable[1] = new Pixel[256 * 240];

			bufferPatternTable[0] = new Pixel[128 * 128];
			bufferPatternTable[1] = new Pixel[128 * 128];

			// Create the texture showing the NES contents that will be display
			CreateTexture(ref textureScreenID, NESConfig.NES_WIDTH, NESConfig.NES_HEIGHT, out bufferScreen);

			//
			CreateTexture(ref bufferNameTableID[0], 256, 240, out bufferNameTable[bufferNameTableID[0]]);
			CreateTexture(ref bufferNameTableID[1], 256, 240, out bufferNameTable[bufferNameTableID[1]]);

			//	
			CreateTexture(ref bufferPatternTableID[0], 128, 128, out bufferPatternTable[bufferPatternTableID[0]]);
			CreateTexture(ref bufferPatternTableID[1], 128, 128, out bufferPatternTable[bufferPatternTableID[1]]);
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

		/// <summary>
		/// Helper method to get part of a byte array interpreted as a struct
		/// Overrall mechanic tolen in part from here:
		/// https://stackoverflow.com/questions/2871/reading-a-c-c-data-structure-in-c-sharp-from-a-byte-array
		/// But made it staticaly defined
		/// We add the offset to the pointer to emulate accessor of a sObjectAttributeEntry[]
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="bytes"></param>
		/// <returns></returns>
		ObjectAttributeEntry GetObjectAttribute(byte offset)
		{
			return (ObjectAttributeEntry)Marshal.PtrToStructure(pOAM + (offset * 4), typeof(ObjectAttributeEntry));
		}

		/// <summary>
		/// And the opposite operation
		/// </summary>
		/// <param name="offset"></param>
		/// <returns></returns>
		void GetObjectAttribute(byte offset, ObjectAttributeEntry entry)
		{
			Marshal.StructureToPtr(entry, pOAM + (offset * 4), false);
		}


		Pixel GetColourFromPaletteRam(byte palette, byte pixel)
		{
			return palleteScreen[PPURead((ushort)(0x3F00 + (palette << 2) + pixel)) & 0x3F];
		}

		#region Read and Write Stuff

		public byte CPURead(ushort addr, bool readOnly)
		{
			byte data = 0x00;

			if (readOnly)
			{
				switch (addr)
				{
					case 0x0000: // Control
						data = (byte)control.Data;
						break;
					case 0x0001: // Mask
						data = (byte)mask.Data;
						break;
					case 0x0002: // Status
						data = (byte)status.Data;
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
			else
			{
				switch (addr)
				{
					case 0x0000: // Control -- Not readable
						break;
					case 0x0001: // Mask --  Not readable
						break;
					case 0x0002: // Status
						data = (byte)((status.Data & 0xE0) | (ppuDataBuffer & 0x1F));
						status[statusVerticalBlank] = 0;
						addressLatch = 0;
						break;
					case 0x0003: // OAM Address -- Not readable
						break;
					case 0x0004: // OAM Data
						data = oam[oamAddr];
						break;
					case 0x0005: // Scroll -- Not readable
						break;
					case 0x0006: // PPU Address -- Not readable
						break;
					case 0x0007: // PPU Data
						data = ppuDataBuffer;
						ppuDataBuffer = PPURead((ushort)vram_addr.Data);

						if (vram_addr.Data >= 0x3F00) data = ppuDataBuffer;

						vram_addr[allFirst15] += control[controlIncrementMode] ? 32 : 1;
						break;
				}
			}

			return data;
		}

		public void CPUWrite(ushort addr, byte data)
		{
			switch (addr)
			{
				case 0x0000: // Control
					control[allFirst15] = data;
					tram_addr[loopyNametableX] = control[controlNametableX] ? 1 : 0;
					tram_addr[loopyNametableY] = control[controlNametableY] ? 1 : 0;
					break;
				case 0x0001: // Mask
					mask[allFirst15] = data;
					break;
				case 0x0002: // Status
					break;
				case 0x0003: // OAM Address
					oamAddr = data;
					break;
				case 0x0004: // OAM Data
					oam[oamAddr] = data;
					break;
				case 0x0005: // Scroll
					if(addressLatch == 0)
					{
						fineX = (byte)(data & 0x07);
						tram_addr[loopyCoarseX] = data >> 3;
						addressLatch = 1;
					}
					else
					{
						tram_addr[loopyFineY] = data & 0x07;
						tram_addr[loopyCoarseY] = data >> 3;
						addressLatch = 0;
					}
					break;
				case 0x0006: // PPU Address
					if (addressLatch == 0)
					{
						tram_addr[allFirst15] = (ushort)((data & 0x3F) << 8) | (tram_addr.Data & 0x00FF);
						addressLatch = 1;
					}
					else
					{
						tram_addr[allFirst15] = (tram_addr.Data & 0xFF00) | data;
						vram_addr = tram_addr;
						addressLatch = 0;
					}
					break;
				case 0x0007: // PPU Data
					PPUWrite((ushort)vram_addr.Data, data);
					vram_addr[allFirst15] += control[controlIncrementMode] ? 32 : 1;
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
			else if(addr >= 0x0000 && addr <= 0x1FFF)
			{
				// If the cartridge cant map the address, have
				// a physical location ready here
				data = tblPattern[(addr & 0x1000) >> 12][addr & 0x0FFF];
			}
			else if(addr >= 0x2000 && addr <= 0x3EFF)
			{
				addr &= 0x0FFF;

				if (cartridge.mirror == Cartridge.MIRROR.VERTICAL)
				{
					// Vertical
					if (addr >= 0x0000 && addr <= 0x03FF)
						data = tblName[0][addr & 0x03FF];
					if (addr >= 0x0400 && addr <= 0x07FF)
						data = tblName[1][addr & 0x03FF];
					if (addr >= 0x0800 && addr <= 0x0BFF)
						data = tblName[0][addr & 0x03FF];
					if (addr >= 0x0C00 && addr <= 0x0FFF)
						data = tblName[1][addr & 0x03FF];
				}
				else if(cartridge.mirror == Cartridge.MIRROR.HORIZONTAL)
				{
					// Horizontal
					if (addr >= 0x0000 && addr <= 0x03FF)
						data = tblName[0][addr & 0x03FF];
					if (addr >= 0x0400 && addr <= 0x07FF)
						data = tblName[0][addr & 0x03FF];
					if (addr >= 0x0800 && addr <= 0x0BFF)
						data = tblName[1][addr & 0x03FF];
					if (addr >= 0x0C00 && addr <= 0x0FFF)
						data = tblName[1][addr & 0x03FF];
				}
			}
			else if(addr >= 0x3F00 && addr <= 0x3FFF)
			{
				addr &= 0x001F;

				if (addr == 0x0010) addr = 0x0000;
				if (addr == 0x0014) addr = 0x0004;
				if (addr == 0x0018) addr = 0x0008;
				if (addr == 0x001C) addr = 0x000C;

				data = (byte)(tblPalette[addr] & (mask[maskGrayscale] ? 0x30 : 0x3F));

			}


			return data;
		}

		public void PPUWrite(ushort addr, byte data)
		{
			addr &= 0x3FFF;

			if(cartridge.PPUWrite(addr, data))
			{

			}
			else if (addr >= 0x0000 && addr <= 0x1FFF)
			{
				tblPattern[(addr & 0x1000) >> 12][addr & 0x0FFF] = data;
			}
			else if (addr >= 0x2000 && addr <= 0x3EFF)
			{
				addr &= 0x0FFF;

				if (cartridge.mirror == Cartridge.MIRROR.VERTICAL)
				{
					// Vertical
					if (addr >= 0x0000 && addr <= 0x03FF)
						tblName[0][addr & 0x03FF] = data;
					if (addr >= 0x0400 && addr <= 0x07FF)
						tblName[1][addr & 0x03FF] = data;
					if (addr >= 0x0800 && addr <= 0x0BFF)
						tblName[0][addr & 0x03FF] = data;
					if (addr >= 0x0C00 && addr <= 0x0FFF)
						tblName[1][addr & 0x03FF] = data;
				}
				else if (cartridge.mirror == Cartridge.MIRROR.HORIZONTAL)
				{
					// Horizontal
					if (addr >= 0x0000 && addr <= 0x03FF)
						tblName[0][addr & 0x03FF] = data;
					if (addr >= 0x0400 && addr <= 0x07FF)
						tblName[0][addr & 0x03FF] = data;
					if (addr >= 0x0800 && addr <= 0x0BFF)
						tblName[1][addr & 0x03FF] = data;
					if (addr >= 0x0C00 && addr <= 0x0FFF)
						tblName[1][addr & 0x03FF] = data;
				}
			}
			else if (addr >= 0x3F00 && addr <= 0x3FFF)
			{
				addr &= 0x001F;

				if (addr == 0x0010) addr = 0x0000;
				if (addr == 0x0014) addr = 0x0004;
				if (addr == 0x0018) addr = 0x0008;
				if (addr == 0x001C) addr = 0x000C;

				tblPalette[addr] = data;
			}
		}

		#endregion

		#region Interface

		public void ConnectCartridge(Cartridge _cartridge)
		{
			this.cartridge = _cartridge;
		}

		public void Reset()
		{
			fineX = 0x00;
			addressLatch = 0x00;
			ppuDataBuffer = 0x00;
			scanline = 0;
			cycle = 0;
			bgNextTileId = 0x00;
			bgNextTileAttrib = 0x00;
			bgNextTileLSB = 0x00;
			bgNextTileMSB = 0x00;
			bgShifterPatternLO = 0x0000;
			bgShifterPatternHI = 0x0000;
			bgShifterAttribLO = 0x0000;
			bgShifterAttribHI = 0x0000;
			status[allFirst15] = 0x00;
			mask[allFirst15] = 0x00;
			control[allFirst15] = 0x00;
			vram_addr[allFirst15] = 0x0000;
			tram_addr[allFirst15] = 0x0000;
		}

		public void Clock()
		{
			void IncrementScrollX()
			{
				if(mask[maskRenderBackground] || mask[maskRenderSprites])
				{
					if(vram_addr[loopyCoarseX] == 31)
					{
						vram_addr[loopyCoarseX] = 0;
						vram_addr[loopyNametableX] = ~vram_addr[loopyNametableX];
					}
					else
					{
						vram_addr[loopyCoarseX]++;
					}
				}
			}

			void IncrementScrollY()
			{
				if (mask[maskRenderBackground] || mask[maskRenderSprites])
				{
					if (vram_addr[loopyFineY] < 7)
					{
						vram_addr[loopyFineY]++;
					}
					else
					{
						// If we have gone beyond the height of a row, we need to
						// increment the row, potentially wrapping into neighbouring
						// vertical nametables. Dont forget however, the bottom two rows
						// do not contain tile information. The coarse y offset is used
						// to identify which row of the nametable we want, and the fine
						// y offset is the specific "scanline"

						// Reset fine y offset
						vram_addr[loopyFineY] = 0;

						// Check if we need to swap vertical nametable targets
						if (vram_addr[loopyCoarseY] == 29)
						{
							// We do, so reset coarse y offset
							vram_addr[loopyCoarseY] = 0;

							// And flip the target nametable bit
							vram_addr[loopyNametableY] = ~vram_addr[loopyNametableY];
						}
						else if (vram_addr[loopyCoarseY] == 31)
						{
							// In case the pointer is in the attribute memory, we
							// just wrap around the current nametable
							vram_addr[loopyCoarseY] = 0;
						}
						else
						{
							// None of the above boundary/wrapping conditions apply
							// so just increment the coarse y offset
							vram_addr[loopyCoarseY]++;
						}
					}
				}
			}

			void TransferAddressX()
			{
				// Only if rendering is enable
				if(mask[maskRenderBackground] || mask[maskRenderSprites])
				{
					vram_addr[loopyNametableX] = tram_addr[loopyNametableX];
					vram_addr[loopyCoarseX] = tram_addr[loopyCoarseX];
				}
			}

			void TransferAddressY()
			{
				if(mask[maskRenderBackground] || mask[maskRenderSprites])
				{
					vram_addr[loopyFineY] = tram_addr[loopyFineY];
					vram_addr[loopyNametableY] = tram_addr[loopyNametableY];
					vram_addr[loopyCoarseY] = tram_addr[loopyCoarseY];
				}
			}

			void LoadBackgroundShifters()
			{
				bgShifterPatternLO = (ushort)((bgShifterPatternLO & 0xFF00) | bgNextTileLSB);
				bgShifterPatternHI = (ushort)((bgShifterPatternHI & 0xFF00) | bgNextTileMSB);

				bgShifterAttribLO = (ushort)((bgShifterAttribLO & 0xFF00) | ((bgNextTileAttrib & 0b01) != 0 ? 0xFF : 0x00));
				bgShifterAttribHI = (ushort)((bgShifterAttribHI & 0xFF00) | ((bgNextTileAttrib & 0b10) != 0 ? 0xFF : 0x00));
			}

			void UpdateShifters()
			{
				if(mask[maskRenderBackground])
				{
					bgShifterPatternLO <<= 1;
					bgShifterPatternHI <<= 1;

					bgShifterAttribLO <<= 1;
					bgShifterAttribHI <<= 1;
				}

				if(mask[maskRenderSprites] && cycle >= 1 && cycle < 258)
				{
					for(int i = 0; i < spriteCount; i++)
					{
						if (spriteScanline[i].x > 0)
						{
							spriteScanline[i].x--;
						}
						else
						{
							spriteShifterPatternLO[i] <<= 1;
							spriteShifterPatternHI[i] <<= 1;
						}
					}
				}
			}

			if(scanline >= -1 && scanline < 240)
			{
				if(scanline == 0 && cycle == 0 && (mask[maskRenderBackground] || mask[maskRenderSprites]))
				{
					// "Odd frame" cycle skip
					cycle = 1;
				}

				if (scanline == -1 && cycle == 1)
				{
					status[statusVerticalBlank] = 0;
					status[statusSpriteOverflow] = 0;
					status[statusSpriteZeroHit] = 0;

					for (int i = 0; i < 8; i++)
					{
						spriteShifterPatternLO[i] = 0;
						spriteShifterPatternHI[i] = 0;
					}
				}

				if((cycle >= 2 && cycle < 258) || (cycle >= 321 && cycle < 338))
				{
					UpdateShifters();

					switch ((cycle - 1) % 8)
					{
						case 0:
							LoadBackgroundShifters();

							bgNextTileId = PPURead((ushort)(0x2000 | (vram_addr.Data & 0x0FFF)));
							break;
						case 2:
							bgNextTileAttrib = PPURead((ushort)(0x23C0 | (vram_addr[loopyNametableY] << 11)
								| (vram_addr[loopyNametableX] << 10)
								| ((vram_addr[loopyCoarseY] >> 2) << 3)
								| (vram_addr[loopyCoarseX] >> 2)));

							if ((vram_addr[loopyCoarseY] & 0x02) != 0) bgNextTileAttrib >>= 4;
							if ((vram_addr[loopyCoarseX] & 0x02) != 0) bgNextTileAttrib >>= 2;
							bgNextTileAttrib &= 0x03;
							break;
						case 4:
							bgNextTileLSB = PPURead((ushort)(((control[controlPatternBackground] ? 1 : 0) << 12)
								+ (bgNextTileId << 4)
								+ (vram_addr[loopyFineY]) + 0));
							break;
						case 6:
							bgNextTileMSB = PPURead((ushort)(((control[controlPatternBackground] ? 1 : 0) << 12)
								+ (bgNextTileId << 4)
								+ (vram_addr[loopyFineY]) + 8));
							break;
						case 7:
							IncrementScrollX();
							break;
					}
				}

				if (cycle == 256)
				{
					IncrementScrollY();
				}

				if(cycle == 257)
				{
					LoadBackgroundShifters();
					TransferAddressX();
				}

				if(cycle == 338 || cycle == 340)
				{
					bgNextTileId = PPURead((ushort)(0x2000 | (vram_addr.Data & 0x0FFF)));
				}

				if(scanline == -1 && cycle >= 280 && cycle < 305)
				{
					TransferAddressY();
				}

				// Foreground Rendering =========================================

				// Sprite Evaluation
				if(cycle == 257 && scanline >= 0)
				{
					// Firstly, clear out the sprite memory. This memory is used to store the
					// sprites to be rendered. It is not the OAM.
					Array.Clear(spriteScanline, 0, spriteScanline.Length);

					// The NES supports a maximum number of sprites per scanline. Nominally
					// this is 8 or fewer sprites. This is why in some games you see sprites
					// flicker or disappear when the scene gets busy.
					spriteCount = 0;

					// Secondly, clear out any residual information in sprite pattern shifters
					for (byte i = 0; i < 8; i++)
					{
						spriteShifterPatternLO[i] = 0;
						spriteShifterPatternHI[i] = 0;
					}

					byte nOAMEntry = 0;
					spriteZeroHitPossible = false;
					while(nOAMEntry < 64 && spriteCount < 9)
					{
						short diff = (short)(scanline - GetObjectAttribute(nOAMEntry).y);
						if(diff >= 0 && diff < (control[controlSpriteSize] ? 16 : 8))
						{
							if(spriteCount < 8)
							{
								if(nOAMEntry == 0)
								{
									spriteZeroHitPossible = true;
								}

								spriteScanline[spriteCount] = GetObjectAttribute(nOAMEntry);
								spriteCount++;
							}
						}

						nOAMEntry++;
					}
					status[statusSpriteOverflow] = (spriteCount > 8) ? 1 : 0;
				}

				if(cycle == 340)
				{
					for(byte i = 0; i < spriteCount; i++)
					{
						byte _spritePatternBitsLO, _spritePatternBitsHI;
						ushort _spritePatternAddrLO, _spritePatternAddrHI;

						if(!control[controlSpriteSize])
						{
							// 8x8 Sprite Mode
							if ((spriteScanline[i].attribute & 0x80) == 0)
							{
								// Sprite is not flipped vertically, i.e. normal
								_spritePatternAddrLO =
									(ushort)(
												(control[controlPatternSprite] ? 1 : 0 << 12)
												| (spriteScanline[i].id << 4)
												| (scanline - spriteScanline[i].y)
											);
							}
							else
							{
								// Sprite is flipped vertically, i.e. upside down
								_spritePatternAddrLO =
									(ushort)(
												(control[controlPatternSprite] ? 1 : 0 << 12)
												| (spriteScanline[i].id << 4)
												| (7 - (scanline - spriteScanline[i].y))
											);
							}
						}
						else
						{
							// 8x16 Sprite Mode
							if ((spriteScanline[i].attribute & 0x80) == 0)
							{
								// Sprite is not flipped vertically, i.e. normal
								if(scanline - spriteScanline[i].y < 8)
								{
									// Reading top half tile
									_spritePatternAddrLO =
										(ushort)(
													((spriteScanline[i].id & 0x01) << 12)
													| ((spriteScanline[i].id & 0xFE) << 4)
													| ((scanline - spriteScanline[i].y) & 0x07)
												);
								}
								else
								{
									// Reading bottom half tile
									_spritePatternAddrLO =
										(ushort)(
													((spriteScanline[i].id & 0x01) << 12)
													| (((spriteScanline[i].id & 0xFE) + 1) << 4)
													| ((scanline - spriteScanline[i].y) & 0x07)
												);
								}
							}
							else
							{
								// Sprite is flipped vertically, i.e. upside down
								if (scanline - spriteScanline[i].y < 8)
								{
									// Reading top half tile
									_spritePatternAddrLO =
										(ushort)(
													((spriteScanline[i].id & 0x01) << 12)
													| (((spriteScanline[i].id & 0xFE) + 1) << 4)
													| (7 - (scanline - spriteScanline[i].y) & 0x07)
												);
								}
								else
								{
									// Reading bottom half tile
									_spritePatternAddrLO =
										(ushort)(
													((spriteScanline[i].id & 0x01) << 12)
													| ((spriteScanline[i].id & 0xFE) << 4)
													| (7 - (scanline - spriteScanline[i].y) & 0x07)
												);
								}
							}
						}

						_spritePatternAddrHI = (ushort)(_spritePatternAddrLO + 8);

						_spritePatternBitsLO = PPURead(_spritePatternAddrLO);
						_spritePatternBitsHI = PPURead(_spritePatternAddrHI);

						// Check for flipped horizontally
						if ((spriteScanline[i].attribute & 0x40) != 0)
						{
							// This little lambda function "flips" a byte
							// so 0b11100000 becomes 0b00000111. It's very
							// clever, and stolen completely from here:
							// https://stackoverflow.com/a/2602885
							byte flipbyte(byte b)
							{
								b = (byte)((byte)((b & 0xF0) >> 4) | (byte)((b & 0x0F) << 4));
								b = (byte)((byte)((b & 0xCC) >> 2) | (byte)((b & 0x33) << 2));
								b = (byte)((byte)((b & 0xAA) >> 1) | (byte)((b & 0x55) << 1));
								return b;
							}

							// Flip patterns horizontally
							_spritePatternBitsLO = flipbyte(_spritePatternBitsLO);
							_spritePatternBitsHI = flipbyte(_spritePatternBitsHI);
						}

						// Load the bytes into the shifters
						spriteShifterPatternLO[i] = _spritePatternBitsLO;
						spriteShifterPatternHI[i] = _spritePatternBitsHI;


					}
				}
			}

			if(scanline == 240)
			{
				// Post Render Scanline - Do Nothing!
			}

			if(scanline  >= 241 && scanline < 261)
			{
				if (scanline == 241 && cycle == 1)
				{
					status[statusVerticalBlank] = 1;
					if (control[controlEnableNmi])
						nmi = true;
				}
			}


			// Draw Background
			byte _bgPixel = 0x00;
			byte _bgPalette = 0x00;
			if(mask[maskRenderBackground])
			{
				ushort _bitMUX = (ushort)(0x8000 >> fineX);

				byte _p0Pixel = (bgShifterPatternLO & _bitMUX) > 0 ? (byte)1 : (byte)0;
				byte _p1Pixel = (bgShifterPatternHI & _bitMUX) > 0 ? (byte)1 : (byte)0;

				_bgPixel = (byte)((_p1Pixel << 1) | _p0Pixel);

				byte _bg0Palette = (bgShifterAttribLO & _bitMUX) > 0 ? (byte)1 : (byte)0;
				byte _bg1Palette = (bgShifterAttribHI & _bitMUX) > 0 ? (byte)1 : (byte)0;

				_bgPalette = (byte)((_bg1Palette << 1) | _bg0Palette);
			}

			// Draw Foreground/Sprites
			byte _fgPixel = 0x00;
			byte _fgPalette = 0x00;
			byte _fgPriority = 0x00;

			if(mask[maskRenderSprites])
			{
				spriteZeroBeingRendered = false;

				for(int i = 0; i < spriteCount; i++)
				{
					if (spriteScanline[i].x == 0)
					{
						byte _fgPixelLO = (spriteShifterPatternLO[i] & 0x80) > 0 ? (byte)1 : (byte)0;
						byte _fgPixelHI = (spriteShifterPatternHI[i] & 0x80) > 0 ? (byte)1 : (byte)0;
						_fgPixel = (byte)((_fgPixelHI << 1) | _fgPixelLO);

						_fgPalette = (byte)((spriteScanline[i].attribute & 0x30) + 0x04);
						_fgPriority = (spriteScanline[i].attribute & 0x20) == 0 ? (byte)1 : (byte)0;

						if(_fgPixel != 0)
						{
							if(i == 0) // Is this sprite zero?
								spriteZeroBeingRendered = true;

							break;
						}
					}
				}
			}


			int x = cycle - 1;
			int y = scanline;
			if (x >= 0 && x < NESConfig.NES_WIDTH && y >= 0 && y < NESConfig.NES_HEIGHT)
			{
				byte _pixel = 0x00;		// The final PIXEL
				byte _palette = 0x00;   // The final PALETTE

				if (_bgPixel == 0 && _fgPixel == 0)
				{
					// The background pixel is transparent
					// The foreground pixel is transparent
					_pixel = 0x00;
					_palette = 0x00;
				}
				else if (_bgPixel == 0 && _fgPixel > 0)
				{
					// The background pixel is transparent
					// The foreground pixel is visible
					_pixel = _fgPixel;
					_palette = _fgPalette;
				}
				else if(_bgPixel > 0 && _fgPixel == 0)
				{
					// The background pixel is visible
					// The foreground pixel is transparent
					_pixel = _bgPixel;
					_palette = _bgPalette;
				}
				else if (_bgPixel > 0 && _fgPixel > 0)
				{
					// The background pixel is visible
					// The foreground pixel is visible
					if(_fgPriority != 0)
					{
						// The sprite has priority
						_pixel = _fgPixel;
						_palette = _fgPalette;
					}
					else
					{
						// The sprite doesn't have priority
						_pixel = _bgPixel;
						_palette = _bgPalette;
					}

					// Sprite Zero Hit Detection
					if(spriteZeroHitPossible && spriteZeroBeingRendered)
					{
						if(mask[maskRenderBackground] & mask[maskRenderSprites])
						{
							if(!(mask[maskRenderBackgroundLeft] | mask[maskRenderSpritesLeft]))
							{
								if(cycle >= 9 && cycle < 258)
								{
									status[statusSpriteZeroHit] = 1;
								}
							}
							else
							{
								if (cycle >= 1 && cycle < 258)
								{
									status[statusSpriteZeroHit] = 1;
								}
							}
						}
					}
				}

				bufferScreen[y * NESConfig.NES_WIDTH + x] = GetColourFromPaletteRam(_palette, _pixel);

				// Rendering noise
				//bufferScreen[y * NESConfig.NES_WIDTH + x] = palleteScreen[(Random.Shared.Next(2) == 1) ? 0x3F : 0x30];
			}

			cycle++;

			if(cycle >= 341)
			{
				cycle = 0;
				scanline++;
				if(scanline >= 261)
				{
					scanline = -1;
					frameCompleted = true;

					UploadBuffer(textureScreenID, bufferScreen, NESConfig.NES_WIDTH, NESConfig.NES_HEIGHT);
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

		public Pixel[] GetPatternTable(byte i, byte palette)
		{
			for(ushort nTileY = 0; nTileY < 16; nTileY++)
			{
				for(ushort nTileX = 0; nTileX < 16; nTileX++)
				{
					// Convert the 2D tile coordinate into a 1D offset into the pattern
					// table memory.
					ushort nOffset = (ushort)(nTileY * 256 + nTileX * 16);

					for(ushort row = 0; row < 8; row++)
					{
						byte tile_lsb = PPURead((ushort)(i * 0x1000 + nOffset + row + 0x0000));
						byte tile_msb = PPURead((ushort)(i * 0x1000 + nOffset + row + 0x0008));

						for(ushort col = 0; col < 8; col++)
						{
							byte pixel = (byte)((tile_lsb & 0x01) << 1 | (tile_msb & 0x01));
							tile_lsb >>= 1; tile_msb >>= 1;

							int x = nTileX * 8 + (7 - col);
							int y = nTileY * 8 + row;

							Pixel colourOfBuffer = GetColourFromPaletteRam(palette, pixel);

							bufferPatternTable[i][y * 128 + x] = colourOfBuffer;
						}
					}
				}
			}

			UploadBuffer(bufferPatternTableID[i], bufferPatternTable[i], 128, 128);
			return bufferPatternTable[i];
		}

		#endregion
	}
}
