// VoxScript.Native/Platform/BrowserUrlService.cs
using System.Runtime.InteropServices;

namespace VoxScript.Native.Platform;

/// <summary>
/// Extracts the active tab URL from Chrome, Edge, or Firefox using UI Automation COM interop.
/// Fallback: returns null (title-based matching in PowerModeManager).
/// </summary>
public static class BrowserUrlService
{
    private static readonly string[] BrowserProcessNames =
        ["chrome", "msedge", "firefox", "brave", "opera"];

    public static string? TryGetActiveTabUrl()
    {
        var hwnd = Win32NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;

        Win32NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);

        try
        {
            using var proc = System.Diagnostics.Process.GetProcessById((int)pid);
            if (!BrowserProcessNames.Contains(proc.ProcessName, StringComparer.OrdinalIgnoreCase))
                return null;

            return ExtractUrlViaUiAutomation(hwnd);
        }
        catch { /* UIA failure or process access denied — return null */ }

        return null;
    }

    private static string? ExtractUrlViaUiAutomation(IntPtr hwnd)
    {
        IUIAutomation? uia = null;
        IUIAutomationElement? root = null;
        IUIAutomationCondition? condition = null;
        IUIAutomationElement? addressBar = null;

        try
        {
            uia = (IUIAutomation)new CUIAutomation();
            root = uia.ElementFromHandle(hwnd);
            if (root is null) return null;

            // Chrome/Edge: AutomationId "addressEditBox" or ClassName "OmniboxViewViews"
            // Firefox: AutomationId "urlbar-input"
            var cond1 = uia.CreatePropertyCondition(UIA_PropertyIds.AutomationId, "addressEditBox");
            var cond2 = uia.CreatePropertyCondition(UIA_PropertyIds.AutomationId, "urlbar-input");
            var cond3 = uia.CreatePropertyCondition(UIA_PropertyIds.ClassName, "OmniboxViewViews");

            var orCond12 = uia.CreateOrCondition(cond1, cond2);
            condition = uia.CreateOrCondition(orCond12, cond3);

            addressBar = root.FindFirst(TreeScope.TreeScope_Descendants, condition);
            if (addressBar is null) return null;

            // Try to get the Value pattern
            var patternObj = addressBar.GetCurrentPattern(UIA_PatternIds.ValuePattern);
            if (patternObj is IUIAutomationValuePattern vp)
            {
                var value = vp.CurrentValue;
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }
        finally
        {
            if (addressBar is not null) Marshal.ReleaseComObject(addressBar);
            if (condition is not null) Marshal.ReleaseComObject(condition);
            if (root is not null) Marshal.ReleaseComObject(root);
            if (uia is not null) Marshal.ReleaseComObject(uia);
        }

        return null;
    }

    // --- COM interop declarations for UI Automation ---

    [ComImport, Guid("ff48dba4-60ef-4201-aa87-54103eef594e")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomation
    {
        // IUIAutomation methods — we only declare the ones we need.
        // VTable slots must be in order, so unused ones are stubbed.

        int CompareElements(IUIAutomationElement el1, IUIAutomationElement el2);
        int CompareRuntimeIds(int[] runtimeId1, int[] runtimeId2);
        IUIAutomationElement GetRootElement();
        IUIAutomationElement ElementFromHandle(IntPtr hwnd);
        IUIAutomationElement ElementFromPoint(tagPOINT pt);
        IUIAutomationElement GetFocusedElement();
        IUIAutomationTreeWalker GetRawViewWalker(); // slot 6
        IUIAutomationTreeWalker GetControlViewWalker(); // slot 7
        IUIAutomationTreeWalker GetContentViewWalker(); // slot 8
        IUIAutomationCacheRequest CreateCacheRequest(); // slot 9 (unused, placeholder)
        IUIAutomationCondition CreateTrueCondition();
        IUIAutomationCondition CreateFalseCondition();
        IUIAutomationCondition CreatePropertyCondition(int propertyId, [MarshalAs(UnmanagedType.Struct)] object value);
        IUIAutomationCondition CreatePropertyConditionEx(int propertyId, [MarshalAs(UnmanagedType.Struct)] object value, int flags);
        IUIAutomationCondition CreateAndCondition(IUIAutomationCondition condition1, IUIAutomationCondition condition2);
        void CreateAndConditionFromArray(); // slot 15 stub
        void CreateAndConditionFromNativeArray(); // slot 16 stub
        IUIAutomationCondition CreateOrCondition(IUIAutomationCondition condition1, IUIAutomationCondition condition2);
    }

    [ComImport, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationElement
    {
        void SetFocus();
        int[] GetRuntimeId();
        IUIAutomationElement FindFirst(TreeScope scope, IUIAutomationCondition condition);
        IUIAutomationElementArray FindAll(TreeScope scope, IUIAutomationCondition condition);
        void FindFirstBuildCache(); // stub
        void FindAllBuildCache(); // stub
        void BuildUpdatedCache(); // stub
        object GetCurrentPropertyValue(int propertyId);
        void GetCurrentPropertyValueEx(); // stub
        void GetCachedPropertyValue(); // stub
        void GetCachedPropertyValueEx(); // stub
        IntPtr GetCurrentPatternAs(int patternId, ref Guid riid);

        [return: MarshalAs(UnmanagedType.IUnknown)]
        object GetCurrentPattern(int patternId);
    }

    [ComImport, Guid("a94cd8b1-0844-4cd6-9d2d-640537ab3942")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationValuePattern
    {
        void SetValue([MarshalAs(UnmanagedType.BStr)] string val);

        [DispId(0)]
        string CurrentValue
        {
            [return: MarshalAs(UnmanagedType.BStr)]
            get;
        }
    }

    // Placeholder interfaces for vtable ordering
    [ComImport, Guid("352ffba8-0973-437c-a61f-f64cafd81df9")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationCondition { }

    [ComImport, Guid("a4072986-c7a2-4ec8-b991-f0c4bc5e8a31")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationTreeWalker { }

    [ComImport, Guid("b32a92b5-5b97-4e74-9b80-2601ca937fd4")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationCacheRequest { }

    [ComImport, Guid("14314595-b4bc-4055-95f2-58f2e42c9855")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationElementArray { }

    // CUIAutomation COM class
    [ComImport, Guid("e22ad333-b25f-460c-83d0-0581107395c9")]
    private class CUIAutomation { }

    [StructLayout(LayoutKind.Sequential)]
    private struct tagPOINT
    {
        public int x;
        public int y;
    }

    private enum TreeScope
    {
        TreeScope_Element = 1,
        TreeScope_Children = 2,
        TreeScope_Descendants = 4,
        TreeScope_Subtree = 7,
    }

    private static class UIA_PropertyIds
    {
        public const int AutomationId = 30011;
        public const int ClassName = 30012;
    }

    private static class UIA_PatternIds
    {
        public const int ValuePattern = 10002;
    }
}
