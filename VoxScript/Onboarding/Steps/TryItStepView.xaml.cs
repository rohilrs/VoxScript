using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace VoxScript.Onboarding.Steps;

public sealed partial class TryItStepView : UserControl
{
    private readonly TryItStepViewModel _vm;

    public TryItStepView(TryItStepViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        ApplyState();
        _vm.PropertyChanged += OnVmChanged;
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e) => ApplyState();

    private void ApplyState()
    {
        TranscriptBox.Visibility = Visibility.Collapsed;
        TryAgainLink.Visibility = Visibility.Collapsed;
        EmptyHint.Visibility = _vm.ShowEmptyHint ? Visibility.Visible : Visibility.Collapsed;

        switch (_vm.SubState)
        {
            case TryItSubState.Idle:
                TitleText.Text = "Let's try it";
                SubtitleText.Text = "Hold Ctrl+Win and say something — like \"hello from VoxScript\". Release to stop.";
                StatusText.Text = "Waiting for you to press the hotkey…";
                break;
            case TryItSubState.Recording:
                TitleText.Text = "Recording…";
                SubtitleText.Text = "Release Ctrl+Win when you're done.";
                StatusText.Text = "● Recording";
                break;
            case TryItSubState.Transcribing:
                TitleText.Text = "Transcribing…";
                SubtitleText.Text = "Running on your machine — no data leaves your device.";
                StatusText.Text = "◐ Transcribing";
                break;
            case TryItSubState.Success:
                TitleText.Text = "You're dictating!";
                SubtitleText.Text = "Here's what we heard. Hold the hotkey again to try another clip, or continue.";
                StatusText.Text = string.Empty;
                TranscriptText.Text = $"\u201C{_vm.TranscriptText}\u201D";
                TranscriptBox.Visibility = Visibility.Visible;
                TryAgainLink.Visibility = Visibility.Visible;
                break;
        }
    }

    private void TryAgain_Click(object sender, RoutedEventArgs e) => _vm.TryAgain();
}
