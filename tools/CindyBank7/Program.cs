using PKHeX.Core;
using System.Text;
using System.Buffers.Binary;
using System.Security.Cryptography;
if(args.Length!=3) { Console.Error.WriteLine("Usage: CindyBank7 <template.bin> <source root> <output root>"); return 2; }
var template=File.ReadAllBytes(args[0]);
if(template.Length!=0xACA48 || BinaryPrimitives.ReadUInt16LittleEndian(template.AsSpan(0x15C))!=2 || BinaryPrimitives.ReadUInt16LittleEndian(template.AsSpan(0x15E))!=100) throw new InvalidDataException("Expected exact Bank7 view revision 2.");
EncounterEvent.RefreshMGDB();
Directory.CreateDirectory(args[2]);
const int boxStart=0x17C, stride=30*232+38, size=232;
int Offset(int index)=>boxStart+(index/30)*stride+(index%30)*size;
byte[] blank=new byte[size];new PK7().WriteEncryptedDataStored(blank);
byte[] EmptyBank() {
 var b=template.ToArray();
 for(int i=0;i<3000;i++)blank.CopyTo(b,Offset(i));
 for(int i=0;i<100;i++){var o=boxStart+i*stride+30*size; b.AsSpan(o,34).Clear(); Encoding.Unicode.GetBytes($"Box {i+1:D3}").CopyTo(b,o);}
 for(int i=0;i<30;i++)blank.CopyTo(b,0xAAF14+i*size);
 b.AsSpan(0xACA44,4).Clear();
 return b;
}
PKM Read(string file) {
 var d=File.ReadAllBytes(file);
 return Path.GetExtension(file).ToLowerInvariant() switch {
 ".pk1"=>PokeList1.ReadFromSingle(d), ".pk2"=>PokeList2.ReadFromSingle(d),
 ".ck3"=>new CK3(d),".xk3"=>new XK3(d),".pk3"=>new PK3(d),
 ".pk4"=>new PK4(d),".pk5"=>new PK5(d),".pk6"=>new PK6(d),".pk7"=>new PK7(d),
 _=>throw new InvalidDataException(file)};
}
string[] groups=["Gen1","Gen2","NGC","Gen3","Gen4","Gen5","Gen6","Gen7"];
using var log=new StreamWriter(Path.Combine(args[2],"manifest.tsv"));
log.WriteLine("Mode\tVolume\tBox\tSlot\tGroup\tSource\tSpecies\tForm\tGender\tShiny\tOrigin\tOT\tTID\tSourceLegal\tTargetLegal\tStatus\tReport");
int failures=0;
string Clean(string s)=>s.Replace("\r"," ").Replace("\n"," ").Replace("\t"," ");
foreach(var mode in new[]{"NonShiny","Shiny"}) {
 int volume=1,index=0; var b=EmptyBank();
 var expected=new Dictionary<int,PK7>();
 void SaveVolume() {
  if(expected.Count==0)return;
  foreach(var (slot,pk) in expected){
   var read=new PK7(b.AsSpan(Offset(slot),size).ToArray());
   if(!read.Valid||read.Species!=pk.Species||read.Form!=pk.Form||read.IsShiny!=pk.IsShiny||read.ID32!=pk.ID32||read.PID!=pk.PID||read.Version!=pk.Version)throw new InvalidDataException($"Readback failed {slot}");
  }
  for(int slot=0;slot<3000;slot++) if(!expected.ContainsKey(slot)&&new PK7(b.AsSpan(Offset(slot),size).ToArray()).Species!=0)throw new InvalidDataException($"Nonempty padding {slot}");
  var folder=Path.Combine(args[2],mode,$"Part{volume:D2}");Directory.CreateDirectory(folder);
  File.WriteAllBytes(Path.Combine(folder,"bulk_import.bin"),b);
  Console.WriteLine($"{mode} Part{volume:D2}: {expected.Count} entities, {Math.Ceiling(index/30.0)} boxes");
 }
 foreach(var group in groups){
  if(index%30!=0)index+=30-index%30;
  var dir=Path.Combine(args[1],mode,group);if(!Directory.Exists(dir)){failures++;Console.WriteLine("MISSING "+dir);continue;}
  var groupBox=0;
  foreach(var file in Directory.GetFiles(dir,"*",SearchOption.AllDirectories).OrderBy(x=>x,StringComparer.Ordinal)){
   PKM? source=null;
   try {
    source=Read(file); if(source.Species==0||source.IsEgg)throw new InvalidDataException("Empty or egg");
    if(source.IsShiny!=(mode=="Shiny")) { log.WriteLine($"{mode}\t-\t-\t-\t{group}\t{Path.GetFileName(file)}\t{source.Species}\t{source.Form}\t{source.Gender}\t{source.IsShiny}\t{source.Version}\t{source.OriginalTrainerName}\t{source.TID16}\t-\t-\tSKIPPED_WRONG_SHINY\t");continue; }
    bool srcLegal=new LegalityAnalysis(source).Valid;
    var converted=EntityConverter.ConvertToType(source,typeof(PK7),out var result);
    if(converted is not PK7 pk)throw new InvalidDataException($"ConvertToType: {result}");
    pk.HeldItem=0;pk.RefreshChecksum();
    if(pk.Species!=source.Species||pk.Form!=source.Form||pk.IsShiny!=source.IsShiny||pk.TID16!=source.TID16)throw new InvalidDataException("Conversion changed identity/form/shiny/TID");
    var la=new LegalityAnalysis(pk);
    if(!la.Valid){failures++;log.WriteLine($"{mode}\t-\t-\t-\t{group}\t{Path.GetFileName(file)}\t{pk.Species}\t{pk.Form}\t{pk.Gender}\t{pk.IsShiny}\t{pk.Version}\t{pk.OriginalTrainerName}\t{pk.TID16}\t{srcLegal}\tFalse\tEXCLUDED_ILLEGAL\t{Clean(la.Report())}");continue;}
    if(index==3000){SaveVolume();volume++;index=0;b=EmptyBank();expected.Clear();}
    if(index%30==0){groupBox++;var o=boxStart+(index/30)*stride+30*size; b.AsSpan(o,34).Clear();Encoding.Unicode.GetBytes($"{group}-{groupBox:D2}").CopyTo(b,o);}
    pk.WriteEncryptedDataStored(b.AsSpan(Offset(index),size));
    expected.Add(index,(PK7)pk.Clone());
    log.WriteLine($"{mode}\t{volume}\t{index/30+1}\t{index%30+1}\t{group}\t{Path.GetFileName(file)}\t{pk.Species}\t{pk.Form}\t{pk.Gender}\t{pk.IsShiny}\t{pk.Version}\t{pk.OriginalTrainerName}\t{pk.TID16}\t{srcLegal}\tTrue\tOK\t");
    index++;
   } catch(Exception ex){failures++;log.WriteLine($"{mode}\t-\t-\t-\t{group}\t{Path.GetFileName(file)}\t{source?.Species}\t{source?.Form}\t{source?.Gender}\t{source?.IsShiny}\t{source?.Version}\t{source?.OriginalTrainerName}\t{source?.TID16}\t-\t-\tFAILED\t{Clean(ex.Message)}");}
  }
 }
 SaveVolume();
}
Console.WriteLine($"Conversion failures/excluded: {failures}");return 0;
