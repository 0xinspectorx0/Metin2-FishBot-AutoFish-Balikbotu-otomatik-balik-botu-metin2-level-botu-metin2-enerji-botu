using Metin2AutoFishCSharp;
using Metin2AutoFishCSharp.Sources;
using Metin2AutoFishCSharp.Sources.ChatHandler;
using MusicPlayerApp.Debugs;
using MusicPlayerApp.Sources;
using MusicPlayerApp.Sources.GameHandler;
using MusicPlayerApp.Sources.ImageHandle;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;


namespace MusicPlayerApp
{
    public partial class MainForm : Form
    {
        public static Label labelCopyStartStatus;
        public static Label labelCopyLevelFarmStatus;
        public static Label labelCopyEnergyCristalStatus;
        public static Button buttonFishingStartCopy;
        public static Button buttonLevelFarmStartCopy;
        public static Button buttonEnergyStartCopy;
        public static volatile bool CloseApplicationAfterTotalTime;
        
        private ImageObjects imageObjects;
        private ThreadsHandler threadsHandler;
        private GameObjectCoordinates coor;

        private StatusStrip timerCountdownStatusStrip;
        private ToolStripStatusLabel labelTotalCountdown;
        private ToolStripStatusLabel labelActiveCountdown;
        private ToolStripStatusLabel labelBreakCountdown;
        private System.Windows.Forms.Timer timerCountdownRefresh;

        private TelegramBot telegramBot;
        /// <summary>checkBoxTelegram.Checked programatik olarak değiştirilirken olayın
        /// yeniden tetiklenmesini engeller.</summary>
        private bool isTelegramCheckChanging;
        private volatile bool isManualGrillActionActive;
        private volatile bool isManualWormActionActive;
        private volatile bool isAutomaticInventoryGrillRecoveryActive;
        private volatile bool isAutomaticInventoryGrillRecoveryCancelled;
        private int inventoryFullGrillRecoveryRequested;
        //ChatHandlerForm chatHandlerForm;



        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
       

        private const int MY_HOTKEY_ID = 1;
        private const int MY_PAUSE_HOTKEY_ID = 2;
        private const uint MOD_CONTROL = 0x0002; // Ctrl tuşu için modifiyer
        private const uint MOD_NOREPEAT = 0x4000; // Basılı tutarken tekrar tetiklemeyi önler
        private const uint VK_O = 0x4F; // O harfi için sanal tuş kodu
        private const uint VK_P = 0x50; // P harfi için sanal tuş kodu

        public MainForm()
        {
            InitializeComponent();
            Thread.CurrentThread.Name = "Main Thread";
            DebugPfCnsl.println("MainForm constructor is called");
            DebugPfCnsl.println(Thread.CurrentThread.Name);

            try
            {
                InitializeForm();
            }
            catch (Exception ex)
            {
                // Açılıştaki hatalar eskiden Windows'un anlaşılmaz "uygulama düzgün
                // başlatılamadı" penceresiyle görünüyordu. Artık neden loglanıyor ve
                // kullanıcıya Türkçe açıklanıyor.
                FileLogger.Error("Ana form başlatılamadı", ex);
                MessageBox.Show(
                    "Program başlatılamadı." + Environment.NewLine + Environment.NewLine +
                    "Hata: " + ex.Message + Environment.NewLine + Environment.NewLine +
                    "Olası nedenler:" + Environment.NewLine +
                    "  • Program klasöründeki Images, Fishes veya ChatResources dosyaları eksik." + Environment.NewLine +
                    "  • Metin2 açık değil ya da ekran çözünürlüğü/ölçeklendirme (DPI) farklı." + Environment.NewLine +
                    "  • Program yönetici yetkisi olmadan çalıştırıldı." + Environment.NewLine + Environment.NewLine +
                    "Ayrıntılar günlük dosyasında: " + FileLogger.LogDirectory,
                    "Başlatma Hatası", MessageBoxButtons.OK, MessageBoxIcon.Error);
                throw;
            }

            InitializeToolTip();
        }

        /// <summary>
        /// Formun çalışma zamanı bağımlılıklarını (görüntü işleme, iş parçacıkları,
        /// koordinatlar ve sıcak tuş) hazırlar.
        /// </summary>
        /// <remarks>
        /// Kurucudan ayrıldı; böylece açılış hatası tek bir <c>try/catch</c> içinde
        /// yakalanıp kullanıcıya anlamlı bir mesaj gösterilebiliyor.
        /// </remarks>
        private void InitializeForm()
        {
            labelCopyStartStatus = labelStartStatus;
            labelCopyLevelFarmStatus = labelLevelFarmStatus;
            labelCopyEnergyCristalStatus = labelEnergyCristal;
            buttonFishingStartCopy = buttonFishingStart;
            buttonLevelFarmStartCopy = buttonLevelStart;
            buttonEnergyStartCopy = buttonEnergyStart;
            // Referans PNG'ler yalnızca bir kez yüklenir (singleton). Eskiden burada
            // `new ImageObjects()` çağrılıyordu; bu, ~70 PNG'yi ikinci kez okuyup her
            // biri için dispose edilmeyen Bitmap yaratıyordu (GDI nesne sızıntısı).
            imageObjects = ImageObjects.Instance;
            threadsHandler = new ThreadsHandler(imageObjects);
            threadsHandler.InventoryFullWarningDetected += HandleInventoryFullWarningDetected;
            coor = new GameObjectCoordinates(imageObjects);
            // Ctrl+O tamamen durdurur; Ctrl+P duraklatıp/devam ettirir.
            RegisterHotKey(this.Handle, MY_HOTKEY_ID, MOD_CONTROL | MOD_NOREPEAT, VK_O);
            if (!RegisterHotKey(this.Handle, MY_PAUSE_HOTKEY_ID, MOD_CONTROL | MOD_NOREPEAT, VK_P))
            {
                FileLogger.Warning("Ctrl+P duraklatma kısayolu kaydedilemedi; başka bir uygulama kullanıyor olabilir");
            }
            // chatHandlerForm = new ChatHandlerForm();



            LoadCheckBoxes();
            EnableOrDisableTimerCheckBox(false);
            InitializeTimerCountdownDisplay();
            // Surum denetimi arka planda calisir; form acilisini bloklamaz.
            // (VersionChecker artik MusicPlayerApp.Sources ad alaninda.)
            MusicPlayerApp.Sources.VersionChecker.CheckForUpdate();
        }

