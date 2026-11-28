using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using PKHeX.Core;
using PKHeX.Core.AutoMod;

internal static class NgcDex
{
    const ushort Tid = 55555, Sid = 18180;
    const string OT = "Ｃｉｎｄｙ";
    static bool Identity(PKM p) => p.TID16 == Tid && p.SID16 == Sid && p.OriginalTrainerName.Normalize(System.Text.NormalizationForm.FormKC) == "Cindy";
    static int IVTotal(PKM p) => p.IV_HP+p.IV_ATK+p.IV_DEF+p.IV_SPA+p.IV_SPD+p.IV_SPE;
    static byte[] Bytes(PKM p) { var b = new byte[p.SIZE_PARTY]; p.WriteDecryptedDataParty(b); return b; }
    // Enumerate the pinned Core's NGC encounter catalog directly, not the GBA national dex.
    static IEnumerable<IEncounterable> NativeCatalog(string game)
    {
        var name = game == "Colosseum" ? "Encounters3Colo" : "Encounters3XD";
        var type = typeof(PKM).Assembly.GetType("PKHeX.Core." + name, true)!;
        var fields = game == "Colosseum" ? new[] {"Starters","Gifts","Shadow","EReader"} : new[] {"Gifts","Trades","Shadow","Slots"};
        foreach (var field in fields)
        {
            var values = (Array)(type.GetField(field,System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic)?.GetValue(null)
                ?? throw new InvalidOperationException("Missing native encounter table: "+name+"."+field));
            foreach (var value in values)
            {
                if (value is IEncounterable encounter) yield return encounter;
                else if (value is EncounterArea3XD area)
                    foreach (var slot in area.Slots) yield return slot;
            }
        }
    }
    static bool Native(PKM pk,string game)
    {
        if (pk.Version != GameVersion.CXD || pk.IsEgg) return false;
        var la = new LegalityAnalysis(pk);
        if (!la.Valid) return false;
        return game=="Colosseum"
            ? la.EncounterOriginal is EncounterStarter3Colo or EncounterShadow3Colo or EncounterGift3Colo
            : la.EncounterOriginal is EncounterShadow3XD or EncounterStatic3XD or EncounterSlot3XD or EncounterTrade3XD;
    }
    public static int Generate(string root, string onlyGame = "Both")
    {
        Directory.CreateDirectory(root);
        EncounterEvent.RefreshMGDB();
        var trainer = BlankSaveFile.Get(GameVersion.E, OT, LanguageID.Japanese);
        trainer.TID16 = Tid; trainer.SID16 = Sid; trainer.Gender = 0;
        RecentTrainerCache.SetRecentTrainer(trainer);
        using var log = new StreamWriter(Path.Combine(root,"manifest.tsv"));
        log.AutoFlush=true;
        log.WriteLine("Game\tMode\tSpecies\tForm\tGender\tStatus\tOrigin\tOT\tTID\tSID\tShiny\tIVTotal\tNature\tEncounter");
        foreach (var game in new[]{"Colosseum","XD"}.Where(x=>onlyGame=="Both"||onlyGame==x))
        {
            var ext=game=="Colosseum"?"ck3":"xk3";
            var entries=NativeCatalog(game).Where(e=>e.Version is GameVersion.COLO or GameVersion.XD or GameVersion.CXD)
                .GroupBy(e=>(e.Species,e.Form)).OrderBy(g=>g.Key.Species).ThenBy(g=>g.Key.Form);
            foreach(var group in entries)
            {
                var ratio=PersonalTable.E[group.Key.Species].Gender;
                byte[] genders=ratio switch {0=>[0],254=>[1],255=>[2],_=>[0,1]};
                foreach(var gender in genders)
                foreach(var shiny in new[]{false,true})
                {
                    var mode=shiny?"AllShiny":"AllNonShiny";
                    PKM? best=null, fixedBest=null;
                    string bestEncounter="", fixedEncounter="";
                    bool eligible=false;
                    foreach(var enc in group.GroupBy(e=>e.GetType().Name+":"+e.LongName).Select(g=>g.First()))
                    {
                        if(shiny && enc.Shiny==Shiny.Never) continue;
                        if(enc is IFixedGender fg && fg.IsFixedGender && fg.Gender!=gender) continue;
                        eligible=true;
                        var blank=trainer.BlankPKM;blank.Species=enc.Species;blank.Form=enc.Form;
                        var nature=CindyDexExporter.GetNgcRecommendedNature(blank);
                        var basic=EncounterCriteria.Unrestricted with {Gender=(Gender)gender,Shiny=shiny?Shiny.Always:Shiny.Never};
                        var perfect=basic with {Nature=nature,IV_HP=31,IV_ATK=31,IV_DEF=31,IV_SPA=31,IV_SPD=31,IV_SPE=31};
                        for(int attempt=0;attempt<5;attempt++)
                        {
                            try
                            {
                                var criteria=attempt==0?perfect:attempt==1?basic with {Nature=nature}:basic;
                                var pk=enc.ConvertToPKM(trainer,criteria);
                                if(pk.Species!=enc.Species || pk.Form!=enc.Form || pk.Gender!=gender || pk.IsShiny!=shiny) continue;
                                pk.RefreshChecksum();
                                if(!Native(pk,game)) continue;
                                int Score(PKM p)=>(p.Nature==nature?1000:0)+IVTotal(p);
                                if(Identity(pk))
                                {
                                    if(best==null||Score(pk)>Score(best)){best=pk;bestEncounter=enc.LongName;}
                                    if(IVTotal(pk)==186&&pk.Nature==nature)break;
                                }
                                else if((enc is EncounterGift3Colo or EncounterTrade3XD) && (fixedBest==null||Score(pk)>Score(fixedBest)))
                                {fixedBest=pk;fixedEncounter=enc.LongName;}
                            }
                            catch(Exception ex) when(ex is ArgumentException or InvalidOperationException) { Console.WriteLine(ex.Message); }
                        }
                        if(best!=null&&IVTotal(best)==186&&best.Nature==nature)break;
                    }
                    void Emit(PKM pk,string folder,string status,string encounter)
                    {
                        var dir=Path.Combine(root,game,folder);Directory.CreateDirectory(dir);
                        File.WriteAllBytes(Path.Combine(dir,$"{pk.Species:D4}-{pk.Form:D2}-{pk.Gender}.{ext}"),Bytes(pk));
                        log.WriteLine($"{game}\t{folder}\t{pk.Species}\t{pk.Form}\t{pk.Gender}\t{status}\t{pk.Version}\t{pk.OriginalTrainerName}\t{pk.TID16}\t{pk.SID16}\t{pk.IsShiny}\t{IVTotal(pk)}\t{pk.Nature}\t{encounter}");
                    }
                    if(best!=null) Emit(best,mode,"OK",bestEncounter);
                    else log.WriteLine($"{game}\t{mode}\t{group.Key.Species}\t{group.Key.Form}\t{gender}\t{(!eligible?"LOCKED":fixedBest!=null?"FIXED_OT_ONLY":"UNRESOLVED")}");
                    if(fixedBest!=null) Emit(fixedBest,"FixedOT-"+mode,"FIXED_OT_EXCEPTION",fixedEncounter);
                }
            }
        }
        return 0;
    }

