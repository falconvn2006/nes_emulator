using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using static nes_emulator.src.Bus;

namespace nes_emulator.src
{
    public class CPU
    {
		#region Constants

		public const ushort PC_START_ADDRESS = 0xFFFC;
		public const ushort IRQ_PC_START_ADDRESS = 0xFFFE;
		public const ushort NMI_PC_START_ADDRESS = 0xFFFA;

		public const byte INITIAL_STACK_POINTER = 0xFD;
		public const ushort STACK_ADDRESS_HIGH_BYTE_MASK = 0x0100;

		public const byte RESET_CYCLE_COUNT = 8;
		public const byte IRQ_CYCLE_COUNT = 7;
		public const byte NMI_CYCLE_COUNT = 8;

		#endregion

		public struct INSTRUCTION
        {
            public string name;
            public Func<byte> operate;
            public Func<byte> addrmode;
            public byte cycles;

            public INSTRUCTION(string _name, Func<byte> _operate, Func<byte> _addrmode, byte cycles = 0)
            {
                name = _name;
                operate = _operate;
                addrmode = _addrmode;
                this.cycles = cycles;
            }
        }

        List<INSTRUCTION> lookup;

        [Flags]
        public enum FLAGS6502 : byte
        {
            None = 0,
            C = (1 << 0), // Carry bit
            Z = (1 << 1), // Zero
            I = (1 << 2), // Disable Interrupts
            D = (1 << 3), // Decimal Mode
            B = (1 << 4), // Break
            U = (1 << 5), // Unused
            V = (1 << 6), // Overflow
            N = (1 << 7), // Negative
        }

        public byte a = 0x00;      // Accumulator Register
        public byte x = 0x00;      // X Register
        public byte y = 0x00;      // Y Register
        public byte stkp = 0x00;   // Stack pointer (points to a location on the bus)
        public ushort pc = 0x0000; // Program counter
        public FLAGS6502 status = 0x00; // Status Register

        private ushort addr_abs = 0x0000;
        private ushort addr_rel = 0x00;
        private byte opcode = 0x00;
        private ushort temp = 0x0000;
        private byte fetched = 0x00;
        private byte cycles = 0;

        Bus bus;

