# PLAN

This project is a voxel engine experiment evolving toward a complete runtime with
custom rendering, physics, and animation systems. The plan below records the
major workstreams and the intended order of development.

## Rendering

Rendering remains an active optimization area. The current priority is improving
image stability and perceived quality without losing the voxel look.

Focus areas:

- Continue refining RTAO quality, especially fixed STBN behavior, hit-distance
  variance reduction, AO quantization, minimum visibility control, and spatial
  denoising.
- Tune temporal anti-aliasing as a final image stabilizer, but avoid relying on
  TAA to hide unstable lighting estimators.
- Improve reflection quality and denoising while preserving sharp voxel edges.
- Keep debug views and in-game controls for fast iteration on AO, lighting,
  reflection, and denoising parameters.
- Profile render cost regularly, with special attention to ray count, spatial
  filter taps, render target formats, and RTAS update cost.

Near-term rendering tasks:

- Compare AO quantization, minimum visibility, and spiral spatial blur settings
  against Teardown-like fixed-step visual behavior.
- Decide which RTAO parameters should become production defaults and which
  should remain debug-only.
- Add documentation for the active render pipeline, including the current frame
  graph and resource ownership.

## Physics

The project needs a custom physics system designed for voxel data instead of
depending entirely on Unity's built-in collider workflow.

Goals:

- Represent voxel collision data efficiently at chunk and sub-chunk granularity.
- Support broad phase queries over voxel chunks and dynamic voxel objects.
- Support narrow phase queries against voxel occupancy, AABBs, and future
  simplified collision proxies.
- Provide deterministic-enough behavior for gameplay systems that need stable
  destruction, movement, and interaction.
- Keep physics data synchronized with voxel edits and chunk lifecycle events.

Initial physics milestones:

1. Define physics ownership boundaries: world chunks, dynamic voxel bodies, and
   transient query helpers.
2. Implement basic voxel collision queries: raycast, sphere overlap, AABB overlap,
   and swept AABB.
3. Build a broad phase over chunk bounds and dynamic body bounds.
4. Add rigid-body style integration for simple voxel bodies.
5. Add collision response suitable for character movement and debris.
6. Add profiling/debug visualization for physics bounds, contacts, and query cost.

Open design questions:

- Whether dynamic destructible bodies should store collision as dense voxels,
  compressed occupancy, AABB sets, or generated convex/simple proxies.
- How much determinism is required for gameplay and replay.
- Whether physics simulation should run at chunk resolution first, then refine
  local contacts only near impact points.

## Animation

The animation system should support voxel characters and voxel objects without
depending on a mesh-first pipeline.

Goals:

- Add voxel skeletal binding so voxel model data can be driven by bones.
- Support authored poses and clips for voxel assets.
- Support runtime blending between clips.
- Keep deformation compatible with voxel rendering data and future physics
  interaction.

Voxel skeletal binding milestones:

1. Define a voxel skeleton asset format: bones, hierarchy, bind pose, and local
   transforms.
2. Define voxel-to-bone binding data: single-bone assignment first, then optional
   weighted binding if needed.
3. Add editor/debug tooling to inspect bones and voxel binding.
4. Implement CPU-side pose evaluation for correctness.
5. Move deformation or transform application toward a render-friendly path once
   the data model is stable.

## Animation State Machine

The project needs its own animation state machine so animation control can be
designed around voxel gameplay requirements.

Goals:

- Provide a lightweight runtime state machine independent of Unity Animator.
- Support states, transitions, conditions, parameters, and blend durations.
- Support layered animation later if needed for upper-body/lower-body or tool
  interactions.
- Make the state machine easy to debug at runtime.

Milestones:

1. Define runtime data structures for states, clips, transitions, and parameters.
2. Implement basic state switching with transition conditions.
3. Add timed blends between two clips.
4. Add debug UI showing active state, transition progress, and parameter values.
5. Connect the state machine to voxel skeleton pose output.

## Suggested Order

1. Stabilize the current rendering path enough that visual regressions are easy
   to detect.
2. Start the custom physics data model and basic query API.
3. Implement minimal voxel skeletal binding and pose evaluation.
4. Build the custom animation state machine on top of the pose system.
5. Integrate physics, animation, and rendering around shared voxel asset/runtime
   data.

## Engineering Principles

- Prefer voxel-native data structures over mesh-first shortcuts when the system
  will become core engine infrastructure.
- Keep debug visualization available for every low-level system.
- Make each system testable in isolation before integrating it into gameplay.
- Record hard-earned implementation notes in `EXPERIENCE.md`.
