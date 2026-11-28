using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Text.Json;
using System.Buffers.Binary;
using PKHeX.Core;

namespace PKHeX.Core.AutoMod
{
    public static class CindyDexExporter
    {
        private sealed record Profile(int Gen, int Max, uint DisplayId, GameVersion[] Games);
        private static readonly Profile[] Profiles =
        [
            new(3, 386, 55555, [GameVersion.E, GameVersion.R, GameVersion.S, GameVersion.FR, GameVersion.LG]),
            new(4, 493, 52006, [GameVersion.D, GameVersion.P, GameVersion.Pt, GameVersion.HG, GameVersion.SS]),
            new(5, 649, 10011, [GameVersion.B, GameVersion.W, GameVersion.B2, GameVersion.W2]),
            new(6, 720, 4288, [GameVersion.X, GameVersion.Y, GameVersion.OR, GameVersion.AS]),
            new(7, 807, 109866, [GameVersion.SN, GameVersion.MN, GameVersion.US, GameVersion.UM]),
        ];

        private static SaveFile Trainer(Profile p, GameVersion game, LanguageID lang)
        {
            var name = lang is LanguageID.Japanese or LanguageID.ChineseS or LanguageID.ChineseT ? "Ｃｉｎｄｙ" : "Cindy";
            var save = BlankSaveFile.Get(game, name, lang);
            save.Language = (int)lang;
            save.OT = name;
            save.Gender = 1;
            if (save.BoxesUnlocked >= 0) save.BoxesUnlocked = save.BoxCount;
            save.TID16 = (ushort)(p.DisplayId & 0xFFFF);
            save.SID16 = p.Gen == 7 ? (ushort)(p.DisplayId >> 16) : (ushort)12345;
            return save;
        }

        private static string CindyName(int language) => language is (int)LanguageID.Japanese or (int)LanguageID.ChineseS or (int)LanguageID.ChineseT ? "Ｃｉｎｄｙ" : "Cindy";

        private static bool HasId(PKM pk, Profile p) => p.Gen == 7 ? pk.ID32 % 1000000 == p.DisplayId : pk.TID16 == p.DisplayId;
        private static bool IsCindy(string name) => name.Normalize(NormalizationForm.FormKC) == "Cindy";
        private static bool Identity(PKM pk, Profile p) => pk.OriginalTrainerName == CindyName(pk.Language) && HasId(pk,p);
        private static bool NormalizeCindy(PKM pk, Profile p)
        {
            if (!IsCindy(pk.OriginalTrainerName))
                return FixedEvent(pk) && new LegalityAnalysis(pk).Valid;
            if (!HasId(pk,p))
                return false;
            pk.OriginalTrainerName = CindyName(pk.Language);
            pk.RefreshChecksum();
            return Identity(pk,p) && new LegalityAnalysis(pk).Valid;
        }

        // Separate item-free, statically transfer-compatible collection.
        // Never alter the encounter to disguise a blocked form or manufacture a HOME tracker.
        private static string? HomeBlock(PKM pk)
        {
            if (pk.IsEgg) return "EGG";
            if (pk.Context == EntityContext.Gen7b) return "LGPE_FORMAT";
            if (pk.Species == 172 && pk.Form != 0) return "SPIKY_EARED_PICHU";
            if (pk.Species == 25 && pk.Form != 0 && pk.Generation == 6) return "COSPLAY_PIKACHU";
            if (FormInfo.IsFusedForm(pk.Species,pk.Form,pk.Format)) return "FUSED_FORM";
            if (FormInfo.IsBattleOnlyForm(pk.Species,pk.Form,pk.Format)) return "BATTLE_ONLY_FORM";
            if (pk.Format == 7 && FormInfo.IsTotemForm(pk.Species,pk.Form))
            {
                if (pk.Species is 105 or 743 or 752 or 777) return "BANK_BLOCKED_TOTEM";
                return "TOTEM_REVERTS_ON_HOME_TRANSFER";
            }
            if (pk.Species == 676 && pk.Form != 0) return "TRIM_REVERTS";
            return null;
        }

        private static List<PKM> HomeCollection(Profile p, string root, string mode, List<PKM> source, StreamWriter log)
        {
            var unique = new Dictionary<(ushort,byte),PKM>();
            foreach (var original in source)
            {
                var key = $"{original.Species:D4}-{original.Form:D2}";
                var blocked = HomeBlock(original);
                if (blocked != null)
                {
                    log.WriteLine($"{p.Gen}\t{mode}-Audit\t{key}\tEXCLUDED:{blocked}");
                    continue;
                }
                var pk = original.Clone();
                pk.HeldItem = 0;
                if (FormItem.TryGetForm(pk.Species,0,pk.Format,out var expected)) pk.Form=expected;
                if (pk.Species == 487) pk.Form=0; // Griseous Orb / Giratina
                pk.ResetPartyStats();
                pk.RefreshChecksum();
                if (!NormalizeCindy(pk,p) || pk.HeldItem != 0 || !new LegalityAnalysis(pk).Valid)
                {
                    log.WriteLine($"{p.Gen}\t{mode}-Audit\t{key}\tEXCLUDED:POST_ITEM_LEGALITY");
                    continue;
                }
                var k=(pk.Species,pk.Form);
                if (!unique.TryGetValue(k,out var prev) ||
                    (mode=="HOME-Shiny" && pk.IsShiny && !prev.IsShiny) ||
                    (pk.IsShiny==prev.IsShiny && Quality(pk)>Quality(prev)))
                    unique[k]=pk;
                if (original.Form!=pk.Form) log.WriteLine($"{p.Gen}\t{mode}-Audit\t{key}\tNORMALIZED_FORM:{pk.Form}");
            }
            foreach(var pk in unique.Values.OrderBy(x=>x.Species).ThenBy(x=>x.Form))
                Write(log,root,p.Gen,mode,$"{pk.Species:D4}-{pk.Form:D2}",pk,mode=="HOME-Shiny" && !pk.IsShiny ? "HOME_NONSHINY_FALLBACK" : mode=="HOME-NonShiny" && pk.IsShiny ? "HOME_SHINY_FALLBACK" : Identity(pk,p)?"HOME_STATIC_OK":"EVENT_OT_EXCEPTION");
            return unique.Values.OrderBy(x=>x.Species).ThenBy(x=>x.Form).ToList();
        }

