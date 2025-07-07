using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

using nes_emulator.src.Mappers;

namespace nes_emulator.src
{
	public class Cartridge
	{
		#region Structs and Enums

		// iNES Format header
		[StructLayout(LayoutKind.Sequential, Pack = 1)]
		struct NESHeader
		{
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
			public char[] name;
			public byte prg_rom_chunks;
			public byte chr_rom_chunks;
			public byte mapper1;
			public byte mapper2;
			public byte prg_ram_size;
			public byte tv_system1;
			public byte tv_system2;
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
			public char[] unused;
		}

		public enum MIRROR
		{
			HARDWARE,
			HORIZONTAL,
			VERTICAL,
			ONESCREEN_LO,
			ONESCREEN_HI,
		}

		#endregion

		private byte nMapperID = 0;
		private byte nPRGBanks = 0;
		private byte nCHRBanks = 0;

		private List<byte> vPRGMemory;
		private List<byte> vCHRMemory;

		private Mapper mapper;

		private MIRROR mirror = MIRROR.HORIZONTAL;

		private bool imageValid;

		public Cartridge(string filename)
		{
			NESHeader header;

			imageValid = false;

			if (File.Exists(filename))
			{
				using (FileStream stream = new FileStream(filename, FileMode.Open, FileAccess.Read))
				using (BinaryReader reader = new BinaryReader(stream))
				{
					// Reading the header
					int headerSize = Marshal.SizeOf(typeof(NESHeader));
					byte[] headerBytes = reader.ReadBytes(headerSize);

					GCHandle handle = GCHandle.Alloc(headerBytes, GCHandleType.Pinned);
					header = (NESHeader)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(NESHeader));

					handle.Free();

					// The next 512 byte is for training which is unnecessary
					if ((header.mapper1 & 0x04) != 0)
						reader.BaseStream.Seek(512, SeekOrigin.Current);

					// Determine the mapper ID
					nMapperID = (byte)(((header.mapper2 >> 4) << 4) | (header.mapper1 >> 4));
					mirror = (header.mapper1 & 0x01) != 0 ? MIRROR.VERTICAL : MIRROR.HORIZONTAL;

					// "Discover" File Format
					byte nFileType = 1;
					if ((header.mapper2 & 0x0C) == 0x08) nFileType = 2;

					if(nFileType == 0)
					{

					}

					if(nFileType == 1)
					{
						nPRGBanks = header.prg_rom_chunks;
						vPRGMemory = new List<byte>(nPRGBanks * 16384);
						vPRGMemory = reader.ReadBytes(vPRGMemory.Capacity).ToList();

						nCHRBanks = header.chr_rom_chunks;
						if(nCHRBanks == 0)
							vCHRMemory = new List<byte>(8192);
						else
							vCHRMemory = new List<byte>(nCHRBanks * 8192);
						vCHRMemory = reader.ReadBytes(vCHRMemory.Capacity).ToList();
					}

					if(nFileType == 2)
					{
						nPRGBanks = (byte)(((header.prg_ram_size & 0x07) << 8) | header.prg_rom_chunks);
						vPRGMemory = new List<byte>(nPRGBanks * 16384);
						vPRGMemory = reader.ReadBytes(vPRGMemory.Capacity).ToList();

						nCHRBanks = (byte)(((header.prg_ram_size & 0x38) << 8) | header.chr_rom_chunks);
						vCHRMemory = new List<byte>(nCHRBanks * 8192);
						vCHRMemory = reader.ReadBytes(vCHRMemory.Capacity).ToList();
					}

					if(vCHRMemory?.Count == 0)
					{
						if (nCHRBanks == 0)
							vCHRMemory.AddRange(Enumerable.Repeat((byte)0, 8192));
						else
							vCHRMemory.AddRange(Enumerable.Repeat((byte)0, nCHRBanks * 8192));
					}

					// Load appropriate mapper
					switch(nMapperID)
					{
						case 0:	mapper = new Mapper000(nPRGBanks, nCHRBanks); break;
						case 1: mapper = new Mapper001(nPRGBanks, nCHRBanks); break;
						case 2: mapper = new Mapper002(nPRGBanks, nCHRBanks); break;
						case 3: mapper = new Mapper003(nPRGBanks, nCHRBanks); break;
						case 4: mapper = new Mapper004(nPRGBanks, nCHRBanks); break;
						default: throw new NotImplementedException("The cartridge uses an unsupported type of mapper!");
					}

					imageValid = true;
					Console.WriteLine("Cartridge load successfully!");
				}
			}
			else
			{
                Console.WriteLine("Cartridge not found!");
			}
		}

		public bool CPURead(ushort addr, ref byte data)
		{
			uint mapped_addr = 0;

			if(mapper.CPUMapRead(addr, ref mapped_addr, ref data))
			{
				if(mapped_addr == 0xFFFFFFFF)
				{
					// Mapper has actually set the data value, for example cartridge based RAM
					return true;
				}
				else
				{
					// Mapper has produced an offset into cartridge bank memory
					data = vPRGMemory[Convert.ToInt32(mapped_addr)];
				}

				return true;
			}
			else
				return false;
		}

		public bool CPUWrite(ushort addr, byte data) 
		{
			uint mapped_addr = 0;

			if (mapper.CPUMapWrite(addr, ref mapped_addr, data))
			{
				if(mapped_addr == 0xFFFFFFFF)
				{
					// Mapper has actually set the data value, for example cartridge based RAM
					return true;
				}
				else
				{
					// Mapper has produced an offset into cartridge bank memory
					vPRGMemory[Convert.ToInt32(mapped_addr)] = data;
				}

				return true;
			}
			else
				return false;
		}

		public bool PPURead(ushort addr, ref byte data)
		{
			uint mapped_addr = 0;

			if (mapper.PPUMapRead(addr, ref mapped_addr))
			{
				data = vCHRMemory[Convert.ToInt32(mapped_addr)];
				return true;
			}
			else
				return false;
		}

		public bool PPUWrite(ushort addr, byte data)
		{
			uint mapped_addr = 0;

			if (mapper.PPUMapWrite(addr, ref mapped_addr))
			{
				vCHRMemory[Convert.ToInt32(mapped_addr)] = data;
				return true;
			}
			else
				return false;
		}

		public void Reset()
		{
			// Note: This does not reset the ROM contents,
			// but does reset the mapper.
			if (mapper != null)
				mapper.Reset();
		}

		public MIRROR GetMirror()
		{
			MIRROR m = mapper.GetMirror();
			if(m == MIRROR.HARDWARE)
			{
				return mirror;
			}
			else
			{
				return m;
			}
		}

		public Mapper GetMapper()
		{
			return mapper;
		}
	}
}