    public static int Import(string templatePath,string entityFolder,string output)
    {
        var template=SaveUtil.GetSaveFile(templatePath);
        if (template is not (SAV3Colosseum or SAV3XD) || !template.ChecksumsValid)
            throw new InvalidOperationException("Expected checksum-valid NGC template.");
        Directory.CreateDirectory(output);
        var ext=template is SAV3Colosseum?"ck3":"xk3";
        var files=Directory.GetFiles(entityFolder,"*."+ext).OrderBy(x=>x).ToArray();
        int part=0;
        foreach (var chunk in files.Chunk(template.SlotCount))
        {
            // Reload original for each part so GameCube container/header and progress remain intact.
            var save=SaveUtil.GetSaveFile(templatePath)!;
            save.TID16=Tid; save.SID16=Sid; save.OT=OT;
            var expected=new List<PKM>();
            for (int i=0;i<save.SlotCount;i++)
            {
                PKM pk=save.BlankPKM;
                if(i<chunk.Length)
                {
                    var data=File.ReadAllBytes(chunk[i]);
                    pk=ext=="ck3"?new CK3(data):new XK3(data);
                    if(!Identity(pk) || !Native(pk,ext=="ck3"?"Colosseum":"XD")) throw new InvalidOperationException(chunk[i]);
                    expected.Add(pk.Clone());
                }
                save.SetBoxSlotAtIndex(pk,i,EntityImportSettings.None);
            }
            var bytes=save.Write().ToArray();
            // Retain original GCI header if Core emits the inner raw container.
            var original=File.ReadAllBytes(templatePath);
            if(original.Length==bytes.Length+64) bytes=original.Take(64).Concat(bytes).ToArray();
            var read=SaveUtil.GetSaveFile(bytes);
            if(read is null || !read.ChecksumsValid || read.TID16!=Tid || read.SID16!=Sid) throw new InvalidOperationException("GCI readback failed");
            var entries=read.BoxData;
            for(int i=0;i<expected.Count;i++)
            {
                var a=entries[i];var b=expected[i];
                if(a.Species!=b.Species || a.Form!=b.Form || a.PID!=b.PID || a.ID32!=b.ID32 || a.IsShiny!=b.IsShiny || a.Gender!=b.Gender || !Identity(a) || !Native(a,ext=="ck3"?"Colosseum":"XD"))
                    throw new InvalidOperationException($"Readback failed slot {i+1}");
            }
            File.WriteAllBytes(Path.Combine(output,$"Cindy-{++part:D2}.gci"),bytes);
        }
        Console.WriteLine($"Wrote {part} checked GCI files from {files.Length} entries.");
        return files.Length==0?2:0;
    }
}
