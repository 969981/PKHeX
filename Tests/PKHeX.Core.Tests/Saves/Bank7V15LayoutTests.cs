using System;
using FluentAssertions;
using Xunit;

namespace PKHeX.Core.Tests.Saves;

public class Bank7V15LayoutTests
{
    [Theory]
    [InlineData(0, 0, 0x17C, 0xACA44, 0xB4AA0, 0xB5658)]
    [InlineData(99, 29, 0xAAE06, 0xAD5FB, 0xB5657, 0xBB410)]
    public void SlotOffsetsAreStable(int box, int slot, int pkm, int format, int source, int time)
    {
        Bank7V15Layout.GetPkmOffset(box, slot).Should().Be(pkm);
        Bank7V15Layout.GetFormatOffset(box, slot).Should().Be(format);
        Bank7V15Layout.GetSourceOffset(box, slot).Should().Be(source);
        Bank7V15Layout.GetTimestampOffset(box, slot).Should().Be(time);
        Bank7V15Layout.GetSlotIndex(box, slot).Should().Be((box * 30) + slot);
    }

    [Fact]
    public void IdentifiesLegacyImage()
    {
        var data = new byte[Bank7V15Layout.LegacySize];
        data[0] = 1;

        Bank7V15Layout.TryIdentify(data, out var kind).Should().BeTrue();
        kind.Should().Be(Bank7ImageKind.Legacy);
    }

    [Fact]
    public void IdentifiesFullSerializedImage()
    {
        var data = CreateFullBody();

        Bank7V15Layout.TryIdentify(data, out var kind).Should().BeTrue();
        kind.Should().Be(Bank7ImageKind.V15Full);
    }

    [Fact]
    public void IdentifiesRuntimeObjectDump()
    {
        var body = CreateFullBody();
        var data = new byte[Bank7V15Layout.ObjectDumpSize];
        body.CopyTo(data.AsSpan(Bank7V15Layout.ObjectHeaderSize));

        Bank7V15Layout.TryIdentify(data, out var kind).Should().BeTrue();
        kind.Should().Be(Bank7ImageKind.V15ObjectDump);
    }

    [Fact]
    public void RejectsFullSizeDataWithInvalidVersionOrBoxCount()
    {
        var data = new byte[Bank7V15Layout.FullSize];
        data[0] = 1;

        Bank7V15Layout.TryIdentify(data, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(100, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 30)]
    public void SlotIndexRejectsOutOfRangeCoordinates(int box, int slot)
    {
        var action = () => Bank7V15Layout.GetSlotIndex(box, slot);
        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static byte[] CreateFullBody()
    {
        var data = new byte[Bank7V15Layout.FullSize];
        data[0] = 1;
        data[Bank7V15Layout.VersionOffset] = 2;
        data[Bank7V15Layout.VersionOffset + 1] = 0;
        data[Bank7V15Layout.BoxCountOffset] = 100;
        data[Bank7V15Layout.BoxCountOffset + 1] = 0;
        return data;
    }
}
