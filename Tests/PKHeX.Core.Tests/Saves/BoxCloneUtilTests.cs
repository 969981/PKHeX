using FluentAssertions;
using Xunit;

namespace PKHeX.Core.Tests.Saves;

public class BoxCloneUtilTests
{
    [Fact]
    public void CloneToAllBoxesFillsEverySlot()
    {
        const int bankBoxCount = 2;
        const int headerSize = 0x17C;
        const int slotSize = 0xE8;
        const int bankNameSpacing = 0x26;

        // Bank7 now exposes BankCount + 1 boxes; the extra box is the Transfer Box.
        // Reserve one additional box-sized region in this compact synthetic fixture.
        const int exposedBoxCount = bankBoxCount + 1;
        var data = new byte[headerSize + (exposedBoxCount * ((30 * slotSize) + bankNameSpacing))];
        data[0x15E] = bankBoxCount;

        var bank = Bank7.GetBank7(data);
        var pk = new PK7 { Species = (ushort)Species.Pikachu };

        var skipped = BoxCloneUtil.SetAllBoxes(bank, pk);

        skipped.Should().Be(0);
        bank.BoxCount.Should().Be(exposedBoxCount);
        for (int box = 0; box < bank.BoxCount; box++)
        {
            for (int slot = 0; slot < bank.BoxSlotCount; slot++)
                bank.GetBoxSlotAtIndex(box, slot).Species.Should().Be((ushort)Species.Pikachu);
        }
    }
}
