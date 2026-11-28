using System;
using System.IO;
using PKHeX.Core;
using PKHeX.Core.AutoMod;

if ((args.Length != 2 && args.Length != 3) || !int.TryParse(args[0], out var generation) || generation < 3 || generation > 7)
{
    Console.Error.WriteLine("Usage: CindyDex.Cli <generation 3-7> <new output directory> [original save path]");
    return 2;
}
if (Directory.Exists(args[1]) && Directory.GetFileSystemEntries(args[1]).Length != 0)
{
    Console.Error.WriteLine("Output directory must be empty.");
    return 2;
}
EncounterEvent.RefreshMGDB();
SaveFile? template = null;
if (args.Length == 3)
{
    template = SaveUtil.GetSaveFile(args[2]);
    if (template is null || template.Generation != generation || !template.ChecksumsValid)
    {
        Console.Error.WriteLine("Cannot load a checksum-valid save of the requested generation.");
        return 2;
    }
}
CindyDexExporter.Run(args[1], generation, template);
Console.WriteLine("Export ended; inspect manifest.tsv for gaps and fallback entries.");
return 0;