        private void HandleInventoryFullWarningDetected()
        {
            if (Interlocked.CompareExchange(ref inventoryFullGrillRecoveryRequested, 1, 0) != 0) return;

            // FishingHandle çağrısından hemen dönülebilmesi için durdurma isteğini burada koy;
            // gerçek düğme tıklaması tüm bot thread'leri kapandıktan sonra yapılır.
            ThreadGlobals.isFishingStopped = true;
            FileLogger.Info("Envanter dolu uyarısı: bot durdurulup Balıkları Pişir düğmesine geçiliyor");

            Task.Run(() =>
            {
                bool stopped = false;
                try
                {
                    stopped = threadsHandler.StopAndWait(30000);
                }
                catch (Exception ex)
                {
                    FileLogger.Error("Envanter uyarısı sonrası bot durdurulamadı", ex);
                }

                if (!stopped)
                {
                    DispatchInventoryGrillStatus("Bot thread'leri güvenle durmadı; otomatik pişirme başlatılmadı.");
                    Interlocked.Exchange(ref inventoryFullGrillRecoveryRequested, 0);
                    return;
                }

                if (IsDisposed || !IsHandleCreated)
                {
                    Interlocked.Exchange(ref inventoryFullGrillRecoveryRequested, 0);
                    return;
                }

                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        tabControlTelegram.SelectedTab = tabPageFishing;
                        isAutomaticInventoryGrillRecoveryActive = true;
                        isAutomaticInventoryGrillRecoveryCancelled = false;
                        buttonGrillFish.Enabled = true;
                        buttonGrillFish.PerformClick();

                        if (!isManualGrillActionActive)
                        {
                            isAutomaticInventoryGrillRecoveryActive = false;
                            Interlocked.Exchange(ref inventoryFullGrillRecoveryRequested, 0);
                            labelStartStatus.Text = "Balıkları Pişir düğmesi otomatik başlatılamadı; balık botu durduruldu.";
                        }
                    }));
                }
                catch (InvalidOperationException ex)
                {
                    FileLogger.Error("Balıkları Pişir bölümü açılamadı", ex);
                    Interlocked.Exchange(ref inventoryFullGrillRecoveryRequested, 0);
                }
            });
        }

        private void DispatchInventoryGrillStatus(string message)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke((Action)(() => labelStartStatus.Text = message));
            }
            catch (InvalidOperationException ex)
            {
                FileLogger.Error("Envanter pişirme durumu arayüzde gösterilemedi", ex);
            }
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_HOTKEY = 0x0312;
            if (m.Msg == WM_HOTKEY)
            {
                int hotkeyId = m.WParam.ToInt32();
                if (hotkeyId == MY_HOTKEY_ID)
                {
                    StopBotFromHotkey();
                }
                else if (hotkeyId == MY_PAUSE_HOTKEY_ID)
                {
                    ToggleBotPauseFromHotkey();
                }
            }

            base.WndProc(ref m);
        }

        private void StopBotFromHotkey()
        {
            if ((isManualGrillActionActive || isManualWormActionActive) && !ThreadGlobals.isBotPaused)
            {
                if (isAutomaticInventoryGrillRecoveryActive)
                {
                    isAutomaticInventoryGrillRecoveryCancelled = true;
                }
                ThreadGlobals.isFishingStopped = true;
                labelStartStatus.Text = isManualWormActionActive
                    ? "Solucan hazırlama Ctrl+O ile durduruldu"
                    : "Ctrl+O ile durduruldu";
                threadsHandler.Stop();
                return;
            }

            if (ThreadGlobals.isBotPaused)
            {
                if (isAutomaticInventoryGrillRecoveryActive)
                {
                    isAutomaticInventoryGrillRecoveryCancelled = true;
                }
                bool wasFishingActive = ThreadGlobals.WasFishingActiveBeforePause;
                bool wasLevelFarmActive = ThreadGlobals.WasLevelFarmActiveBeforePause;
                bool wasEnergyActive = ThreadGlobals.WasEnergyActiveBeforePause;
                ThreadGlobals.isFishingStopped = true;
                ThreadGlobals.isLevelFarmStopped = true;
                ThreadGlobals.isEnergyCristalStopped = true;
                if (isManualWormActionActive) labelStartStatus.Text = "Solucan hazırlama Ctrl+O ile durduruldu";
                else if (isManualGrillActionActive || wasFishingActive) labelStartStatus.Text = "Ctrl+O ile durduruldu";
                if (wasLevelFarmActive) labelLevelFarmStatus.Text = "Ctrl+O ile durduruldu";
                if (wasEnergyActive) labelEnergyCristal.Text = "Ctrl+O ile durduruldu";
                threadsHandler.Stop();
                return;
            }

            if (!ThreadGlobals.isFishingStopped)
            {
                buttonFishingStart.PerformClick();
                labelStartStatus.Text = "Ctrl+O ile durduruldu";
            }
            if (!ThreadGlobals.isLevelFarmStopped)
            {
                buttonLevelStart.PerformClick();
                labelLevelFarmStatus.Text = "Ctrl+O ile durduruldu";
            }
            if (!ThreadGlobals.isEnergyCristalStopped)
            {
                buttonEnergyStart.PerformClick();
                labelEnergyCristal.Text = "Ctrl+O ile durduruldu";
            }
        }

        private void ToggleBotPauseFromHotkey()
        {
            if (ThreadGlobals.isBotPaused)
            {
                bool resumeFishing = ThreadGlobals.WasFishingActiveBeforePause;
                bool resumeLevelFarm = ThreadGlobals.WasLevelFarmActiveBeforePause;
                bool resumeEnergy = ThreadGlobals.WasEnergyActiveBeforePause;
                if (!ThreadGlobals.ResumeBot()) return;

                TimerGame.ResumeBotTimers();
                bool canStartOtherActions = !isManualGrillActionActive && !isManualWormActionActive;
                buttonFishingStart.Enabled = canStartOtherActions;
                buttonLevelStart.Enabled = canStartOtherActions;
                buttonEnergyStart.Enabled = canStartOtherActions;
                buttonResetSettings.Enabled = canStartOtherActions;
                buttonGrillFish.Enabled = canStartOtherActions;
                buttonPrepareWorms.Enabled = canStartOtherActions;
                if (isManualGrillActionActive)
                {
                    labelStartStatus.Text = "Ctrl+P ile pişirmeye devam edildi";
                }
                else if (isManualWormActionActive)
                {
                    labelStartStatus.Text = "Solucan hazırlama Ctrl+P ile devam etti";
                }
                else if (resumeFishing)
                {
                    buttonFishingStart.Text = "DURDUR";
                    labelStartStatus.Text = "Ctrl+P ile devam edildi";
                }
                if (resumeLevelFarm)
                {
                    buttonLevelStart.Text = "DURDUR";
                    labelLevelFarmStatus.Text = "Ctrl+P ile devam edildi";
                }
                if (resumeEnergy)
                {
                    buttonEnergyStart.Text = "DURDUR";
                    labelEnergyCristal.Text = "Ctrl+P ile devam edildi";
                }
                FileLogger.Info("Bot Ctrl+P ile devam ettirildi");
                return;
            }

            if (!ThreadGlobals.PauseBot()) return;

            TimerGame.PauseBotTimers();
            buttonFishingStart.Enabled = false;
            buttonLevelStart.Enabled = false;
            buttonEnergyStart.Enabled = false;
            buttonResetSettings.Enabled = false;
            buttonGrillFish.Enabled = false;
            buttonPrepareWorms.Enabled = false;
            if (isManualGrillActionActive) labelStartStatus.Text = "Pişirme Ctrl+P ile duraklatıldı";
            else if (isManualWormActionActive) labelStartStatus.Text = "Solucan hazırlama Ctrl+P ile duraklatıldı";
            else if (ThreadGlobals.WasFishingActiveBeforePause) labelStartStatus.Text = "Ctrl+P ile duraklatıldı";
            if (ThreadGlobals.WasLevelFarmActiveBeforePause) labelLevelFarmStatus.Text = "Ctrl+P ile duraklatıldı";
            if (ThreadGlobals.WasEnergyActiveBeforePause) labelEnergyCristal.Text = "Ctrl+P ile duraklatıldı";
            FileLogger.Info("Bot Ctrl+P ile duraklatıldı");
        }


        private void buttonFishingStartClick(object sender, EventArgs e)
        {
            // DebugPfCnsl.println("value " + tabPageFishing.CanFocus);
            if (ThreadGlobals.isLevelFarmStopped)
            {
                if (ThreadGlobals.isEnergyCristalStopped)
                {
                    if (ThreadGlobals.isFishingStopped)
                    {
                        ThreadGlobals.isFishingStopped = false;
                        threadsHandler.Start();
                        buttonFishingStartCopy.Text = "DURDUR";

                    }
                    else
                    {
                        ThreadGlobals.isFishingStopped = true;
                        threadsHandler.Stop();
                        threadsHandler.HandleFormElement(labelCopyStartStatus, "Balık botu durduruldu");
                        buttonFishingStartCopy.Text = "BAŞLAT";
                    }
                }
                else
                {
                    MessageBox.Show("Level Kasma Aktifken Balıkçılığı Başlatamazsın", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            else
            {
                MessageBox.Show("Enerji Kristali Aktifken Balıkçılığı Başlatamazsın", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);

            }
        }

        private void buttonGrillFish_Click(object sender, EventArgs e)
        {
            if (isManualGrillActionActive || isManualWormActionActive || ThreadGlobals.isBotPaused ||
                !ThreadGlobals.isFishingStopped || !ThreadGlobals.isLevelFarmStopped ||
                !ThreadGlobals.isEnergyCristalStopped || ThreadGlobals.IsAnyThreadActive())
            {
                MessageBox.Show("Balıkları pişirmek için önce tüm bot işlemlerini durdurun.",
                    "İşlem başlatılamadı", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            isManualGrillActionActive = true;
            ThreadGlobals.isFishingStopped = false;
            buttonFishingStart.Enabled = false;
            buttonLevelStart.Enabled = false;
            buttonEnergyStart.Enabled = false;
            buttonResetSettings.Enabled = false;
            buttonGrillFish.Enabled = false;
            buttonPrepareWorms.Enabled = false;
            labelStartStatus.Text = "Pişirme 5 saniye içinde başlayacak… (Ctrl+O ile durdurabilirsiniz)";

            Task.Run(() =>
            {
                string result = "Balık pişirme işlemi tamamlanamadı.";
                bool grillCompleted = false;
                bool resumeFishingAfterGrill = false;
                bool automaticInventoryRecovery = isAutomaticInventoryGrillRecoveryActive;
                try
                {
                    // Başlangıç beklemesi duraklatmaya duyarlıdır; bu sırada Ctrl+O
                    // gelirse oyun üzerinde hiçbir işlem yapılmadan görev sonlandırılır.
                    TimerGame.SleepActiveTime(5000);
                    if (ThreadGlobals.isFishingStopped)
                    {
                        result = "Balık pişirme işlemi durduruldu.";
                    }
                    else
                    {
                        PrepareFishing prepareFishing = new PrepareFishing(imageObjects);
                        grillCompleted = prepareFishing.GrillAllFishOnly(out result);
                        if (automaticInventoryRecovery && grillCompleted &&
                            !isAutomaticInventoryGrillRecoveryCancelled)
                        {
                            ThreadGlobals.isFishingStopped = false;
                            prepareFishing.GoToFishPlace();
                            resumeFishingAfterGrill = !ThreadGlobals.isFishingStopped &&
                                !isAutomaticInventoryGrillRecoveryCancelled;
                        }
                    }
                }
                catch (Exception ex)
                {
                    FileLogger.Error("Tek seferlik balık pişirme işlemi başlatılamadı", ex);
                    result = "Balık pişirme hatası: " + ex.Message;
                }
                finally
                {
                    ThreadGlobals.isFishingStopped = true;
                    ThreadGlobals.isPrepareFishingStarted = false;
                }

                try
                {
                    if (!IsDisposed && IsHandleCreated)
                    {
                        BeginInvoke((Action)(() =>
                        {
                            isManualGrillActionActive = false;
                            buttonFishingStart.Text = "BAŞLAT";
                            buttonFishingStart.Enabled = true;
                            buttonLevelStart.Enabled = true;
                            buttonEnergyStart.Enabled = true;
                            buttonResetSettings.Enabled = true;
                            buttonGrillFish.Enabled = true;
                            buttonPrepareWorms.Enabled = true;
                            labelStartStatus.Text = result;
                            if (automaticInventoryRecovery)
                            {
                                isAutomaticInventoryGrillRecoveryActive = false;
                                if (resumeFishingAfterGrill && !isAutomaticInventoryGrillRecoveryCancelled)
                                {
                                    try
                                    {
                                        ThreadGlobals.isFishingStopped = false;
                                        threadsHandler.Start();
                                        buttonFishingStart.Text = "DURDUR";
                                        labelStartStatus.Text = "Pişirme tamamlandı; balık tutma devam ediyor.";
                                    }
                                    catch (Exception ex)
                                    {
                                        ThreadGlobals.isFishingStopped = true;
                                        FileLogger.Error("Pişirme sonrası balık botu yeniden başlatılamadı", ex);
                                        labelStartStatus.Text = "Pişirme tamamlandı ancak balık botu başlatılamadı: " + ex.Message;
                                    }
                                }
                                else
                                {
                                    ThreadGlobals.isFishingStopped = true;
                                    labelStartStatus.Text = result + " Balık botu otomatik olarak yeniden başlatılmadı.";
                                }
                                isAutomaticInventoryGrillRecoveryCancelled = false;
                                Interlocked.Exchange(ref inventoryFullGrillRecoveryRequested, 0);
                            }
                        }));
                    }
                    else
                    {
                        isManualGrillActionActive = false;
                    }
                }
                catch (InvalidOperationException)
                {
                    isManualGrillActionActive = false;
                }
            });
        }

        private void buttonPrepareWorms_Click(object sender, EventArgs e)
        {
            if (isManualGrillActionActive || isManualWormActionActive || ThreadGlobals.isBotPaused ||
                !ThreadGlobals.isFishingStopped || !ThreadGlobals.isLevelFarmStopped ||
                !ThreadGlobals.isEnergyCristalStopped || ThreadGlobals.IsAnyThreadActive())
            {
                MessageBox.Show("Solucan hazırlamak için önce tüm bot işlemlerini durdurun.",
                    "İşlem başlatılamadı", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            isManualWormActionActive = true;
            ThreadGlobals.isFishingStopped = false;
            buttonFishingStart.Enabled = false;
            buttonLevelStart.Enabled = false;
            buttonEnergyStart.Enabled = false;
            buttonResetSettings.Enabled = false;
            buttonGrillFish.Enabled = false;
            buttonPrepareWorms.Enabled = false;
            labelStartStatus.Text = "Solucan hazırlama 5 saniye içinde başlayacak… (Ctrl+O ile durdurabilirsiniz)";

            Task.Run(() =>
            {
                string result = "Solucan hazırlama işlemi tamamlanamadı.";
                try
                {
                    TimerGame.SleepActiveTime(5000);
                    if (ThreadGlobals.isFishingStopped)
                    {
                        result = "Solucan hazırlama işlemi durduruldu.";
                    }
                    else
                    {
                        PrepareFishing prepareFishing = new PrepareFishing(imageObjects);
                        prepareFishing.PrepareWormsOnly(out result);
                    }
                }
                catch (Exception ex)
                {
                    FileLogger.Error("Bağımsız solucan hazırlama işlemi başlatılamadı", ex);
                    result = "Solucan hazırlama hatası: " + ex.Message;
                }
                finally
                {
                    ThreadGlobals.isFishingStopped = true;
                    ThreadGlobals.isPrepareFishingStarted = false;
                }

                try
                {
                    if (!IsDisposed && IsHandleCreated)
                    {
                        BeginInvoke((Action)(() =>
                        {
                            isManualWormActionActive = false;
                            buttonFishingStart.Text = "BAŞLAT";
                            buttonFishingStart.Enabled = !ThreadGlobals.isBotPaused;
                            buttonLevelStart.Enabled = !ThreadGlobals.isBotPaused;
                            buttonEnergyStart.Enabled = !ThreadGlobals.isBotPaused;
                            buttonResetSettings.Enabled = !ThreadGlobals.isBotPaused;
                            buttonGrillFish.Enabled = !ThreadGlobals.isBotPaused;
                            buttonPrepareWorms.Enabled = !ThreadGlobals.isBotPaused;
                            labelStartStatus.Text = result;
                        }));
                    }
                    else
                    {
                        isManualWormActionActive = false;
                    }
                }
                catch (InvalidOperationException)
                {
                    isManualWormActionActive = false;
                }
            });
        }

        private void buttonLevelStart_Click(object sender, EventArgs e)
        {
            if (ThreadGlobals.isFishingStopped)
            {
                if (ThreadGlobals.isEnergyCristalStopped)
                {
                    if (ThreadGlobals.isLevelFarmStopped)
                    {
                        if (ThreadGlobals.IsHpSpRateEmpty())
                        {
                            if (int.TryParse(textBoxHpRate.Text, out int value) &&
                                int.TryParse(textBoxSpRate.Text, out int spValue))
                            {
                                ThreadGlobals.SetHpSpRate(value, spValue);
                            }
                        }
                        if (ThreadGlobals.IsStatusPriorityEmpty())
                        {
                            if (int.TryParse(textBoxHp.Text, out int hpStatusPrio) && int.TryParse(textBoxSp.Text, out int spStatusPrio)
                                && int.TryParse(textBoxStr.Text, out int strStatusPrio) && int.TryParse(textBoxDex.Text, out int dexStatusPrio))
                            {
                                ThreadGlobals.SetStatusPriority(hpStatusPrio, spStatusPrio, strStatusPrio, dexStatusPrio);
                            }
                        }
                        ThreadGlobals.isLevelFarmStopped = false;
                        threadsHandler.Start();
                        buttonLevelStart.Text = "DURDUR";
                    }
                    else
                    {
                        ThreadGlobals.isLevelFarmStopped = true;
                        threadsHandler.Stop();
                        threadsHandler.HandleFormElement(labelLevelFarmStatus, "Level kasma botu durduruldu");
                        buttonLevelStart.Text = "BAŞLAT";
                    }
                }
                else
                {
                    MessageBox.Show("Balıkçılık aktifken Level Farm butonuna basamazsın", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }else
            {
                MessageBox.Show("Enerji kristali aktifken Level Farm butonuna basamazsın", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);

            }
        }

        private void buttonEnergyCristalStart_Click(object sender, EventArgs e)
        {
            if (ThreadGlobals.isFishingStopped)
            {
                if(ThreadGlobals.isLevelFarmStopped)
                {
                    if(ThreadGlobals.isEnergyCristalStopped)
                    {
                        ThreadGlobals.isEnergyCristalStopped = false;
                        threadsHandler.Start();
                        buttonEnergyStart.Text = "DURDUR";
                        threadsHandler.HandleFormElement(labelEnergyCristal, "Enerji botu başlatıldı");
                    }
                    else
                    {
                        ThreadGlobals.isEnergyCristalStopped = true;
                        threadsHandler.Stop();
                        buttonEnergyStart.Text = "BAŞLAT";
                        threadsHandler.HandleFormElement(labelEnergyCristal, "Enerji botu durduruldu");
                    }
                }
                else
                {
                    MessageBox.Show("Level ve Farm aktifken Enerji kristal Start butonuna basamazsın", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);

                }
            }
            else
            {
                MessageBox.Show("Balıkçılık aktifken Enerji kristal Start butonuna basamazsın", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            // TelegramBot artik burada OLUSTURULMUYOR: eski kod, kullanıcı Telegram'ı
            // hiç kullanmasa bile geçersiz ("Add Your Token") token ile bir istemci
            // oluşturup uygulama açılışında hata üretiyordu. Alıcı döngüsü yalnızca
            // kullanıcı Telegram'ı etkinleştirdiğinde başlatılır (bakınız: checkBoxTelegram).
            InitializeTelegramTab();
            DebugPfCnsl.println("Telegram Bot is Starting");
        }
     
   






        private void checkBoxCloseAfterTime_CheckedChanged(object sender, EventArgs e)
        {
            CloseApplicationAfterTotalTime = checkBoxCloseAfterTime.Checked;
        }

        private void buttonResetSettings_Click(object sender, EventArgs e)
        {
            // Sıfırlama bot çalışırken de istenebilir; önce tüm modları durdur.
            bool botWasRunning = !ThreadGlobals.isFishingStopped ||
                !ThreadGlobals.isLevelFarmStopped || !ThreadGlobals.isEnergyCristalStopped ||
                ThreadGlobals.IsAnyThreadActive();
            ThreadGlobals.isFishingStopped = true;
            ThreadGlobals.isLevelFarmStopped = true;
            ThreadGlobals.isEnergyCristalStopped = true;
            if (threadsHandler != null)
            {
                if (botWasRunning)
                {
                    threadsHandler.Stop();
                }
                else
                {
                    ThreadGlobals.SetDefaultGloabalValues();
                }
            }

            // Balık türleri: uygulamanın başlangıç varsayılanları.
            checkBoxYabbie.Checked = true;
            checkBoxAltinSudak.Checked = true;
            checkBoxPalamut.Checked = true;
            checkBoxKurbaga.Checked = false;
            checkBoxKadife.Checked = false;
            checkBoxDeniz.Checked = false;
            checkBoxHepsi.Checked = false;
            foreach (CheckBox fishCheckBox in new[] {
                checkBoxYabbie, checkBoxAltinSudak, checkBoxPalamut,
                checkBoxKurbaga, checkBoxKadife, checkBoxDeniz })
            {
                fishCheckBox.Enabled = true;
            }
            ThreadGlobals.isYabbieSelected = true;
            ThreadGlobals.isAltinSudakSelected = true;
            ThreadGlobals.isPalamutSelected = true;
            ThreadGlobals.isKurbagaSelected = false;
            ThreadGlobals.isKadifeSelected = false;
            ThreadGlobals.isDenizkizSelected = false;
            ThreadGlobals.isHepsiSelected = false;

            // Zamanlayıcı varsayılanı kapalıdır; değer kutuları tasarımda boştur.
            checkBoxEnableTime.Checked = false;
            checkBoxCloseAfterTime.Checked = false;
            textBoxMinWorkTime.Text = string.Empty;
            textBoxMaxWorkTime.Text = string.Empty;
            textBoxMinMaxBreak.Text = string.Empty;
            textBoxStopGameTime.Text = string.Empty;
            TimerGame.MIN_WORK_TIME = 0;
            TimerGame.MAX_WORK_TIME = 0;
            TimerGame.MIN_BREAK_TIME = 0;
            TimerGame.MAX_BREAK_TIME = 0;
            TimerGame.GAME_STOP_TIME = 0;
            ThreadGlobals.isTimerBreakEnabled = false;
            EnableOrDisableTimerCheckBox(false);
            TimerGame.ResetCountdownDisplay();

            // Diğer balıkçılık seçenekleri: kapalı.
            checkBoxFishingMiniBreak.Checked = false;
            checkBoxChatActive.Checked = false;
            checkBoxWhisperActive.Checked = false;
            checkBoxAdaptableFish.Checked = false;
            checkBoxPCSlow.Checked = false;

            // Level/Farm sekmesinin varsayılan değerleri.
            checkBoxETPPickUp.Checked = false;
            textBoxHp.Text = "4";
            textBoxSp.Text = "2";
            textBoxStr.Text = "3";
            textBoxDex.Text = "1";
            ThreadGlobals.SetStatusPriority(4, 2, 3, 1);
            trackBarHp.Value = 50;
            trackBarSp.Value = 50;
            textBoxHpRate.Text = "50";
            textBoxSpRate.Text = "50";
            ThreadGlobals.SetHpSpRate(50, 50);

            TextBox[] skillBoxes = {
                textBoxSkillOne, textBoxSkillTwo, textBoxSkillThree, textBoxSkillFour,
                textBoxSkillF1, textBoxSkillF2, textBoxSkillF3, textBoxSkillF4
            };
            for (int i = 0; i < skillBoxes.Length; i++)
            {
                skillBoxes[i].Text = "0";
                ThreadGlobals.SetSkillTime(i, 0);
            }

            // Telegram token'ı saklı tutulur; yalnızca bot varsayılan olarak kapatılır.
            checkBoxTelegram.Checked = false;
            buttonFishingStart.Text = "BAŞLAT";
            buttonLevelStart.Text = "BAŞLAT";
            buttonEnergyStart.Text = "BAŞLAT";
            labelStartStatus.Text = "Hazır";
            labelLevelFarmStatus.Text = "Bekliyor";
            labelEnergyCristal.Text = "Bekliyor";
            CloseApplicationAfterTotalTime = false;
            UpdateTimerCountdownDisplay();
        }

        private void LoadCheckBoxes()
        {
            checkBoxYabbie.Checked = true;
            checkBoxAltinSudak.Checked = true;
            checkBoxPalamut.Checked = true;
                  
        }

        private void EnableOrDisableTimerCheckBox(bool state)
        {
            if(state)
            {
                textBoxMinWorkTime.Enabled = true;
                textBoxMaxWorkTime.Enabled = true;
                textBoxStopGameTime.Enabled = true;
                textBoxMinMaxBreak.Enabled = true;
            }
            else
            {
                textBoxMinWorkTime.Enabled = false;
                textBoxMaxWorkTime.Enabled = false;
                textBoxStopGameTime.Enabled = false;
                textBoxMinMaxBreak.Enabled = false;
            }
           


        }

        private void checkBoxsFishes_Click(object sender, EventArgs e)
        {
            DebugPfCnsl.println("checkBoxsFishes_Click çalişti");
            if (checkBoxYabbie.Checked)
            {
                ThreadGlobals.isYabbieSelected = true;
            }
            else
            {
                ThreadGlobals.isYabbieSelected = false;
            }
            if (checkBoxAltinSudak.Checked)
            {
                ThreadGlobals.isAltinSudakSelected = true;
            }
            else
            {
                ThreadGlobals.isAltinSudakSelected = false;
            }
            if (checkBoxPalamut.Checked)
            {
                ThreadGlobals.isPalamutSelected = true;
            }
            else
            {
                ThreadGlobals.isPalamutSelected = false;
            }
            if (checkBoxKurbaga.Checked)
            {
                ThreadGlobals.isKurbagaSelected = true;
            }
            else
            {
                ThreadGlobals.isKurbagaSelected = false;
            }
            if (checkBoxKadife.Checked)
            {
                ThreadGlobals.isKadifeSelected = true;
            }
            else
            {
                ThreadGlobals.isKadifeSelected = false;
            }
            if (checkBoxDeniz.Checked)
            {
                ThreadGlobals.isDenizkizSelected = true;
            }
            else
            {
                ThreadGlobals.isDenizkizSelected = false;
            }
        }

        private void checkBoxsHepsi_Click(object sender, EventArgs e)
        {
            DebugPfCnsl.println("checkBoxsHepsi_Click çalişti");
            if (checkBoxHepsi.Checked) 
            {
                ThreadGlobals.isHepsiSelected = true;

                checkBoxYabbie.Checked = false;
                checkBoxAltinSudak.Checked = false;
                checkBoxPalamut.Checked = false;
                checkBoxKurbaga.Checked = false;
                checkBoxKadife.Checked = false;
                checkBoxDeniz.Checked = false;

                checkBoxYabbie.Enabled = false;
                checkBoxAltinSudak.Enabled = false;
                checkBoxKurbaga.Enabled = false;
                checkBoxKadife.Enabled = false;
                checkBoxPalamut.Enabled = false;
                checkBoxDeniz.Enabled = false;
            }
            else
            {
                ThreadGlobals.isHepsiSelected= false;

                checkBoxYabbie.Enabled = true;
                checkBoxPalamut.Enabled = true;
                checkBoxKurbaga.Enabled = true;
                checkBoxDeniz.Enabled = true;
                checkBoxKadife.Enabled = true;
                checkBoxAltinSudak.Enabled = true;
                checkBoxDeniz.Enabled = true;

                checkBoxYabbie.Checked = true;
                checkBoxAltinSudak.Checked = true;
                checkBoxPalamut.Checked = true;
                checkBoxKurbaga.Checked = false;
                checkBoxDeniz.Checked = false;
                checkBoxKadife.Checked = false;
                checkBoxDeniz.Checked = false;

                ThreadGlobals.isYabbieSelected = true;
                ThreadGlobals.isAltinSudakSelected = true;
                ThreadGlobals.isPalamutSelected = true;
                ThreadGlobals.isKurbagaSelected = false;
                ThreadGlobals.isDenizkizSelected = false;
                ThreadGlobals.isKadifeSelected = false;

            }
        }

        private void InitializeTimerCountdownDisplay()
        {
            // Alt durum çubuğu, mevcut Balık Tutma kontrollerinin üstüne binmeden
            // sayaçları tüm sekmelerde görünür tutar.
            this.ClientSize = new Size(503, 496);
            tabControlTelegram.Dock = DockStyle.Fill;

            timerCountdownStatusStrip = new StatusStrip();
            timerCountdownStatusStrip.Dock = DockStyle.Bottom;
            timerCountdownStatusStrip.SizingGrip = false;
            timerCountdownStatusStrip.Visible = checkBoxEnableTime.Checked;

            labelTotalCountdown = new ToolStripStatusLabel("Toplam: bekliyor");
            labelActiveCountdown = new ToolStripStatusLabel("Aktiflik: bekliyor");
            labelBreakCountdown = new ToolStripStatusLabel("Mola: başlamadı");
            timerCountdownStatusStrip.Items.Add(labelTotalCountdown);
            timerCountdownStatusStrip.Items.Add(new ToolStripStatusLabel("  |  "));
            timerCountdownStatusStrip.Items.Add(labelActiveCountdown);
            timerCountdownStatusStrip.Items.Add(new ToolStripStatusLabel("  |  "));
            timerCountdownStatusStrip.Items.Add(labelBreakCountdown);
            this.Controls.Add(timerCountdownStatusStrip);
            timerCountdownStatusStrip.BringToFront();

            timerCountdownRefresh = new System.Windows.Forms.Timer(components);
            timerCountdownRefresh.Interval = 500;
            timerCountdownRefresh.Tick += TimerCountdownRefresh_Tick;
            timerCountdownRefresh.Start();
            UpdateTimerCountdownDisplay();
        }

        private void TimerCountdownRefresh_Tick(object sender, EventArgs e)
        {
            UpdateTimerCountdownDisplay();
        }

        private void UpdateTimerCountdownDisplay()
        {
            if (timerCountdownStatusStrip == null)
            {
                return;
            }

            timerCountdownStatusStrip.Visible = checkBoxEnableTime.Checked;
            if (!checkBoxEnableTime.Checked)
            {
                return;
            }

            TimeSpan? total = TimerGame.GetTotalCountdownRemaining();
            TimeSpan? active = TimerGame.GetActiveCountdownRemaining();
            TimeSpan? pause = TimerGame.GetBreakCountdownRemaining();

            labelTotalCountdown.Text = total.HasValue
                ? "Toplam: " + FormatCountdown(total.Value)
                : "Toplam: bekliyor";

            if (pause.HasValue)
            {
                labelActiveCountdown.Text = "Aktiflik: molada";
                labelBreakCountdown.Text = "Mola bitimine: " + FormatCountdown(pause.Value);
            }
            else
            {
                labelActiveCountdown.Text = active.HasValue
                    ? "Aktiflik: " + FormatCountdown(active.Value)
                    : "Aktiflik: bekliyor";
                labelBreakCountdown.Text = "Mola: başlamadı";
            }
        }

        private string FormatCountdown(TimeSpan remaining)
        {
            long totalSeconds = (long)Math.Ceiling(Math.Max(0, remaining.TotalSeconds));
            return (totalSeconds / 60).ToString("00") + ":" +
                (totalSeconds % 60).ToString("00");
        }

        private void checkBoxEnableTime_CheckedChanged(object sender, EventArgs e)
        {
            if(checkBoxEnableTime.Checked)
            {
                ThreadGlobals.isTimerBreakEnabled = true;
                EnableOrDisableTimerCheckBox(true);
                if (!ThreadGlobals.isFishingStopped || !ThreadGlobals.isLevelFarmStopped ||
                    !ThreadGlobals.isEnergyCristalStopped)
                {
                    threadsHandler.StartTotalCountdownTimer();
                }
                if(ThreadGlobals.isFishingStopped)
                {
                    labelStartStatus.Text = "Süreleri dakika olarak girin";
                }
                
            }
            else
            {
                ThreadGlobals.isTimerBreakEnabled = false;
                EnableOrDisableTimerCheckBox(false);
                if(ThreadGlobals.isFishingStopped)
                {
                    labelStartStatus.Text = "Hazır";
                }
                
            }
            
            UpdateTimerCountdownDisplay();
        }

        private void textBoxMinWorkLeave(object sender, EventArgs e)
        {
            string value = textBoxMinWorkTime.Text;

            if (int.TryParse(value, out int minWork))
            {
                TimerGame.MIN_WORK_TIME = minWork;
            }
            else
            {
                MessageBox.Show("Please enter just a number as 'minute' unit (Exmp 3 or 35).", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);

            }
        }

        private void textBoxMaxWorkLeave(object sender, EventArgs e)
        {
            string value = textBoxMaxWorkTime.Text;

            if (int.TryParse(value, out int maxWork))
            {
                TimerGame.MAX_WORK_TIME = maxWork;
            }
            else
            {
                MessageBox.Show("Please enter just a number as 'minute' unit (Exmp 3 or 35).", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);

            }
        }

        private void textBoxMinMaxBreakLeave(object sender, EventArgs e)
        {

            string[] values = textBoxMinMaxBreak.Text.Split(new char[] { ' ', ',', '.' }, StringSplitOptions.RemoveEmptyEntries);

            if (values.Length == 2 && int.TryParse(values[0], out int minBreakTime) &&
                int.TryParse(values[1], out int maxBreakTime))
            {
                TimerGame.MAX_BREAK_TIME = maxBreakTime;
                TimerGame.MIN_BREAK_TIME = minBreakTime;
            }
            else
            {
                MessageBox.Show("Please enter two numbers as 'minute' unit (Exmp 2 5 or 5 9).", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);

            }
        }

        private void textBoxStopTime_Leave(object sender, EventArgs e)
        {
            //DebugPfCnsl.println("textBoxStopTime_Leave çalişti");
            string value = textBoxStopGameTime.Text;

            if (int.TryParse(value, out int stopGameTime))
            {
                TimerGame.GAME_STOP_TIME = stopGameTime;
            }
            else
            {
                MessageBox.Show("Please enter just a number as 'minute' unit (Exmp 3 or 35).", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);

            }
        }

    


        private void buttonCheckChat_Click(object sender, EventArgs e)
        {
            DebugPfCnsl.println("buttonCheckChat_Click çalişti");
            ChatHandlerForm chatHandlerForm = new ChatHandlerForm();
            chatHandlerForm.Show();
        }

       

        private void textBoxStatus_Leave(object sender, EventArgs e)
        {
            string valueHp = textBoxHp.Text;
            string valueSp = textBoxSp.Text;
            string valueStr = textBoxStr.Text;
            string valueDex = textBoxDex.Text;

            if (int.TryParse(valueHp, out int hpPriority) && int.TryParse(valueSp, out int spPriority)
                && int.TryParse(valueStr, out int strPriority) && int.TryParse(valueDex, out int dexPriority)) 
            {

                ThreadGlobals.SetStatusPriority(hpPriority, spPriority, strPriority, dexPriority);
                DebugPfCnsl.PrintArray(ThreadGlobals.GetStatusPriority());
            }
            else
            {
                MessageBox.Show("Lütfen öncelik sırasını sayılar ile belirleyiniz (örnek hp = 4 sp = 3 dex = 2 str = 1).", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);

            }
        }

        private void trackBarHp_ValueChanged(object sender, EventArgs e)
        {
            textBoxHpRate.Text = trackBarHp.Value.ToString();
            ThreadGlobals.SetHpSpRate(trackBarHp.Value, ThreadGlobals.GetHpSpRate()[1]);
           // DebugPfCnsl.println("hp value for trackHpVar = " + trackBarHp.Value);
        }

        private void trackBarSp_ValueChanged(object sender, EventArgs e)
        {
            textBoxSpRate.Text = trackBarSp.Value.ToString();
            ThreadGlobals.SetHpSpRate(ThreadGlobals.GetHpSpRate()[0], trackBarSp.Value);
           // DebugPfCnsl.println("sp value for trackspVar = " + trackBarSp.Value);
        }

        private void textBoxHpRate_Leave(object sender, EventArgs e)
        {
            string valueHpRate = textBoxHpRate.Text;
            string valueSpRate = textBoxSpRate.Text;
            

            if (int.TryParse(valueHpRate, out int hpRate) && int.TryParse(valueSpRate, out int spRate))
            {
                if ((hpRate > 0 && hpRate < 101) && (spRate > 0 && spRate < 101))
                {
                    ThreadGlobals.SetHpSpRate(hpRate, spRate);

                    trackBarHp.Value = hpRate;
                    trackBarSp.Value = spRate;

                    DebugPfCnsl.PrintArray(ThreadGlobals.GetHpSpRate());
                }
                else
                {
                    MessageBox.Show("Lütfen yüzde değerini 1 ile 100 arasında bir sayı ile giriniz.",
                        "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
              
             
                
            }
            else
            {
                MessageBox.Show("Lütfen yüzdelik belirlemesini rakam ile giriniz 1 ile 100 arası.", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);

            }
        }

        private void tabPage2_Click(object sender, EventArgs e)
        {

        }

        private void textBoxHpAndSPRate_Leave(object sender, EventArgs e)
        {
            string valueHpRate = textBoxHpRate.Text;
            string valueSpRate = textBoxSpRate.Text;


            if (int.TryParse(valueHpRate, out int hpRate) && int.TryParse(valueSpRate, out int spRate))
            {
                if ((hpRate > 0 && hpRate < 101) && (spRate > 0 && spRate < 101))
                {
                    ThreadGlobals.SetHpSpRate(hpRate, spRate);

                    trackBarHp.Value = hpRate;
                    trackBarSp.Value = spRate;

                    DebugPfCnsl.PrintArray(ThreadGlobals.GetHpSpRate());
                }
                else
                {
                    MessageBox.Show("Lütfen yüzde değerini 1 ile 100 arasında bir sayı ile giriniz.",
                        "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }



            }
            else
            {
                MessageBox.Show("Lütfen yüzdelik belirlemesini rakam ile giriniz 1 ile 100 arası.", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);

            }
        }

        private void checkBoxTelegram_CheckedChanged(object sender, EventArgs e)
        {
            // Yeniden giriş koruması (token yokken kutucuğu programatik olarak geri
            // alıyoruz; bu, aynı olayın ikinci kez tetiklenmesini engeller).
            if (isTelegramCheckChanging)
            {
                return;
            }

            if (checkBoxTelegram.Checked)
            {
                if (!TelegramBot.IsTokenConfigured)
                {
                    MessageBox.Show(
                        "Telegram bot token'ı yapılandırılmamış.\n\n" +
                        "1) Telegram'da @BotFather ile bir bot oluşturun ve size verilen token'ı kopyalayın.\n" +
                        "2) Bu sekmedeki 'Bot Token' kutusuna yapıştırıp 'Token'ı Kaydet' düğmesine basın.\n\n" +
                        "Alternatif olarak token'ı App.config içindeki TelegramBotToken anahtarına da yazabilirsiniz.",
                        "Telegram Token Gerekli", MessageBoxButtons.OK, MessageBoxIcon.Warning);

                    isTelegramCheckChanging = true;
                    checkBoxTelegram.Checked = false;
                    isTelegramCheckChanging = false;

                    UpdateTelegramStatusLabel(Color.Red, "Token yapılandırılmadı — Telegram kapalı");
                    return;
                }

                if (!EnsureTelegramBot())
                {
                    isTelegramCheckChanging = true;
                    checkBoxTelegram.Checked = false;
                    isTelegramCheckChanging = false;

                    UpdateTelegramStatusLabel(Color.Red, "Telegram başlatılamadı — log dosyasına bakınız");
                    return;
                }

                ThreadGlobals.isTelegramBotActive = true;

                if (!telegramBot.StartReceiver())
                {
                    ThreadGlobals.isTelegramBotActive = false;
                    isTelegramCheckChanging = true;
                    checkBoxTelegram.Checked = false;
                    isTelegramCheckChanging = false;

                    UpdateTelegramStatusLabel(Color.Red, "Telegram başlatılamadı (token hatalı olabilir)");
                    return;
                }

                UpdateTelegramStatusLabel(Color.Magenta, "Telegram aktif — bota mesaj gönderip 'Test Et' deyin");
            }
            else
            {
                ThreadGlobals.isTelegramBotActive = false;
                TelegramBot.TELEGRAM_BOT_IS_READY = false;
                if (telegramBot != null)
                {
                    telegramBot.StopReceiver();
                }
                UpdateTelegramStatusLabel(Color.Gray, "Telegram pasif");
            }
        }

        /// <summary>
        /// TelegramBot örneğini (yalnızca ilk kez) oluşturur.
        /// </summary>
        private bool EnsureTelegramBot()
        {
            if (telegramBot != null)
            {
                return true;
            }
            try
            {
                telegramBot = new TelegramBot(imageObjects);
                return true;
            }
            catch (Exception ex)
            {
                FileLogger.Error("TelegramBot oluşturulamadı", ex);
                MessageBox.Show("Telegram botu başlatılamadı: " + ex.Message,
                    "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        /// <summary>
        /// Telegram sekmesinin başlangıç durumunu hazırlar (token kutusunu doldurur,
        /// durumu etikete yazar).
        /// </summary>
        private void InitializeTelegramTab()
        {
            try
            {
                textBoxTelegramToken.Text = TelegramBot.GetToken();
            }
            catch (Exception ex)
            {
                FileLogger.Warning("Telegram token kutusu doldurulamadı: " + ex.Message);
            }

            if (TelegramBot.IsTokenConfigured)
            {
                UpdateTelegramStatusLabel(Color.Gray, "Token hazır — 'Telegram Bot Aktif Et' kutusunu işaretleyin");
            }
            else
            {
                UpdateTelegramStatusLabel(Color.Red, "Token yapılandırılmadı — Telegram kapalı");
            }
        }

        /// <summary>Durum etiketini güvenli biçimde günceller.</summary>
        private void UpdateTelegramStatusLabel(Color color, string text)
        {
            try
            {
                if (labelTelegramStatus == null || labelTelegramStatus.IsDisposed)
                {
                    return;
                }
                if (labelTelegramStatus.InvokeRequired)
                {
                    labelTelegramStatus.BeginInvoke((MethodInvoker)delegate
                    {
                        labelTelegramStatus.ForeColor = color;
                        labelTelegramStatus.Text = text;
                    });
                    return;
                }
                labelTelegramStatus.ForeColor = color;
                labelTelegramStatus.Text = text;
            }
            catch (Exception ex)
            {
                FileLogger.Debug("Telegram durum etiketi güncellenemedi: " + ex.Message);
            }
        }

        /// <summary>
        /// "Token'ı Kaydet" düğmesi: token'ı exe klasöründeki telegram.ini dosyasına yazar.
        /// </summary>
        private void buttonTelegramTokenSave_Click(object sender, EventArgs e)
        {
            string token = textBoxTelegramToken.Text == null
                ? string.Empty
                : textBoxTelegramToken.Text.Trim();

            if (TelegramBot.SaveToken(token))
            {
                UpdateTelegramStatusLabel(
                    token.Length > 0 ? Color.Green : Color.Gray,
                    token.Length > 0 ? "Token kaydedildi — kutucuğu işaretleyerek etkinleştirin"
                                     : "Token temizlendi — Telegram kapalı");

                MessageBox.Show(
                    token.Length > 0
                        ? "Token kaydedildi (dosya: telegram.ini).\nŞimdi 'Telegram Bot Aktif Et' kutusunu işaretleyin."
                        : "Token temizlendi. Telegram özelliği devre dışı.",
                    "Kaydedildi", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show("Token kaydedilemedi. Ayrıntı için Logs klasöründeki günlük dosyasına bakınız.",
                    "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void buttonTelegramTest_Click(object sender, EventArgs e)
        {
            // Bot aktif değilse veya istemci hiç oluşturulmamışsa test yapılamaz.
            if (!ThreadGlobals.isTelegramBotActive || telegramBot == null)
            {
                MessageBox.Show("Önce 'Telegram Bot Aktif Et' kutucuğunu işaretlemelisin.",
                    "Uyarı", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!TelegramBot.TELEGRAM_BOT_IS_READY)
            {
                // Henüz onaylanmamış bir bağlantı isteği var mı?
                if (TelegramBot.TELEGRAM_SEND_MESSAGE != null && TelegramBot.TELEGRAM_SEND_MESSAGE.Chat != null)
                {
                    TelegramBot.TELEGRAM_DIALOG_PANEL_ACTIVE = true;

                    string requestOwner = TelegramBot.TELEGRAM_SEND_MESSAGE.Chat.FirstName ?? "Bilinmeyen";

                    DialogResult result = MessageBox.Show(
                        requestOwner + " adlı kullanıcı size mi ait?",
                        "Onay", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                    TelegramBot.TELEGRAM_DIALOG_PANEL_ACTIVE = false;

                    if (result == DialogResult.Yes)
                    {
                        labelTelegramStatus.ForeColor = Color.Green;
                        labelTelegramStatus.Text = "Bağlantı kuruldu. Kullanıma hazır";
                        TelegramBot.SendMessageTelegram("Bağlantı hazır");
                        TelegramBot.TELEGRAM_BOT_IS_READY = true;
                        buttonTelegramTest.Text = "Sıfırla";
                    }
                    else
                    {
                        labelTelegramStatus.ForeColor = Color.Red;
                        labelTelegramStatus.Text = "Bağlantı kurmak için tekrar mesaj gönderin";
                    }
                }
                else
                {
                    MessageBox.Show(
                        "Telegram botuna mesaj gönderip (veya 'Start' düğmesine basıp) " +
                        "ardından tekrar 'Test Et' düğmesine basınız.",
                        "Uyarı", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            else
            {
                // Zaten kurulmuş bir bağlantı var: kullanıcı yeni biriyle mi bağlanmak istiyor?
                string currentOwner = TelegramBot.TELEGRAM_SEND_MESSAGE != null &&
                                      TelegramBot.TELEGRAM_SEND_MESSAGE.Chat != null
                    ? (TelegramBot.TELEGRAM_SEND_MESSAGE.Chat.FirstName ?? "mevcut kullanıcı")
                    : "mevcut kullanıcı";

                DialogResult result = MessageBox.Show(
                    "Zaten " + currentOwner + " ile bağlantı kuruldu." +
                    " Yeni bir kişi ile mi bağlantı kurmak istiyorsun?",
                    "Onay", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                if (result == DialogResult.Yes)
                {
                    TelegramBot.SendMessageTelegram("Bağlantınız sıfırlandı. Aktif etmek için " +
                        "yeniden mesaj gönderip 'Test Et' düğmesine basınız.");

                    TelegramBot.TELEGRAM_BOT_IS_READY = false;
                    TelegramBot.TELEGRAM_SEND_MESSAGE = null;

                    labelTelegramStatus.ForeColor = Color.Magenta;
                    labelTelegramStatus.Text = "Bağlantınız sıfırlandı";
                    buttonTelegramTest.Text = "Test Et";
                }
            }
        }

        private void TextBoxes_Skills_Leave(object sender, EventArgs e)
        {
            HandleSkillTimeForTextBox(textBoxSkillOne, 0);
            HandleSkillTimeForTextBox(textBoxSkillTwo, 1);
            HandleSkillTimeForTextBox(textBoxSkillThree, 2);
            HandleSkillTimeForTextBox(textBoxSkillFour, 3);
            HandleSkillTimeForTextBox(textBoxSkillF1, 4);
            HandleSkillTimeForTextBox(textBoxSkillF2, 5);
            HandleSkillTimeForTextBox(textBoxSkillF3, 6);
            HandleSkillTimeForTextBox(textBoxSkillF4, 7);
        }

        private void HandleSkillTimeForTextBox(TextBox textBox,int indexForSkillTime)
        {
            string valueSkillTime = textBox.Text;



            if (int.TryParse(valueSkillTime, out int skillTime))
            {
                if (skillTime >= 0)
                {
                    ThreadGlobals.SetSkillTime(indexForSkillTime, skillTime);
                }
                else
                {
                    MessageBox.Show("Lütfen " + textBox.Name + " zamanı 0 dan büyük bir değer giriniz.", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }

                

            }
            else
            {
                MessageBox.Show("Lütfen " + textBox.Name + " kısmındaki kutucuğa süre olarak sadece sayı giriniz.", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);

            }
        }
        private void checkBoxFishingMiniBreak_CheckedChanged(object sender, EventArgs e)
        {
            if(checkBoxFishingMiniBreak.Checked)
            {
                ThreadGlobals.isFishingMiniBreakActive = true;
            }
            else
            {
                ThreadGlobals.isFishingMiniBreakActive = false;
            }
        }

        private void checkBoxChatActive_CheckedChanged(object sender, EventArgs e)
        {
            if(checkBoxChatActive.Checked)
            {
                ThreadGlobals.isChattingAnswerActive = true;
            }
            else
            {
                ThreadGlobals.isChattingAnswerActive = false;
            }
        }

        private void checkBoxWhisperActive_CheckedChanged(object sender, EventArgs e)
        {
            if(checkBoxWhisperActive.Checked)
            {
                ThreadGlobals.isWhisperAnswerActive = true;
            }
            else
            {
                ThreadGlobals.isWhisperAnswerActive = false;
            }
        }

        private void checkBoxETPPickUp_CheckedChanged(object sender, EventArgs e)
        {
            if(checkBoxETPPickUp.Checked)
            {
                
                ThreadGlobals.isETPPickUpActive = true;
            }
            else
            {
                ThreadGlobals.isETPPickUpActive = false;
            }
        }

        private void InitializeToolTip()
        {
            toolTip.SetToolTip(checkBoxAdaptableFish, "Eğer haritada veya yakınınızda oyuncu var ise yavaş balık tutar");
            toolTip.SetToolTip(checkBoxPCSlow, "Eğer Bilgisayarın çok yavaş ise balık tutmak yada enerji parçası için bu seçeneği tıkla");
            toolTip.SetToolTip(checkBoxCloseAfterTime, "Toplam süre dolduğunda uygulamayı tamamen kapatır.");
            toolTip.SetToolTip(buttonPrepareWorms, "Solucanları 200'lük yapar; 32 yığından azsa balıkçıdan tamamlar ve hızlı erişime ekler. Balık tutmayı başlatmaz.");
        }

        private void checkBoxAdaptable_CheckedChanged(object sender, EventArgs e)
        {
            if(checkBoxAdaptableFish.Checked)
            {
                ThreadGlobals.isAdaptableFishing = true;
            }
            else
            {
                ThreadGlobals.isAdaptableFishing = false;
            }
        }

        private void checkBoxPCSlow_CheckedChanged(object sender, EventArgs e)
        {
            if(checkBoxPCSlow.Checked)
            {
                TimerGame.IS_PC_SLOW = true;
            }
            else
            {
                TimerGame.IS_PC_SLOW = false;
            }
        }
    }
}
