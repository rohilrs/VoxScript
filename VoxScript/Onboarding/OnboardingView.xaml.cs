using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VoxScript.Onboarding.Steps;

namespace VoxScript.Onboarding;

public sealed partial class OnboardingView : UserControl
{
    private readonly OnboardingViewModel _vm;
    private readonly MicStepViewModel _micVm;
    private readonly ModelStepViewModel _modelVm;
    private readonly TryItStepViewModel _tryItVm;

    private readonly Dictionary<OnboardingStep, UserControl> _stepViews;

    public OnboardingView(
        OnboardingViewModel vm,
        MicStepViewModel micVm,
        ModelStepViewModel modelVm,
        TryItStepViewModel tryItVm)
    {
        InitializeComponent();
        _vm = vm;
        _micVm = micVm;
        _modelVm = modelVm;
        _tryItVm = tryItVm;

        _stepViews = new Dictionary<OnboardingStep, UserControl>
        {
            [OnboardingStep.Welcome]   = new WelcomeStepView(),
            [OnboardingStep.MicPick]   = new MicStepView(micVm),
            [OnboardingStep.ModelPick] = new ModelStepView(modelVm),
            [OnboardingStep.Hotkeys]   = new HotkeysStepView(),
            [OnboardingStep.TryIt]     = new TryItStepView(tryItVm),
            [OnboardingStep.Final]     = new FinalStepView(),
        };

        _vm.PropertyChanged += OnVmChanged;
        ApplyState();
    }

    private OnboardingStep _displayedStep = (OnboardingStep)(-1);

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e) => ApplyState();

    private async void ApplyState()
    {
        Header.Progress = _vm.ProgressValue;
        Header.StepText = _vm.StepLabel;

        // Start/stop mic monitoring on step transitions into/out of MicPick.
        // Set _displayedStep BEFORE awaits so a second ApplyState firing during the
        // await cannot also trigger the transition.
        if (_displayedStep != _vm.CurrentStep)
        {
            var leaving = _displayedStep;
            var entering = _vm.CurrentStep;
            _displayedStep = entering;

            try
            {
                if (leaving == OnboardingStep.MicPick)
                    await _micVm.StopMonitoringAsync();
                if (entering == OnboardingStep.MicPick)
                    await _micVm.StartMonitoringAsync();
            }
            catch (Exception ex)
            {
                // async void -> can't throw out. Log and continue; the wizard should still function.
                Serilog.Log.Warning(ex, "Onboarding: mic monitor transition failed");
            }
        }

        StepHost.Content = _stepViews[_vm.CurrentStep];

        BackButton.Visibility = _vm.CanGoBack ? Visibility.Visible : Visibility.Collapsed;

        if (_vm.CurrentStep == OnboardingStep.Final)
        {
            PrimaryButton.Content = "Finish";
            PrimaryButton.IsEnabled = true;
        }
        else if (_vm.CurrentStep == OnboardingStep.Welcome)
        {
            PrimaryButton.Content = "Get started";
            PrimaryButton.IsEnabled = true;
        }
        else if (_vm.CurrentStep == OnboardingStep.Hotkeys)
        {
            PrimaryButton.Content = "Got it";
            PrimaryButton.IsEnabled = true;
        }
        else
        {
            PrimaryButton.Content = "Next";
            PrimaryButton.IsEnabled = _vm.CanGoNext;
        }

        // "Skip for now" appears only on the try-it step
        SkipLink.Visibility = _vm.CurrentStep == OnboardingStep.TryIt
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        // Mid-download Back = cancel + return to picker
        if (_vm.CurrentStep == OnboardingStep.ModelPick &&
            _modelVm.SubState == ModelSubState.Downloading)
        {
            _modelVm.CancelDownload();
            return;
        }
        _vm.GoBack();
    }

    private void PrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        switch (_vm.CurrentStep)
        {
            case OnboardingStep.MicPick:
                _micVm.ConfirmDevice();
                _vm.GoNext();
                break;
            case OnboardingStep.ModelPick:
                // If we're still in the Picker sub-state, Next means "start download"
                if (_modelVm.SubState == ModelSubState.Picker)
                    _ = _modelVm.StartDownloadAsync();
                else
                    _vm.GoNext();
                break;
            case OnboardingStep.Final:
                _vm.Finish();
                break;
            default:
                _vm.GoNext();
                break;
        }
    }

    private void SkipHyperlink_Click(Microsoft.UI.Xaml.Documents.Hyperlink sender,
        Microsoft.UI.Xaml.Documents.HyperlinkClickEventArgs args)
    {
        _tryItVm.SkipForNow();
        _vm.GoNext();
    }
}
