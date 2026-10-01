// Preserve physical foreground ordering despite scaled-space raster coverage.
uniform float backdrop_far_units = 420000.0;

float physical_backdrop_depth(vec3 physical_view, mat4 projection) {
    float z = max(-physical_view.z, 0.001);
    // Local scene geometry remains exact through 98% of the far range. Compress
    // only the distant tail, monotonically, so planets beyond Far remain ordered.
    float begin = backdrop_far_units * 0.98;
    float span = max(backdrop_far_units - begin, 1.0);
    float mapped = z <= begin ? z : begin + span * (1.0 - 1.0 / (1.0 + (z - begin) / span));
    vec4 clip = projection * vec4(physical_view * (mapped / z), 1.0);
    float ndc = clip.z / clip.w;
    return clamp((ndc - CLIP_SPACE_FAR) / (1.0 - CLIP_SPACE_FAR), 0.0, 1.0);
}
