using PKHeX.Core;
using System.Security.Cryptography;
using System.Text.Json;

const int StoredSize = 232;
if (args.Length == 1 && args[0] == "--smoke")
{
    var blank = new SAV7USUM();
    blank.Version = GameVersion.US;
    var bytes = blank.Write().ToArray();
    var read = new SAV7USUM(bytes);
    if (read.BoxCount != 32 || read.BoxSlotCount != 30 || !read.ChecksumsValid)
        throw new InvalidDataException("USUM roundtrip failed");
    Console.WriteLine("USUM roundtrip passed");
    return 0;
}
if (args.Length != 2) throw new ArgumentException("Usage: Gen7EventRepair <original directory> <new output directory>");
var input = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Output must not exist");
var files = Directory.GetFiles(input, "main*").OrderBy(x => x).ToArray();
if (files.Length == 0) throw new IOException("No saves");
Directory.CreateDirectory(output);
var repairedDir = Directory.CreateDirectory(Path.Combine(output,"repaired")).FullName;
var summaries = new List<object>();
var duplicateGroups = new Dictionary<string,List<string>>();
using var before = new StreamWriter(Path.Combine(output,"legality_before.jsonl"));
using var after = new StreamWriter(Path.Combine(output,"legality_after.jsonl"));
using var changes = new StreamWriter(Path.Combine(output,"changes.jsonl"));
using var unresolved = new StreamWriter(Path.Combine(output,"unresolved.jsonl"));
int failures = 0;
foreach (var file in files)
{
    var raw = File.ReadAllBytes(file);
    if (raw.Length != 445440) throw new InvalidDataException("Not USUM: " + Path.GetFileName(file));
    var name = Path.GetFileName(file);
    var sav = new SAV7USUM((byte[])raw.Clone());
    if (!sav.ChecksumsValid) throw new InvalidDataException("Input block checksum failed: " + name);
    var original = new PKM?[960];
    var expected = new PKM?[960];
    int count=0, legalBefore=0, legalAfter=0, changed=0, unparsed=0;
    for(int i=0;i<960;i++)
    {
        var p = sav.GetBoxSlotAtIndex(i);
        if (p.Species == 0) continue;
        count++;
        original[i]=p.Clone(); expected[i]=p.Clone();
        var hash=Convert.ToHexString(SHA256.HashData(p.Data[..StoredSize]));
        if(!duplicateGroups.TryGetValue(hash,out var locations)) duplicateGroups[hash]=locations=new();
        locations.Add(name+":"+(i/30+1)+":"+(i%30+1));
        try
        {
            var initial = new LegalityAnalysis(p,sav.Personal);
            Write(before, Row(name,i,p,initial));
            if (initial.Valid && initial.Parsed && p.ChecksumValid) { legalBefore++; continue; }
            if (!initial.Parsed || !p.ChecksumValid) continue;
            // Only non-identity fields are candidates. Calendar changes are not proof of event dates.
            // Preserve exact matched encounter object; never regenerate a different event.
            var candidates = new List<(PKM Entity,string Rule)>();
            var date = p.Clone();
            ClampDate(date);
            candidates.Add((date,"calendar-clamp"));
            var pp = p.Clone();
            ClampPP(pp);
            candidates.Add((pp,"pp-range"));
            var combined = date.Clone();
            ClampPP(combined);
            candidates.Add((combined,"calendar-and-pp"));
            foreach (var (candidate,rule) in candidates)
            {
                candidate.RefreshChecksum();
                if (candidate.Data.SequenceEqual(p.Data)) continue;
                if (!Identity(p,candidate)) continue;
                var result = new LegalityAnalysis(candidate,sav.Personal);
                if (!result.Parsed || !result.Valid || !candidate.ChecksumValid) continue;
                if (!ReferenceEquals(initial.EncounterOriginal,result.EncounterOriginal) &&
                    !Equals(initial.EncounterOriginal,result.EncounterOriginal)) continue;
                if (!ReferenceEquals(initial.EncounterMatch,result.EncounterMatch) &&
                    !Equals(initial.EncounterMatch,result.EncounterMatch)) continue;
                // Direct stored-slot serialization avoids trade, dex or record side effects.
                candidate.WriteEncryptedDataStored(sav.Data.Slice(sav.GetBoxSlotOffset(i),StoredSize));
                expected[i]=candidate.Clone(); changed++;
                Write(changes,new {save=name,box=i/30+1,slot=i%30+1,rule,before=Row(name,i,p,initial),after=Row(name,i,candidate,result)});
                break;
            }
        }
        catch(Exception ex)
        {
            Write(before,new {save=name,box=i/30+1,slot=i%30+1,error=ex.Message});
        }
    }
    var bytes = changed==0 ? raw : sav.Write().ToArray();
    var verify = new SAV7USUM((byte[])bytes.Clone());
    if(!verify.ChecksumsValid) throw new InvalidDataException("Output checksum failed: "+name);
    for(int i=0;i<960;i++)
    {
        var p=verify.GetBoxSlotAtIndex(i);
        var offset=verify.GetBoxSlotOffset(i);
        if(expected[i] is null)
        {
            if(!raw.AsSpan(offset,StoredSize).SequenceEqual(bytes.AsSpan(offset,StoredSize))) throw new InvalidDataException("Empty slot changed");
            continue;
        }
        if(!p.Data[..StoredSize].SequenceEqual(expected[i]!.Data[..StoredSize])) throw new InvalidDataException("Unexpected slot change");
        if(original[i]!.Data[..StoredSize].SequenceEqual(expected[i]!.Data[..StoredSize]) &&
           !raw.AsSpan(offset,StoredSize).SequenceEqual(bytes.AsSpan(offset,StoredSize))) throw new InvalidDataException("Unchanged slot bytes changed");
        try
        {
            var la=new LegalityAnalysis(p,verify.Personal);
            var row=Row(name,i,p,la);
            Write(after,row);
            if(la.Parsed && la.Valid && p.ChecksumValid) legalAfter++;
            else { if(!la.Parsed) unparsed++; Write(unresolved,row); }
        }
        catch(Exception ex)
        {
            unparsed++;
            Write(unresolved,new {save=name,box=i/30+1,slot=i%30+1,error=ex.Message});
        }
    }
    if(legalAfter<legalBefore) throw new InvalidDataException("Legal count regressed");
    File.WriteAllBytes(Path.Combine(repairedDir,name.Replace("(3)","")),bytes);
    summaries.Add(new {save=name,count,legalBefore,legalAfter,changed,unresolved=count-legalAfter,unparsed,sha256=Convert.ToHexString(SHA256.HashData(bytes))});
    failures+=count-legalAfter;
}
File.WriteAllText(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(summaries,new JsonSerializerOptions{WriteIndented=true}));
File.WriteAllText(Path.Combine(output,"exact_duplicates.json"),JsonSerializer.Serialize(duplicateGroups.Where(x=>x.Value.Count>1),new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"Finished. Unresolved: {failures}. No claim of historical event dates or complete distribution coverage.");
return failures==0?0:2;

static void Write(StreamWriter w,object v)=>w.WriteLine(JsonSerializer.Serialize(v));
static object Row(string save,int i,PKM p,LegalityAnalysis la)=>new {
    save,box=i/30+1,slot=i%30+1,species=p.Species,form=p.Form,language=p.Language,
    ot=p.OriginalTrainerName,tid=p.TID16,sid=p.SID16,shiny=p.IsShiny,
    metDate=p.MetDate,eggDate=p.EggMetDate,parsed=la.Parsed,valid=la.Valid,checksum=p.ChecksumValid,
    encounter=la.EncounterMatch.GetType().Name,encounterOriginal=la.EncounterOriginal.GetType().Name,
    encounterName=la.EncounterOriginal.LongName,report=la.Report(true)
};
static bool Identity(PKM a,PKM b)=>a.Species==b.Species && a.Form==b.Form && a.Language==b.Language &&
    a.OriginalTrainerName==b.OriginalTrainerName && a.TID16==b.TID16 && a.SID16==b.SID16 &&
    a.PID==b.PID && a.EncryptionConstant==b.EncryptionConstant && a.IsShiny==b.IsShiny &&
    a.Version==b.Version && a.Ball==b.Ball && a.FatefulEncounter==b.FatefulEncounter &&
    a.IsEgg==b.IsEgg && a.Nature==b.Nature && a.AbilityNumber==b.AbilityNumber;
static void ClampDate(PKM p)
{
    if (p.MetMonth is >=1 and <=12 && p.MetDay > DateTime.DaysInMonth(2000+p.MetYear,p.MetMonth))
        p.MetDay=(byte)DateTime.DaysInMonth(2000+p.MetYear,p.MetMonth);
    if (p.EggMonth is >=1 and <=12 && p.EggDay > DateTime.DaysInMonth(2000+p.EggYear,p.EggMonth))
        p.EggDay=(byte)DateTime.DaysInMonth(2000+p.EggYear,p.EggMonth);
}
static void ClampPP(PKM p)
{
    p.Move1_PPUps=p.Move1==0?0:Math.Clamp(p.Move1_PPUps,0,3);
    p.Move2_PPUps=p.Move2==0?0:Math.Clamp(p.Move2_PPUps,0,3);
    p.Move3_PPUps=p.Move3==0?0:Math.Clamp(p.Move3_PPUps,0,3);
    p.Move4_PPUps=p.Move4==0?0:Math.Clamp(p.Move4_PPUps,0,3);
    p.Move1_PP=p.Move1==0?0:Math.Clamp(p.Move1_PP,0,p.GetMovePP(p.Move1,p.Move1_PPUps));
    p.Move2_PP=p.Move2==0?0:Math.Clamp(p.Move2_PP,0,p.GetMovePP(p.Move2,p.Move2_PPUps));
    p.Move3_PP=p.Move3==0?0:Math.Clamp(p.Move3_PP,0,p.GetMovePP(p.Move3,p.Move3_PPUps));
    p.Move4_PP=p.Move4==0?0:Math.Clamp(p.Move4_PP,0,p.GetMovePP(p.Move4,p.Move4_PPUps));
}
