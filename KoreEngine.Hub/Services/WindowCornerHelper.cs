using System.Runtime.InteropServices;

namespace KoreEngine.Hub.Services;

/// <summary>
/// Active les coins arrondis natifs du compositeur DWM (Windows 11 build 22000+).
/// Sans effet sur Windows 10 (l'appel échoue silencieusement, coins droits en fallback).
/// </summary>
public static class WindowCornerHelper
{
    enum DwmWindowCornerPreference
    {
        Default = 0,
        DoNotRound = 1,
        Round = 2,
        RoundSmall = 3
    }

    const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int pvAttribute, int cbAttribute);

    public static void EnableRoundedCorners(IntPtr hwnd)
    {
        int preference = (int)DwmWindowCornerPreference.Round;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
    }
}
