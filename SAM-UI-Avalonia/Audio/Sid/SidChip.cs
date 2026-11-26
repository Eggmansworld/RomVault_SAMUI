namespace SAM_UI_Avalonia.Audio.Sid;

/// <summary>
/// MOS 6581/8580 SID approximation: three voices with 24-bit oscillators, triangle/saw/pulse/noise
/// (combined waveforms ANDed), ring mod, hard sync, ADSR with the real rate tables and exponential
/// decay steps, and a two-pole state-variable filter. Clocked in per-sample chunks.
/// </summary>
public sealed class SidChip
{
    private static readonly int[] RatePeriod = { 9, 32, 63, 95, 149, 220, 267, 313, 392, 977, 1954, 3126, 3907, 11720, 19532, 31251 };

    private sealed class Voice
    {
        public uint Acc, Freq, Pw;
        public byte Ctrl, Ad, Sr;
        public uint Lfsr = 0x7FFFF8;
        public int Env;
        public int State; // 0 attack, 1 decay/sustain, 2 release
        public int RateCounter, ExpCounter;
        public bool MsbRose;
        public bool Gate => (Ctrl & 1) != 0;
    }

    private readonly Voice[] _v = { new(), new(), new() };
    private readonly byte[] _regs = new byte[32];
    private readonly double _clock;
    private readonly bool _is6581;
    private double _cyclesPerSample, _cycleFrac;
    private double _lp, _bp;
    private double _dc; // DC blocker state
    private double _prevOut;

    public SidChip(double clockHz, bool is6581, int sampleRate)
    {
        _clock = clockHz;
        _is6581 = is6581;
        _cyclesPerSample = clockHz / sampleRate;
    }

    public byte Read(int reg)
    {
        reg &= 0x1F;
        return reg switch
        {
            0x1B => (byte)(Output(2, false) >> 4),
            0x1C => (byte)_v[2].Env,
            0x19 or 0x1A => 0xFF, // paddles
            _ => _regs[reg],
        };
    }

    public void Write(int reg, byte val)
    {
        reg &= 0x1F;
        _regs[reg] = val;
        if (reg < 21)
        {
            var v = _v[reg / 7];
            switch (reg % 7)
            {
                case 0: v.Freq = (v.Freq & 0xFF00) | val; break;
                case 1: v.Freq = (v.Freq & 0x00FF) | ((uint)val << 8); break;
                case 2: v.Pw = (v.Pw & 0xF00) | val; break;
                case 3: v.Pw = (v.Pw & 0x0FF) | ((uint)(val & 0x0F) << 8); break;
                case 4:
                    bool wasGate = v.Gate;
                    v.Ctrl = val;
                    if ((val & 8) != 0) { v.Acc = 0; v.Lfsr = 0x7FFFF8; }
                    if (!wasGate && v.Gate) { v.State = 0; }
                    else if (wasGate && !v.Gate) { v.State = 2; }
                    break;
                case 5: v.Ad = val; break;
                case 6: v.Sr = val; break;
            }
        }
    }

    private int Output(int i, bool forMix)
    {
        var v = _v[i];
        int ctrl = v.Ctrl;
        int wave = ctrl >> 4;
        if (wave == 0) return forMix ? 0x800 : 0;
        uint acc = v.Acc;
        int o = 0xFFF;
        if ((wave & 1) != 0) // triangle
        {
            uint msb = acc & 0x800000;
            if ((ctrl & 4) != 0 && (_v[(i + 2) % 3].Acc & 0x800000) != 0) msb ^= 0x800000; // ring mod
            o &= (int)(((msb != 0 ? ~acc : acc) >> 11) & 0xFFF);
        }
        if ((wave & 2) != 0) o &= (int)(acc >> 12);                                         // saw
        if ((wave & 4) != 0) o &= (ctrl & 8) != 0 || (acc >> 12) >= v.Pw ? 0xFFF : 0;       // pulse
        if ((wave & 8) != 0)                                                                 // noise
        {
            uint l = v.Lfsr;
            int n = (int)(((l >> 20) & 1) << 11 | ((l >> 18) & 1) << 10 | ((l >> 14) & 1) << 9 | ((l >> 11) & 1) << 8
                        | ((l >> 9) & 1) << 7 | ((l >> 5) & 1) << 6 | ((l >> 2) & 1) << 5 | (l & 1) << 4);
            o &= n;
        }
        return o;
    }

