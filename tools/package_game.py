#!/usr/bin/env python3
"""Validate a self-contained desktop export, then make a checked sharing ZIP."""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import shutil
import struct
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[1]


def digest(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def require(path: Path) -> None:
    if not path.is_file() or path.stat().st_size == 0:
        raise ValueError(f"Missing or empty export dependency: {path}")


def windows_x64(path: Path) -> None:
    require(path)
    with path.open("rb") as stream:
        if stream.read(2) != b"MZ":
            raise ValueError(f"Not a Windows executable: {path}")
        stream.seek(0x3C)
        offset = struct.unpack("<I", stream.read(4))[0]
        stream.seek(offset)
        if stream.read(4) != b"PE\0\0" or struct.unpack("<H", stream.read(2))[0] != 0x8664:
            raise ValueError(f"Expected an AMD64 PE binary: {path}")


def validate(folder: Path, platform: str) -> dict:
    windows = platform == "windows"
    require(folder / ("Exosphere.exe" if windows else "Exosphere.x86_64"))
    require(folder / "Exosphere.pck")
    require(folder / "GODOT-LICENSE.txt")
    require(folder / "GODOT-THIRD-PARTY-NOTICES.json")
    runtime = folder / ("data_Exosphere_windows_x86_64" if windows else "data_Exosphere_linuxbsd_x86_64")
    for name in ["Exosphere.dll", "ExosphereSimulation.dll", "GodotSharp.dll", "Exosphere.deps.json", "Exosphere.runtimeconfig.json"]:
        require(runtime / name)
    for name in (["coreclr.dll", "hostfxr.dll", "hostpolicy.dll"] if windows else ["libcoreclr.so", "libhostfxr.so", "libhostpolicy.so"]):
        require(runtime / name)
        if windows:
            windows_x64(runtime / name)
    if windows:
        windows_x64(folder / "Exosphere.exe")
    config = json.loads((runtime / "Exosphere.runtimeconfig.json").read_text())
    frameworks = config["runtimeOptions"].get("includedFrameworks", [])
    framework = next((f for f in frameworks if f["name"] == "Microsoft.NETCore.App"), None)
    if framework is None:
        raise ValueError("Export is not self-contained: includedFrameworks is missing")
    source = ROOT / "data"
    expected = {p.relative_to(source).as_posix(): p for p in source.rglob("*")
                if p.is_file() and p.suffix != ".uid" and "__pycache__" not in p.parts and ".git" not in p.parts}
    actual = {p.relative_to(folder / "data").as_posix(): p for p in (folder / "data").rglob("*") if p.is_file()}
    if expected.keys() != actual.keys():
        raise ValueError("Export data files differ from the current source tree")
    for name, path in expected.items():
        if digest(path) != digest(actual[name]):
            raise ValueError(f"Stale or corrupted simulation data: {name}")
        if path.suffix == ".json":
            json.loads(path.read_text())
    return {"runtime_version": framework["version"], "data_files_verified": len(expected)}


def sharing_files(folder: Path, platform: str, runtime_version: str) -> None:
    shutil.copyfile(ROOT / "LICENSE", folder / "LICENSE.txt")
    runtime_package = Path.home() / ".nuget/packages" / f"microsoft.netcore.app.runtime.{'win' if platform == 'windows' else 'linux'}-x64" / runtime_version
    for name in ["LICENSE.TXT", "THIRD-PARTY-NOTICES.TXT"]:
        require(runtime_package / name)
        shutil.copyfile(runtime_package / name, folder / f"DOTNET-{name}")
    if platform != "windows":
        return
    # cd /d supports extraction on another drive; delayed expansion remains off
    # so a user's folder name containing ! is preserved.
    launches = {
        "Start Exosphere.bat": ("automatic", "--rendering-method forward_plus --rendering-driver vulkan", "normal"),
        "Start Compatibility.bat": ("integrated", "--rendering-method gl_compatibility --rendering-driver opengl3", "compatibility"),
    }
    for name, (preset, args, log_name) in launches.items():
        text = (
            '@echo off\nsetlocal DisableDelayedExpansion\ncd /d "%~dp0"\n'
            f'set "EXOSPHERE_GRAPHICS_PRESET={preset}"\n'
            'set "EXO_LOG_DIR=%LOCALAPPDATA%\\Exosphere\\logs"\n'
            'if not exist "%EXO_LOG_DIR%" mkdir "%EXO_LOG_DIR%"\n'
            f'"%~dp0Exosphere.exe" {args} --log-file "%EXO_LOG_DIR%\\{log_name}.log"\n'
            'if errorlevel 1 (\n'
            '  echo Exosphere could not start or exited with an error.\n'
            '  echo Logs: "%EXO_LOG_DIR%"\n'
            '  pause\n)\nendlocal\n'
        )
        (folder / name).write_bytes(text.replace("\n", "\r\n").encode("ascii"))
    (folder / "README-FIRST.txt").write_text(
        "EXOSPHERE - WINDOWS x64 TEST BUILD\n\n"
        "1. Extract the ENTIRE ZIP to a local folder. Do not run inside the ZIP.\n"
        "2. Open Start Exosphere.bat (normal Vulkan rendering).\n"
        "3. Choose Explore Flight 14, then Start Exploration.\n\n"
        "Godot and .NET are bundled; no editor, SDK, or separate runtime is needed.\n"
        "Keep Exosphere.exe, Exosphere.pck, data/ and data_Exosphere_windows_x86_64/ together.\n"
        "Directly opening Exosphere.exe also works; the launchers add graphics selection and logs.\n\n"
        "GRAPHICS\n"
        "The normal launcher uses Automatic: dedicated GPU -> Quality, integrated GPU -> Integrated GPU.\n"
        "The launcher cannot choose the physical GPU. On a hybrid laptop, assign Exosphere.exe\n"
        "to the high-performance GPU in Windows Settings > System > Display > Graphics.\n"
        "If normal rendering fails, close it and open Start Compatibility.bat.\n"
        "Compatibility uses OpenGL and a lighter profile; some effects differ from Vulkan.\n"
        "No benchmark or performance guarantee is provided for your hardware.\n\n"
        "CONTROLS\n"
        "Flight 14 exploration is automatic: right-drag/scroll = camera, comma/period = time speed.\n"
        "Pause, restart, menu and Super Heavy observation are available in the flight UI.\n"
        "Free flight: L = countdown, Z/X = throttle, G = ascent autopilot, Space = stage.\n"
        "W/S/A/D/Q/E = attitude, F5/F9 = save/load, V = vehicle assembly.\n\n"
        "TROUBLESHOOTING\n"
        "Send the GPU model, driver version, rendering mode and the last relevant log.\n"
        "Launcher logs: %LOCALAPPDATA%\\Exosphere\\logs\\normal.log or compatibility.log.\n"
        "Saves/settings: %APPDATA%\\Godot\\app_userdata\\Exosphere\\.\n"
        "The build is unsigned. Do not disable Windows or antivirus protection.\n\n"
        "Flight 14 is an engineering exploration, with estimated guidance and known limits,\n"
        "rather than an exact reconstructed mission. GPU testing on native Windows is still required.\n"
        "See BUILD-INFO.json for source revision and CHECKSUMS.sha256 for file integrity.\n",
        encoding="utf-8",
    )


def package(folder: Path, platform: str, archive: Path) -> None:
    checks = validate(folder, platform)
    sharing_files(folder, platform, checks["runtime_version"])
    revision = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    status = subprocess.check_output(["git", "status", "--porcelain"], cwd=ROOT, text=True)
    manifest = {"application": "Exosphere", "platform": f"{platform}-x86_64", "godot": "4.6.3.stable.mono",
                "source_revision": revision, "working_tree_dirty": bool(status.strip()),
                "built_utc": datetime.now(timezone.utc).isoformat(), "validation": checks,
                "native_windows_gpu_validation": "pending"}
    (folder / "BUILD-INFO.json").write_text(json.dumps(manifest, indent=2) + "\n")
    files = sorted(p for p in folder.rglob("*") if p.is_file() and p.name != "CHECKSUMS.sha256")
    (folder / "CHECKSUMS.sha256").write_text("".join(f"{digest(p)}  {p.relative_to(folder).as_posix()}\n" for p in files))
    archive.parent.mkdir(parents=True, exist_ok=True)
    temporary = archive.with_suffix(".zip.tmp")
    try:
        with zipfile.ZipFile(temporary, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=6) as zipped:
            for path in sorted(p for p in folder.rglob("*") if p.is_file()):
                zipped.write(path, (Path(folder.name) / path.relative_to(folder)).as_posix())
        with zipfile.ZipFile(temporary) as zipped:
            corrupt = zipped.testzip()
            if corrupt:
                raise ValueError(f"Corrupt ZIP entry: {corrupt}")
            for path in files:
                entry = (Path(folder.name) / path.relative_to(folder)).as_posix()
                if hashlib.sha256(zipped.read(entry)).hexdigest() != digest(path):
                    raise ValueError(f"ZIP payload mismatch: {entry}")
        temporary.replace(archive)
    finally:
        temporary.unlink(missing_ok=True)
    archive.with_suffix(".zip.sha256").write_text(f"{digest(archive)}  {archive.name}\n")
    print(f"PACKAGE_OK archive={archive} size={archive.stat().st_size} revision={revision} data={checks['data_files_verified']} self_contained=true crc=true sha256=true")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("folder", type=Path)
    parser.add_argument("--platform", required=True, choices=["windows", "linux"])
    parser.add_argument("--archive", required=True, type=Path)
    args = parser.parse_args()
    package(args.folder, args.platform, args.archive)
