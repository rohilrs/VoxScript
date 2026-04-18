using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using VoxScript.Core.Settings;
using VoxScript.Core.Transcription.Core;
using VoxScript.Core.Transcription.Models;
using VoxScript.Native.Whisper;
using VoxScript.Onboarding;
using VoxScript.Onboarding.Steps;
using Xunit;

namespace VoxScript.Tests.Onboarding;

public class ModelStepViewModelTests
{
    private sealed class InMemorySettingsStore : ISettingsStore
    {
        private readonly Dictionary<string, object?> _data = new();
        public T? Get<T>(string key) => _data.TryGetValue(key, out var v) ? (T?)v : default;
        public void Set<T>(string key, T value) => _data[key] = value;
        public bool Contains(string key) => _data.ContainsKey(key);
        public void Remove(string key) => _data.Remove(key);
    }

    private static (ModelStepViewModel vm, IWhisperModelManager manager, ILocalTranscriptionBackend backend, AppSettings settings, OnboardingViewModel onboarding) Build()
    {
        var manager = Substitute.For<IWhisperModelManager>();
        var backend = Substitute.For<ILocalTranscriptionBackend>();
        var settings = new AppSettings(new InMemorySettingsStore());
        var onboarding = new OnboardingViewModel(settings);
        var vm = new ModelStepViewModel(manager, backend, settings, onboarding);
        return (vm, manager, backend, settings, onboarding);
    }

    [Fact]
    public void Initial_sub_state_is_Picker()
    {
        var (vm, _, _, _, _) = Build();
        vm.SubState.Should().Be(ModelSubState.Picker);
    }

    [Fact]
    public void Balanced_is_pre_selected()
    {
        var (vm, _, _, _, _) = Build();
        vm.SelectedChoiceIndex.Should().Be(1);
        ModelStepViewModel.Choices[vm.SelectedChoiceIndex].Model.Name.Should().Be(PredefinedModels.BaseEn.Name);
    }

    [Fact]
    public async Task StartDownload_transitions_to_Downloading()
    {
        var (vm, manager, _, _, _) = Build();
        var tcs = new TaskCompletionSource();
        manager.DownloadAsync(Arg.Any<string>(), Arg.Any<IProgress<double>>(), Arg.Any<CancellationToken>())
               .Returns(tcs.Task);

        var download = vm.StartDownloadAsync();
        await Task.Delay(30);

        vm.SubState.Should().Be(ModelSubState.Downloading);

        tcs.SetCanceled();
        try { await download; } catch { }
    }

    [Fact]
    public async Task Successful_download_and_load_transitions_to_Done_and_writes_settings()
    {
        var (vm, manager, backend, settings, _) = Build();
        manager.DownloadAsync(Arg.Any<string>(), Arg.Any<IProgress<double>>(), Arg.Any<CancellationToken>())
               .Returns(Task.CompletedTask);
        manager.DownloadVadAsync(Arg.Any<IProgress<double>?>(), Arg.Any<CancellationToken>())
               .Returns(Task.CompletedTask);
        manager.GetModelPath(Arg.Any<string>()).Returns("/tmp/model.bin");
        backend.LoadModelAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(Task.CompletedTask);

        await vm.StartDownloadAsync();

        vm.SubState.Should().Be(ModelSubState.Done);
        settings.SelectedModelName.Should().Be(PredefinedModels.BaseEn.Name);
    }

    [Fact]
    public async Task Download_failure_transitions_to_Failed()
    {
        var (vm, manager, _, _, _) = Build();
        manager.DownloadAsync(Arg.Any<string>(), Arg.Any<IProgress<double>>(), Arg.Any<CancellationToken>())
               .Throws(new HttpRequestException("Connection refused"));

        await vm.StartDownloadAsync();

        vm.SubState.Should().Be(ModelSubState.Failed);
        vm.ErrorMessage.Should().Contain("Connection refused");
    }

    [Fact]
    public async Task CancelDownload_returns_to_Picker()
    {
        var (vm, manager, _, _, _) = Build();
        manager.DownloadAsync(Arg.Any<string>(), Arg.Any<IProgress<double>>(), Arg.Any<CancellationToken>())
               .Returns(async ci =>
               {
                   var ct = ci.Arg<CancellationToken>();
                   await Task.Delay(Timeout.Infinite, ct);
               });

        var downloadTask = vm.StartDownloadAsync();
        await Task.Delay(30);
        vm.SubState.Should().Be(ModelSubState.Downloading);

        vm.CancelDownload();
        await downloadTask;

        vm.SubState.Should().Be(ModelSubState.Picker);
    }

    [Fact]
    public async Task Done_state_unlocks_onboarding_model_step()
    {
        var (vm, manager, backend, _, onboarding) = Build();
        manager.DownloadAsync(Arg.Any<string>(), Arg.Any<IProgress<double>>(), Arg.Any<CancellationToken>())
               .Returns(Task.CompletedTask);
        manager.DownloadVadAsync(Arg.Any<IProgress<double>?>(), Arg.Any<CancellationToken>())
               .Returns(Task.CompletedTask);
        manager.GetModelPath(Arg.Any<string>()).Returns("/tmp/model.bin");
        backend.LoadModelAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(Task.CompletedTask);

        // Onboarding VM needs to be on the ModelPick step to evaluate CanGoNext usefully
        onboarding.UnlockMicStep();
        onboarding.GoNext(); // → MicPick
        onboarding.GoNext(); // → ModelPick (gated)
        onboarding.CanGoNext.Should().BeFalse();

        await vm.StartDownloadAsync();

        onboarding.CanGoNext.Should().BeTrue();
    }

    [Fact]
    public async Task VAD_download_failure_is_non_blocking()
    {
        var (vm, manager, backend, _, _) = Build();
        manager.DownloadAsync(Arg.Any<string>(), Arg.Any<IProgress<double>>(), Arg.Any<CancellationToken>())
               .Returns(Task.CompletedTask);
        manager.DownloadVadAsync(Arg.Any<IProgress<double>?>(), Arg.Any<CancellationToken>())
               .Throws(new Exception("VAD CDN down"));
        manager.GetModelPath(Arg.Any<string>()).Returns("/tmp/model.bin");
        backend.LoadModelAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(Task.CompletedTask);

        await vm.StartDownloadAsync();
        // VAD runs on a background Task.Run; give it a moment to throw
        await Task.Delay(30);

        vm.SubState.Should().Be(ModelSubState.Done);
    }

    [Fact]
    public void ReturnToPicker_gates_the_step_again()
    {
        var (vm, _, _, _, onboarding) = Build();
        onboarding.UnlockModelStep(); // pretend we've completed it previously

        vm.ReturnToPicker();

        vm.SubState.Should().Be(ModelSubState.Picker);
        // Step is gated again
        onboarding.UnlockMicStep();
        onboarding.GoNext(); // → MicPick
        onboarding.GoNext(); // → ModelPick (gated)
        onboarding.CanGoNext.Should().BeFalse();
    }
}
