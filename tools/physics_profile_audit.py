#!/usr/bin/env python3
"""Re-run the F6 profile matrix and retain test output, scope and input identities.

This reports existing acceptance criteria. It does not turn a catalogue rating or
legacy parity check into a historical trajectory measurement.
"""

import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj"
MATRIX = [
    ("Mercury Freedom 7", "mercury_redstone3_freedom7_1961", ["MercuryFreedom7Tests"], "historical mission"),
    ("Mercury Friendship 7", "mercury_atlas6_friendship7_1962", ["MercuryFriendship7Tests"], "historical mission"),
    ("Gemini 8", "gemini8_titan2_1966", ["Gemini8AscentScenarioTests", "Gemini8ReturnScenarioTests"], "historical mission"),
    ("Apollo 8", "apollo8_saturn5_as503_1968", ["SaturnVAscentScenarioTests.ClosedLoopUpperStagesReachTheParkingOrbit",
        "TranslunarInjectionScenarioTests"], "historical orbit and finite lunar transfer"),
    ("Apollo 11", "apollo11_saturn5_as506_1969", ["Apollo11DataTests.SpacecraftInterfacesPreserveThePublishedStructuralSpansAndOverlaps",
        "Apollo11DataTests.SpacecraftActuatorsUseThePublishedModuleReferencePlanes",
        "Apollo11DataTests.As506StackClosesPublishedIgnitionMassDimensionsAndThrust",
        "Apollo11DataTests.ServicePropulsionInventoryMatchesPostflightTankAndLineLoads",
        "ApolloCsmMassPropertiesHistoricalTests",
        "SaturnVAscentScenarioTests.As506ReachesParkingOrbitWithTheTranslunarPropellant",
        "SaturnVJetDampingHistoricalTests", "PropellantSloshTests.As506SicSloshFollowsTheAs501MeasuredFrequencies",
        "Apollo11TranspositionDockingScenarioTests", "Apollo11PoweredDescentScenarioTests",
        "LunarOrbitInsertionBurnTests", "Apollo11SpsBurnScenarioTests"],
        "source interfaces, mass history, frequencies, orbit, RCS docking, CSM table B-I mass properties, docked SPS capture, SP-238 non-circular circularization and LM descent; circular 60 n mi descent fixture"),
    ("Starship Flight 7", "starship_flight7_block2_2025", ["StarshipFlight7DataTests",
        "PhysicsParityIntegrationTests.Flight7ClosedLoopElevationProgramRemainsWithinParityTolerance"],
        "catalogue and controlled ascent parity; no measured trajectory residual"),
    ("Starship Flight 14", "starship_flight7_block2_2025", ["Flight14DescentHistoricalTests",
        "Flight14LandingBurnTests.LoadedPadToTerminalBurnRetainsItsPhysicalShipAndOriginalFuel"],
        "broadcast descent anchors and continuous pad-to-terminal-burn acceptance"),
    ("Falcon 9", "falcon9_block5_standard_2025", ["Falcon9Block5Tests"],
        "dated public ratings and engine acceptance; no mission-specific trajectory"),
    ("New Glenn", "newglenn_7x2_public_2026", ["NewGlennTests"],
        "advertised upgrade envelope and funded engine operation; not a flown mission reconstruction"),
]


def digest(paths):
    result = hashlib.sha256()
    for path in sorted(paths):
        result.update(str(path.relative_to(ROOT)).encode())
        result.update(b"\0")
        result.update(path.read_bytes())
    return result.hexdigest()


def identities():
    paths = [p for folder in ("ExosphereSimulation", "ExosphereSimulation.Tests", "data")
        for p in (ROOT / folder).rglob("*") if p.is_file()
        and p.suffix in (".cs", ".csproj", ".json") and not {"bin", "obj"}.intersection(p.parts)]
    assembly = ROOT / "ExosphereSimulation.Tests/bin/Debug/net8.0/ExosphereSimulation.Tests.dll"
    return {"sourceAndDataSha256": digest(paths), "testAssemblySha256": hashlib.sha256(assembly.read_bytes()).hexdigest()}


