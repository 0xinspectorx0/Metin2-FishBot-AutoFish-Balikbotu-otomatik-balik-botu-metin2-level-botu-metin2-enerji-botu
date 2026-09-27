using MusicPlayerApp.Debugs;
using System;
using System.Diagnostics;
using System.Threading;

namespace MusicPlayerApp.Sources
{
    /// <summary>
    /// Botun tüm zamanlama ihtiyaçlarını karşılar: gecikme/geri sayım kontrolleri,
    /// rastgele bekleme süreleri, çalışma-mola döngüleri ve bilgisayar hızı ölçümü.
    /// </summary>
    /// <remarks>
    /// <para><b>Metot adı uyarısı:</b> <see cref="CheckDelayTimeInSecond"/> ve kardeşleri
    /// "<i>verilen süre HENÜZ dolmadı mı?</i>" sorusuna cevap verir. Yani <c>true</c>
    /// dönüyorsa beklemeye devam edilmelidir, <c>false</c> dönüyorsa süre dolmuştur.
    /// Kod tabanındaki kullanımlar (<c>if (!timer.CheckDelayTimeInSecond(3))</c>) bu
    /// anlama göre yazılmıştır; davranış korunmuş, yalnızca açıklama eklenmiştir.</para>
    ///
    /// <para><b>Bakım turunda düzeltilenler:</b></para>
    /// <list type="bullet">
    /// <item>Her çağrıda <c>new Random()</c> yaratılıyordu. <see cref="Random"/> zaman
    /// tohumlu olduğundan art arda yapılan çağrılar <b>aynı</b> sayıyı üretiyor, yani
    /// "rastgele" mola/tıklama süreleri sabitleniyordu. Artık kilit altında tek bir
    /// örnek kullanılıyor.</item>
    /// <item><see cref="StartTimeBreakMinute"/> / <see cref="StartTimeBreakSecond"/>
    /// mola süresi boyunca <c>Thread.Sleep</c> olmadan dönüyor (CPU %100) ve mola
    /// sırasında DURDUR'a basılsa bile uyanamıyordu. Artık saniyelik aralıklarla
    /// uyuyup durdurma isteğini kontrol ediyor.</item>
    /// </list>
    /// </remarks>
    internal class TimerGame
    {
        // Arayüz sayaçlarının ortak, thread-safe durumu.
        private static readonly object countdownDisplayLock = new object();
        private static DateTime? totalCountdownEndUtc;
        private static DateTime? activeCountdownEndUtc;
        private static DateTime? breakCountdownEndUtc;
        private static DateTime? botPauseStartedUtc;
        private static TimeSpan accumulatedBotPauseTime = TimeSpan.Zero;

        /// <summary>Bot duraklatılınca tüm TimerGame sayaçlarının saatini dondurur.</summary>
        public static void PauseBotTimers()
        {
            lock (countdownDisplayLock)
            {
                if (!botPauseStartedUtc.HasValue)
                {
                    botPauseStartedUtc = DateTime.UtcNow;
                }
            }
        }

        /// <summary>Bot devam ettiğinde, duraklama süresini bütün sayaçlardan çıkarır.</summary>
        public static void ResumeBotTimers()
        {
            lock (countdownDisplayLock)
            {
                if (!botPauseStartedUtc.HasValue) return;
                accumulatedBotPauseTime += DateTime.UtcNow - botPauseStartedUtc.Value;
                botPauseStartedUtc = null;
            }
        }

        private static DateTime GetLogicalUtcNow()
        {
            lock (countdownDisplayLock)
            {
                DateTime wallClock = botPauseStartedUtc ?? DateTime.UtcNow;
                return wallClock - accumulatedBotPauseTime;
            }
        }

        private static long GetLogicalUnixTimeMilliseconds()
        {
            return new DateTimeOffset(GetLogicalUtcNow()).ToUnixTimeMilliseconds();
        }

        private static long GetLogicalUnixTimeSeconds()
        {
            return new DateTimeOffset(GetLogicalUtcNow()).ToUnixTimeSeconds();
        }

        public static void StartTotalCountdownDisplay(int minutes)
        {
            lock (countdownDisplayLock)
            {
                totalCountdownEndUtc = GetLogicalUtcNow().AddMinutes(Math.Max(0, minutes));
            }
        }

