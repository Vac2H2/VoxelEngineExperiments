# TLAS BVH Self-Collision Demo

This demo lives in `Assets/Scenes/BVH.unity` and is driven by `TlasBvhDemoController`.

It generates moving AABB bodies, builds a Morton-sorted flat TLAS every frame, and compares broadphase body-pair generation modes:

- naive all-pairs
- body-vs-tree reference
- single-thread NodePair self-query
- parallel seed-pair NodePair self-query

The OnGUI panel exposes rebuild, seed randomization, query mode cycling, validation, rendering, and a basic benchmark sampler. Naive validation is intended for small body counts only.