        public CPU()
        {
            // Assembles the translation table. It's big, it's ugly, but it yields a convenient way
            // to emulate the 6502. There is probably a better implementation of this

            // It is 16x16 entries. This gives 256 instructions. It is arranged to that the bottom
            // 4 bits of the instruction choose the column, and the top 4 bits choose the row.

            // For convenience to get function pointers to members of this class, I'm using this
            // or else it will be much much larger :D

            // The table is one big initializer list of structs
            lookup = new List<INSTRUCTION>
            {
                    new INSTRUCTION("BRK", BRK, IMM, 7), new INSTRUCTION("ORA", ORA, IZX, 6), new INSTRUCTION("???", XXX, IMP, 2), new INSTRUCTION("???", XXX, IMP, 8), new INSTRUCTION("???", NOP, IMP, 3), new INSTRUCTION("ORA", ORA, ZP0, 3), new INSTRUCTION("ASL", ASL, ZP0, 5), new INSTRUCTION("???", XXX, IMP, 5), new INSTRUCTION("PHP", PHP, IMP, 3), new INSTRUCTION("ORA", ORA, IMM, 2), new INSTRUCTION("ASL", ASL, IMP, 2), new INSTRUCTION("???", XXX, IMP, 2), new INSTRUCTION("???", NOP, IMP, 4), new INSTRUCTION("ORA", ORA, ABS, 4), new INSTRUCTION("ASL", ASL, ABS, 6), new INSTRUCTION("???", XXX, IMP, 6),
                    new INSTRUCTION("BPL", BPL, REL, 2), new INSTRUCTION("ORA", ORA, IZY, 5), new INSTRUCTION("???", XXX, IMP, 2), new INSTRUCTION("???", XXX, IMP, 8), new INSTRUCTION("???", NOP, IMP, 4), new INSTRUCTION("ORA", ORA, ZPX, 4), new INSTRUCTION("ASL", ASL, ZPX, 6), new INSTRUCTION("???", XXX, IMP, 6), new INSTRUCTION("CLC", CLC, IMP, 2), new INSTRUCTION("ORA", ORA, ABY, 4), new INSTRUCTION("???", NOP, IMP, 2), new INSTRUCTION("???", XXX, IMP, 7), new INSTRUCTION("???", NOP, IMP, 4), new INSTRUCTION("ORA", ORA, ABX, 4), new INSTRUCTION("ASL", ASL, ABX, 7), new INSTRUCTION("???", XXX, IMP, 7),
                    new INSTRUCTION("JSR", JSR, ABS, 6), new INSTRUCTION("AND", AND, IZX, 6), new INSTRUCTION("???", XXX, IMP, 2), new INSTRUCTION("???", XXX, IMP, 8), new INSTRUCTION("BIT", BIT, ZP0, 3), new INSTRUCTION("AND", AND, ZP0, 3), new INSTRUCTION("ROL", ROL, ZP0, 5), new INSTRUCTION("???", XXX, IMP, 5), new INSTRUCTION("PLP", PLP, IMP, 4), new INSTRUCTION("AND", AND, IMM, 2), new INSTRUCTION("ROL", ROL, IMP, 2), new INSTRUCTION("???", XXX, IMP, 2), new INSTRUCTION("BIT", BIT, ABS, 4), new INSTRUCTION("AND", AND, ABS, 4), new INSTRUCTION("ROL", ROL, ABS, 6), new INSTRUCTION("???", XXX, IMP, 6),
                    new INSTRUCTION("BMI", BMI, REL, 2), new INSTRUCTION("AND", AND, IZY, 5), new INSTRUCTION("???", XXX, IMP, 2), new INSTRUCTION("???", XXX, IMP, 8), new INSTRUCTION("???", NOP, IMP, 4), new INSTRUCTION("AND", AND, ZPX, 4), new INSTRUCTION("ROL", ROL, ZPX, 6), new INSTRUCTION("???", XXX, IMP, 6), new INSTRUCTION("SEC", SEC, IMP, 2), new INSTRUCTION("AND", AND, ABY, 4), new INSTRUCTION("???", NOP, IMP, 2), new INSTRUCTION("???", XXX, IMP, 7), new INSTRUCTION("???", NOP, IMP, 4), new INSTRUCTION("AND", AND, ABX, 4), new INSTRUCTION("ROL", ROL, ABX, 7), new INSTRUCTION("???", XXX, IMP, 7),
                    new INSTRUCTION("RTI", RTI, IMP, 6), new INSTRUCTION("EOR", EOR, IZX, 6), new INSTRUCTION("???", XXX, IMP, 2), new INSTRUCTION("???", XXX, IMP, 8), new INSTRUCTION("???", NOP, IMP, 3), new INSTRUCTION("EOR", EOR, ZP0, 3), new INSTRUCTION("LSR", LSR, ZP0, 5), new INSTRUCTION("???", XXX, IMP, 5), new INSTRUCTION("PHA", PHA, IMP, 3), new INSTRUCTION("EOR", EOR, IMM, 2), new INSTRUCTION("LSR", LSR, IMP, 2), new INSTRUCTION("???", XXX, IMP, 2), new INSTRUCTION("JMP", JMP, ABS, 3), new INSTRUCTION("EOR", EOR, ABS, 4), new INSTRUCTION("LSR", LSR, ABS, 6), new INSTRUCTION("???", XXX, IMP, 6),
                    new INSTRUCTION("BVC", BVC, REL, 2), new INSTRUCTION("EOR", EOR, IZY, 5), new INSTRUCTION("???", XXX, IMP, 2), new INSTRUCTION("???", XXX, IMP, 8), new INSTRUCTION("???", NOP, IMP, 4), new INSTRUCTION("EOR", EOR, ZPX, 4), new INSTRUCTION("LSR", LSR, ZPX, 6), new INSTRUCTION("???", XXX, IMP, 6), new INSTRUCTION("CLI", CLI, IMP, 2), new INSTRUCTION("EOR", EOR, ABY, 4), new INSTRUCTION("???", NOP, IMP, 2), new INSTRUCTION("???", XXX, IMP, 7), new INSTRUCTION("???", NOP, IMP, 4), new INSTRUCTION("EOR", EOR, ABX, 4), new INSTRUCTION("LSR", LSR, ABX, 7), new INSTRUCTION("???", XXX, IMP, 7),
                    new INSTRUCTION("RTS", RTS, IMP, 6), new INSTRUCTION("ADC", ADC, IZX, 6), new INSTRUCTION("???", XXX, IMP, 2), new INSTRUCTION("???", XXX, IMP, 8), new INSTRUCTION("???", NOP, IMP, 3), new INSTRUCTION("ADC", ADC, ZP0, 3), new INSTRUCTION("ROR", ROR, ZP0, 5), new INSTRUCTION("???", XXX, IMP, 5), new INSTRUCTION("PLA", PLA, IMP, 4), new INSTRUCTION("ADC", ADC, IMM, 2), new INSTRUCTION("ROR", ROR, IMP, 2), new INSTRUCTION("???", XXX, IMP, 2), new INSTRUCTION("JMP", JMP, IND, 5), new INSTRUCTION("ADC", ADC, ABS, 4), new INSTRUCTION("ROR", ROR, ABS, 6), new INSTRUCTION("???", XXX, IMP, 6),
                    new INSTRUCTION("BVS", BVS, REL, 2), new INSTRUCTION("ADC", ADC, IZY, 5), new INSTRUCTION("???", XXX, IMP, 2), new INSTRUCTION("???", XXX, IMP, 8), new INSTRUCTION("???", NOP, IMP, 4), new INSTRUCTION("ADC", ADC, ZPX, 4), new INSTRUCTION("ROR", ROR, ZPX, 6), new INSTRUCTION("???", XXX, IMP, 6), new INSTRUCTION("SEI", SEI, IMP, 2), new INSTRUCTION("ADC", ADC, ABY, 4), new INSTRUCTION("???", NOP, IMP, 2), new INSTRUCTION("???", XXX, IMP, 7), new INSTRUCTION("???", NOP, IMP, 4), new INSTRUCTION("ADC", ADC, ABX, 4), new INSTRUCTION("ROR", ROR, ABX, 7), new INSTRUCTION("???", XXX, IMP, 7),
                    new INSTRUCTION("???", NOP, IMP, 2), new INSTRUCTION("STA", STA, IZX, 6), new INSTRUCTION("???", NOP, IMP, 2), new INSTRUCTION("???", XXX, IMP, 6), new INSTRUCTION("STY", STY, ZP0, 3), new INSTRUCTION("STA", STA, ZP0, 3), new INSTRUCTION("STX", STX, ZP0, 3), new INSTRUCTION("???", XXX, IMP, 3), new INSTRUCTION("DEY", DEY, IMP, 2), new INSTRUCTION("???", NOP, IMP, 2), new INSTRUCTION("TXA", TXA, IMP, 2), new INSTRUCTION("???", XXX, IMP, 2), new INSTRUCTION("STY", STY, ABS, 4), new INSTRUCTION("STA", STA, ABS, 4), new INSTRUCTION("STX", STX, ABS, 4), new INSTRUCTION("???", XXX, IMP, 4),
                    new INSTRUCTION("BCC", BCC, REL, 2), new INSTRUCTION("STA", STA, IZY, 6), new INSTRUCTION("???", XXX, IMP, 2), new INSTRUCTION("???", XXX, IMP, 6), new INSTRUCTION("STY", STY, ZPX, 4), new INSTRUCTION("STA", STA, ZPX, 4), new INSTRUCTION("STX", STX, ZPY, 4), new INSTRUCTION("???", XXX, IMP, 4), new INSTRUCTION("TYA", TYA, IMP, 2), new INSTRUCTION("STA", STA, ABY, 5), new INSTRUCTION("TXS", TXS, IMP, 2), new INSTRUCTION("???", XXX, IMP, 5), new INSTRUCTION("???", NOP, IMP, 5), new INSTRUCTION("STA", STA, ABX, 5), new INSTRUCTION("???", XXX, IMP, 5), new INSTRUCTION("???", XXX, IMP, 5),
                    new INSTRUCTION("LDY", LDY, IMM, 2), new INSTRUCTION("LDA", LDA, IZX, 6), new INSTRUCTION("LDX", LDX, IMM, 2), new INSTRUCTION("???", XXX, IMP, 6), new INSTRUCTION("LDY", LDY, ZP0, 3), new INSTRUCTION("LDA", LDA, ZP0, 3), new INSTRUCTION("LDX", LDX, ZP0, 3), new INSTRUCTION("???", XXX, IMP, 3), new INSTRUCTION("TAY", TAY, IMP, 2), new INSTRUCTION("LDA", LDA, IMM, 2), new INSTRUCTION("TAX", TAX, IMP, 2), new INSTRUCTION("???", XXX, IMP, 2), new INSTRUCTION("LDY", LDY, ABS, 4), new INSTRUCTION("LDA", LDA, ABS, 4), new INSTRUCTION("LDX", LDX, ABS, 4), new INSTRUCTION("???", XXX, IMP, 4),
                    new INSTRUCTION("BCS", BCS, REL, 2), new INSTRUCTION("LDA", LDA, IZY, 5), new INSTRUCTION("???", XXX, IMP, 2), new INSTRUCTION("???", XXX, IMP, 5), new INSTRUCTION("LDY", LDY, ZPX, 4), new INSTRUCTION("LDA", LDA, ZPX, 4), new INSTRUCTION("LDX", LDX, ZPY, 4), new INSTRUCTION("???", XXX, IMP, 4), new INSTRUCTION("CLV", CLV, IMP, 2), new INSTRUCTION("LDA", LDA, ABY, 4), new INSTRUCTION("TSX", TSX, IMP, 2), new INSTRUCTION("???", XXX, IMP, 4), new INSTRUCTION("LDY", LDY, ABX, 4), new INSTRUCTION("LDA", LDA, ABX, 4), new INSTRUCTION("LDX", LDX, ABY, 4), new INSTRUCTION("???", XXX, IMP, 4),
                    new INSTRUCTION("CPY", CPY, IMM, 2), new INSTRUCTION("CMP", CMP, IZX, 6), new INSTRUCTION("???", NOP, IMP, 2), new INSTRUCTION("???", XXX, IMP, 8), new INSTRUCTION("CPY", CPY, ZP0, 3), new INSTRUCTION("CMP", CMP, ZP0, 3), new INSTRUCTION("DEC", DEC, ZP0, 5), new INSTRUCTION("???", XXX, IMP, 5), new INSTRUCTION("INY", INY, IMP, 2), new INSTRUCTION("CMP", CMP, IMM, 2), new INSTRUCTION("DEX", DEX, IMP, 2), new INSTRUCTION("???", XXX, IMP, 2), new INSTRUCTION("CPY", CPY, ABS, 4), new INSTRUCTION("CMP", CMP, ABS, 4), new INSTRUCTION("DEC", DEC, ABS, 6), new INSTRUCTION("???", XXX, IMP, 6),
                    new INSTRUCTION("BNE", BNE, REL, 2), new INSTRUCTION("CMP", CMP, IZY, 5), new INSTRUCTION("???", XXX, IMP, 2), new INSTRUCTION("???", XXX, IMP, 8), new INSTRUCTION("???", NOP, IMP, 4), new INSTRUCTION("CMP", CMP, ZPX, 4), new INSTRUCTION("DEC", DEC, ZPX, 6), new INSTRUCTION("???", XXX, IMP, 6), new INSTRUCTION("CLD", CLD, IMP, 2), new INSTRUCTION("CMP", CMP, ABY, 4), new INSTRUCTION("NOP", NOP, IMP, 2), new INSTRUCTION("???", XXX, IMP, 7), new INSTRUCTION("???", NOP, IMP, 4), new INSTRUCTION("CMP", CMP, ABX, 4), new INSTRUCTION("DEC", DEC, ABX, 7), new INSTRUCTION("???", XXX, IMP, 7),
                    new INSTRUCTION("CPX", CPX, IMM, 2), new INSTRUCTION("SBC", SBC, IZX, 6), new INSTRUCTION("???", NOP, IMP, 2), new INSTRUCTION("???", XXX, IMP, 8), new INSTRUCTION("CPX", CPX, ZP0, 3), new INSTRUCTION("SBC", SBC, ZP0, 3), new INSTRUCTION("INC", INC, ZP0, 5), new INSTRUCTION("???", XXX, IMP, 5), new INSTRUCTION("INX", INX, IMP, 2), new INSTRUCTION("SBC", SBC, IMM, 2), new INSTRUCTION("NOP", NOP, IMP, 2), new INSTRUCTION("???", SBC, IMP, 2), new INSTRUCTION("CPX", CPX, ABS, 4), new INSTRUCTION("SBC", SBC, ABS, 4), new INSTRUCTION("INC", INC, ABS, 6), new INSTRUCTION("???", XXX, IMP, 6),
                    new INSTRUCTION("BEQ", BEQ, REL, 2), new INSTRUCTION("SBC", SBC, IZY, 5), new INSTRUCTION("???", XXX, IMP, 2), new INSTRUCTION("???", XXX, IMP, 8), new INSTRUCTION("???", NOP, IMP, 4), new INSTRUCTION("SBC", SBC, ZPX, 4), new INSTRUCTION("INC", INC, ZPX, 6), new INSTRUCTION("???", XXX, IMP, 6), new INSTRUCTION("SED", SED, IMP, 2), new INSTRUCTION("SBC", SBC, ABY, 4), new INSTRUCTION("NOP", NOP, IMP, 2), new INSTRUCTION("???", XXX, IMP, 7), new INSTRUCTION("???", NOP, IMP, 4), new INSTRUCTION("SBC", SBC, ABX, 4), new INSTRUCTION("INC", INC, ABX, 7), new INSTRUCTION("???", XXX, IMP, 7)
            };
        }

