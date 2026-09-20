using System;
using System.Buffers.Binary;
using System.Linq;
using System.Text;
using FluentAssertions;
using Xunit;

namespace PKHeX.Core.Tests.Saves;

public class Bank7MetadataTests
{
    [Fact]
    public void ReadsHeaderSlotSourceAggregateAndTailMetadata()
    {
        var data = CreateFullBody();
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(0), 0x0102030405060708UL);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x160), 2026);
        data[0x162] = 9;
        data[0x163] = 20;
        data[0x164] = 10;
        data[0x165] = 30;
        data[0x166] = 45;
        data[0x167] = 0xAA;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x168), 11);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x16C), 22);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x170), 33);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x174), 44);
        data.AsSpan(0x178, 4).CopyFrom([1, 2, 3, 4]);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(Bank7V15Layout.CountersBase), 55);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(Bank7V15Layout.CountersBase + 2), 66);

        const int box = 2;
        const int slot = 5;
        data[Bank7V15Layout.GetFormatOffset(box, slot)] = 1;
        data[Bank7V15Layout.GetSourceOffset(box, slot)] = 0x20;
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(Bank7V15Layout.GetTimestampOffset(box, slot)), 0x1122334455667788UL);
        data[Bank7V15Layout.TransferFormatBase + 7] = 9;

        const int sourceIndex = 3;
        int source = Bank7V15Layout.SourceRecordBase + (sourceIndex * Bank7V15Layout.SourceRecordSize);
        WriteFixedUtf16(data, source, 26, "PLAYER");
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(source + 26), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(source + 28), 0x12345678);
        for (uint i = 0; i < 9; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(source + 32 + ((int)i * 4)), 100 + i);

        data[Bank7V15Layout.AggregateBase] = 0xAB;
        data[Bank7V15Layout.AggregateBase + Bank7V15Layout.AggregateSize - 1] = 0xCD;
        data[Bank7V15Layout.TailBase] = 0xEF;
        data[Bank7V15Layout.TailBase + Bank7V15Layout.TailSize - 1] = 0x12;

        var bank = Bank7.GetBank7(data);
        var header = bank.GetHeaderMetadata();
        header.ObjectID.Should().Be(0x0102030405060708UL);
        header.Version.Should().Be(2);
        header.BoxCount.Should().Be(100);
        header.Year.Should().Be(2026);
        header.Month.Should().Be(9);
        header.Day.Should().Be(20);
        header.Hour.Should().Be(10);
        header.Minute.Should().Be(30);
        header.Second.Should().Be(45);
        header.Padding.Should().Be(0xAA);
        header.UpdateGiftID.Should().Be(11);
        header.TimedGiftID.Should().Be(22);
        header.Points.Should().Be(33);
        header.Passes.Should().Be(44);
        header.Flags.Span.ToArray().Should().Equal(1, 2, 3, 4);
        header.DepositCount.Should().Be(55);
        header.WithdrawCount.Should().Be(66);

        bank.GetSlotMetadata(box, slot).Should().Be(new Bank7SlotMetadata(1, 0x20, 0x1122334455667788UL));
        bank.GetTransferFormatTag(7).Should().Be(9);

        var record = bank.GetSourceRecord(sourceIndex);
        record.PlayerName.Should().Be("PLAYER");
        record.Sex.Should().Be(1);
        record.TrainerID.Should().Be(0x12345678);
        record.Statistics.Span.ToArray().Should().Equal(Enumerable.Range(100, 9).Select(z => (uint)z));

        var aggregate = bank.GetAggregateData();
        aggregate[0].Should().Be(0xAB);
        aggregate[^1].Should().Be(0xCD);
        var tail = bank.GetTailData();
        tail[0].Should().Be(0xEF);
        tail[^1].Should().Be(0x12);
    }

    [Fact]
    public void FullOnlyMetadataRejectsLegacyImages()
    {
        var data = new byte[Bank7V15Layout.LegacySize];
        data[0] = 1;
        var bank = Bank7.GetBank7(data);

        var action = () => bank.GetSlotMetadata(0, 0);
        action.Should().Throw<InvalidOperationException>();
    }

    private static byte[] CreateFullBody()
    {
        var data = new byte[Bank7V15Layout.FullSize];
        data[0] = 1;
        data[Bank7V15Layout.VersionOffset] = 2;
        data[Bank7V15Layout.BoxCountOffset] = 100;
        return data;
    }

    private static void WriteFixedUtf16(byte[] data, int offset, int length, string value)
    {
        var encoded = Encoding.Unicode.GetBytes(value + "\0");
        encoded.CopyTo(data.AsSpan(offset, length));
    }
}

internal static class SpanTestExtensions
{
    public static void CopyFrom(this Span<byte> destination, ReadOnlySpan<byte> source) => source.CopyTo(destination);
}
