namespace PKHeX.Core;

/// <summary>
/// Physical representation supplied for a Pokémon Bank Generation 7 storage image.
/// </summary>
public enum Bank7ImageKind
{
    /// <summary>Legacy serialized Bank body, length <c>0xACA48</c>.</summary>
    Legacy,

    /// <summary>Pokémon Bank v1.5 serialized body, length <c>0xBB518</c>.</summary>
    V15Full,

    /// <summary>Runtime object dump containing an 8-byte object header before a v1.5 body.</summary>
    V15ObjectDump,
}
