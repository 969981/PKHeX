using System;
using System.Diagnostics.CodeAnalysis;
using static System.Buffers.Binary.BinaryPrimitives;

namespace PKHeX.Core;

/// <summary>
/// Generation 7 <see cref="SaveFile"/> object that reads from Pokémon Bank savedata (stored on AWS).
/// </summary>
public sealed class Bank7 : BulkStorage, IBoxDetailNameRead
{
    public Bank7(Memory<byte> data, Type t, [ConstantExpected] int start, int slotsPerBox = 30, Bank7ImageKind imageKind = Bank7ImageKind.Legacy) : base(data, t, start, slotsPerBox)
    {
        Version = GameVersion.USUM;
        ImageKind = imageKind;
    }

    public Bank7ImageKind ImageKind { get; }
    public bool IsFullImage => ImageKind is Bank7ImageKind.V15Full or Bank7ImageKind.V15ObjectDump;

    public override GameVersion Version { get => GameVersion.USUM; set { } }
    public override PersonalTable7 Personal => PersonalTable.USUM;
    public override ReadOnlySpan<ushort> HeldItems => Legal.HeldItems_SM;
    protected override PK7 GetPKM(Memory<byte> data) => new(data);
    protected override void DecryptPKM(Span<byte> data) => PokeCrypto.Decrypt67(data);
    protected override Bank7 CloneInternal() => new(Data.ToArray(), PKMType, BoxStart, SlotsPerBox, ImageKind);
    public override string PlayTimeString => $"{Year:00}{Month:00}{Day:00}_{Hours:00}ː{Minutes:00}";
    protected internal override string ShortSummary => PlayTimeString;
    private const int GroupNameSize = 0x22; // 17 UTF-16 code units
    private const int BankNameSize = 0x22; // 17 UTF-16 code units
    private const int GroupNameSpacing = GroupNameSize;
    private const int BankFooterSize = 0x26;

    public ulong UID => ReadUInt64LittleEndian(Data);

    public string GetGroupName(int group)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)group, 10u);
        int offset = 0x8 + (GroupNameSpacing * group);
        return GetString(Data.Slice(offset, GroupNameSize));
    }

    public override int BoxCount => BankCount;

    private int BankCount
    {
        get => ReadUInt16LittleEndian(Data[Bank7V15Layout.BoxCountOffset..]);
        set => WriteUInt16LittleEndian(Data[Bank7V15Layout.BoxCountOffset..], (ushort)value);
    }

    private int Year => ReadUInt16LittleEndian(Data[0x160..]);
    private int Month => Data[0x162];
    private int Day => Data[0x163];
    private int Hours => Data[0x164];
    private int Minutes => Data[0x165];

    private int BoxDataSize => (SlotsPerBox * SIZE_STORED) + BankFooterSize;
    public override int GetBoxOffset(int box) => Box + (BoxDataSize * box);
    public string GetBoxName(int box) => GetString(Data.Slice(GetBoxNameOffset(box), BankNameSize));
    public int GetBoxNameOffset(int box) => GetBoxOffset(box) + (SlotsPerBox * SIZE_STORED);
    public byte GetBoxBackground(int box) => Data[GetBoxNameOffset(box) + 0x22];
    public byte GetBoxGroup(int box) => Data[GetBoxNameOffset(box) + 0x23];
    public int GetBoxOrder(int box) => ReadUInt16LittleEndian(Data[(GetBoxNameOffset(box) + 0x24)..]);
    public int GetBoxIndex(int box) => GetBoxOrder(box);

    private void EnsureFullImage()
    {
        if (!IsFullImage)
            throw new InvalidOperationException("This metadata is only available in Pokémon Bank v1.5 full images.");
    }

    public Bank7HeaderMetadata GetHeaderMetadata()
    {
        EnsureFullImage();
        return new Bank7HeaderMetadata(
            ReadUInt64LittleEndian(Data),
            ReadUInt16LittleEndian(Data[Bank7V15Layout.VersionOffset..]),
            ReadUInt16LittleEndian(Data[Bank7V15Layout.BoxCountOffset..]),
            ReadUInt16LittleEndian(Data[0x160..]),
            Data[0x162], Data[0x163], Data[0x164], Data[0x165], Data[0x166], Data[0x167],
            ReadUInt32LittleEndian(Data[0x168..]),
            ReadUInt32LittleEndian(Data[0x16C..]),
            ReadUInt32LittleEndian(Data[0x170..]),
            ReadUInt32LittleEndian(Data[0x174..]),
            Data.Slice(0x178, 4).ToArray(),
            ReadUInt16LittleEndian(Data[Bank7V15Layout.CountersBase..]),
            ReadUInt16LittleEndian(Data[(Bank7V15Layout.CountersBase + 2)..]));
    }

    public Bank7SlotMetadata GetSlotMetadata(int box, int slot)
    {
        EnsureFullImage();
        return new Bank7SlotMetadata(
            Data[Bank7V15Layout.GetFormatOffset(box, slot)],
            Data[Bank7V15Layout.GetSourceOffset(box, slot)],
            ReadUInt64LittleEndian(Data[Bank7V15Layout.GetTimestampOffset(box, slot)..]));
    }

    public byte GetTransferFormatTag(int slot)
    {
        EnsureFullImage();
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)slot, (uint)Bank7V15Layout.SlotsPerBox);
        return Data[Bank7V15Layout.TransferFormatBase + slot];
    }

    public Bank7SourceRecord GetSourceRecord(int index)
    {
        EnsureFullImage();
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, 8u);
        int offset = Bank7V15Layout.SourceRecordBase + (index * Bank7V15Layout.SourceRecordSize);
        var statistics = new uint[9];
        for (int i = 0; i < statistics.Length; i++)
            statistics[i] = ReadUInt32LittleEndian(Data[(offset + 32 + (i * sizeof(uint)))..]);

        return new Bank7SourceRecord(
            GetString(Data.Slice(offset, 26)),
            ReadUInt16LittleEndian(Data[(offset + 26)..]),
            ReadUInt32LittleEndian(Data[(offset + 28)..]),
            statistics);
    }

    public ReadOnlySpan<byte> GetAggregateData()
    {
        EnsureFullImage();
        return Data.Slice(Bank7V15Layout.AggregateBase, Bank7V15Layout.AggregateSize);
    }

    public ReadOnlySpan<byte> GetTailData()
    {
        EnsureFullImage();
        return Data.Slice(Bank7V15Layout.TailBase, Bank7V15Layout.TailSize);
    }

    private const int BoxStart = Bank7V15Layout.BoxStart;

    public static Bank7 GetBank7(Memory<byte> data)
    {
        // SaveUtil performs strict file recognition. Keep this explicit factory permissive so
        // synthetic buffers and programmatic Bank containers remain backwards compatible.
        if (!Bank7V15Layout.TryIdentify(data.Span, out var kind))
            return new Bank7(data, typeof(PK7), BoxStart, Bank7V15Layout.SlotsPerBox, Bank7ImageKind.Legacy);

        var body = kind == Bank7ImageKind.V15ObjectDump
            ? data.Slice(Bank7V15Layout.ObjectHeaderSize, Bank7V15Layout.FullSize)
            : data;
        return new Bank7(body, typeof(PK7), BoxStart, Bank7V15Layout.SlotsPerBox, kind);
    }
}