        public void ConnectBus(Bus _bus)
        {
            this.bus = _bus;
        }

        private void Write(ushort addr, byte data)
        {
            bus.CPUWrite(addr, data);
        }

        private byte Read(ushort addr)
        {
            return bus.CPURead(addr, false);
        }

        #region Addressing Modes

        ///////////////////////////////////////////////////////////////////////////////
        // ADDRESSING MODES

        // The 6502 can address between 0x0000 - 0xFFFF. The high byte is often referred
        // to as the "page", and the low byte is the offset into that page. This implies
        // there are 256 pages, each containing 256 bytes.
        //
        // Several addressing modes have the potential to require an additional clock
        // cycle if they cross a page boundary. This is combined with several instructions
        // that enable this additional clock cycle. So each addressing function returns
        // a flag saying it has potential, as does each instruction. If both instruction
        // and address function return 1, then an additional clock cycle is required.


        // Address Mode: Implied
        // There is no additional data required for this instruction. The instruction
        // does something very simple like like sets a status bit. However, we will
        // target the accumulator, for instructions like PHA
        private byte IMP()
        {
            fetched = a;
            return 0;
        }


        // Address Mode: Immediate
        // The instruction expects the next byte to be used as a value, so we'll prep
        // the read address to point to the next byte
        private byte IMM()
        {
            addr_abs = pc++;
            return 0;
        }



        // Address Mode: Zero Page
        // To save program bytes, zero page addressing allows you to absolutely address
        // a location in first 0xFF bytes of address range. Clearly this only requires
        // one byte instead of the usual two.
        private byte ZP0()
        {
            addr_abs = Read(pc);
            pc++;
            addr_abs &= 0x00FF;
            return 0;
        }



        // Address Mode: Zero Page with X Offset
        // Fundamentally the same as Zero Page addressing, but the contents of the X Register
        // is added to the supplied single byte address. This is useful for iterating through
        // ranges within the first page.
        private byte ZPX()
        {
            addr_abs = (ushort)(ReadPc() + x);
            addr_abs &= 0x00FF;
            return 0;
        }


        // Address Mode: Zero Page with Y Offset
        // Same as above but uses Y Register for offset
        private byte ZPY()
        {
            addr_abs = (ushort)(ReadPc() + y);
            addr_abs &= 0x00FF;
            return 0;
        }


        // Address Mode: Relative
        // This address mode is exclusive to branch instructions. The address
        // must reside within -128 to +127 of the branch instruction, i.e.
        // you cant directly branch to any address in the addressable range.
        private byte REL()
        {
            addr_rel = ReadPc();
            if ((addr_rel & 0x80) != 0)
                addr_rel |= 0xFF00;
            return 0;
        }


        // Address Mode: Absolute 
        // A full 16-bit address is loaded and used
        private byte ABS()
        {
            addr_abs = ReadPcAsAddress();

            return 0;
        }


        // Address Mode: Absolute with X Offset
        // Fundamentally the same as absolute addressing, but the contents of the X Register
        // is added to the supplied two byte address. If the resulting address changes
        // the page, an additional clock cycle is required
        private byte ABX()
        {
            ushort beforeOffset = ReadPcAsAddress();
            addr_abs = (ushort)(beforeOffset + x);

            if ((addr_abs & 0xFF00) != (beforeOffset & 0xFF00))
                return 1;
            else
                return 0;
        }


        // Address Mode: Absolute with Y Offset
        // Fundamentally the same as absolute addressing, but the contents of the Y Register
        // is added to the supplied two byte address. If the resulting address changes
        // the page, an additional clock cycle is required
        private byte ABY()
        {
            ushort beforeOffset = ReadPcAsAddress();
            addr_abs = (ushort)(beforeOffset + y);

            if ((addr_abs & 0xFF00) != (beforeOffset & 0xFF00))
                return 1;
            else
                return 0;
        }

        // Note: The next 3 address modes use indirection (aka Pointers!)

