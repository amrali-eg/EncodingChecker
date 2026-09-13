using System.Runtime.InteropServices;

namespace EncodingChecker.GuiSmoke;

internal static class DesktopLocation
{
    // This asks the shell directly; a missing UIA subtree cannot answer this question.
    internal static bool IsCurrent(nint window)
    {
        Type type = Type.GetTypeFromCLSID(new Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A"))!;
        object instance = Activator.CreateInstance(type)!;
        int result = ((IVirtualDesktopManager)instance).IsWindowOnCurrentVirtualDesktop(
            window, out bool current);
        Marshal.ThrowExceptionForHR(result);
        return current;
    }

    [ComImport, Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        [PreserveSig]
        int IsWindowOnCurrentVirtualDesktop(nint window, [MarshalAs(UnmanagedType.Bool)] out bool current);
    }
}
