// Pudge Wars asset extractor: UE5.3 IoStore -> JSON (meshes/anims/materials) + PNG (textures)
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Versions;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Animation;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse_Conversion.Meshes;
using CUE4Parse_Conversion.Dto;
using CUE4Parse_Conversion.Animations;
using CUE4Parse_Conversion.Textures;
using CUE4Parse_Conversion.Options;
using Newtonsoft.Json;

var paks = args[0]; var outDir = args[1]; var roots = args.Skip(2).ToArray();
var p = new DefaultFileProvider(paks, SearchOption.AllDirectories, new VersionContainer(EGame.GAME_UE5_3));
p.Initialize(); p.Mount();
p.MappingsContainer = new CUE4Parse.MappingsProvider.Usmap.FileUsmapTypeMappingsProvider(Environment.GetEnvironmentVariable("USMAP")!, StringComparer.OrdinalIgnoreCase);
Directory.CreateDirectory(outDir);

static float[] T(FVector v) => new[] { (float)v.X, (float)v.Y, (float)v.Z };
static float[] Q(FQuat q) => new[] { (float)q.X, (float)q.Y, (float)q.Z, (float)q.W };
static object Xf(FTransform t) => new { t = T(t.Translation), r = Q(t.Rotation), s = T(t.Scale3D) };
static string? PI(CUE4Parse.UE4.Objects.UObject.FPackageIndex? i) => i == null || i.IsNull ? null : i.ResolvedObject?.GetPathName(false);
string Rel(string objPath) => objPath.Split('.')[0].Replace("/Game/", "");
void Save(string rel, object o)
{
    var f = Path.Combine(outDir, rel); Directory.CreateDirectory(Path.GetDirectoryName(f)!);
    File.WriteAllText(f, JsonConvert.SerializeObject(o));
}

