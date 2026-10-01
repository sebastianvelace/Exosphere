# Coastal cloud shape, lighting and foreground transport

Status: foreground transport implemented; final cloud appearance and Flight 14
reference acceptance remain open.

## Cloud changes

The former elevated cloud field only composed onto sky and Earth rays. Opaque
vehicle geometry therefore remained sharp inside a cloud. A `MaterialOverlay`
now integrates the shared cloud field from the camera to each actual hull fragment.
The underlying opaque depth pass remains intact. Transparent Raptor plumes receive
only foreground transmission, so they fade against the cloud source already in
the background. Physics, atmospheric density, heating and forces are unchanged.

Coastal cells now have flat condensation bases, overlapping rounded crowns,
variable height, clear geographic gaps and a 1.2–2.4 km visual envelope. The 5 km
cell spacing and optical-density multiplier are explicitly appearance calibration,
not recovered weather measurements. Nearby sunlit faces use ten concentrated solar
samples; sky view rays use at most 96 local steps with conservative empty-space
bounds. The global field retains its existing sample budget. The former diffuse cloud closure increased brightness as direct transmission
fell, flattening the illuminated/shaded contrast. The bounded replacement gives
lit crowns more diffuse illumination and keeps a smaller interior floor.
Ground and globe use
a bounded daytime diffuse closure so cloud shadows do not remove all skylight.
These are finite transport approximations, not a meteorological droplet solver.

`--cloud-traverse` is a paused optical fixture below, inside and above the local
cloud layer. Its A/B pair uses the same camera, time and cell, and toggles only
foreground composition. `validate_cloud_occlusion.py` measures the contrast across
the projected hull and rejects missing, moved or unoccluded comparison frames.
A copied unobscured frame was rejected by the negative control. Fixture placement
is not flown ascent or trajectory evidence.

Final crown/light runs passed all five paused cases on Compatibility and Forward+
at 640×360. The projected-hull contrast reduction was 51.3% and 84.8%, respectively.
These display-space measurements are not a cross-renderer radiometric equivalence
claim. Both sets were inspected above, below and inside the cloud. The unchanged
unobscured frame negative control rejects false occlusion acceptance. The final
shape shows overlapping crowns and directional shadowing, but remains procedural.
Local captures live under ignored `exports/cloud-occlusion-review/light-compat/`
and `light-forward/`; compare them with the supplied/reference video frames.
The black water in the below-cloud fixture and the coarse distant morphology
remain appearance limitations. A continuous powered traverse and target-camera
comparison remain separate from this paused geometry test.
Hardware-GPU performance, cloud microstructure and camera/exposure matching remain
open. A green capture marker alone is not visual acceptance.


Verification: the final `tools/ci_check.sh` passed with both C# builds at zero
warnings/errors, 902 xUnit tests and flight/menu/construction smoke checks.
The sky-budget and jitter guards retain the global 24/five-sample constraints
and explicitly check the new bounded local path. Temporary harness source and
autoload entries were removed. The reference extractor also passed three tests.

Exploratory final software-renderer frame medians were 735.5 ms (Compatibility)
and 967 ms (Forward+) at 640×360 over 160 frames. These varied-camera, paused
fixtures are not a controlled performance comparison or hardware-GPU acceptance.
