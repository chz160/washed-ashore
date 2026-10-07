"""Offline Roslyn compile of the W4-touched assemblies, staged files overlaid on the project, in dependency order."""
import glob, json, os, subprocess, sys

PROJ = r"E:/GitHub/washed-ashore"
STAGE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "new")
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")
ED = r"C:/Program Files/Unity/Hub/Editor/6000.6.4f1/Editor/Data"
CSC = r"C:/Program Files/dotnet/sdk/8.0.425/Roslyn/bincore/csc.dll"
NUNIT = glob.glob(PROJ + "/Library/PackageCache/com.unity.ext.nunit@*/net472/unity-custom/nunit.framework.dll")[0]

# asmdef path (relative to Assets) -> editor assembly?
ORDER = [
    ("Scripts/World/WashedAshore.World.asmdef", False),
    ("Scripts/Gameplay/WashedAshore.Gameplay.asmdef", False),
    ("Scripts/Level/WashedAshore.Level.asmdef", False),
    ("Scripts/Wildlife/WashedAshore.Wildlife.asmdef", False),
    ("Scripts/Birds/WashedAshore.Birds.asmdef", False),
    ("Scripts/Perf/WashedAshore.Perf.asmdef", False),
    ("Editor/Level/WashedAshore.Level.Editor.asmdef", True),
    ("Editor/Wildlife/WashedAshore.Wildlife.Editor.asmdef", True),
    ("Editor/Wildlife/Level/WashedAshore.Wildlife.Level.Editor.asmdef", True),
    ("Editor/Birds/WashedAshore.Birds.Editor.asmdef", True),
    ("Editor/Habitat/WashedAshore.Habitat.Editor.asmdef", True),
    ("Tests/EditMode/WashedAshore.Tests.EditMode.asmdef", True),
    ("Tests/PlayMode/WashedAshore.Tests.PlayMode.asmdef", False),
    ("Tests/PlayMode/Wildlife/WashedAshore.Tests.Wildlife.asmdef", False),
    ("Tests/PlayMode/Birds/WashedAshore.Tests.Birds.asmdef", False),
    ("Tests/Performance/WashedAshore.Tests.Performance.asmdef", False),
]

def overlay_path(rel):
    s = os.path.join(STAGE, "Assets", rel)
    return s if os.path.exists(s) else os.path.join(PROJ, "Assets", rel)

def asmdef_dirs():
    dirs = set()
    for root in (PROJ + "/Assets", STAGE + "/Assets"):
        for p in glob.glob(root + "/**/*.asmdef", recursive=True):
            dirs.add(os.path.relpath(os.path.dirname(p), root).replace("\\", "/"))
    return dirs

def sources(adir, all_dirs):
    files = {}
    for root in (PROJ + "/Assets", STAGE + "/Assets"):
        base = os.path.join(root, adir)
        for p in glob.glob(base + "/**/*.cs", recursive=True):
            rel = os.path.relpath(p, root).replace("\\", "/")
            d = os.path.dirname(rel)
            owner = max((x for x in all_dirs if d == x or d.startswith(x + "/")), key=len)
            if owner == adir:
                files[rel] = overlay_path(rel)
    return sorted(files.values())

def main():
    os.makedirs(OUT, exist_ok=True)
    all_dirs = asmdef_dirs()
    base_refs = [ED + "/NetStandard/ref/2.1.0/netstandard.dll", NUNIT]
    base_refs += glob.glob(ED + "/NetStandard/compat/2.1.0/shims/netfx/*.dll")
    base_refs += glob.glob(ED + "/Managed/UnityEngine/*.dll")
    built = {}
    ok = True
    for asm_rel, editor in ORDER:
        meta = json.load(open(overlay_path(asm_rel), encoding="utf-8"))
        name = meta["name"]
        adir = os.path.dirname(asm_rel)
        srcs = sources(adir, all_dirs)
        lib = {os.path.splitext(os.path.basename(p))[0]: p for p in glob.glob(PROJ + "/Library/ScriptAssemblies/*.dll")}
        lib.update(built)
        lib.pop(name, None)
        refs = list(base_refs) + list(lib.values())
        defines = "UNITY_EDITOR;UNITY_INCLUDE_TESTS;UNITY_6000_0_OR_NEWER;UNITY_2021_3_OR_NEWER;UNITY_STANDALONE_WIN;UNITY_STANDALONE"
        out = os.path.join(OUT, name + ".dll")
        rsp = os.path.join(OUT, name + ".rsp")
        with open(rsp, "w", encoding="utf-8") as f:
            f.write("-nostdlib\n-noconfig\n-target:library\n-nowarn:CS0618,CS0649,CS0169,CS0414,CS1701,CS1702\n-langversion:9.0\n")
            f.write(f"-define:{defines}\n-out:\"{out}\"\n")
            for r in refs: f.write(f"\"-r:{r}\"\n")
            for s in srcs: f.write(f"\"{s}\"\n")
        r = subprocess.run(["C:/Program Files/dotnet/dotnet.exe", CSC, "@" + rsp], capture_output=True, text=True)
        errs = [l for l in r.stdout.splitlines() if ": error " in l]
        print(f"{name}: {len(srcs)} files, exit {r.returncode}, errors {len(errs)}")
        for l in errs[:30]: print("   ", l)
        if r.returncode != 0:
            ok = False
            if not errs: print(r.stdout[-2000:], r.stderr[-2000:])
        else:
            built[name] = out
    sys.exit(0 if ok else 1)

main()
