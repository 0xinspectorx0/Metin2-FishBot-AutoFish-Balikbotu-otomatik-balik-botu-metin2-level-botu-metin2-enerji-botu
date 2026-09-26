using MusicPlayerApp.Debugs;
using MusicPlayerApp.Sources.ImageHandle;
using System;
using System.Drawing;
using System.Threading;

namespace MusicPlayerApp.Sources.CoordinatesHandler
{
    /// <summary>
    /// Oyun penceresinin ekran uzerindeki konumunu, gorev cubugundaki/penceredeki
    /// <c>metin2Icon.png</c> sembolunu arayarak bulur. Butun koordinat hesaplamalari
    /// bu sinifin urettigi <see cref="currentScreenGamePoint"/> ofsetine dayanir.
    /// </summary>
    /// <remarks>
    /// BAKIM TURUNDA YAPILAN DEGISIKLIKLER:
    ///  1) <see cref="GameObjectCoordinates"/> 18 ayri yerde <c>new</c>'lendigi icin bu
    ///     sinifin <c>timerGame</c> alani her kurucuda yeniden atanip sifirlaniyor,
    ///     boylece "60 saniyede bir yeniden tara" mantigi calismiyordu; her koordinat
    ///     sorgusunda TAM EKRAN sembol taramasi tetiklenebiliyordu. Artik tum ornekler
    ///     ayni statik onbellegi ve ayni kilidi paylasir.
    ///  2) Tarama sonucu kisa sure (varsayilan 3 sn) onbellekte tutulur; boylece ayni
    ///     bot turundaki yuzlerce <c>RectXxx()</c> cagrisi tek tarama ile beslenir.
    ///  3) Uc thread ayni anda tarama tetikleyemesin diye <c>lock</c> eklendi.
    /// </remarks>
    internal class CheckGameCoordinate
    {
        /// <summary>Yeniden tam tarama yapilmadan once kullanilacak onbellek suresi (saniye).</summary>
        private const int RESCAN_INTERVAL_SECOND = 60;

        /// <summary>Ayni anda yapilan koordinat sorgularinin yeniden tarama tetiklemesini
        /// engelleyen kisa onbellek suresi (saniye).</summary>
        private const int SHARED_CACHE_SECOND = 3;

        /// <summary>Ekranda aranacak Metin2 sembolunun referans (sol ust) noktasi.</summary>
        private readonly Rectangle referenceCoordinates = new Rectangle(18, 11, 6, 6);

        /// <summary>Son bulunan oyun penceresi ofseti (tum ornekler paylasir).</summary>
        public static Point currentScreenGamePoint = new Point();

        /// <summary>Metin2 sembolu son taramada bulunabildi mi?</summary>
        public static volatile bool IS_METIN2_ICON_DETECTED = true;

        private static readonly object scanLock = new object();
        private static TimerGame sharedTimer;
        private static bool isScannedMt2Icon = false;
        private static DateTime lastSharedResultTime = DateTime.MinValue;
        private static Point lastSharedResult = new Point();
        private static long totalScanCount;
        private static long totalScanMilliseconds;

        private readonly ImageObjects process;

        public CheckGameCoordinate(ImageObjects imageObjects)
        {
            // imageObjects null gelirse singleton'a dusulur; boylece referans PNG'ler
            // hicbir durumda yeniden yuklenmez.
            this.process = imageObjects ?? ImageObjects.Instance;
        }

        /// <summary>Toplam sembol tarama sayisi (tanilama amacli).</summary>
        public static long TotalScanCount { get { return Interlocked.Read(ref totalScanCount); } }

        /// <summary>Sembol taramasi basina ortalama sure (ms).</summary>
        public static double AverageScanMilliseconds
        {
            get
            {
                long count = Interlocked.Read(ref totalScanCount);
                if (count <= 0)
                {
                    return 0d;
                }
                return Interlocked.Read(ref totalScanMilliseconds) / (double)count;
            }
        }

