using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using PKHeX.Core;
using PKHeX.Core.AutoMod;

internal static class NgcDex
{
    const ushort Tid = 8888, Sid = 18180;
    const string OT = "Ｃｉｎｄｙ";
    static bool Identity(PKM p) => p.TID16 == Tid && p.SID16 == Sid && p.OriginalTrainerName.Normalize(System.Text.NormalizationForm.FormKC) == "Cindy";
    static int IVTotal(PKM p) => p.IV_HP+p.IV_ATK+p.IV_DEF+p.IV_SPA+p.IV_SPD+p.IV_SPE;
    static byte[] Bytes(PKM p) { var b = new byte[p.SIZE_PARTY]; p.WriteDecryptedDataParty(b); return b; }
    public static int Generate(string root)
    {
        Directory.CreateDirectory(root);
        using var scope = APILegality.EnterSettingsScope();
        APILegality.UseTrainerData = false;
        APILegality.GameVersionPriority = GameVersionPriorityType.PriorityOrder;
        APILegality.PriorityOrder = [GameVersion.CXD, GameVersion.E, GameVersion.R, GameVersion.S, GameVersion.FR, GameVersion.LG];
        APILegality.MatchRequestedBuild = true;
        APILegality.RequestedNatureMatchPolicy = RequestedNatureMatchPolicy.NativeOnly;
        APILegality.ConstrainedIVStrategy = CorrelationConstrainedIVStrategy.PreferHighestLegalIVs;
        Legalizer.EnableEasterEggs = false;
        EncounterEvent.RefreshMGDB();
        var trainer = BlankSaveFile.Get(GameVersion.E, OT, LanguageID.Japanese);
        trainer.TID16 = Tid; trainer.SID16 = Sid; trainer.Gender = 0;
        RecentTrainerCache.SetRecentTrainer(trainer);
        var sets = new Dictionary<bool, Dictionary<(ushort,byte),PKM>>();
        foreach (bool shiny in new[] { false, true })
        {
            var found = new Dictionary<(ushort,byte),PKM>(); sets[shiny] = found;
            var cfg = new LivingDexConfig { IncludeForms=true, SetShiny=shiny, UseRecommendedBattleBuild=true, UseRecommendedBattleNature=true, UseRecommendedBattleIVs=true, UseRecommendedBattleEVs=true, UseRecommendedBattleMoves=true, LivingDexTargetGeneration=3, LivingDexTargetVersion=GameVersion.CXD };
            var summary = trainer.GenerateLivingDex(trainer.Personal,cfg,pk =>
            {
                if (pk is not PK3 || pk.IsEgg || !Identity(pk) || !new LegalityAnalysis(pk).Valid) return;
                var k=(pk.Species,pk.Form);
                if (!found.TryGetValue(k,out var prev) || (pk.IsShiny==shiny && prev.IsShiny!=shiny) || (pk.IsShiny==prev.IsShiny && IVTotal(pk)>IVTotal(prev))) found[k]=pk;
            },CancellationToken.None);
            File.WriteAllText(Path.Combine(root,$"generation-{shiny}.json"),System.Text.Json.JsonSerializer.Serialize(summary));
        }
        using var log = new StreamWriter(Path.Combine(root,"manifest.tsv"));
        log.WriteLine("Game\tMode\tSpecies\tForm\tStatus\tOrigin\tOT\tTID\tSID\tShiny\tIVTotal\tNature");
        foreach (var (game,type,ext) in new[] { ("Colosseum",typeof(CK3),"ck3"), ("XD",typeof(XK3),"xk3") })
        foreach (bool shiny in new[] { false,true })
        {
            var mode=shiny?"AllShiny":"AllNonShiny";
            var dir=Path.Combine(root,game,mode); Directory.CreateDirectory(dir);
            foreach (var key in sets[false].Keys.Concat(sets[true].Keys).Distinct().OrderBy(x=>x.Item1).ThenBy(x=>x.Item2))
            {
                sets[shiny].TryGetValue(key,out var source);
                if (source?.IsShiny!=shiny) source=null;
                if (source is null && shiny && sets[false].TryGetValue(key,out var normal) && !normal.IsShiny) source=normal;
                if (source is null) { log.WriteLine($"{game}\t{mode}\t{key.Item1}\t{key.Item2}\tMISSING"); continue; }
                var converted=EntityConverter.ConvertToType(source.Clone(),type,out _);
                if (converted is null || converted.GetType()!=type || !Identity(converted) || !new LegalityAnalysis(converted).Valid)
                { log.WriteLine($"{game}\t{mode}\t{key.Item1}\t{key.Item2}\tCONVERSION_FAILED"); continue; }
                File.WriteAllBytes(Path.Combine(dir,$"{key.Item1:D4}-{key.Item2:D2}.{ext}"),Bytes(converted));
                log.WriteLine($"{game}\t{mode}\t{key.Item1}\t{key.Item2}\t{(converted.IsShiny==shiny?"OK":"NONSHINY_FALLBACK")}\t{converted.Version}\t{converted.OriginalTrainerName}\t{converted.TID16}\t{converted.SID16}\t{converted.IsShiny}\t{IVTotal(converted)}\t{converted.Nature}");
            }
            for (int i=1;i<=386;i++)
                if (!Directory.EnumerateFiles(dir,$"{i:D4}-*.{ext}").Any()) log.WriteLine($"{game}\t{mode}\t{i}\t0\tMISSING_SPECIES");
        }
        return 0;
    }

