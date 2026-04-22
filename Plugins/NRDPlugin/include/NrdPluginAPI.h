#pragma once

#if defined(_WIN32)
#  if defined(NRD_PLUGIN_EXPORTS)
#    define NRD_PLUGIN_API __declspec(dllexport)
#  else
#    define NRD_PLUGIN_API __declspec(dllimport)
#  endif
#else
#  define NRD_PLUGIN_API
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct NrdSettingsNative
{
    int width;
    int height;
    float denoisingRange;
    int maxAccumulatedFrameNum;
    int maxFastAccumulatedFrameNum;
    int historyFixFrameNum;
    float hitDistanceA;
    float hitDistanceB;
    float hitDistanceC;
    int enableValidation;
} NrdSettingsNative;

typedef struct NrdFrameDataNative
{
    void* noisyAmbientNormalizedHitDistance;
    void* motion;
    void* normalRoughness;
    void* viewZ;
    void* denoisedAmbientOutput;
    int cameraId;
    int width;
    int height;
    int frameIndex;
    float currentWorldToView[16];
    float previousWorldToView[16];
    float currentViewToClip[16];
    float previousViewToClip[16];
} NrdFrameDataNative;

NRD_PLUGIN_API int NrdPlugin_Initialize(void* unityGraphicsDevice, int graphicsApi);
NRD_PLUGIN_API void NrdPlugin_Shutdown(void);
NRD_PLUGIN_API void NrdPlugin_OnResize(int width, int height);
NRD_PLUGIN_API void NrdPlugin_SetSettings(const NrdSettingsNative* settings);
NRD_PLUGIN_API void NrdPlugin_QueueFrame(const NrdFrameDataNative* frameData);
NRD_PLUGIN_API void* NrdPlugin_GetRenderEventFunc(void);
NRD_PLUGIN_API int NrdPlugin_GetDenoiseEventId(void);
NRD_PLUGIN_API int NrdPlugin_IsBackendActive(void);
NRD_PLUGIN_API const char* NrdPlugin_GetLastError(void);

#ifdef __cplusplus
}
#endif
