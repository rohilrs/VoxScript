// VoxScript.Native/Whisper/WhisperNativeMethods.cs
using System.Runtime.InteropServices;

namespace VoxScript.Native.Whisper;

internal static class WhisperNativeMethods
{
    private const string DllName = "whisper";

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr whisper_init_from_file(string path_model);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void whisper_free(IntPtr ctx);

    // Returns a HEAP-ALLOCATED pointer to whisper_full_params.
    // Caller must free with whisper_free_params.
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr whisper_full_default_params_by_ref(int strategy);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void whisper_free_params(IntPtr @params);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int whisper_full(IntPtr ctx, IntPtr @params,
        [In] float[] samples, int n_samples);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int whisper_full_n_segments(IntPtr ctx);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr whisper_full_get_segment_text(IntPtr ctx, int i_segment);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int whisper_lang_id(string lang);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int whisper_model_n_vocab(IntPtr ctx);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr whisper_print_system_info();

    // ── ggml backend device enumeration ──────────────────────
    // These live in ggml.dll / ggml-base.dll and let us discover
    // which compute backends (CPU, Vulkan, etc.) are available.

    // Registry (ggml.dll) — device enumeration
    [DllImport("ggml", CallingConvention = CallingConvention.Cdecl)]
    internal static extern nuint ggml_backend_dev_count();

    [DllImport("ggml", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ggml_backend_dev_get(nuint index);

    // Device info (ggml-base.dll) — name and description
    [DllImport("ggml-base", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ggml_backend_dev_name(IntPtr dev);

    [DllImport("ggml-base", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ggml_backend_dev_description(IntPtr dev);

    // Offsets into whisper_full_params struct for the fields we need to set.
    // These are determined from the v1.8.4 struct layout (whisper.h).
    // strategy (int32) is at offset 0, n_threads (int32) at offset 4.
    internal static class ParamOffsets
    {
        // int strategy                    offset 0
        // int n_threads                   offset 4
        public const int NThreads = 4;
        // int n_max_text_ctx              offset 8
        // int offset_ms                   offset 12
        // int duration_ms                 offset 16
        // bool translate                  offset 20
        // bool no_context                 offset 21
        // bool no_timestamps              offset 22
        public const int NoTimestamps = 22;
        // bool single_segment             offset 23
        // bool print_special              offset 24
        // bool print_progress             offset 25
        public const int PrintProgress = 25;
        // bool print_realtime             offset 26
        public const int PrintRealtime = 26;
        // bool print_timestamps           offset 27
    }
}
