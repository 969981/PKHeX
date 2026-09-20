namespace PKHeX.Core;

/// <summary>
/// Raw per-slot metadata stored in the parallel Pokémon Bank v1.5 arrays.
/// </summary>
/// <remarks>
/// Values are intentionally exposed without assigning unverified business meanings.
/// </remarks>
public readonly record struct Bank7SlotMetadata(byte FormatTag, byte SourceCode, ulong Timestamp);