        public static void ResetCountdownDisplay()
        {
            lock (countdownDisplayLock)
            {
                totalCountdownEndUtc = null;
                activeCountdownEndUtc = null;
                breakCountdownEndUtc = null;
                if (botPauseStartedUtc.HasValue)
                {
                    accumulatedBotPauseTime += DateTime.UtcNow - botPauseStartedUtc.Value;
                    botPauseStartedUtc = null;
                }
            }
        }

        private static void SetActiveCountdownDisplay(DateTime endUtc)
        {
            lock (countdownDisplayLock)
            {
                activeCountdownEndUtc = endUtc;
                breakCountdownEndUtc = null;
            }
        }

        private static void ClearActiveCountdownDisplay()
        {
            lock (countdownDisplayLock)
            {
                activeCountdownEndUtc = null;
            }
        }

        private static void StartBreakCountdownDisplay(int minutes)
        {
            lock (countdownDisplayLock)
            {
                activeCountdownEndUtc = null;
                breakCountdownEndUtc = GetLogicalUtcNow().AddMinutes(Math.Max(0, minutes));
            }
        }

        private static void EndBreakCountdownDisplay()
        {
            lock (countdownDisplayLock)
            {
                breakCountdownEndUtc = null;
            }
        }

        public static TimeSpan? GetTotalCountdownRemaining()
        {
            lock (countdownDisplayLock)
            {
                return GetRemainingTime(totalCountdownEndUtc);
            }
        }

        /// <summary>Toplam süre sayacı kurulmuş ve süresi dolmuş mu?</summary>
        public static bool IsTotalCountdownExpired()
        {
            lock (countdownDisplayLock)
            {
                return totalCountdownEndUtc.HasValue && GetLogicalUtcNow() >= totalCountdownEndUtc.Value;
            }
        }

        public static TimeSpan? GetActiveCountdownRemaining()
        {
            lock (countdownDisplayLock)
            {
                return GetRemainingTime(activeCountdownEndUtc);
            }
        }

        public static TimeSpan? GetBreakCountdownRemaining()
        {
            lock (countdownDisplayLock)
            {
                return GetRemainingTime(breakCountdownEndUtc);
            }
        }

        private static TimeSpan? GetRemainingTime(DateTime? endUtc)
        {
            if (!endUtc.HasValue)
            {
                return null;
            }

            TimeSpan remaining = endUtc.Value - GetLogicalUtcNow();
            return remaining <= TimeSpan.Zero ? TimeSpan.Zero : remaining;
        }
        private long StartedMilliSecTime = 0L;
        private long StartedSecondTime = 0L;
        private long StartedMinuteTime = 0L;

        private int breakTime = 0;
        private bool isBreakTimeDefined = false;

        /// <summary>"Hızlı bilgisayar" kabul edilen eşik işlemci performans yüzdesi.</summary>
        private static readonly float FAST_PC_CPU_MHZ = 150;

        /// <summary>Kullanıcı "PC yavaş" seçeneğini işaretledi mi?</summary>
        public static volatile bool IS_PC_SLOW = false;

        /// <summary>Aralıksız balık tutma süresi (dakika, alt sınır).</summary>
        public static volatile int MIN_WORK_TIME = 0;
        /// <summary>Aralıksız balık tutma süresi (dakika, üst sınır).</summary>
        public static volatile int MAX_WORK_TIME = 0;

        /// <summary>Mola süresi (dakika, alt sınır).</summary>
        public static volatile int MIN_BREAK_TIME = 0;
        /// <summary>Mola süresi (dakika, üst sınır).</summary>
        public static volatile int MAX_BREAK_TIME = 0;
        /// <summary>Botun tamamen durdurulacağı toplam süre (dakika).</summary>
        public static volatile int GAME_STOP_TIME = 0;

        #region Rastgele sayı üretimi

        /// <summary>
        /// Tüm bot tarafından paylaşılan tek <see cref="Random"/> örneği.
        /// </summary>
        private static readonly Random sharedRandom = new Random();
        private static readonly object randomLock = new object();

