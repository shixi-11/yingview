// Source: https://github.com/cssxn/UserChoiceLatestHash/tree/662070e5cdea4496bc474f4bdfd81f77d4cabab3
// Copyright (c) 2026 cssxn. MIT License; see ThirdParty/UserChoiceLatestHash.txt.
// Faithful managed port of UserChoiceLatestHash, commit
// 662070e5cdea4496bc474f4bdfd81f77d4cabab3 (HashCodec.cpp / HashTables.cpp).
namespace YingView.Files.AssociationHash;

internal static partial class LatestAssociationHash
{
    private static uint LowMul(uint a, uint b) => unchecked(a * b);

private static bool MixA(uint[] words, int dword_count, uint[] md5_words, uint[] seed, uint[] out_pair)
{
    unchecked
    {
    if ((dword_count & 1) != 0)
    {
        return false;
    }

    uint md0 = md5_words[0] | 1U;
    uint md1 = (md5_words[1] | 1U) + 0x13DB0000U;
    uint seed0 = seed[0];
    uint seed1 = seed[1];
    uint seed2 = seed[2];
    uint seed3 = seed[3];

    uint sum = 0U;
    uint acc = 0U;
    int p = 0;
    int remaining = dword_count;

    if (((((uint)remaining - 2U) >> 1) + 1U) != 0U)
    {
        uint loops = (((uint)remaining - 2U) >> 1) + 1U;
        uint mul = md0 + seed0;
        while (loops-- != 0U)
        {
            acc += words[p];
            remaining -= 2;

            uint x = acc;
            uint hi = x >> 16;
            p += 2;

            uint t = LowMul(x, mul) - LowMul(hi, seed3);
            mul = md0 + seed0;

            uint s = LowMul(t, seed1) + LowMul(t >> 16, 0x689B6B9FU);
            uint part1 = LowMul(s, 0xEA970001U) - LowMul(s >> 16, 0x3C101569U);

            uint w = words[p - 1] + part1;
            uint t2 = LowMul(w, md1) - LowMul(w >> 16, seed2);
            uint temp = LowMul(t2, 0x59C3AF2DU) - LowMul(t2 >> 16, 0x2232E0F1U);
            acc = LowMul(temp >> 16, 0x35BD1EC9U) + LowMul(temp, 0x1EC90001U);
            sum += part1 + acc;
        }
    }

    if (remaining == 1)
    {
        acc += words[p];
        uint t = LowMul(acc, md0 + seed0) - LowMul(acc >> 16, seed3);
        uint part = LowMul(t, seed1) - LowMul(t >> 16, 0x689B6B9FU);
        part += LowMul(t >> 16, 0x39646B9FU);

        uint t2 = LowMul(part, md1) - LowMul(part >> 16, seed2);
        uint temp = LowMul(t2, 0x59C3AF2DU) - LowMul(t2 >> 16, 0x2232E0F1U);
        uint extra = LowMul(t2, 0x2A18AF2DU)
                        + LowMul(t2 >> 16, 0x02941F0FU)
                        + LowMul(temp >> 16, 0x35BD1EC9U);
        acc = extra;
        sum += part + acc;
    }

    out_pair[0] = acc;
    out_pair[1] = sum;
    return true;
    }
}

private static bool MixB(uint[] words, int dword_count, uint[] md5_words, uint[] seed, uint[] out_pair)
{
    unchecked
    {
    if ((dword_count & 1) != 0)
    {
        return false;
    }

    uint seed0 = seed[0];
    uint seed1 = seed[1];
    uint seed2 = seed[2];
    uint seed3 = seed[3];
    uint md1 = md5_words[1] | 1U;
    uint md0 = md5_words[0] | 1U;

    uint sum = 0U;
    uint acc = 0U;
    int p = 0;
    int remaining = dword_count;

    if (((((uint)remaining - 2U) >> 1) + 1U) != 0U)
    {
        uint loops = (((uint)remaining - 2U) >> 1) + 1U;
        while (loops-- != 0U)
        {
            uint x = words[p] + acc;
            remaining -= 2;
            p += 2;

            uint t = LowMul(x, md0);
            uint a = LowMul(t, 0xB1110000U) - LowMul(t >> 16, 0x30674EEFU);
            uint b = LowMul(a, seed2);
            uint c = b - LowMul(a >> 16, 0x78F7A461U);
            c = LowMul(c >> 16, seed0) - LowMul(a >> 16, 0x164D0000U) - LowMul(b, 0x46930000U);
            uint d = LowMul(c >> 16, 0x257E1D83U) + LowMul(c, 0x1D830000U);

            uint e = words[p - 1] + d;
            uint f = LowMul(e, md1);
            uint g = LowMul(f, 0x16F50000U) - LowMul(f >> 16, 0x5D8BE90BU);
            uint h = LowMul(g >> 16, seed3);
            uint i = LowMul(g, 0x69010000U) + h;
            uint j = LowMul(i, seed1);
            uint k = ((0U - h) - LowMul(g, 0x69010000U)) >> 16;
            uint m = LowMul(k, 0x7C932B89U) - j;
            uint n = LowMul(k, 0xF2310000U) - LowMul(m >> 16, 0x405B6097U);
            n += LowMul(j, 0x60970000U);

            acc = n;
            sum += d + acc;
        }
    }

    if (remaining == 1)
    {
        uint x = words[p] + acc;
        uint t = LowMul(x, md0);
        uint a = LowMul(t, 0xB1110000U) - LowMul(t >> 16, 0x30674EEFU);
        uint b = LowMul(a, seed2);
        uint c = LowMul(a >> 16, 0xE9B30000U) - LowMul(b, 0x46930000U) + LowMul((b - LowMul(a >> 16, 0x78F7A461U)) >> 16, seed0);
        uint d = LowMul(c >> 16, 0x257E1D83U) + LowMul(c, 0x1D830000U);

        uint e = LowMul(d, md1);
        uint f = LowMul(e, 0x16F50000U) - LowMul(e >> 16, 0x5D8BE90BU);
        uint h = LowMul(f >> 16, seed3);
        uint i = LowMul(f, 0x69010000U) + h;
        uint j = LowMul(i, seed1);
        uint k = ((0U - h) - LowMul(f, 0x69010000U)) >> 16;
        uint m = LowMul(k, 0x7C932B89U) - j;
        uint n = LowMul(k, 0xF2310000U) - LowMul(m >> 16, 0x405B6097U);
        n += LowMul(j, 0x60970000U);

        acc = n;
        sum += d + acc;
    }

    out_pair[0] = acc;
    out_pair[1] = sum;
    return true;
    }
}

}