        // Address Mode: Indirect
        // The supplied 16-bit address is read to get the actual 16-bit address. This is
        // instruction is unusual in that it has a bug in the hardware! To emulate its
        // function accurately, we also need to emulate this bug. If the low byte of the
        // supplied address is 0xFF, then to read the high byte of the actual address
        // we need to cross a page boundary. This doesnt actually work on the chip as 
        // designed, instead it wraps back around in the same page, yielding an 
        // invalid actual address
        private byte IND()
        { 
            ushort ptr = ReadPcAsAddress();

            if ((ptr & 0x00FF) == 0x00FF) // Simulate page boundary hardware bug
            {
                addr_abs = (ushort)((Read((ushort)(ptr & 0xFF00)) << 8) | Read((ushort)(ptr + 0)));
            }
            else // Behave normally
            {
                addr_abs = (ushort)((Read((ushort)(ptr + 1)) << 8) | Read((ushort)(ptr + 0)));
            }

            return 0;
        }


        // Address Mode: Indirect X
        // The supplied 8-bit address is offset by X Register to index
        // a location in page 0x00. The actual 16-bit address is read 
        // from this location
        private byte IZX()
        {
            byte t = ReadPc();

            ushort lo = Read((byte)(t + x));
            ushort hi = Read((byte)(t + x + 1));

            addr_abs = (ushort)((hi << 8) | lo);

            return 0;
        }


        // Address Mode: Indirect Y
        // The supplied 8-bit address indexes a location in page 0x00. From 
        // here the actual 16-bit address is read, and the contents of
        // Y Register is added to it to offset it. If the offset causes a
        // change in page then an additional clock cycle is required.
        private byte IZY()
        {
            byte t = ReadPc();

            byte lo = Read((ushort)(t & 0x00FF));
			// The high byte read is limited to an address of 8 bit range in this specific case !
			byte hi = Read((ushort)((t + 1) & 0x00FF));

            addr_abs = (ushort)((hi << 8) | lo);
            addr_abs += y;

            if ((addr_abs & 0xFF00) != (hi << 8))
                return 1;
            else
                return 0;
        }



        // This function sources the data used by the instruction into 
        // a convenient numeric variable. Some instructions dont have to 
        // fetch data as the source is implied by the instruction. For example
        // "INX" increments the X register. There is no additional data
        // required. For all other addressing modes, the data resides at 
        // the location held within addr_abs, so it is read from there. 
        // Immediate adress mode exploits this slightly, as that has
        // set addr_abs = pc + 1, so it fetches the data from the
        // next byte for example "LDA $FF" just loads the accumulator with
        // 256, i.e. no far reaching memory fetch is required. "fetched"
        // is a variable global to the CPU, and is set by calling this 
        // function. It also returns it for convenience.
        private byte fetch()
        {
            if (lookup[opcode].addrmode != IMP)
                fetched = Read(addr_abs);
            return fetched;
        }


        ///////////////////////////////////////////////////////////////////////////////
        // INSTRUCTION IMPLEMENTATIONS

        // Note: Ive started with the two most complicated instructions to emulate, which
        // ironically is addition and subtraction! Ive tried to include a detailed 
        // explanation as to why they are so complex, yet so fundamental. Im also NOT
        // going to do this through the explanation of 1 and 2's complement.

        // Instruction: Add with Carry In
        // Function:    A = A + M + C
        // Flags Out:   C, V, N, Z
        //
        // Explanation:
        // The purpose of this function is to add a value to the accumulator and a carry bit. If
        // the result is > 255 there is an overflow setting the carry bit. Ths allows you to
        // chain together ADC instructions to add numbers larger than 8-bits. This in itself is
        // simple, however the 6502 supports the concepts of Negativity/Positivity and Signed Overflow.
        //
        // 10000100 = 128 + 4 = 132 in normal circumstances, we know this as unsigned and it allows
        // us to represent numbers between 0 and 255 (given 8 bits). The 6502 can also interpret 
        // this word as something else if we assume those 8 bits represent the range -128 to +127,
        // i.e. it has become signed.
        //
        // Since 132 > 127, it effectively wraps around, through -128, to -124. This wraparound is
        // called overflow, and this is a useful to know as it indicates that the calculation has
        // gone outside the permissable range, and therefore no longer makes numeric sense.
        //
        // Note the implementation of ADD is the same in binary, this is just about how the numbers
        // are represented, so the word 10000100 can be both -124 and 132 depending upon the 
        // context the programming is using it in. We can prove this!
        //
        //  10000100 =  132  or  -124
        // +00010001 = + 17      + 17
        //  ========    ===       ===     See, both are valid additions, but our interpretation of
        //  10010101 =  149  or  -107     the context changes the value, not the hardware!
        //
        // In principle under the -128 to 127 range:
        // 10000000 = -128, 11111111 = -1, 00000000 = 0, 00000000 = +1, 01111111 = +127
        // therefore negative numbers have the most significant set, positive numbers do not
        //
        // To assist us, the 6502 can set the overflow flag, if the result of the addition has
        // wrapped around. V <- ~(A^M) & A^(A+M+C) :D lol, let's work out why!
        //
        // Let's suppose we have A = 30, M = 10 and C = 0
        //          A = 30 = 00011110
        //          M = 10 = 00001010+
        //     RESULT = 40 = 00101000
        //
        // Here we have not gone out of range. The resulting significant bit has not changed.
        // So let's make a truth table to understand when overflow has occurred. Here I take
        // the MSB of each component, where R is RESULT.
        //
        // A  M  R | V | A^R | A^M |~(A^M) | 
        // 0  0  0 | 0 |  0  |  0  |   1   |
        // 0  0  1 | 1 |  1  |  0  |   1   |
        // 0  1  0 | 0 |  0  |  1  |   0   |
        // 0  1  1 | 0 |  1  |  1  |   0   |  so V = ~(A^M) & (A^R)
        // 1  0  0 | 0 |  1  |  1  |   0   |
        // 1  0  1 | 0 |  0  |  1  |   0   |
        // 1  1  0 | 1 |  1  |  0  |   1   |
        // 1  1  1 | 0 |  0  |  0  |   1   |
        //
        // We can see how the above equation calculates V, based on A, M and R. V was chosen
        // based on the following hypothesis:
        //       Positive Number + Positive Number = Negative Result -> Overflow
        //       Negative Number + Negative Number = Positive Result -> Overflow
        //       Positive Number + Negative Number = Either Result -> Cannot Overflow
        //       Positive Number + Positive Number = Positive Result -> OK! No Overflow
        //       Negative Number + Negative Number = Negative Result -> OK! NO Overflow

        private byte ADC()
        {
            // Grab the data that we are adding to the accumulator
            fetch();

            // Add is performed in 16-bit domain for emulation to capture any
            // carry bit, which will exist in bit 8 of the 16-bit word
            temp = (ushort)(a + fetched + (status.HasFlag(FLAGS6502.C) ? 1 : 0));

            // The carry flag out exists in the high byte bit 0
            SetFlag(FLAGS6502.C, temp > 255);

            // The Zero flag is set if the result is 0
            SetFlag(FLAGS6502.Z, (temp & 0x00FF) == 0);

            // The signed Overflow flag is set based on all that up there! :D
            SetFlag(FLAGS6502.V, ((~(a ^ fetched) & (a ^ temp)) & 0x0080) != 0);

            // The negative flag is set to the most significant bit of the result
            SetFlag(FLAGS6502.N, (temp & 0x80) != 0);

            // Load the result into the accumulator (it's 8-bit dont forget!)
            a = (byte)(temp & 0x00FF);

            // This instruction has the potential to require an additional clock cycle
            return 1;
        }


