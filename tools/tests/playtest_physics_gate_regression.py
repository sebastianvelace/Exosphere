#!/usr/bin/env python3
"""Exercise production playtest gates against real orbit states and distinct clocks."""
from pathlib import Path
import re
import subprocess
import tempfile
import html

root = Path(__file__).resolve().parents[2]
source = (root / "tools/visual_playtest.sh").read_text()
signature = "private static bool IsAtmosphericDeorbit("
start = source.index(signature)
brace = source.index("{", start)
depth = 1
end = brace + 1
while depth:
    depth += (source[end] == "{") - (source[end] == "}")
    end += 1
method = source[start:end]
ascent = re.search(r"universe.CurrentTime - _ascentStartedSimulationTime > AscentFallbackSec", source)
entry = re.search(r"universe.CurrentTime - _deorbitStartedSimulationTime > 720\.0", source)
assert ascent and entry, "Mission deadlines must use committed simulation time relative to their own start"
work = Path(tempfile.mkdtemp(prefix="exo-entry-gates-"))
project = root / "ExosphereSimulation/ExosphereSimulation.csproj"
planner = root / "scripts/ManeuverPlanner.cs"
# ManeuverPlanner only uses Godot as an unused namespace import. Link its production
# source verbatim and provide that namespace; no display or Godot runtime is needed.
(work / "GateTests.csproj").write_text(f"""<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><Nullable>enable</Nullable></PropertyGroup>
<ItemGroup><ProjectReference Include="{html.escape(str(project), quote=True)}" />
<Compile Include="{html.escape(str(planner), quote=True)}" Link="ManeuverPlanner.cs" /></ItemGroup>
</Project>""")
program = r"""using System;
using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Exosphere.Game;
namespace Godot { }
class Program {
METHOD
static void Require(bool value, string label) {
    if (!value) throw new Exception(label);
    Console.WriteLine("PASS " + label);
}
static int Main() {
    var universe = Universe.LoadFromDataDirectory(DATA);
    var earth = universe.GetBody("earth")!;
    foreach (double altitude in new[] { 150000.0, 400000.0 }) {
        var planner = new ManeuverPlanner();
        double radius = earth.Radius + altitude;
        planner.SetOrbit(Vector3d.Right * radius,
            Vector3d.Forward * Math.Sqrt(earth.GM / radius), earth.GM);
        Require(planner.PlanDeorbit(earth), "planner creates deorbit");
        Require(planner.IsDeorbitNode, "small and large deorbit presets have explicit intent");
        Require(IsAtmosphericDeorbit(planner, earth), $"{altitude} m orbit enters atmosphere");
        if (altitude == 150000.0)
            Require(planner.DeltaVMagnitude < 50.0, "valid low-orbit burn needs less than 50 m/s");
        planner.DvPrograde = -1.0;
        Require(!IsAtmosphericDeorbit(planner, earth), "vacuum periapsis is rejected");
        planner.DvPrograde = 100.0;
        Require(!planner.IsDeorbitNode, "prograde edit cancels deorbit classification");
        Require(!IsAtmosphericDeorbit(planner, earth), "prograde burn is rejected");
        planner.DvPrograde = -5000.0;
        Require(!IsAtmosphericDeorbit(planner, earth), "surface-intersecting trajectory is rejected");
        planner.DvPrograde = double.NaN;
        Require(!IsAtmosphericDeorbit(planner, earth), "nonfinite burn is rejected");
        planner.PlanDeorbit(earth);
        planner.CreateNodeAt(1.0);
        Require(!planner.IsDeorbitNode, "fresh manual node cannot inherit stale deorbit intent");
        planner.ClearNode();
        Require(!IsAtmosphericDeorbit(planner, earth), "missing node is rejected");
    }
    foreach (double epoch in new[] { 0.0, 1000000.0 }) {
        foreach (double wall in new[] { 100.0, 720.0, 3600.0 }) {
            double sim = epoch + 136.0;
            Require(!AscentExpired(sim, epoch), $"progressing ascent remains valid at wall={wall}");
            Require(!EntryExpired(sim, epoch), "entry deadline starts at deorbit arming");
        }
        Require(AscentExpired(epoch + 721.0, epoch), "ascent deadline still expires");
        Require(EntryExpired(epoch + 721.0, epoch), "entry deadline still expires");
    }
    return 0;
}
static bool AscentExpired(double now, double start) => ASCENT;
static bool EntryExpired(double now, double start) => ENTRY;
}
"""
import json
program = program.replace("METHOD", method).replace("DATA", json.dumps(str(root / "data")))
program = program.replace("ASCENT", ascent.group().replace("universe.CurrentTime", "now").replace("_ascentStartedSimulationTime", "start").replace("AscentFallbackSec", "720.0"))
program = program.replace("ENTRY", entry.group().replace("universe.CurrentTime", "now").replace("_deorbitStartedSimulationTime", "start"))
(work / "Program.cs").write_text(program)
(work / "NuGet.Config").write_text('<configuration><packageSources><clear /></packageSources></configuration>')
subprocess.run(["dotnet", "run", "--project", str(work / "GateTests.csproj"), "--verbosity", "quiet"], check=True, cwd=work)
