using System;
using static System.Buffers.Binary.BinaryPrimitives;

namespace PKHeX.Core;

/// <summary>
/// Physical layout of Pokémon Bank v1.5 serialized storage.
/// </summary>
public static class Bank7V15Layout
{
    public const int LegacySize = 0xACA48;
    public const int FullSize = 0xBB518;
    public const int ObjectHeaderSize = 8;
    public const int ObjectDumpSize = FullSize + ObjectHeaderSize;

    public const int VersionOffset = 0x15C;
    public const int BoxCountOffset = 0x15E;

    public const int BoxStart = 0x17C;
    public const int BoxStride = 0x1B56;
    public const int BoxCount = 100;
    public const int SlotsPerBox = 30;
    public const int SlotSize = 0xE8;

    public const int TransferBase = 0xAAF14;
    public const int FormatBase = 0xACA44;
    public const int TransferFormatBase = 0xAD5FC;
    public const int SourceRecordBase = 0xAD61C;
    public const int SourceRecordSize = 0x44;
    public const int AggregateBase = 0xAD83C;
    public const int AggregateSize = 0x7260;
    public const int CountersBase = 0xB4A9C;
    public const int SourceCodeBase = 0xB4AA0;
    public const int TimestampBase = 0xB5658;
    public const int TailBase = 0xBB418;
    public const int TailSize = 0x100;

    /// <summary>
    /// Identifies supported Bank images without accepting arbitrary data that only shares the same size.
    /// </summary>
    public static bool TryIdentify(ReadOnlySpan<byte> data, out Bank7ImageKind kind)
    {
        if (data.Length == LegacySize && data[0] != 0)
        {
            kind = Bank7ImageKind.Legacy;
            return true;
        }

        if (data.Length == FullSize && IsCurrentBody(data))
        {
            kind = Bank7ImageKind.V15Full;
            return true;
        }

        if (data.Length == ObjectDumpSize && IsCurrentBody(data[ObjectHeaderSize..]))
        {
            kind = Bank7ImageKind.V15ObjectDump;
            return true;
        }

        kind = default;
        return false;
    }

    private static bool IsCurrentBody(ReadOnlySpan<byte> data) =>
        ReadUInt16LittleEndian(data[VersionOffset..]) == 2 &&
        ReadUInt16LittleEndian(data[BoxCountOffset..]) == BoxCount;

    public static int GetSlotIndex(int box, int slot)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)box, (uint)BoxCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)slot, (uint)SlotsPerBox);
        return (box * SlotsPerBox) + slot;
    }

    public static int GetPkmOffset(int box, int slot)
    {
        GetSlotIndex(box, slot);
        return BoxStart + (box * BoxStride) + (slot * SlotSize);
    }

    public static int GetFormatOffset(int box, int slot) => FormatBase + GetSlotIndex(box, slot);
    public static int GetSourceOffset(int box, int slot) => SourceCodeBase + GetSlotIndex(box, slot);
    public static int GetTimestampOffset(int box, int slot) => TimestampBase + (GetSlotIndex(box, slot) * sizeof(ulong));
}