        // Instruction: Subtraction with Borrow In
        // Function:    A = A - M - (1 - C)
        // Flags Out:   C, V, N, Z
        //
        // Explanation:
        // Given the explanation for ADC above, we can reorganize our data
        // to use the same computation for addition, for subtraction by multiplying
        // the data by -1, i.e. make it negative
        //
        // A = A - M - (1 - C)  ->  A = A + -1 * (M - (1 - C))  ->  A = A + (-M + 1 + C)
        //
        // To make a signed positive number negative, we can invert the bits and add 1
        // (OK, I lied, a little bit of 1 and 2s complement :P)
        //
        //  5 = 00000101
        // -5 = 11111010 + 00000001 = 11111011 (or 251 in our 0 to 255 range)
        //
        // The range is actually unimportant, because if I take the value 15, and add 251
        // to it, given we wrap around at 256, the result is 10, so it has effectively 
        // subtracted 5, which was the original intention. (15 + 251) % 256 = 10
        //
        // Note that the equation above used (1-C), but this got converted to + 1 + C.
        // This means we already have the +1, so all we need to do is invert the bits
        // of M, the data(!) therfore we can simply add, exactly the same way we did 
        // before.

        private byte SBC()
        {
            fetch();

            // Operating in 16-bit domain to capture carry out

            // We can invert the bottom 8 bits with bitwise xor
            ushort value = (ushort)(fetched ^ 0x00FF);

            // Notice this is exactly the same as addition from here!
            temp = (ushort)(a + value + (status.HasFlag(FLAGS6502.C) ? 1 : 0));
            SetFlag(FLAGS6502.C, (temp & 0xFF00) != 0);
            SetFlag(FLAGS6502.Z, (temp & 0x00FF) == 0);
            SetFlag(FLAGS6502.V, (ushort)((temp ^ a) & (temp ^ value) & 0x0080) != 0);
            SetFlag(FLAGS6502.N, (temp & 0x80) != 0);
            a = (byte)(temp & 0x00FF);
            return 1;
        }

        // OK! Complicated operations are done! the following are much simpler
        // and conventional. The typical order of events is:
        // 1) Fetch the data you are working with
        // 2) Perform calculation
        // 3) Store the result in desired place
        // 4) Set Flags of the status register
        // 5) Return if instruction has potential to require additional 
        //    clock cycle


        // Instruction: Bitwise Logic AND
        // Function:    A = A & M
        // Flags Out:   N, Z
        private byte AND()
        {
            fetch();
            a = (byte)(a & fetched);
            SetFlag(FLAGS6502.Z, a == 0x00);
            SetFlag(FLAGS6502.N, (a & 0x80) != 0);
            return 1;
        }


        // Instruction: Arithmetic Shift Left
        // Function:    A = C <- (A << 1) <- 0
        // Flags Out:   N, Z, C
        private byte ASL()
        {
            fetch();
            temp = (ushort)((ushort)fetched << 1);
            SetFlag(FLAGS6502.C, (temp & 0xFF00) > 0);
            SetFlag(FLAGS6502.Z, (temp & 0x00FF) == 0x00);
            SetFlag(FLAGS6502.N, (temp & 0x80) != 0);
            if (lookup[opcode].addrmode == IMP)
                a = (byte)(temp & 0x00FF);
            else
                Write(addr_abs, (byte)(temp & 0x00FF));
            return 0;
        }


        // Instruction: Branch if Carry Clear
        // Function:    if(C == 0) pc = address 
        private byte BCC()
        {
            if (!status.HasFlag(FLAGS6502.C))
            {
                cycles++;
                addr_abs = (ushort)(pc + addr_rel);

                if ((addr_abs & 0xFF00) != (pc & 0xFF00))
                    cycles++;

                pc = addr_abs;
            }
            return 0;
        }


        // Instruction: Branch if Carry Set
        // Function:    if(C == 1) pc = address
        private byte BCS()
        {
            if (status.HasFlag(FLAGS6502.C))
            {
                cycles++;
                addr_abs = (ushort)(pc + addr_rel);

                if ((addr_abs & 0xFF00) != (pc & 0xFF00))
                    cycles++;

                pc = addr_abs;
            }
            return 0;
        }


        // Instruction: Branch if Equal
        // Function:    if(Z == 1) pc = address
        private byte BEQ()
        {
            if (status.HasFlag(FLAGS6502.Z))
            {
                cycles++;
                addr_abs = (ushort)(pc + addr_rel);

                if ((addr_abs & 0xFF00) != (pc & 0xFF00))
                    cycles++;

                pc = addr_abs;
            }
            return 0;
        }

        private byte BIT()
        {
            fetch();
            temp = (ushort)(a & fetched);
            SetFlag(FLAGS6502.Z, (temp & 0x00FF) == 0x00);
            SetFlag(FLAGS6502.N, (fetched & (1 << 7)) != 0);
            SetFlag(FLAGS6502.V, (fetched & (1 << 6)) != 0);
            return 0;
        }


        // Instruction: Branch if Negative
        // Function:    if(N == 1) pc = address
        private byte BMI()
        {
            if (status.HasFlag(FLAGS6502.N))
            {
                cycles++;
                addr_abs = (ushort)(pc + addr_rel);

                if ((addr_abs & 0xFF00) != (pc & 0xFF00))
                    cycles++;

                pc = addr_abs;
            }
            return 0;
        }


        // Instruction: Branch if Not Equal
        // Function:    if(Z == 0) pc = address
        private byte BNE()
        {
            if (!status.HasFlag(FLAGS6502.Z))
            {
                cycles++;
                addr_abs = (ushort)(pc + addr_rel);

                if ((addr_abs & 0xFF00) != (pc & 0xFF00))
                    cycles++;

                pc = addr_abs;
            }
            return 0;
        }


        // Instruction: Branch if Positive
        // Function:    if(N == 0) pc = address
        private byte BPL()
        {
            if (!status.HasFlag(FLAGS6502.N))
            {
                cycles++;
                addr_abs = (ushort)(pc + addr_rel);

                if ((addr_abs & 0xFF00) != (pc & 0xFF00))
                    cycles++;

                pc = addr_abs;
            }
            return 0;
        }

        // Instruction: Break
        // Function:    Program Sourced Interrupt
        private byte BRK()
        {
            pc++;

            PushPcOnStack();

            SetFlag(FLAGS6502.B, true);
            PushStack((byte)status);
            SetFlag(FLAGS6502.B, false);

			// After writing to the stack, set the Interupt flag to 1
			// to prevent other interrupts
			SetFlag(FLAGS6502.I, true);

			pc = ReadAsAddress(IRQ_PC_START_ADDRESS);
            return 0;
        }


        // Instruction: Branch if Overflow Clear
        // Function:    if(V == 0) pc = address
        private byte BVC()
        {
            if (!status.HasFlag(FLAGS6502.V))
            {
                cycles++;
                addr_abs = (ushort)(pc + addr_rel);

                if ((addr_abs & 0xFF00) != (pc & 0xFF00))
                    cycles++;

                pc = addr_abs;
            }
            return 0;
        }


