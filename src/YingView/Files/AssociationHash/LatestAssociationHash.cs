// Source: https://github.com/cssxn/UserChoiceLatestHash/tree/662070e5cdea4496bc474f4bdfd81f77d4cabab3
// Copyright (c) 2026 cssxn. MIT License; see ThirdParty/UserChoiceLatestHash.txt.
// Port of HashCodec.cpp and HashTables.cpp at commit
// 662070e5cdea4496bc474f4bdfd81f77d4cabab3. No registry or shell operations.
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace YingView.Files.AssociationHash;

internal static partial class LatestAssociationHash
{
    private const ulong RepeatSentinel = 0xFBFFFFFFFFFFFFFFUL;
    private const ulong RepeatTokenA = 0xFE00000000000000UL;
    private const ulong RepeatTokenB = 0xFE00800000000000UL;
    private const ulong RepeatTokenC = 0xFF00000000000000UL;
    private const ulong RepeatTokenD = 0xFE007DFFFFFFFFFFUL;
    private const ulong RepeatTokenE = 0xFE00801F7FFFFFFFUL;
    private const uint SeenModulo = 0x2803U;
    private static readonly uint[] SeedA = [0x69FB0000U, 0x79F8A395U, 0x3CE8EC25U, 0x10FA9605U];
    private static readonly uint[] SeedB = [0x12CEB96DU, 0x2B890000U, 0x5B9F0000U, 0x2C7C6901U];

    private sealed class EncoderState
    {
        internal readonly List<ulong> Entries = new(4096) { 0UL };
        internal readonly ulong[] Seen = new ulong[161];
        internal ulong Last = RepeatSentinel;
        internal uint Repeat;
        internal uint TotalUnits;
        internal ulong Count => (ulong)Entries.Count;
    }

    /// <summary>Computes the pinned UserChoiceLatest algorithm over canonical UTF-16 input.</summary>
    internal static string Compute(string canonicalInput)
    {
        ArgumentNullException.ThrowIfNull(canonicalInput);
        // The upstream standalone executable leaves the CRT in the C locale:
        // towlower maps ASCII A-Z, preserving all other UTF-16 code units. Do not
        // normalize, expand, or replace surrogate pairs (including unpaired ones).
        char[] input = canonicalInput.ToCharArray();
        for (int i = 0; i < input.Length; i++)
            if (input[i] is >= 'A' and <= 'Z') input[i] = (char)(input[i] + ('a' - 'A'));

        var state = new EncoderState();
        uint firstPassChars = FirstPass(state, input);
        if (firstPassChars > input.Length)
            throw new InvalidOperationException("The pinned UserChoiceLatest encoder consumed beyond the input.");
        SecondPass(state, input, checked((int)firstPassChars));
        state.Entries[0] = state.TotalUnits | (state.Count << 32);

        var packed = new List<ushort>();
        foreach (ulong value in state.Entries)
            for (int shift = 0; shift < 64; shift += 16)
            {
                ushort part = unchecked((ushort)(value >> shift));
                if (part != 0) packed.Add(part);
            }
        packed.Add(0);
        byte[] bytes = new byte[checked(packed.Count * 2)];
        for (int i = 0; i < packed.Count; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), packed[i]);
        byte[] digest = MD5.HashData(bytes);
        var md5Words = new uint[4];
        for (int i = 0; i < 4; i++)
            md5Words[i] = BinaryPrimitives.ReadUInt32LittleEndian(digest.AsSpan(i * 4));