def tank_coverage(variant):
    vehicle = json.loads((ROOT / "data/vehicles" / (variant + ".json")).read_text())
    parts = {p["id"]: p for path in (ROOT / "data/parts").glob("*.json")
        if (p := json.loads(path.read_text())).get("id")}
    stores = []
    for part_id in vehicle["stackTopToBottom"]:
        part = parts[part_id]
        declared = {t["resource"] for t in part.get("propellant_tanks", [])}
        for field, resource in (("fuel_capacity_lf", "liquid_fuel"), ("fuel_capacity_ox", "oxidizer")):
            if part.get(field, 0) > 0:
                stores.append({"part": part_id, "resource": resource, "declaredSloshGeometry": resource in declared})
    return {"primarySource": vehicle["primarySource"], "liquidStores": stores}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--no-build", action="store_true", help="Reuse a current CI build; no source changes during the audit.")
    parser.add_argument("--verify-only", action="store_true", help="Re-evaluate preserved TRX and verify the input identities.")
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    identity_path = output / "inputs.json"
    if not args.verify_only:
        # A failed/interrupted run must never reuse an earlier successful TRX.
        # Remove only this tool's own artifacts before even attempting the build.
        for name in ("profiles.trx", "report.json", "test-exit-code.txt", "inputs.json", "console.log"):
            (output / name).unlink(missing_ok=True)
        if not args.no_build:
            subprocess.run(["dotnet", "build", str(PROJECT), "--nologo"], cwd=ROOT, check=True)
        before = identities()
        identity_path.write_text(json.dumps(before, indent=2) + "\n")
        selectors = sorted({s for _, _, entries, _ in MATRIX for s in entries})
        command = ["dotnet", "test", str(PROJECT), "--no-build", "--no-restore", "--nologo",
            "--filter", "|".join("FullyQualifiedName~" + s for s in selectors),
            "--logger", "trx;LogFileName=profiles.trx", "--results-directory", str(output)]
        with (output / "console.log").open("w") as log:
            result = subprocess.run(command, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT)
        (output / "test-exit-code.txt").write_text(str(result.returncode) + "\n")
    before = json.loads(identity_path.read_text())
    if before != identities():
        raise RuntimeError("Source, data or test assembly changed during/after this audit; rerun it.")
    namespace = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    tree = ET.parse(output / "profiles.trx")
    tests = [{"name": row.attrib["testName"], "outcome": row.attrib["outcome"],
        "duration": row.attrib.get("duration"), "output": row.findtext("t:Output/t:StdOut", "", namespace),
        "error": row.findtext("t:Output/t:ErrorInfo/t:Message", "", namespace)}
        for row in tree.findall(".//t:UnitTestResult", namespace)]
    profiles = []
    for name, variant, selectors, scope in MATRIX:
        matches = [test for test in tests if any(s in test["name"] for s in selectors)]
        missing = [s for s in selectors if not any(s in t["name"] for t in matches)]
        profiles.append({"name": name, "variant": variant, "validationScope": scope,
            "passed": bool(matches) and not missing and all(t["outcome"] == "Passed" for t in matches),
            "missingSelectors": missing, **tank_coverage(variant), "tests": matches})
    passed = all(p["passed"] for p in profiles) and int((output / "test-exit-code.txt").read_text()) == 0
    report = {"passed": passed, **before, "profiles": profiles}
    (output / "report.json").write_text(json.dumps(report, indent=2) + "\n")
    for profile in profiles:
        print(f'{profile["name"]}: {"PASS" if profile["passed"] else "FAIL"} ({len(profile["tests"])} tests); {profile["validationScope"]}')
    return 0 if passed else 1


if __name__ == "__main__":
    sys.exit(main())