        // Instruction: Branch if Overflow Set
        // Function:    if(V == 1) pc = address
        private byte BVS()
        {
            if (status.HasFlag(FLAGS6502.V))
            {
                cycles++;
                addr_abs = (ushort)(pc + addr_rel);

                if ((addr_abs & 0xFF00) != (pc & 0xFF00))
                    cycles++;

                pc = addr_abs;
            }
            return 0;
        }


        // Instruction: Clear Carry Flag
        // Function:    C = 0
        private byte CLC()
        {
            SetFlag(FLAGS6502.C, false);
            return 0;
        }


        // Instruction: Clear Decimal Flag
        // Function:    D = 0
        private byte CLD()
        {
            SetFlag(FLAGS6502.D, false);
            return 0;
        }


        // Instruction: Disable Interrupts / Clear Interrupt Flag
        // Function:    I = 0
        private byte CLI()
        {
            SetFlag(FLAGS6502.I, false);
            return 0;
        }


        // Instruction: Clear Overflow Flag
        // Function:    V = 0
        private byte CLV()
        {
            SetFlag(FLAGS6502.V, false);
            return 0;
        }

        // Instruction: Compare Accumulator
        // Function:    C <- A >= M      Z <- (A - M) == 0
        // Flags Out:   N, C, Z
        private byte CMP()
        {
            fetch();
            temp = (ushort)(a - fetched);
            SetFlag(FLAGS6502.C, a >= fetched);
            SetFlag(FLAGS6502.Z, (temp & 0x00FF) == 0x0000);
            SetFlag(FLAGS6502.N, (temp & 0x0080) != 0);
            return 1;
        }


        // Instruction: Compare X Register
        // Function:    C <- X >= M      Z <- (X - M) == 0
        // Flags Out:   N, C, Z
        private byte CPX()
        {
            fetch();
            temp = (ushort)(x - fetched);
            SetFlag(FLAGS6502.C, x >= fetched);
            SetFlag(FLAGS6502.Z, (temp & 0x00FF) == 0x0000);
            SetFlag(FLAGS6502.N, (temp & 0x0080) != 0);
            return 0;
        }


        // Instruction: Compare Y Register
        // Function:    C <- Y >= M      Z <- (Y - M) == 0
        // Flags Out:   N, C, Z
        private byte CPY()
        {
            fetch();
            temp = (ushort)(y - fetched);
            SetFlag(FLAGS6502.C, y >= fetched);
            SetFlag(FLAGS6502.Z, (temp & 0x00FF) == 0x0000);
            SetFlag(FLAGS6502.N, (temp & 0x0080) != 0);
            return 0;
        }


        // Instruction: Decrement Value at Memory Location
        // Function:    M = M - 1
        // Flags Out:   N, Z
        private byte DEC()
        {
            fetch();
            temp = (ushort)(fetched - 1);
            Write(addr_abs, (byte)(temp & 0x00FF));
            SetFlag(FLAGS6502.Z, (temp & 0x00FF) == 0x0000);
            SetFlag(FLAGS6502.N, (temp & 0x0080) != 0);
            return 0;
        }


        // Instruction: Decrement X Register
        // Function:    X = X - 1
        // Flags Out:   N, Z
        private byte DEX()
        {
            x--;
            SetFlag(FLAGS6502.Z, x == 0x00);
            SetFlag(FLAGS6502.N, (x & 0x80) != 0);
            return 0;
        }


        // Instruction: Decrement Y Register
        // Function:    Y = Y - 1
        // Flags Out:   N, Z
        private byte DEY()
        {
            y--;
            SetFlag(FLAGS6502.Z, y == 0x00);
            SetFlag(FLAGS6502.N, (y & 0x80) != 0);
            return 0;
        }


        // Instruction: Bitwise Logic XOR
        // Function:    A = A xor M
        // Flags Out:   N, Z
        private byte EOR()
        {
            fetch();
            a = (byte)(a ^ fetched);
            SetFlag(FLAGS6502.Z, a == 0x00);
            SetFlag(FLAGS6502.N, (a & 0x80) != 0);
            return 1;
        }


        // Instruction: Increment Value at Memory Location
        // Function:    M = M + 1
        // Flags Out:   N, Z
        private byte INC()
        {
            fetch();
            temp = (ushort)(fetched + 1);
            Write(addr_abs, (byte)(temp & 0x00FF));
            SetFlag(FLAGS6502.Z, (temp & 0x00FF) == 0x0000);
            SetFlag(FLAGS6502.N, (temp & 0x0080) != 0);
            return 0;
        }


        // Instruction: Increment X Register
        // Function:    X = X + 1
        // Flags Out:   N, Z
        private byte INX()
        {
            x++;
            SetFlag(FLAGS6502.Z, x == 0x00);
            SetFlag(FLAGS6502.N, (x & 0x80) != 0);
            return 0;
        }


        // Instruction: Increment Y Register
        // Function:    Y = Y + 1
        // Flags Out:   N, Z
        private byte INY()
        {
            y++;
            SetFlag(FLAGS6502.Z, y == 0x00);
            SetFlag(FLAGS6502.N, (y & 0x80) != 0);
            return 0;
        }


        // Instruction: Jump To Location
        // Function:    pc = address
        private byte JMP()
        {
            pc = addr_abs;
            return 0;
        }


        // Instruction: Jump To Sub-Routine
        // Function:    Push current pc to stack, pc = address
        private byte JSR()
        {
            pc--;

            PushPcOnStack();

            pc = addr_abs;
            return 0;
        }


        // Instruction: Load The Accumulator
        // Function:    A = M
        // Flags Out:   N, Z
        private byte LDA()
        {
            fetch();
            a = fetched;
            SetFlag(FLAGS6502.Z, a == 0x00);
            SetFlag(FLAGS6502.N, (a & 0x80) != 0);
            return 1;
        }


        // Instruction: Load The X Register
        // Function:    X = M
        // Flags Out:   N, Z
        private byte LDX()
        {
            fetch();
            x = fetched;
            SetFlag(FLAGS6502.Z, x == 0x00);
            SetFlag(FLAGS6502.N, (x & 0x80) != 0);
            return 1;
        }


        // Instruction: Load The Y Register
        // Function:    Y = M
        // Flags Out:   N, Z
        private byte LDY()
        {
            fetch();
            y = fetched;
            SetFlag(FLAGS6502.Z, y == 0x00);
            SetFlag(FLAGS6502.N, (y & 0x80) != 0);
            return 1;
        }

        private byte LSR()
        {
            fetch();
            SetFlag(FLAGS6502.C, (fetched & 0x0001) != 0);
            temp = (ushort)(fetched >> 1);
            SetFlag(FLAGS6502.Z, (temp & 0x00FF) == 0x0000);
            SetFlag(FLAGS6502.N, (temp & 0x0080) != 0);
            if (lookup[opcode].addrmode == IMP)
                a = (byte)(temp & 0x00FF);
            else
                Write(addr_abs, (byte)(temp & 0x00FF));
            return 0;
        }

