using System;
using FluentAssertions;
using Xunit;

namespace PKHeX.Core.Tests.Saves;

public class Bank7RoundTripTests
{
    [Fact]
    public void FullImageNoOpExportIsByteIdentical()
    {
        var data = CreateFullBody();
        data[0x10] = 0xA1;
        data[Bank7V15Layout.AggregateBase + 123] = 0xB2;
        data[Bank7V15Layout.SourceCodeBase + 456] = 0xC3;
        data[Bank7V15Layout.TimestampBase + 789] = 0xD4;
        data[Bank7V15Layout.TailBase + 17] = 0xE5;

        var bank = Bank7.GetBank7(data.ToArray());
        bank.Write().Span.SequenceEqual(data).Should().BeTrue();
    }

    [Fact]
    public void EditingOneSlotLeavesEveryByteOutsideThatSlotUntouched()
    {
        var data = CreateFullBody();
        data[0x10] = 0xA1;
        data[Bank7V15Layout.AggregateBase + 123] = 0xB2;
        data[Bank7V15Layout.SourceCodeBase + 456] = 0xC3;
        data[Bank7V15Layout.TimestampBase + 789] = 0xD4;
        data[Bank7V15Layout.TailBase + 17] = 0xE5;

        var bank = Bank7.GetBank7(data.ToArray());
        var pk = bank.BlankPKM;
        pk.Species = 25;
        pk.EncryptionConstant = 0x12345678;
        bank.SetBoxSlotAtIndex(pk, 0, 0, EntityImportSettings.None);
        var output = bank.Write().ToArray();

        int start = Bank7V15Layout.GetPkmOffset(0, 0);
        int end = start + Bank7V15Layout.SlotSize;
        output.AsSpan(start, Bank7V15Layout.SlotSize).SequenceEqual(data.AsSpan(start, Bank7V15Layout.SlotSize)).Should().BeFalse();
        for (int i = 0; i < output.Length; i++)
        {
            if (i >= start && i < end)
                continue;
            output[i].Should().Be(data[i], $"byte 0x{i:X} is outside the edited Bank slot");
        }
    }

    [Fact]
    public void LegacyNoOpExportRemainsLegacySizedAndByteIdentical()
    {
        var data = new byte[Bank7V15Layout.LegacySize];
        data[0] = 1;
        data[0x100] = 0x5A;

        var bank = Bank7.GetBank7(data.ToArray());
        var output = bank.Write();
        output.Length.Should().Be(Bank7V15Layout.LegacySize);
        output.Span.SequenceEqual(data).Should().BeTrue();
    }

    private static byte[] CreateFullBody()
    {
        var data = new byte[Bank7V15Layout.FullSize];
        data[0] = 1;
        data[Bank7V15Layout.VersionOffset] = 2;
        data[Bank7V15Layout.BoxCountOffset] = 100;
        return data;
    }
}
