using System.Text;

namespace SAM_UI_Avalonia.Audio.Sid;

/// <summary>Header of a .sid (PSID / RSID) file.</summary>
public sealed class SidFile
{
    public bool IsRsid;
    public ushort Load, Init, Play;
    public int Songs, StartSong;
    public uint Speed;
    public string Name = "", Author = "", Released = "";
    public bool Ntsc, Mos8580;
    public byte[] Data = Array.Empty<byte>();

    public static SidFile Parse(byte[] f)
    {
        f = PowerPacker.Unpack(f);
        if (f.Length < 0x76) throw new InvalidDataException("not a SID file");
        var magic = Encoding.ASCII.GetString(f, 0, 4);
        if (magic != "PSID" && magic != "RSID") throw new InvalidDataException("not a SID file");
        int U16(int o) => (f[o] << 8) | f[o + 1];
        string Str(int o) => Encoding.Latin1.GetString(f, o, 32).TrimEnd('\0', ' ');
        int version = U16(4), offset = U16(6);
        var s = new SidFile
        {
            IsRsid = magic == "RSID",
            Load = (ushort)U16(8),
            Init = (ushort)U16(0x0A),
            Play = (ushort)U16(0x0C),
            Songs = Math.Max(1, U16(0x0E)),
            StartSong = Math.Max(1, U16(0x10)),
            Speed = (uint)((f[0x12] << 24) | (f[0x13] << 16) | (f[0x14] << 8) | f[0x15]),
            Name = Str(0x16),
            Author = Str(0x36),
            Released = Str(0x56),
        };
        if (version >= 2 && f.Length > 0x77)
        {
            int flags = U16(0x76);
            s.Ntsc = ((flags >> 2) & 3) == 2;
            s.Mos8580 = ((flags >> 4) & 3) == 2;
        }
        int start = offset;
        if (s.Load == 0) { s.Load = (ushort)(f[start] | (f[start + 1] << 8)); start += 2; }
        s.Data = f[start..];
        if (s.Init == 0) s.Init = s.Load;
        return s;
    }
}

/// <summary>
/// Plays one subtune: a 64 KB C64 with no ROMs, I/O mapped for SID, CIA timer A and a fake raster,
/// calling the tune's play routine once per frame (VBI 50/60 Hz or the CIA rate the tune sets).
/// </summary>
public sealed class SidSource : ITuneSource
{
    private const ushort Trap = 0xFFF0; // returning here ends a call
    private readonly byte[] _ram = new byte[65536];
    private readonly Cpu6502 _cpu;
    private readonly SidChip _sid;
    private readonly SidFile _f;
    private readonly double _clock;
    private double _samplesPerFrame, _untilFrame;
    private int _cia = 0;
    private ushort _ciaLatch = 0x4025; // PAL 50 Hz default
    private readonly float[] _mono = new float[4096];
    private bool _useIrqVector;
    public int DbgFrames, DbgJams, DbgTimeouts, DbgSidWrites;
    public string DbgInfo => $"load ${_f.Load:X4}-${_f.Load + _f.Data.Length - 1:X4} init ${_f.Init:X4} play ${_f.Play:X4} speed {_f.Speed:X8} irqvec {_useIrqVector} rate {StreamOut.Rate / _samplesPerFrame:0.0}Hz frames {DbgFrames} jams {DbgJams} timeouts {DbgTimeouts} sidwrites {DbgSidWrites} lastPC ${_cpu.PC:X4}";

    public string Title { get; }
    public string Format => _f.IsRsid ? "RSID" : "PSID";
    public double Duration => 0;
    public int Song { get; }
    public int Songs => _f.Songs;

    public SidSource(string path, int song)
    {
        _f = SidFile.Parse(File.ReadAllBytes(path));
        Song = Math.Clamp(song <= 0 ? _f.StartSong : song, 1, _f.Songs);
        _clock = _f.Ntsc ? 1022727 : 985248;
        _sid = new SidChip(_clock, !_f.Mos8580, StreamOut.Rate);
        _cpu = new Cpu6502(Read, Write);
        var name = string.IsNullOrWhiteSpace(_f.Name) ? Path.GetFileNameWithoutExtension(path) : _f.Name;
        Title = _f.Songs > 1 ? $"{name} ({Song}/{_f.Songs})" : name;
        if (!string.IsNullOrWhiteSpace(_f.Author)) Title += " - " + _f.Author;

        int end = Math.Min(65536, _f.Load + _f.Data.Length);
        Array.Copy(_f.Data, 0, _ram, _f.Load, end - _f.Load);
        bool Free(int a, int len) => a + len <= _f.Load || a >= end;
        // kernal IRQ exit ($EA31/$EA81: PLA TAY PLA TAX PLA RTI) for tunes that chain through $0314
        byte[] irqExit = { 0x68, 0xA8, 0x68, 0xAA, 0x68, 0x40 };
        if (Free(0xEA31, 6)) Array.Copy(irqExit, 0, _ram, 0xEA31, 6);
        if (Free(0xEA81, 6)) Array.Copy(irqExit, 0, _ram, 0xEA81, 6);
        _ram[0x01] = 0x37;
        _ram[0x02A6] = (byte)(_f.Ntsc ? 0 : 1); // PAL/NTSC flag some players check

        // init
        _cpu.A = (byte)(Song - 1);
        Call(_f.Init, 2_000_000);

        bool cia = Song <= 32 && ((_f.Speed >> (Song - 1)) & 1) != 0;
        if (_f.IsRsid || _f.Play == 0)
        {
            _useIrqVector = true;
            cia = true;
        }
        double hz = cia ? _clock / (_ciaLatch + 1.0) : (_f.Ntsc ? 60 : 50);
        if (hz < 20 || hz > 1000) hz = _f.Ntsc ? 60 : 50;
        _samplesPerFrame = StreamOut.Rate / hz;
    }