    public static int Import(string templatePath,string entityFolder,string output)
    {
        var template=SaveUtil.GetSaveFile(templatePath);
        if (template is not (SAV3Colosseum or SAV3XD) || !template.ChecksumsValid || template.TID16!=Tid || template.SID16!=Sid)
            throw new InvalidOperationException("Expected valid NGC template matching TID 8888 and SID 18180.");
        Directory.CreateDirectory(output);
        var ext=template is SAV3Colosseum?"ck3":"xk3";
        var files=Directory.GetFiles(entityFolder,"*."+ext).OrderBy(x=>x).ToArray();
        int part=0;
        foreach (var chunk in files.Chunk(template.SlotCount))
        {
            // Reload original for each part so GameCube container/header and progress remain intact.
            var save=SaveUtil.GetSaveFile(templatePath)!;
            var expected=new List<PKM>();
            for (int i=0;i<save.SlotCount;i++)
            {
                PKM pk=save.BlankPKM;
                if(i<chunk.Length)
                {
                    var data=File.ReadAllBytes(chunk[i]);
                    pk=ext=="ck3"?new CK3(data):new XK3(data);
                    if(!Identity(pk) || !new LegalityAnalysis(pk).Valid) throw new InvalidOperationException(chunk[i]);
                    expected.Add(pk.Clone());
                }
                save.SetBoxSlotAtIndex(pk,i,EntityImportSettings.None);
            }
            var bytes=save.Write().ToArray();
            // Retain original GCI header if Core emits the inner raw container.
            var original=File.ReadAllBytes(templatePath);
            if(original.Length==bytes.Length+64) bytes=original.Take(64).Concat(bytes).ToArray();
            var read=SaveUtil.GetSaveFile(bytes);
            if(read is null || !read.ChecksumsValid) throw new InvalidOperationException("GCI readback failed");
            var entries=read.BoxData;
            for(int i=0;i<expected.Count;i++)
            {
                var a=entries[i];var b=expected[i];
                if(a.Species!=b.Species || a.Form!=b.Form || a.PID!=b.PID || a.ID32!=b.ID32 || a.IsShiny!=b.IsShiny || !Identity(a) || !new LegalityAnalysis(a).Valid)
                    throw new InvalidOperationException($"Readback failed slot {i+1}");
            }
            File.WriteAllBytes(Path.Combine(output,$"Cindy-{++part:D2}.gci"),bytes);
        }
        Console.WriteLine($"Wrote {part} checked GCI files from {files.Length} entries.");
        return files.Length==0?2:0;
    }
}
