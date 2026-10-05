using FluentAssertions;
using Xunit;

namespace PKHeX.Core.Tests.Saves;

public class Bank7LegacyCompactTests
{
    [Fact]
    public void CompactGen7BankIsRecognizedAndPreserved()
    {
        var data = new byte[SaveUtil.SIZE_G7BANK_1];
        data[0x15C] = (byte)BankRevision.Gen7;
        data[0x15D] = 0;
        data[0x15E] = Bank7.FixedBoxCount;
        data[0x15F] = 0;

        SaveUtil.IsSizeValid(data.Length).Should().BeTrue();
        Bank7.IsCompactGen7(data).Should().BeTrue();
        Bank7.IsBank(data).Should().BeTrue();

        var sav = SaveUtil.GetSaveFile(data);
        sav.Should().BeOfType<Bank7>();
        sav!.Buffer.Length.Should().Be(SaveUtil.SIZE_G7BANK_1);
    }

    [Fact]
    public void CompactGen6BankStillUpgradesToFullLayout()
    {
        var data = new byte[SaveUtil.SIZE_G7BANK_1];
        data[0x15C] = (byte)BankRevision.Gen6;
        data[0x15D] = 0;
        data[0x15E] = Bank7.FixedBoxCount;
        data[0x15F] = 0;

        Bank7.IsFormat1(data).Should().BeTrue();

        var sav = SaveUtil.GetSaveFile(data);
        sav.Should().BeOfType<Bank7>();
        sav!.Buffer.Length.Should().Be(SaveUtil.SIZE_G7BANK_2);
    }
}
