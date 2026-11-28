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

        private static bool Identity(PKM pk, Profile p) =>
            pk.OriginalTrainerName == (pk.Language is (int)LanguageID.Japanese or (int)LanguageID.ChineseS or (int)LanguageID.ChineseT ? "Ｃｉｎｄｙ" : "Cindy") &&
            (p.Gen == 7 ? pk.ID32 % 1000000 == p.DisplayId : pk.TID16 == p.DisplayId);

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
                if (!Identity(pk, p) && !FixedEvent(pk))
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
            log.WriteLine($"{gen}\t{mode}\t{key}\t{status}\t{pk.Version}\t{pk.Generation}\t{pk.OriginalTrainerName}\t{pk.TID16}\t{pk.ID32}\t{pk.IsShiny}\t{IVTotal(pk)}\t{pk.Language}\t{pk.Nature}\t{pk.IV_HP}\t{pk.IV_ATK}\t{pk.IV_DEF}\t{pk.IV_SPA}\t{pk.IV_SPD}\t{pk.IV_SPE}\t{Preferred(pk)}");
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
                        pk.IsShiny != expected.IsShiny || pk.Nature != expected.Nature ||
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

        public static void Run(string root, int? generation = null, SaveFile? originalSave = null)
        {
            if (originalSave != null && originalSave.Generation != generation)
                throw new ArgumentException("Template generation does not match requested generation.");
            Directory.CreateDirectory(root);
            using var log = new StreamWriter(Path.Combine(root, "manifest.tsv"), false, Encoding.UTF8);
            log.WriteLine("Gen\tMode\tKey\tStatus\tOriginVersion\tOriginGen\tOT\tTID16\tID32\tShiny\tIVTotal\tLanguage\tNature\tHP_IV\tATK_IV\tDEF_IV\tSPA_IV\tSPD_IV\tSPE_IV\tPreferredNature");
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
