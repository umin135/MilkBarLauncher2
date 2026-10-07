using System.IO;
using System.Reflection;
using System.Runtime.Loader;
var mb = @"D:\GameFolder\WIIU\MilkBar\MilkBarLauncher";
AssemblyLoadContext.Default.Resolving += (c, n) => { var p = Path.Combine(mb, n.Name + ".dll"); return File.Exists(p) ? c.LoadFromAssemblyPath(p) : null; };
AssemblyLoadContext.Default.ResolvingUnmanagedDll += (a, n) => n == "Yaz0" ? System.Runtime.InteropServices.NativeLibrary.Load(Path.Combine(mb, "runtimes", "win-x64", "native", "Yaz0.dll")) : IntPtr.Zero;
var bl = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(mb, "BfresLibrary.dll"));
Type BF = bl.GetTypes().First(t => t.Name == "BfresFile");
dynamic Load(string path) => Activator.CreateInstance(BF, new object[] { new MemoryStream(File.ReadAllBytes(path)), false });
void Save(dynamic f, string path) { var ms = new MemoryStream(); f.ToBinary(ms, true); File.WriteAllBytes(path, ms.ToArray()); }
switch (args[0])
{
    case "mipfix":   // mipfix <in raw> <out raw> <names file>: listed textures with mips>1 -> single level
    {
        var names = new HashSet<string>(File.ReadAllLines(args[3]).Select(l => l.Trim()).Where(l => l != ""));
        dynamic f = Load(args[1]); int n = 0;
        foreach (string k in f.Textures.Keys)
        {
            if (!names.Contains(k)) continue;
            dynamic t = f.Textures[k];
            if ((int)t.MipCount <= 1) continue;
            t.MipCount = (uint)1; t.MipData = new byte[0];
            try { var mo = (uint[])t.MipOffsets; for (int i = 0; i < mo.Length; i++) mo[i] = 0; t.MipOffsets = mo; } catch { }
            n++;
        }
        Save(f, args[2]); Console.WriteLine($"single-level: {n}");
        break;
    }
    case "texhash":   // texhash <raw bfres>...: name<TAB>sha1(level0)<TAB>mips per texture
    {
        foreach (var path in args.Skip(1))
        {
            dynamic f = Load(path);
            foreach (string k in f.Textures.Keys) { dynamic t = f.Textures[k]; byte[] d = t.Data; Console.WriteLine($"{Path.GetFileName(path)}	{k}	{(d == null ? "-" : Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(d)))}	{t.MipCount}"); }
        }
        break;
    }
    case "texprune":   // texprune <in raw> <out raw> <names file>: drop listed textures (Tex2) 
    {
        var names = new HashSet<string>(File.ReadAllLines(args[3]).Select(l => l.Trim()).Where(l => l != ""));
        dynamic f = Load(args[1]); int n = 0;
        foreach (string k in new List<string>((IEnumerable<string>)f.Textures.Keys)) if (names.Contains(k)) { f.Textures.RemoveKey(k); n++; }
        Save(f, args[2]); Console.WriteLine($"removed {n}, left {f.Textures.Count}");
        break;
    }
    case "unittex":   // unittex <model raw> <unit>: textures referenced by a unit
    {
        dynamic f = Load(args[1]); dynamic m = f.Models[args[2]];
        var used = new SortedSet<string>();
        foreach (string matk in m.Materials.Keys) { dynamic mat = m.Materials[matk]; foreach (dynamic tr in mat.TextureRefs) used.Add((string)tr.Name); }
        Console.WriteLine(string.Join(" ", used));
        break;
    }
    case "texcmp":   // texcmp <a raw> <b raw> [names...]: compare texture data between two bfres
    {
        dynamic a = Load(args[1]); dynamic b = Load(args[2]);
        static string H(byte[] d) => d == null ? "-" : Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(d))[..8] + $"/{d.Length}";
        foreach (string k in a.Textures.Keys)
        {
            if (args.Length > 3 && !args.Skip(3).Any(n => k.Contains(n))) continue;
            dynamic x = a.Textures[k]; dynamic y = b.Textures.ContainsKey(k) ? b.Textures[k] : null;
            byte[] xd = x.Data, xm = x.MipData; byte[] yd = y?.Data, ym = y?.MipData;
            Console.WriteLine($"  {k,-30} A mips={x.MipCount} data={H(xd)} mip={H(xm)} | B mips={(y == null ? "-" : y.MipCount)} data={H(yd)} mip={H(ym)}");
        }
        break;
    }
    case "texinfo":   // texinfo <tex1 raw> <tex2 raw> [filter]
    {
        dynamic t1 = Load(args[1]); dynamic t2 = Load(args[2]);
        string flt = args.Length > 3 ? args[3] : "";
        int same = 0, diff = 0, only1 = 0;
        foreach (string k in t1.Textures.Keys)
        {
            dynamic a = t1.Textures[k];
            string sa = $"{a.Width}x{a.Height} mips={a.MipCount} fmt={a.Format}";
            if (!t2.Textures.ContainsKey(k)) { only1++; if (k.Contains(flt)) Console.WriteLine($"  {k,-40} T1 {sa} | T2 -"); continue; }
            dynamic b = t2.Textures[k];
            string sb = $"{b.Width}x{b.Height} mips={b.MipCount} fmt={b.Format}";
            if (sa == sb) same++; else diff++;
            if (k.Contains(flt)) Console.WriteLine($"  {k,-40} T1 {sa} | T2 {sb}");
        }
        Console.WriteLine($"same={same} differ={diff} onlyTex1={only1}");
        break;
    }
    case "local":   // local <big raw (has MP_Linkle unit)> <linkle Armor_Default raw> <big tex1> <big tex2> <outdir> <unitlist out>
    {
        string od = args[5];
        dynamic big = Load(args[1]);
        dynamic local = Load(args[1]);
        dynamic def = Load(args[2]);
        using (var w = new StreamWriter(args[6])) foreach (string k in big.Models.Keys) if (k != "MP_Linkle") w.WriteLine(k);
        foreach (string k in new List<string>((IEnumerable<string>)local.Models.Keys)) if (k != "MP_Linkle") local.Models.RemoveKey(k);
        foreach (var ex in new[] { "Armor_Default_Extra_00", "Armor_Default_Extra_01" }) { dynamic m = def.Models[ex]; local.Models.Add(ex, m); }
        local.Name = "MP_Linkle";
        var used = new HashSet<string>();
        foreach (string mk in local.Models.Keys) { dynamic m = local.Models[mk]; foreach (string matk in m.Materials.Keys) { dynamic mat = m.Materials[matk]; foreach (dynamic tr in mat.TextureRefs) used.Add((string)tr.Name); } }
        Console.WriteLine($"local units={local.Models.Count} textures={used.Count}");
        Save(local, Path.Combine(od, "MP_Linkle.bfres"));
        foreach (var t in new[] { "Tex1", "Tex2" })
        {
            dynamic tex = Load(t == "Tex1" ? args[3] : args[4]);
            foreach (string k in new List<string>((IEnumerable<string>)tex.Textures.Keys)) if (!used.Contains(k)) tex.Textures.RemoveKey(k);
            Console.WriteLine($"  {t}: {tex.Textures.Count}");
            tex.Name = "MP_Linkle." + t;
            Save(tex, Path.Combine(od, $"MP_Linkle.{t}.bfres"));
        }
        break;
    }
    case "split":   // split <big raw> <big tex1 raw> <big tex2 raw> <outdir>
    {
        string od = args[4];
        dynamic big = Load(args[1]);
        dynamic local = Load(args[1]);
        // local: only the full-body unit
        foreach (string k in new List<string>((IEnumerable<string>)local.Models.Keys)) if (k != "MP_Linkle") local.Models.RemoveKey(k);
        local.Name = "MP_Linkle";
        var used = new HashSet<string>();
        foreach (string mk in local.Models.Keys) { dynamic m = local.Models[mk]; foreach (string matk in m.Materials.Keys) { dynamic mat = m.Materials[matk]; foreach (dynamic tr in mat.TextureRefs) used.Add((string)tr.Name); } }
        Console.WriteLine($"local model units={local.Models.Count} textures referenced={used.Count}");
        Save(local, Path.Combine(od, "MP_Linkle.bfres"));
        foreach (var t in new[] { "Tex1", "Tex2" })
        {
            dynamic tex = Load(t == "Tex1" ? args[2] : args[3]);
            int before = tex.Textures.Count;
            foreach (string k in new List<string>((IEnumerable<string>)tex.Textures.Keys)) if (!used.Contains(k)) tex.Textures.RemoveKey(k);
            Console.WriteLine($"  {t}: {before} -> {tex.Textures.Count}");
            tex.Name = "MP_Linkle." + t;
            Save(tex, Path.Combine(od, $"MP_Linkle.{t}.bfres"));
            dynamic texBig = Load(t == "Tex1" ? args[2] : args[3]);
            texBig.Name = "MP_Linkle_MP." + t;
            Save(texBig, Path.Combine(od, $"MP_Linkle_MP.{t}.bfres"));
        }
        // remote: everything except the local-only unit
        big.Models.RemoveKey("MP_Linkle");
        big.Name = "MP_Linkle_MP";
        Save(big, Path.Combine(od, "MP_Linkle_MP.bfres"));
        Console.WriteLine($"remote model units={big.Models.Count}");
        break;
    }
    case "applies":
    {
        var asm = AssemblyLoadContext.Default.LoadFromAssemblyPath(args[1]);
        var t = asm.GetType("Breath_of_the_Wild_Multiplayer.MVVM.ViewModel.ServerBrowserModel");
        var m = t.GetMethod("AppliesLocally");
        foreach (var s in new[] { "MP_Linkle", "Npc_Rito_Hero", "Jugador1ModelNameLongForASpecificReason", "Animal_Bear", "" })
            Console.WriteLine($"  {s,-42} -> {m.Invoke(null, new object[] { s })}");
        break;
    }
    case "types":
        foreach (var t in bl.GetExportedTypes().Where(t => t.Name is "BfresFile" or "Model" or "ResDict`1")) Console.WriteLine(t.FullName);
        foreach (var c in BF.GetConstructors()) Console.WriteLine("ctor(" + string.Join(",", c.GetParameters().Select(p => p.ParameterType.Name)) + ")");
        foreach (var m in BF.GetMethods().Where(m => m.Name is "Save" or "ToBinary")) Console.WriteLine(m);
        break;
    case "gen":
    {
        var asm = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(mb, "Milk Bar Launcher.dll"));
        var st = asm.GetType("Breath_of_the_Wild_Multiplayer.Properties.Settings");
        var def = st.GetProperty("Default", BindingFlags.Static | BindingFlags.Public).GetValue(null);
        st.GetProperty("bcmlLocation").SetValue(def, args[1]);
        var gfm = asm.GetTypes().First(t => t.Name == "GameFilesModifier");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Exception err = null;
        var th = new Thread(() => { try { gfm.GetMethod("CreateModifiedModel", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Invoke(null, null); } catch (Exception e) { err = e; } }, 512 * 1024 * 1024);
        th.Start(); th.Join();
        if (err != null) throw err;
        Console.WriteLine($"CreateModifiedModel done in {sw.Elapsed.TotalSeconds:F1}s");
        break;
    }
    case "finish":   // finish <jugador raw> <link raw> <out raw> <name>
    {
        dynamic jug = Load(args[1]); dynamic link = Load(args[2]);
        dynamic full = link.Models[0];
        full.Name = "MP_Linkle";
        jug.Models.Add("MP_Linkle", full);
        jug.Name = args[4];
        Save(jug, args[3]);
        Console.WriteLine($"models={jug.Models.Count}");
        break;
    }
    case "rename":   // rename <in raw> <out raw> <name>
    {
        dynamic f = Load(args[1]); Console.WriteLine($"{f.Name} -> {args[3]} textures={f.Textures.Count}");
        f.Name = args[3]; Save(f, args[2]); break;
    }
    case "info":
    {
        dynamic f = Load(args[1]);
        Console.WriteLine($"name={f.Name} models={f.Models.Count} textures={f.Textures.Count}");
        int n = 0; foreach (var key in f.Models.Keys) { if (n++ >= int.Parse(args.Length > 2 ? args[2] : "8")) break; dynamic m = f.Models[key]; Console.WriteLine($"  {m.Name} shapes={m.Shapes.Count} bones={m.Skeleton.Bones.Count}"); }
        break;
    }
}