        private static int IVTotal(PKM p) => p.IV_HP + p.IV_ATK + p.IV_DEF + p.IV_SPA + p.IV_SPD + p.IV_SPE;

        private static byte[] PartyBytes(PKM pk)
        {
            var bytes = new byte[pk.SIZE_PARTY];
            pk.WriteDecryptedDataParty(bytes);
            return bytes;
        }

        private static bool Preferred(PKM pk) => pk.Nature == LivingDexRecommendedBuild.GetProfile(pk).Nature;
        private static int Quality(PKM pk) => (Preferred(pk) ? 1000000 : 0) + (IVTotal(pk) == 186 ? 100000 : 0) + IVTotal(pk);
        private static bool FixedEvent(PKM pk) => new LegalityAnalysis(pk).EncounterMatch is MysteryGift || pk.FatefulEncounter;

        private static Dictionary<(ushort, byte), PKM> Generate(Profile p, SaveFile save, bool shiny, string root, StreamWriter log)
        {
            Directory.CreateDirectory(root);
            var result = new Dictionary<(ushort, byte), PKM>();
            // Use the current develop Living Dex core, including its native RNG, form and shiny-lock handling.
            var cfg = new LivingDexConfig
            {
                IncludeForms = true, SetShiny = shiny, UseRecommendedBattleBuild = true,
                UseRecommendedBattleNature = true, UseRecommendedBattleIVs = true,
                UseRecommendedBattleEVs = true, UseRecommendedBattleMoves = true,
                LivingDexTargetGeneration = (byte)p.Gen, LivingDexTargetVersion = p.Games[0],
            };
            RecentTrainerCache.SetRecentTrainer(save);
            var summary = save.GenerateLivingDex(save.Personal, cfg, pk =>
            {
                if (pk.Species > p.Max || pk.Format != p.Gen || pk.IsEgg || !new LegalityAnalysis(pk).Valid)
                    return;
                if (pk.GetType() != save.PKMType)
                    return;
                if (!NormalizeCindy(pk,p))
                    return;
                var key = (pk.Species, pk.Form);
                if (!result.TryGetValue(key, out var prev) ||
                    (pk.IsShiny == shiny && prev.IsShiny != shiny) ||
                    (pk.IsShiny == prev.IsShiny && Quality(pk) > Quality(prev)))
                    result[key] = pk;
            }, CancellationToken.None);
            File.WriteAllText(Path.Combine(root, $"Gen{p.Gen}-{(shiny ? "shiny" : "normal")}-diagnostics.json"), JsonSerializer.Serialize(summary));
            foreach (var target in summary.FailedTargets.Where(t => t.Species <= p.Max))
                log.WriteLine($"{p.Gen}\tGeneration\t{target.Species:D4}-{target.Form:D2}\tMISSING:ENGINE_TARGET");
            return result;
        }

