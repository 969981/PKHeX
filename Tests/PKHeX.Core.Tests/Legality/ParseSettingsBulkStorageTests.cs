using System.Reflection;
using FluentAssertions;
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
}
