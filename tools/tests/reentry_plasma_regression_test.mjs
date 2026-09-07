// Run with node; --baseline checks HEAD. No Godot process or visual harness is started.
// Execute the controller's actual rotation/cadence code with Godot's managed math.
import { readFileSync, writeFileSync, mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { resolve, dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const path = 'scripts/ReentryPlasmaController.cs';
const source = process.argv.includes('--baseline')
  ? readGitSource() : readFileSync(join(root, path), 'utf8');
function readGitSource() {
  const result = spawnSync('git', ['show', `HEAD:${path}`], { cwd: root, encoding: 'utf8' });
  if (result.status !== 0) throw new Error(result.stderr);
  return result.stdout;
}
function method(signature) {
  const start = source.indexOf(signature);
  if (start < 0) throw new Error(`Missing ${signature}`);
  let end = source.indexOf('{', start), depth = 1;
  for (++end; depth && end < source.length; ++end) {
    if (source[end] === '{') ++depth;
    if (source[end] === '}') --depth;
  }
  return source.slice(start, end);
}
const processBody = method('public override void _Process');
const gate = processBody.indexOf('if (_visualSampleTimer > 0.0) return;');
let failed = 0;
for (const operation of ['SyncToVesselFrame();', 'vessel.IsDestroyed', 'SetEffectsVisible(false);']) {
  const index = processBody.indexOf(operation);
  const ok = index >= 0 && gate > index;
  console.log(`${ok ? 'PASS' : 'FAIL'} every-frame path: ${operation}`);
  if (!ok) ++failed;
}
const cadence = source.match(/_visualSampleTimer -=[^]*?(?=\n\s*var (?:bridge|body) =)/)?.[0];
const period = source.match(/private const double VisualSamplePeriodSeconds = [^;]+;/)?.[0];
if (!cadence || !period) throw new Error('Cannot extract the production sampling block');
const work = mkdtempSync(join(tmpdir(), 'ripley-reentry-math-'));
const godot = join(root, '.godot/mono/temp/bin/Debug/GodotSharp.dll');
const xmlPath = godot.replaceAll('&', '&amp;').replaceAll('"', '&quot;');
writeFileSync(join(work, 'Regression.csproj'), `<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup><Reference Include="GodotSharp"><HintPath>${xmlPath}</HintPath></Reference></ItemGroup>
</Project>`);
writeFileSync(join(work, 'Program.cs'), `using System;
using Godot;
class Program {
    // Only the destination property is replaced; vectors and matrices are real Godot math.
    class TransformProbe { public Basis Basis = Basis.Identity; }
    ${method('private static void OrientYAxis').replace('Node3D node', 'TransformProbe node')}
    ${period}
    double _visualSampleTimer;
    int samples;
    void Sample(double delta) { ${cadence} ++samples; }
    static int Main() {
        int failures = 0;
        foreach (var direction in new[] { Vector3.Up, Vector3.Down, Vector3.Left,
            Vector3.Right, Vector3.Forward, new Vector3(1, -2, 3),
            new Vector3(0.0001f, -1, 0) }) {
            var node = new TransformProbe();
            OrientYAxis(node, direction);
            float error = (node.Basis * Vector3.Up - direction.Normalized()).Length();
            bool ok = error < 0.0011f && Math.Abs(node.Basis.Determinant() - 1) < 0.00001f;
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")} direction={direction} error={error}");
            if (!ok) ++failures;
        }
        foreach (int fps in new[] { 10, 20, 30, 60, 120, 144 }) {
            var probe = new Program();
            for (int frame = 0; frame < fps * 10; ++frame) probe.Sample(1.0 / fps);
            bool ok = Math.Abs(probe.samples - Math.Min(20, fps) * 10) <= 1;
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {fps} FPS: {probe.samples} samples / 10 s");
            if (!ok) ++failures;
        }
        var stalled = new Program();
        stalled.Sample(2.013);
        bool bounded = stalled.samples == 1 && stalled._visualSampleTimer > 0
            && stalled._visualSampleTimer <= VisualSamplePeriodSeconds;
        Console.WriteLine($"{(bounded ? "PASS" : "FAIL")} long frame: one sample, no catch-up burst");
        return failures + (bounded ? 0 : 1) == 0 ? 0 : 1;
    }
}`);
const run = spawnSync('dotnet', ['run', '--project', join(work, 'Regression.csproj'), '--verbosity', 'quiet'],
  { cwd: work, stdio: 'inherit', timeout: 120_000 });
console.log(`Managed regression artifacts: ${work}`);
if (run.error) throw run.error;
process.exitCode = failed === 0 && run.status === 0 ? 0 : 1;
