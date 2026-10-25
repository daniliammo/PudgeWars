# Compiles the project's C# without Unity (the editor may stay open): copies of Unity's generated .csproj files with
# absolute paths and a glob for our own folders (new files Unity has not listed yet), built by dotnet into <out dir>.
# Usage: python3 Tools/Check/compile.py <out dir>   (out dir outside the project, e.g. a scratch folder)
# Prints only errors and the result; the Mirror weaver does not run, so this checks C# only.
import glob, os, re, subprocess, sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
OWN = {'PudgeWars.Gameplay': 'Assets/Scripts/Gameplay', 'PudgeWars.Net': 'Assets/Scripts/Net',
       'Assembly-CSharp-Editor': 'Assets/Editor', 'PudgeWars.Tests.PlayMode': 'Assets/Tests/PlayMode'}


def main():
    out = os.path.abspath(sys.argv[1])
    os.makedirs(out, exist_ok=True)
    for path in glob.glob(os.path.join(ROOT, '*.csproj')):
        name = os.path.basename(path)[:-7]
        s = open(path, encoding='utf-8-sig').read()
        s = s.replace('<BaseIntermediateOutputPath>Temp/obj/', f'<BaseIntermediateOutputPath>{out}/obj/')
        s = s.replace('<OutputPath>Temp/bin/Debug/</OutputPath>', f'<OutputPath>{out}/bin/</OutputPath>')
        if name in OWN:
            d = OWN[name]
            s = re.sub(r'\s*<Compile Include="' + re.escape(d) + r'/[^"]*" />', '', s)
            s = s.replace('</Project>', f'<ItemGroup><Compile Include="{ROOT}/{d}/**/*.cs" /></ItemGroup></Project>')
        s = s.replace('<HintPath>Library/', f'<HintPath>{ROOT}/Library/').replace('<HintPath>Assets/', f'<HintPath>{ROOT}/Assets/')
        s = re.sub(r'Include="(Assets|Packages|Library)/', lambda m: f'Include="{ROOT}/{m.group(1)}/', s)
        open(os.path.join(out, name + '.csproj'), 'w', encoding='utf-8').write(s)
    ok = True
    for project in ('Assembly-CSharp-Editor', 'PudgeWars.Tests.PlayMode'):
        result = subprocess.run(['dotnet', 'build', os.path.join(out, project + '.csproj'), '-nologo', '-v', 'q'],
                                capture_output=True, text=True)
        errors = sorted({l.strip() for l in result.stdout.splitlines() if ' error ' in l})
        print(project, 'OK' if result.returncode == 0 else 'FAILED')
        for e in errors: print('  ', e)
        ok &= result.returncode == 0
    sys.exit(0 if ok else 1)


main()
