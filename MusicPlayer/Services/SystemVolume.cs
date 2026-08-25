using System.Runtime.InteropServices;

// This file is the ONLY native interop in the application, and that is deliberate rather than drift.
// There is no WinRT and no managed API for the Windows master volume at all - Core Audio COM is the
// whole of the choice - so the alternative was not "a tidier API", it was "the feature does not
// exist". Where a managed route does exist, it wins; see the IPropertyStore note in Plan-17.md.
//
// Trap: see TRAPS.md T-12. Read it before touching any interface below.

namespace MusicPlayer.Services;

/// <summary>
/// The Windows master volume and mute for the default playback device. Step 18.
///
/// Static and shaped like AlbumArtCache: one lazily activated handle, and every method degrades to a
/// sentinel instead of throwing. It holds no state beyond that handle, so threading a ninth service
/// through App's wiring (A3) would buy nothing.
///
/// This file imports zero UI namespaces and names no playback-engine type, so invariant A1 holds.
/// Phrased that way on purpose: A1 is checked by grepping for the engine's type name, and spelling
/// it here even inside a comment would put a false positive in that check.
/// </summary>
internal static class SystemVolume
{
    /// <summary>
    /// Passed to both setters as the event-context GUID. Any volume-change notification carrying it
    /// is this app's own echo, so a future IAudioEndpointVolumeCallback can tell our writes from the
    /// user's and skip them. Nothing reads it yet; it costs nothing to pass the right thing now, and
    /// passing a shared or empty GUID is what makes such a callback loop on itself later.
    /// </summary>
    private static readonly Guid EventContext = new("7C9F1E4A-2B83-4D16-9E5C-A1D3F0B86427");

    /// <summary>eRender = 0 and eConsole = 0 from mmdeviceapi.h. Named because two bare zeroes at a
    /// call site say nothing about which enum each one belongs to.</summary>
    private const int DataFlowRender = 0;
    private const int RoleConsole = 0;

    /// <summary>CLSCTX_ALL. In-proc, handler, local and remote servers.</summary>
    private const int ClsCtxAll = 0x17;

    private static IAudioEndpointVolume? _endpoint;

    /// <summary>
    /// The endpoint handle, activated on first use. Null means the machine has no usable default
    /// playback device - an RDP session, or every output unplugged - and every caller below treats
    /// that as "no system volume available" rather than as an error.
    /// </summary>
    private static IAudioEndpointVolume? Endpoint => _endpoint ??= Activate();

    /// <summary>
    /// Sets the master volume. The scalar is the audio-tapered 0.0-1.0 value, which is exactly the
    /// number the Windows volume slider shows, so the caller's percentage maps straight onto it with
    /// no curve of its own. False means the write did not happen and the caller should fall back.
    /// </summary>
    internal static bool SetLevel(double scalar0To1)
    {
        try
        {
            Guid context = EventContext;

            return Endpoint?.SetMasterVolumeLevelScalar((float)Math.Clamp(scalar0To1, 0d, 1d), ref context) == 0;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            // A volume we cannot set is a volume the caller applies to its own stream instead. Same
            // trade as AlbumArtCache.Write: degrade, never take the app down over one control.
            return Forget();
        }
    }

    /// <summary>
    /// Reads the master volume as a 0.0-1.0 scalar, or null when there is no usable endpoint. Null
    /// and 0.0 mean different things and must not be conflated: 0.0 is silence the user chose.
    /// </summary>
    internal static double? GetLevel()
    {
        try
        {
            if (Endpoint?.GetMasterVolumeLevelScalar(out float level) == 0)
            {
                return level;
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            Forget();
        }

        return null;
    }

    /// <summary>
    /// Sets the system mute bit, which is the one Windows draws as a crossed-out speaker. This is a
    /// real mute and not a volume of zero, so unmuting returns to the level the user had without
    /// this app remembering it.
    /// </summary>
    internal static bool SetMuted(bool muted)
    {
        try
        {
            Guid context = EventContext;

            return Endpoint?.SetMute(muted, ref context) == 0;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            return Forget();
        }
    }

    /// <summary>Reads the system mute bit, or null when there is no usable endpoint.</summary>
    internal static bool? GetMuted()
    {
        try
        {
            if (Endpoint?.GetMute(out bool muted) == 0)
            {
                return muted;
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            Forget();
        }

        return null;
    }

    /// <summary>
    /// Releases the endpoint handle, from AudioService.Dispose. Not strictly required - the runtime
    /// drops it at process exit - but this app has exactly one COM object and releasing it keeps
    /// shutdown honest rather than relying on that.
    /// </summary>
    internal static void Release() => Forget();

    /// <summary>
    /// Drops the cached handle so the next call re-activates. Always returns false, so a failing
    /// setter can end with "return Forget();" and read as one thought. Re-activating matters: the
    /// default device changes when the user plugs in headphones, and the old handle goes stale.
    /// </summary>
    private static bool Forget()
    {
        if (_endpoint is not null)
        {
            Marshal.FinalReleaseComObject(_endpoint);
            _endpoint = null;
        }

        return false;
    }

    /// <summary>
    /// Walks CLSID_MMDeviceEnumerator to the default render endpoint's volume control. Returns null
    /// on any failure, which is the normal answer on a machine with no playback device.
    /// </summary>
    private static IAudioEndpointVolume? Activate()
    {
        try
        {
            IMMDeviceEnumerator enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();

            if (enumerator.GetDefaultAudioEndpoint(DataFlowRender, RoleConsole, out IMMDevice device) != 0)
            {
                return null;
            }

            Guid iid = typeof(IAudioEndpointVolume).GUID;

            return device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out object volume) == 0
                ? volume as IAudioEndpointVolume
                : null;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or NotSupportedException)
        {
            return null;
        }
    }

    // -----------------------------------------------------------------------------------------
    // Core Audio COM. EVERY interface below is a VTABLE, so the declarations are positional: each
    // method sits at the offset its position gives it, whether this app calls it or not. The
    // NotImpl members are placeholders holding those offsets and MUST NOT be deleted or reordered -
    // removing one silently moves every later method onto the wrong function, with a clean build
    // and no error at all. Order and GUIDs were read from the Windows SDK headers, mmdeviceapi.h
    // and endpointvolume.h, not from memory. Trap: see TRAPS.md T-12.
    //
    // The placeholders declare no parameters on purpose: an uncalled slot only needs to exist, and
    // spelling out arguments nothing passes would invite someone to call one.
    // -----------------------------------------------------------------------------------------

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator
    {
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int NotImpl1(); // EnumAudioEndpoints

        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(
            ref Guid iid,
            int clsCtx,
            IntPtr activationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int NotImpl1(); // RegisterControlChangeNotify
        [PreserveSig] int NotImpl2(); // UnregisterControlChangeNotify
        [PreserveSig] int NotImpl3(); // GetChannelCount
        [PreserveSig] int NotImpl4(); // SetMasterVolumeLevel

        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);

        [PreserveSig] int NotImpl6(); // GetMasterVolumeLevel

        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);

        [PreserveSig] int NotImpl8();  // SetChannelVolumeLevel
        [PreserveSig] int NotImpl9();  // SetChannelVolumeLevelScalar
        [PreserveSig] int NotImpl10(); // GetChannelVolumeLevel
        [PreserveSig] int NotImpl11(); // GetChannelVolumeLevelScalar

        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);

        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);

        // Slots 14-18 - GetVolumeStepInfo, VolumeStepUp, VolumeStepDown, QueryHardwareSupport,
        // GetVolumeRange - are omitted. Nothing after the last called method needs a slot.
    }
}
