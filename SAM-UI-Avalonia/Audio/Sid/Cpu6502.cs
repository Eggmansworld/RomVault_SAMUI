namespace SAM_UI_Avalonia.Audio.Sid;

/// <summary>
/// A 6502/6510 interpreter: all documented opcodes plus the stable undocumented ones C64 music
/// players use (LAX, SAX, DCP, ISC, SLO, RLA, SRE, RRA, ANC, ALR, ARR, SBX and the NOP variants).
/// Memory goes through the bus delegates so the SID player can map I/O.
/// </summary>
public sealed class Cpu6502
{
    public byte A, X, Y, SP = 0xFF;
    public ushort PC;
    public bool C, Z, I, D, V, N;
    public long Cycles;
    public bool Jammed;

    private readonly Func<ushort, byte> _read;
    private readonly Action<ushort, byte> _write;

    public Cpu6502(Func<ushort, byte> read, Action<ushort, byte> write)
    {
        _read = read;
        _write = write;
    }

    private byte Rd(int a) => _read((ushort)a);
    private void Wr(int a, byte v) => _write((ushort)a, v);
    private ushort Rd16(int a) => (ushort)(Rd(a) | (Rd(a + 1) << 8));
    // JMP ($xxFF) wraps inside the page, as on the real chip
    private ushort Rd16Bug(int a) => (ushort)(Rd(a) | (Rd((a & 0xFF00) | ((a + 1) & 0xFF)) << 8));

    public void Push(byte v) { Wr(0x100 | SP, v); SP--; }
    public byte Pop() { SP++; return Rd(0x100 | SP); }

    public byte P
    {
        get => (byte)((C ? 1 : 0) | (Z ? 2 : 0) | (I ? 4 : 0) | (D ? 8 : 0) | 0x20 | (V ? 0x40 : 0) | (N ? 0x80 : 0));
        set { C = (value & 1) != 0; Z = (value & 2) != 0; I = (value & 4) != 0; D = (value & 8) != 0; V = (value & 0x40) != 0; N = (value & 0x80) != 0; }
    }

    private byte NZ(int v) { var b = (byte)v; Z = b == 0; N = (b & 0x80) != 0; return b; }

    // ---- addressing (return effective address; page-cross adds a cycle on reads)
    private int Imm() => PC++;
    private int Zp() => Rd(PC++);
    private int ZpX() => (Rd(PC++) + X) & 0xFF;
    private int ZpY() => (Rd(PC++) + Y) & 0xFF;
    private int Abs() { int a = Rd16(PC); PC += 2; return a; }
    private int AbsX(bool rd) { int b = Rd16(PC); PC += 2; int a = (b + X) & 0xFFFF; if (rd && ((a ^ b) & 0xFF00) != 0) Cycles++; return a; }
    private int AbsY(bool rd) { int b = Rd16(PC); PC += 2; int a = (b + Y) & 0xFFFF; if (rd && ((a ^ b) & 0xFF00) != 0) Cycles++; return a; }
    private int IndX() { int z = (Rd(PC++) + X) & 0xFF; return Rd(z) | (Rd((z + 1) & 0xFF) << 8); }
    private int IndY(bool rd) { int z = Rd(PC++); int b = Rd(z) | (Rd((z + 1) & 0xFF) << 8); int a = (b + Y) & 0xFFFF; if (rd && ((a ^ b) & 0xFF00) != 0) Cycles++; return a; }

    // ---- ALU
    private void Adc(byte m)
    {
        if (D)
        {
            int lo = (A & 0x0F) + (m & 0x0F) + (C ? 1 : 0);
            int hi = (A & 0xF0) + (m & 0xF0);
            if (lo > 9) { lo += 6; }
            if (lo > 0x0F) hi += 0x10;
            Z = ((A + m + (C ? 1 : 0)) & 0xFF) == 0;
            N = (hi & 0x80) != 0;
            V = (~(A ^ m) & (A ^ hi) & 0x80) != 0;
            if (hi > 0x90) hi += 0x60;
            C = hi > 0xFF;
            A = (byte)((hi & 0xF0) | (lo & 0x0F));
            return;
        }
        int r = A + m + (C ? 1 : 0);
        V = (~(A ^ m) & (A ^ r) & 0x80) != 0;
        C = r > 0xFF;
        A = NZ(r);
    }

