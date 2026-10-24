// Dump raw (still compressed) audio of SoundWaves under given roots.
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Versions;
using CUE4Parse.UE4.Assets.Exports.Sound;
using CUE4Parse_Conversion.Sounds;
var p = new DefaultFileProvider(args[0], SearchOption.AllDirectories, new VersionContainer(EGame.GAME_UE5_3));
p.Initialize(); p.Mount();
p.MappingsContainer = new CUE4Parse.MappingsProvider.Usmap.FileUsmapTypeMappingsProvider(Environment.GetEnvironmentVariable("USMAP")!, StringComparer.OrdinalIgnoreCase);
foreach (var f in p.Files.Keys.Where(f => f.EndsWith(".uasset") && args.Skip(2).Any(r => f.StartsWith(r))))
    foreach (var e in p.LoadPackage(f).GetExports())
        if (e is USoundWave w)
        {
            w.Decode(false, out var fmt, out var data);
            if (data == null) { Console.WriteLine("NODATA " + f); continue; }
            var rel = f.Replace("PudgeWars/Content/PudgeWars/Assets/Sounds/", "").Replace(".uasset", "");
            var o = Path.Combine(args[1], rel + "." + fmt.ToLower()); Directory.CreateDirectory(Path.GetDirectoryName(o)!);
            File.WriteAllBytes(o, data);
            Console.WriteLine($"{rel} {fmt} {data.Length}");
        }