        int dwordLength = bytes.Length >> 2;
        int mixCount = (bytes.Length & 4) == 0 ? dwordLength : dwordLength - 1;
        var words = new uint[(packed.Count + 1) / 2];
        for (int i = 0; i < words.Length; i++)
        {
            uint lo = packed[i * 2];
            uint hi = i * 2 + 1 < packed.Count ? packed[i * 2 + 1] : 0U;
            words[i] = lo | (hi << 16);
        }
        var pairA = new uint[2];
        var pairB = new uint[2];
        if (mixCount >= 2)
        {
            MixA(words, mixCount, md5Words, SeedA, pairA);
            MixB(words, mixCount, md5Words, SeedB, pairB);
        }
        Span<byte> result = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(result, pairA[0] ^ pairB[0]);
        BinaryPrimitives.WriteUInt32LittleEndian(result[4..], pairA[1] ^ pairB[1]);
        return Convert.ToBase64String(result);
    }

    private static bool TestSeen(EncoderState state, ulong value)
    {
        uint index = (uint)(value % SeenModulo);
        return (state.Seen[index >> 6] & (1UL << (int)(index & 63))) != 0;
    }

    private static void PushSeen(EncoderState state, ulong value)
    {
        uint index = (uint)(value % SeenModulo);
        state.Seen[index >> 6] |= 1UL << (int)(index & 63);
        state.Entries.Add(value);
    }

    private static bool FindPrevious(EncoderState state, ulong value, out ulong distance)
    {
        distance = 0;
        if (!TestSeen(state, value) || state.Count == 0) return false;
        int start = state.Entries.Count > 0x400 ? state.Entries.Count - 0x400 : 0;
        for (int index = state.Entries.Count - 1; index >= start; index--)
            if (state.Entries[index] == value)
            {
                distance = state.Count - (ulong)index;
                return true;
            }
        return false;
    }

    private static byte ClassFromWord(char ch)
    {
        byte hi = (byte)(ch >> 8);
        return hi != 0 ? (byte)(LatestHashTables.ByteClassTable[hi] + 8)
            : LatestHashTables.ByteClassTable[ch & 0xFF];
    }

    private static bool BuildToken(char[] input, int cursor, out ulong token, out byte kind, out byte length)
    {
        token = 0; kind = 0; length = 0;
        if (cursor < 0)
            throw new InvalidOperationException("The pinned UserChoiceLatest encoder rewound before the input.");
        if (cursor >= input.Length) return false;
        int code = LatestHashTables.TokenRegion[ClassFromWord(input[cursor]) + 256];
        int tokenLength = LatestHashTables.TokenRegion[code];
        if (cursor + tokenLength > input.Length) return false;
        int tableBase = 168 * code;
        for (;;)
        {
            int position = 0;
            if (tokenLength == 0) break;
            int probe = cursor;
            while ((ulong)input[probe] < (1UL << LatestHashTables.TokenBitWidthTable[tableBase + position]))
            {
                position++; probe++;
                if (position >= tokenLength) goto Done;
            }
            code++;
            tableBase = 168 * code;
            tokenLength = LatestHashTables.TokenRegion[code];
            if (tokenLength > input.Length - cursor) break;
        }
    Done:
        token = (ulong)(byte)code << 56;
        int shift = 56;
        for (int i = 0; i < (byte)tokenLength; i++)
        {
            shift -= LatestHashTables.TokenBitWidthTable[tableBase + i];
            token |= (ulong)input[cursor + i] << shift;
        }
        kind = (byte)code; length = (byte)tokenLength;
        return true;
    }

    private static bool BuildTokenSecondPass(char[] input, int cursor, out ulong token, out byte kind, out byte length)
    {
        token = 0; kind = 0; length = 0;
        if (cursor < 0)
            throw new InvalidOperationException("The pinned UserChoiceLatest encoder rewound before the input.");
        if (cursor >= input.Length) return false;
        int remain = input.Length - cursor;
        int code = LatestHashTables.TokenRegion[ClassFromWord(input[cursor]) + 256];
        int tokenLength = LatestHashTables.TokenRegion[code];
        if (tokenLength > remain)
        {
            code = LatestHashTables.FallbackTokenCodeTable[remain];
            tokenLength = LatestHashTables.TokenRegion[code];
        }
        int tableBase = 168 * code;
    Retry:
        int position = 0;
        if (tokenLength != 0)
        {
            int probe = cursor;
            while (probe < input.Length)
            {
                if ((ulong)input[probe] >= (1UL << LatestHashTables.TokenBitWidthTable[tableBase + position]))
                {
                    code++;
                    tableBase = 168 * code;
                    tokenLength = LatestHashTables.TokenRegion[code];
                    if (tokenLength <= remain) goto Retry;
                    break;
                }
                position++; probe++;
                if (position >= tokenLength) break;
            }
        }
        token = (ulong)(byte)code << 56;
        int shift = 56;
        for (int i = 0; i < (byte)tokenLength; i++)
        {
            if (cursor + i >= input.Length) break;
            shift -= LatestHashTables.TokenBitWidthTable[tableBase + i];
            token |= (ulong)input[cursor + i] << shift;
        }
        kind = (byte)code; length = (byte)tokenLength;
        return true;
    }

    // Shared verbatim repeat branch of EmitFirstPass / EmitSecondPass.
    private static bool EmitRepeated(EncoderState state, ref int cursor, ulong token, byte kind)
    {
        if (state.Repeat == 0 || !FindPrevious(state, state.Last, out ulong distance)) return false;
        uint repMinusOne = state.Repeat - 1;
        ulong entry;
        if (distance >= 0x800UL)
        {
            PushSeen(state, (token >> 17) | ((ulong)repMinusOne << 48) | RepeatTokenA);
            entry = state.Last;
        }
        else
            entry = (token >> 27)
                | ((ulong)((((ushort)distance - 1U) & 0x03FFU) | (repMinusOne << 11)) << 37)
                | RepeatTokenB;
        PushSeen(state, entry);
        state.Last = RepeatSentinel;
        state.Repeat = 0;
        cursor -= distance >= 0x800UL ? LatestHashTables.LongRewindTable[kind] : LatestHashTables.ShortRewindTable[kind];
        return true;
    }

    private static void EmitFirstPass(EncoderState state, ref int cursor, int end, char[] input, ulong token, byte kind)
    {
        if (token == state.Last && state.Repeat < 0xFFU) { state.Repeat++; return; }
        if (state.Repeat != 0)
        {
            if (EmitRepeated(state, ref cursor, token, kind)) return;
        }
        else if (FindPrevious(state, token, out ulong distance) && cursor < end)
        {
            byte nextKind = LatestHashTables.TokenRegion[ClassFromWord(input[cursor]) + 256];
            ulong entry = distance >= 0x800UL ? token : (token >> 18) | ((distance - 1UL) << 46) | RepeatTokenC;
            PushSeen(state, entry);
            if (distance < 0x800UL) cursor -= LatestHashTables.ShortRewindTable[nextKind + 256];
            state.Last = token;
            return;
        }
        PushSeen(state, token);
        state.Last = token;
    }

    private static void EmitSecondPass(EncoderState state, ref int cursor, ulong token, byte kind)
    {
        if (token == state.Last && state.Repeat < 0xFFU) { state.Repeat++; return; }
        if (EmitRepeated(state, ref cursor, token, kind)) return;
        PushSeen(state, token);
        state.Last = token;
    }

    private static void FlushSecondPass(EncoderState state)
    {
        if (state.Repeat == 0) return;
        ulong entry;
        if (state.Last == RepeatSentinel)
            entry = ((ulong)state.Repeat << 48) | RepeatTokenD;
        else if (FindPrevious(state, state.Last, out ulong distance) && distance != 0)
            entry = ((ulong)((((ushort)distance - 1U) & 0x03FFU) | ((state.Repeat - 1U) << 11)) << 37) | RepeatTokenE;
        else
        {
            PushSeen(state, ((ulong)state.Repeat << 48) | RepeatTokenD);
            entry = state.Last;
        }
        PushSeen(state, entry);
        state.Repeat = 0;
        state.Last = RepeatSentinel;
    }

    // The pinned upstream can loop forever for some periodic long inputs. This
    // safety budget only rejects runaway encoding; it never changes a hash.
    private static void TakeEncodingStep(ref long remainingSteps)
    {
        if (--remainingSteps < 0)
            throw new InvalidOperationException("The pinned UserChoiceLatest encoder exceeded its bounded work limit.");
    }

    private static uint FirstPass(EncoderState state, char[] input)
    {
        long remainingSteps = (long)input.Length * 64 + 4096;
        int cursor = 0;
        uint consumed = 0;
        ulong result = (2249744775UL * (uint)input.Length) >> 32;
        ulong quota = (result + (((ulong)input.Length - result) >> 1)) >> 7;
        while ((uint)quota > 1U)
        {
            TakeEncodingStep(ref remainingSteps);
            uint limit = (uint)quota - 1;
            uint processed = 0;
            while (processed < limit)
            {
                TakeEncodingStep(ref remainingSteps);
                if (!BuildToken(input, cursor, out ulong token, out byte kind, out byte length)) break;
                consumed += length;
                cursor += length;
                EmitFirstPass(state, ref cursor, input.Length, input, token, kind);
                processed++;
            }
            result = (2249744775UL * (uint)(input.Length - cursor)) >> 32;
            quota = ((uint)result + (((uint)(input.Length - cursor) - (uint)result) >> 1)) >> 7;
        }
        // Upstream uses the summed token lengths, not the rewound cursor, as the
        // second-pass start. Preserve that detail; TotalUnits is second-pass only.
        return consumed;
    }

    private static void SecondPass(EncoderState state, char[] input, int cursor)
    {
        long remainingSteps = (long)input.Length * 64 + 4096;
        while (cursor < input.Length)
        {
            TakeEncodingStep(ref remainingSteps);
            if (!BuildTokenSecondPass(input, cursor, out ulong token, out byte kind, out byte length)) break;
            state.TotalUnits += length;
            cursor += length;
            EmitSecondPass(state, ref cursor, token, kind);
        }
        FlushSecondPass(state);
    }
}
