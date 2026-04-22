using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using VoxelEngine.Render.NRD.Data;

namespace VoxelEngine.Render.NRD.Bridge
{
    internal static class NrdBridge
    {
        private const string NativeLibraryName = "NRDPlugin";

        private static bool _hasAttemptedInitialize;
        private static bool _isInitialized;
        private static int _denoiseEventId = 1;
        private static string _lastError = "NRD bridge not initialized.";

        public static int DenoiseEventId => _denoiseEventId;

        public static string LastError => _lastError;

        public static bool TryEnsureInitialized(out string error)
        {
            if (_isInitialized)
            {
                error = string.Empty;
                return true;
            }

            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D12)
            {
                error = _lastError = $"NRD currently requires D3D12, current API is {SystemInfo.graphicsDeviceType}.";
                return false;
            }

            RuntimePlatform platform = Application.platform;
            if (platform != RuntimePlatform.WindowsEditor && platform != RuntimePlatform.WindowsPlayer)
            {
                error = _lastError = $"NRD currently supports Windows only, current platform is {platform}.";
                return false;
            }

            if (_hasAttemptedInitialize)
            {
                error = _lastError;
                return false;
            }

            _hasAttemptedInitialize = true;
            try
            {
                _isInitialized = NrdPlugin_Initialize(IntPtr.Zero, (int)SystemInfo.graphicsDeviceType);
                if (!_isInitialized)
                {
                    _lastError = ReadNativeLastError("NRD plugin initialization failed.");
                }
                else
                {
                    _denoiseEventId = NrdPlugin_GetDenoiseEventId();
                }
            }
            catch (DllNotFoundException exception)
            {
                _lastError = $"NRD native plugin '{NativeLibraryName}' was not found: {exception.Message}";
            }
            catch (EntryPointNotFoundException exception)
            {
                _lastError = $"NRD native plugin API is incomplete: {exception.Message}";
            }
            catch (BadImageFormatException exception)
            {
                _lastError = $"NRD native plugin binary is invalid for the current process: {exception.Message}";
            }

            error = _lastError;
            return _isInitialized;
        }

        public static bool TryQueueFrame(in NrdSettings settings, in NrdFrameData frameData, out string error)
        {
            if (!TryEnsureInitialized(out error))
            {
                return false;
            }

            try
            {
                NrdSettings settingsCopy = settings;
                NrdFrameData frameCopy = frameData;
                NrdPlugin_SetSettings(ref settingsCopy);
                NrdPlugin_QueueFrame(ref frameCopy);
                error = string.Empty;
                return true;
            }
            catch (Exception exception) when (
                exception is DllNotFoundException ||
                exception is EntryPointNotFoundException ||
                exception is BadImageFormatException)
            {
                _isInitialized = false;
                _lastError = exception.Message;
                error = _lastError;
                return false;
            }
        }

        public static bool TryGetRenderEventFunc(out IntPtr renderEventFunc, out string error)
        {
            renderEventFunc = IntPtr.Zero;
            if (!TryEnsureInitialized(out error))
            {
                return false;
            }

            try
            {
                renderEventFunc = NrdPlugin_GetRenderEventFunc();
                if (renderEventFunc == IntPtr.Zero)
                {
                    error = _lastError = ReadNativeLastError("NRD render event function is null.");
                    return false;
                }

                error = string.Empty;
                return true;
            }
            catch (Exception exception) when (
                exception is DllNotFoundException ||
                exception is EntryPointNotFoundException ||
                exception is BadImageFormatException)
            {
                _isInitialized = false;
                _lastError = exception.Message;
                error = _lastError;
                return false;
            }
        }

        public static void NotifyResize(int width, int height)
        {
            if (!_isInitialized)
            {
                return;
            }

            try
            {
                NrdPlugin_OnResize(width, height);
            }
            catch (Exception exception) when (
                exception is DllNotFoundException ||
                exception is EntryPointNotFoundException ||
                exception is BadImageFormatException)
            {
                _isInitialized = false;
                _lastError = exception.Message;
            }
        }

        public static void Shutdown()
        {
            if (!_isInitialized)
            {
                _hasAttemptedInitialize = false;
                return;
            }

            try
            {
                NrdPlugin_Shutdown();
            }
            catch (Exception exception) when (
                exception is DllNotFoundException ||
                exception is EntryPointNotFoundException ||
                exception is BadImageFormatException)
            {
                _lastError = exception.Message;
            }
            finally
            {
                _isInitialized = false;
                _hasAttemptedInitialize = false;
            }
        }

        public static bool TryGetBackendActive(out bool isActive)
        {
            isActive = false;
            if (!_isInitialized)
            {
                return false;
            }

            try
            {
                isActive = NrdPlugin_IsBackendActive() != 0;
                return true;
            }
            catch (Exception exception) when (
                exception is DllNotFoundException ||
                exception is EntryPointNotFoundException ||
                exception is BadImageFormatException)
            {
                _isInitialized = false;
                _lastError = exception.Message;
                return false;
            }
        }

        private static string ReadNativeLastError(string fallback)
        {
            try
            {
                IntPtr errorPtr = NrdPlugin_GetLastError();
                return errorPtr == IntPtr.Zero
                    ? fallback
                    : Marshal.PtrToStringAnsi(errorPtr) ?? fallback;
            }
            catch
            {
                return fallback;
            }
        }

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern bool NrdPlugin_Initialize(IntPtr unityGraphicsDevice, int graphicsApi);

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void NrdPlugin_Shutdown();

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void NrdPlugin_OnResize(int width, int height);

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void NrdPlugin_SetSettings(ref NrdSettings settings);

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void NrdPlugin_QueueFrame(ref NrdFrameData frameData);

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr NrdPlugin_GetRenderEventFunc();

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int NrdPlugin_GetDenoiseEventId();

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int NrdPlugin_IsBackendActive();

        [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr NrdPlugin_GetLastError();
    }
}