        public byte NOP()
        {
            // Sadly not all NOPs are equal, Ive added a few here
            // based on https://wiki.nesdev.com/w/index.php/CPU_unofficial_opcodes
            // and will add more based on game compatibility, and ultimately
            // I'd like to cover all illegal opcodes too
            switch (opcode)
            {
                case 0x1C:
                case 0x3C:
                case 0x5C:
                case 0x7C:
                case 0xDC:
                case 0xFC:
                    return 1;
                    break;
            }
            return 0;
        }


        // Instruction: Bitwise Logic OR
        // Function:    A = A | M
        // Flags Out:   N, Z
        private byte ORA()
        {
            fetch();
            a = (byte)(a | fetched);
            SetFlag(FLAGS6502.Z, a == 0x00);
            SetFlag(FLAGS6502.N, (a & 0x80) != 0);
            return 1;
        }


        // Instruction: Push Accumulator to Stack
        // Function:    A -> stack
        private byte PHA()
        {
            PushStack(a);
            return 0;
        }


        // Instruction: Push Status Register to Stack
        // Function:    status -> stack
        // Note:        Break flag is set to 1 before push
        private byte PHP()
        {
            PushStack((byte)(status | FLAGS6502.B | FLAGS6502.U));
            SetFlag(FLAGS6502.B, false);
            SetFlag(FLAGS6502.U, false);
            return 0;
        }


        // Instruction: Pop Accumulator off Stack
        // Function:    A <- stack
        // Flags Out:   N, Z
        private byte PLA()
        {
            a = PopStack();
            SetFlag(FLAGS6502.Z, a == 0x00);
            SetFlag(FLAGS6502.N, (a & 0x80) != 0);
            return 0;
        }


        // Instruction: Pop Status Register off Stack
        // Function:    Status <- stack
        private byte PLP()
        {
            status = (FLAGS6502)PopStack();
            SetFlag(FLAGS6502.U, true);
            return 0;
        }

        private byte ROL()
        {
            fetch();
            temp = (ushort)((ushort)(fetched << 1) | (status.HasFlag(FLAGS6502.C) ? 1 : 0));
            SetFlag(FLAGS6502.C, (temp & 0xFF00) != 0);
            SetFlag(FLAGS6502.Z, (temp & 0x00FF) == 0x0000);
            SetFlag(FLAGS6502.N, (temp & 0x0080) != 0);
            if (lookup[opcode].addrmode == IMP)
                a = (byte)(temp & 0x00FF);
            else
                Write(addr_abs, (byte)(temp & 0x00FF));
            return 0;
        }

        private byte ROR()
        {
            fetch();
            temp = (ushort)(((status.HasFlag(FLAGS6502.C) ? 1 : 0) << 7) | (fetched >> 1));
            SetFlag(FLAGS6502.C, (fetched & 0x01) != 0);
            SetFlag(FLAGS6502.Z, (temp & 0x00FF) == 0x00);
            SetFlag(FLAGS6502.N, (temp & 0x0080) != 0);
            if (lookup[opcode].addrmode == IMP)
                a = (byte)(temp & 0x00FF);
            else
                Write(addr_abs, (byte)(temp & 0x00FF));
            return 0;
        }

        private byte RTI()
        {
            status = (FLAGS6502)PopStack();
            status &= ~FLAGS6502.B;
            status &= ~FLAGS6502.U;

            PopStackToPc();
            return 0;
        }

        private byte RTS()
        {
            PopStackToPc();

            pc++;
            return 0;
        }




        // Instruction: Set Carry Flag
        // Function:    C = 1
        private byte SEC()
        {
            SetFlag(FLAGS6502.C, true);
            return 0;
        }


        // Instruction: Set Decimal Flag
        // Function:    D = 1
        private byte SED()
        {
            SetFlag(FLAGS6502.D, true);
            return 0;
        }


        // Instruction: Set Interrupt Flag / Enable Interrupts
        // Function:    I = 1
        private byte SEI()
        {
            SetFlag(FLAGS6502.I, true);
            return 0;
        }


        // Instruction: Store Accumulator at Address
        // Function:    M = A
        private byte STA()
        {
            Write(addr_abs, a);
            return 0;
        }


        // Instruction: Store X Register at Address
        // Function:    M = X
        private byte STX()
        {
            Write(addr_abs, x);
            return 0;
        }


        // Instruction: Store Y Register at Address
        // Function:    M = Y
        private byte STY()
        {
            Write(addr_abs, y);
            return 0;
        }


        // Instruction: Transfer Accumulator to X Register
        // Function:    X = A
        // Flags Out:   N, Z
        private byte TAX()
        {
            x = a;
            SetFlag(FLAGS6502.Z, x == 0x00);
            SetFlag(FLAGS6502.N, (x & 0x80) != 0);
            return 0;
        }


        // Instruction: Transfer Accumulator to Y Register
        // Function:    Y = A
        // Flags Out:   N, Z
        private byte TAY()
        {
            y = a;
            SetFlag(FLAGS6502.Z, y == 0x00);
            SetFlag(FLAGS6502.N, (y & 0x80) != 0);
            return 0;
        }


        // Instruction: Transfer Stack Pointer to X Register
        // Function:    X = stack pointer
        // Flags Out:   N, Z
        private byte TSX()
        {
            x = stkp;
            SetFlag(FLAGS6502.Z, x == 0x00);
            SetFlag(FLAGS6502.N, (x & 0x80) != 0);
            return 0;
        }


        // Instruction: Transfer X Register to Accumulator
        // Function:    A = X
        // Flags Out:   N, Z
        private byte TXA()
        {
            a = x;
            SetFlag(FLAGS6502.Z, a == 0x00);
            SetFlag(FLAGS6502.N, (a & 0x80) != 0);
            return 0;
        }


        // Instruction: Transfer X Register to Stack Pointer
        // Function:    stack pointer = X
        private byte TXS()
        {
            stkp = x;
            return 0;
        }


        // Instruction: Transfer Y Register to Accumulator
        // Function:    A = Y
        // Flags Out:   N, Z
        private byte TYA()
        {
            a = y;
            SetFlag(FLAGS6502.Z, a == 0x00);
            SetFlag(FLAGS6502.N, (a & 0x80) != 0);
            return 0;
        }


        // This function captures illegal opcodes
        private byte XXX()
        {
            return 0;
        }

        #endregion

        #region External Inputs

        public void Clock()
        {
            if (cycles == 0)
            {
                opcode = (byte)Read(pc);

                status |= FLAGS6502.U;

                pc++;

                cycles = lookup[opcode].cycles;

                byte additional_cycle1 = lookup[opcode].addrmode();
                byte additional_cycle2 = lookup[opcode].operate();

                cycles += (byte)(additional_cycle1 & additional_cycle2);

				status |= FLAGS6502.U;
			}

            cycles--;
        }

        public void Reset()
        {
            addr_abs = 0xFFFC;
            ushort lo = Read((ushort)(addr_abs + 0));
            ushort hi = Read((ushort)(addr_abs + 1));

            pc = (ushort)((hi << 8) | lo);

            a = 0;
            x = 0;
            y = 0;
            stkp = 0xFD;
            status = FLAGS6502.U;

            addr_rel = 0x0000;
            addr_abs = 0x0000;
            fetched = 0x00;

            cycles = 8;
        }

        public void IRQ()
        {
            if (!status.HasFlag(FLAGS6502.I))
            {
                PushPcOnStack();

                status |= FLAGS6502.B;
                status |= FLAGS6502.U;
                PushStack((byte)status);

                status |= FLAGS6502.I;

                pc = ReadAsAddress(IRQ_PC_START_ADDRESS);

                cycles = IRQ_CYCLE_COUNT;
            }
        }

