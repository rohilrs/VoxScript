// VoxScript.Native/Whisper/WhisperParams.cs
using System.Runtime.InteropServices;

namespace VoxScript.Native.Whisper;

// Mirror of whisper_full_params from whisper.h — GREEDY strategy (0)
[StructLayout(LayoutKind.Sequential)]
internal struct WhisperFullParams
{
    public int strategy;
    public int n_threads;
    public int n_max_text_ctx;
    public int offset_ms;
    public int duration_ms;
    [MarshalAs(UnmanagedType.I1)] public bool translate;
    [MarshalAs(UnmanagedType.I1)] public bool no_context;
    [MarshalAs(UnmanagedType.I1)] public bool no_timestamps;
    [MarshalAs(UnmanagedType.I1)] public bool single_segment;
    [MarshalAs(UnmanagedType.I1)] public bool print_special;
    [MarshalAs(UnmanagedType.I1)] public bool print_progress;
    [MarshalAs(UnmanagedType.I1)] public bool print_realtime;
    [MarshalAs(UnmanagedType.I1)] public bool print_timestamps;
    // token_timestamps fields (omit for now)
    [MarshalAs(UnmanagedType.I1)] public bool token_timestamps;
    public float thold_pt;
    public float thold_ptsum;
    public int max_len;
    [MarshalAs(UnmanagedType.I1)] public bool split_on_word;
    public int max_tokens;
    [MarshalAs(UnmanagedType.I1)] public bool debug_mode;
    public int audio_ctx;
    [MarshalAs(UnmanagedType.I1)] public bool tdrz_enable;
    IntPtr suppress_regex;
    IntPtr initial_prompt;
    IntPtr prompt_tokens;
    public int prompt_n_tokens;
    IntPtr language;
    [MarshalAs(UnmanagedType.I1)] public bool detect_language;
    [MarshalAs(UnmanagedType.I1)] public bool suppress_blank;
    [MarshalAs(UnmanagedType.I1)] public bool suppress_non_speech_tokens;
    public float temperature;
    public float max_initial_ts;
    public float length_penalty;
    public float temperature_inc;
    public float entropy_thold;
    public float logprob_thold;
    public float no_speech_thold;
    // greedy
    public int greedyBestOf;
    // beam search
    public int beamSearchBeamSize;
    public float beamSearchPatience;
    // callbacks — keep as IntPtr
    IntPtr new_segment_callback;
    IntPtr new_segment_callback_user_data;
    IntPtr progress_callback;
    IntPtr progress_callback_user_data;
    IntPtr encoder_begin_callback;
    IntPtr encoder_begin_callback_user_data;
    IntPtr abort_callback;
    IntPtr abort_callback_user_data;
    IntPtr logits_filter_callback;
    IntPtr logits_filter_callback_user_data;
    IntPtr grammar_rules;
    public IntPtr n_grammar_rules;
    public IntPtr i_start_rule;
    public float grammar_penalty;
}
