using Metin2AutoFishCSharp.Sources.CharacterHandle;
using Metin2AutoFishCSharp.Sources.ChatHandler;
using Metin2AutoFishCSharp.Sources.GameHandler;
using Metin2AutoFishCSharp.Sources.LevelAndFarms;
using Metin2AutoFishCSharp.Sources;
using MusicPlayerApp.Debugs;
using MusicPlayerApp.Sources.CharacterHandle;
using MusicPlayerApp.Sources.GameHandler;
using MusicPlayerApp.Sources.ImageHandle;
using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace MusicPlayerApp.Sources
{
    /// <summary>
    /// Botun üç iş parçacığını kurar, başlatır ve durdurur.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><b>T1</b> — ana bot döngüsü: balık tutma / level-farm / enerji kristali.</item>
    /// <item><b>T2</b> — oyun durumu nöbetçisi: giriş ekranı, ölüm, karakter ekranı,
    /// satış başlığı, ayar düğmesi, balık panosu.</item>
    /// <item><b>T3</b> — yan görevler: ticaret paneli, fısıltı/chat cevaplama, statü
    /// dağıtma, ETP toplama.</item>
    /// </list>
    ///
    /// <para><b>Bakım turunda düzeltilenler:</b></para>
    /// <list type="bullet">
    /// <item>Üç thread gövdesi de <c>try/catch</c> olmadan çalışıyordu; tek bir
    /// <see cref="NullReferenceException"/> botu sessizce öldürüyor, arayüz ise
    /// "çalışıyor" göstermeye devam ediyordu. Artık hata loglanıyor, arayüz
    /// bilgilendiriliyor ve buton yeniden etkinleştiriliyor.</item>
    /// <item><see cref="Stop"/> içindeki <c>while (... ) { }</c> boş döngüsü bir
    /// çekirdeği %100 meşgul ediyordu; <c>Thread.Sleep</c> ile bekleme + azami
    /// bekleme süresi eklendi.</item>
    /// <item>T1 ana döngüsü, üç mod da kapalıyken boşuna tam hızda dönüyordu;
    /// uyku eklendi.</item>
    /// <item>Enerji kristali geri sayımında yanlışlıkla "Level Farm Bot is starting"
    /// yazılıyordu; düzeltildi ve geri sayım her modda "başlatıldı" mesajıyla bitiyor.</item>
    /// <item><see cref="HandleFormElement"/> artık form kapanmış/kontrol dispose edilmiş
    /// olsa bile exception üretmiyor ve <c>Invoke</c> yerine <c>BeginInvoke</c> kullanarak
    /// kapanış sırasında kilitlenmeyi önlüyor.</item>
    /// </list>
    /// </remarks>
    internal class ThreadsHandler
    {
        public const string MainThreadName = "Main Thread";
        public const string T1Name = "Thread 1";
        public const string T2Name = "Thread 2";
        public const string T3Name = "Thread 3";

        /// <summary>Stop() çağrısının thread'lerin bitmesini bekleyeceği azami süre (ms).</summary>
        private const int STOP_WAIT_TIMEOUT_MILLISECONDS = 30000;

        /// <summary>Thread'lerin bitip bitmediğini yoklama aralığı (ms).</summary>
        private const int THREAD_POLL_INTERVAL_MILLISECONDS = 50;

        /// <summary>Hiçbir mod açık değilken T1'in boşta bekleme süresi (ms).</summary>
        private const int IDLE_SLEEP_MILLISECONDS = 200;

        private ScreenShotWinAPI screenShot;

        private ImageObjects imageObject;
        private FishingHandle fishing;
        private CheckGameStatus gameStatus;
        private GameObjectCoordinates coordinates;
        private CharSpecialThings charThings;
        private Chatting chatting;
        private GameInputHandler inputGame;
        private GameAlphabetDetecter gameWords;
        private CharInfo charInfo;
        private CharPickUpItems charPickUp;
        private ChatSentencer chatSentencer;
        private PlayersHandler playersHandle;
        private LevelHandle levelHandle;
        private StatusHandler statusHandler;
        private CharMovement charMovement;
        private EnerjyCristalHandle enerjyCrisHandle;

        private TimerGame timeGeneral;
        private TimerGame timerLevelFarmBot;
        private TimerGame timeEnergyBot;

        private readonly object totalCountdownWatcherLock = new object();
        private System.Threading.Timer totalCountdownWatcher;
        private volatile bool isBotRunActive;
        private int totalCountdownGeneration;
        private int totalTimeExpiryHandled;

        public ThreadsHandler(ImageObjects imageObject)
        {
            // imageObject null gelirse singleton kullanılır; referans PNG'ler yeniden yüklenmez.
            this.imageObject = imageObject ?? ImageObjects.Instance;

            gameWords = new GameAlphabetDetecter(this.imageObject);
            fishing = new FishingHandle(this.imageObject);
            gameStatus = new CheckGameStatus(this.imageObject, gameWords);
            coordinates = new GameObjectCoordinates(this.imageObject);
            screenShot = new ScreenShotWinAPI();
            charThings = new CharSpecialThings(this.imageObject);
            timeGeneral = new TimerGame();
            chatting = new Chatting(this.imageObject);
            inputGame = new GameInputHandler();
            charInfo = new CharInfo(this.imageObject);
            chatSentencer = new ChatSentencer();
            playersHandle = new PlayersHandler(this.imageObject);
            levelHandle = new LevelHandle(this.imageObject);
            statusHandler = new StatusHandler(this.imageObject, gameWords, levelHandle);
            charPickUp = new CharPickUpItems(this.imageObject, gameWords);
            timerLevelFarmBot = new TimerGame();
            timeEnergyBot = new TimerGame();
            charMovement = new CharMovement(this.imageObject, gameWords);
            enerjyCrisHandle = new EnerjyCristalHandle(this.imageObject, gameWords);
        }

        /// <summary>
        /// Üç iş parçacığını başlatır. Hangi modun çalışacağı
        /// <see cref="ThreadGlobals"/> bayraklarıyla belirlenir.
        /// </summary>
        public void Start()
        {
            FileLogger.Info("ThreadsHandler.Start çağrıldı (balık=" + ThreadGlobals.isFishingStopped +
                ", levelFarm=" + ThreadGlobals.isLevelFarmStopped +
                ", enerji=" + ThreadGlobals.isEnergyCristalStopped + ")");

            StopTotalCountdownWatcher();
            Interlocked.Exchange(ref totalTimeExpiryHandled, 0);
            isBotRunActive = true;
            // Thread gövdeleri başlamadan önce bayrakları kaldır ki çok kısa/0 dakikalık
            // toplam sürede Stop() çağrısı başlangıç atamasıyla geri çevrilmesin.
            ThreadGlobals.IsThreadOneActive = true;
            ThreadGlobals.IsThreadTwoActive = true;
            ThreadGlobals.IsThreadThreeActive = true;
            TimerGame.ResetCountdownDisplay();
            if (ThreadGlobals.isTimerBreakEnabled)
            {
                StartTotalCountdownTimer();
            }

            //@@@@@@@@    THREAD 1 — ana bot döngüsü    @@@@@@@@@@@@
            Thread t1 = new Thread(() => RunThreadSafely(T1Name, ThreadOneBody));
            t1.Name = T1Name;
            t1.IsBackground = true;
            t1.Start();

            //@@@@@@@@    THREAD 2 — oyun durumu nöbetçisi    @@@@@@@@@@@@
            Thread t2 = new Thread(() => RunThreadSafely(T2Name, ThreadTwoBody));
            t2.Name = T2Name;
            t2.IsBackground = true;
            t2.Start();

            //@@@@@@@@    THREAD 3 — yan görevler    @@@@@@@@@@@@
            Thread t3 = new Thread(() => RunThreadSafely(T3Name, ThreadThreeBody));
            t3.Name = T3Name;
            t3.IsBackground = true;
            t3.Start();
        }

        /// <summary>Toplam süre sayacını başlatır veya kullanıcı yeniden etkinleştirirse sıfırlar.</summary>
        public void StartTotalCountdownTimer()
        {
            timeGeneral.SetStartedMinuteTime();
            TimerGame.StartTotalCountdownDisplay(TimerGame.GAME_STOP_TIME);
            StartTotalCountdownWatcher();
        }

        /// <summary>
        /// Botun ana döngüsünden bağımsız olarak toplam süreyi izler. Böylece balık
        /// aktiflik süresinin veya başka bir oyun işleminin bitmesi beklenmez.
        /// </summary>
        private void StartTotalCountdownWatcher()
        {
            if (!isBotRunActive)
            {
                return;
            }

            lock (totalCountdownWatcherLock)
            {
                if (totalCountdownWatcher == null)
                {
                    int generation = Volatile.Read(ref totalCountdownGeneration);
                    totalCountdownWatcher = new System.Threading.Timer(
                        TotalCountdownWatcherTick, generation, 200, 200);
                }
            }
        }

        private void StopTotalCountdownWatcher()
        {
            Interlocked.Increment(ref totalCountdownGeneration);

            System.Threading.Timer watcher;
            lock (totalCountdownWatcherLock)
            {
                watcher = totalCountdownWatcher;
                totalCountdownWatcher = null;
            }

            if (watcher != null)
            {
                watcher.Dispose();
            }
        }

        private void TotalCountdownWatcherTick(object state)
        {
            int generation = (int)state;
            if (!isBotRunActive || ThreadGlobals.isBotPaused || !ThreadGlobals.isTimerBreakEnabled ||
                generation != Volatile.Read(ref totalCountdownGeneration) ||
                !TimerGame.IsTotalCountdownExpired())
            {
                return;
            }

            HandleTotalTimeExpired(generation);
        }

        private void HandleTotalTimeExpired(int generation)
        {
            if (ThreadGlobals.isBotPaused || !isBotRunActive ||
                generation != Volatile.Read(ref totalCountdownGeneration) ||
                Interlocked.CompareExchange(ref totalTimeExpiryHandled, 1, 0) != 0)
            {
                return;
            }

            FileLogger.Info("Toplam süre doldu; tüm bot modları durduruluyor");
            HandleFormElement(MainForm.labelCopyStartStatus, "Toplam süre doldu; bot durduruldu");
            HandleFormElement(MainForm.labelCopyLevelFarmStatus, "Toplam süre doldu; bot durduruldu");
            HandleFormElement(MainForm.labelCopyEnergyCristalStatus, "Toplam süre doldu; bot durduruldu");

            // Modları önce durdur; aktiflik döngüsü devam ediyor olsa bile yeni işlem
            // başlatılmasın. Oyun içi çıkış bundan sonra denenir.
            ThreadGlobals.isFishingStopped = true;
            ThreadGlobals.isLevelFarmStopped = true;
            ThreadGlobals.isEnergyCristalStopped = true;
            bool closeApplication = MainForm.CloseApplicationAfterTotalTime;
            Stop();

            // Oyun içi çıkış mevcut davranış olarak korunur; hata botun durmasını engellemez.
            try
            {
                charThings.SettingButtonClick(SettingButtonPrefers.EXIT_BUTTON);
            }
            catch (Exception ex)
            {
                FileLogger.Warning("Süre sonunda oyun içi çıkış başarısız: " + ex.Message);
            }

            if (closeApplication)
            {
                FileLogger.Info("Süre sonu seçeneği etkin; uygulama hard olarak kapatılıyor");
                Environment.Exit(0);
            }
        }

        /// <summary>
        /// Bir thread gövdesini çalıştırır; beklenmeyen hata olursa loglar, arayüzü
        /// bilgilendirir ve başlat/durdur butonlarını yeniden kullanılabilir yapar.
        /// </summary>
        private void RunThreadSafely(string threadName, Action body)
        {
            try
            {
                body();
            }
            catch (ThreadAbortException)
            {
                FileLogger.Warning("'" + threadName + "' thread'i dışarıdan sonlandırıldı");
            }
            catch (Exception ex)
            {
                FileLogger.ErrorForThread(threadName, ex);
                isBotRunActive = false;
                StopTotalCountdownWatcher();

                // Kullanıcıyı bilgilendir ve botu durmuş say.
                ThreadGlobals.isFishingStopped = true;
                ThreadGlobals.isLevelFarmStopped = true;
                ThreadGlobals.isEnergyCristalStopped = true;
                ThreadGlobals.IsThreadOneActive = false;
                ThreadGlobals.IsThreadTwoActive = false;
                ThreadGlobals.IsThreadThreeActive = false;
                ThreadGlobals.isFishingStopped = true;
                ThreadGlobals.isLevelFarmStopped = true;
                ThreadGlobals.isEnergyCristalStopped = true;
                TimerGame.ResetCountdownDisplay();
                AutoHunter.IS_AUTO_HUNTER_STARTED = false;

                string message = "Bot beklenmeyen bir hata nedeniyle durdu. Ayrıntı: Logs klasöründeki günlük dosyası.";
                HandleFormElement(MainForm.labelCopyStartStatus, message);
                HandleFormElement(MainForm.labelCopyLevelFarmStatus, message);
                HandleFormElement(MainForm.labelCopyEnergyCristalStatus, message);
                HandleFormElement(MainForm.buttonFishingStartCopy, "BAŞLAT", true);
                HandleFormElement(MainForm.buttonLevelFarmStartCopy, "BAŞLAT", true);
                HandleFormElement(MainForm.buttonEnergyStartCopy, "BAŞLAT", true);

                try
                {
                    TelegramBot.SendMessageTelegram(message + " Hata: " + FileLogger.Describe(ex));
                }
                catch (Exception telegramEx)
                {
                    FileLogger.Error("Hata bildirimi Telegram'a gönderilemedi", telegramEx);
                }
            }
        }

        private void ThreadOneBody()
        {
            HandleFormElement(MainForm.buttonFishingStartCopy, string.Empty, false);
            MakeBackCounting();
            HandleFormElement(MainForm.buttonFishingStartCopy, string.Empty, true);

            FileLogger.Info("Zamanlayıcı ayarları -> min çalışma: " + TimerGame.MIN_WORK_TIME +
                ", max çalışma: " + TimerGame.MAX_WORK_TIME +
                ", min mola: " + TimerGame.MIN_BREAK_TIME +
                ", max mola: " + TimerGame.MAX_BREAK_TIME +
                ", oyunu durdurma: " + TimerGame.GAME_STOP_TIME + " (dakika)");

            try
            {
                if (ThreadGlobals.isSettingButtonSeemed && !ThreadGlobals.CheckGameIsStopped())
                {
                    charInfo.ProvideCharNameCanSee();
                }

                while (ThreadGlobals.IsThreadOneActive)
                {
                    if (ThreadGlobals.isBotPaused)
                    {
                        Thread.Sleep(IDLE_SLEEP_MILLISECONDS);
                        continue;
                    }

                    // T1 hızlı bir ek kontrol yapar; asıl süre denetçisi ayrı watcher'dır.
                    if (ThreadGlobals.isTimerBreakEnabled && TimerGame.IsTotalCountdownExpired())
                    {
                        HandleTotalTimeExpired(Volatile.Read(ref totalCountdownGeneration));
                        return;
                    }

                    if (!ThreadGlobals.isFishingStopped)
                    {
                        fishing.StartFishing();
                    }
                    else if (!ThreadGlobals.isLevelFarmStopped)
                    {
                        levelHandle.StartLevelAndFarming();
                    }
                    else if (!ThreadGlobals.isEnergyCristalStopped)
                    {
                        enerjyCrisHandle.StartEnergyCristal();
                    }
                    else
                    {
                        // Hiçbir mod açık değil: boşuna CPU harcamamak için kısa uyku.
                        Thread.Sleep(IDLE_SLEEP_MILLISECONDS);
                    }
                }
            }
            finally
            {
                ThreadGlobals.IsThreadOneActive = false;
                FileLogger.Info(T1Name + " sonlandı");
            }
        }

        private void ThreadTwoBody()
        {
            TimerGame.SleepRandom(1500, 2000);

            try
            {
                while (ThreadGlobals.IsThreadTwoActive)
                {
                    if (!ThreadGlobals.isBotPaused)
                    {
                        gameStatus.StartCheckingGame();
                    }

                    // Boş döngü yerine kısa uyku: CPU kullanımını düşürür, durdurma
                    // isteğinin fark edilmesini sağlar.
                    Thread.Sleep(THREAD_POLL_INTERVAL_MILLISECONDS);
                }
            }
            finally
            {
                ThreadGlobals.IsThreadTwoActive = false;
                FileLogger.Info(T2Name + " sonlandı");
            }
        }

        private void ThreadThreeBody()
        {
            Thread.Sleep(3100);

            try
            {
                while (ThreadGlobals.IsThreadThreeActive)
                {
                    if (ThreadGlobals.isBotPaused)
                    {
                        Thread.Sleep(THREAD_POLL_INTERVAL_MILLISECONDS);
                        continue;
                    }

                    if (!ThreadGlobals.isFishingStopped)
                    {
                        HandleFishingSideTasks();
                    }
                    else if (!ThreadGlobals.isLevelFarmStopped)
                    {
                        HandleLevelFarmSideTasks();
                    }
                    else if (!ThreadGlobals.isEnergyCristalStopped && !ThreadGlobals.isEnemyDetected)
                    {
                        HandleEnergySideTasks();
                    }

                    Thread.Sleep(THREAD_POLL_INTERVAL_MILLISECONDS);
                }
            }
            finally
            {
                ThreadGlobals.IsThreadThreeActive = false;
                FileLogger.Info(T3Name + " sonlandı");
            }
        }

        /// <summary>Balık tutarken gelen ticaret tekliflerini ve sohbet/fısıltı cevaplarını yönetir.</summary>
        private void HandleFishingSideTasks()
        {
            if (ThreadGlobals.isBotPaused || !ThreadGlobals.isSettingButtonSeemed)
            {
                return;
            }

            if (charThings.CheckTradePanelActive())
            {
                TimerGame.SleepRandom(2000, 4000);
                if (ThreadGlobals.isBotPaused) return;
                charThings.CloseTradePanel();
                chatting.ChatForTrade();
            }

            if (ThreadGlobals.isPrepareFishingStarted)
            {
                return;
            }

            if (ThreadGlobals.isBotPaused) return;

            if (ThreadGlobals.isWhisperAnswerActive && chatting.whispers.CheckHasAnyWhisper())
            {
                chatting.whispers.StartWhisperHandling();
            }

            if (!ThreadGlobals.isBotPaused && ThreadGlobals.isChattingAnswerActive &&
                (playersHandle.DetectAnotherPlayersMiniMap() || playersHandle.DetectAnotherPlayer()))
            {
                chatting.ChatStart();
            }
        }

        /// <summary>Level/farm sırasında statü dağıtımı, ticaret paneli ve ETP toplamayı yönetir.</summary>
        private void HandleLevelFarmSideTasks()
        {
            if (ThreadGlobals.isBotPaused || !ThreadGlobals.isSettingButtonSeemed)
            {
                return;
            }

            if (!timerLevelFarmBot.CheckDelayTimeInSecond(3))
            {
                if (charThings.CheckTradePanelActive())
                {
                    DebugPfCnsl.println("Birisi ticaret teklifi gönderdi, panel kapatılıyor");
                    TimerGame.SleepRandom(2000, 4000);
                    if (ThreadGlobals.isBotPaused) return;
                    charThings.CloseTradePanel();
                }

                if (ThreadGlobals.isBotPaused) return;
                if (statusHandler.CheckStatusImproveTitle())
                {
                    statusHandler.StartStatusHandle();
                }

                if (ThreadGlobals.isETPPickUpActive)
                {
                    timerLevelFarmBot.SetStartedSecondTime();
                }
            }

            if (!ThreadGlobals.isBotPaused)
            {
                charPickUp.PickUpWantedItem("ejderha taşı");
            }
        }

        /// <summary>Enerji kristali döngüsünde gelen ticaret tekliflerini kapatır.</summary>
        private void HandleEnergySideTasks()
        {
            if (ThreadGlobals.isBotPaused) return;
            if (!timeEnergyBot.CheckDelayTimeInSecond(3))
            {
                if (charThings.CheckTradePanelActive())
                {
                    DebugPfCnsl.println("Birisi ticaret teklifi gönderdi, panel kapatılıyor");
                    TimerGame.SleepRandom(700, 1400);
                    if (ThreadGlobals.isBotPaused) return;
                    charThings.CloseTradePanel();
                }
                timeEnergyBot.SetStartedSecondTime();
            }
        }

        /// <summary>
        /// Botu durdurur: tüm ortak bayrakları sıfırlar ve thread'lerin bitmesini
        /// sınırlı bir süre bekler.
        /// </summary>
        /// <remarks>
        /// Eski sürüm <c>while (IsThreadOneActive || ...) { }</c> ile boş dönüyordu
        /// (bir çekirdek %100) ve T1 uzun bir mola uykusundaysa sonsuza kadar
        /// bekliyordu. Artık aralıklı yoklama + azami bekleme süresi var.
        /// </remarks>
        public void Stop()
        {
            FileLogger.Info("ThreadsHandler.Stop çağrıldı");

            isBotRunActive = false;
            StopTotalCountdownWatcher();
            TimerGame.ResetCountdownDisplay();
            ThreadGlobals.isFishingStopped = true;
            ThreadGlobals.isLevelFarmStopped = true;
            ThreadGlobals.isEnergyCristalStopped = true;
            ThreadGlobals.SetDefaultGloabalValues();

            HandleFormElement(MainForm.buttonFishingStartCopy, "BAŞLAT", false);
            HandleFormElement(MainForm.buttonLevelFarmStartCopy, "BAŞLAT", false);
            HandleFormElement(MainForm.buttonEnergyStartCopy, "BAŞLAT", false);

            Thread waiter = new Thread(() =>
            {
                int waited = 0;
                while (ThreadGlobals.IsAnyThreadActive() && waited < STOP_WAIT_TIMEOUT_MILLISECONDS)
                {
                    Thread.Sleep(THREAD_POLL_INTERVAL_MILLISECONDS);
                    waited += THREAD_POLL_INTERVAL_MILLISECONDS;
                }

                if (ThreadGlobals.IsAnyThreadActive())
                {
                    FileLogger.Warning("Thread'ler " + STOP_WAIT_TIMEOUT_MILLISECONDS +
                        " ms içinde sonlanmadı; arayüz yine de serbest bırakılıyor. " +
                        "(T1: " + ThreadGlobals.IsThreadOneActive +
                        ", T2: " + ThreadGlobals.IsThreadTwoActive +
                        ", T3: " + ThreadGlobals.IsThreadThreeActive + ")");
                    ThreadGlobals.IsThreadOneActive = false;
                    ThreadGlobals.IsThreadTwoActive = false;
                    ThreadGlobals.IsThreadThreeActive = false;
                }

                HandleFormElement(MainForm.buttonFishingStartCopy, "BAŞLAT", true);
                HandleFormElement(MainForm.buttonLevelFarmStartCopy, "BAŞLAT", true);
                HandleFormElement(MainForm.buttonEnergyStartCopy, "BAŞLAT", true);
            });
            waiter.Name = "Stop Waiter";
            waiter.IsBackground = true;
            waiter.Start();
        }

        /// <summary>
        /// Arayüz bileşenini hangi thread'den olursa olsun güvenle günceller.
        /// </summary>
        /// <param name="visComponent">Label, Button, PictureBox veya TextBox</param>
        /// <param name="text">Label/TextBox metni veya Button için isteğe bağlı başlık</param>
        /// <param name="state">Button için Enabled değeri</param>
        /// <param name="bitmapClipped">PictureBox için yeni görsel</param>
        public void HandleFormElement(Control visComponent, string text = "", bool state = false, Bitmap bitmapClipped = null)
        {
            if (visComponent == null)
            {
                return;
            }

            try
            {
                // Form kapanmış/kontrol dispose edilmiş olabilir; bu durumda hiçbir şey yapma.
                if (visComponent.IsDisposed || !visComponent.IsHandleCreated)
                {
                    return;
                }

                Action action = () => ApplyFormElement(visComponent, text, state, bitmapClipped);

                if (visComponent.InvokeRequired)
                {
                    // BeginInvoke, UI thread'i beklemez; kapanış sırasında Invoke'un
                    // yol açabileceği kilitlenmeyi (deadlock) önler.
                    visComponent.BeginInvoke(action);
                }
                else
                {
                    action();
                }
            }
            catch (ObjectDisposedException)
            {
                // Form kapanırken normal bir durumdur.
            }
            catch (InvalidOperationException ex)
            {
                // Handle henüz yaratılmamış olabilir.
                FileLogger.Warning("Arayüz bileşeni güncellenemedi: " + ex.Message);
            }
            catch (Exception ex)
            {
                FileLogger.Error("HandleFormElement beklenmeyen hata", ex);
            }
        }

        private static void ApplyFormElement(Control visComponent, string text, bool state, Bitmap bitmapClipped)
        {
            if (visComponent is Label)
            {
                visComponent.Text = text;
            }
            else if (visComponent is Button)
            {
                if (!string.IsNullOrEmpty(text))
                {
                    visComponent.Text = text;
                }
                visComponent.Enabled = state;
            }
            else if (visComponent is PictureBox box)
            {
                if (bitmapClipped != null)
                {
                    // Önceki görsel serbest bırakılmazsa her güncellemede birikir.
                    Image previousImage = box.Image;
                    box.Image = bitmapClipped;
                    if (previousImage != null && !ReferenceEquals(previousImage, bitmapClipped))
                    {
                        previousImage.Dispose();
                    }
                }
            }
            else if (visComponent is TextBox textBox)
            {
                textBox.Text = text;
            }
        }

        /// <summary>
        /// Seçilen moda göre 3-2-1 geri sayımı yapar ve arayüzü bilgilendirir.
        /// </summary>
        private void MakeBackCounting()
        {
            Label statusLabel;
            string botName;

            if (!ThreadGlobals.isFishingStopped)
            {
                statusLabel = MainForm.labelCopyStartStatus;
                botName = "Balık botu";
            }
            else if (!ThreadGlobals.isLevelFarmStopped)
            {
                statusLabel = MainForm.labelCopyLevelFarmStatus;
                botName = "Level/Farm botu";
            }
            else if (!ThreadGlobals.isEnergyCristalStopped)
            {
                statusLabel = MainForm.labelCopyEnergyCristalStatus;
                // HATA DÜZELTİLDİ: enerji kristali modunda yanlışlıkla
                // "Level Farm Bot is starting" yazılıyordu.
                botName = "Enerji kristali botu";
            }
            else
            {
                return;
            }

            for (int remaining = 3; remaining > 0; remaining--)
            {
                if (ThreadGlobals.CheckGameIsStopped())
                {
                    return;
                }
                HandleFormElement(statusLabel, botName + " " + remaining + " saniye içinde başlıyor");
                TimerGame.SleepActiveTime(1000);
            }

            HandleFormElement(statusLabel, botName + " başlatıldı");
            FileLogger.Info(botName + " başlatıldı");
        }
    }
}