    private void Sbc(byte m)
    {
        int r = A - m - (C ? 0 : 1);
        V = ((A ^ m) & (A ^ r) & 0x80) != 0;
        if (D)
        {
            int lo = (A & 0x0F) - (m & 0x0F) - (C ? 0 : 1);
            int hi = (A & 0xF0) - (m & 0xF0);
            if ((lo & 0x10) != 0) { lo -= 6; hi -= 0x10; }
            if ((hi & 0x100) != 0) hi -= 0x60;
            C = r >= 0;
            NZ(r);
            A = (byte)((hi & 0xF0) | (lo & 0x0F));
            return;
        }
        C = r >= 0;
        A = NZ(r);
    }

    private void Cmp(byte reg, byte m) { int r = reg - m; C = r >= 0; NZ(r); }
    private byte Asl(byte v) { C = (v & 0x80) != 0; return NZ(v << 1); }
    private byte Lsr(byte v) { C = (v & 1) != 0; return NZ(v >> 1); }
    private byte Rol(byte v) { int r = (v << 1) | (C ? 1 : 0); C = (v & 0x80) != 0; return NZ(r); }
    private byte Ror(byte v) { int r = (v >> 1) | (C ? 0x80 : 0); C = (v & 1) != 0; return NZ(r); }
    private void Bit(byte m) { Z = (A & m) == 0; N = (m & 0x80) != 0; V = (m & 0x40) != 0; }

    private void Branch(bool cond)
    {
        sbyte off = (sbyte)Rd(PC++);
        if (!cond) return;
        int np = (PC + off) & 0xFFFF;
        Cycles += ((np ^ PC) & 0xFF00) != 0 ? 2 : 1;
        PC = (ushort)np;
    }

    private void Rmw(int a, Func<byte, byte> op) { byte v = Rd(a); Wr(a, v); Wr(a, op(v)); }

    private static readonly byte[] BaseCycles =
    {
        7,6,2,8,3,3,5,5,3,2,2,2,4,4,6,6, 2,5,2,8,4,4,6,6,2,4,2,7,4,4,7,7,
        6,6,2,8,3,3,5,5,4,2,2,2,4,4,6,6, 2,5,2,8,4,4,6,6,2,4,2,7,4,4,7,7,
        6,6,2,8,3,3,5,5,3,2,2,2,3,4,6,6, 2,5,2,8,4,4,6,6,2,4,2,7,4,4,7,7,
        6,6,2,8,3,3,5,5,4,2,2,2,5,4,6,6, 2,5,2,8,4,4,6,6,2,4,2,7,4,4,7,7,
        2,6,2,6,3,3,3,3,2,2,2,2,4,4,4,4, 2,6,2,6,4,4,4,4,2,5,2,5,5,5,5,5,
        2,6,2,6,3,3,3,3,2,2,2,2,4,4,4,4, 2,5,2,5,4,4,4,4,2,4,2,4,4,4,4,4,
        2,6,2,8,3,3,5,5,2,2,2,2,4,4,6,6, 2,5,2,8,4,4,6,6,2,4,2,7,4,4,7,7,
        2,6,2,8,3,3,5,5,2,2,2,2,4,4,6,6, 2,5,2,8,4,4,6,6,2,4,2,7,4,4,7,7,
    };

