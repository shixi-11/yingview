// Ported from PS-SFTA Get-Hash by Danyfirex and Dany3j, Copyright (c) 2022 Danysys.
// Fixed source: https://github.com/DanysysTeam/PS-SFTA/blob/22a32292e576afc976a1167d92b50741ef523066/SFTA.ps1
// The original script is MIT licensed; see ThirdParty/PS-SFTA.txt.
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace YingView.Files.AssociationHash;

internal static class LegacyAssociationHash
{
    internal static string Compute(string canonicalInput)
    {
        ArgumentNullException.ThrowIfNull(canonicalInput);
        var textBytes = Encoding.Unicode.GetBytes(canonicalInput);
        var bytes = new byte[textBytes.Length + 2];
        textBytes.CopyTo(bytes, 0); // UTF-16LE, followed by the terminating wide NUL.
        var md5 = MD5.HashData(bytes);
        var lengthBase = checked(canonicalInput.Length * 2 + 2);
        var length = unchecked(((lengthBase & 4) <= 1 ? 1 : 0) + (int)ShiftRight(lengthBase, 2) - 1);
        if (length <= 1)
            return string.Empty;

        uint cache = 0, outHash1 = 0, outHash2 = 0;
        var md51 = unchecked(ReadInt32(md5, 0) | 1u) + 0x69FB0000u;
        var md52 = unchecked((ReadInt32(md5, 4) | 1u) + 0x13DB0000u);
        var index = ShiftRight(length - 2, 1);
        var counter = index + 1;
        var pData = 0;
        while (counter-- != 0)
        {
            var r0 = ReadInt32(bytes, pData) + outHash1;
            var r1 = ReadInt32(bytes, pData + 4);
            pData += 8;
            var r20 = unchecked(Mul(r0, md51) - 0x10FA9605u * (uint)ShiftRight(unchecked((int)r0), 16));
            var r21 = unchecked(Mul(0x79F8A395u, r20) + 0x689B6B9Fu * (uint)ShiftRight(unchecked((int)r20), 16));
            var r3 = unchecked(Mul(0xEA970001u, r21) - 0x3C101569u * (uint)ShiftRight(unchecked((int)r21), 16));
            var r40 = unchecked(r3 + r1);
            var r50 = unchecked(cache + r3);
            var r60 = unchecked(Mul(r40, md52) - 0x3CE8EC25u * (uint)ShiftRight(unchecked((int)r40), 16));
            var r61 = unchecked(Mul(0x59C3AF2Du, r60) - 0x2232E0F1u * (uint)ShiftRight(unchecked((int)r60), 16));
            outHash1 = unchecked(Mul(0x1EC90001u, r61) + 0x35BD1EC9u * (uint)ShiftRight(unchecked((int)r61), 16));
            outHash2 = unchecked(r50 + outHash1);
            cache = outHash2;
        }

        var first1 = outHash1;
        var first2 = outHash2;
        cache = outHash1 = outHash2 = 0;
        md51 = ReadInt32(md5, 0) | 1u;
        md52 = ReadInt32(md5, 4) | 1u;
        index = ShiftRight(length - 2, 1);
        counter = index + 1;
        pData = 0;
        while (counter-- != 0)
        {
            var r0 = ReadInt32(bytes, pData) + outHash1;
            pData += 8;
            var r10 = Mul(r0, md51);
            var r11 = unchecked(Mul(0xB1110000u, r10) - 0x30674EEFu * (uint)ShiftRight(unchecked((int)r10), 16));
            var r20 = unchecked(Mul(0x5B9F0000u, r11) - 0x78F7A461u * (uint)ShiftRight(unchecked((int)r11), 16));
            var r21 = unchecked(0x12CEB96Du * (uint)ShiftRight(unchecked((int)r20), 16) - Mul(0x46930000u, r20));
            var r3 = unchecked(Mul(0x1D830000u, r21) + 0x257E1D83u * (uint)ShiftRight(unchecked((int)r21), 16));
            var r40 = Mul(md52, unchecked(r3 + ReadInt32(bytes, pData - 4)));
            var r41 = unchecked(Mul(0x16F50000u, r40) - 0x5D8BE90Bu * (uint)ShiftRight(unchecked((int)r40), 16));
            var r50 = unchecked(Mul(0x96FF0000u, r41) - 0x2C7C6901u * (uint)ShiftRight(unchecked((int)r41), 16));
            var r51 = unchecked(Mul(0x2B890000u, r50) + 0x7C932B89u * (uint)ShiftRight(unchecked((int)r50), 16));
            outHash1 = unchecked(Mul(0x9F690000u, r51) - 0x405B6097u * (uint)ShiftRight(unchecked((int)r51), 16));
            outHash2 = unchecked(outHash1 + cache + r3);
            cache = outHash2;
        }

        Span<byte> final = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(final, outHash1 ^ first1);
        BinaryPrimitives.WriteUInt32LittleEndian(final[4..], outHash2 ^ first2);
        return Convert.ToBase64String(final);
    }

    private static uint ReadInt32(byte[] data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
    private static uint Mul(uint left, uint right) => unchecked((uint)((ulong)left * right));
    private static long ShiftRight(long value, int count) => (value & 0x80000000L) != 0
        ? (value >> count) ^ 0xFFFF0000L
        : value >> count;
    private static int ShiftRight(int value, int count) => unchecked((int)ShiftRight((long)value, count));
}






