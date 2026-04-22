Native implementation lives in `NrdPlugin.cpp`.

Key decisions:

- D3D12-only for the current Unity SRP path
- `NRDIntegration::RecreateD3D12` / `DenoiseD3D12` is used instead of a custom low-level NRD dispatch path
- each camera gets its own NRD integration context to avoid history contamination across Scene/Game cameras
- resource states are requested through `IUnityGraphicsD3D12v8::RequestResourceState` before invoking NRD on the active command list