    /// <summary>Executes one instruction.</summary>
    public void Step()
    {
        if (Jammed) { Cycles += 2; return; }
        byte op = Rd(PC++);
        Cycles += BaseCycles[op];
        int a;
        switch (op)
        {
            // ---- loads / stores
            case 0xA9: A = NZ(Rd(Imm())); break;
            case 0xA5: A = NZ(Rd(Zp())); break;
            case 0xB5: A = NZ(Rd(ZpX())); break;
            case 0xAD: A = NZ(Rd(Abs())); break;
            case 0xBD: A = NZ(Rd(AbsX(true))); break;
            case 0xB9: A = NZ(Rd(AbsY(true))); break;
            case 0xA1: A = NZ(Rd(IndX())); break;
            case 0xB1: A = NZ(Rd(IndY(true))); break;
            case 0xA2: X = NZ(Rd(Imm())); break;
            case 0xA6: X = NZ(Rd(Zp())); break;
            case 0xB6: X = NZ(Rd(ZpY())); break;
            case 0xAE: X = NZ(Rd(Abs())); break;
            case 0xBE: X = NZ(Rd(AbsY(true))); break;
            case 0xA0: Y = NZ(Rd(Imm())); break;
            case 0xA4: Y = NZ(Rd(Zp())); break;
            case 0xB4: Y = NZ(Rd(ZpX())); break;
            case 0xAC: Y = NZ(Rd(Abs())); break;
            case 0xBC: Y = NZ(Rd(AbsX(true))); break;
            case 0x85: Wr(Zp(), A); break;
            case 0x95: Wr(ZpX(), A); break;
            case 0x8D: Wr(Abs(), A); break;
            case 0x9D: Wr(AbsX(false), A); break;
            case 0x99: Wr(AbsY(false), A); break;
            case 0x81: Wr(IndX(), A); break;
            case 0x91: Wr(IndY(false), A); break;
            case 0x86: Wr(Zp(), X); break;
            case 0x96: Wr(ZpY(), X); break;
            case 0x8E: Wr(Abs(), X); break;
            case 0x84: Wr(Zp(), Y); break;
            case 0x94: Wr(ZpX(), Y); break;
            case 0x8C: Wr(Abs(), Y); break;

            // ---- transfers / stack
            case 0xAA: X = NZ(A); break;
            case 0xA8: Y = NZ(A); break;
            case 0x8A: A = NZ(X); break;
            case 0x98: A = NZ(Y); break;
            case 0xBA: X = NZ(SP); break;
            case 0x9A: SP = X; break;
            case 0x48: Push(A); break;
            case 0x68: A = NZ(Pop()); break;
            case 0x08: Push((byte)(P | 0x10)); break;
            case 0x28: P = Pop(); break;

            // ---- arithmetic / logic
            case 0x69: Adc(Rd(Imm())); break;
            case 0x65: Adc(Rd(Zp())); break;
            case 0x75: Adc(Rd(ZpX())); break;
            case 0x6D: Adc(Rd(Abs())); break;
            case 0x7D: Adc(Rd(AbsX(true))); break;
            case 0x79: Adc(Rd(AbsY(true))); break;
            case 0x61: Adc(Rd(IndX())); break;
            case 0x71: Adc(Rd(IndY(true))); break;
            case 0xE9: case 0xEB: Sbc(Rd(Imm())); break;
            case 0xE5: Sbc(Rd(Zp())); break;
            case 0xF5: Sbc(Rd(ZpX())); break;
            case 0xED: Sbc(Rd(Abs())); break;
            case 0xFD: Sbc(Rd(AbsX(true))); break;
            case 0xF9: Sbc(Rd(AbsY(true))); break;
            case 0xE1: Sbc(Rd(IndX())); break;
            case 0xF1: Sbc(Rd(IndY(true))); break;
            case 0x29: A = NZ(A & Rd(Imm())); break;
            case 0x25: A = NZ(A & Rd(Zp())); break;
            case 0x35: A = NZ(A & Rd(ZpX())); break;
            case 0x2D: A = NZ(A & Rd(Abs())); break;
            case 0x3D: A = NZ(A & Rd(AbsX(true))); break;
            case 0x39: A = NZ(A & Rd(AbsY(true))); break;
            case 0x21: A = NZ(A & Rd(IndX())); break;
            case 0x31: A = NZ(A & Rd(IndY(true))); break;
            case 0x09: A = NZ(A | Rd(Imm())); break;
            case 0x05: A = NZ(A | Rd(Zp())); break;
            case 0x15: A = NZ(A | Rd(ZpX())); break;
            case 0x0D: A = NZ(A | Rd(Abs())); break;
            case 0x1D: A = NZ(A | Rd(AbsX(true))); break;
            case 0x19: A = NZ(A | Rd(AbsY(true))); break;
            case 0x01: A = NZ(A | Rd(IndX())); break;
            case 0x11: A = NZ(A | Rd(IndY(true))); break;
            case 0x49: A = NZ(A ^ Rd(Imm())); break;
            case 0x45: A = NZ(A ^ Rd(Zp())); break;
            case 0x55: A = NZ(A ^ Rd(ZpX())); break;
            case 0x4D: A = NZ(A ^ Rd(Abs())); break;
            case 0x5D: A = NZ(A ^ Rd(AbsX(true))); break;
            case 0x59: A = NZ(A ^ Rd(AbsY(true))); break;
            case 0x41: A = NZ(A ^ Rd(IndX())); break;
            case 0x51: A = NZ(A ^ Rd(IndY(true))); break;
            case 0xC9: Cmp(A, Rd(Imm())); break;
            case 0xC5: Cmp(A, Rd(Zp())); break;
            case 0xD5: Cmp(A, Rd(ZpX())); break;
            case 0xCD: Cmp(A, Rd(Abs())); break;
            case 0xDD: Cmp(A, Rd(AbsX(true))); break;
            case 0xD9: Cmp(A, Rd(AbsY(true))); break;
            case 0xC1: Cmp(A, Rd(IndX())); break;
            case 0xD1: Cmp(A, Rd(IndY(true))); break;
            case 0xE0: Cmp(X, Rd(Imm())); break;
            case 0xE4: Cmp(X, Rd(Zp())); break;
            case 0xEC: Cmp(X, Rd(Abs())); break;
            case 0xC0: Cmp(Y, Rd(Imm())); break;
            case 0xC4: Cmp(Y, Rd(Zp())); break;
            case 0xCC: Cmp(Y, Rd(Abs())); break;
            case 0x24: Bit(Rd(Zp())); break;
            case 0x2C: Bit(Rd(Abs())); break;

            // ---- inc / dec
            case 0xE6: Rmw(Zp(), v => NZ(v + 1)); break;
            case 0xF6: Rmw(ZpX(), v => NZ(v + 1)); break;
            case 0xEE: Rmw(Abs(), v => NZ(v + 1)); break;
            case 0xFE: Rmw(AbsX(false), v => NZ(v + 1)); break;
            case 0xC6: Rmw(Zp(), v => NZ(v - 1)); break;
            case 0xD6: Rmw(ZpX(), v => NZ(v - 1)); break;
            case 0xCE: Rmw(Abs(), v => NZ(v - 1)); break;
            case 0xDE: Rmw(AbsX(false), v => NZ(v - 1)); break;
            case 0xE8: X = NZ(X + 1); break;
            case 0xC8: Y = NZ(Y + 1); break;
            case 0xCA: X = NZ(X - 1); break;
            case 0x88: Y = NZ(Y - 1); break;

            // ---- shifts
            case 0x0A: A = Asl(A); break;
            case 0x06: Rmw(Zp(), Asl); break;
            case 0x16: Rmw(ZpX(), Asl); break;
            case 0x0E: Rmw(Abs(), Asl); break;
            case 0x1E: Rmw(AbsX(false), Asl); break;
            case 0x4A: A = Lsr(A); break;
            case 0x46: Rmw(Zp(), Lsr); break;
            case 0x56: Rmw(ZpX(), Lsr); break;
            case 0x4E: Rmw(Abs(), Lsr); break;
            case 0x5E: Rmw(AbsX(false), Lsr); break;
            case 0x2A: A = Rol(A); break;
            case 0x26: Rmw(Zp(), Rol); break;
            case 0x36: Rmw(ZpX(), Rol); break;
            case 0x2E: Rmw(Abs(), Rol); break;
            case 0x3E: Rmw(AbsX(false), Rol); break;
            case 0x6A: A = Ror(A); break;
            case 0x66: Rmw(Zp(), Ror); break;
            case 0x76: Rmw(ZpX(), Ror); break;
            case 0x6E: Rmw(Abs(), Ror); break;
            case 0x7E: Rmw(AbsX(false), Ror); break;

            // ---- jumps / branches
            case 0x4C: PC = Rd16(PC); break;
            case 0x6C: PC = Rd16Bug(Rd16(PC)); break;
            case 0x20: a = Rd16(PC); PC++; Push((byte)(PC >> 8)); Push((byte)PC); PC = (ushort)a; break;
            case 0x60: PC = (ushort)((Pop() | (Pop() << 8)) + 1); break;
            case 0x40: P = Pop(); PC = (ushort)(Pop() | (Pop() << 8)); break;
            case 0x00: PC++; Push((byte)(PC >> 8)); Push((byte)PC); Push((byte)(P | 0x10)); I = true; PC = Rd16(0xFFFE); break;
            case 0x10: Branch(!N); break;
            case 0x30: Branch(N); break;
            case 0x50: Branch(!V); break;
            case 0x70: Branch(V); break;
            case 0x90: Branch(!C); break;
            case 0xB0: Branch(C); break;
            case 0xD0: Branch(!Z); break;
            case 0xF0: Branch(Z); break;

            // ---- flags
            case 0x18: C = false; break;
            case 0x38: C = true; break;
            case 0x58: I = false; break;
            case 0x78: I = true; break;
            case 0xB8: V = false; break;
            case 0xD8: D = false; break;
            case 0xF8: D = true; break;
            case 0xEA: break;

            // ---- undocumented, stable ones
            case 0xA7: A = X = NZ(Rd(Zp())); break;                          // LAX
            case 0xB7: A = X = NZ(Rd(ZpY())); break;
            case 0xAF: A = X = NZ(Rd(Abs())); break;
            case 0xBF: A = X = NZ(Rd(AbsY(true))); break;
            case 0xA3: A = X = NZ(Rd(IndX())); break;
            case 0xB3: A = X = NZ(Rd(IndY(true))); break;
            case 0xAB: A = X = NZ((A | 0xEE) & Rd(Imm())); break;            // LXA (unstable; common value)
            case 0x87: Wr(Zp(), (byte)(A & X)); break;                       // SAX
            case 0x97: Wr(ZpY(), (byte)(A & X)); break;
            case 0x8F: Wr(Abs(), (byte)(A & X)); break;
            case 0x83: Wr(IndX(), (byte)(A & X)); break;
            case 0xC7: case 0xD7: case 0xCF: case 0xDF: case 0xDB: case 0xC3: case 0xD3:   // DCP
                a = UndocAddr(op); Rmw(a, v => { var r = (byte)(v - 1); Cmp(A, r); return r; }); break;
            case 0xE7: case 0xF7: case 0xEF: case 0xFF: case 0xFB: case 0xE3: case 0xF3:   // ISC
                a = UndocAddr(op); Rmw(a, v => { var r = (byte)(v + 1); Sbc(r); return r; }); break;
            case 0x07: case 0x17: case 0x0F: case 0x1F: case 0x1B: case 0x03: case 0x13:   // SLO
                a = UndocAddr(op); Rmw(a, v => { var r = Asl(v); A = NZ(A | r); return r; }); break;
            case 0x27: case 0x37: case 0x2F: case 0x3F: case 0x3B: case 0x23: case 0x33:   // RLA
                a = UndocAddr(op); Rmw(a, v => { var r = Rol(v); A = NZ(A & r); return r; }); break;
            case 0x47: case 0x57: case 0x4F: case 0x5F: case 0x5B: case 0x43: case 0x53:   // SRE
                a = UndocAddr(op); Rmw(a, v => { var r = Lsr(v); A = NZ(A ^ r); return r; }); break;
            case 0x67: case 0x77: case 0x6F: case 0x7F: case 0x7B: case 0x63: case 0x73:   // RRA
                a = UndocAddr(op); Rmw(a, v => { var r = Ror(v); Adc(r); return r; }); break;
            case 0x0B: case 0x2B: A = NZ(A & Rd(Imm())); C = N; break;       // ANC
            case 0x4B: A = Lsr((byte)(A & Rd(Imm()))); break;                // ALR
            case 0x6B:                                                       // ARR
                A = (byte)(A & Rd(Imm())); A = NZ((A >> 1) | (C ? 0x80 : 0));
                C = (A & 0x40) != 0; V = (((A >> 6) ^ (A >> 5)) & 1) != 0; break;
            case 0xCB: { int r = (A & X) - Rd(Imm()); C = r >= 0; X = NZ(r); break; } // SBX
            case 0x1A: case 0x3A: case 0x5A: case 0x7A: case 0xDA: case 0xFA: break;   // NOP
            case 0x80: case 0x82: case 0x89: case 0xC2: case 0xE2: PC++; break;        // NOP #imm
            case 0x04: case 0x44: case 0x64: PC++; break;                              // NOP zp
            case 0x14: case 0x34: case 0x54: case 0x74: case 0xD4: case 0xF4: PC++; break;
            case 0x0C: PC += 2; break;
            case 0x1C: case 0x3C: case 0x5C: case 0x7C: case 0xDC: case 0xFC: AbsX(true); break;
            case 0x9C: a = AbsX(false); Wr(a, (byte)(Y & ((a >> 8) + 1))); break;      // SHY
            case 0x9E: a = AbsY(false); Wr(a, (byte)(X & ((a >> 8) + 1))); break;      // SHX
            default: Jammed = true; break;                                             // KIL/JAM and the rest
        }
    }

    /// <summary>Addressing for the RMW undocumented groups (same layout as their documented cousins).</summary>
    private int UndocAddr(byte op) => (op & 0x1F) switch
    {
        0x03 => IndX(),
        0x07 => Zp(),
        0x0F => Abs(),
        0x13 => IndY(false),
        0x17 => ZpX(),
        0x1B => AbsY(false),
        _ => AbsX(false), // 0x1F
    };
}
