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
