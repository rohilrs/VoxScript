using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VoxScript.Core.Audio;

namespace VoxScript.Onboarding.Steps;

public sealed partial class MicStepView : UserControl
{
    private readonly MicStepViewModel _vm;
    private readonly DispatcherTimer _timer;
    private DateTime _lastTick;

    public MicStepView(MicStepViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DeviceDropdown.ItemsSource = vm.Devices;
        if (vm.SelectedDevice is not null)
            DeviceDropdown.SelectedItem = vm.SelectedDevice;

        ApplyState();
        _vm.PropertyChanged += OnVmChanged;

        // Drive the level meter from the VM's AudioLevel. The actual audio
        // capture pump is started by App wiring in Task 7.
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += (_, _) =>
        {
            var now = DateTime.UtcNow;
            var delta = (now - _lastTick).TotalSeconds;
            _lastTick = now;
            Meter.Level = _vm.AudioLevel;
        };
        _lastTick = DateTime.UtcNow;
        _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e) => ApplyState();

    private void ApplyState()
    {
        NoDeviceBox.Visibility = _vm.NoDevicesFound ? Visibility.Visible : Visibility.Collapsed;
        MeterBox.Visibility = _vm.NoDevicesFound ? Visibility.Collapsed : Visibility.Visible;
        SignalChip.Visibility = _vm.SignalDetected ? Visibility.Visible : Visibility.Collapsed;
        NoSignalHint.Visibility = _vm.ShowNoSignalHint ? Visibility.Visible : Visibility.Collapsed;
    }

    private void DeviceDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DeviceDropdown.SelectedItem is AudioDeviceInfo device)
            _vm.SelectedDevice = device;
    }

    private void Retry_Click(object sender, RoutedEventArgs e)
    {
        _vm.Retry();
        DeviceDropdown.ItemsSource = _vm.Devices;
        if (_vm.SelectedDevice is not null)
            DeviceDropdown.SelectedItem = _vm.SelectedDevice;
    }

    private void SkipCheck_Click(object sender, RoutedEventArgs e) => _vm.SkipCheck();
}