        /// <summary>
        /// Oyun penceresinin sol ust referans noktasini doner. Gerektiginde ekrani
        /// tarayarak Metin2 sembolunu bulur ve sonucu onbellege alir.
        /// </summary>
        protected Point CheckGameScreenPlace()
        {
            lock (scanLock)
            {
                // 1) Cok taze bir sonuc varsa hicbir sey yapma (yuzlerce RectXxx cagrisi
                //    ayni bot turunda buraya gelir).
                if ((DateTime.Now - lastSharedResultTime).TotalSeconds < SHARED_CACHE_SECOND)
                {
                    return lastSharedResult;
                }

                if (sharedTimer == null)
                {
                    sharedTimer = new TimerGame();
                    sharedTimer.SetStartedSecondTime();
                    isScannedMt2Icon = false;
                }

                Point result;

                if (!isScannedMt2Icon)
                {
                    result = ScanMetin2IconFromScreen();
                }
                else if (sharedTimer.CheckDelayTimeInSecond(RESCAN_INTERVAL_SECOND))
                {
                    // Henuz yeniden tarama zamani gelmedi; bilinen ofset kullanilir.
                    IS_METIN2_ICON_DETECTED = true;
                    result = currentScreenGamePoint;
                }
                else
                {
                    // 60 saniye doldu -> bir sonraki sorguda yeniden tara, ama bu tur
                    // bilinen ofseti kullan (botu bloklamamak icin).
                    isScannedMt2Icon = false;
                    sharedTimer.SetStartedSecondTime();
                    result = currentScreenGamePoint;
                }

                lastSharedResult = result;
                lastSharedResultTime = DateTime.Now;
                return result;
            }
        }

        /// <summary>
        /// Ekrani tarayarak Metin2 sembolunu bulur ve <see cref="currentScreenGamePoint"/>
        /// degerini gunceller. scanLock icinde cagrilmalidir.
        /// </summary>
        private Point ScanMetin2IconFromScreen()
        {
            long startTicks = DateTime.UtcNow.Ticks;
            try
            {
                Rectangle rectNewMetin2Icon = process.FindImageOnScreen(process.arrayMetin2Icon, referenceCoordinates);

                if (rectNewMetin2Icon != Rectangle.Empty)
                {
                    sharedTimer.SetStartedSecondTime();
                    isScannedMt2Icon = true;

                    int metin2IconX = rectNewMetin2Icon.X - referenceCoordinates.X;
                    int metin2IconY = rectNewMetin2Icon.Y - referenceCoordinates.Y;

                    currentScreenGamePoint = new Point(metin2IconX, metin2IconY);
                    IS_METIN2_ICON_DETECTED = true;

                    LogScanStatistics(startTicks, true);
                    return currentScreenGamePoint;
                }

                FileLogger.Warning("Program Metin2 sembolunu ekranda bulamadi. Oyun penceresi " +
                    "tasinmis veya kapanmis olabilir; son bilinen ofset kullanilacak: " +
                    currentScreenGamePoint);
                IS_METIN2_ICON_DETECTED = false;

                // Eski davranis (0,0 donmek) butun tıklamaları ekranin sol ustune kaydiriyordu.
                // Bunun yerine son bilinen ofset korunur; CheckGameStatus sembolu zaten
                // ayrica izleyip oyunu yeniden baslatiyor.
                LogScanStatistics(startTicks, false);
                return currentScreenGamePoint;
            }
            catch (Exception ex)
            {
                FileLogger.Error("Metin2 sembol taramasi sirasinda hata olustu", ex);
                IS_METIN2_ICON_DETECTED = false;
                return currentScreenGamePoint;
            }
        }

        private static void LogScanStatistics(long startTicks, bool found)
        {
            long elapsed = (DateTime.UtcNow.Ticks - startTicks) / TimeSpan.TicksPerMillisecond;
            Interlocked.Increment(ref totalScanCount);
            Interlocked.Add(ref totalScanMilliseconds, elapsed);

            FileLogger.Debug(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "Metin2 sembol taramasi: bulundu={0} sure={1}ms toplam={2} ortalama={3:0.0}ms ofset=({4},{5})",
                found, elapsed, TotalScanCount, AverageScanMilliseconds,
                currentScreenGamePoint.X, currentScreenGamePoint.Y));
        }

        /// <summary>
        /// Onbellegi gecersiz kilar; bir sonraki koordinat sorgusunda ekran yeniden taranir.
        /// Ornegin oyun penceresinin tasindigi bilindiginde cagrilabilir.
        /// </summary>
        public static void InvalidateCache()
        {
            lock (scanLock)
            {
                isScannedMt2Icon = false;
                lastSharedResultTime = DateTime.MinValue;
                if (sharedTimer != null)
                {
                    sharedTimer.SetStartedSecondTime();
                }
            }
        }
    }
}
