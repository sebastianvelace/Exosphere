# Environment data pipeline (`envdata`)

Environment plan unit E0.2 (`docs/physics/environment_and_recalibration_plan.md`). Every
external dataset behind the simulated environment (time scales, ephemerides, Earth orientation,
gravity fields, winds, atmospheres) enters through `data/environment/manifest.json`:

- a **dataset** entry per source file: URL, publisher, title, licence, access date, size and
  SHA-256;
- a **product** entry per generated file under `data/environment/`: builder, input datasets,
  output path, output SHA-256 and the provenance fields (`status`, `variant`, `conditions`,
  `uncertainty`).

`data/provenance/environment_products.json` is generated from the manifest, so no product
reaches the simulation without a cited source.

## Commands

```bash
export EXOSPHERE_ENVDATA_CACHE=~/.cache/exosphere/envdata   # optional; this is the default
python3 tools/envdata/envdata.py fetch            # download every dataset, verify SHA-256
python3 tools/envdata/envdata.py build            # rebuild every product; fails if bytes differ
python3 tools/envdata/envdata.py build --pin ID   # rebuild one product and record its new hash
python3 tools/envdata/envdata.py verify           # offline contract, run by tools/contract_checks.sh
python3 tools/envdata/envdata.py provenance       # rewrite data/provenance/environment_products.json
```

Only `fetch` uses the network. A source whose download no longer matches its pinned SHA-256 is
an error: the change has to be reviewed and the manifest updated by hand. `verify` checks, with no
network, that every product matches its pinned hash, was built from the declared sources, decodes
cleanly, that no undeclared `.bin` sits in `data/environment/`, and that the provenance file is
current.

## Product format

`tools/envdata/envfile.py`: magic `EXOENV01`, a little-endian `u32` header length, a canonical
JSON header (product, format version, time scale, frame, columns, units, source hashes, payload
hash), then a row-major `float64` payload aligned to 8 bytes. Building the same inputs twice gives
the same bytes.

## Adding a product

1. Add the source to `datasets` (fetch it once to learn its SHA-256 and size).
2. Write `tools/envdata/products/<builder>.py` with `build(sources) -> (header, rows)`. Cross-check
   against an independent source when one exists (the TAI−UTC builder checks USNO against NAIF).
3. Add the product with its provenance fields, run `build --pin <id>`, then `verify`.
4. Run `python3 tools/provenance_register.py` if the status is not `published`.

## Products

| Product | Sources | Use |
| --- | --- | --- |
| `time/tai_utc.bin` | USNO `tai-utc.dat` (1961 onwards, with the 1961–1971 rate terms), cross-checked against NAIF `naif0012.tls` | UTC↔TAI for dated missions (plan E1) |