    private void ClockVoice(int i, int cycles)
    {
        var v = _v[i];
        if ((v.Ctrl & 8) == 0)
        {
            uint old = v.Acc;
            long adv = (long)v.Freq * cycles;
            long nu = old + adv;
            // noise LFSR steps each time bit 19 rises
            long clocks = ((nu + 0x80000) >> 20) - ((old + 0x80000L) >> 20);
            for (long k = 0; k < Math.Min(clocks, 64); k++)
            {
                uint bit = ((v.Lfsr >> 22) ^ (v.Lfsr >> 17)) & 1;
                v.Lfsr = ((v.Lfsr << 1) | bit) & 0x7FFFFF;
            }
            v.Acc = (uint)(nu & 0xFFFFFF);
            v.MsbRose = (old & 0x800000) == 0 && ((nu & 0x800000) != 0 || adv >= 0x1000000);
        }
        else v.MsbRose = false;

        // envelope
        int period = v.State switch { 0 => RatePeriod[v.Ad >> 4], 1 => RatePeriod[v.Ad & 0xF], _ => RatePeriod[v.Sr & 0xF] };
        v.RateCounter += cycles;
        while (v.RateCounter >= period)
        {
            v.RateCounter -= period;
            if (v.State == 0)
            {
                if (++v.Env >= 0xFF) { v.Env = 0xFF; v.State = 1; }
                continue;
            }
            int expPeriod = v.Env >= 0x5D ? 1 : v.Env >= 0x36 ? 2 : v.Env >= 0x1A ? 4 : v.Env >= 0x0E ? 8 : v.Env >= 0x06 ? 16 : 30;
            if (++v.ExpCounter < expPeriod) continue;
            v.ExpCounter = 0;
            if (v.State == 1)
            {
                int sustain = (v.Sr >> 4) * 0x11;
                if (v.Env > sustain) v.Env--;
            }
            else if (v.Env > 0) v.Env--;
        }
    }

    /// <summary>Renders mono samples in the range ~[-1, 1].</summary>
    public void Render(float[] dst, int count)
    {
        for (int s = 0; s < count; s++)
        {
            _cycleFrac += _cyclesPerSample;
            int cycles = (int)_cycleFrac;
            _cycleFrac -= cycles;

            byte route = _regs[0x17];
            byte mode = _regs[0x18];
            double filtIn = 0, direct = 0;

            // oversample the oscillators and average: cuts the aliasing of naive pulse/saw edges
            const int Over = 6;
            int left = cycles;
            for (int k = 0; k < Over; k++)
            {
                int c = left / (Over - k);
                left -= c;
                for (int i = 0; i < 3; i++) ClockVoice(i, c);
                // hard sync: a voice resets when the previous one's MSB rose
                for (int i = 0; i < 3; i++)
                    if ((_v[i].Ctrl & 2) != 0 && _v[(i + 2) % 3].MsbRose) _v[i].Acc = 0;
                for (int i = 0; i < 3; i++)
                {
                    double o = (Output(i, true) - 0x800) * _v[i].Env / (2048.0 * 255.0 * Over);
                    if ((route & (1 << i)) != 0) filtIn += o;
                    else if (!(i == 2 && (mode & 0x80) != 0)) direct += o;
                }
            }

            int fc = (_regs[0x15] & 7) | (_regs[0x16] << 3);
            double cutoff = _is6581
                ? 200 + 9000 * Math.Pow(fc / 2047.0, 1.6)
                : 30 + 12000 * (fc / 2047.0);
            double w = 2 * Math.Sin(Math.PI * Math.Min(cutoff, 16000) / (_clock / _cyclesPerSample));
            double res = (_regs[0x17] >> 4) / 15.0;
            double damp = 1.4 - res * 1.15;
            double hp = filtIn - _lp - damp * _bp;
            _bp += w * hp;
            _lp += w * _bp;
            double filtered = ((mode & 0x10) != 0 ? _lp : 0) + ((mode & 0x20) != 0 ? _bp : 0) + ((mode & 0x40) != 0 ? hp : 0);

            double vol = (mode & 0x0F) / 15.0;
            double outv = (direct + filtered) * vol * 0.26;
            // gentle DC blocker
            _dc = outv - _prevOut + 0.9995 * _dc;
            _prevOut = outv;
            dst[s] = (float)(Math.Abs(_dc) < 0.7 ? _dc : Math.Sign(_dc) * (0.7 + 0.3 * Math.Tanh((Math.Abs(_dc) - 0.7) / 0.3))); // soft knee
        }
    }
}
