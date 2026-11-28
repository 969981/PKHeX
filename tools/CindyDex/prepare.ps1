$ErrorActionPreference = 'Stop'
$plugin = 'PKHeX-Plugins/PKHeX-Plugins_CHS'
Copy-Item 'tools/CindyDex/CindyDexExporter.cs' "$plugin/PKHeX.Core.AutoMod/Enhancements/CindyDexExporter.cs" -Force
Copy-Item 'tools/CindyDex/Program.cs' "$plugin/CindyDex.Cli/Program.cs" -Force
Copy-Item 'tools/CindyDex/CindyDex.Cli.csproj' "$plugin/CindyDex.Cli/CindyDex.Cli.csproj" -Force
# Request 31 in every IV rather than competitive zeroes; the core still checks legality.
$p = "$plugin/PKHeX.Core.AutoMod/Enhancements/LivingDexRecommendedBuild.cs"
$s = Get-Content $p -Raw
$s = $s.Replace('var atk = Nature is Nature.Timid or Nature.Quiet ? 0 : 31;', 'var atk = 31;').Replace('var spe = Nature is Nature.Brave or Nature.Quiet ? 0 : 31;', 'var spe = 31;')
Set-Content $p $s -Encoding utf8
$p = "$plugin/PKHeX.Core.AutoMod/Enhancements/ModLogic.cs"
$s = Get-Content $p -Raw
$s = $s.Replace('var attack = profile.Nature is Nature.Timid or Nature.Quiet ? 0 : 31;', 'var attack = 31;').Replace('var speed = profile.Nature is Nature.Brave or Nature.Quiet ? 0 : 31;', 'var speed = 31;')
Set-Content $p $s -Encoding utf8

# Expose a targeted entry point that follows the full Living Dex trainer override path.
$p = "$plugin/PKHeX.Core.AutoMod/Enhancements/ModLogic.cs"
$s = Get-Content $p -Raw
$anchor = '    public static bool GetRandomEncounter(this ITrainerInfo tr, ushort species, byte form, bool shiny, bool alpha, out PKM? pk)'
if (-not $s.Contains($anchor)) { throw 'Pinned ModLogic API changed' }
$helper = @'
    public static bool GetCindyTargetEncounter(this ITrainerInfo tr, ushort species, byte form, bool shiny, out PKM? pk)
    {
        var cfg = new LivingDexConfig
        {
            IncludeForms = true, SetShiny = shiny, UseRecommendedBattleBuild = true,
            UseRecommendedBattleNature = true, UseRecommendedBattleIVs = true,
            UseRecommendedBattleEVs = true, UseRecommendedBattleMoves = true,
        };
        pk = AddPKM(tr, tr, species, form, cfg, null, trainerOverride: tr);
        return pk is not null;
    }

'@
$s = $s.Replace($anchor, $helper + $anchor)
Set-Content $p $s -Encoding utf8

# CI-only legacy sharding; leave plugin upstream untouched.
$p = "$plugin/PKHeX.Core.AutoMod/Enhancements/ModLogic.cs"
$s = Get-Content $p -Raw
$old = 'int nextSpeciesToStream = 1;'
if (-not $s.Contains($old)) { throw 'Range overlay anchor missing' }
$s = $s.Replace($old, @'
int firstSpecies = int.TryParse(Environment.GetEnvironmentVariable("CINDY_FIRST"), out var first) ? Math.Clamp(first,1,personal.MaxSpeciesID) : 1;
        int lastSpecies = int.TryParse(Environment.GetEnvironmentVariable("CINDY_LAST"), out var last) ? Math.Clamp(last,firstSpecies,personal.MaxSpeciesID) : personal.MaxSpeciesID;
        int nextSpeciesToStream = firstSpecies;
'@)
$s = $s.Replace('Parallel.For(1, personal.MaxSpeciesID + 1, options, ProcessSpecies);','Parallel.For(firstSpecies, lastSpecies + 1, options, ProcessSpecies);')
$s = $s.Replace('sav.Context is EntityContext.Gen7 or EntityContext.Gen9a ? 1 : LivingDexGenerateAttempts;', 'Environment.GetEnvironmentVariable("CINDY_FAST") == "1" || sav.Context is EntityContext.Gen7 or EntityContext.Gen9a ? 1 : LivingDexGenerateAttempts;')
$s = $s.Replace('attempt < LivingDexCoreLegalizeAttempts;', 'attempt < (Environment.GetEnvironmentVariable("CINDY_FAST") == "1" ? 1 : LivingDexCoreLegalizeAttempts);')
$s = $s.Replace('if (context is EntityContext.Gen7)' + [Environment]::NewLine + '        {', 'if (context is EntityContext.Gen7)' + [Environment]::NewLine + '        {')
# Limit build fallback stages for the initial legacy coverage pass.
$anchor = '        return Enum.GetValues<LivingDexBuildFallbackStage>();'
if (-not $s.Contains($anchor)) { throw 'Fallback overlay anchor missing' }
$s = $s.Replace($anchor, @'
        if (Environment.GetEnvironmentVariable("CINDY_FAST") == "1")
            return [LivingDexBuildFallbackStage.FullBuild, LivingDexBuildFallbackStage.WithoutCompetitiveStats];
        return Enum.GetValues<LivingDexBuildFallbackStage>();
'@)
Set-Content $p $s -Encoding utf8

# Egg-first coverage: one relaxed encounter pass rather than repeated full build searches.
$p = "$plugin/PKHeX.Core.AutoMod/Enhancements/ModLogic.cs"
$s = Get-Content $p -Raw
$anchor = '        // ZA''s shiny/Alpha encounters are seed-correlated.'
if (-not $s.Contains($anchor)) { throw 'Fast path anchor missing' }
$fast = @'
        if (Environment.GetEnvironmentVariable("CINDY_FAST") == "1" && sav.Generation is >= 3 and <= 5)
        {
            var relaxed = cfg with
            {
                SetShiny = requireShiny, UseRecommendedBattleBuild = false,
                UseRecommendedBattleNature = false, UseRecommendedBattleIVs = false,
                UseRecommendedBattleEVs = false, UseRecommendedBattleMoves = false,
            };
            PKM? candidate = AddPKM(sav,tr,species,form,relaxed,customBuild,trainerOverride:tr);
            if (!IsAcceptedLivingDexResult(candidate,species,form,true,requireShiny) ||
                (!cfg.SetShiny && candidate?.IsShiny==true))
            {
                failureReason = LivingDexFailureReason.NoEncounter;
                failureReport = "Initial egg-first bounded encounter pass did not return a matching legal individual.";
                return null;
            }
            // Apply each battle field independently, keeping the last fully legal state.
            // Never overwrite a correlated PID/IV or nature when that makes the individual illegal.
            var best = candidate!;
            var flags = relaxed with {UseRecommendedBattleBuild=true};
            foreach (var stage in new[]
            {
                flags with {UseRecommendedBattleEVs=true},
                flags with {UseRecommendedBattleMoves=true},
                flags with {UseRecommendedBattleNature=true},
                flags with {UseRecommendedBattleIVs=true},
            })
            {
                var improved = ApplyFastRecommendedBattleBuild(best,stage);
                if (improved is not null && improved.IsShiny==best.IsShiny &&
                    IsAcceptedLivingDexResult(improved,species,form,true,requireShiny))
                    best=improved;
            }
            return best;
        }

'@
$s = $s.Replace($anchor, $fast + $anchor)
Set-Content $p $s -Encoding utf8