var texturesOnly = Environment.GetEnvironmentVariable("TEXTURES_ONLY") == "1";
var report = new List<string>();
var files = p.Files.Keys.Where(f => f.EndsWith(".uasset") && roots.Any(r => f.StartsWith(r, StringComparison.OrdinalIgnoreCase))).OrderBy(f => f).ToList();
foreach (var file in files)
{
    IEnumerable<UObject> exports;
    try { exports = p.LoadPackage(file).GetExports().ToList(); }
    catch (Exception ex) { report.Add($"FAIL load {file}: {ex.Message}"); continue; }
    foreach (var e in exports)
    {
        if (texturesOnly && e is not UTexture2D) continue;
        var rel = Rel(e.GetPathName());
        try
        {
            switch (e)
            {
                case USkeletalMesh m:
                {
                    if (!MeshConverter.TryConvert(m, out SkeletalMeshDto dto, EMeshQuality.Highest)) { report.Add("FAIL convert " + rel); break; }
                    var lod = dto.LODs[0];
                    Save(rel + ".skmesh.json", new
                    {
                        name = m.Name,
                        skeleton = PI(m.Skeleton),
                        bones = dto.Bones.Select(b => new { name = b.Name, parent = b.ParentIndex, xf = Xf(b.Transform) }),
                        materials = dto.Materials.Select(mt => new { slot = mt.SlotName, path = PI(mt.Material) }),
                        pos = lod.Vertices.SelectMany(v => T(v.Position)),
                        nrm = lod.Vertices.SelectMany(v => new[] { (float)v.Normal.X, (float)v.Normal.Y, (float)v.Normal.Z }),
                        uv = lod.Vertices.SelectMany(v => new[] { v.Uv.U, v.Uv.V }),
                        uv2 = lod.ExtraUvs?.Select(a => a.SelectMany(u => new[] { u.U, u.V })),
                        inf = lod.Vertices.Select(v => v.Influences.Where(i => i.Weight > 0).SelectMany(i => new[] { (float)i.Bone, i.Weight })),
                        idx = lod.Indices,
                        sections = lod.Sections.Select(s => new { mat = s.MaterialIndex, first = s.FirstIndex, faces = s.NumFaces }),
                    });
                    report.Add($"skmesh {rel} verts {lod.Vertices.Length} bones {dto.Bones.Length}");
                    break;
                }
                case UStaticMesh sm:
                {
                    if (!MeshConverter.TryConvert(sm, out StaticMeshDto dto, EMeshQuality.Highest, ENaniteMeshFormat.NoNanite, null)) { report.Add("FAIL convert " + rel); break; }
                    var lod = dto.LODs[0];
                    Save(rel + ".stmesh.json", new
                    {
                        name = sm.Name,
                        materials = dto.Materials.Select(mt => new { slot = mt.SlotName, path = PI(mt.Material) }),
                        pos = lod.Vertices.SelectMany(v => T(v.Position)),
                        nrm = lod.Vertices.SelectMany(v => new[] { (float)v.Normal.X, (float)v.Normal.Y, (float)v.Normal.Z }),
                        uv = lod.Vertices.SelectMany(v => new[] { v.Uv.U, v.Uv.V }),
                        uv2 = lod.ExtraUvs?.Select(a => a.SelectMany(u => new[] { u.U, u.V })),
                        idx = lod.Indices,
                        sections = lod.Sections.Select(s => new { mat = s.MaterialIndex, first = s.FirstIndex, faces = s.NumFaces }),
                    });
                    report.Add($"stmesh {rel} verts {lod.Vertices.Length}");
                    break;
                }
                case USkeleton sk:
                {
                    var rs = sk.ReferenceSkeleton;
                    Save(rel + ".skeleton.json", new
                    {
                        name = sk.Name,
                        bones = rs.FinalRefBoneInfo.Select((b, i) => new { name = b.Name.Text, parent = b.ParentIndex, xf = Xf(rs.FinalRefBonePose[i]) }),
                        sockets = sk.Sockets?.Select(s => s.Load<USkeletalMeshSocket>()).Where(s => s != null).Select(s => new { name = s!.SocketName.Text, bone = s.BoneName.Text, t = T(s.RelativeLocation), r = new[] { (float)s.RelativeRotation.Pitch, (float)s.RelativeRotation.Yaw, (float)s.RelativeRotation.Roll }, s = T(s.RelativeScale) }),
                    });
                    report.Add($"skeleton {rel} bones {rs.FinalRefBoneInfo.Length}");
                    break;
                }
                case UAnimSequence a:
                {
                    var set = a.ConvertAnims();
                    var skel = set.Skeleton.ReferenceSkeleton;
                    foreach (var seq in set.Sequences)
                    {
                        var n = Math.Max(seq.NumFrames, 1);
                        var tracks = new List<object?>();
                        for (int b = 0; b < skel.FinalRefBoneInfo.Length; b++)
                        {
                            var tr = b < seq.Tracks.Count ? seq.Tracks[b] : null;
                            if (tr == null || !tr.HasKeys()) { tracks.Add(null); continue; }
                            var pos = new List<float>(); var rot = new List<float>(); var scl = new List<float>();
                            for (int f = 0; f < n; f++)
                            {
                                FQuat q = skel.FinalRefBonePose[b].Rotation; FVector t = skel.FinalRefBonePose[b].Translation; FVector s = skel.FinalRefBonePose[b].Scale3D;
                                tr.GetBoneTransform(f, n, ref q, ref t, ref s);
                                pos.AddRange(T(t)); rot.AddRange(Q(q)); scl.AddRange(T(s));
                            }
                            tracks.Add(new { pos, rot, scl });
                        }
                        Save(rel + ".anim.json", new
                        {
                            name = a.Name,
                            skeleton = set.Skeleton.GetPathName(),
                            fps = seq.FramesPerSecond,
                            frames = n,
                            length = a.SequenceLength,
                            rateScale = a.RateScale,
                            additive = seq.IsAdditive,
                            bones = skel.FinalRefBoneInfo.Select(b => b.Name.Text),
                            parents = skel.FinalRefBoneInfo.Select(b => b.ParentIndex),
                            refpose = skel.FinalRefBonePose.Select(Xf),
                            tracks,
                            notifies = a.Notifies?.Select(nf => new { name = nf.NotifyName?.Text, time = nf.LinkValue, notify = PI(nf.Notify) }),
                        });
                        report.Add($"anim {rel} frames {n} fps {seq.FramesPerSecond} additive {seq.IsAdditive} keyed {tracks.Count(t => t != null)}/{tracks.Count}");
                    }
                    break;
                }
                case UTexture2D tex:
                {
                    var ct = TextureDecoder.Decode(tex, ETexturePlatform.DesktopMobile);
                    if (ct == null) { report.Add("FAIL decode " + rel); break; }
                    var f = Path.Combine(outDir, rel + ".raw"); Directory.CreateDirectory(Path.GetDirectoryName(f)!);
                    File.WriteAllBytes(f, ct.Data);
                    File.WriteAllText(Path.Combine(outDir, rel + ".rawinfo.json"), JsonConvert.SerializeObject(new { w = ct.Width, h = ct.Height, fmt = ct.PixelFormat.ToString(), len = ct.Data.Length, srcfmt = tex.Format.ToString(), srgb = tex.SRGB, comp = tex.CompressionSettings.ToString(), normal = tex.IsNormalMap }));
                    report.Add($"texture {rel} {ct.Width}x{ct.Height} {tex.Format} srgb {tex.SRGB} comp {tex.CompressionSettings} normal {tex.IsNormalMap}");
                    break;
                }
                case UMaterialInterface mi:
                {
                    var o = new Dictionary<string, object?> { ["name"] = mi.Name, ["class"] = mi.ExportType };
                    if (mi is UMaterialInstanceConstant mic)
                    {
                        o["parent"] = PI(mic.Parent);
                        o["textures"] = mic.TextureParameterValues.ToDictionary(t => t.ParameterInfo.Name.Text, t => PI(t.ParameterValue));
                        o["scalars"] = mic.ScalarParameterValues.ToDictionary(t => t.ParameterInfo.Name.Text, t => (object)t.ParameterValue);
                        o["vectors"] = mic.VectorParameterValues.ToDictionary(t => t.ParameterInfo.Name.Text, t => (object)new[] { t.ParameterValue?.R, t.ParameterValue?.G, t.ParameterValue?.B, t.ParameterValue?.A });
                    }
                    if (mi is UMaterial mat)
                    {
                        o["blend"] = mat.BlendMode.ToString(); o["twoSided"] = mat.TwoSided; o["shading"] = mat.ShadingModel.ToString();
                        o["textures"] = mat.ReferencedTextures.Select(t => t?.ToString()).Distinct().ToList();
                    }
                    var cp = new CMaterialParams2(); mi.GetParams(cp, CUE4Parse.UE4.Assets.Exports.Material.EMaterialDepth.AllLayersNoRef);
                    o["params"] = new { textures = cp.Textures.ToDictionary(k => k.Key, k => k.Value?.ToString()), colors = cp.Colors.ToDictionary(k => k.Key, k => new[] { k.Value.R, k.Value.G, k.Value.B, k.Value.A }), scalars = cp.Scalars, blend = cp.BlendMode.ToString() };
                    Save(rel + ".material.json", o);
                    report.Add($"material {rel}");
                    break;
                }
            }
        }
        catch (Exception ex) { report.Add($"FAIL {e.ExportType} {rel}: {ex.GetType().Name} {ex.Message}"); }
    }
}
File.WriteAllLines(Path.Combine(outDir, "report.txt"), report);
foreach (var r in report) Console.WriteLine(r);
