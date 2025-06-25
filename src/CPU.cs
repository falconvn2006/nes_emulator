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
        public struct INSTRUCTION
        {
            public string name;
            public Func<byte> operate;
            public Func<byte> addrmode;
            public byte cycles;

            public INSTRUCTION(string _name, Func<byte> _operate = null, Func<byte> _addrmode = null, byte cycles = 0)
            {
                name = _name;
                operate = _operate;
                addrmode = _addrmode;
                this.cycles = cycles;
            }
        }

        List<INSTRUCTION> lookup;

        public enum FLAGS6502
        {
            C = 1 << 0, // Carry bit
            Z = 1 << 1, // Zero
            I = 1 << 2, // Disable Interrupts
            D = 1 << 3, // Decimal Mode
            B = 1 << 4, // Break
            U = 1 << 5, // Unused
            V = 1 << 6, // Overflow
            N = 1 << 7, // Negative
        }

        public byte a = 0x00;      // Accumulator Register
        public byte x = 0x00;      // X Register
        public byte y = 0x00;      // Y Register
        public byte stkp = 0x00;   // Stack pointer (points to a location on the bus)
        public ushort pc = 0x0000; // Program counter
        public byte status = 0x00; // Status Register

        private ushort addr_abs = 0x0000;
        private ushort addr_rel = 0x00;
        private byte opcode = 0x00;
        private ushort temp = 0x0000;
        private byte fetched = 0x00;
        private byte cycles = 0;
        private uint clock_count = 0;

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
            bus = _bus;
        }

        private void Write(ushort addr, byte data)
        {
            bus.Write(addr, data);
        }

        private ushort Read(ushort addr)
        {
            return bus.Read(addr, false);
        }

        private void SetFlag(FLAGS6502 f, bool v)
        {
            if (v)
            {
                status |= (byte)f;
            }
            else
            {
                status &= (byte)~(byte)f;
            }
        }

        private byte GetFlag(FLAGS6502 f)
        {
            return (byte)((status & (byte)f) > 0 ? 1 : 0);
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
        public byte IMP()
        {
            fetched = a;
            return 0;
        }


        // Address Mode: Immediate
        // The instruction expects the next byte to be used as a value, so we'll prep
        // the read address to point to the next byte
        public byte IMM()
        {
            addr_abs = pc++;
            return 0;
        }



        // Address Mode: Zero Page
        // To save program bytes, zero page addressing allows you to absolutely address
        // a location in first 0xFF bytes of address range. Clearly this only requires
        // one byte instead of the usual two.
        public byte ZP0()
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
        public byte ZPX()
        {
            addr_abs = (ushort)(Read(pc) + x);
            pc++;
            addr_abs &= 0x00FF;
            return 0;
        }


        // Address Mode: Zero Page with Y Offset
        // Same as above but uses Y Register for offset
        public byte ZPY()
        {
            addr_abs = (ushort)(Read(pc) + y);
            pc++;
            addr_abs &= 0x00FF;
            return 0;
        }


        // Address Mode: Relative
        // This address mode is exclusive to branch instructions. The address
        // must reside within -128 to +127 of the branch instruction, i.e.
        // you cant directly branch to any address in the addressable range.
        public byte REL()
        {
            addr_rel = Read(pc);
            pc++;
            if (Convert.ToBoolean(addr_rel & 0x80))
                addr_rel |= 0xFF00;
            return 0;
        }


        // Address Mode: Absolute 
        // A full 16-bit address is loaded and used
        public byte ABS()
        {
            ushort lo = Read(pc);
            pc++;
            ushort hi = Read(pc);
            pc++;

            addr_abs = (ushort)(hi << 8 | lo);

            return 0;
        }


        // Address Mode: Absolute with X Offset
        // Fundamentally the same as absolute addressing, but the contents of the X Register
        // is added to the supplied two byte address. If the resulting address changes
        // the page, an additional clock cycle is required
        public byte ABX()
        {
            ushort lo = Read(pc);
            pc++;
            ushort hi = Read(pc);
            pc++;

            addr_abs = (ushort)(hi << 8 | lo);
            addr_abs += x;

            if ((addr_abs & 0xFF00) != hi << 8)
                return 1;
            else
                return 0;
        }


        // Address Mode: Absolute with Y Offset
        // Fundamentally the same as absolute addressing, but the contents of the Y Register
        // is added to the supplied two byte address. If the resulting address changes
        // the page, an additional clock cycle is required
        public byte ABY()
        {
            ushort lo = Read(pc);
            pc++;
            ushort hi = Read(pc);
            pc++;

            addr_abs = (ushort)(hi << 8 | lo);
            addr_abs += y;

            if ((addr_abs & 0xFF00) != hi << 8)
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
        public byte IND()
        {
            ushort ptr_lo = Read(pc);
            pc++;
            ushort ptr_hi = Read(pc);
            pc++;

            ushort ptr = (ushort)(ptr_hi << 8 | ptr_lo);

            if (ptr_lo == 0x00FF) // Simulate page boundary hardware bug
            {
                addr_abs = (ushort)(Read((ushort)((ptr & 0xFF00) << 8)) | Read((ushort)(ptr + 0)));
            }
            else // Behave normally
            {
                addr_abs = (ushort)(Read((ushort)(ptr + 1)) << 8 | Read((ushort)(ptr + 0)));
            }

            return 0;
        }


        // Address Mode: Indirect X
        // The supplied 8-bit address is offset by X Register to index
        // a location in page 0x00. The actual 16-bit address is read 
        // from this location
        public byte IZX()
        {
            ushort t = Read(pc);
            pc++;

            ushort lo = Read((ushort)((ushort)(t + x) & 0x00FF));
            ushort hi = Read((ushort)((ushort)(t + x + 1) & 0x00FF));

            addr_abs = (ushort)(hi << 8 | lo);

            return 0;
        }


        // Address Mode: Indirect Y
        // The supplied 8-bit address indexes a location in page 0x00. From 
        // here the actual 16-bit address is read, and the contents of
        // Y Register is added to it to offset it. If the offset causes a
        // change in page then an additional clock cycle is required.
        public byte IZY()
        {
            ushort t = Read(pc);
            pc++;

            ushort lo = Read((ushort)(t & 0x00FF));
            ushort hi = Read((ushort)(t + 1 & 0x00FF));

            addr_abs = (ushort)(hi << 8 | lo);
            addr_abs += y;

            if ((addr_abs & 0xFF00) != hi << 8)
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
        public byte fetch()
        {
            if (!(lookup[opcode].addrmode == IMP))
                fetched = (byte)Read(addr_abs);
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

        public byte ADC()
        {
            // Grab the data that we are adding to the accumulator
            fetch();

            // Add is performed in 16-bit domain for emulation to capture any
            // carry bit, which will exist in bit 8 of the 16-bit word
            temp = (ushort)(a + fetched + GetFlag(FLAGS6502.C));

            // The carry flag out exists in the high byte bit 0
            SetFlag(FLAGS6502.C, temp > 255);

            // The Zero flag is set if the result is 0
            SetFlag(FLAGS6502.Z, (temp & 0x00FF) == 0);

            // The signed Overflow flag is set based on all that up there! :D
            SetFlag(FLAGS6502.V, Convert.ToBoolean(~(a ^ fetched) & (a ^ temp) & 0x0080));

            // The negative flag is set to the most significant bit of the result
            SetFlag(FLAGS6502.N, Convert.ToBoolean((ushort)(temp & 0x80)));

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

        public byte SBC()
        {
            fetch();

            // Operating in 16-bit domain to capture carry out

            // We can invert the bottom 8 bits with bitwise xor
            ushort value = (ushort)(fetched ^ 0x00FF);

            // Notice this is exactly the same as addition from here!
            temp = (ushort)(a + value + GetFlag(FLAGS6502.C));
            SetFlag(FLAGS6502.C, Convert.ToBoolean((ushort)(temp & 0xFF00)));
            SetFlag(FLAGS6502.Z, (temp & 0x00FF) == 0);
            SetFlag(FLAGS6502.V, Convert.ToBoolean((temp ^ a) & (temp ^ value) & 0x0080));
            SetFlag(FLAGS6502.N, Convert.ToBoolean((ushort)(temp & 0x0080)));
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
        public byte AND()
        {
            fetch();
            a = (byte)(a & fetched);
            SetFlag(FLAGS6502.Z, a == 0x00);
            SetFlag(FLAGS6502.N, Convert.ToBoolean((byte)(a & 0x80)));
            return 1;
        }


        // Instruction: Arithmetic Shift Left
        // Function:    A = C <- (A << 1) <- 0
        // Flags Out:   N, Z, C
        public byte ASL()
        {
            fetch();
            temp = (ushort)(fetched << 1);
            SetFlag(FLAGS6502.C, (temp & 0xFF00) > 0);
            SetFlag(FLAGS6502.Z, (temp & 0x00FF) == 0x00);
            SetFlag(FLAGS6502.N, Convert.ToBoolean((ushort)(temp & 0x80)));
            if (lookup[opcode].addrmode == IMP)
                a = (byte)(temp & 0x00FF);
            else
                Write(addr_abs, (byte)(temp & 0x00FF));
            return 0;
        }


        // Instruction: Branch if Carry Clear
        // Function:    if(C == 0) pc = address 
        public byte BCC()
        {
            if (GetFlag(FLAGS6502.C) == 0)
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
        public byte BCS()
        {
            if (GetFlag(FLAGS6502.C) == 1)
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
        public byte BEQ()
        {
            if (GetFlag(FLAGS6502.Z) == 1)
            {
                cycles++;
                addr_abs = (ushort)(pc + addr_rel);

                if ((addr_abs & 0xFF00) != (pc & 0xFF00))
                    cycles++;

                pc = addr_abs;
            }
            return 0;
        }

        public byte BIT()
        {
            fetch();
            temp = (ushort)(a & fetched);
            SetFlag(FLAGS6502.Z, (temp & 0x00FF) == 0x00);
            SetFlag(FLAGS6502.N, Convert.ToBoolean((byte)(fetched & 1 << 7)));
            SetFlag(FLAGS6502.V, Convert.ToBoolean((byte)(fetched & 1 << 6)));
            return 0;
        }


        // Instruction: Branch if Negative
        // Function:    if(N == 1) pc = address
        public byte BMI()
        {
            if (GetFlag(FLAGS6502.N) == 1)
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
        public byte BNE()
        {
            if (GetFlag(FLAGS6502.Z) == 0)
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
        public byte BPL()
        {
            if (GetFlag(FLAGS6502.N) == 0)
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
        public byte BRK()
        {
            pc++;

            SetFlag(FLAGS6502.I, true);
            Write((ushort)(0x0100 + stkp), (byte)(pc >> 8 & 0x00FF));
            stkp--;
            Write((ushort)(0x0100 + stkp), (byte)(pc & 0x00FF));
            stkp--;

            SetFlag(FLAGS6502.B, true);
            Write((ushort)(0x0100 + stkp), status);
            stkp--;
            SetFlag(FLAGS6502.B, false);

            pc = (ushort)(Read(0xFFFE) | Read(0xFFFF) << 8);
            return 0;
        }


        // Instruction: Branch if Overflow Clear
        // Function:    if(V == 0) pc = address
        public byte BVC()
        {
            if (GetFlag(FLAGS6502.V) == 0)
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
        public byte BVS()
        {
            if (GetFlag(FLAGS6502.V) == 1)
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
        public byte CLC()
        {
            SetFlag(FLAGS6502.C, false);
            return 0;
        }


        // Instruction: Clear Decimal Flag
        // Function:    D = 0
        public byte CLD()
        {
            SetFlag(FLAGS6502.D, false);
            return 0;
        }


        // Instruction: Disable Interrupts / Clear Interrupt Flag
        // Function:    I = 0
        public byte CLI()
        {
            SetFlag(FLAGS6502.I, false);
            return 0;
        }


        // Instruction: Clear Overflow Flag
        // Function:    V = 0
        public byte CLV()
        {
            SetFlag(FLAGS6502.V, false);
            return 0;
        }

        // Instruction: Compare Accumulator
        // Function:    C <- A >= M      Z <- (A - M) == 0
        // Flags Out:   N, C, Z
        public byte CMP()
        {
            fetch();
            temp = (ushort)(a - fetched);
            SetFlag(FLAGS6502.C, a >= fetched);
            SetFlag(FLAGS6502.Z, (temp & 0x00FF) == 0x0000);
            SetFlag(FLAGS6502.N, Convert.ToBoolean((ushort)(temp & 0x0080)));
            return 1;
        }


        // Instruction: Compare X Register
        // Function:    C <- X >= M      Z <- (X - M) == 0
        // Flags Out:   N, C, Z
        public byte CPX()
        {
            fetch();
            temp = (ushort)(x - fetched);
            SetFlag(FLAGS6502.C, x >= fetched);
            SetFlag(FLAGS6502.Z, (temp & 0x00FF) == 0x0000);
            SetFlag(FLAGS6502.N, Convert.ToBoolean((ushort)(temp & 0x0080)));
            return 0;
        }


        // Instruction: Compare Y Register
        // Function:    C <- Y >= M      Z <- (Y - M) == 0
        // Flags Out:   N, C, Z
        public byte CPY()
        {
            fetch();
            temp = (ushort)(y - fetched);
            SetFlag(FLAGS6502.C, y >= fetched);
            SetFlag(FLAGS6502.Z, (temp & 0x00FF) == 0x0000);
            SetFlag(FLAGS6502.N, Convert.ToBoolean((ushort)(temp & 0x0080)));
            return 0;
        }


        // Instruction: Decrement Value at Memory Location
        // Function:    M = M - 1
        // Flags Out:   N, Z
        public byte DEC()
        {
            fetch();
            temp = (ushort)(fetched - 1);
            Write(addr_abs, (byte)(ushort)(temp & 0x00FF));
            SetFlag(FLAGS6502.Z, (temp & 0x00FF) == 0x0000);
            SetFlag(FLAGS6502.N, Convert.ToBoolean((ushort)(temp & 0x0080)));
            return 0;
        }


        // Instruction: Decrement X Register
        // Function:    X = X - 1
        // Flags Out:   N, Z
        public byte DEX()
        {
            x--;
            SetFlag(FLAGS6502.Z, x == 0x00);
            SetFlag(FLAGS6502.N, Convert.ToBoolean((byte)(x & 0x80)));
            return 0;
        }


        // Instruction: Decrement Y Register
        // Function:    Y = Y - 1
        // Flags Out:   N, Z
        public byte DEY()
        {
            y--;
            SetFlag(FLAGS6502.Z, y == 0x00);
            SetFlag(FLAGS6502.N, Convert.ToBoolean((byte)(y & 0x80)));
            return 0;
        }


        // Instruction: Bitwise Logic XOR
        // Function:    A = A xor M
        // Flags Out:   N, Z
        public byte EOR()
        {
            fetch();
            a = (byte)(a ^ fetched);
            SetFlag(FLAGS6502.Z, a == 0x00);
            SetFlag(FLAGS6502.N, Convert.ToBoolean((byte)(a & 0x80)));
            return 1;
        }


        // Instruction: Increment Value at Memory Location
        // Function:    M = M + 1
        // Flags Out:   N, Z
        public byte INC()
        {
            fetch();
            temp = (ushort)(fetched + 1);
            Write(addr_abs, (byte)(temp & 0x00FF));
            SetFlag(FLAGS6502.Z, (temp & 0x00FF) == 0x0000);
            SetFlag(FLAGS6502.N, Convert.ToBoolean((ushort)(temp & 0x0080)));
            return 0;
        }


        // Instruction: Increment X Register
        // Function:    X = X + 1
        // Flags Out:   N, Z
        public byte INX()
        {
            x++;
            SetFlag(FLAGS6502.Z, x == 0x00);
            SetFlag(FLAGS6502.N, Convert.ToBoolean((byte)(x & 0x80)));
            return 0;
        }


        // Instruction: Increment Y Register
        // Function:    Y = Y + 1
        // Flags Out:   N, Z
        public byte INY()
        {
            y++;
            SetFlag(FLAGS6502.Z, y == 0x00);
            SetFlag(FLAGS6502.N, Convert.ToBoolean((byte)(y & 0x80)));
            return 0;
        }


        // Instruction: Jump To Location
        // Function:    pc = address
        public byte JMP()
        {
            pc = addr_abs;
            return 0;
        }


        // Instruction: Jump To Sub-Routine
        // Function:    Push current pc to stack, pc = address
        public byte JSR()
        {
            pc--;

            Write((ushort)(0x0100 + stkp), (byte)(pc >> 8 & 0x00FF));
            stkp--;
            Write((ushort)(0x0100 + stkp), (byte)(pc & 0x00FF));
            stkp--;

            pc = addr_abs;
            return 0;
        }


        // Instruction: Load The Accumulator
        // Function:    A = M
        // Flags Out:   N, Z
        public byte LDA()
        {
            fetch();
            a = fetched;
            SetFlag(FLAGS6502.Z, a == 0x00);
            SetFlag(FLAGS6502.N, Convert.ToBoolean((byte)(a & 0x80)));
            return 1;
        }


        // Instruction: Load The X Register
        // Function:    X = M
        // Flags Out:   N, Z
        public byte LDX()
        {
            fetch();
            x = fetched;
            SetFlag(FLAGS6502.Z, x == 0x00);
            SetFlag(FLAGS6502.N, Convert.ToBoolean((byte)(x & 0x80)));
            return 1;
        }


        // Instruction: Load The Y Register
        // Function:    Y = M
        // Flags Out:   N, Z
        public byte LDY()
        {
            fetch();
            y = fetched;
            SetFlag(FLAGS6502.Z, y == 0x00);
            SetFlag(FLAGS6502.N, Convert.ToBoolean((byte)(y & 0x80)));
            return 1;
        }

        public byte LSR()
        {
            fetch();
            SetFlag(FLAGS6502.C, Convert.ToBoolean((byte)(fetched & 0x0001)));
            temp = (ushort)(fetched >> 1);
            SetFlag(FLAGS6502.Z, (temp & 0x00FF) == 0x0000);
            SetFlag(FLAGS6502.N, Convert.ToBoolean((ushort)(temp & 0x0080)));
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
        public byte ORA()
        {
            fetch();
            a = (byte)(a | fetched);
            SetFlag(FLAGS6502.Z, a == 0x00);
            SetFlag(FLAGS6502.N, Convert.ToBoolean(a & 0x80));
            return 1;
        }


        // Instruction: Push Accumulator to Stack
        // Function:    A -> stack
        public byte PHA()
        {
            Write((ushort)(0x0100 + stkp), a);
            stkp--;
            return 0;
        }


        // Instruction: Push Status Register to Stack
        // Function:    status -> stack
        // Note:        Break flag is set to 1 before push
        public byte PHP()
        {
            Write((ushort)(0x0100 + stkp), (byte)(status | (byte)FLAGS6502.B | (byte)FLAGS6502.U));
            SetFlag(FLAGS6502.B, false);
            SetFlag(FLAGS6502.U, false);
            stkp--;
            return 0;
        }


        // Instruction: Pop Accumulator off Stack
        // Function:    A <- stack
        // Flags Out:   N, Z
        public byte PLA()
        {
            stkp++;
            a = (byte)Read((ushort)(0x0100 + stkp));
            SetFlag(FLAGS6502.Z, a == 0x00);
            SetFlag(FLAGS6502.N, Convert.ToBoolean((byte)(a & 0x80)));
            return 0;
        }


        // Instruction: Pop Status Register off Stack
        // Function:    Status <- stack
        public byte PLP()
        {
            stkp++;
            status = (byte)Read((ushort)(0x0100 + stkp));
            SetFlag(FLAGS6502.U, true);
            return 0;
        }

        public byte ROL()
        {
            fetch();
            temp = (ushort)((ushort)(fetched << 1) | GetFlag(FLAGS6502.C));
            SetFlag(FLAGS6502.C, Convert.ToBoolean(temp & 0xFF00));
            SetFlag(FLAGS6502.Z, (temp & 0x00FF) == 0x0000);
            SetFlag(FLAGS6502.N, Convert.ToBoolean(temp & 0x0080));
            if (lookup[opcode].addrmode == IMP)
                a = (byte)(temp & 0x00FF);
            else
                Write(addr_abs, (byte)(temp & 0x00FF));
            return 0;
        }

        public byte ROR()
        {
            fetch();
            temp = (ushort)((ushort)(GetFlag(FLAGS6502.C) << 7) | fetched >> 1);
            SetFlag(FLAGS6502.C, Convert.ToBoolean(fetched & 0x01));
            SetFlag(FLAGS6502.Z, (temp & 0x00FF) == 0x00);
            SetFlag(FLAGS6502.N, Convert.ToBoolean(temp & 0x0080));
            if (lookup[opcode].addrmode == IMP)
                a = (byte)(temp & 0x00FF);
            else
                Write(addr_abs, (byte)(temp & 0x00FF));
            return 0;
        }

        public byte RTI()
        {
            stkp++;
            status = (byte)Read((ushort)(0x0100 + stkp));
            status &= Convert.ToByte(~(byte)FLAGS6502.B);
            status &= Convert.ToByte(~(byte)FLAGS6502.U);

            stkp++;
            pc = Read((ushort)(0x0100 + stkp));
            stkp++;
            pc |= (ushort)(Read((ushort)(0x0100 + stkp)) << 8);
            return 0;
        }

        public byte RTS()
        {
            stkp++;
            pc = Read((ushort)(0x0100 + stkp));
            stkp++;
            pc |= (ushort)(Read((ushort)(0x0100 + stkp)) << 8);

            pc++;
            return 0;
        }




        // Instruction: Set Carry Flag
        // Function:    C = 1
        public byte SEC()
        {
            SetFlag(FLAGS6502.C, true);
            return 0;
        }


        // Instruction: Set Decimal Flag
        // Function:    D = 1
        public byte SED()
        {
            SetFlag(FLAGS6502.D, true);
            return 0;
        }


        // Instruction: Set Interrupt Flag / Enable Interrupts
        // Function:    I = 1
        public byte SEI()
        {
            SetFlag(FLAGS6502.I, true);
            return 0;
        }


        // Instruction: Store Accumulator at Address
        // Function:    M = A
        public byte STA()
        {
            Write(addr_abs, a);
            return 0;
        }


        // Instruction: Store X Register at Address
        // Function:    M = X
        public byte STX()
        {
            Write(addr_abs, x);
            return 0;
        }


        // Instruction: Store Y Register at Address
        // Function:    M = Y
        public byte STY()
        {
            Write(addr_abs, y);
            return 0;
        }


        // Instruction: Transfer Accumulator to X Register
        // Function:    X = A
        // Flags Out:   N, Z
        public byte TAX()
        {
            x = a;
            SetFlag(FLAGS6502.Z, x == 0x00);
            SetFlag(FLAGS6502.N, Convert.ToBoolean((byte)(x & 0x80)));
            return 0;
        }


        // Instruction: Transfer Accumulator to Y Register
        // Function:    Y = A
        // Flags Out:   N, Z
        public byte TAY()
        {
            y = a;
            SetFlag(FLAGS6502.Z, y == 0x00);
            SetFlag(FLAGS6502.N, Convert.ToBoolean((byte)(y & 0x80)));
            return 0;
        }


        // Instruction: Transfer Stack Pointer to X Register
        // Function:    X = stack pointer
        // Flags Out:   N, Z
        public byte TSX()
        {
            x = stkp;
            SetFlag(FLAGS6502.Z, x == 0x00);
            SetFlag(FLAGS6502.N, Convert.ToBoolean((byte)(x & 0x80)));
            return 0;
        }


        // Instruction: Transfer X Register to Accumulator
        // Function:    A = X
        // Flags Out:   N, Z
        public byte TXA()
        {
            a = x;
            SetFlag(FLAGS6502.Z, a == 0x00);
            SetFlag(FLAGS6502.N, Convert.ToBoolean((byte)(a & 0x80)));
            return 0;
        }


        // Instruction: Transfer X Register to Stack Pointer
        // Function:    stack pointer = X
        public byte TXS()
        {
            stkp = x;
            return 0;
        }


        // Instruction: Transfer Y Register to Accumulator
        // Function:    A = Y
        // Flags Out:   N, Z
        public byte TYA()
        {
            a = y;
            SetFlag(FLAGS6502.Z, a == 0x00);
            SetFlag(FLAGS6502.N, Convert.ToBoolean((byte)(a & 0x80)));
            return 0;
        }


        // This function captures illegal opcodes
        public byte XXX()
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

                //SetFlag(FLAGS6502.U, true);

                pc++;

                cycles = lookup[opcode].cycles;

                byte additional_cycle1 = lookup[opcode].addrmode();
                byte additional_cycle2 = lookup[opcode].operate();

                cycles += (byte)(additional_cycle1 & additional_cycle2);

                //SetFlag(FLAGS6502.U, true);
            }

            //clock_count++;

            cycles--;
        }

        public void Reset()
        {
            addr_abs = 0xFFFC;
            ushort lo = Read((ushort)(addr_abs + 0));
            ushort hi = Read((ushort)(addr_abs + 1));

            pc = (ushort)(hi << 8 | lo);

            a = 0;
            x = 0;
            y = 0;
            stkp = 0xFD;
            status = 0x00 | (byte)FLAGS6502.U;

            addr_rel = 0x0000;
            addr_abs = 0x0000;
            fetched = 0x00;

            cycles = 8;
        }

        public void IRQ()
        {
            if (GetFlag(FLAGS6502.I) == 0)
            {
                Write((ushort)(0x0100 + stkp), (byte)(pc >> 8 & 0x00FF));
                stkp--;
                Write((ushort)(0x0100 + stkp), (byte)(pc & 0x00FF));
                stkp--;

                SetFlag(FLAGS6502.B, false);
                SetFlag(FLAGS6502.U, true);
                SetFlag(FLAGS6502.I, true);
                Write((ushort)(0x0100 + stkp), status);
                stkp--;

                addr_abs = 0xFFFE;
                ushort lo = Read((ushort)(addr_abs + 0));
                ushort hi = Read((ushort)(addr_abs + 1));
                pc = (byte)(hi << 8 | lo);

                cycles = 7;
            }
        }

        public void NMI()
        {
            Write((ushort)(0x0100 + stkp), (byte)(pc >> 8 & 0x00FF));
            stkp--;
            Write((ushort)(0x0100 + stkp), (byte)(pc & 0x00FF));
            stkp--;

            SetFlag(FLAGS6502.B, false);
            SetFlag(FLAGS6502.U, true);
            SetFlag(FLAGS6502.I, true);
            Write((ushort)(0x0100 + stkp), status);
            stkp--;

            addr_abs = 0xFFFA;
            ushort lo = Read((ushort)(addr_abs + 0));
            ushort hi = Read((ushort)(addr_abs + 1));
            pc = (byte)(hi << 8 | lo);

            cycles = 8;
        }

        #endregion

        #region Helper functions

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

            // A convenient utility to convert variables into
            // hex strings because "modern C++"'s method with 
            // streams is atrocious
            //var hex = [](uint n, byte d)
            //{
            //	std::string s(d, '0');
            //	for (int i = d - 1; i >= 0; i--, n >>= 4)
            //		s[i] = "0123456789ABCDEF"[n & 0xF];
            //	return s;
            //};

            // Starting at the specified address we read an instruction
            // byte, which in turn yields information from the lookup table
            // as to how many additional bytes we need to read and what the
            // addressing mode is. I need this info to assemble human readable
            // syntax, which is different depending upon the addressing mode

            // As the instruction is decoded, a std::string is assembled
            // with the readable output
            while (addr <= nStop)
            {
                line_addr = (ushort)addr;

                // Prefix line with instruction address
                string sInst = "$" + addr.ToString("X4") + ": ";

                // Read instruction, and get its readable name
                byte opcode = (byte)bus.Read((ushort)addr, true); addr++;
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
                    value = (byte)bus.Read((ushort)addr, true); addr++;
                    sInst += "#$" + value.ToString("X2") + " {IMM}";
                }
                else if (lookup[opcode].addrmode == ZP0)
                {
                    lo = (byte)bus.Read((ushort)addr, true); addr++;
                    hi = 0x00;
                    sInst += "$" + lo.ToString("X2") + " {ZP0}";
                }
                else if (lookup[opcode].addrmode == ZPX)
                {
                    lo = (byte)bus.Read((ushort)addr, true); addr++;
                    hi = 0x00;
                    sInst += "$" + lo.ToString("X2") + ", X {ZPX}";
                }
                else if (lookup[opcode].addrmode == ZPY)
                {
                    lo = (byte)bus.Read((ushort)addr, true); addr++;
                    hi = 0x00;
                    sInst += "$" + lo.ToString("X2") + ", Y {ZPY}";
                }
                else if (lookup[opcode].addrmode == IZX)
                {
                    lo = (byte)bus.Read((ushort)addr, true); addr++;
                    hi = 0x00;
                    sInst += "($" + lo.ToString("X2") + ", X) {IZX}";
                }
                else if (lookup[opcode].addrmode == IZY)
                {
                    lo = (byte)bus.Read((ushort)addr, true); addr++;
                    hi = 0x00;
                    sInst += "($" + lo.ToString("X2") + "), Y {IZY}";
                }
                else if (lookup[opcode].addrmode == ABS)
                {
                    lo = (byte)bus.Read((ushort)addr, true); addr++;
                    hi = (byte)bus.Read((ushort)addr, true); addr++;
                    sInst += "$" + ((ushort)(hi << 8) | lo).ToString("X4") + " {ABS}";
                }
                else if (lookup[opcode].addrmode == ABX)
                {
                    lo = (byte)bus.Read((ushort)addr, true); addr++;
                    hi = (byte)bus.Read((ushort)addr, true); addr++;
                    sInst += "$" + ((ushort)(hi << 8) | lo).ToString("X4") + ", X {ABX}";
                }
                else if (lookup[opcode].addrmode == ABY)
                {
                    lo = (byte)bus.Read((ushort)addr, true); addr++;
                    hi = (byte)bus.Read((ushort)addr, true); addr++;
                    sInst += "$" + ((ushort)(hi << 8) | lo).ToString("X4") + ", Y {ABY}";
                }
                else if (lookup[opcode].addrmode == IND)
                {
                    lo = (byte)bus.Read((ushort)addr, true); addr++;
                    hi = (byte)bus.Read((ushort)addr, true); addr++;
                    sInst += "($" + ((ushort)(hi << 8) | lo).ToString("X4") + ") {IND}";
                }
                else if (lookup[opcode].addrmode == REL)
                {
                    value = (byte)bus.Read((ushort)addr, true); addr++;
                    sInst += "$" + value.ToString("X2") + " [$" + (addr + value).ToString("X4") + "] {REL}";
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
