using OhMyHarness.Core;
using System.Text.Json.Nodes;

int checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine("OK: " + message); checks++; }
var root = Path.Combine(PortableStorage.Root, "probe-profile-" + Guid.NewGuid().ToString("N"));
PortableStorage.UseDatabase(Path.Combine(root, "database.sqlite"));
using var run = new ConversationSession(new Chat { Id = 41 }, new Project(), new Provider(), new AppState { EnabledSkills = "python" }, "", [], Path.Combine(root, "database.sqlite"));
bool allowed = false; string skill = "python"; int permissions = 0;
Task<bool> Approve(string scope, string title, string detail, CancellationToken ct) { permissions++; return Task.FromResult(allowed); }
Task<string> Tool(string name, JsonObject p) => PythonTools.CallAsync(run, name, p, () => skill, Approve, CancellationToken.None);
var info = JsonNode.Parse(await Tool("python_info", []))!;
Check(info["Version"]!.GetValue<string>() == "3.13.15", "Bundled pinned Python version");
Check(!Directory.Exists(root), "Info does not extract or create directories");
var denied = await Tool("write_python_script", new() { ["path"] = "demo.py", ["code"] = "print('denied')" });
Check(denied.Contains("denied") && !Directory.Exists(root), "Denied write leaves no files");
allowed = true;
await Tool("write_python_script", new() { ["path"] = "helper.py", ["code"] = "VALUE='français'" });
await Tool("write_python_script", new() { ["path"] = "demo.py", ["code"] = "import sys,json,sqlite3,ssl,helper,pathlib\nprint(json.dumps({'version':sys.version.split()[0], 'value':helper.VALUE, 'args':sys.argv[1:], 'exe':sys.executable},ensure_ascii=False))\npathlib.Path('created.txt').write_text('réussi',encoding='utf-8')\nprint('stderr marker',file=sys.stderr)" });
var oldHome = Environment.GetEnvironmentVariable("PYTHONHOME"); var oldPath = Environment.GetEnvironmentVariable("PYTHONPATH");
Environment.SetEnvironmentVariable("PYTHONHOME", "nonexistent-system-python"); Environment.SetEnvironmentVariable("PYTHONPATH", "nonexistent-system-python");
JsonNode result;
try { result = JsonNode.Parse(await Tool("run_python_script", new() { ["path"] = "demo.py", ["args"] = new JsonArray("with spaces", "$(not-shell)") }))!; }
finally { Environment.SetEnvironmentVariable("PYTHONHOME", oldHome); Environment.SetEnvironmentVariable("PYTHONPATH", oldPath); }
Check(result["exit_code"]!.GetValue<int>() == 0 && !result["timed_out"]!.GetValue<bool>(), "Real interpreter executes successfully");
var output = JsonNode.Parse(result["stdout"]!.GetValue<string>())!;
Check(output["version"]!.GetValue<string>() == "3.13.15" && output["value"]!.GetValue<string>() == "français", "Standard library, SQLite, SSL and sibling modules; UTF-8");
Check(output["args"]![1]!.GetValue<string>() == "$(not-shell)", "Arguments passed literally without a shell");
Check(output["exe"]!.GetValue<string>().StartsWith(Path.Combine(root, "runtimes")), "Interpreter extracted beside the portable profile");
Check(result["stderr"]!.GetValue<string>().Contains("stderr marker") && File.Exists(Path.Combine(PythonTools.ScriptsDirectory(41), "created.txt")), "Captured stderr and working directory");
allowed = false;
Check((await Tool("run_python_script", new() { ["path"] = "demo.py" })).Contains("denied"), "Execution permission can be denied separately");
allowed = true;
await Tool("write_python_script", new() { ["path"] = "fail.py", ["code"] = "raise ValueError('expected failure')" });
result = JsonNode.Parse(await Tool("run_python_script", new() { ["path"] = "fail.py" }))!;
Check(result["exit_code"]!.GetValue<int>() != 0 && result["stderr"]!.GetValue<string>().Contains("expected failure"), "Traceback and nonzero exit code retained");
await Tool("write_python_script", new() { ["path"] = "slow.py", ["code"] = "import time\nprint('started',flush=True)\ntime.sleep(30)" });
result = JsonNode.Parse(await Tool("run_python_script", new() { ["path"] = "slow.py", ["timeout_seconds"] = 1 }))!;
Check(result["timed_out"]!.GetValue<bool>(), "Execution timeout stops Python");
Check(result["stdout"]!.GetValue<string>().Contains("started"), "Timeout retains partial output");
await Tool("write_python_script", new() { ["path"] = "types.py", ["code"] = "from __future__ import annotations\nfrom dataclasses import dataclass\nimport pickle\n@dataclass\nclass Demo:\n    value: str\nprint(pickle.loads(pickle.dumps(Demo('ok'))).value)" });
result = JsonNode.Parse(await Tool("run_python_script", new() { ["path"] = "types.py" }))!;
Check(result["exit_code"]!.GetValue<int>() == 0 && result["stdout"]!.GetValue<string>().Contains("ok"), "Main module supports dataclasses and pickle");
bool rejected = false; try { PythonTools.ScriptPath(41, "../../other.py"); } catch (UnauthorizedAccessException) { rejected = true; }
Check(rejected, "Script cannot escape conversation directory");
Check(PythonTools.ScriptPath(42, "demo.py") != PythonTools.ScriptPath(41, "demo.py"), "Scripts are conversation-scoped");
Check(AgentPolicy.Allowed("plan", "python_info") && !AgentPolicy.Allowed("plan", "write_python_script") && !AgentPolicy.Allowed("plan", "run_python_script"), "Plan forbids writes and execution");
Check(!SandboxWorkspace.Allowed("run_python_script"), "Container sandbox cannot execute host Python");
skill = ""; rejected = false; try { await Tool("python_info", []); } catch (UnauthorizedAccessException) { rejected = true; }
Check(rejected, "Disabled skill rejected");
Console.WriteLine($"{checks} Python checks passed. Portable fixture: {root}");
