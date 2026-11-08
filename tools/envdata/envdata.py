#!/usr/bin/env python3
"""Environment data pipeline (environment plan E0.2).

Every external dataset the simulation's environment uses (time scales, ephemerides, Earth
orientation, gravity fields, winds, atmospheres) enters through data/environment/manifest.json:
each source with its URL, licence, access date and SHA-256, each generated product with its
builder, inputs and output SHA-256. The network is only needed to regenerate; CI verifies
offline that every product matches the manifest and carries provenance.

    python3 tools/envdata/envdata.py fetch [DATASET ...]        download into the cache, verify hashes
    python3 tools/envdata/envdata.py build [PRODUCT ...]        rebuild products, fail if bytes differ
    python3 tools/envdata/envdata.py build --pin PRODUCT        rebuild and record the new output hash
    python3 tools/envdata/envdata.py verify                     offline contract (used by CI)
    python3 tools/envdata/envdata.py provenance [--check]       write/check data/provenance records

The cache lives outside git: $EXOSPHERE_ENVDATA_CACHE or ~/.cache/exosphere/envdata.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib
import json
import os
import pathlib
import ssl
import sys
import urllib.request

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parent.parent
ENVIRONMENT = ROOT / "data" / "environment"
MANIFEST = ENVIRONMENT / "manifest.json"
PROVENANCE = ROOT / "data" / "provenance" / "environment_products.json"
sys.path.insert(0, str(HERE))

import envfile  # noqa: E402


def sha256(blob: bytes) -> str:
    return hashlib.sha256(blob).hexdigest()


def cache_dir() -> pathlib.Path:
    path = pathlib.Path(os.environ.get("EXOSPHERE_ENVDATA_CACHE",
                                       pathlib.Path.home() / ".cache" / "exosphere" / "envdata"))
    path.mkdir(parents=True, exist_ok=True)
    return path


def load_manifest() -> dict:
    return json.loads(MANIFEST.read_text(encoding="utf-8"))


def save_manifest(manifest: dict) -> None:
    MANIFEST.write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def datasets(manifest: dict) -> dict[str, dict]:
    return {d["id"]: d for d in manifest["datasets"]}


def products(manifest: dict) -> dict[str, dict]:
    return {p["id"]: p for p in manifest["products"]}


def pinned_content(dataset: dict, blob: bytes) -> bytes:
    """The bytes a product may depend on. A living series (``prefix_through``, e.g. the daily
    IERS C04 file) is pinned by the prefix ending with the line that starts with that marker,
    so a newer download with more rows still verifies and rebuilds the same product."""
    marker = dataset.get("prefix_through")
    if not marker:
        return blob
    lines = blob.splitlines(keepends=True)
    for i, line in enumerate(lines):
        if line.startswith(marker.encode("ascii")):
            return b"".join(lines[:i + 1])
    raise SystemExit(f"envdata: {dataset['id']} has no line starting with {marker!r}")


def check_source(dataset: dict, blob: bytes) -> bytes:
    content = pinned_content(dataset, blob)
    digest = sha256(content)
    if digest != dataset["sha256"]:
        what = "pinned prefix" if dataset.get("prefix_through") else "file"
        raise SystemExit(f"envdata: {dataset['id']} {what} has SHA-256 {digest}, manifest has "
                         f"{dataset['sha256']}; the source changed and must be re-reviewed")
    return content


def cached_source(dataset: dict) -> bytes:
    path = cache_dir() / dataset["id"] / dataset["file"]
    if not path.exists():
        raise SystemExit(f"envdata: {dataset['id']} is not cached; run `envdata.py fetch {dataset['id']}`")
    return check_source(dataset, path.read_bytes())


def fetch(names: list[str]) -> None:
    manifest = load_manifest()
    known = datasets(manifest)
    context = ssl.create_default_context(cafile=os.environ.get("SSL_CERT_FILE") or None)
    for name in names or list(known):
        dataset = known[name]
        target = cache_dir() / dataset["id"] / dataset["file"]
        target.parent.mkdir(parents=True, exist_ok=True)
        request = urllib.request.Request(dataset["url"], headers={"User-Agent": "exosphere-envdata/1"})
        with urllib.request.urlopen(request, context=context, timeout=600) as response:
            blob = response.read()
        if dataset.get("zip_member"):
            # Archives are unpacked to the declared member; the pin covers the member's bytes.
            import io
            import zipfile
            with zipfile.ZipFile(io.BytesIO(blob)) as archive:
                blob = archive.read(dataset["zip_member"])
        check_source(dataset, blob)
        target.write_bytes(blob)
        print(f"fetched {name}: {len(blob)} bytes, SHA-256 verified")


def build_bytes(manifest: dict, product: dict) -> bytes:
    known = datasets(manifest)
    sources = {name: cached_source(known[name]) for name in product["inputs"]}
    sources["__datasets__"] = known
    builder = importlib.import_module(f"products.{product['builder']}")
    header, rows = (builder.build_series(product["series"], sources) if "series" in product
                    else builder.build(sources))
    header["sources"] = {name: known[name]["sha256"] for name in product["inputs"]}
    return envfile.encode(header, rows)


def build(names: list[str], pin: bool) -> None:
    manifest = load_manifest()
    known = products(manifest)
    for name in names or list(known):
        product = known[name]
        blob = build_bytes(manifest, product)
        digest = sha256(blob)
        output = ENVIRONMENT / product["output"]
        if pin:
            output.parent.mkdir(parents=True, exist_ok=True)
            output.write_bytes(blob)
            product["sha256"] = digest
            product["bytes"] = len(blob)
            print(f"built and pinned {name}: {len(blob)} bytes, {digest}")
        elif digest != product.get("sha256"):
            raise SystemExit(f"envdata: rebuilding {name} gave {digest}, manifest pins "
                             f"{product.get('sha256')}; the build is not reproducible")
        else:
            output.parent.mkdir(parents=True, exist_ok=True)
            output.write_bytes(blob)
            print(f"rebuilt {name}: reproduces {digest}")
    if pin:
        save_manifest(manifest)
        write_provenance(manifest)


def provenance_document(manifest: dict) -> dict:
    known = datasets(manifest)
    records = []
    for product in manifest["products"]:
        p = product["provenance"]
        sources = "; ".join(
            f"{known[n]['publisher']}, {known[n]['title']}: {known[n]['url']} "
            + (f"(rows through {known[n]['prefix_through'].strip()}, prefix SHA-256 {known[n]['sha256']})"
               if known[n].get("prefix_through") else f"(SHA-256 {known[n]['sha256']})")
            for n in product["inputs"])
        records.append({
            "dataId": p["dataId"],
            "field": p["field"],
            "status": p["status"],
            "source": ("; ".join(x for x in (product.get("model", ""), sources) if x))
                      + f". Built by tools/envdata/products/{product['builder']}.py into "
                      f"data/environment/{product['output']} (SHA-256 {product['sha256']}).",
            "accessedDate": min([known[n]["accessed"] for n in product["inputs"]]
                                + ([product["accessed"]] if "accessed" in product else [])),
            "variant": p["variant"],
            "conditions": p["conditions"],
            "uncertainty": p["uncertainty"],
        })
    return {"manifestId": "environment-products", "records": records}


def write_provenance(manifest: dict) -> None:
    PROVENANCE.write_text(json.dumps(provenance_document(manifest), indent=2, ensure_ascii=False) + "\n",
                          encoding="utf-8")


def verify() -> int:
    manifest = load_manifest()
    known = datasets(manifest)
    errors = []
    for dataset in manifest["datasets"]:
        for key in ("id", "url", "file", "sha256", "accessed", "licence", "publisher", "title"):
            if not dataset.get(key):
                errors.append(f"dataset {dataset.get('id')} lacks {key}")
    for product in manifest["products"]:
        output = ENVIRONMENT / product["output"]
        if not output.exists():
            errors.append(f"{product['id']}: {output} is missing")
            continue
        blob = output.read_bytes()
        if sha256(blob) != product.get("sha256"):
            errors.append(f"{product['id']}: {output.name} does not match the manifest SHA-256")
            continue
        try:
            header, _ = envfile.decode(blob)
        except ValueError as error:
            errors.append(f"{product['id']}: {error}")
            continue
        expected = {name: known[name]["sha256"] for name in product["inputs"]}
        if header.get("sources") != expected:
            errors.append(f"{product['id']}: built from sources other than the manifest declares")
        if header.get("product") != product["id"]:
            errors.append(f"{product['id']}: header names product {header.get('product')}")
    listed = {(ENVIRONMENT / p["output"]).resolve() for p in manifest["products"]}
    for path in ENVIRONMENT.rglob("*.bin"):
        if path.resolve() not in listed:
            errors.append(f"{path.relative_to(ROOT)} is not declared in the manifest")
    if not PROVENANCE.exists() or json.loads(PROVENANCE.read_text(encoding="utf-8")) != provenance_document(manifest):
        errors.append(f"{PROVENANCE.relative_to(ROOT)} is stale; run `envdata.py provenance`")
    for error in errors:
        print(f"envdata verify: FAIL {error}", file=sys.stderr)
    if not errors:
        print(f"envdata verify: PASS ({len(manifest['products'])} products, {len(manifest['datasets'])} datasets)")
    return 1 if errors else 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)
    p_fetch = sub.add_parser("fetch")
    p_fetch.add_argument("names", nargs="*")
    p_build = sub.add_parser("build")
    p_build.add_argument("names", nargs="*")
    p_build.add_argument("--pin", action="store_true")
    sub.add_parser("verify")
    p_prov = sub.add_parser("provenance")
    p_prov.add_argument("--check", action="store_true")
    args = parser.parse_args()
    if args.command == "fetch":
        fetch(args.names)
    elif args.command == "build":
        build(args.names, args.pin)
    elif args.command == "verify":
        return verify()
    elif args.command == "provenance":
        if args.check:
            return verify()
        write_provenance(load_manifest())
    return 0


if __name__ == "__main__":
    sys.exit(main())
