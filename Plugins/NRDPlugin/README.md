# NRDPlugin

This folder contains the Windows x64 Unity native plugin that runs real
`NRDIntegration` / `NRI` backed `REBLUR_DIFFUSE_OCCLUSION` on the render thread.

Responsibilities:

- own per-camera NRD integration contexts and history
- receive per-frame settings and resource handles from `NrdBridge`
- execute `NewFrame / SetCommonSettings / SetDenoiserSettings / Denoise` on Unity's D3D12 render thread
- write denoised ambient AO into the Unity-owned output texture
- expose backend state and last-error diagnostics back to C#

Build notes:

- the plugin is built through `Plugins/NRDPlugin/CMakeLists.txt`
- official vendored sources live in `ThirdParty/NRD`
- the canonical built runtime binary is `Plugins/x86_64/NRDPlugin.dll`
- post-build, the same DLL is mirrored to `Assets/Plugins/x86_64/NRDPlugin.dll` so Unity can import and load it in-editor
