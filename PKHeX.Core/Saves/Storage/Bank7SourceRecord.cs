using System;

namespace PKHeX.Core;

/// <summary>
/// One of the eight source-game summary records stored by Pokémon Bank v1.5.
/// </summary>
public sealed record Bank7SourceRecord(
    string PlayerName,
    ushort Sex,
    uint TrainerID,
    ReadOnlyMemory<uint> Statistics);