        /// <summary>
        /// <paramref name="minValue"/> (dahil) ile <paramref name="maxValue"/> (hariç)
        /// arasında rastgele bir tam sayı üretir.
        /// </summary>
        /// <remarks>
        /// Geriye dönük uyumluluk için eski davranış korunmuştur:
        /// <c>minValue &gt;= maxValue</c> ise aralık <c>[0, minValue]</c> olarak kabul edilir.
        /// Bu durum genelde bir yapılandırma hatasıdır ve <see cref="FileLogger"/> ile loglanır.
        /// </remarks>
        public static int MakeRandomValue(int minValue, int maxValue)
        {
            if (minValue >= maxValue)
            {
                FileLogger.Warning("MakeRandomValue: min (" + minValue + ") >= max (" + maxValue +
                    ") -> aralık 0.." + minValue + " olarak kullanıldı. Ayarları kontrol edin.");

                if (minValue <= 0)
                {
                    return 0;
                }
                maxValue = minValue;
                minValue = 0;
            }

            lock (randomLock)
            {
                return sharedRandom.Next(minValue, maxValue);
            }
        }

        /// <summary>
        /// <see cref="MakeRandomValue"/> sonucu + 1000 ms döner (saniyelik rastgele gecikme).
        /// </summary>
        public static int MakeRandomTimeSecond(int minValue, int maxValue)
        {
            return MakeRandomValue(minValue, maxValue) + 1000;
        }

        #endregion

        #region Bilgisayar hızı ölçümü

        /// <summary>
        /// İşlemci performans yüzdesini ölçer ve bilgisayarın "yavaş" sayılıp
        /// sayılmayacağını döner.
        /// </summary>
        public static bool DecideSlowOrFastPC()
        {
            float myCpuSpeed = MeasureCPUSpeed();

            if (FAST_PC_CPU_MHZ - myCpuSpeed >= 55)
            {
                DebugPfCnsl.println("Bu bilgisayar yavaş, balık tutma hızı düşürülecek");
                return true;
            }

            DebugPfCnsl.println("Bu bilgisayar hızlı");
            return false;
        }

        /// <summary>
        /// <c>Processor Information / % Processor Performance</c> sayacından işlemci
        /// performans yüzdesini okur. Okunamazsa 0 döner.
        /// </summary>
        public static float MeasureCPUSpeed()
        {
            try
            {
                using (PerformanceCounter cpuCounter = new PerformanceCounter(
                    "Processor Information", "% Processor Performance", "_Total"))
                {
                    // İlk okuma genelde 0 döner; doğru ölçüm için bir saniye beklenir.
                    cpuCounter.NextValue();
                    SleepActiveTime(1000);
                    float cpuFrequency = cpuCounter.NextValue();

                    FileLogger.Info(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "İşlemci performansı: {0:0.0}", cpuFrequency));
                    return cpuFrequency;
                }
            }
            catch (Exception ex)
            {
                // Performans sayaçları bazı sistemlerde (örn. çekirdek sayısı yüksek veya
                // sanallaştırılmış makinelerde) hata verebilir; botu durdurmamalı.
                FileLogger.Error("İşlemci hızı ölçülemedi", ex);
                return 0f;
            }
        }

        /// <summary>
        /// Basit bir döngü üzerinden ortalama işlem süresini ölçer (tanılama amaçlı).
        /// </summary>
        public static void MeasureProcessSpeed()
        {
            const int numberOfIterations = 100;
            long totalElapsedTicks = 0;

            for (int i = 0; i < numberOfIterations; i++)
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                for (int k = 0; k < 1000; k++)
                {
                    Math.Sqrt(k);
                }
                stopwatch.Stop();
                totalElapsedTicks += stopwatch.ElapsedTicks;
            }

            double averageElapsedMilliseconds = (totalElapsedTicks / (double)numberOfIterations)
                / Stopwatch.Frequency * 1000;
            double operationsPerMillisecond = averageElapsedMilliseconds <= 0
                ? 0
                : 1000.0 / averageElapsedMilliseconds;

