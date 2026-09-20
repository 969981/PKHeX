using System;
using FluentAssertions;
using Xunit;

namespace PKHeX.Core.Tests.Saves;

public class SaveUtilBank7Tests
{
    [Fact]
    public void LoadsLegacyBankWithoutChangingItsExportSize()
    {
        var data = new byte[Bank7V15Layout.LegacySize];
        data[0] = 1;

        var sav = SaveUtil.GetSaveFile(data).Should().BeOfType<Bank7>().Subject;
        sav.ImageKind.Should().Be(Bank7ImageKind.Legacy);
        sav.IsFullImage.Should().BeFalse();
        sav.Write().Length.Should().Be(Bank7V15Layout.LegacySize);
    }

    [Fact]
    public void LoadsFullV15BodyDirectly()
    {
        var data = CreateFullBody();

        var sav = SaveUtil.GetSaveFile(data).Should().BeOfType<Bank7>().Subject;
        sav.ImageKind.Should().Be(Bank7ImageKind.V15Full);
        sav.IsFullImage.Should().BeTrue();
        sav.BoxCount.Should().Be(100);
        sav.Buffer.Length.Should().Be(Bank7V15Layout.FullSize);
        sav.Write().Span.SequenceEqual(data).Should().BeTrue();
    }

    [Fact]
    public void LoadsRuntimeObjectDumpAndExportsOnlySerializedBody()
    {
        var body = CreateFullBody();
        var dump = new byte[Bank7V15Layout.ObjectDumpSize];
        dump[0] = 0xFC;
        dump[1] = 0x26;
        dump[2] = 0x36;
        body.CopyTo(dump.AsSpan(Bank7V15Layout.ObjectHeaderSize));

        var sav = SaveUtil.GetSaveFile(dump).Should().BeOfType<Bank7>().Subject;
        sav.ImageKind.Should().Be(Bank7ImageKind.V15ObjectDump);
        sav.IsFullImage.Should().BeTrue();
        sav.Buffer.Length.Should().Be(Bank7V15Layout.FullSize);
        sav.Write().Span.SequenceEqual(body).Should().BeTrue();
    }

    [Fact]
    public void RejectsFullSizedLookalikeWithInvalidHeader()
    {
        var data = new byte[Bank7V15Layout.FullSize];
        data[0] = 1;

        SaveUtil.GetSaveFile(data).Should().BeNull();
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
