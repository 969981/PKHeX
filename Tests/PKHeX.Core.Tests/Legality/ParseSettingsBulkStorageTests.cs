using System.IO;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using PKHeX.Core.Bulk;
using Xunit;

namespace PKHeX.Core.Tests.Legality;

public class ParseSettingsBulkStorageTests
{
    [Fact]
    public void BulkStorageDoesNotBecomeActiveTrainer()
    {
        var bank = Bank7.GetBank7(new byte[0x17C]);

        ParseSettings.InitFromSaveFileData(bank);

        var property = typeof(ParseSettings).GetProperty("ActiveTrainer", BindingFlags.Static | BindingFlags.NonPublic);
        property.Should().NotBeNull();
        property!.GetValue(null).Should().BeNull();
    }

    [Fact]
    public void BulkStorageDoesNotRequirePlaceholderHandlerName()
    {
        var path = Path.Combine(
            TestUtil.GetRepoPath(),
            "Legality",
            "Legal",
            "Generation 7 Transfer",
            "132 - Ditto - 3159082DBAD7.pk7");

        var pk = new PK7(File.ReadAllBytes(path))
        {
            CurrentHandler = 1,
            HandlingTrainerName = "Night",
        };
        pk.RefreshChecksum();

        // The entity itself is legal; only a false comparison against BulkStorage.OT ("PKHeX")
        // used to make bulk legality reject any other recent-handler name.
        new LegalityAnalysis(pk).Valid.Should().BeTrue();

        var bank = Bank7.GetBank7(new byte[SaveUtil.SIZE_G7BANK_2])
        {
            BankCount = Bank7.FixedBoxCount,
        };
        bank.SetBoxSlotAtIndex(pk, 0, 0, EntityImportSettings.None);

        var bulk = new BulkAnalysis(bank, new BulkAnalysisSettings());
        bulk.Parse.Select(z => z.Result.Result)
            .Should().NotContain(LegalityCheckResultCode.TransferHandlerMismatchName);
    }
}