        public void NMI()
        {
            PushStack((byte)((pc >> 8) & 0x00FF));
            PushStack((byte)(pc & 0x00FF));

            status |= FLAGS6502.B;
            status |= FLAGS6502.U;
            PushStack((byte)status);

            status |= FLAGS6502.I;

            pc = ReadAsAddress(NMI_PC_START_ADDRESS);

            cycles = NMI_CYCLE_COUNT;
        }

		#endregion

		#region Helpers

		private void PushStack(byte value)
		{
			Write((ushort)(STACK_ADDRESS_HIGH_BYTE_MASK + stkp), value);
			stkp--;
		}

		private void PushPcOnStack()
		{
			PushStack((byte)((pc >> 8) & 0x00FF)); // Store the high byte (& 0x00FF clears the high portion)
			PushStack((byte)(pc & 0x00FF)); // Store the low byte
		}

		private byte PopStack()
		{
			stkp++;
			return Read((ushort)(STACK_ADDRESS_HIGH_BYTE_MASK + stkp));
		}

		private void PopStackToPc()
		{
			pc = PopStack();
			pc |= (ushort)(PopStack() << 8);

		}


		private ushort ReadAsAddress(ushort startAddress)
		{
			addr_abs = startAddress;
			ushort low = Read((ushort)(addr_abs + 0));
			ushort high = Read((ushort)(addr_abs + 1));

			// return the result as an address
			return (ushort)((high << 8) | low);
		}

		/// <summary>
		/// Read the data at current program counter and increments it after
		/// </summary>
		/// <returns></returns>
		private byte ReadPc()
		{
			return Read(pc++);

		}

		/// <summary>
		/// Read the next two bytes at current program counter, increments and return as a ushort address
		/// </summary>
		/// <returns></returns>
		private ushort ReadPcAsAddress()
		{
			ushort low = ReadPc();
			ushort high = ReadPc();

			// return the result as an address
			return (ushort)((high << 8) | low);
		}

		/// <summary>
		/// Set of unset a flag based on a condition
		/// </summary>
		/// <param name="flag"></param>
		/// <param name="isSet"></param>
		private void SetFlag(FLAGS6502 flag, bool isSet)
		{
			status = isSet ? status | flag : status & ~flag;
		}

		#endregion

		#region Debug functions

		public bool Complete()
        {
            return cycles == 0;
        }

        // This is the disassembly function. Its workings are not required for emulation.
        // It is merely a convenience function to turn the binary instruction code into
        // human readable form. Its included as part of the emulator because it can take
        // advantage of many of the CPUs internal operations to do this.
        public Dictionary<ushort, string> Disassemble(ushort nStart, ushort nStop)
        {
            uint addr = nStart;
            byte value = 0x00, lo = 0x00, hi = 0x00;
            Dictionary<ushort, string> mapLines = new Dictionary<ushort, string>();
            ushort line_addr = 0;

            // Starting at the specified address we read an instruction
            // byte, which in turn yields information from the lookup table
            // as to how many additional bytes we need to read and what the
            // addressing mode is. I need this info to assemble human readable
            // syntax, which is different depending upon the addressing mode

            // As the instruction is decoded, a std::string is assembled
            // with the readable output
            while (addr <= (uint)nStop)
            {
                line_addr = (ushort)addr;

                // Prefix line with instruction address
                string sInst = "$" + addr.ToString("X4") + ": ";

                // Read instruction, and get its readable name
                byte opcode = (byte)bus.CPURead((ushort)addr, true); addr++;
                sInst += lookup[opcode].name + " ";

                // Get oprands from desired locations, and form the
                // instruction based upon its addressing mode. These
                // routines mimmick the actual fetch routine of the
                // 6502 in order to get accurate data as part of the
                // instruction
                if (lookup[opcode].addrmode == IMP)
                {
                    sInst += " {IMP}";
                }
                else if (lookup[opcode].addrmode == IMM)
                {
                    value = (byte)bus.CPURead((ushort)addr, true); addr++;
                    sInst += "#$" + value.ToString("X2") + " {IMM}";
                }
                else if (lookup[opcode].addrmode == ZP0)
                {
                    lo = (byte)bus.CPURead((ushort)addr, true); addr++;
                    hi = 0x00;
                    sInst += "$" + lo.ToString("X2") + " {ZP0}";
                }
                else if (lookup[opcode].addrmode == ZPX)
                {
                    lo = (byte)bus.CPURead((ushort)addr, true); addr++;
                    hi = 0x00;
                    sInst += "$" + lo.ToString("X2") + ", X {ZPX}";
                }
                else if (lookup[opcode].addrmode == ZPY)
                {
                    lo = (byte)bus.CPURead((ushort)addr, true); addr++;
                    hi = 0x00;
                    sInst += "$" + lo.ToString("X2") + ", Y {ZPY}";
                }
                else if (lookup[opcode].addrmode == IZX)
                {
                    lo = (byte)bus.CPURead((ushort)addr, true); addr++;
                    hi = 0x00;
                    sInst += "($" + lo.ToString("X2") + ", X) {IZX}";
                }
                else if (lookup[opcode].addrmode == IZY)
                {
                    lo = (byte)bus.CPURead((ushort)addr, true); addr++;
                    hi = 0x00;
                    sInst += "($" + lo.ToString("X2") + "), Y {IZY}";
                }
                else if (lookup[opcode].addrmode == ABS)
                {
                    lo = (byte)bus.CPURead((ushort)addr, true); addr++;
                    hi = (byte)bus.CPURead((ushort)addr, true); addr++;
                    sInst += "$" + ((ushort)(hi << 8) | lo).ToString("X4") + " {ABS}";
                }
                else if (lookup[opcode].addrmode == ABX)
                {
                    lo = (byte)bus.CPURead((ushort)addr, true); addr++;
                    hi = (byte)bus.CPURead((ushort)addr, true); addr++;
                    sInst += "$" + ((ushort)(hi << 8) | lo).ToString("X4") + ", X {ABX}";
                }
                else if (lookup[opcode].addrmode == ABY)
                {
                    lo = (byte)bus.CPURead((ushort)addr, true); addr++;
                    hi = (byte)bus.CPURead((ushort)addr, true); addr++;
                    sInst += "$" + ((ushort)(hi << 8) | lo).ToString("X4") + ", Y {ABY}";
                }
                else if (lookup[opcode].addrmode == IND)
                {
                    lo = (byte)bus.CPURead((ushort)addr, true); addr++;
                    hi = (byte)bus.CPURead((ushort)addr, true); addr++;
                    sInst += "($" + ((ushort)(hi << 8) | lo).ToString("X4") + ") {IND}";
                }
                else if (lookup[opcode].addrmode == REL)
                {
                    value = (byte)bus.CPURead((ushort)addr, true); addr++;
                    sInst += "$" + value.ToString("X2") + " [$" + (addr + (sbyte)value).ToString("X4") + "] {REL}";
                }

                // Add the formed string to a std::map, using the instruction's
                // address as the key. This makes it convenient to look for later
                // as the instructions are variable in length, so a straight up
                // incremental index is not sufficient.
                mapLines[line_addr] = sInst;
            }

            return mapLines;
        }

        #endregion
    }
}