    // ---- bus
    private byte Read(ushort a)
    {
        if (a >= 0xD400 && a <= 0xD7FF && IoVisible) return _sid.Read(a & 0x1F);
        if (a >= 0xD000 && a <= 0xDFFF && IoVisible)
        {
            if (a == 0xD012) return (byte)(_cpu.Cycles / 63 % 312);
            if (a == 0xD011) return (byte)((_cpu.Cycles / 63 % 312) > 255 ? 0x9B : 0x1B);
            if (a == 0xDC04) return (byte)_ciaLatch;
            if (a == 0xDC05) return (byte)(_ciaLatch >> 8);
            if (a == 0xDC0D || a == 0xDD0D) return 0x81;
        }
        return _ram[a];
    }

    private bool IoVisible => (_ram[1] & 3) != 0 && (_ram[1] & 4) != 0;

    private void Write(ushort a, byte v)
    {
        if (a >= 0xD400 && a <= 0xD7FF && IoVisible) { DbgSidWrites++; _sid.Write(a & 0x1F, v); return; }
        if (a >= 0xD000 && a <= 0xDFFF && IoVisible)
        {
            if (a == 0xDC04) _ciaLatch = (ushort)((_ciaLatch & 0xFF00) | v);
            else if (a == 0xDC05) _ciaLatch = (ushort)((_ciaLatch & 0x00FF) | (v << 8));
            _cia++;
            return;
        }
        _ram[a] = v;
    }

    /// <summary>JSR to <paramref name="addr"/> and run until it returns to the trap (or the cycle budget runs out).</summary>
    private void Call(ushort addr, long budget, bool asIrq = false)
    {
        _cpu.Jammed = false;
        _cpu.SP = 0xFF;
        if (asIrq)
        {
            // as if the kernal's IRQ entry ran: return address + flags, then A, X, Y
            _cpu.Push((byte)(Trap >> 8)); _cpu.Push((byte)(Trap & 0xFF)); _cpu.Push(_cpu.P);
            _cpu.Push(_cpu.A); _cpu.Push(_cpu.X); _cpu.Push(_cpu.Y);
        }
        else
        {
            _cpu.Push((byte)((Trap - 1) >> 8)); _cpu.Push((byte)((Trap - 1) & 0xFF));
        }
        _cpu.PC = addr;
        long stop = _cpu.Cycles + budget;
        while (_cpu.PC != Trap && !_cpu.Jammed && _cpu.Cycles < stop) _cpu.Step();
        if (_cpu.Jammed) DbgJams++; else if (_cpu.PC != Trap) DbgTimeouts++;
    }

    private void PlayFrame()
    {
        DbgFrames++;
        if (!_useIrqVector) { Call(_f.Play, 200_000); return; }
        bool kernalOff = (_ram[1] & 2) == 0;
        ushort vec = kernalOff ? (ushort)(_ram[0xFFFE] | (_ram[0xFFFF] << 8)) : (ushort)(_ram[0x0314] | (_ram[0x0315] << 8));
        if (vec == 0) vec = _f.Play != 0 ? _f.Play : (ushort)0;
        if (vec == 0) return;
        if (kernalOff)
        {
            _cpu.SP = 0xFF;
            _cpu.Push((byte)(Trap >> 8)); _cpu.Push((byte)(Trap & 0xFF)); _cpu.Push(_cpu.P);
            _cpu.PC = vec;
            long stop = _cpu.Cycles + 200_000;
            while (_cpu.PC != Trap && !_cpu.Jammed && _cpu.Cycles < stop) _cpu.Step();
        }
        else Call(vec, 200_000, asIrq: true);
    }

    public int Render(short[] buffer, int frames)
    {
        int done = 0;
        while (done < frames)
        {
            if (_untilFrame <= 0)
            {
                PlayFrame();
                _untilFrame += _samplesPerFrame;
            }
            int n = Math.Min(frames - done, Math.Max(1, (int)Math.Ceiling(_untilFrame)));
            n = Math.Min(n, _mono.Length);
            _sid.Render(_mono, n);
            for (int i = 0; i < n; i++)
            {
                short s = (short)Math.Clamp(_mono[i] * 30000, -32767, 32767);
                buffer[(done + i) * 2] = s;
                buffer[(done + i) * 2 + 1] = s;
            }
            done += n;
            _untilFrame -= n;
        }
        return frames;
    }

    public void Dispose() { }

    public static (int Songs, int Start) SongInfo(string path)
    {
        try { var f = SidFile.Parse(File.ReadAllBytes(path)); return (f.Songs, f.StartSong); } catch { return (0, 0); }
    }
}
