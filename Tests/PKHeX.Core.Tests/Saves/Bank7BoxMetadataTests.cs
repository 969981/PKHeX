using System;
using System.Buffers.Binary;
using System.Text;
using FluentAssertions;
using Xunit;

namespace PKHeX.Core.Tests.Saves;

public class Bank7BoxMetadataTests
{
    [Fact]
    public void ReadsCompleteGroupAndBoxFooterFields()
    {
        var data = CreateFullBody();
        WriteFixedUtf16(data, 0x008, 34, "ABCDEFGHIJKL");

        int box = Bank7V15Layout.BoxStart;
        int footer = box + (Bank7V15Layout.SlotsPerBox * Bank7V15Layout.SlotSize);
        WriteFixedUtf16(data, footer, 34, "MNOPQRSTUVWX");
        data[footer + 0x22] = 7;
        data[footer + 0x23] = 3;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(footer + 0x24), 42);

        var bank = Bank7.GetBank7(data);
        bank.GetGroupName(0).Should().Be("ABCDEFGHIJKL");
        bank.GetBoxName(0).Should().Be("MNOPQRSTUVWX");
        bank.GetBoxBackground(0).Should().Be(7);
        bank.GetBoxGroup(0).Should().Be(3);
        bank.GetBoxOrder(0).Should().Be(42);
        bank.GetBoxIndex(0).Should().Be(42);
    }

    [Fact]
    public void OneHundredBoxRecordsEndAtTransferBoxBase()
    {
        var bank = Bank7.GetBank7(CreateFullBody());
        (bank.GetBoxOffset(99) + Bank7V15Layout.BoxStride).Should().Be(Bank7V15Layout.TransferBase);
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
