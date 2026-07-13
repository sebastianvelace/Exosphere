# Starbase launch campus V2

## Scope

This pass extends the orbital launch site beyond the OLM/tower/tank-farm trio. It
models the nearby launch-support campus as a coherent operational site while keeping
the simulation honest about features whose exact footprints are not public.

The coordinate convention used by the renderer is east `+X`, south `+Z`, with all
dimensions authored in metres and converted once by `LaunchPadController.U`.

## Added operational areas

`LaunchSupportCampusSpec.StarbasePostDeluge` is the testable source of truth for ten
low-rise facilities:

- two pad-support buildings and an emergency-response building;
- the Highway 4 security gate;
- an electrical/power building and a deluge pump house;
- desalination/water treatment;
- LNG pretreatment and liquefaction buildings;
- a tank-farm control building.

The scene also adds personnel parking, an electrical substation with transformer
banks, process-water vessels, separate west/east fill pads, and a bermed retention
pond. Far-away facilities such as Stargate, the production campus and the solar farm
are intentionally excluded: placing them beside the OLM would make the launch area
look busier but geographically less accurate.

## Launch-platform improvements

- The OLIT now has broad dark service cladding on its back and side faces, separated
  into vertical zones so its lattice structure remains visible.
- The obsolete oversized water-tower representation was replaced by a compact header
  tank on a short stand.
- The deluge route is made from endpoint-connected cylindrical segments: tank outlet,
  pump inlet, distribution manifold and water-cooled plate feed.
- The previous duplicate blockhouse was removed; those functions now live in the
  explicit campus specification.
- The west fill pad was widened so the personnel parking area no longer floats beyond
  its prepared ground.

## Validation and fidelity boundaries

Automated tests enforce finite positive dimensions, a strict 30 ft low-rise height
envelope, minimum OLM clearance, no building-to-building footprint overlap and the
presence of the essential operational categories.

The OLM, OLIT and vehicle-interface dimensions continue to come from
`LaunchComplexSpec`. Exact support-building footprints are layout estimates informed
by publicly identified site functions; they are not claimed as survey-grade replicas.
This distinction prevents uncertain geometry from silently becoming physics data.

## Next fidelity targets

The next useful visual pass is signage, fencing, pipe-rack detail, road drainage and
LOD/instancing for repeated campus equipment. Exact placement should only be promoted
into the specification when supported by dated aerial or regulatory evidence.
