using System;

namespace PKHeX.Core;

/// <summary>
/// Helpers for cloning one entity across box storage.
/// </summary>
public static class BoxCloneUtil
{
    /// <summary>
    /// Writes the same entity to every writable slot in every box.
    /// </summary>
    /// <param name="sav">Destination save or bulk storage.</param>
    /// <param name="pk">Entity to clone.</param>
    /// <returns>Total number of locked slots skipped by the save implementation.</returns>
    public static int SetAllBoxes(SaveFile sav, PKM pk)
    {
        var clones = new PKM[sav.BoxSlotCount];
        Array.Fill(clones, pk);

        int skipped = 0;
        for (int box = 0; box < sav.BoxCount; box++)
            skipped += sav.SetBoxData(clones, box);
        return skipped;
    }
}
