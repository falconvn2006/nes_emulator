using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace nes_emulator.src.Mappers
{
    public abstract class Mapper
    {
        protected byte nPRGBanks = 0;
        protected byte nCHRBanks = 0;

        public Mapper(byte prgBanks, byte chrBanks)
        {
            nPRGBanks = prgBanks;
            nCHRBanks = chrBanks;
        }

        public abstract bool CPUMapRead(ushort addr, ref uint mapped_addr);
        public abstract bool CPUMapWrite(ushort addr, ref uint mapped_addr, byte data);
        public abstract bool PPUMapRead(ushort addr, ref uint mapped_addr);
        public abstract bool PPUMapWrite(ushort addr, ref uint mapped_addr);
    }
}
