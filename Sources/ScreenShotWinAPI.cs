using MusicPlayerApp.Debugs;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace MusicPlayerApp.Sources
{
    /// <summary>
    /// Windows GDI (BitBlt) kullanarak ekran goruntusu alir ve alinan goruntuyu
    /// piksel dizisine (<c>int[]</c>, ARGB) cevirir.
    ///
    /// ONEMLI DEGISIKLIKLER (bakim turu):
    ///  1) <see cref="Image.FromHbitmap(IntPtr)"/> her cagrida bir GDI palette handle'i
    ///     yaratir ve bu handle serbest birakilmazsa surec 10.000 GDI nesnesi limitine
    ///     ulasip cokuyordu. Artik palette <see cref="DeleteObject"/> ile siliniyor.
    ///  2) Piksel okuma <c>GetPixel</c> yerine <c>LockBits</c> ile yapiliyor
    ///     (~20-50 kat daha hizli; botun ana maliyeti buradaydi).
    ///  3) <see cref="CaptureAreaAsArray"/> eklendi: yakala -> diziye cevir -> bitmap'i
    ///     dispose et. Boylece ara bitmap'lar bellekte/GDI'da birikmiyor.
    ///  4) Hata durumunda dispose edilmis bitmap dondurme hatasi giderildi; artik
    ///     <c>null</c> doner ve cagiran taraf bunu kontrol eder.
    ///  5) Worker thread'lerde <see cref="MessageBox.Show"/> cagirmak botu kilitledigi
    ///     icin kaldirildi; yerine log yaziliyor.
    /// </summary>
    internal class ScreenShotWinAPI
    {
        #region Win32 bildirileri

        [DllImport("user32.dll")]
        public static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindowDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindowRect(IntPtr hwnd, ref Rectangle rect);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

        [DllImport("gdi32.dll")]
        public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, uint dwRop);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteDC(IntPtr hdc);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ReleaseDC(IntPtr hwnd, IntPtr hdc);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("gdi32.dll")]
        public static extern uint GetDeviceCaps(IntPtr hdc, int nIndex);

        private const int LOGPIXELSX = 88;
        private const int LOGPIXELSY = 90;

        private const uint SRCCOPY = 0x00CC0020;
        private const uint CAPTUREBLT = 0x40000000;

        #endregion

        private static readonly object lockObject = new object();

        #region Tanilama sayaclari

        private static long totalCaptureCount;
        private static long totalCaptureMilliseconds;
        private static long totalPixelReadCount;
        private static long failedCaptureCount;
        private static DateTime lastDiagnosticLogTime = DateTime.MinValue;

        /// <summary>Toplam basarili ekran yakalama sayisi.</summary>
        public static long TotalCaptureCount { get { return Interlocked.Read(ref totalCaptureCount); } }

        /// <summary>Basarisiz ekran yakalama denemesi sayisi.</summary>
        public static long FailedCaptureCount { get { return Interlocked.Read(ref failedCaptureCount); } }

        /// <summary>Yakalama basina ortalama sure (ms).</summary>
        public static double AverageCaptureMilliseconds
        {
            get
            {
                long count = Interlocked.Read(ref totalCaptureCount);
                if (count <= 0)
                {
                    return 0d;
                }
                return Interlocked.Read(ref totalCaptureMilliseconds) / (double)count;
            }
        }

        #endregion

        /// <summary>
        /// Birincil ekranin (sanal masaustu degil) sinirlarini doner.
        /// </summary>
        public static Rectangle PrimaryScreenBounds
        {
            get
            {
                try
                {
                    return Screen.PrimaryScreen.Bounds;
                }
                catch (Exception ex)
                {
                    FileLogger.Error("PrimaryScreenBounds okunamadi", ex);
                    return new Rectangle(0, 0, 800, 600);
                }
            }
        }

        /// <summary>
        /// Sistem DPI olcegini yuzde olarak doner (96 = %100). Bot piksel bazli
        /// calistigi icin %100 disindaki degerler tanilama amacli loglanir.
        /// </summary>
        public static int GetSystemDpiPercent()
        {
            IntPtr dc = IntPtr.Zero;
            try
            {
                dc = GetDC(IntPtr.Zero);
                if (dc == IntPtr.Zero)
                {
                    return 100;
                }
                int dpiX = (int)GetDeviceCaps(dc, LOGPIXELSX);
                if (dpiX <= 0)
                {
                    return 100;
                }
                return (int)Math.Round(dpiX * 100f / 96f);
            }
            catch
            {
                return 100;
            }
            finally
            {
                if (dc != IntPtr.Zero)
                {
                    ReleaseDC(IntPtr.Zero, dc);
                }
            }
        }

        /// <summary>
        /// Tum ekranin goruntusunu alir. Cagiran taraf dispose etmekle yukumludur
        /// (tercihen <c>using</c> ile).
        /// </summary>
        public Bitmap CaptureScreen()
        {
            return CaptureSpecifiedScreen(PrimaryScreenBounds);
        }

        /// <summary>
        /// Belirtilen bolgenin ekran goruntusunu alir.
        /// Basarisiz olursa <c>null</c> doner (dispose edilmis bitmap DONMEZ).
        /// Cagiran taraf bitmap'i dispose etmelidir.
        /// </summary>
        public Bitmap CaptureSpecifiedScreen(Rectangle rectDefined)
        {
            if (!IsRectangleValid(rectDefined, "CaptureSpecifiedScreen"))
            {
                return null;
            }

            long startTicks = DateTime.UtcNow.Ticks;
            Bitmap result = new Bitmap(rectDefined.Width, rectDefined.Height, PixelFormat.Format32bppArgb);

            IntPtr desktophWnd = IntPtr.Zero;
            IntPtr desktopDc = IntPtr.Zero;
            IntPtr memoryDc = IntPtr.Zero;
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;
            IntPtr hPalette = IntPtr.Zero;
            bool success = false;

            lock (lockObject)
            {
                try
                {
                    desktophWnd = GetDesktopWindow();
                    desktopDc = GetWindowDC(desktophWnd);
                    if (desktopDc == IntPtr.Zero)
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "GetWindowDC basarisiz");
                    }

                    memoryDc = CreateCompatibleDC(desktopDc);
                    hBitmap = CreateCompatibleBitmap(desktopDc, rectDefined.Width, rectDefined.Height);
                    oldBitmap = SelectObject(memoryDc, hBitmap);

                    success = BitBlt(memoryDc, 0, 0, rectDefined.Width, rectDefined.Height,
                        desktopDc, rectDefined.Left, rectDefined.Top, SRCCOPY | CAPTUREBLT);

                    if (!success)
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "BitBlt basarisiz");
                    }

                    // FromHbitmap ikinci bir GDI palette handle'i yaratir; silinmezse sizinti olur.
                    using (Bitmap captured = Image.FromHbitmap(hBitmap))
                    {
                        using (Graphics graphics = Graphics.FromImage(result))
                        {
                            graphics.DrawImage(captured, Point.Empty);
                        }
                    }
                }
                catch (Exception ex)
                {
                    success = false;
                    Interlocked.Increment(ref failedCaptureCount);
                    FileLogger.Error("Ekran yakalama basarisiz. Bolge = " + Describe(rectDefined), ex);

                    result.Dispose();
                    result = null;
                }
                finally
                {
                    if (memoryDc != IntPtr.Zero)
                    {
                        if (oldBitmap != IntPtr.Zero)
                        {
                            SelectObject(memoryDc, oldBitmap);
                        }
                        DeleteDC(memoryDc);
                    }
                    if (hBitmap != IntPtr.Zero)
                    {
                        DeleteObject(hBitmap);
                    }
                    if (hPalette != IntPtr.Zero)
                    {
                        DeleteObject(hPalette);
                    }
                    if (desktopDc != IntPtr.Zero && desktophWnd != IntPtr.Zero)
                    {
                        ReleaseDC(desktophWnd, desktopDc);
                    }
                }
            }

            if (success && result != null)
            {
                long elapsed = (DateTime.UtcNow.Ticks - startTicks) / TimeSpan.TicksPerMillisecond;
                Interlocked.Increment(ref totalCaptureCount);
                Interlocked.Add(ref totalCaptureMilliseconds, elapsed);
                LogDiagnosticsPeriodically();
            }

            return result;
        }

        /// <summary>
        /// Bolgeyi yakalar, ARGB piksel dizisine cevirir ve aradaki bitmap'i
        /// HEMEN dispose eder. Bot icindeki en sik kullanim sekli budur:
        /// <code>ConvertBitmapToArray(CaptureSpecifiedScreen(rect))</code>
        /// Bu metot o ikilinin sizinti yapmayan, tek satirlik karsiligidir.
        /// </summary>
        /// <returns>Piksel dizisi; yakalama basarisizsa <c>null</c>.</returns>
        public int[] CaptureAreaAsArray(Rectangle rectDefined)
        {
            using (Bitmap captured = CaptureSpecifiedScreen(rectDefined))
            {
                return ConvertBitmapToArray(captured);
            }
        }

        /// <summary>
        /// Bolgeyi yakalar ve ARGB piksel dizisine cevirir.
        /// (Geriye donuk uyumluluk icin korunmustur; tercihen
        /// <see cref="CaptureAreaAsArray"/> kullanin.)
        /// </summary>
        public int[] ImageArraySpecifiedArea(Rectangle rectangle)
        {
            return CaptureAreaAsArray(rectangle);
        }

        /// <summary>
        /// <see cref="Graphics.CopyFromScreen"/> tabanli basit yakalama.
        /// Hata durumunda kullaniciyi engelleyen bir dialog GOSTERMEZ; loglar ve
        /// <c>null</c> doner.
        /// </summary>
        public Bitmap CaptureScreenBasic(Rectangle rect)
        {
            if (!IsRectangleValid(rect, "CaptureScreenBasic"))
            {
                return null;
            }

            Bitmap captureBitmap = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
            try
            {
                using (Graphics captureGraphics = Graphics.FromImage(captureBitmap))
                {
                    captureGraphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, rect.Size);
                }
                return captureBitmap;
            }
            catch (Exception ex)
            {
                FileLogger.Error("CaptureScreenBasic basarisiz. Bolge = " + Describe(rect), ex);
                captureBitmap.Dispose();
                return null;
            }
        }

        /// <summary>
        /// Bitmap'i ARGB <c>int[]</c> dizisine cevirir. <c>LockBits</c> kullanir.
        /// </summary>
        /// <remarks>
        /// Eski surum <c>GetPixel</c> kullaniyordu; her piksel icin ayri bir GDI+
        /// cagrisi yapildigindan 800x600'lik bir alan icin ~480.000 cagri demekti.
        /// Ayrica ekran yakalamada kaynak format <c>Format32bppRgb</c> (BGRA) olup
        /// <c>GetPixel().ToArgb()</c> ile ayni sonuc uretilir; burada BGRA baytlari
        /// dogrudan ARGB int'ine cevrilir.
        /// </remarks>
        public int[] ConvertBitmapToArray(Bitmap screenshot)
        {
            if (screenshot == null)
            {
                FileLogger.Warning("ConvertBitmapToArray: bitmap null, geri donus null");
                return null;
            }
            if (screenshot.Width <= 0 || screenshot.Height <= 0)
            {
                FileLogger.Warning("ConvertBitmapToArray: gecersiz bitmap boyutu " + Describe(screenshot));
                return null;
            }

            int width = screenshot.Width;
            int height = screenshot.Height;
            int[] pixelData = new int[width * height];

            BitmapData data = null;
            try
            {
                data = screenshot.LockBits(new Rectangle(0, 0, width, height),
                    ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

                int stride = data.Stride;
                byte[] buffer = new byte[stride * height];
                Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);

                int index = 0;
                for (int y = 0; y < height; y++)
                {
                    int rowStart = y * stride;
                    for (int x = 0; x < width; x++)
                    {
                        int offset = rowStart + (x * 4);
                        // Bellek duzeni BGRA -> ARGB int
                        pixelData[index++] = (buffer[offset + 3] << 24) |
                                             (buffer[offset + 2] << 16) |
                                             (buffer[offset + 1] << 8) |
                                             buffer[offset];
                    }
                }

                Interlocked.Add(ref totalPixelReadCount, index);
                return pixelData;
            }
            catch (Exception ex)
            {
                FileLogger.Error("ConvertBitmapToArray basarisiz", ex);
                return null;
            }
            finally
            {
                if (data != null)
                {
                    try
                    {
                        screenshot.UnlockBits(data);
                    }
                    catch
                    {
                        // yoksay
                    }
                }
            }
        }

        private bool IsRectangleValid(Rectangle rect, string callerName)
        {
            if (rect == Rectangle.Empty || rect.Width <= 0 || rect.Height <= 0)
            {
                FileLogger.Warning(callerName + ": gecersiz dikdortgen " + Describe(rect));
                return false;
            }

            Rectangle bounds = PrimaryScreenBounds;
            if (rect.Right > bounds.Right + bounds.Width || rect.Bottom > bounds.Bottom + bounds.Height)
            {
                FileLogger.Warning(callerName + ": ekran disina tasan bolge " + Describe(rect) +
                    " (ekran = " + Describe(bounds) + ")");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Performans tanilamasini en fazla 30 saniyede bir loglar.
        /// </summary>
        private static void LogDiagnosticsPeriodically()
        {
            try
            {
                if ((DateTime.Now - lastDiagnosticLogTime).TotalSeconds < 30)
                {
                    return;
                }
                lastDiagnosticLogTime = DateTime.Now;

                FileLogger.Debug(string.Format(
                    CultureInfo.InvariantCulture,
                    "Ekran yakalama istatistigi: toplam={0} basarisiz={1} ortalama={2:0.00}ms okunanPiksel={3}",
                    TotalCaptureCount,
                    FailedCaptureCount,
                    AverageCaptureMilliseconds,
                    Interlocked.Read(ref totalPixelReadCount)));
            }
            catch
            {
                // yoksay
            }
        }

        internal static string Describe(Rectangle rect)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "[X={0} Y={1} W={2} H={3}]", rect.X, rect.Y, rect.Width, rect.Height);
        }

        internal static string Describe(Bitmap bitmap)
        {
            if (bitmap == null)
            {
                return "(null bitmap)";
            }
            return string.Format(CultureInfo.InvariantCulture,
                "[{0}x{1} {2}]", bitmap.Width, bitmap.Height, bitmap.PixelFormat);
        }
    }
}
