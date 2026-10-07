// Wrapper installed as Milk Bar's Resources\aapmLib.exe.
// Runs aapmLib_fixed.py (type-preserving AAMP edits) with the Python named in aapmLib_fixed.cfg;
// if that fails, falls back to the original tool kept as aapmLib.orig.exe.
using System.Diagnostics;
var dir = AppContext.BaseDirectory;
int Run(string exe, string args)
{
    var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true });
    var so = p.StandardOutput.ReadToEndAsync(); var se = p.StandardError.ReadToEndAsync();
    p.WaitForExit();
    Console.Write(so.Result);
    if (p.ExitCode != 0) Console.Error.Write(se.Result);
    return p.ExitCode;
}
var log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BOTWM", "Temp", "aapmLib_fixed.log");
try
{
    var cfg = Path.Combine(dir, "aapmLib_fixed.cfg");
    var python = File.Exists(cfg) ? File.ReadAllText(cfg).Trim() : @"D:\Tools\botw-venv\Scripts\python.exe";
    var script = Path.Combine(dir, "aapmLib_fixed.py");
    if (File.Exists(python) && File.Exists(script))
    {
        var code = Run(python, $"\"{script}\"");
        File.AppendAllText(log, $"{DateTime.Now:s} fixed rc={code}\n");
        if (code == 0) return 0;
    }
    else File.AppendAllText(log, $"{DateTime.Now:s} python/script missing: {python}\n");
}
catch (Exception e) { try { File.AppendAllText(log, $"{DateTime.Now:s} error {e.Message}\n"); } catch { } }
var orig = Path.Combine(dir, "aapmLib.orig.exe");
File.AppendAllText(log, $"{DateTime.Now:s} fallback to original\n");
return Run(orig, "");
