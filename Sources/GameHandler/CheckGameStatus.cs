using Metin2AutoFishCSharp.Sources;
using Metin2AutoFishCSharp.Sources.ChatHandler;
using Metin2AutoFishCSharp.Sources.GameHandler;
using Metin2AutoFishCSharp.Sources.LevelAndFarms;
using MusicPlayerApp.Debugs;
using MusicPlayerApp.Sources.CharacterHandle;
using MusicPlayerApp.Sources.CoordinatesHandler;
using MusicPlayerApp.Sources.ImageHandle;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Drawing;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MusicPlayerApp.Sources.GameHandler
{
    internal class CheckGameStatus
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        public static readonly string gameForgeProcessName = "gfservice";
        public static readonly string metin2ProcessNAme = "metin2client";

        private int[] imageEntryScrn;
        private int[] imageKillScrn;
        private int[] imageCharScreen;
        private int[] imageSaleTitle;
        private int[] imageIsCharOnline;
        private int[] imageIsAnotPlayer;
        private int[] imageIsMetin2IconSeemed;

        GameObjectCoordinates coordinates;
        ScreenShotWinAPI screenShot;
        ImageObjects imageObjects;
        GameInputHandler inputGame;
        CharSpecialThings charThigs;
        TimerGame timerSettingButton;
        TimerGame timerCheckOnline;
        LevelHandle levelHandle;
       
        private DebugPfCnsl debugConsole;
        private readonly GameAlphabetDetecter alphabetDetecter;

        public CheckGameStatus(ImageObjects imageObjects, GameAlphabetDetecter alphabetDetecter)
        {
            this.imageObjects = imageObjects;
            this.alphabetDetecter = alphabetDetecter;
            coordinates = new GameObjectCoordinates(imageObjects);
            screenShot = new ScreenShotWinAPI();       
            inputGame = new GameInputHandler();
            charThigs = new CharSpecialThings(imageObjects);
            timerSettingButton = new TimerGame();
            debugConsole = new DebugPfCnsl();
            timerCheckOnline = new TimerGame();
            levelHandle = new LevelHandle(imageObjects);
        }
        public void StartCheckingGame()
        {
            if (ThreadGlobals.isPausedTheGame)
            {
                
                timerSettingButton.SetStartedSecondTime();
                timerCheckOnline.SetStartedSecondTime();
                return;
            }

            if(ThreadGlobals.CheckGameIsStopped())
            {
                DebugPfCnsl.println("StartCheckingGame is returned");
                return;
            }

            imageEntryScrn = screenShot.CaptureAreaAsArray(coordinates.RectEntryScreen());
            imageKillScrn = screenShot.CaptureAreaAsArray(coordinates.RectDieScreen());
            imageCharScreen = screenShot.CaptureAreaAsArray(coordinates.RectCharScreen());
            imageSaleTitle = screenShot.CaptureAreaAsArray(coordinates.RectSaleCross());
            imageIsCharOnline = screenShot.CaptureAreaAsArray(coordinates.RectSettingButton());

           // DebugPfCnsl.printlnTime("StartChecking taken images", 2);

            if (imageObjects.CompareTwoArrayAdvanced(imageObjects.arrayEntryScreen,
                imageEntryScrn, ImageSensibilityLevel.SENSIBILTY_MED))
            {
                ThreadGlobals.isSettingButtonSeemed = false;
                ThreadGlobals.isEntryScreenActive = true;
                EntryScreenHandle();
            }
            else if (imageObjects.CompareTwoArrayAdvanced(imageObjects.arrayCharScreen,
                imageCharScreen, ImageSensibilityLevel.SENSIBILTY_MED))
            {
                ThreadGlobals.isSettingButtonSeemed = false;
                ThreadGlobals.isCharScreenActive = true;
                CharacterScreenHandle();
            }
            else if (imageObjects.CompareTwoArrayAdvanced(imageObjects.arrayKilledScreen,
                imageKillScrn, ImageSensibilityLevel.SENSIBILTY_MED))
            {
               
                ThreadGlobals.isCharKilled = true;
                TimerGame.SleepRandom(3000, 4599);
                charThigs.SettingButtonClick(SettingButtonPrefers.EXIT_BUTTON);
                ThreadGlobals.isCharKilled = false;
            }
            else if (imageObjects.CompareTwoArrayAdvanced(imageObjects.arraySaleTitle,
                imageSaleTitle, ImageSensibilityLevel.SENSIBILTY_MED))
            {
                ThreadGlobals.isSaleTitleActive = true;
                CloseSaleTitle();
            }
            else if (imageObjects.CompareTwoArrayAdvanced(imageObjects.arraySettingButton,
                imageIsCharOnline, ImageSensibilityLevel.SENSIBILTY_MED))
            {
                ThreadGlobals.isSettingButtonSeemed = true;
                ThreadGlobals.isEntryScreenActive = false;
                ThreadGlobals.isCharScreenActive = false;

                timerSettingButton.SetStartedSecondTime();
                timerCheckOnline.SetStartedSecondTime();

                if (!ThreadGlobals.isFishingStopped)
                {
                    //debugConsole.printlnTime("detected settingButton ", 2);

                    int[] imageIsActiveFishBoard = screenShot.CaptureAreaAsArray(coordinates.RectFishTitle());

                    ThreadGlobals.isActiveFishBoard = imageObjects.CompareTwoArrayAdvanced(
                        imageObjects.arrayFishTitle, imageIsActiveFishBoard, ImageSensibilityLevel.SENSIBILTY_MED);
                    //DebugPfCnsl.println("isActiveFishboard "+ ThreadGlobals.isActiveFishBoard);
                   
                }
                else if (!ThreadGlobals.isLevelFarmStopped)
                {

                }
                
            }
            else
            {
                ThreadGlobals.isSettingButtonSeemed = false;
               if(!timerCheckOnline.CheckDelayTimeInSecond(30))
                {
                    DebugPfCnsl.println("Setting button tespit edilemediği için esc" +
                        "tuşuna basılıyor");
                    inputGame.KeyPress(KeyboardInput.ScanCodeShort.ESCAPE);
                    TimerGame.SleepRandom(400, 600);
                   // timerCheckOnline.SetStartedSecondTime();
                }
                  if(!timerSettingButton.CheckDelayTimeInSecond((TimerGame.MAX_BREAK_TIME +2) * 60) || 
                    !CheckGameCoordinate.IS_METIN2_ICON_DETECTED)                  
                    {
                    TelegramBot.SendMessageTelegram("Metin2 sembolü tespit edilemiyor.Tekrar oyuna girilmesi sağlanıyor. " +
                        "Bilgisayarı kontrol etmekte fayda var");

                    DebugPfCnsl.println("Program couldn't detect setting button  " +
                         "\n so ProcessCreateNewMetin2 function called");
                    if(ProcessCreateNewMetin2())
                    {
                        timerSettingButton.SetStartedSecondTime();
                        timerCheckOnline.SetStartedSecondTime();
                        DebugPfCnsl.println("başarı ile metin2 oluşturuldu");
                    }
                }
                
            }
        }

        private void CharacterScreenHandle()
        {
            TimerGame timerGame = new TimerGame();
            timerGame.SetStartedSecondTime();
            int[] targetCharScreen = screenShot.ImageArraySpecifiedArea(coordinates.RectCharScreen());

            while (imageObjects.CompareTwoArrayAdvanced(imageObjects.arrayCharScreen, targetCharScreen,
                ImageSensibilityLevel.SENSIBILTY_HIGH) && timerGame.CheckDelayTimeInSecond(15))
            {
                if(ThreadGlobals.CheckGameIsStopped() || ThreadGlobals.isPausedTheGame) return;
                DebugPfCnsl.println("In the Char Screen");
                inputGame.MouseMoveAndPressLeft(coordinates.PointCharEnterButton().X,
                    coordinates.PointCharEnterButton().Y);
                TimerGame.SleepRandom(1500, 2599);
                targetCharScreen = screenShot.ImageArraySpecifiedArea(coordinates.RectCharScreen());

            }

            if(!timerGame.CheckDelayTimeInSecond(15))
            {
                DebugPfCnsl.println("Game is bug at the Char Screen more than 15 second...");
                //TODO: Burada mutlaka GameForge dan yeni metin2 yi baslat.
                ProcessCreateNewMetin2();
                
            }
            else
            {
                TimerGame timerSettingButtonCanSee = new TimerGame();
                int[] targetSettingButton = screenShot.ImageArraySpecifiedArea(coordinates.RectSettingButton());
                while(!imageObjects.CompareTwoArrayAdvanced(imageObjects.arraySettingButton, targetSettingButton,
                    ImageSensibilityLevel.SENSIBILTY_HIGH))
                {
                    // DebugPfCnsl.println("setting button görülmüyor");
                    if (timerSettingButtonCanSee.CheckDelayTimeInSecond(15))
                    {
                        if (ThreadGlobals.CheckGameIsStopped()) return;
                        targetSettingButton = screenShot.ImageArraySpecifiedArea(coordinates.RectSettingButton());
                    }
                    else
                    {
                        DebugPfCnsl.println("uzun süredir CharacterScreenHandle setting button tespit edilemedi return edildi");
                        return;
                    }
                    
                   
                }
                TimerGame.SleepRandom(2000, 4000);

               
                if (!ThreadGlobals.isFishingStopped)
                {
                    inputGame.KeyPress(KeyboardInput.ScanCodeShort.ESCAPE);
                    TimerGame.SleepRandom(90, 120);
                    inputGame.KeyPress(KeyboardInput.ScanCodeShort.ESCAPE);
                    TimerGame.SleepRandom(90, 120);
                    inputGame.KeyPress(KeyboardInput.ScanCodeShort.ESCAPE);
                    TimerGame.SleepRandom(90, 120);

                    inputGame.KeyDown(KeyboardInput.ScanCodeShort.KEY_W);
                    TimerGame.SleepRandom(90, 120);
                    inputGame.KeyRelease(KeyboardInput.ScanCodeShort.KEY_W);
                }
                else if (!ThreadGlobals.isLevelFarmStopped)
                {
                    AutoHunter.IS_AUTO_HUNTER_STARTED = false;
                }

                ThreadGlobals.isCheckedShiftPage = false;
                ThreadGlobals.isSettingButtonSeemed = true;
                ThreadGlobals.isCharScreenActive = false;

                charThigs.ProvideCharNameCanSee();
                charThigs.CheckShiftPageNumber();

                

                timerSettingButtonCanSee.SetStartedSecondTime();
                timerCheckOnline.SetStartedSecondTime();
                //  DebugPfCnsl.println("CheckGameStatus DebugThreadGlobalPref = " +
                //    ThreadGlobals.GetGameStatusOrUserPrefer());
            }

            
        }

        private const int ENTRY_CHANNEL_COUNT = 6;
        private const int ENTRY_RECHECK_SECONDS = 30;
        private const int ENTRY_CHANNEL_CONNECT_TIMEOUT_SECONDS = 20;
        private const int CHANNEL_FULL_TEMPLATE_WIDTH = 26;
        private const int CHANNEL_FULL_TEMPLATE_HEIGHT = 16;
        private const int NITE_TEMPLATE_WIDTH = 105;
        private const int NITE_TEMPLATE_HEIGHT = 20;
        private const int NITE_SELECTED_TEMPLATE_WIDTH = 131;
        private const int NITE_SELECTED_TEMPLATE_HEIGHT = 22;
        private const int CHANNEL_UNKNOWN_TEMPLATE_WIDTH = 53;
        private const int CHANNEL_UNKNOWN_TEMPLATE_HEIGHT = 11;
        private const int TAMAM_TEMPLATE_WIDTH = 80;
        private const int TAMAM_TEMPLATE_HEIGHT = 23;

        private void EntryScreenHandle()
        {
            while (IsEntryScreenVisible())
            {
                if (ThreadGlobals.CheckGameIsStopped() || ThreadGlobals.isPausedTheGame) return;

                // Her denemede Nite'yi seç, CH'lerden rastgele birini ve Tamam PNG'sini tıkla.
                if (!TryClickNiteServerTemplate())
                {
                    FileLogger.Warning("Nite bulunamadı; " + ENTRY_RECHECK_SECONDS +
                        " saniye sonra giriş denemesi tekrarlanacak");
                    WaitWhileEntryScreen(ENTRY_RECHECK_SECONDS);
                    continue;
                }
                TimerGame.SleepActiveTime(500);

                int selectedChannel = TimerGame.MakeRandomValue(1, ENTRY_CHANNEL_COUNT + 1);
                Point channelPoint = coordinates.PointChannel(selectedChannel);
                inputGame.MouseMoveAndPressLeft(channelPoint.X, channelPoint.Y);
                FileLogger.Info("Rastgele CH" + selectedChannel + " seçildi");
                TimerGame.SleepActiveTime(500);

                TryClickTamamButtonTemplate();
                WaitForChannelConnection();

                if (IsEntryScreenVisible())
                {
                    FileLogger.Info("Giriş ekranı hâlâ açık; Nite-CH-Tamam akışı yeniden denenecek");
                }
            }

            ThreadGlobals.isEntryScreenActive = false;
        }

        private bool TryClickNiteServerTemplate()
        {
            Rectangle searchArea = coordinates.RectNiteServerSearchArea();
            Rectangle templateArea = new Rectangle(0, 0,
                NITE_TEMPLATE_WIDTH, NITE_TEMPLATE_HEIGHT);
            Rectangle match = imageObjects.FindImageInArea(
                imageObjects.arrayNiteServer, templateArea, searchArea);
            string matchedTemplate = "normal";

            if (match == Rectangle.Empty)
            {
                templateArea = new Rectangle(0, 0,
                    NITE_SELECTED_TEMPLATE_WIDTH, NITE_SELECTED_TEMPLATE_HEIGHT);
                match = imageObjects.FindImageInArea(
                    imageObjects.arrayNiteServerSelected, templateArea, searchArea);
                matchedTemplate = "mavi/seçili";
            }

            // Pencere ofseti/arka plan tonu farklıysa dar alandaki eşleşme kaçabilir;
            // bu durumda aynı iki PNG tüm ekranda aranır.
            if (match == Rectangle.Empty)
            {
                templateArea = new Rectangle(0, 0, NITE_TEMPLATE_WIDTH, NITE_TEMPLATE_HEIGHT);
                match = imageObjects.FindImageInArea(
                    imageObjects.arrayNiteServer, templateArea, Rectangle.Empty);
                matchedTemplate = "normal (tam ekran araması)";
            }

            if (match == Rectangle.Empty)
            {
                templateArea = new Rectangle(0, 0,
                    NITE_SELECTED_TEMPLATE_WIDTH, NITE_SELECTED_TEMPLATE_HEIGHT);
                match = imageObjects.FindImageInArea(
                    imageObjects.arrayNiteServerSelected, templateArea, Rectangle.Empty);
                matchedTemplate = "mavi/seçili (tam ekran araması)";
            }

            if (match == Rectangle.Empty)
            {
                // PNG'ler renk/ölçek farkı yüzünden eşleşmese de giriş akışını durdurma;
                // oyun penceresi tespit edilmiş olduğundan Nite'nin bilinen satırına tıkla.
                Point fallbackPoint = coordinates.PointNiteServer();
                inputGame.MouseMoveAndPressLeft(fallbackPoint.X, fallbackPoint.Y);
                FileLogger.Warning("Nite PNG eşleşmedi; bilinen Nite koordinatına tıklandı (" +
                    fallbackPoint.X + "," + fallbackPoint.Y + ")");
                return true;
            }

            inputGame.MouseMoveAndPressLeft(match.X + match.Width / 2,
                match.Y + match.Height / 2);
            FileLogger.Info("Nite sunucusu PNG ile bulundu ve tıklandı (" + matchedTemplate +
                ", x=" + match.X + ", y=" + match.Y + ")");
            return true;
        }

        private bool TryClickTamamButtonTemplate()
        {
            Rectangle templateArea = new Rectangle(0, 0,
                TAMAM_TEMPLATE_WIDTH, TAMAM_TEMPLATE_HEIGHT);
            Rectangle match = imageObjects.FindImageInArea(
                imageObjects.arrayTamamButton, templateArea,
                coordinates.RectOkButtonSearchArea());

            if (match == Rectangle.Empty)
            {
                match = imageObjects.FindImageInArea(
                    imageObjects.arrayTamamButton, templateArea, Rectangle.Empty);
            }

            if (match != Rectangle.Empty)
            {
                inputGame.MouseMoveAndPressLeft(match.X + match.Width / 2,
                    match.Y + match.Height / 2);
                FileLogger.Info("Tamam butonu tmm.png ile bulundu ve tıklandı (x=" +
                    match.X + ", y=" + match.Y + ")");
                return true;
            }

            Point fallbackPoint = coordinates.PointOkButton();
            inputGame.MouseMoveAndPressLeft(fallbackPoint.X, fallbackPoint.Y);
            FileLogger.Warning("tmm.png eşleşmedi; Tamam butonunun bilinen koordinatına tıklandı");
            return false;
        }

        private Rectangle FindChannelStatusTemplate(int[] template, int width, int height,
            int channelNumber)
        {
            Rectangle templateArea = new Rectangle(0, 0, width, height);
            return imageObjects.FindImageInArea(template, templateArea,
                coordinates.RectChannelStatusSearchArea(channelNumber));
        }

        private List<int> ReadUnknownChannels(out bool hasFullChannel)
        {
            List<int> channels = new List<int>();
            hasFullChannel = false;
            for (int channel = 1; channel <= ENTRY_CHANNEL_COUNT; channel++)
            {
                Rectangle unknownMatch = FindChannelStatusTemplate(
                    imageObjects.arrayChannelUnknownStatus,
                    CHANNEL_UNKNOWN_TEMPLATE_WIDTH, CHANNEL_UNKNOWN_TEMPLATE_HEIGHT, channel);
                if (unknownMatch != Rectangle.Empty)
                {
                    channels.Add(channel);
                    FileLogger.Debug("CH" + channel + " Bilinmeyen PNG'si bulundu");
                    continue;
                }

                Rectangle fullMatch = FindChannelStatusTemplate(
                    imageObjects.arrayChannelFullStatus,
                    CHANNEL_FULL_TEMPLATE_WIDTH, CHANNEL_FULL_TEMPLATE_HEIGHT, channel);
                if (fullMatch != Rectangle.Empty)
                {
                    hasFullChannel = true;
                    FileLogger.Debug("CH" + channel + " Dolu PNG'si bulundu");
                    continue;
                }

                // PNG şablonları eşleşmezse farklı renk/arka plan varyantlarına karşı
                // mevcut harf OCR'ı yedek olarak kullanılır.
                string status = ReadChannelStatus(channel);
                FileLogger.Debug("Nite CH" + channel + " OCR durumu: " +
                    (string.IsNullOrEmpty(status) ? "okunamadı" : status));

                if (status.IndexOf("bilinmeyen", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    channels.Add(channel);
                }
                else if (status.IndexOf("dolu", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    status.IndexOf("kalabal", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    hasFullChannel = true;
                }
            }
            return channels;
        }

        private int ReselectNiteAndTryUnknownChannel(HashSet<int> attemptedChannels)
        {
            if (ThreadGlobals.CheckGameIsStopped() || ThreadGlobals.isPausedTheGame ||
                !TryClickNiteServerTemplate())
            {
                return 0;
            }

            TimerGame.SleepActiveTime(500);
            if (ThreadGlobals.CheckGameIsStopped() || ThreadGlobals.isPausedTheGame) return 0;

            bool hasFullChannel;
            List<int> refreshedUnknownChannels = ReadUnknownChannels(out hasFullChannel)
                .Where(channel => !attemptedChannels.Contains(channel))
                .ToList();
            if (refreshedUnknownChannels.Count == 0)
            {
                FileLogger.Info("Nite yeniden seçildi; yeni Bilinmeyen PNG'si görünmedi");
                return 0;
            }

            int selectedChannel = refreshedUnknownChannels[
                TimerGame.MakeRandomValue(0, refreshedUnknownChannels.Count)];
            ClickChannelAndConfirm(selectedChannel, "Nite yenilendikten sonra Bilinmeyen PNG'si bulundu");
            return selectedChannel;
        }

        private void ClickChannelAndConfirm(int channelNumber, string reason)
        {
            Point channelPoint = coordinates.PointChannel(channelNumber);
            inputGame.MouseMoveAndPressLeft(channelPoint.X, channelPoint.Y);
            TimerGame.SleepActiveTime(250);

            Point okButton = coordinates.PointOkButton();
            inputGame.MouseMoveAndPressLeft(okButton.X, okButton.Y);
            FileLogger.Info("CH" + channelNumber + " seçildi ve Tamam tıklandı: " + reason);
        }

        private void WaitForChannelConnection()
        {
            TimerGame connectionTimer = new TimerGame();
            while (connectionTimer.CheckDelayTimeInSecond(ENTRY_CHANNEL_CONNECT_TIMEOUT_SECONDS) &&
                IsEntryScreenVisible())
            {
                if (ThreadGlobals.CheckGameIsStopped() || ThreadGlobals.isPausedTheGame) return;
                TimerGame.SleepActiveTime(500);
            }
        }

        private string ReadChannelStatus(int channelNumber)
        {
            Rectangle statusArea = coordinates.RectChannelStatus(channelNumber);
            int[] statusPixels = screenShot.CaptureAreaAsArray(statusArea);
            if (statusPixels == null || statusPixels.Length == 0)
            {
                return string.Empty;
            }

            // Durum yazılarının rengi sunucu arayüzü temasına göre değişebildiğinden,
            // satırdaki baskın parlak metin renkleri OCR için aday olarak denenir.
            Dictionary<int, int> colorCounts = new Dictionary<int, int>();
            foreach (int pixel in statusPixels)
            {
                int color = pixel & 0x00FFFFFF;
                int red = (color >> 16) & 0xFF;
                int green = (color >> 8) & 0xFF;
                int blue = color & 0xFF;
                if (red + green + blue < 180) continue;

                int count;
                colorCounts.TryGetValue(color, out count);
                colorCounts[color] = count + 1;
            }

            int maxColorPixels = statusPixels.Length / 3;
            int[] candidateColors = colorCounts
                .Where(item => item.Value >= 2 && item.Value <= maxColorPixels)
                .OrderByDescending(item => item.Value)
                .Take(4)
                .Select(item => unchecked((int)(0xFF000000 | (uint)item.Key)))
                .ToArray();

            foreach (int color in candidateColors)
            {
                try
                {
                    string detected = alphabetDetecter.DetectGameTextWithProvidedImage(
                        statusPixels, statusArea, color);
                    string normalized = new string((detected ?? string.Empty)
                        .Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
                    if (normalized.Contains("bilinmeyen")) return "Bilinmeyen";
                    if (normalized.Contains("dolu")) return "Dolu";
                    if (normalized.Contains("kalabal")) return "Kalabalık";
                }
                catch (Exception ex)
                {
                    FileLogger.Debug("CH" + channelNumber + " OCR adayı okunamadı: " + ex.Message);
                }
            }

            return string.Empty;
        }

        private bool IsEntryScreenVisible()
        {
            return imageObjects.CompareTwoArrayAdvanced(imageObjects.arrayEntryScreen,
                screenShot.CaptureAreaAsArray(coordinates.RectEntryScreen()),
                ImageSensibilityLevel.SENSIBILTY_HIGH);
        }

        private void WaitWhileEntryScreen(int seconds)
        {
            int remainingMilliseconds = seconds * 1000;
            const int sleepSliceMilliseconds = 500;
            while (remainingMilliseconds > 0 && IsEntryScreenVisible())
            {
                if (ThreadGlobals.CheckGameIsStopped() || ThreadGlobals.isPausedTheGame) return;
                int sleepMilliseconds = Math.Min(sleepSliceMilliseconds, remainingMilliseconds);
                TimerGame.SleepActiveTime(sleepMilliseconds);
                remainingMilliseconds -= sleepMilliseconds;
            }
        }

        private void CloseSaleTitle()
       {
            int[] targetSaleTitle = screenShot.ImageArraySpecifiedArea(coordinates.RectSaleCross());

            while(imageObjects.CompareTwoArrayAdvanced(imageObjects.arraySaleTitle,
                targetSaleTitle,ImageSensibilityLevel.SENSIBILTY_MED)) 
            {
                if(ThreadGlobals.CheckGameIsStopped() || ThreadGlobals.isPausedTheGame) return;
                inputGame.MouseMoveAndPressLeft(coordinates.RectSaleCross().X,
                    coordinates.RectSaleCross().Y);
                targetSaleTitle = screenShot.ImageArraySpecifiedArea(coordinates.RectSaleCross());
            }
            ThreadGlobals.isSaleTitleActive = false;
       }

        private bool CheckProgramIsActive(string programName)
        {
            Process[] processes = Process.GetProcessesByName(programName);
           
            if (processes.Length > 0)
            {
                // Program çalışıyorsa odaklan
                DebugPfCnsl.println($"'{programName}' programı zaten çalışıyor. Odaklanılıyor...");
               
                return true;
            }
            else
            {
                // Program çalışmıyorsa mesaj göster
                DebugPfCnsl.println($"'{programName}' programı çalışmıyor.");
                return false;
            }
            
        }
        
        public bool ProcessCreateNewMetin2()
        {
            if (ThreadGlobals.CheckGameIsStopped())
            {
                DebugPfCnsl.println("ProcessCreateNewMetin2 is returned false");
                return false; 
            } 

          

           /* if (hwdnMetin2Exe != IntPtr.Zero)
            {
                DebugPfCnsl.println("zaten metin2 açık odaklanılıyor");
                SetForegroundWindow(hwdnMetin2Exe);
                TimerGame.SleepRandom(1500, 2500);
                return true;
            }*/
             if (CheckProgramIsActive(gameForgeProcessName))
            {
                DebugPfCnsl.println("Gforge arka planda çalışıyor");
                Rectangle[] rectTargetsGForgeApp = imageObjects.FindAllImagesOnScreen(
                    imageObjects.arrayGameForgeAppIcon, coordinates.RectGForgeAppIconSample(),
                    Rectangle.Empty);

                if(rectTargetsGForgeApp.Length > 0)
                {
                    DebugPfCnsl.println("Gforge sembolü bulundu");
                    for (int program =0;  program<rectTargetsGForgeApp.Length; program++) 
                    {
                        inputGame.MouseMoveAndPressLeft(rectTargetsGForgeApp[program].X,
                            rectTargetsGForgeApp[program].Y);
                        TimerGame.SleepRandom(2000, 3000);

                        //Press game Forge Maximize Icon 

                        Rectangle rectGFMaximize = imageObjects.FindImageOnScreen(imageObjects.arrayGameForgeMaximizeBut,
                            coordinates.RectGForgeMaximizeButtonSample());

                        if (rectGFMaximize != Rectangle.Empty)
                        {
                            DebugPfCnsl.println("Gforge ekranı büyütülüyor");

                            inputGame.MouseMoveAndPressLeft(rectGFMaximize.X + rectGFMaximize.Width / 2,
                            rectGFMaximize.Y + rectGFMaximize.Height / 2);
                            TimerGame.SleepRandom(2000, 3000);

                        }
                            //Check GameForge Page is open and button text is "Oyna "
                            Rectangle rectGForgeOynaButton = imageObjects.FindImageOnScreen(
                                imageObjects.arrayGameForgeOyna, coordinates.RectGForgeOynaButton());

                            if (rectGForgeOynaButton != Rectangle.Empty)
                            {
                                inputGame.MouseMoveAndPressLeft(rectGForgeOynaButton.X,
                                    rectGForgeOynaButton.Y);

                                TimerGame timerMetin2Icon = new TimerGame();


                                while (!imageObjects.CompareTwoArrayAdvanced(imageObjects.arrayEntryScreen,
                                    screenShot.ImageArraySpecifiedArea(coordinates.RectEntryScreen()),
                                    ImageSensibilityLevel.SENSIBILTY_HIGH))
                                {
                                    if (timerMetin2Icon.CheckDelayTimeInSecond(60))
                                    {
                                        if (ThreadGlobals.CheckGameIsStopped()) return false;
                                        inputGame.MouseMoveAndPressLeft(rectTargetsGForgeApp[program].X,
                                        rectTargetsGForgeApp[program].Y);
                                        TimerGame.SleepRandom(1000, 2000);
                                    }

                                    else
                                    {
                                        DebugPfCnsl.println("We couldn't get Metin2 Application");
                                        return false;
                                    }
                                }

                                //Focus metin2 application
                                inputGame.MouseMoveAndPressLeft(coordinates.RectEntryScreen().X,
                                    coordinates.RectEntryScreen().Y);

                                DebugPfCnsl.println("Metin2 Application is opened and entry screen detected");
                                return true;

                            }
                          
                            //If button text is not equal to "Oyna"
                            else
                            {
                                Rectangle rectGForgeOynaDownBut = imageObjects.FindImageOnScreen(
                                    imageObjects.arrayGameForgeOynaDownBut, coordinates.RectGForgeOynaDownButtton());
                                if (rectGForgeOynaDownBut != Rectangle.Empty)
                                {
                                    inputGame.MouseMoveAndPressLeft(rectGForgeOynaDownBut.X,
                                        rectGForgeOynaDownBut.Y);

                                    //For Blocking Button Highlight Effect
                                    inputGame.MouseMove(rectGForgeOynaDownBut.X + 100,
                                        rectGForgeOynaDownBut.Y);
                                    TimerGame.SleepRandom(2000, 4000);

                                    Rectangle rectGForgeOynaUpDown = imageObjects.FindImageOnScreen(
                                        imageObjects.arrayGameForgeOynaUpBut, coordinates.RectGForgeOynaUpButton());

                                    if (rectGForgeOynaUpDown != Rectangle.Empty)
                                    {
                                        inputGame.MouseMoveAndPressLeft(rectGForgeOynaUpDown.X,
                                            rectGForgeOynaUpDown.Y + 100);

                                        TimerGame timerMetin2Icon = new TimerGame();

                                        while (!imageObjects.CompareTwoArrayAdvanced(imageObjects.arrayEntryScreen,
                                     screenShot.ImageArraySpecifiedArea(coordinates.RectEntryScreen()),
                                     ImageSensibilityLevel.SENSIBILTY_HIGH))
                                        {
                                            if (timerMetin2Icon.CheckDelayTimeInSecond(60))
                                            {
                                                if (ThreadGlobals.CheckGameIsStopped()) return false;
                                                inputGame.MouseMoveAndPressLeft(rectTargetsGForgeApp[program].X,
                                              rectTargetsGForgeApp[program].Y);
                                                TimerGame.SleepRandom(1000, 2000);
                                            }

                                            else
                                            {
                                                DebugPfCnsl.println("We couldn't get Metin2 Application");
                                                return false;
                                            }
                                        }


                                        //Focus metin2 application
                                        inputGame.MouseMoveAndPressLeft(coordinates.RectEntryScreen().X,
                                            coordinates.RectEntryScreen().Y);

                                        DebugPfCnsl.println("Metin2 Application is opened and entry screen detected");
                                        return true;



                                    }
                                    else
                                    {
                                        DebugPfCnsl.println("Game Forge Down Button not Detected");
                                    }
                                }
                                else
                                {
                                    DebugPfCnsl.println("rectGForgeOynaDownBut butonu tespit edilemedi");
                                }
                            }
                        
                       
                    }
                }
                else
                {
                    DebugPfCnsl.println("Game Forge Icon is Not Detected");
                }
            }
            return false;
        }
    }
}
