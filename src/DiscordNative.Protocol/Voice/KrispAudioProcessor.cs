using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace DiscordNative.Protocol.Voice
{
    /// <summary>
    /// Processes 48kHz 16-bit mono PCM voice frames (960 samples = 20ms) through Discord's official
    /// native Krisp Neural Noise Cancellation engine (discord_krisp.node + .kef neural models),
    /// with an integrated Biquad High-Pass Filter (80Hz rumble removal) and adaptive spectral gate fallback.
    /// </summary>
    public sealed class KrispAudioProcessor : IDisposable
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryW(string lpLibFileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualProtect(IntPtr lpAddress, UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int KrispInitDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte KrispSetModelDelegate([MarshalAs(UnmanagedType.LPStr)] string modelName);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr KrispSetupDelegate(int sampleRate, int frameDurationMs, byte useBvc);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int KrispProcessDelegate(IntPtr session, IntPtr inSamples, ulong inLen, IntPtr outSamples, ulong outLen);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int KrispResetDelegate(IntPtr session);

        private static readonly object _staticSync = new();
        private static IntPtr _krispLib = IntPtr.Zero;
        private static bool _krispInitialized;
        private static bool _krispLoadAttempted;
        private static string _loadedModelInfo = "DSP intégré";

        private static KrispSetModelDelegate? _fnSetModel;
        private static KrispSetupDelegate? _fnSetup;
        private static KrispProcessDelegate? _fnProcess;
        private static KrispResetDelegate? _fnReset;

        private readonly object _sessionLock = new();
        private IntPtr _session = IntPtr.Zero;
        private readonly short[] _outBuffer = new short[960];
        private bool _disposed;

        // High-pass biquad filter state (80 Hz cutoff @ 48000 Hz) + fallback adaptive noise gate
        private double _hpX1, _hpX2, _hpY1, _hpY2;
        private double _noiseFloorRms = 120.0;
        private double _gateGain = 1.0;

        public static bool IsNativeKrispLoaded => _krispInitialized;
        public static string LoadedEngineDescription => _loadedModelInfo;

        public KrispAudioProcessor()
        {
            EnsureNativeKrispInitialized();
            CreateSessionIfPossible();
        }

        public static string? FindKrispDirectory()
        {
            try
            {
                // 1. Check bundled krisp folder next to executable
                string baseDir = AppContext.BaseDirectory;
                string bundledDir = Path.Combine(baseDir, "krisp");
                if (File.Exists(Path.Combine(bundledDir, "discord_krisp.node")))
                    return bundledDir;

                if (File.Exists(Path.Combine(baseDir, "discord_krisp.node")))
                    return baseDir;

                // 2. Check user's official Discord installation (Stable, PTB, Canary, Development)
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string[] discordFlavors = { "Discord", "DiscordPTB", "DiscordCanary", "DiscordDevelopment" };

                foreach (var flavor in discordFlavors)
                {
                    string flavorRoot = Path.Combine(localAppData, flavor);
                    if (!Directory.Exists(flavorRoot))
                        continue;

                    var appDirs = Directory.GetDirectories(flavorRoot, "app-*")
                        .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase);

                    foreach (var appDir in appDirs)
                    {
                        string modulesDir = Path.Combine(appDir, "modules");
                        if (!Directory.Exists(modulesDir))
                            continue;

                        var krispModDirs = Directory.GetDirectories(modulesDir, "discord_krisp*")
                            .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase);

                        foreach (var modDir in krispModDirs)
                        {
                            string innerDir = Path.Combine(modDir, "discord_krisp");
                            if (File.Exists(Path.Combine(innerDir, "discord_krisp.node")))
                                return innerDir;

                            if (File.Exists(Path.Combine(modDir, "discord_krisp.node")))
                                return modDir;
                        }
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        private static void EnsureNativeKrispInitialized()
        {
            lock (_staticSync)
            {
                if (_krispLoadAttempted)
                    return;
                _krispLoadAttempted = true;

                try
                {
                    string? krispDir = FindKrispDirectory();
                    if (string.IsNullOrEmpty(krispDir))
                        return;

                    string dllPath = Path.Combine(krispDir, "discord_krisp.node");
                    if (!File.Exists(dllPath))
                        return;

                    _krispLib = LoadLibraryW(dllPath);
                    if (_krispLib == IntPtr.Zero)
                        return;

                    IntPtr pInit = GetProcAddress(_krispLib, "KrispInitializeExternal");
                    IntPtr pSetModel = GetProcAddress(_krispLib, "KrispNCSetModel");
                    IntPtr pSetup = GetProcAddress(_krispLib, "KrispNCSetup");
                    IntPtr pProcess = GetProcAddress(_krispLib, "KrispNCProcess");
                    IntPtr pReset = GetProcAddress(_krispLib, "KrispNCReset");

                    if (pInit == IntPtr.Zero || pSetModel == IntPtr.Zero || pSetup == IntPtr.Zero || pProcess == IntPtr.Zero || pReset == IntPtr.Zero)
                        return;

                    // Patch the WinVerifyTrust caller check inside DoKrispInitialize so Discord's official
                    // Krisp SDK initializes inside Diswin 100% natively and for free.
                    PatchSignatureCheckInMemory(_krispLib, pInit);

                    var fnInit = Marshal.GetDelegateForFunctionPointer<KrispInitDelegate>(pInit);
                    _fnSetModel = Marshal.GetDelegateForFunctionPointer<KrispSetModelDelegate>(pSetModel);
                    _fnSetup = Marshal.GetDelegateForFunctionPointer<KrispSetupDelegate>(pSetup);
                    _fnProcess = Marshal.GetDelegateForFunctionPointer<KrispProcessDelegate>(pProcess);
                    _fnReset = Marshal.GetDelegateForFunctionPointer<KrispResetDelegate>(pReset);

                    int initResult = fnInit();
                    if (initResult == 0)
                    {
                        // Prefer full_NC (krisp-nc-o-med-v7.kef) or c6_BVC (krisp-bvc-o-pro-v3.kef)
                        byte modelOk = _fnSetModel("full_NC");
                        if (modelOk != 0)
                        {
                            _loadedModelInfo = "Krisp Neural NC Officiel (full_NC • krisp-nc-o-med-v7.kef)";
                        }
                        else if (_fnSetModel("c6_BVC") != 0)
                        {
                            _loadedModelInfo = "Krisp Neural BVC Pro (c6_BVC • krisp-bvc-o-pro-v3.kef)";
                        }
                        else
                        {
                            _loadedModelInfo = "Krisp Neural SDK Officiel";
                        }

                        _krispInitialized = true;
                    }
                }
                catch
                {
                    _krispInitialized = false;
                }
            }
        }

        private static void PatchSignatureCheckInMemory(IntPtr libBase, IntPtr pInit)
        {
            try
            {
                // Scan DoKrispInitialize (within 512 bytes of KrispInitializeExternal) for the pattern:
                // 48 89 F1 (mov rcx, rsi)
                // E8 xx xx xx xx (call VerifyApplicationSignedByDiscord)
                // 84 C0 (test al, al)
                // 0F 84 xx xx 00 00 (je FailNotSigned)
                for (int offset = 0; offset < 450; offset++)
                {
                    IntPtr cur = IntPtr.Add(pInit, offset);
                    byte b0 = Marshal.ReadByte(cur, 0);
                    byte b1 = Marshal.ReadByte(cur, 1);
                    byte b2 = Marshal.ReadByte(cur, 2);
                    byte b3 = Marshal.ReadByte(cur, 3);
                    byte b8 = Marshal.ReadByte(cur, 8);
                    byte b9 = Marshal.ReadByte(cur, 9);

                    if (b0 == 0x48 && b1 == 0x89 && b2 == 0xF1 && b3 == 0xE8 && b8 == 0x84 && b9 == 0xC0)
                    {
                        int rel32 = Marshal.ReadInt32(cur, 4);
                        IntPtr targetFn = IntPtr.Add(cur, 8 + rel32);

                        if (VirtualProtect(targetFn, (UIntPtr)16, 0x40 /* PAGE_EXECUTE_READWRITE */, out uint oldProt))
                        {
                            Marshal.WriteByte(targetFn, 0, 0xB0); // mov al, 1
                            Marshal.WriteByte(targetFn, 1, 0x01);
                            Marshal.WriteByte(targetFn, 2, 0xC3); // ret
                            VirtualProtect(targetFn, (UIntPtr)16, oldProt, out _);
                            return;
                        }
                    }
                }

                // Fallback direct RVA 0x51840 if pattern differed
                IntPtr fallbackFn = IntPtr.Add(libBase, 0x51840);
                if (VirtualProtect(fallbackFn, (UIntPtr)16, 0x40, out uint oldProt2))
                {
                    Marshal.WriteByte(fallbackFn, 0, 0xB0);
                    Marshal.WriteByte(fallbackFn, 1, 0x01);
                    Marshal.WriteByte(fallbackFn, 2, 0xC3);
                    VirtualProtect(fallbackFn, (UIntPtr)16, oldProt2, out _);
                }
            }
            catch
            {
            }
        }

        private void CreateSessionIfPossible()
        {
            if (!_krispInitialized || _fnSetup == null)
                return;

            lock (_sessionLock)
            {
                if (_session == IntPtr.Zero)
                {
                    try
                    {
                        // 48000 Hz, 20 ms frame duration (960 samples), standard NC
                        _session = _fnSetup(48000, 20, 0);
                    }
                    catch
                    {
                        _session = IntPtr.Zero;
                    }
                }
            }
        }

        /// <summary>
        /// Processes a 20ms frame of 960 mono 16-bit PCM samples at 48kHz in-place.
        /// </summary>
        public void ProcessFrame48kMono(short[] pcm960)
        {
            if (pcm960 == null || pcm960.Length < 960 || _disposed)
                return;

            // Step 1: Try official Discord Krisp Neural Noise Cancellation (discord_krisp.node)
            if (_krispInitialized && _fnProcess != null)
            {
                lock (_sessionLock)
                {
                    if (_session == IntPtr.Zero && _fnSetup != null)
                    {
                        try { _session = _fnSetup(48000, 20, 0); } catch { }
                    }

                    if (_session != IntPtr.Zero)
                    {
                        GCHandle hIn = GCHandle.Alloc(pcm960, GCHandleType.Pinned);
                        GCHandle hOut = GCHandle.Alloc(_outBuffer, GCHandleType.Pinned);
                        try
                        {
                            int res = _fnProcess(_session, hIn.AddrOfPinnedObject(), 960UL, hOut.AddrOfPinnedObject(), 960UL);
                            if (res == 0)
                            {
                                Array.Copy(_outBuffer, 0, pcm960, 0, 960);
                                return;
                            }
                        }
                        catch
                        {
                        }
                        finally
                        {
                            hIn.Free();
                            hOut.Free();
                        }
                    }
                }
            }

            // Step 2: Built-in C# DSP Fallback (80Hz 2nd-order Butterworth High-Pass + Smooth Expander/Noise Suppressor)
            ApplyFallbackDsp(pcm960);
        }

        private void ApplyFallbackDsp(short[] pcm960)
        {
            // 2nd-order Butterworth High-Pass Filter at fc = 85 Hz, fs = 48000 Hz
            // Removes low-frequency rumble, desk vibrations, and AC hum
            const double b0 = 0.99217670;
            const double b1 = -1.98435340;
            const double b2 = 0.99217670;
            const double a1 = -1.98429215;
            const double a2 = 0.98441465;

            double sumSq = 0;
            for (int i = 0; i < 960; i++)
            {
                double x0 = pcm960[i];
                double y0 = b0 * x0 + b1 * _hpX1 + b2 * _hpX2 - a1 * _hpY1 - a2 * _hpY2;
                _hpX2 = _hpX1;
                _hpX1 = x0;
                _hpY2 = _hpY1;
                _hpY1 = y0;

                double clamped = Math.Clamp(y0, short.MinValue, short.MaxValue);
                pcm960[i] = (short)clamped;
                sumSq += clamped * clamped;
            }

            double frameRms = Math.Sqrt(sumSq / 960.0);

            // Adaptive noise floor tracker
            if (frameRms < _noiseFloorRms)
                _noiseFloorRms = _noiseFloorRms * 0.92 + frameRms * 0.08;
            else if (frameRms < _noiseFloorRms * 1.8)
                _noiseFloorRms = _noiseFloorRms * 0.995 + frameRms * 0.005;

            _noiseFloorRms = Math.Clamp(_noiseFloorRms, 45.0, 480.0);

            double threshold = _noiseFloorRms * 2.1;
            double targetGain = frameRms >= threshold
                ? 1.0
                : Math.Pow(Math.Clamp(frameRms / Math.Max(1.0, threshold), 0.0, 1.0), 2.2);

            for (int i = 0; i < 960; i++)
            {
                // Fast attack (0.015), smooth release (0.0015) per sample
                double coeff = targetGain > _gateGain ? 0.015 : 0.0015;
                _gateGain += (targetGain - _gateGain) * coeff;
                pcm960[i] = (short)Math.Clamp(pcm960[i] * _gateGain, short.MinValue, short.MaxValue);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            lock (_sessionLock)
            {
                if (_session != IntPtr.Zero && _fnReset != null)
                {
                    try { _fnReset(_session); } catch { }
                    _session = IntPtr.Zero;
                }
            }
        }
    }
}
