using FluentAssertions;
using Xunit;

namespace PKHeX.Core.Tests.Saves;

public class BoxCloneUtilTests
{
    [Fact]
    public void CloneToAllBoxesFillsEverySlot()
    {
        const int boxCount = 2;
        const int headerSize = 0x17C;
        const int slotSize = 0xE8;
        const int bankNameSpacing = 0x26;
        var data = new byte[headerSize + (boxCount * ((30 * slotSize) + bankNameSpacing))];
        data[0x15E] = boxCount;
        var bank = Bank7.GetBank7(data);
        var pk = new PK7 { Species = (ushort)Species.Pikachu };

        var skipped = BoxCloneUtil.SetAllBoxes(bank, pk);

        skipped.Should().Be(0);
        for (int box = 0; box < boxCount; box++)
        {
            for (int slot = 0; slot < bank.BoxSlotCount; slot++)
                bank.GetBoxSlotAtIndex(box, slot).Species.Should().Be((ushort)Species.Pikachu);
        }
    }
}
