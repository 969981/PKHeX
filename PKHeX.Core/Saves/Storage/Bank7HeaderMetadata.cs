using System;

namespace PKHeX.Core;

/// <summary>
/// Read-only header-level metadata from a Pokémon Bank v1.5 full image.
/// </summary>
public sealed record Bank7HeaderMetadata(
    ulong ObjectID,
    ushort Version,
    ushort BoxCount,
    ushort Year,
    byte Month,
    byte Day,
    byte Hour,
    byte Minute,
    byte Second,
    byte Padding,
    uint UpdateGiftID,
    uint TimedGiftID,
    uint Points,
    uint Passes,
    ReadOnlyMemory<byte> Flags,
    ushort DepositCount,
    ushort WithdrawCount);
