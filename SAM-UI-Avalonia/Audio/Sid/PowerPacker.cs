namespace SAM_UI_Avalonia.Audio.Sid;

/// <summary>
/// PowerPacker "PP20" decruncher. Amiga-era .sid collections (PlaySID) were often crunched with it.
/// The packed stream is read backwards from the end, bits LSB-first, and the output is also written back to front.
/// </summary>
public static class PowerPacker
{
    public static bool IsPacked(byte[] d) => d.Length > 12 && d[0] == 'P' && d[1] == 'P' && d[2] == '2' && d[3] == '0';

    public static byte[] Unpack(byte[] d)
    {
        if (!IsPacked(d)) return d;
        int len = d.Length;
        int outLen = (d[len - 4] << 16) | (d[len - 3] << 8) | d[len - 2];
        int skip = d[len - 1];
        byte[] offLens = { d[4], d[5], d[6], d[7] };
        if (outLen <= 0 || outLen > 16 * 1024 * 1024) throw new InvalidDataException("bad PowerPacker length");

        var dst = new byte[outLen];
        int src = len - 4;          // packed data runs from 8 up to here; read backwards
        const int srcStart = 8;
        uint bitBuf = 0;
        int bitsLeft = 0;

        uint Bits(int n)
        {
            while (bitsLeft < n)
            {
                if (src <= srcStart) throw new InvalidDataException("PowerPacker data truncated");
                bitBuf |= (uint)d[--src] << bitsLeft;
                bitsLeft += 8;
            }
            uint v = 0;
            for (int i = 0; i < n; i++) { v = (v << 1) | (bitBuf & 1); bitBuf >>= 1; }
            bitsLeft -= n;
            return v;
        }

        Bits(skip);
        int outPos = outLen, written = 0;
        while (written < outLen)
        {
            if (Bits(1) == 0)
            {
                int run = 1;
                uint x;
                do { x = Bits(2); run += (int)x; } while (x == 3);
                for (; run > 0; run--) { dst[--outPos] = (byte)Bits(8); written++; }
                if (written == outLen) break;
            }
            uint sel = Bits(2);
            int offBits = offLens[sel];
            int todo = (int)sel + 2;
            int offset;
            if (sel == 3)
            {
                if (Bits(1) == 0) offBits = 7;
                offset = (int)Bits(offBits);
                uint x;
                do { x = Bits(3); todo += (int)x; } while (x == 7);
            }
            else offset = (int)Bits(offBits);
            for (; todo > 0; todo--)
            {
                int from = outPos + offset;
                if (from >= outLen || outPos == 0) throw new InvalidDataException("PowerPacker offset out of range");
                dst[--outPos] = dst[from];
                written++;
            }
        }
        return dst;
    }
}
