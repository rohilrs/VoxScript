// VoxScript/Infrastructure/NavigationService.cs
using Microsoft.UI.Xaml.Controls;

namespace VoxScript.Infrastructure;

/// <summary>
/// Provides frame-based navigation for the main window's content area.
/// Registered as a singleton in the DI container so ViewModels can
/// trigger navigation without referencing UI types directly.
/// </summary>
public sealed class NavigationService
{
    private Frame? _frame;

    public Frame? Frame
    {
        get => _frame;
        set => _frame = value;
    }

    public bool CanGoBack => _frame?.CanGoBack ?? false;

    public void NavigateTo(Type pageType, object? parameter = null)
    {
        if (_frame is null)
            throw new InvalidOperationException("NavigationService frame not set.");

        _frame.Navigate(pageType, parameter);
    }

    public void GoBack()
    {
        if (_frame?.CanGoBack == true)
            _frame.GoBack();
    }
}
