using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;

namespace DiscordNative.App.Services;

public sealed class ScreenShareSourceItem
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public bool IsWindow { get; init; }
    public IntPtr WindowHandle { get; init; }
    public int BoundsX { get; init; }
    public int BoundsY { get; init; }
    public int BoundsWidth { get; init; } = 1920;
    public int BoundsHeight { get; init; } = 1080;
    public WriteableBitmap? Thumbnail { get; set; }
}

/// <summary>
/// Service natif Win32/DWM de capture d'écran et de fenêtres en temps réel (Go Live / Partage d'écran)
/// avec mixage optionnel de l'audio système (WASAPI Loopback).
/// </summary>
public sealed class ScreenCaptureService : IDisposable
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLengthW(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool StretchBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest, IntPtr hdcSrc, int xSrc, int ySrc, int wSrc, int hSrc, int rop);

    [DllImport("gdi32.dll")]
    private static extern int SetStretchBltMode(IntPtr hdc, int mode);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
    }

    private const int SRCCOPY = 0x00CC0020;
    private const int CAPTUREBLT = 0x40000000;
    private const int HALFTONE = 4;
    private const uint PW_RENDERFULLCONTENT = 0x00000002;
    private const int DWMWA_CLOAKED = 14;

    private CancellationTokenSource? _cts;
    private WriteableBitmap? _liveBitmap;
    private int _targetWidth = 1280;
    private int _targetHeight = 720;
    private int _targetFps = 30;
    private ScreenShareSourceItem? _activeSource;

    public WriteableBitmap? LiveBitmap => _liveBitmap;
    public ScreenShareSourceItem? ActiveSource => _activeSource;
    public string ResolutionLabel => _targetHeight >= 1080 ? "1080p" : "720p";
    public string FpsLabel => $"{_targetFps} FPS";

    public static List<ScreenShareSourceItem> GetAvailableSources()
    {
        var list = new List<ScreenShareSourceItem>();

        try
        {
            // 1. Primary Screen
            int primW = Math.Max(640, GetSystemMetrics(0)); // SM_CXSCREEN
            int primH = Math.Max(360, GetSystemMetrics(1)); // SM_CYSCREEN
            var primaryItem = new ScreenShareSourceItem
            {
                Id = "screen:primary",
                Title = "Écran 1 (Principal)",
                Subtitle = $"{primW} × {primH} • Bureau complet",
                IsWindow = false,
                BoundsX = 0,
                BoundsY = 0,
                BoundsWidth = primW,
                BoundsHeight = primH
            };
            primaryItem.Thumbnail = CaptureThumbnail(primaryItem, 320, 180);
            list.Add(primaryItem);

            // Virtual Desktop if multi-monitor
            int virtX = GetSystemMetrics(76); // SM_XVIRTUALSCREEN
            int virtY = GetSystemMetrics(77); // SM_YVIRTUALSCREEN
            int virtW = GetSystemMetrics(78); // SM_CXVIRTUALSCREEN
            int virtH = GetSystemMetrics(79); // SM_CYVIRTUALSCREEN
            if (virtW > primW || virtH > primH)
            {
                var allScreens = new ScreenShareSourceItem
                {
                    Id = "screen:all",
                    Title = "Tous les écrans",
                    Subtitle = $"{virtW} × {virtH} • Bureau étendu",
                    IsWindow = false,
                    BoundsX = virtX,
                    BoundsY = virtY,
                    BoundsWidth = virtW,
                    BoundsHeight = virtH
                };
                allScreens.Thumbnail = CaptureThumbnail(allScreens, 320, 180);
                list.Add(allScreens);
            }

            // 2. Visible Top-Level Application Windows
            EnumWindows((hWnd, _) =>
            {
                if (!IsWindowVisible(hWnd) || IsIconic(hWnd))
                    return true;

                if (DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                    return true;

                int len = GetWindowTextLengthW(hWnd);
                if (len <= 1) return true;

                var sb = new StringBuilder(len + 1);
                GetWindowTextW(hWnd, sb, sb.Capacity);
                string title = sb.ToString().Trim();
                if (string.IsNullOrWhiteSpace(title) || title == "Program Manager" || title == "Windows Input Experience")
                    return true;

                if (!GetWindowRect(hWnd, out RECT rc))
                    return true;

                int w = rc.Right - rc.Left;
                int h = rc.Bottom - rc.Top;
                if (w < 200 || h < 120)
                    return true;

                var winItem = new ScreenShareSourceItem
                {
                    Id = $"window:{hWnd.ToInt64()}",
                    Title = title,
                    Subtitle = $"Fenêtre • {w} × {h}",
                    IsWindow = true,
                    WindowHandle = hWnd,
                    BoundsX = rc.Left,
                    BoundsY = rc.Top,
                    BoundsWidth = w,
                    BoundsHeight = h
                };
                winItem.Thumbnail = CaptureThumbnail(winItem, 320, 180);
                list.Add(winItem);
                return list.Count < 14;
            }, IntPtr.Zero);
        }
        catch
        {
        }

        return list;
    }

    public static WriteableBitmap? CaptureThumbnail(ScreenShareSourceItem source, int thumbW, int thumbH)
    {
        try
        {
            byte[] pixels = new byte[thumbW * thumbH * 4];
            if (!CaptureBgraBytes(source, thumbW, thumbH, pixels))
                return null;

            var wb = new WriteableBitmap(thumbW, thumbH);
            using var stream = wb.PixelBuffer.AsStream();
            stream.Write(pixels, 0, pixels.Length);
            wb.Invalidate();
            return wb;
        }
        catch
        {
            return null;
        }
    }

    public void StartCapture(ScreenShareSourceItem source, int width = 1280, int height = 720, int fps = 30, Action? onFrameUpdated = null)
    {
        StopCapture();

        _activeSource = source;
        _targetWidth = Math.Clamp(width, 640, 1920);
        _targetHeight = Math.Clamp(height, 360, 1080);
        _targetFps = Math.Clamp(fps, 15, 60);

        _liveBitmap = new WriteableBitmap(_targetWidth, _targetHeight);
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        int intervalMs = Math.Max(16, 1000 / _targetFps);
        byte[] frameBuffer = new byte[_targetWidth * _targetHeight * 4];

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    if (CaptureBgraBytes(source, _targetWidth, _targetHeight, frameBuffer))
                    {
                        DiscordNative.Protocol.App.RunOnUiThread(() =>
                        {
                            if (token.IsCancellationRequested || _liveBitmap is null) return;
                            try
                            {
                                using var stream = _liveBitmap.PixelBuffer.AsStream();
                                stream.Position = 0;
                                stream.Write(frameBuffer, 0, frameBuffer.Length);
                                _liveBitmap.Invalidate();
                                onFrameUpdated?.Invoke();
                            }
                            catch { }
                        });
                    }
                }
                catch { }

                sw.Stop();
                int wait = Math.Max(8, intervalMs - (int)sw.ElapsedMilliseconds);
                try { await Task.Delay(wait, token); } catch { break; }
            }
        }, token);
    }

    private static bool CaptureBgraBytes(ScreenShareSourceItem source, int outW, int outH, byte[] destPixels)
    {
        IntPtr hDesktop = GetDesktopWindow();
        IntPtr hdcScreen = GetWindowDC(hDesktop);
        if (hdcScreen == IntPtr.Zero)
            return false;

        IntPtr hdcMem = CreateCompatibleDC(hdcScreen);
        IntPtr hDib = IntPtr.Zero;
        IntPtr oldObj = IntPtr.Zero;

        try
        {
            var bmi = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = outW,
                    biHeight = -outH, // top-down DIB
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0 // BI_RGB
                }
            };

            hDib = CreateDIBSection(hdcMem, ref bmi, 0, out IntPtr ppvBits, IntPtr.Zero, 0);
            if (hDib == IntPtr.Zero || ppvBits == IntPtr.Zero)
                return false;

            oldObj = SelectObject(hdcMem, hDib);
            SetStretchBltMode(hdcMem, HALFTONE);

            int srcX = source.BoundsX;
            int srcY = source.BoundsY;
            int srcW = Math.Max(1, source.BoundsWidth);
            int srcH = Math.Max(1, source.BoundsHeight);

            if (source.IsWindow && source.WindowHandle != IntPtr.Zero && GetWindowRect(source.WindowHandle, out RECT rc))
            {
                srcX = rc.Left;
                srcY = rc.Top;
                srcW = Math.Max(1, rc.Right - rc.Left);
                srcH = Math.Max(1, rc.Bottom - rc.Top);
            }

            StretchBlt(hdcMem, 0, 0, outW, outH, hdcScreen, srcX, srcY, srcW, srcH, SRCCOPY | CAPTUREBLT);

            Marshal.Copy(ppvBits, destPixels, 0, destPixels.Length);
            // Force opaque alpha channel (GDI StretchBlt can leave alpha = 0)
            for (int i = 3; i < destPixels.Length; i += 4)
            {
                destPixels[i] = 255;
            }
            return true;
        }
        finally
        {
            if (oldObj != IntPtr.Zero) SelectObject(hdcMem, oldObj);
            if (hDib != IntPtr.Zero) DeleteObject(hDib);
            if (hdcMem != IntPtr.Zero) DeleteDC(hdcMem);
            ReleaseDC(hDesktop, hdcScreen);
        }
    }

    public void StopCapture()
    {
        try { _cts?.Cancel(); } catch { }
        try { _cts?.Dispose(); } catch { }
        _cts = null;
        _activeSource = null;
    }

    public void Dispose()
    {
        StopCapture();
    }
}
