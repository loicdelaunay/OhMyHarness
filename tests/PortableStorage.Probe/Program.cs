using OhMyHarness.Core;

var expected = Path.GetFullPath(args.Single());
if (!PlatformSupport.PathComparer.Equals(expected, PortableStorage.Root))
    throw new Exception($"Wrong root: {PortableStorage.Root}; expected {expected}");
if (PlatformSupport.PathComparer.Equals(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), expected))
    throw new Exception("This regression check must run with full single-file extraction enabled.");
PortableStorage.EnsureWritable();
// Explicit path avoids importing any real user data during the regression test.
using var db = new HarnessDb(HarnessDb.DatabasePath);
await db.InitializeAsync();
new CustomSkills(CustomSkills.DefaultRoot).EnsureTemplate();
if (!File.Exists(Path.Combine(expected, "database.sqlite")) || !File.Exists(Path.Combine(expected, "skills", "exemple-revue", "SKILL.md")))
    throw new Exception("Portable files are missing beside the executable.");
Console.WriteLine("PASS: SQLite and skills beside actual EXE, despite full extraction and different working directory.");