        private static void Write(StreamWriter log, string root, int gen, string mode, string key, PKM? pk, string status)
        {
            if (pk is null)
            {
                log.WriteLine($"{gen}\t{mode}\t{key}\tMISSING:{status}");
                log.Flush();
                return;
            }
            if (!new LegalityAnalysis(pk).Valid)
                throw new InvalidOperationException("Refusing invalid output " + key);
            var dir = Path.Combine(root, $"Gen{gen}", mode);
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, $"{key}.pk{pk.Format}"), PartyBytes(pk));
            log.WriteLine($"{gen}\t{mode}\t{key}\t{status}\t{pk.Version}\t{pk.Generation}\t{pk.OriginalTrainerName}\t{pk.TID16}\t{pk.ID32}\t{pk.IsShiny}\t{IVTotal(pk)}\t{pk.Language}\t{pk.Nature}\t{pk.IV_HP}\t{pk.IV_ATK}\t{pk.IV_DEF}\t{pk.IV_SPA}\t{pk.IV_SPD}\t{pk.IV_SPE}\t{Preferred(pk)}\t{pk.HeldItem}\t{pk.EV_HP}\t{pk.EV_ATK}\t{pk.EV_DEF}\t{pk.EV_SPA}\t{pk.EV_SPD}\t{pk.EV_SPE}\t{pk.Move1}\t{pk.Move2}\t{pk.Move3}\t{pk.Move4}");
            log.Flush();
        }

        private static List<PKM> Events(Profile p, string root, StreamWriter log)
        {
            // One output per database entry, not merely one per species/card ID.
            var results = new List<PKM>();
            var destination = Trainer(p, p.Gen == 7 ? GameVersion.US : p.Games[0], p.Gen == 7 ? LanguageID.ChineseS : LanguageID.Japanese);
            var gifts = EncounterEvent.GetAllEvents().Where(x => x.Generation == p.Gen && x.Context == destination.Context && x.Species > 0 && x.Species <= p.Max).ToArray();
            var languages = p.Gen == 7
                ? new[] { (LanguageID)9, LanguageID.Japanese, LanguageID.English }
                : new[] { LanguageID.Japanese, LanguageID.English };
            for (int i = 0; i < gifts.Length; i++)
            {
                var gift = gifts[i];
                var key = $"{i + 1:D5}-card{gift.CardID:D5}-species{gift.Species:D4}";
                PKM? best = null;
                try
                {
                    foreach (var lang in languages)
                    {
                        foreach (var game in p.Games)
                        {
                            var tr = Trainer(p, game, lang);
                            // Keep the original distribution restrictions; never force a PID/OT.
                            PKM pk;
                            try { pk = gift.ConvertToPKM(tr); }
                            catch { continue; }
                            if (pk.GetType() != destination.PKMType || pk.Format != p.Gen || pk.Species != gift.Species ||
                                pk.Form != gift.Form || !new LegalityAnalysis(pk).Valid)
                                continue;
                            var la = new LegalityAnalysis(pk);
                            if (la.EncounterMatch is not MysteryGift matched ||
                                matched.CardID != gift.CardID || matched.GetType() != gift.GetType())
                                continue;
                            if (!NormalizeCindy(pk,p)) continue;
                            if (best is null || (pk.IsShiny && !best.IsShiny))
                                best = pk;
                            if (best.IsShiny)
                                break;
                        }
                        if (best?.IsShiny == true)
                            break;
                    }
                    if (best != null && !best.IsEgg) results.Add(best);
                    Write(log, root, p.Gen, "Events", key, best,
                        best?.IsShiny == true ? "EVENT_SHINY" : "EVENT_NONSHINY_OR_UNRESOLVED");
                }
                catch (Exception ex)
                {
                    log.WriteLine($"{p.Gen}\tEvents\t{key}\tERROR:{ex.GetType().Name}");
                    log.Flush();
                }
            }
            return results;
        }

        private static void ExportSaveCopies(string root, SaveFile template, string mode, List<PKM> entries, StreamWriter log)
        {
            var compatible = new List<PKM>();
            foreach (var pk in entries)
            {
                if (pk.GetType() != template.PKMType)
                {
                    log.WriteLine($"{template.Generation}\tSave-{mode}\t{pk.Species:D4}-{pk.Form:D2}\tERROR:INCOMPATIBLE_ENTITY_TYPE:{pk.GetType().Name}->{template.PKMType.Name}");
                    continue;
                }
                if (pk.Format != template.Generation || pk.Species > template.MaxSpeciesID || !template.Personal.IsPresentInGame(pk.Species, pk.Form))
                {
                    log.WriteLine($"{template.Generation}\tSave-{mode}\t{pk.Species:D4}-{pk.Form:D2}\tSAVE_UNSUPPORTED_FORM");
                    continue;
                }
                compatible.Add(pk);
            }
            var dir = Path.Combine(root, $"Gen{template.Generation}", "Saves", template.Version.ToString(), mode);
            Directory.CreateDirectory(dir);
            var capacity = template.SlotCount;
            if (capacity <= 0)
                throw new InvalidOperationException("Template has no usable box capacity.");
            int part = 0;
            foreach (var chunk in compatible.Chunk(capacity))
            {
                var copy = template.Clone();
                try
                {
                    for (int i = 0; i < capacity; i++)
                        copy.SetBoxSlotAtIndex(i < chunk.Length ? chunk[i].Clone() : copy.BlankPKM, i, EntityImportSettings.None);
                }
                catch (Exception ex)
                {
                    log.WriteLine($"{template.Generation}\tSave-{mode}\t{template.Version}-Part{part + 1:D2}\tERROR:WRITE:{ex.GetType().Name}:{ex.Message}");
                    log.Flush();
                    return;
                }
                InitializeFooter(copy);
                var data = copy.Write();
                var read = SaveUtil.GetSaveFile(data);
                if (read == null || !read.ChecksumsValid)
                {
                    log.WriteLine($"{template.Generation}\tSave-{mode}\t{template.Version}-Part{part + 1:D2}\tERROR:Save readback/checksum failed ({read?.ChecksumInfo ?? "not recognized"})");
                    log.Flush();
                    return;
                }
                var readback = read.BoxData;
                for (int i = 0; i < chunk.Length; i++)
                {
                    var pk = readback[i];
                    var expected = chunk[i];
                    if (pk.Species != expected.Species || pk.Form != expected.Form || pk.PID != expected.PID ||
                        pk.ID32 != expected.ID32 || pk.OriginalTrainerName != expected.OriginalTrainerName ||
                        pk.IsShiny != expected.IsShiny || pk.Nature != expected.Nature || pk.Language != expected.Language ||
                        pk.HeldItem != expected.HeldItem || pk.Move1 != expected.Move1 || pk.Move2 != expected.Move2 || pk.Move3 != expected.Move3 || pk.Move4 != expected.Move4 ||
                        pk.EV_HP != expected.EV_HP || pk.EV_ATK != expected.EV_ATK || pk.EV_DEF != expected.EV_DEF || pk.EV_SPA != expected.EV_SPA || pk.EV_SPD != expected.EV_SPD || pk.EV_SPE != expected.EV_SPE ||
                        pk.IV_HP != expected.IV_HP || pk.IV_ATK != expected.IV_ATK ||
                        pk.IV_DEF != expected.IV_DEF || pk.IV_SPA != expected.IV_SPA ||
                        pk.IV_SPD != expected.IV_SPD || pk.IV_SPE != expected.IV_SPE || !pk.ChecksumValid || !new LegalityAnalysis(pk).Valid)
                        {
                        log.WriteLine($"{template.Generation}\tSave-{mode}\t{template.Version}-Part{part + 1:D2}\tERROR:Save entity readback failed slot {i + 1}");
                        log.Flush();
                        return;
                    }
                }
                part++;
                var partDir = Path.Combine(dir, $"Part{part:D2}");
                Directory.CreateDirectory(partDir);
                File.WriteAllBytes(Path.Combine(partDir, template.Generation >= 6 ? "main" : "Cindy.sav"), data.ToArray());
                log.WriteLine($"{template.Generation}\tSave-{mode}\tPart{part:D2}\tSAVE_VERIFIED:{chunk.Length}");
                log.Flush();
            }
        }

        private static void InitializeFooter(SaveFile copy)
        {
            if (copy is not SAV_BEEF beef || copy.Generation is not (6 or 7))
                return;
            // PKHeX blank constructors have no on-disk BEEF footer metadata.
            int footer = copy.Data.Length - 0x200;
            BinaryPrimitives.WriteUInt32LittleEndian(copy.Data[(footer + 0x10)..], 0x42454546);
            foreach (var block in beef.AllBlocks)
            {
                int offset = footer + 0x14 + (int)block.ID * 8;
                BinaryPrimitives.WriteUInt32LittleEndian(copy.Data[offset..], (uint)block.Length);
                BinaryPrimitives.WriteUInt16LittleEndian(copy.Data[(offset + 4)..], (ushort)block.ID);
            }
        }


        // Reuse the actual V2 artifacts; never call the full Living Dex generator.
        public static void RunIncremental(string baseline, string root, int generation)
        {
            var p=Profiles.Single(x=>x.Gen==generation);
            if(generation is not (6 or 7)) throw new ArgumentException("Incremental baseline is Gen6 or Gen7 only.");
            Directory.CreateDirectory(root);
            using var scope=APILegality.EnterSettingsScope();
            APILegality.UseTrainerData=false;
            APILegality.GameVersionPriority=GameVersionPriorityType.PriorityOrder;
            APILegality.MatchRequestedBuild=true;
            APILegality.RequestedNatureMatchPolicy=RequestedNatureMatchPolicy.NativeOnly;
            APILegality.ConstrainedIVStrategy=CorrelationConstrainedIVStrategy.PreferHighestLegalIVs;
            Legalizer.EnableEasterEggs=false;
            EncounterEvent.RefreshMGDB();
            var lang=generation==7?LanguageID.ChineseS:LanguageID.Japanese;
            var carrier=generation==7?GameVersion.US:GameVersion.OR;
            var save=Trainer(p,carrier,lang);
            using var log=new StreamWriter(Path.Combine(root,"manifest.tsv"),false,Encoding.UTF8);
            using var audit=new StreamWriter(Path.Combine(root,"incremental-audit.tsv"),false,Encoding.UTF8);
            log.WriteLine(File.ReadLines(Path.Combine(baseline,"manifest.tsv")).First());
            audit.WriteLine("Mode\\tKey\\tStatus\\tDetail".Replace("\\t","\t"));
            var baseRows=File.ReadLines(Path.Combine(baseline,"manifest.tsv")).Skip(1).Select(x=>x.Split('\t')).Where(x=>x.Length>=4).ToArray();
            PKM Read(string path) => generation==6 ? new PK6(File.ReadAllBytes(path)) : new PK7(File.ReadAllBytes(path));
            Dictionary<string,PKM> Load(string mode)
            {
                var dict=new Dictionary<string,PKM>();
                var dir=Path.Combine(baseline,$"Gen{generation}",mode);
                foreach(var file in Directory.GetFiles(dir,$"*.pk{generation}").OrderBy(x=>x))
                {
                    var pk=Read(file);
                    var key=Path.GetFileNameWithoutExtension(file);
                    if (!NormalizeCindy(pk,p) || !new LegalityAnalysis(pk).Valid)
                    { audit.WriteLine($"{mode}\t{key}\tBASE_INVALID\t{pk.OriginalTrainerName}"); continue; }
                    dict[key]=pk;
                }
                return dict;
            }
            var collections=new Dictionary<string,List<PKM>>();
            var all=new Dictionary<string,Dictionary<string,PKM>>();
            foreach(var mode in new[]{"AllNonShiny","AllShiny"})
            {
                var shiny=mode=="AllShiny";
                var current=Load(mode);
                var targets=new List<(ushort Species,byte Form)>();
                if(generation==7) for(byte form=0;form<28;form++) targets.Add((201,form));
                targets.Add((711,3));
                foreach(var target in targets)
                {
                    string key=$"{target.Species:D4}-{target.Form:D2}";
                    if(current.ContainsKey(key)) continue;
                    PKM? best=null;
                    // Use the existing verified Super Size Pumpkaboo as the evolution source.
                    // A different size cannot be obtained merely by editing a Gourgeist form.
                    if(target.Species==711 && current.TryGetValue("0710-03",out var pumpkin) && pumpkin.IsShiny==shiny)
                    {
                        var evolved=pumpkin.Clone();
                        evolved.Species=711;
                        evolved.CurrentLevel=100;
                        evolved.IsNicknamed=false;
                        evolved.Nickname=SpeciesName.GetSpeciesNameGeneration(711,evolved.Language,evolved.Format);
                        evolved.RefreshAbility(evolved.AbilityNumber==4?2:evolved.AbilityNumber==2?1:0);
                        if(current.TryGetValue("0711-00",out var reference))
                        {
                            evolved.Move1=reference.Move1; evolved.Move2=reference.Move2;
                            evolved.Move3=reference.Move3; evolved.Move4=reference.Move4;
                            evolved.HealPP();
                        }
                        evolved.ResetPartyStats(); evolved.RefreshChecksum();
                        if(evolved.Form==3 && NormalizeCindy(evolved,p) && Identity(evolved,p) && new LegalityAnalysis(evolved).Valid)
                        {best=evolved; audit.WriteLine($"{mode}\t{key}\tEVOLUTION_CANDIDATE\tSuper Size Pumpkaboo");}
                    }
                    // Unown originates before Gen7. Generate with the final raw ID32
                    // in a Japanese source game, then use official Core conversion.
                    var sources=target.Species==201
                        ? new[]{GameVersion.HG,GameVersion.SS,GameVersion.D,GameVersion.Pt,GameVersion.OR,GameVersion.AS}
                        : new[]{GameVersion.X,GameVersion.Y,GameVersion.OR,GameVersion.AS};
                    foreach(var version in sources)
                    {
                        var tr=BlankSaveFile.Get(version,"Ｃｉｎｄｙ",LanguageID.Japanese);
                        tr.ID32=save.ID32; tr.Gender=save.Gender;
                        APILegality.PriorityOrder=[version];
                        RecentTrainerCache.SetRecentTrainer(tr);
                        for(int attempt=0;attempt<3;attempt++)
                        {
                            try
                            {
                                if(!tr.GetCindyTargetEncounter(target.Species,target.Form,shiny,out var source) || source==null)
                                { audit.WriteLine($"{mode}\t{key}\tNO_ENCOUNTER\t{version}"); continue; }
                                var pk=EntityConverter.ConvertToType(source,save.PKMType,out var conversion);
                                if(pk==null || pk.Species!=target.Species || pk.Form!=target.Form || pk.IsShiny!=shiny ||
                                   !NormalizeCindy(pk,p) || !Identity(pk,p) || !new LegalityAnalysis(pk).Valid)
                                { audit.WriteLine($"{mode}\t{key}\tREJECTED\t{version} {conversion} OT={pk?.OriginalTrainerName} ID={pk?.ID32} Lang={pk?.Language} Species={pk?.Species} Form={pk?.Form} Shiny={pk?.IsShiny} Legal={(pk==null?"null":new LegalityAnalysis(pk).Report().Replace('\r',' ').Replace('\n',' '))}"); continue; }
                                if(best==null || Quality(pk)>Quality(best)) best=pk;
                                if(IVTotal(best)==186 && Preferred(best)) break;
                            }
                            catch(Exception ex){audit.WriteLine($"{mode}\t{key}\tATTEMPT_ERROR\t{version} {ex.GetType().Name}");}
                        }
                        if(best!=null && IVTotal(best)==186 && Preferred(best)) break;
                    }
                    if(best!=null){current[key]=best;audit.WriteLine($"{mode}\t{key}\tREPAIRED\t{best.Version} IV={IVTotal(best)}");}
                    else log.WriteLine($"{generation}\t{mode}\t{key}\tMISSING:TARGETED_FORM");
                    audit.Flush();
                }
                all[mode]=current;
                foreach(var (key,pk) in current.OrderBy(x=>x.Key))
                    Write(log,root,generation,mode,key,pk,pk.IsShiny!=shiny?(shiny?"NONSHINY_FALLBACK":"SHINY_FALLBACK"):Identity(pk,p)?"OK":"EVENT_OT_EXCEPTION");
                collections[mode]=current.OrderBy(x=>x.Key).Select(x=>x.Value).ToList();
            }

            // Reattempt only the exact missing event indices from the V2 manifest.
            var events=Load("Events");
            var gifts=EncounterEvent.GetAllEvents().Where(x=>x.Generation==generation && x.Context==save.Context && x.Species>0 && x.Species<=p.Max).ToArray();
            var languages=generation==7
                ? new[]{LanguageID.ChineseS,LanguageID.Japanese,LanguageID.English,LanguageID.French,LanguageID.Italian,LanguageID.German,LanguageID.Spanish,LanguageID.Korean,LanguageID.ChineseT}
                : new[]{LanguageID.Japanese,LanguageID.English,LanguageID.French,LanguageID.Italian,LanguageID.German,LanguageID.Spanish,LanguageID.Korean};
            foreach(var row in baseRows.Where(x=>x[1]=="Events" && x[3].StartsWith("MISSING:")))
            {
                string key=row[2];
                if(events.ContainsKey(key)) continue;
                int index=int.Parse(key.Split('-')[0])-1;
                if(index<0 || index>=gifts.Length){log.WriteLine($"{generation}\tEvents\t{key}\tMISSING:INDEX");continue;}
                var gift=gifts[index];
                if(key!=$"{index+1:D5}-card{gift.CardID:D5}-species{gift.Species:D4}") throw new InvalidOperationException("Event database changed: "+key);
                PKM? best=null;
                var reasons=new Dictionary<string,int>();
                void Count(string reason)=>reasons[reason]=reasons.GetValueOrDefault(reason)+1;
                foreach(var language in languages)
                {
                    foreach(var game in p.Games)
                    {
                        for(int attempt=0;attempt<3;attempt++)
                        {
                            try
                            {
                                var tr=Trainer(p,game,language);
                                var pk=gift.ConvertToPKM(tr);
                                // WC6 recipient-owned simulated Bank and egg templates do not apply recipient IDs in ConvertToPKM.
                                // Only supply recipient identity when the source card does not set a fixed OT and allows a recipient ID.
                                if(gift is WC6 wc && !wc.IsOriginalTrainerNameSet && (wc.IsEgg || (wc.CardID==0 && wc.OTGender==3)))
                                {
                                    pk.ID32=tr.ID32;
                                    pk.OriginalTrainerName=CindyName(pk.Language);
                                    if(pk.IsEgg && pk.EggMetDate==null) pk.EggMetDate=pk.MetDate;
                                    pk.RefreshChecksum();
                                }
                                // Card 0142 is the April 2016 redistribution (Project Pokemon event archive).
                                // Use a valid date inside its documented window only for malformed source dates.
                                if(gift is WC6 {CardID:142,IsEgg:true} && pk.IsEgg && (pk.MetDate==null || pk.EggMetDate==null))
                                {
                                    var distributionDate=new DateOnly(2016,4,1);
                                    if(pk.MetDate==null) pk.MetDate=distributionDate;
                                    if(pk.EggMetDate==null) pk.EggMetDate=distributionDate;
                                    pk.RefreshChecksum();
                                }
                                if(pk.GetType()!=save.PKMType || pk.Species!=gift.Species || pk.Form!=gift.Form){Count("TYPE_OR_FORM");continue;}
                                var la=new LegalityAnalysis(pk);
                                if(!la.Valid){Count("INVALID:"+la.Report().Replace('\r',' ').Replace('\n',' '));continue;}
                                if(la.EncounterMatch is not MysteryGift matched || matched.CardID!=gift.CardID || matched.GetType()!=gift.GetType())
                                {
                                    // Distinct distribution cards can be byte-equivalent in a received egg.
                                    // Archive only if this precise source template also matches, and report ambiguity separately.
                                    if(!gift.IsEgg || la.EncounterMatch is not MysteryGift ||
                                       !gift.IsMatchExact(pk,new EvoCriteria {Species=pk.Species,Form=pk.Form,LevelMin=pk.MetLevel,LevelMax=100}) ||
                                       gift.GetMatchRating(pk)!=EncounterMatchRating.Match)
                                    {Count("MATCHED_OTHER:"+la.EncounterMatch.GetType().Name+":"+(la.EncounterMatch as MysteryGift)?.CardID);continue;}
                                    Count("AMBIGUOUS_EQUIVALENT_EGG_CARD:"+((MysteryGift)la.EncounterMatch).CardID);
                                }
                                if(!NormalizeCindy(pk,p)){Count("IDENTITY");continue;}
                                best=pk;break;
                            }
                            catch(Exception ex){Count(ex.GetType().Name);}
                        }
                        if(best!=null)break;
                    }
                    if(best!=null)break;
                }
                if(best!=null)events[key]=best;
                else log.WriteLine($"{generation}\tEvents\t{key}\tMISSING:EVENT_RETRY");
                audit.WriteLine($"Events\t{key}\t{(best==null?"UNRESOLVED":"REPAIRED")}\t{gift.GetType().Name} Egg={gift.IsEgg} Title={gift.CardTitle.Replace('\t',' ')} "+JsonSerializer.Serialize(reasons));
                audit.Flush();
            }
            foreach(var (key,pk) in events.OrderBy(x=>x.Key))
                Write(log,root,generation,"Events",key,pk,pk.IsShiny?"EVENT_SHINY":"EVENT_NONSHINY");
            collections["Events"]=events.OrderBy(x=>x.Key).Select(x=>x.Value).Where(x=>!x.IsEgg).ToList();
            // Rebuild derived HOME collections from retained and repaired entries only.
            collections["HOME-NonShiny"]=HomeCollection(p,root,"HOME-NonShiny",collections["AllNonShiny"],log);
            collections["HOME-Shiny"]=HomeCollection(p,root,"HOME-Shiny",collections["AllShiny"],log);
            var mixed=Load("Mixed");
            foreach(var (key,pk) in all["AllNonShiny"])
                if(!mixed.ContainsKey(key)) mixed[key]=all["AllShiny"].GetValueOrDefault(key,pk);
            foreach(var (key,pk) in mixed.OrderBy(x=>x.Key)) Write(log,root,generation,"Mixed",key,pk,"MIXED");
            foreach(var (mode,entries) in collections)
            {
                ExportSaveCopies(root,Trainer(p,p.Games[0],lang),mode,entries,log);
                ExportSaveCopies(root,save,mode,entries,log);
            }
        }


        private static void SetupLegacy(Profile p)
        {
            APILegality.UseTrainerData=false;
            APILegality.GameVersionPriority=GameVersionPriorityType.PriorityOrder;
            APILegality.PriorityOrder=[..p.Games];
            APILegality.MatchRequestedBuild=true;
            APILegality.RequestedNatureMatchPolicy=RequestedNatureMatchPolicy.NativeOnly;
            APILegality.ConstrainedIVStrategy=CorrelationConstrainedIVStrategy.PreferHighestLegalIVs;
            Legalizer.EnableEasterEggs=false;
            EncounterEvent.RefreshMGDB();
        }
        private const string ManifestHeader="Gen\tMode\tKey\tStatus\tOriginVersion\tOriginGen\tOT\tTID16\tID32\tShiny\tIVTotal\tLanguage\tNature\tHP_IV\tATK_IV\tDEF_IV\tSPA_IV\tSPD_IV\tSPE_IV\tPreferredNature\tHeldItem\tEV_HP\tEV_ATK\tEV_DEF\tEV_SPA\tEV_SPD\tEV_SPE\tMove1\tMove2\tMove3\tMove4";
        private static GameVersion LegacyCarrier(Profile p) => p.Gen switch {4=>GameVersion.Pt,5=>GameVersion.B2,_=>p.Games[0]};
        public static void RunLegacyShard(int generation,int first,int last,bool shiny,string root)
        {
            var p=Profiles.Single(x=>x.Gen==generation);
            using var scope=APILegality.EnterSettingsScope();
            SetupLegacy(p);
            Directory.CreateDirectory(root);
            using var log=new StreamWriter(Path.Combine(root,"manifest.tsv"),false,Encoding.UTF8);
            log.AutoFlush=true;log.WriteLine(ManifestHeader);
            var save=Trainer(p,LegacyCarrier(p),LanguageID.Japanese);
            RecentTrainerCache.SetRecentTrainer(save);
            // Shard range is consumed by the CI-only ModLogic overlay.
            Environment.SetEnvironmentVariable("CINDY_FIRST",first.ToString());
            Environment.SetEnvironmentVariable("CINDY_LAST",last.ToString());
            Environment.SetEnvironmentVariable("CINDY_FAST","1");
            APILegality.Timeout=6;
            EncounterMovesetGenerator.PriorityList = [EncounterTypeGroup.Egg, EncounterTypeGroup.Slot, EncounterTypeGroup.Static, EncounterTypeGroup.Mystery, EncounterTypeGroup.Trade];
            var cfg=new LivingDexConfig
            {
                IncludeForms=true,SetShiny=shiny,UseRecommendedBattleBuild=true,
                UseRecommendedBattleNature=true,UseRecommendedBattleIVs=true,
                UseRecommendedBattleEVs=true,UseRecommendedBattleMoves=true,
                LivingDexTargetGeneration=(byte)p.Gen,LivingDexTargetVersion=p.Games[0],
            };
            var mode=shiny?"AllShiny":"AllNonShiny";
            using var origins=new StreamWriter(Path.Combine(root,"origin-audit.tsv"));
            origins.AutoFlush=true;origins.WriteLine("Key\tShiny\tEggOrigin\tOriginVersion\tIVTotal\tNature");
            var accepted=new HashSet<(ushort,byte)>();
            var summary=save.GenerateLivingDex(save.Personal,cfg,pk=>
            {
                if(pk.Species<first || pk.Species>last || pk.GetType()!=save.PKMType || pk.IsEgg || !NormalizeCindy(pk,p) || !new LegalityAnalysis(pk).Valid) return;
                if(!shiny && pk.IsShiny) return;
                if(!accepted.Add((pk.Species,pk.Form)))return;
                var analysis=new LegalityAnalysis(pk);
                origins.WriteLine($"{pk.Species:D4}-{pk.Form:D2}\t{pk.IsShiny}\t{analysis.EncounterOriginal.IsEgg}\t{pk.Version}\t{IVTotal(pk)}\t{pk.Nature}");
                Write(log,root,generation,mode,$"{pk.Species:D4}-{pk.Form:D2}",pk,pk.IsShiny!=shiny?"NONSHINY_FALLBACK":Identity(pk,p)?"OK":"EVENT_OT_EXCEPTION");
            },progress=>Console.WriteLine($"Gen{generation} {mode} {first}-{last}: {progress}/{last-first+1} species workers completed"),CancellationToken.None);
            File.WriteAllText(Path.Combine(root,"diagnostics.json"),JsonSerializer.Serialize(summary));
            foreach(var target in summary.FailedTargets)
                log.WriteLine($"{generation}\t{mode}\t{target.Species:D4}-{target.Form:D2}\tMISSING:ENGINE_TARGET");
            Console.WriteLine($"Gen{generation} {mode} {first}-{last} accepted {accepted.Count}");
        }
        public static void RunLegacyEvents(int generation,string root)
        {
            var p=Profiles.Single(x=>x.Gen==generation);
            using var scope=APILegality.EnterSettingsScope();
            SetupLegacy(p);
            Directory.CreateDirectory(root);
            using var log=new StreamWriter(Path.Combine(root,"manifest.tsv"),false,Encoding.UTF8);
            log.AutoFlush=true;log.WriteLine(ManifestHeader);
            var events=Events(p,root,log);
            ExportSaveCopies(root,Trainer(p,p.Games[0],LanguageID.Japanese),"Events",events,log);
        }
        public static void AssembleLegacy(int generation,string baseline,string root)
        {
            var p=Profiles.Single(x=>x.Gen==generation);
            using var scope=APILegality.EnterSettingsScope();SetupLegacy(p);
            Directory.CreateDirectory(root);
            using var log=new StreamWriter(Path.Combine(root,"manifest.tsv"),false,Encoding.UTF8);
            log.AutoFlush=true;log.WriteLine(ManifestHeader);
            var save=Trainer(p,LegacyCarrier(p),LanguageID.Japanese);
            var collections=new Dictionary<string,List<PKM>>();
            Dictionary<(ushort,byte),PKM> Load(string mode)
            {
                var result=new Dictionary<(ushort,byte),PKM>();
                foreach(var file in Directory.EnumerateFiles(baseline,$"*.pk{generation}",SearchOption.AllDirectories))
                {
                    if(Path.GetFileName(Path.GetDirectoryName(file))!=mode) continue;
                    var raw=File.ReadAllBytes(file);
                    PKM pk=generation switch {3=>new PK3(raw),4=>new PK4(raw),_=>new PK5(raw)};
                    if(!NormalizeCindy(pk,p) || !new LegalityAnalysis(pk).Valid || pk.IsEgg || (mode=="AllNonShiny" && pk.IsShiny))continue;
                    var key=(pk.Species,pk.Form);
                    if(!result.TryGetValue(key,out var prev) || (mode=="AllShiny" && pk.IsShiny&&!prev.IsShiny) || (pk.IsShiny==prev.IsShiny&&Quality(pk)>Quality(prev)))result[key]=pk;
                }
                return result;
            }
            var normal=Load("AllNonShiny");
            var shiny=Load("AllShiny");
            foreach(var (key,pk) in normal) if(!shiny.ContainsKey(key))shiny[key]=pk.Clone();
            foreach(var (mode,data) in new[]{("AllNonShiny",normal),("AllShiny",shiny)})
            {
                foreach(var (key,pk) in data.OrderBy(x=>x.Key))
                    Write(log,root,generation,mode,$"{key.Item1:D4}-{key.Item2:D2}",pk,mode=="AllShiny"&&!pk.IsShiny?"NONSHINY_FALLBACK":Identity(pk,p)?"OK":"EVENT_OT_EXCEPTION");
                foreach(var species in Enumerable.Range(1,p.Max).Where(i=>!data.Keys.Any(x=>x.Item1==i)))
                    log.WriteLine($"{generation}\t{mode}\t{species:D4}-00\tMISSING:SPECIES");
                collections[mode]=data.OrderBy(x=>x.Key).Select(x=>x.Value).ToList();
            }
            collections["HOME-NonShiny"]=HomeCollection(p,root,"HOME-NonShiny",collections["AllNonShiny"],log);
            collections["HOME-Shiny"]=HomeCollection(p,root,"HOME-Shiny",collections["AllShiny"],log);
            if(Environment.GetEnvironmentVariable("CINDY_ENTITIES_ONLY")!="1")
            foreach(var (mode,entries) in collections)
            {
                ExportSaveCopies(root,Trainer(p,p.Games[0],LanguageID.Japanese),mode,entries,log);
                if(save.Version!=p.Games[0])ExportSaveCopies(root,save,mode,entries,log);
            }
            Console.WriteLine($"Gen{generation} normal species={normal.Keys.Select(x=>x.Item1).Distinct().Count()} shiny collection species={shiny.Keys.Select(x=>x.Item1).Distinct().Count()}");
        }

        public static void Run(string root, int? generation = null, SaveFile? originalSave = null)
        {
            if (originalSave != null && originalSave.Generation != generation)
                throw new ArgumentException("Template generation does not match requested generation.");
            Directory.CreateDirectory(root);
            using var log = new StreamWriter(Path.Combine(root, "manifest.tsv"), false, Encoding.UTF8);
            log.WriteLine("Gen\tMode\tKey\tStatus\tOriginVersion\tOriginGen\tOT\tTID16\tID32\tShiny\tIVTotal\tLanguage\tNature\tHP_IV\tATK_IV\tDEF_IV\tSPA_IV\tSPD_IV\tSPE_IV\tPreferredNature\tHeldItem\tEV_HP\tEV_ATK\tEV_DEF\tEV_SPA\tEV_SPD\tEV_SPE\tMove1\tMove2\tMove3\tMove4");
            using var scope = APILegality.EnterSettingsScope();
            var easter = Legalizer.EnableEasterEggs;
            try
            {
                APILegality.UseTrainerData = false;
                APILegality.GameVersionPriority = GameVersionPriorityType.PriorityOrder;
                APILegality.MatchRequestedBuild = true;
                APILegality.RequestedNatureMatchPolicy = RequestedNatureMatchPolicy.NativeOnly;
                APILegality.ConstrainedIVStrategy = CorrelationConstrainedIVStrategy.PreferHighestLegalIVs;
                Legalizer.EnableEasterEggs = false;
                EncounterEvent.RefreshMGDB();
                foreach (var p in Profiles.Where(x => generation is null || x.Gen == generation))
                {
                    APILegality.PriorityOrder = [.. p.Games];
                    var lang = p.Gen == 7 ? LanguageID.ChineseS : LanguageID.Japanese;
                    // USUM is the carrier for all Gen7 species; SN/MN remain preferred origins.
                    var carrier = p.Gen switch { 4 => GameVersion.Pt, 5 => GameVersion.B2, 6 => GameVersion.OR, 7 => GameVersion.US, _ => p.Games[0] };
                    var save = Trainer(p, carrier, lang);
                    var events = Events(p, root, log);
                    var normals = Generate(p, save, false, root, log);
                    var shinies = Generate(p, save, true, root, log);
                    if (p.Gen==7)
                    {
                        // Pre-Gen7 origins cannot be Chinese; use a Japanese trainer for missing forms (notably Unown).
                        var japanese=Trainer(p,carrier,LanguageID.Japanese);
                        foreach (var (target,shiny) in new[] {(normals,false),(shinies,true)})
                        {
                            if (target.Keys.Count(k=>k.Item1==201)==28 && target.Keys.Select(k=>k.Item1).Distinct().Count()>=p.Max) continue;
                            foreach (var (key,pk) in Generate(p,japanese,shiny,Path.Combine(root,"JapaneseFallback"),log))
                            {
                                if (!target.TryGetValue(key,out var prev) || (pk.IsShiny==shiny && prev.IsShiny!=shiny))
                                    target[key]=pk;
                            }
                        }
                    }
                    var collections = new Dictionary<string, List<PKM>>
                    {
                        ["Mixed"] = new(), ["AllShiny"] = new(), ["AllNonShiny"] = new(), ["Events"] = events
                    };
                    foreach (var (species, form) in normals.Keys.Concat(shinies.Keys).Concat(events.Select(x => (x.Species, x.Form))).Distinct().OrderBy(x => x.Item1).ThenBy(x => x.Item2))
                    {
                        var key = $"{species:D4}-{form:D2}";
                        try
                        {
                            normals.TryGetValue((species, form), out var normal);
                            shinies.TryGetValue((species, form), out var shiny);
                            if (normal?.IsShiny == true) normal = null;
                            if (shiny?.IsShiny == false) shiny = null;
                            normal ??= events.FirstOrDefault(x => x.Species == species && x.Form == form && !x.IsShiny);
                            shiny ??= events.FirstOrDefault(x => x.Species == species && x.Form == form && x.IsShiny);
                            Write(log, root, p.Gen, "AllShiny", key, shiny ?? normal, shiny is null ? "NONSHINY_FALLBACK" : Identity(shiny, p) ? "OK" : "EVENT_OT_EXCEPTION");
                            Write(log, root, p.Gen, "AllNonShiny", key, normal ?? shiny, normal is null ? "SHINY_FALLBACK" : Identity(normal, p) ? "OK" : "EVENT_OT_EXCEPTION");
                            var preferShiny = (species + form) % 2 == 0;
                            var mixed = preferShiny ? shiny ?? normal : normal ?? shiny;
                            Write(log, root, p.Gen, "Mixed", key, mixed, "MIXED");
                            if ((shiny ?? normal) is PKM sh) collections["AllShiny"].Add(sh);
                            if ((normal ?? shiny) is PKM ns) collections["AllNonShiny"].Add(ns);
                            if (mixed != null) collections["Mixed"].Add(mixed);
                        }
                        catch (Exception ex)
                        {
                            log.WriteLine($"{p.Gen}\tAll\t{key}\tERROR:{ex.GetType().Name}");
                            log.Flush();
                        }
                    }
                    collections["HOME-NonShiny"] = HomeCollection(p,root,"HOME-NonShiny",collections["AllNonShiny"],log);
                    collections["HOME-Shiny"] = HomeCollection(p,root,"HOME-Shiny",collections["AllShiny"],log);
                    foreach (var (mode, entries) in collections.Where(x => x.Key != "Mixed"))
                    {
                        var primary = originalSave ?? Trainer(p, p.Games[0], lang);
                        ExportSaveCopies(root, primary, mode, entries, log);
                        if (originalSave == null && carrier != p.Games[0])
                            ExportSaveCopies(root, save, mode, entries, log);
                    }
                }
            }
            finally
            {
                Legalizer.EnableEasterEggs = easter;
            }
        }
    }
}