            FileLogger.Info(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "Ortalama işlem süresi: {0:0.0000} ms ({1:0.0} işlem/ms)",
                averageElapsedMilliseconds, operationsPerMillisecond));
        }

        #endregion

        #region Bekleme (Sleep) yardımcıları

        /// <summary>
        /// <see cref="MakeRandomValue"/>(min,max) kadar milisaniye bekler.
        /// </summary>
        public static void SleepRandom(int minValue, int maxValue)
        {
            SleepActiveTime(MakeRandomValue(minValue, maxValue));
        }

        /// <summary>Duraklatma sırasında süre ilerletmeden milisaniye bekler.</summary>
        public static void SleepActiveTime(int milliseconds)
        {
            if (milliseconds <= 0) return;

            const int sliceMilliseconds = 50;
            long deadline = GetLogicalUnixTimeMilliseconds() + milliseconds;
            while (true)
            {
                long now = GetLogicalUnixTimeMilliseconds();
                long remaining = deadline - now;
                if (remaining <= 0) return;

                int sleepTime = ThreadGlobals.isBotPaused
                    ? sliceMilliseconds
                    : (int)Math.Min(sliceMilliseconds, remaining);
                Thread.Sleep(sleepTime);
            }
        }

        /// <summary>
        /// "Adapte tutma" seçeneği aktifse ve haritada başka oyuncu varsa yavaş,
        /// aksi halde hızlı bekler.
        /// </summary>
        public static void SleepRandomForPlayers(int minValue, int maxValue,
            int minValuePlayers, int maxValuePlayers)
        {
            if (ThreadGlobals.isAdaptableFishing && ThreadGlobals.isAnotherPlayerDetected)
            {
                SleepRandom(minValuePlayers, maxValuePlayers);
                return;
            }

            SleepRandom(minValue, maxValue);
        }

        /// <summary>
        /// <see cref="MakeRandomValue"/>(min,max) <b>dakika</b> kadar bekler.
        /// </summary>
        public static void SleepRandomMinute(int minValue, int maxValue)
        {
            int selectedBreakMinutes = MakeRandomValue(minValue, maxValue);
            long oneMinuteFromMilis = 60L * 1000L;
            long waitTime = (long)selectedBreakMinutes * oneMinuteFromMilis;

            // Gerçek balıkçılık molası bu metottan geçtiği için sayaç da aynı
            // rastgele seçilen süreyle burada başlatılmalıdır.
            StartBreakCountdownDisplay(selectedBreakMinutes);
            FileLogger.Info("Mola başladı; seçilen süre: " + selectedBreakMinutes + " dakika");

            try
            {
                // Uzun beklemeler parçalara bölünür; böylece DURDUR isteği anında fark edilir.
                const long sliceMilliseconds = 1000L;
                long remaining = waitTime;
                while (remaining > 0)
                {
                    if (ThreadGlobals.CheckGameIsStopped())
                    {
                        FileLogger.Info("SleepRandomMinute: bot durdurulduğu için bekleme yarıda kesildi");
                        return;
                    }

                    int sleepTime = (int)Math.Min(sliceMilliseconds, remaining);
                    SleepActiveTime(sleepTime);
                    remaining -= sleepTime;
                }
            }
            finally
            {
                EndBreakCountdownDisplay();
            }
        }

        #endregion

        #region Gecikme / geri sayım kontrolleri

        /// <summary>
        /// "Verilen <b>saniye</b> henüz dolmadı mı?" sorusunu cevaplar.
        /// </summary>
        /// <returns>Süre dolmadıysa <c>true</c>, dolduysa <c>false</c>.</returns>
        public bool CheckDelayTimeInSecond(long delayTime)
        {
            if (StartedSecondTime <= 0L) SetStartedSecondTime();
            return GetLogicalUnixTimeSeconds() - StartedSecondTime < delayTime;
        }

        /// <summary>
        /// "Verilen <b>milisaniye</b> henüz dolmadı mı?" sorusunu cevaplar.
        /// </summary>
        /// <returns>Süre dolmadıysa <c>true</c>, dolduysa <c>false</c>.</returns>
        public bool CheckDelayTimeInMilliSec(long delayTime)
        {
            if (StartedMilliSecTime <= 0L) SetStartedMilliSecTime();
            return GetLogicalUnixTimeMilliseconds() - StartedMilliSecTime < delayTime;
        }

        /// <summary>
        /// "Verilen <b>dakika</b> henüz dolmadı mı?" sorusunu cevaplar.
        /// </summary>
        /// <returns>Süre dolmadıysa <c>true</c>, dolduysa <c>false</c>.</returns>
        public bool CheckDelayTimeInMinute(long delayTime)
        {
            if (StartedMinuteTime <= 0L) SetStartedMinuteTime();
            return ((GetLogicalUnixTimeSeconds() - StartedMinuteTime) / 60) < delayTime;
        }

        /// <summary>
        /// Rastgele bir dakika aralığı için geri sayar; süre dolduğunda <c>true</c> döner
        /// ve sayacı sıfırlar (yani her <c>true</c> dönüşü yeni bir periyot başlatır).
        /// </summary>
        public bool CheckCountDownMinute(int minValue, int maxValue = 0,
            bool showAsActiveWorkCountdown = false)
        {
            if (!isBreakTimeDefined)
            {
                isBreakTimeDefined = true;
                breakTime = MakeRandomValue(minValue, maxValue);
                SetStartedMinuteTime();
            }

            // Yalnızca balıkçılığın çalışma/molaya geçiş sayacı gösterilir.
            if (showAsActiveWorkCountdown && StartedMinuteTime > 0L)
            {
                DateTime startUtc = DateTimeOffset.FromUnixTimeSeconds(StartedMinuteTime).UtcDateTime;
                SetActiveCountdownDisplay(startUtc.AddMinutes(breakTime));
            }

            if (!CheckDelayTimeInMinute(breakTime))
            {
                isBreakTimeDefined = false;
                if (showAsActiveWorkCountdown)
                {
                    ClearActiveCountdownDisplay();
                }
                return true;
            }

            return false;
        }

        /// <summary>
        /// Rastgele bir saniye aralığı için geri sayar; süre dolduğunda <c>true</c> döner
        /// ve sayacı sıfırlar.
        /// </summary>
        public bool CheckCountDownSecond(int minValue, int maxValue = 0)
        {
            if (!isBreakTimeDefined)
            {
                isBreakTimeDefined = true;
                breakTime = MakeRandomValue(minValue, maxValue);
                SetStartedSecondTime();
            }

            if (!CheckDelayTimeInSecond(breakTime))
            {
                isBreakTimeDefined = false;
                return true;
            }

            return false;
        }

        public void SetStartedMilliSecTime()
        {
            StartedMilliSecTime = GetLogicalUnixTimeMilliseconds();
        }

        public void SetStartedSecondTime()
        {
            StartedSecondTime = GetLogicalUnixTimeSeconds();
        }

        public void SetStartedMinuteTime()
        {
            StartedMinuteTime = GetLogicalUnixTimeSeconds();
        }

        #endregion

        #region Mola (break) yönetimi

        /// <summary>
        /// Rastgele bir <b>dakika</b> aralığı boyunca mola verir. Mola sırasında
        /// karakter "durdu" olarak işaretlenir.
        /// </summary>
        /// <remarks>
        /// Eski sürüm mola boyunca <c>Thread.Sleep</c> olmadan dönüyordu: bir çekirdek
        /// %100 çalışıyor ve DURDUR'a basılsa bile mola bitene kadar uyanamıyordu.
        /// Artık saniyelik dilimlerle uyuyup her dilimde durdurma isteği kontrol edilir.
        /// </remarks>
        public void StartTimeBreakMinute(int minValue, int maxValue)
        {
            int waitResult = MakeRandomValue(minValue, maxValue);
            FileLogger.Info("Mola veriliyor (dakika cinsinden hedef: " + waitResult + ")");

            SetStartedMinuteTime();
            StartBreakCountdownDisplay(waitResult);
            ThreadGlobals.isCharStopped = true;

            try
            {
                while (CheckDelayTimeInMinute(waitResult))
                {
                    if (ThreadGlobals.CheckGameIsStopped())
                    {
                        FileLogger.Info("Mola, bot durdurulduğu için erken bitirildi");
                        return;
                    }

                    SleepActiveTime(1000);                }
            }
            finally
            {
                ThreadGlobals.isCharStopped = false;
                EndBreakCountdownDisplay();
            }
        }

        /// <summary>
        /// Rastgele bir <b>saniye</b> aralığı boyunca kısa mola verir.
        /// </summary>
        public void StartTimeBreakSecond(int minValue, int maxValue)
        {
            int waitResult = MakeRandomValue(minValue, maxValue);
            FileLogger.Debug("Kısa mola veriliyor (saniye cinsinden hedef: " + waitResult + ")");

            SetStartedSecondTime();
            ThreadGlobals.isCharStopped = true;

            try
            {
                while (CheckDelayTimeInSecond(waitResult))
                {
                    if (ThreadGlobals.CheckGameIsStopped())
                    {
                        return;
                    }

                    SleepActiveTime(250);
                }
            }
            finally
            {
                ThreadGlobals.isCharStopped = false;
            }
        }

        #endregion
    }
}
