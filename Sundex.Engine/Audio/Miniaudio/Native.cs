using System.Reflection;
using System.Runtime.InteropServices;

namespace Sundex.Engine.Audio.Miniaudio;

/// <summary>The C surface in Audio/Native/sundex_audio.c.</summary>
internal static partial class Native
{
    private const string Library = "sundex_audio";

    static Native()
    {
        // a browser build links the shim into the runtime, where there is nothing to load
        if (!OperatingSystem.IsBrowser()) NativeLibrary.SetDllImportResolver(typeof(Native).Assembly, Resolve);
    }

    /// <summary>
    ///     Package consumers find the library through deps.json. A project reference only copies it
    ///     to runtimes/&lt;rid&gt;/native next to the app, which the host doesn't search, so look there too.
    /// </summary>
    private static nint Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (name != Library) return 0;
        if (NativeLibrary.TryLoad(name, assembly, searchPath, out var handle)) return handle;

        string os, file;
        if (OperatingSystem.IsWindows()) (os, file) = ("win", "sundex_audio.dll");
        else if (OperatingSystem.IsMacOS()) (os, file) = ("osx", "libsundex_audio.dylib");
        else (os, file) = ("linux", "libsundex_audio.so");
        var arch = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();

        var path = Path.Combine(AppContext.BaseDirectory, "runtimes", $"{os}-{arch}", "native", file);
        return NativeLibrary.TryLoad(path, out handle) ? handle : 0;
    }

    [LibraryImport(Library, EntryPoint = "sundex_device_open", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int DeviceOpen(int backend, string? name, int sampleRate, out nint device);

    [LibraryImport(Library, EntryPoint = "sundex_device_close")]
    public static partial void DeviceClose(nint device);

    [LibraryImport(Library, EntryPoint = "sundex_device_name")]
    public static partial nint DeviceName(nint device);

    [LibraryImport(Library, EntryPoint = "sundex_device_sample_rate")]
    public static partial int DeviceSampleRate(nint device);

    [LibraryImport(Library, EntryPoint = "sundex_device_latency")]
    public static partial double DeviceLatency(nint device);

    [LibraryImport(Library, EntryPoint = "sundex_device_suspended")]
    public static partial int DeviceSuspended(nint device);

    [LibraryImport(Library, EntryPoint = "sundex_device_set_volume")]
    public static partial void DeviceSetVolume(nint device, float volume);

    [LibraryImport(Library, EntryPoint = "sundex_buffer_create")]
    public static unsafe partial int BufferCreate(nint device, byte* samples, int format, int channels, int sampleRate,
        long frames, out nint buffer);

    [LibraryImport(Library, EntryPoint = "sundex_buffer_free")]
    public static partial void BufferFree(nint buffer);

    [LibraryImport(Library, EntryPoint = "sundex_buffer_play_once")]
    public static partial int BufferPlayOnce(nint buffer, float volume, float pan);

    [LibraryImport(Library, EntryPoint = "sundex_voice_create")]
    public static partial int VoiceCreate(nint buffer, out nint voice);

    [LibraryImport(Library, EntryPoint = "sundex_voice_free")]
    public static partial void VoiceFree(nint voice);

    [LibraryImport(Library, EntryPoint = "sundex_voice_position")]
    public static partial long VoicePosition(nint voice);

    [LibraryImport(Library, EntryPoint = "sundex_voice_seek")]
    public static partial void VoiceSeek(nint voice, long frame);

    [LibraryImport(Library, EntryPoint = "sundex_voice_set_paused")]
    public static partial void VoiceSetPaused(nint voice, int paused);

    [LibraryImport(Library, EntryPoint = "sundex_voice_set_volume")]
    public static partial void VoiceSetVolume(nint voice, float volume);

    [LibraryImport(Library, EntryPoint = "sundex_voice_set_pan")]
    public static partial void VoiceSetPan(nint voice, float pan);

    [LibraryImport(Library, EntryPoint = "sundex_stream_create")]
    public static partial int StreamCreate(nint device, int sampleRate, int channels, long capacity, out nint stream);

    [LibraryImport(Library, EntryPoint = "sundex_stream_free")]
    public static partial void StreamFree(nint stream);

    [LibraryImport(Library, EntryPoint = "sundex_stream_put")]
    public static unsafe partial long StreamPut(nint stream, float* interleaved, long frames);

    [LibraryImport(Library, EntryPoint = "sundex_stream_queued")]
    public static partial long StreamQueued(nint stream);

    [LibraryImport(Library, EntryPoint = "sundex_stream_played")]
    public static partial long StreamPlayed(nint stream);

    [LibraryImport(Library, EntryPoint = "sundex_stream_flush")]
    public static partial void StreamFlush(nint stream, long basePosition);

    [LibraryImport(Library, EntryPoint = "sundex_stream_set_paused")]
    public static partial void StreamSetPaused(nint stream, int paused);

    [LibraryImport(Library, EntryPoint = "sundex_stream_set_volume")]
    public static partial void StreamSetVolume(nint stream, float volume);

    [LibraryImport(Library, EntryPoint = "sundex_stream_set_rate")]
    public static partial void StreamSetRate(nint stream, float rate);
}
