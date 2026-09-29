using MusicPlayerApp.Debugs;
using MusicPlayerApp.Sources.CharacterHandle;
using MusicPlayerApp.Sources.CoordinatesHandler;
using MusicPlayerApp.Sources.ImageHandle;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Drawing;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;


namespace MusicPlayerApp.Sources.GameHandler
{
    internal class PrepareFishing
    {
        private int xGrillFishes = 0;
        private int yGrillFishes = 0;
        private int pageGrillFisher = 1;
        ImageObjects imageObjects;
        GameInputHandler inputGame;
        CharSpecialThings charThings;
        GameObjectCoordinates coordinate;
        ScreenShotWinAPI screenShot;

        private readonly int NEEDED_WORM200_COUNT = 32;

        /// <summary>Balıkçıyı bulmak için azami özyineleme/deneme sayısı.</summary>
        private const int MAX_FISHER_RETRY = 6;

        /// <summary>Kızartma işleminin azami deneme sayısı (sonsuz döngü koruması).</summary>
        private const int MAX_GRILL_RETRY = 8;
        private const int FIRST_CAMPFIRE_REFRESH_AFTER_SECONDS = 25;
        private const int CAMPFIRE_REFRESH_PAUSE_MILLISECONDS = 10000;

        /// <summary>CheckFisherIsThere özyineleme derinliği sayacı.</summary>
        private static int fisherSearchDepth = 0;
        private const int MAX_FISHER_SEARCH_DEPTH = 6;

        private bool isGrillFishesFailed = false;
        private bool isStandaloneGrillAction;
        private Stopwatch standaloneGrillStopwatch;
        private bool standaloneGrillTimedOut;
        private const int MAX_STANDALONE_GRILL_SECONDS = 240;
        private Stopwatch standaloneWormStopwatch;
        private bool standaloneWormTimedOut;
        private const int MAX_STANDALONE_WORM_SECONDS = 240;
        private const int MAX_STANDALONE_ATMK_RESTARTS = 8;
        private bool standaloneAtmkRestartRequested;

        private sealed class GrillFireRefreshSchedule
        {
            public Rectangle CurrentFire { get; set; }
            public Stopwatch FirstFireStopwatch { get; private set; }
            public bool FirstFireRefreshHandled { get; set; }
            public bool KeepCharacterInPlace { get; private set; }

            public GrillFireRefreshSchedule(Rectangle firstFire, bool keepCharacterInPlace)
            {
                CurrentFire = firstFire;
                KeepCharacterInPlace = keepCharacterInPlace;
                FirstFireStopwatch = Stopwatch.StartNew();
            }
        }

        private sealed class FishIconForGrilling
        {
            public int[] Image { get; private set; }
            public Rectangle SampleRect { get; private set; }

            public FishIconForGrilling(int[] image, int width, int height)
            {
                Image = image;
                SampleRect = new Rectangle(0, 0, width, height);
            }
        }

        List<Rectangle> listWorm200;

        public PrepareFishing(ImageObjects imageObjects)
        {
            this.imageObjects = imageObjects;
            inputGame = new GameInputHandler();
            charThings = new CharSpecialThings(imageObjects);
            coordinate = new GameObjectCoordinates(imageObjects);
            screenShot = new ScreenShotWinAPI();
            listWorm200 = new List<Rectangle>();
        }

        public PrepareFishing(ImageObjects imageObjects,CharSpecialThings charthings)
        {
            this.imageObjects = imageObjects;
            inputGame = new GameInputHandler();
            charThings = charthings;
            coordinate = new GameObjectCoordinates(imageObjects);
            screenShot = new ScreenShotWinAPI();
            listWorm200 = new List<Rectangle>();
        }

        public void StartPrepareFishing()
        {
            DebugPfCnsl.println("StartPrepareFishing is running");
            ThreadGlobals.isPrepareFishingStarted = true;
            FindFisher();
            TimerGame.BeginFishCookingSlowdown();
            try
            {
                GrillFishingHandle();
            }
            finally
            {
                TimerGame.EndFishCookingSlowdown();
            }
            WormsHandle();
            ThreadGlobals.isPrepareFishingStarted = false;
        }

        /// <summary>
        /// Envanterde tespit edilebilen tüm balıkları yalnızca bir kez kızartır.
        /// Balık tutma hazırlığının devamındaki yem alma adımını özellikle çağırmaz.
        /// </summary>
        public bool GrillAllFishOnly(out string result)
        {
            return GrillAllFishOnlyCore(out result, false);
        }

        /// <summary>
        /// Envanter dolu kurtarmasında mevcut konum ve kamera açısından ayrılmadan pişirir.
        /// Balıkçı/ateş bulunamazsa karakteri veya kamerayı hareket ettirmeden başarısız olur.
        /// </summary>
        public bool GrillAllFishAtCurrentPositionOnly(out string result)
        {
            return GrillAllFishOnlyCore(out result, true);
        }

        private bool GrillAllFishOnlyCore(out string result, bool keepCharacterInPlace)
        {
            result = "Balık pişirme işlemi tamamlanamadı.";

            if (ThreadGlobals.isFishingStopped || ThreadGlobals.isBotPaused ||
                !ThreadGlobals.isLevelFarmStopped || !ThreadGlobals.isEnergyCristalStopped ||
                ThreadGlobals.IsAnyThreadActive())
            {
                result = "Önce çalışan bot işlemlerini durdurun.";
                return false;
            }

            int[] settingButtonImage = screenShot.ImageArraySpecifiedArea(coordinate.RectSettingButton());
            if (!imageObjects.CompareTwoArrayAdvanced(imageObjects.arraySettingButton,
                settingButtonImage, ImageSensibilityLevel.SENSIBILTY_MED))
            {
                result = "Metin2 karakter ekranı açık olmalı.";
                return false;
            }

            bool previousSettingButtonState = ThreadGlobals.isSettingButtonSeemed;
            bool previousHepsiSelectedState = ThreadGlobals.isHepsiSelected;
            isStandaloneGrillAction = true;
            standaloneGrillTimedOut = false;
            standaloneAtmkRestartRequested = false;
            standaloneGrillStopwatch = Stopwatch.StartNew();
            ThreadGlobals.isFishingStopped = false;
            ThreadGlobals.isSettingButtonSeemed = true;
            ThreadGlobals.isHepsiSelected = false;
            ThreadGlobals.isPrepareFishingStarted = true;
            TimerGame.BeginFishCookingSlowdown();

            try
            {
                if (!HoverAcrossAllInventorySlots())
                {
                    result = "Envanter yuvaları taranırken işlem durduruldu.";
                    return false;
                }

                List<FishIconForGrilling> allFishIcons = GetAllFishTypesForGrilling();
                int restartCount = 0;

                while (true)
                {
                    List<Rectangle[]> fishCoordinatesPageOne = new List<Rectangle[]>();
                    List<Rectangle[]> fishCoordinatesPageTwo = new List<Rectangle[]>();
                    List<Rectangle[]> fishCoordinatesPageThree = new List<Rectangle[]>();
                    List<Rectangle[]> fishCoordinatesPageFour = new List<Rectangle[]>();

                    charThings.OpenCloseInventory(true);
                    foreach (FishIconForGrilling fishIcon in allFishIcons)
                    {
                        if (ThreadGlobals.isFishingStopped || ThreadGlobals.isCharKilled)
                        {
                            result = "Balık pişirme işlemi durduruldu.";
                            return false;
                        }

                        fishCoordinatesPageOne.Add(charThings.CheckObjectInventory(fishIcon.Image,
                            fishIcon.SampleRect, InventoryPage.Page_1));
                        fishCoordinatesPageTwo.Add(charThings.CheckObjectInventory(fishIcon.Image,
                            fishIcon.SampleRect, InventoryPage.Page_2));
                        fishCoordinatesPageThree.Add(charThings.CheckObjectInventory(fishIcon.Image,
                            fishIcon.SampleRect, InventoryPage.Page_3));
                        fishCoordinatesPageFour.Add(charThings.CheckObjectInventory(fishIcon.Image,
                            fishIcon.SampleRect, InventoryPage.Page_4));
                    }
                    charThings.OpenCloseInventory(false);

                    int fishCount = CountTotalFish(fishCoordinatesPageOne) +
                        CountTotalFish(fishCoordinatesPageTwo) +
                        CountTotalFish(fishCoordinatesPageThree) +
                        CountTotalFish(fishCoordinatesPageFour);
                    if (fishCount == 0)
                    {
                        result = restartCount > 0
                            ? "Envanterde kalan balıklar da pişirildi."
                            : "Envanterde tanınan balık bulunamadı.";
                        return restartCount > 0;
                    }

                    // In-place recovery yalnızca mevcut görüntüdeki balıkçıya tıklamayı dener;
                    // NPC görünmüyorsa karakter/kamera hareketi yapmadan işlemi sonlandırır.
                    if (!CheckFisherShopPage())
                    {
                        if (keepCharacterInPlace)
                        {
                            string fisherShopError;
                            if (!OpenFisherShopAtCurrentPosition(out fisherShopError))
                            {
                                result = fisherShopError;
                                return false;
                            }
                        }
                        else
                        {
                            FindFisher();
                        }
                    }

                    if (ThreadGlobals.isFishingStopped || ThreadGlobals.isCharKilled)
                    {
                        result = "Balık pişirme işlemi durduruldu.";
                        return false;
                    }
                    if (standaloneGrillTimedOut || !CheckFisherShopPage())
                    {
                        result = standaloneGrillTimedOut
                            ? "Balıkçı araması zaman aşımına uğradı."
                            : "Balıkçı dükkânı açılamadı.";
                        return false;
                    }

                    BuyKampAtasiFromFisher(keepCharacterInPlace);
                    if (ThreadGlobals.isFishingStopped || ThreadGlobals.isCharKilled)
                    {
                        result = "Balık pişirme işlemi durduruldu.";
                        return false;
                    }

                    Rectangle campfire = keepCharacterInPlace
                        ? FireKampAtesiAtCurrentPosition()
                        : FireKampAtesi();
                    if (campfire == Rectangle.Empty)
                    {
                        if (standaloneAtmkRestartRequested)
                        {
                            standaloneAtmkRestartRequested = false;
                            restartCount++;
                            if (restartCount >= MAX_STANDALONE_ATMK_RESTARTS)
                            {
                                result = "Atmk onayı nedeniyle pişirme yeniden başlatma sınırına ulaşıldı.";
                                return false;
                            }
                            continue;
                        }

                        result = keepCharacterInPlace
                            ? "Mevcut konumdan kamp ateşi hazırlanamadı; karakter ve kamera hareket ettirilmedi."
                            : "Kamp ateşi hazırlanamadı; işlem durduruldu.";
                        return false;
                    }

                    bool completed = RetryGrillUntilDone(fishCoordinatesPageOne.ToArray(),
                        fishCoordinatesPageTwo.ToArray(), campfire,
                        fishCoordinatesPageThree.ToArray(), fishCoordinatesPageFour.ToArray(),
                        keepCharacterInPlace);
                    if (completed)
                    {
                        result = "Envanterde tanınan tüm balıklar pişirildi.";
                        return true;
                    }

                    if (ThreadGlobals.isFishingStopped || ThreadGlobals.isCharKilled)
                    {
                        result = "Balık pişirme işlemi durduruldu.";
                        return false;
                    }
                    if (!standaloneAtmkRestartRequested)
                    {
                        result = "Balıkların tamamı pişirilemedi; başka işlem başlatılmadı.";
                        return false;
                    }

                    standaloneAtmkRestartRequested = false;
                    restartCount++;
                    if (restartCount >= MAX_STANDALONE_ATMK_RESTARTS)
                    {
                        result = "Atmk onayı nedeniyle pişirme yeniden başlatma sınırına ulaşıldı.";
                        return false;
                    }

                    DebugPfCnsl.println("Atmk onayı kapatıldı; balık listesi baştan taranıp pişirme yeniden başlatılıyor.");
                }
            }
            catch (Exception ex)
            {
                FileLogger.Error("Tek seferlik balık pişirme işlemi başarısız oldu", ex);
                result = "Balık pişirme sırasında hata oluştu: " + ex.Message;
                return false;
            }
            finally
            {
                TimerGame.EndFishCookingSlowdown();
                bool wasStoppedByUser = ThreadGlobals.isFishingStopped;
                ThreadGlobals.isFishingStopped = true;
                if (!wasStoppedByUser)
                {
                    ThreadGlobals.isSettingButtonSeemed = previousSettingButtonState;
                    ThreadGlobals.isHepsiSelected = previousHepsiSelectedState;
                }
                ThreadGlobals.isPrepareFishingStarted = false;
                isStandaloneGrillAction = false;
                standaloneGrillStopwatch = null;
                standaloneAtmkRestartRequested = false;
            }
        }

        /// <summary>
        /// Solucanları yalnızca mevcut envanterden 200'lük yığınlara birleştirir ve
        /// 32 yığını hızlı erişim çubuğuna aktarır. Balıkçılık akışını başlatmaz.
        /// </summary>
        public bool PrepareWormsOnly(out string result)
        {
            result = "Solucan hazırlama işlemi tamamlanamadı.";

            if (ThreadGlobals.isFishingStopped || ThreadGlobals.isBotPaused ||
                !ThreadGlobals.isLevelFarmStopped || !ThreadGlobals.isEnergyCristalStopped ||
                ThreadGlobals.IsAnyThreadActive())
            {
                result = "Önce çalışan bot işlemlerini durdurun.";
                return false;
            }

            int[] settingButtonImage = screenShot.ImageArraySpecifiedArea(coordinate.RectSettingButton());
            if (!imageObjects.CompareTwoArrayAdvanced(imageObjects.arraySettingButton,
                settingButtonImage, ImageSensibilityLevel.SENSIBILTY_MED))
            {
                result = "Metin2 karakter ekranı açık olmalı.";
                return false;
            }

            bool previousSettingButtonState = ThreadGlobals.isSettingButtonSeemed;
            bool previousHepsiSelectedState = ThreadGlobals.isHepsiSelected;
            ThreadGlobals.isFishingStopped = false;
            ThreadGlobals.isSettingButtonSeemed = true;
            ThreadGlobals.isHepsiSelected = false;
            ThreadGlobals.isPrepareFishingStarted = true;
            standaloneWormTimedOut = false;
            standaloneWormStopwatch = Stopwatch.StartNew();

            try
            {
                charThings.OpenCloseInventory(true);
                int wormStackCount = charThings.CombineItemsTo200(imageObjects.arrayWorm200);

                if (ThreadGlobals.isFishingStopped || ThreadGlobals.isCharKilled)
                {
                    result = "Solucan hazırlama işlemi durduruldu.";
                    return false;
                }
                if (wormStackCount < 0)
                {
                    result = "Solucan yığınları 200'e dönüştürülemedi.";
                    return false;
                }

                if (wormStackCount < NEEDED_WORM200_COUNT)
                {
                    if (!CheckFisherShopPage())
                    {
                        FindFisher();
                    }

                    if (ThreadGlobals.isFishingStopped || ThreadGlobals.isCharKilled)
                    {
                        result = "Solucan hazırlama işlemi durduruldu.";
                        return false;
                    }
                    if (standaloneWormTimedOut || !CheckFisherShopPage())
                    {
                        result = standaloneWormTimedOut
                            ? "Solucan almak için balıkçı araması zaman aşımına uğradı."
                            : "Eksik solucanları almak için balıkçı dükkânı açılamadı.";
                        return false;
                    }

                    BuyFiftyWormAsNeeded(wormStackCount);
                    if (ThreadGlobals.isFishingStopped || ThreadGlobals.isCharKilled)
                    {
                        result = "Solucan hazırlama işlemi durduruldu.";
                        return false;
                    }

                    wormStackCount = charThings.CombineItemsTo200(imageObjects.arrayWorm200);
                    if (wormStackCount < NEEDED_WORM200_COUNT)
                    {
                        result = wormStackCount < 0
                            ? "Satın alınan solucanlar 200'lük yığınlara dönüştürülemedi."
                            : "Solucanlar " + wormStackCount + "/" + NEEDED_WORM200_COUNT +
                                " yığına tamamlanamadı; envanter alanını ve bakiyeyi kontrol edin.";
                        return false;
                    }
                }

                if (CheckFisherShopPage())
                {
                    CloseFisherShopPage();
                }

                if (!charThings.InsertObjectToSkillSlots(imageObjects.arrayWorm200,
                    coordinate.RectItemSlotSizeSample(), InsertCountSetting.INSERT_COUNT_32))
                {
                    result = ThreadGlobals.isFishingStopped || ThreadGlobals.isCharKilled
                        ? "Solucan hazırlama işlemi durduruldu."
                        : "32 solucan yığını hızlı erişim çubuğuna eklenemedi.";
                    return false;
                }

                result = "32 adet 200'lük solucan hızlı erişim çubuğuna eklendi. Balıkçılık başlatılmadı.";
                return true;
            }
            catch (Exception ex)
            {
                FileLogger.Error("Bağımsız solucan hazırlama işlemi başarısız oldu", ex);
                result = "Solucan hazırlama sırasında hata oluştu: " + ex.Message;
                return false;
            }
            finally
            {
                bool wasStoppedByUser = ThreadGlobals.isFishingStopped;
                charThings.OpenCloseInventory(false);
                ThreadGlobals.isFishingStopped = true;
                if (!wasStoppedByUser)
                {
                    ThreadGlobals.isSettingButtonSeemed = previousSettingButtonState;
                    ThreadGlobals.isHepsiSelected = previousHepsiSelectedState;
                }
                ThreadGlobals.isPrepareFishingStarted = false;
                standaloneWormStopwatch = null;
                standaloneWormTimedOut = false;
            }
        }

        private bool OpenFisherShopAtCurrentPosition(out string result)
        {
            result = "Mevcut kamera açısından balıkçı görünmüyor; karakter ve kamera hareket ettirilmedi.";
            if (CheckFisherShopPage()) return true;

            Rectangle gameArea = coordinate.RectMetin2GameScreen();
            bool[] targetFisher = imageObjects.RecordWantedColorAsBool(
                ColorGame.MAP_BALIKCI_GREEN, imageObjects.arrayFisherWords);
            Rectangle[] fisherMatches = imageObjects.FindAllImagesBoolArrays(
                targetFisher, coordinate.RectFisherSample(), gameArea, ColorGame.MAP_BALIKCI_GREEN);

            foreach (Rectangle fisherMatch in fisherMatches)
            {
                if (ThreadGlobals.isFishingStopped || ThreadGlobals.isCharKilled) return false;

                // Tıklama yalnızca mevcut ekranda görünen NPC adına gider; WASD ve kamera
                // tuşları bu kurtarma yolunda kullanılmaz.
                inputGame.MouseMoveAndPressLeft(
                    fisherMatch.X + fisherMatch.Width / 2,
                    fisherMatch.Y + fisherMatch.Height / 2);
                TimerGame.SleepRandom(700, 1000);

                for (int attempt = 0; attempt < 3; attempt++)
                {
                    if (ThreadGlobals.isFishingStopped || ThreadGlobals.isCharKilled) return false;
                    if (CheckFisherShopPage()) return true;

                    int[] targetOptions = imageObjects.RecordWantedColorIntArray(
                        ColorGame.CHAT_WHITE_COLOR,
                        screenShot.ImageArraySpecifiedArea(coordinate.RectFisherOptionsPage()));
                    int[] sourceOptions = imageObjects.RecordWantedColorIntArray(
                        ColorGame.CHAT_WHITE_COLOR, imageObjects.arrayBalikciAraEkran);
                    if (imageObjects.CompareTwoArrayAdvanced(sourceOptions, targetOptions,
                        ImageSensibilityLevel.SENSIBILTY_HIGH))
                    {
                        inputGame.MouseMoveAndPressLeft(coordinate.RectFisherOptionsPage().X,
                            coordinate.RectFisherOptionsPage().Y);
                        TimerGame.SleepRandom(1400, 1660);
                    }
                    else
                    {
                        TimerGame.SleepRandom(300, 500);
                    }
                }

                if (CheckFisherShopPage()) return true;
                // Etkileşim menüsü açılmadıysa NPC'ye gönderilmiş olası otomatik yürüme emrini iptal et.
                inputGame.KeyPress(KeyboardInput.ScanCodeShort.ESCAPE);
            }

            result = "Balıkçı dükkânı bulunduğu yerden açılamadı; karakter ve kamera hareket ettirilmedi.";
            return false;
        }

        private void FindFisher()
        {
            TimerGame timeGame = new TimerGame();
            TimerGame.SleepRandom(1500, 2000);

            int zigZagWalking = 0;
            int walkSide = 1;

            DebugPfCnsl.println("FindFisher is Started");

            charThings.OpenCloseSettingButton(false);
            charThings.OpenCloseInventory(false);

            charThings.BirdViewPerpective();
            timeGame.SetStartedSecondTime();

            while (!CheckFisherIsThere())
            {
                ThreadGlobals.WaitWhileBotPaused();
                if (ThreadGlobals.isFishingStopped || ThreadGlobals.isCharKilled)
                {
                    ThreadGlobals.DebugThreadGloablValues();
                    return;
                }
                if (isStandaloneGrillAction && standaloneGrillStopwatch != null &&
                    standaloneGrillStopwatch.Elapsed.TotalSeconds >= MAX_STANDALONE_GRILL_SECONDS)
                {
                    standaloneGrillTimedOut = true;
                    return;
                }
                if (standaloneWormStopwatch != null &&
                    standaloneWormStopwatch.Elapsed.TotalSeconds >= MAX_STANDALONE_WORM_SECONDS)
                {
                    standaloneWormTimedOut = true;
                    return;
                }
                    
                if (timeGame.CheckDelayTimeInSecond(40))
                {
                    Rectangle walkingRect = imageObjects.FindBorderAreaBetweenColors(
                        coordinate.RectWoodDetectinonArea(),
                        ColorGame.MIN_WOOD_VALUE, ColorGame.MAX_WOOD_VALUE);

                    int xPos, yPos;

                    if (walkingRect != Rectangle.Empty)
                    {
                        zigZagWalking = TimerGame.MakeRandomValue(0,
                            120);



                        if (walkSide == 1)
                        {
                            xPos = walkingRect.X + (walkingRect.Width / 2) + zigZagWalking;
                            yPos = walkingRect.Y;
                            walkSide = 2;
                        } else
                        {
                            xPos = walkingRect.X + (walkingRect.Width / 2) - zigZagWalking;
                            yPos = walkingRect.Y;
                            walkSide = 1;
                        }

                        inputGame.MouseMoveAndPressLeft(xPos, yPos);
                       // TimerGame.SleepRandom(100, 200);
                       // inputGame.KeyPress(KeyboardInput.ScanCodeShort.KEY_S);
                    }
                    else
                    {
                        xPos = coordinate.RectWoodDetectinonArea().X + (coordinate.RectWoodDetectinonArea().Width / 2);
                        yPos = coordinate.RectWoodDetectinonArea().Y;

                        inputGame.MouseMoveAndPressLeft(xPos, yPos);
                        //TimerGame.SleepRandom(100, 200);
                        //inputGame.KeyPress(KeyboardInput.ScanCodeShort.KEY_S);
                    }
                }
                else
                {
                    DebugPfCnsl.println("FindFisher function couldn't find Balikci");
                    charThings.SettingButtonClick(SettingButtonPrefers.CHAR_BUTTON);
                    while(!ThreadGlobals.isSettingButtonSeemed)
                    {
                        if (ThreadGlobals.isFishingStopped) return;
                    }
                    charThings.OpenCloseSettingButton(false);
                    charThings.OpenCloseInventory(false);

                    TimerGame.SleepRandom(4000, 5500);

                    inputGame.KeyDown(KeyboardInput.ScanCodeShort.KEY_A);
                    inputGame.KeyDown(KeyboardInput.ScanCodeShort.KEY_S);
                    TimerGame.SleepRandom(1000, 1500);
                    inputGame.KeyRelease(KeyboardInput.ScanCodeShort.KEY_A);
                    inputGame.KeyDown(KeyboardInput.ScanCodeShort.KEY_D);
                    TimerGame.SleepRandom(1000, 1500);
                    inputGame.KeyRelease(KeyboardInput.ScanCodeShort.KEY_D);
                    inputGame.KeyRelease(KeyboardInput.ScanCodeShort.KEY_S);
                    timeGame.SetStartedSecondTime();

                    charThings.OpenCloseSettingButton(false);
                    charThings.OpenCloseInventory(false);

                }
            }
        }

        private bool CheckFisherIsThere()
        {
            // Balıkçı ara ekranı bulunamadığında metot kendini yeniden çağırır; derinlik
            // sınırı olmadan StackOverflowException riski vardı.
            if (fisherSearchDepth >= MAX_FISHER_SEARCH_DEPTH)
            {
                FileLogger.Warning("CheckFisherIsThere: azami arama derinliğine ulaşıldı, " +
                    "balıkçı bulunamadı kabul ediliyor");
                fisherSearchDepth = 0;
                return false;
            }

            Stopwatch watch = new Stopwatch();
            watch.Start();

            bool[] targetGreenFisher = imageObjects.RecordWantedColorAsBool(ColorGame.MAP_BALIKCI_GREEN, imageObjects.arrayFisherWords);
          //  Console.WriteLine("targetGreenFisher length = " + targetGreenFisher.Length);
            bool[] scannableGreenFisher = imageObjects.RecordWantedColorAsBool(ColorGame.MAP_BALIKCI_GREEN,
                screenShot.ImageArraySpecifiedArea(coordinate.RectMetin2GameScreen()));

           // Console.WriteLine("RectMetin2GameScreen length = " +
           //    (coordinate.RectMetin2GameScreen().Width * coordinate.RectMetin2GameScreen().Height));

            for (int y = 0; y < coordinate.RectMetin2GameScreen().Height; y++)
            {
                for (int x = 0; x < coordinate.RectMetin2GameScreen().Width; x++)
                {
                    if (imageObjects.IsMatchBoolArrays(targetGreenFisher, coordinate.RectFisherSample(),
                        scannableGreenFisher, coordinate.RectMetin2GameScreen(), x, y))
                    {
                        DebugPfCnsl.println("Elapsed Time = " + watch.ElapsedMilliseconds);





                        int xClickPos = x + coordinate.RectMetin2GameScreen().X + (coordinate.RectFisherSample().Width / 2) + CheckGameCoordinate.currentScreenGamePoint.X;
                        int yClickPos = y +coordinate.RectMetin2GameScreen().Y + (coordinate.RectFisherSample().Height/2) + CheckGameCoordinate.currentScreenGamePoint.Y;
                        inputGame.MouseMoveAndPressLeft(xClickPos, yClickPos);

                        //Wait for activating  fisher options page
                        TimerGame.SleepRandom(700,1000);

                        int[] targetWhiteBalikciAraEkran = imageObjects.RecordWantedColorIntArray(ColorGame.CHAT_WHITE_COLOR,
                            screenShot.ImageArraySpecifiedArea(coordinate.RectFisherOptionsPage()));

                        int[] sourceWhiteBalikciAraEkran = imageObjects.RecordWantedColorIntArray(ColorGame.CHAT_WHITE_COLOR,
                            imageObjects.arrayBalikciAraEkran);

                        if (imageObjects.CompareTwoArrayAdvanced(sourceWhiteBalikciAraEkran, targetWhiteBalikciAraEkran,
                            ImageSensibilityLevel.SENSIBILTY_HIGH))
                        {
                            inputGame.MouseMoveAndPressLeft(coordinate.RectFisherOptionsPage().X,
                                coordinate.RectFisherOptionsPage().Y);
                            //Wait for opening fisher shop page
                            TimerGame.SleepRandom(1400, 1660);
                          //  DebugPfCnsl.println("Elapsed 222  Time = " + watch.ElapsedMilliseconds);
                            while (!CheckFisherShopPage())
                            {
                                if (ThreadGlobals.isFishingStopped || ThreadGlobals.isCharKilled)
                                {
                                    ThreadGlobals.DebugThreadGloablValues();
                                    return false;
                                }
                  
                                inputGame.MouseMoveAndPressLeft(coordinate.RectFisherOptionsPage().X,
                             coordinate.RectFisherOptionsPage().Y);
                                //Wait for opening fisher shop page
                                TimerGame.SleepRandom(1400, 1660);
                            }
                            fisherSearchDepth = 0;
                            return true;
                        }
                        else
                        {
                            inputGame.KeyDown(KeyboardInput.ScanCodeShort.KEY_W);
                            TimerGame.SleepRandom(300, 500);
                            inputGame.KeyRelease(KeyboardInput.ScanCodeShort.KEY_W);

                            fisherSearchDepth++;
                            try
                            {
                                return CheckFisherIsThere();
                            }
                            finally
                            {
                                fisherSearchDepth--;
                            }
                        }
                            
                    }
                }

                
            }
            DebugPfCnsl.println(" Couldn't find Elapsed Time = " + watch.ElapsedMilliseconds);



            return false;
        }

        private void WormsHandle()
        {
            TimerGame timeGame = new TimerGame();
           // Console.WriteLine("calisti wwww");
            if (CheckFisherShopPage())
            {
              //  Console.WriteLine("calisti 111");
                if (CombineAndCountWorms())
                {
                   // Console.WriteLine("calisti4232");
                    CloseFisherShopPage();
                   // Console.WriteLine("calisti");
                    if (charThings.InsertObjectToSkillSlots(imageObjects.arrayWorm200, coordinate.RectItemSlotSizeSample(),
                        InsertCountSetting.INSERT_COUNT_32
                        ))
                    {
                        GoToFishPlace();
                        listWorm200.Clear();
                        charThings.OpenCloseInventory(false);
                        
                    }
                    else
                    {
                        listWorm200.Clear();
                        charThings.OpenCloseInventory(false);
                    }
                   
                }
                else
                {
                    DebugPfCnsl.println("CombineAndCountWorms returned false ...");

                }
            }
            else
            {
                
                // Özyineleme yerine sınırlı deneme (StackOverflow koruması).
                for (int retry = 0; retry < MAX_FISHER_RETRY; retry++)
                {
                    if (ThreadGlobals.isFishingStopped || ThreadGlobals.isCharKilled) return;
                    if (!CheckFisherIsThere()) return;

                    WormsHandle();
                    return;
                }
            }
        }

        private bool CombineAndCountWorms()
        {
            TimerGame timerCombine = new TimerGame();

            DebugPfCnsl.println("CombineAndCountWorms function is running");

            listWorm200.Clear();
                int worms200CharHave = charThings.CombineItemsTo200(imageObjects.arrayWorm200);
                DebugPfCnsl.println("worms200CharHave result = " + worms200CharHave);
                while (worms200CharHave < NEEDED_WORM200_COUNT)
                {
                if (timerCombine.CheckDelayTimeInSecond(500))
                {
                    if(worms200CharHave != -1)
                    {
                        if (ThreadGlobals.isFishingStopped || ThreadGlobals.isCharKilled)
                        {
                            return false;
                        }
                        BuyFiftyWormAsNeeded(worms200CharHave);
                        listWorm200.Clear();
                        worms200CharHave = charThings.CombineItemsTo200(imageObjects.arrayWorm200);
                    }
                    else
                    {
                        worms200CharHave = charThings.CombineItemsTo200(imageObjects.arrayWorm200);
                    }
                    
                }
                else
                {
                    DebugPfCnsl.println("CombineAndCountWorms function time is elapsed");
                    return false;
                }
                }
            
            return true;
           
           
        }
        /// <summary>
        /// Moves the pointer over each slot in all four inventory pages without clicking.
        /// Page tabs are clicked only to change pages; inventory item slots are never clicked.
        /// </summary>
        private bool HoverAcrossAllInventorySlots()
        {
            if (ThreadGlobals.isFishingStopped || ThreadGlobals.isCharKilled) return false;

            charThings.OpenCloseInventory(true);
            InventoryPage[] pages =
            {
                InventoryPage.Page_1,
                InventoryPage.Page_2,
                InventoryPage.Page_3,
                InventoryPage.Page_4
            };
            Rectangle firstSlot = coordinate.RectFirstSlotPlace();

            foreach (InventoryPage page in pages)
            {
                if (ThreadGlobals.isFishingStopped || ThreadGlobals.isCharKilled)
                {
                    charThings.OpenCloseInventory(false);
                    return false;
                }

                charThings.ClickWantedInventoryPage(page);
                for (int row = 0; row < 9; row++)
                {
                    for (int column = 0; column < 5; column++)
                    {
                        if (ThreadGlobals.isFishingStopped || ThreadGlobals.isCharKilled)
                        {
                            charThings.OpenCloseInventory(false);
                            return false;
                        }

                        int x = firstSlot.X + (GameObjectCoordinates.DISTANCE_BTWN_INV_SLOTS * column)
                            + firstSlot.Width / 2;
                        int y = firstSlot.Y + (GameObjectCoordinates.DISTANCE_BTWN_INV_SLOTS * row)
                            + firstSlot.Height / 2;
                        inputGame.MouseMove(x, y);
                    }
                }
            }

            charThings.OpenCloseInventory(false);
            return true;
        }

        private void FinishWormPurchaseHover(bool purchasedWorms)
        {
            if (!purchasedWorms) return;
            CloseFisherShopPage();
            HoverAcrossAllInventorySlots();
        }

        private void FinishCampfirePurchaseHover(bool purchasedCampfire)
        {
            if (!purchasedCampfire) return;
            CloseFisherShopPage();
            HoverAcrossAllInventorySlots();
        }

        private void BuyFiftyWormAsNeeded(int wormsCharHave)
        {
            if(CheckFisherShopPage())
            {
                int neededWorms = NEEDED_WORM200_COUNT - wormsCharHave;
                bool purchasedWorms = false;

                for (int i = 0; i < neededWorms * 4; i++)
                {
                    ThreadGlobals.WaitWhileBotPaused();
                    if (ThreadGlobals.isFishingStopped || ThreadGlobals.isCharKilled)
                    {
                        FinishWormPurchaseHover(purchasedWorms);
                        return;
                    }
                    inputGame.MouseMoveAndPressRight(coordinate.PointFisherShopFiftyWorm().X,
                       coordinate.PointFisherShopFiftyWorm().Y);
                    purchasedWorms = true;

                    TimerGame.SleepRandom(500, 600);

                    if(imageObjects.CompareTwoArrayAdvanced(imageObjects.arrayFullDialogInFisherShop,
                        screenShot.ImageArraySpecifiedArea(coordinate.RectYereAtmaAlgilama()),
                        ImageSensibilityLevel.SENSIBILTY_HIGH))
                    {
                        DebugPfCnsl.println("There aren't any place to buy worm");
                        inputGame.KeyPress(KeyboardInput.ScanCodeShort.ESCAPE);
                        FinishWormPurchaseHover(purchasedWorms);
                        return;
                    }
                }

                FinishWormPurchaseHover(purchasedWorms);
            }
            else
            {
                DebugPfCnsl.println("Fisher shop page is not open");
                return;
            }
           
        }

        private void BuyKampAtasiFromFisher(bool keepCharacterInPlace = false)
        {
            DebugPfCnsl.println("BuyKampAtasiFromFisher is called");
            TimerGame timerBuyKamp = new TimerGame();
            bool purchasedCampfire = false;
            if (CheckFisherShopPage())
            {
                
                //charThings.ClickWantedInventoryPage(InventoryPage.Page_1);
                
                while (charThings.CheckObjectInventory(imageObjects.arrayKampIcon, coordinate.RectItemSlotSizeSample(), InventoryPage.Page_1).Length <= 0 &&
                    charThings.CheckObjectInventory(imageObjects.arrayKampIcon, coordinate.RectItemSlotSizeSample(), InventoryPage.Page_2).Length <= 0 &&
                    charThings.CheckObjectInventory(imageObjects.arrayKampIcon, coordinate.RectItemSlotSizeSample(), InventoryPage.Page_3).Length <= 0 &&
                    charThings.CheckObjectInventory(imageObjects.arrayKampIcon, coordinate.RectItemSlotSizeSample(), InventoryPage.Page_4).Length <= 0)
                {
                    if (timerBuyKamp.CheckDelayTimeInSecond(6))
                    {
                        if (ThreadGlobals.isFishingStopped || ThreadGlobals.isCharKilled)
                        {
                            FinishCampfirePurchaseHover(purchasedCampfire);
                            return;
                        }

                        inputGame.MouseMoveAndPressRight(coordinate.PointFisherShopKampAtesi().X,
                            coordinate.PointFisherShopKampAtesi().Y);
                        purchasedCampfire = true;
                        TimerGame.SleepRandom(500, 800);
                    }
                    else
                    {
                        DebugPfCnsl.println("kamp atesi alinamadi");
                        FinishCampfirePurchaseHover(purchasedCampfire);
                        return;
                    }
                }

               /* while (imageObjects.FindAllImagesOnScreen(imageObjects.arrayKampIcon,
                    coordinate.RectItemSlotSizeSample(), coordinate.RectInventoryPageArea()).Length < 1)
                {
                    
                    else
                    {
                        charThings.ClickWantedInventoryPage(InventoryPage.Page_2);
                        timerBuyKamp.SetStartedSecondTime();
                        if (timerBuyKamp.CheckDelayTimeInSecond(15))
                        {
                            if (ThreadGlobals.isStopped && ThreadGlobals.isCharKilled) return;

                            inputGame.MouseMoveAndPressRight(coordinate.PointFisherShopKampAtesi().X,
                                coordinate.PointFisherShopKampAtesi().Y);
                            TimerGame.SleepRandom(500, 800);
                        }
                    }
                    
                }*/
            }
            else
            {
                if (keepCharacterInPlace)
                {
                    DebugPfCnsl.println("Balıkçı dükkânı kapalı; in-place pişirmede karakter hareket ettirilmiyor");
                    return;
                }

                // Özyineleme yerine sınırlı deneme.
                for (int retry = 0; retry < MAX_FISHER_RETRY; retry++)
                {
                    if (ThreadGlobals.isFishingStopped || ThreadGlobals.isCharKilled) return;
                    if (!CheckFisherIsThere()) return;

                    BuyKampAtasiFromFisher();
                    return;
                }
            }

            FinishCampfirePurchaseHover(purchasedCampfire);
        }
        /// <summary>
        /// Balıkçı dükkanı açıkken kamp ateşi alıp envanterdeki balıkları kızartır.
        /// </summary>
        /// <remarks>
        /// HATA DÜZELTİLDİ: eski sürüm `GetFishTypesForGrilling()` sonucunu sabit
        /// `[0]`, `[1]`, `[2]` indeksleriyle kontrol ediyordu. Kullanıcı yalnızca
        /// Kurbağa/Kadife/Denizkızı seçtiğinde bu indeksler null kalıyor ve
        /// <see cref="NullReferenceException"/> ile bot ölüyordu. Ayrıca Denizkızı
        /// kızartma listesine hiç dahil değildi. Artık seçili balık türleri dinamik
        /// olarak listeleniyor ve toplam sayıya bakılıyor.
        /// </remarks>
        private void GrillFishingHandle()
        {
            DebugPfCnsl.println("GrillFishingHandle func is called");

            if (CheckFisherShopPage())
            {
                if (!HoverAcrossAllInventorySlots()) return;
                BuyKampAtasiFromFisher();

                if (!ThreadGlobals.isHepsiSelected)
                {
                    List<FishIconForGrilling> selectedFishIcons = GetFishTypesForGrilling();
                    if (selectedFishIcons.Count == 0)
                    {
                        DebugPfCnsl.println("Kızartılacak balık seçilmemiş, kızartma atlandı");
                        return;
                    }

                    List<Rectangle[]> fishCoordinatesPageOne = new List<Rectangle[]>();
                    List<Rectangle[]> fishCoordinatesPageTwo = new List<Rectangle[]>();
                    List<Rectangle[]> fishCoordinatesPageThree = new List<Rectangle[]>();
                    List<Rectangle[]> fishCoordinatesPageFour = new List<Rectangle[]>();

                    foreach (FishIconForGrilling fishIcon in selectedFishIcons)
                    {
                        if (ThreadGlobals.isFishingStopped || ThreadGlobals.isCharKilled) return;

                        fishCoordinatesPageOne.Add(charThings.CheckObjectInventory(fishIcon.Image,
                            fishIcon.SampleRect, InventoryPage.Page_1));
                        fishCoordinatesPageTwo.Add(charThings.CheckObjectInventory(fishIcon.Image,
                            fishIcon.SampleRect, InventoryPage.Page_2));
                        fishCoordinatesPageThree.Add(charThings.CheckObjectInventory(fishIcon.Image,
                            fishIcon.SampleRect, InventoryPage.Page_3));
                        fishCoordinatesPageFour.Add(charThings.CheckObjectInventory(fishIcon.Image,
                            fishIcon.SampleRect, InventoryPage.Page_4));
                    }

                    // Tüm seçili balık türleri ve envanter sayfaları taranır.
                    if (CountTotalFish(fishCoordinatesPageOne) + CountTotalFish(fishCoordinatesPageTwo) +
                        CountTotalFish(fishCoordinatesPageThree) + CountTotalFish(fishCoordinatesPageFour) == 0)
                    {
                        DebugPfCnsl.println("Envanterde kızartılacak balık yok");
                        return;
                    }

                    Rectangle rectKampGreenResult = FireKampAtesi();
                    if (rectKampGreenResult != Rectangle.Empty)
                    {
                        RetryGrillUntilDone(fishCoordinatesPageOne.ToArray(),
                            fishCoordinatesPageTwo.ToArray(), rectKampGreenResult,
                            fishCoordinatesPageThree.ToArray(), fishCoordinatesPageFour.ToArray());
                    }
                }
                else
                {
                    // "Hepsi" seçiliyken envanterdeki her nesne ateşe sürüklenir.
                    Rectangle rectKampGreenResult = FireKampAtesi();
                    if (rectKampGreenResult != Rectangle.Empty)
                    {
                        RetryGrillUntilDone(null, null, rectKampGreenResult);
                    }
                }
            }
            else
            {
                DebugPfCnsl.println("GrillFishingHandle: balıkçı dükkanı açık değil");
                // Eski sürüm burada kendini özyinelemeli çağırıyordu; derinlik sınırı eklenmiş
                // döngüye çevrildi (StackOverflow riski kaldırıldı).
                for (int retry = 0; retry < MAX_FISHER_RETRY; retry++)
                {
                    if (ThreadGlobals.isFishingStopped || ThreadGlobals.isCharKilled) return;
                    if (!CheckFisherIsThere()) return;

                    GrillFishingHandle();
                    return;
                }
            }
        }

        /// <summary>
        /// Kızartma başarısız olursa kamp ateşini tazeleyip yeniden dener.
        /// </summary>
        /// <remarks>
        /// Eski kod `while (!GrillFishes(...))` döngüsünde ateş bulunamazsa sonsuza kadar
        /// dönüyordu. Artık deneme sayısı sınırlı ve her turda botun durdurulup
        /// durdurulmadığı kontrol ediliyor.
        /// </remarks>
        private bool DismissAtmkConfirmation()
        {
            Rectangle yereAtmaArea = coordinate.RectYereAtmaAlgilama();
            Rectangle atmkSearchArea = new Rectangle(yereAtmaArea.X - 48, yereAtmaArea.Y,
                yereAtmaArea.Width + 96, yereAtmaArea.Height);
            Rectangle atmkDialog = imageObjects.FindImageInArea(imageObjects.arrayAtmkDialog,
                new Rectangle(0, 0, 93, 15), atmkSearchArea);

            if (atmkDialog == Rectangle.Empty)
            {
                return false;
            }

            DebugPfCnsl.println("Atmk onay penceresi algılandı; ESC ile kapatılıyor.");
            inputGame.KeyPress(KeyboardInput.ScanCodeShort.ESCAPE);
            TimerGame.SleepRandom(150, 250);
            if (isStandaloneGrillAction)
            {
                standaloneAtmkRestartRequested = true;
            }
            return true;
        }

        private bool RetryGrillUntilDone(Rectangle[][] pageOne, Rectangle[][] pageTwo,
            Rectangle kampAtesiGreen, Rectangle[][] pageThree = null, Rectangle[][] pageFour = null,
            bool keepCharacterInPlace = false)
        {
            GrillFireRefreshSchedule fireSchedule = new GrillFireRefreshSchedule(
                kampAtesiGreen, keepCharacterInPlace);

            for (int attempt = 0; attempt < MAX_GRILL_RETRY; attempt++)
            {
                if (ThreadGlobals.isFishingStopped || ThreadGlobals.isCharKilled || standaloneGrillTimedOut)
                    return false;

                if (GrillFishes(pageOne, pageTwo, fireSchedule, pageThree, pageFour))
                {
                    return true;
                }
                if (standaloneAtmkRestartRequested)
                {
                    return false;
                }

                DebugPfCnsl.println("Kızartma tamamlanamadı, kamp ateşi yenileniyor (deneme " + (attempt + 1) + ")");

                if (keepCharacterInPlace)
                {
                    string fisherShopError;
                    if (!CheckFisherShopPage() &&
                        !OpenFisherShopAtCurrentPosition(out fisherShopError))
                    {
                        DebugPfCnsl.println(fisherShopError);
                        return false;
                    }
                }
                else if (!CheckFisherIsThere())
                {
                    return false;
                }
                if (!CheckFisherShopPage())
                {
                    continue;
                }

                BuyKampAtasiFromFisher(keepCharacterInPlace);
                Rectangle newFire = keepCharacterInPlace
                    ? FireKampAtesiAtCurrentPosition()
                    : FireKampAtesi();
                if (newFire != Rectangle.Empty)
                {
                    fireSchedule.CurrentFire = newFire;
                }
            }

            FileLogger.Warning("Kızartma " + MAX_GRILL_RETRY + " denemede tamamlanamadı, işleme devam ediliyor");
            return false;
        }

        private bool EnsureScheduledCampfireRefresh(GrillFireRefreshSchedule fireSchedule,
            TimerGame grillTimer, out bool fireRefreshed)
        {
            fireRefreshed = false;
            if (fireSchedule.FirstFireRefreshHandled ||
                fireSchedule.FirstFireStopwatch.Elapsed.TotalSeconds < FIRST_CAMPFIRE_REFRESH_AFTER_SECONDS)
            {
                return true;
            }

            fireSchedule.FirstFireRefreshHandled = true;
            FileLogger.Info("İlk kamp ateşinden 25 saniye geçti; bot işlemleri 10 saniyeliğine duraklatılıyor.");
            if (!PauseAllBotOperationsForCampfireRefresh()) return false;
            if (ThreadGlobals.isFishingStopped || ThreadGlobals.isCharKilled) return false;

            if (!CheckFisherShopPage())
            {
                if (fireSchedule.KeepCharacterInPlace)
                {
                    string fisherShopError;
                    if (!OpenFisherShopAtCurrentPosition(out fisherShopError))
                    {
                        FileLogger.Warning(fisherShopError);
                        return false;
                    }
                }
                else
                {
                    FindFisher();
                }
            }

            if (ThreadGlobals.isFishingStopped || ThreadGlobals.isCharKilled || !CheckFisherShopPage())
                return false;

            BuyKampAtasiFromFisher(fireSchedule.KeepCharacterInPlace);
            if (ThreadGlobals.isFishingStopped || ThreadGlobals.isCharKilled) return false;

            Rectangle newFire = fireSchedule.KeepCharacterInPlace
                ? FireKampAtesiAtCurrentPosition()
                : FireKampAtesi();
            if (newFire == Rectangle.Empty)
            {
                FileLogger.Warning("10 saniyelik duraklamadan sonra yeni kamp ateşi yakılamadı; pişirme durduruluyor.");
                return false;
            }

            fireSchedule.CurrentFire = newFire;
            grillTimer.SetStartedSecondTime();
            fireRefreshed = true;
            FileLogger.Info("Yeni kamp ateşi yakıldı; balık pişirmeye devam ediliyor.");
            return true;
        }

        private bool PauseAllBotOperationsForCampfireRefresh()
        {
            bool pauseStartedByThisMethod = false;
            if (!ThreadGlobals.isBotPaused)
            {
                pauseStartedByThisMethod = ThreadGlobals.PauseBot();
                if (pauseStartedByThisMethod) TimerGame.PauseBotTimers();
            }

            if (!ThreadGlobals.isBotPaused && !pauseStartedByThisMethod)
            {
                FileLogger.Warning("Kamp ateşi yenilemesi için bot duraklatılamadı.");
                return false;
            }

            Stopwatch pauseTimer = Stopwatch.StartNew();
            try
            {
                while (pauseTimer.ElapsedMilliseconds < CAMPFIRE_REFRESH_PAUSE_MILLISECONDS)
                {
                    if (ThreadGlobals.isFishingStopped || ThreadGlobals.isCharKilled) return false;

                    // Ctrl+P ile duraklama erken kaldırılırsa, istenen 10 saniye tamamlanana
                    // kadar bot işlerini yeniden duraklat.
                    if (!ThreadGlobals.isBotPaused)
                    {
                        if (!ThreadGlobals.PauseBot()) return false;
                        TimerGame.PauseBotTimers();
                        pauseStartedByThisMethod = true;
                    }

                    int remaining = (int)Math.Min(100L,
                        CAMPFIRE_REFRESH_PAUSE_MILLISECONDS - pauseTimer.ElapsedMilliseconds);
                    if (remaining > 0) System.Threading.Thread.Sleep(remaining);
                }

                return !ThreadGlobals.isFishingStopped && !ThreadGlobals.isCharKilled;
            }
            finally
            {
                if (pauseStartedByThisMethod)
                {
                    TimerGame.ResumeBotTimers();
                    ThreadGlobals.ResumeBot();
                }
            }
        }

        /// <summary>Bir sayfadaki tüm balık türlerinin toplam adetini sayar.</summary>
        private static int CountTotalFish(List<Rectangle[]> fishRectanglesPerPage)
        {
            int total = 0;
            if (fishRectanglesPerPage == null)
            {
                return 0;
            }
            foreach (Rectangle[] rectangles in fishRectanglesPerPage)
            {
                if (rectangles != null)
                {
                    total += rectangles.Length;
                }
            }
            return total;
        }

        private bool GrillFishes(Rectangle[][] rectPageOne, Rectangle[][] rectPageTwo,
            GrillFireRefreshSchedule fireSchedule, Rectangle[][] rectPageThree = null, Rectangle[][] rectPageFour = null)
        {
            DebugPfCnsl.println("GrillFishes func is called");
            TimerGame timerGrillFishes = new TimerGame();

            if (!ThreadGlobals.isHepsiSelected)
            {
                Rectangle[][][] fishRectanglesByPage =
                {
                    rectPageOne,
                    rectPageTwo,
                    rectPageThree,
                    rectPageFour
                };

                for (int pageIndex = 0; pageIndex < fishRectanglesByPage.Length; pageIndex++)
                {
                    Rectangle[][] pageFish = fishRectanglesByPage[pageIndex];
                    if (pageFish == null || pageFish.Length == 0) continue;

                    charThings.ClickWantedInventoryPage((InventoryPage)pageIndex);
                    foreach (Rectangle[] fishTypeRectangles in pageFish)
                    {
                        if (fishTypeRectangles == null) continue;
                        foreach (Rectangle rectFish in fishTypeRectangles)
                        {
                            int[] fishImageBeforeGrill = screenShot.ImageArraySpecifiedArea(rectFish);
                            int[] slotImageAfterGrill = fishImageBeforeGrill;

                            while (imageObjects.CompareTwoArrayAdvanced(fishImageBeforeGrill,
                                slotImageAfterGrill, ImageSensibilityLevel.SENSIBILTY_HIGH))
                            {
                                if (ThreadGlobals.isFishingStopped || ThreadGlobals.isCharKilled) return false;
                                bool fireRefreshed;
                                if (!EnsureScheduledCampfireRefresh(fireSchedule, timerGrillFishes, out fireRefreshed))
                                    return false;
                                if (fireRefreshed)
                                {
                                    charThings.OpenCloseInventory(true);
                                    charThings.ClickWantedInventoryPage((InventoryPage)pageIndex);
                                }
                                if (!timerGrillFishes.CheckDelayTimeInSecond(60))
                                {
                                    DebugPfCnsl.println("GrillFishes func CheckDelayTimeSecond else statement started");
                                    return false;
                                }

                                inputGame.MouseMoveAndPressLeft(rectFish.X + rectFish.Width / 2, rectFish.Y);
                                inputGame.MouseMoveAndPressLeft(fireSchedule.CurrentFire.X + fireSchedule.CurrentFire.Width / 2,
                                    fireSchedule.CurrentFire.Y + fireSchedule.CurrentFire.Height / 2);
                                TimerGame.SleepRandom(200, 400);

                                if (DismissAtmkConfirmation()) return false;
                                if (imageObjects.CompareTwoArrayAdvanced(imageObjects.arrayYereAtmaDialog,
                                    screenShot.ImageArraySpecifiedArea(coordinate.RectYereAtmaAlgilama()),
                                    ImageSensibilityLevel.SENSIBILTY_HIGH))
                                {
                                    inputGame.KeyPress(KeyboardInput.ScanCodeShort.ESCAPE);
                                    return false;
                                }

                                slotImageAfterGrill = screenShot.ImageArraySpecifiedArea(rectFish);
                            }
                        }
                    }
                }
            }
            else
            {
                for (; pageGrillFisher <= 4; pageGrillFisher++)
                {
                    if (isGrillFishesFailed)
                    {
                        pageGrillFisher = 1;
                        xGrillFishes = 0;
                        yGrillFishes = 0;
                    }

                    charThings.ClickWantedInventoryPage((InventoryPage)(pageGrillFisher - 1));
                    for (; yGrillFishes < 9; yGrillFishes++)
                    {
                        for (; xGrillFishes < 5; xGrillFishes++)
                        {
                            if (ThreadGlobals.isFishingStopped || ThreadGlobals.isCharKilled) return false;
                            bool fireRefreshed;
                            if (!EnsureScheduledCampfireRefresh(fireSchedule, timerGrillFishes, out fireRefreshed))
                                return false;
                            if (fireRefreshed)
                            {
                                charThings.OpenCloseInventory(true);
                                charThings.ClickWantedInventoryPage((InventoryPage)(pageGrillFisher - 1));
                            }
                            if (!timerGrillFishes.CheckDelayTimeInSecond(60))
                            {
                                isGrillFishesFailed = true;
                                return false;
                            }

                            Rectangle rectScanSlot = new Rectangle(
                                coordinate.RectFirstSlotPlace().X + (GameObjectCoordinates.DISTANCE_BTWN_INV_SLOTS * xGrillFishes),
                                coordinate.RectFirstSlotPlace().Y + (GameObjectCoordinates.DISTANCE_BTWN_INV_SLOTS * yGrillFishes),
                                coordinate.RectFirstSlotPlace().Width, coordinate.RectFirstSlotPlace().Height);
                            int[] targetSlotImage = screenShot.ImageArraySpecifiedArea(rectScanSlot);

                            if (!imageObjects.CompareTwoArrayAdvanced(imageObjects.arrayEmptySlotPlace,
                                targetSlotImage, ImageSensibilityLevel.SENSIBILTY_HIGH))
                            {
                                inputGame.MouseMoveAndPressLeft(rectScanSlot.X + rectScanSlot.Width / 2,
                                    rectScanSlot.Y);
                                inputGame.MouseMoveAndPressLeft(fireSchedule.CurrentFire.X + fireSchedule.CurrentFire.Width / 2,
                                    fireSchedule.CurrentFire.Y + fireSchedule.CurrentFire.Height / 2);
                                TimerGame.SleepRandom(200, 400);

                                if (DismissAtmkConfirmation()) return false;
                                if (imageObjects.CompareTwoArrayAdvanced(imageObjects.arrayYereAtmaDialog,
                                    screenShot.ImageArraySpecifiedArea(coordinate.RectYereAtmaAlgilama()),
                                    ImageSensibilityLevel.SENSIBILTY_HIGH))
                                {
                                    inputGame.KeyPress(KeyboardInput.ScanCodeShort.ESCAPE);
                                    return false;
                                }
                            }
                        }
                        xGrillFishes = 0;
                    }
                    yGrillFishes = 0;
                }
            }

            isGrillFishesFailed = false;
            pageGrillFisher = 1;
            xGrillFishes = 0;
            yGrillFishes = 0;
            return true;
        }

        private Rectangle FireKampAtesiAtCurrentPosition()
        {
            CloseFisherShopPage();

            Rectangle[] campfireItems = null;
            InventoryPage[] pages =
            {
                InventoryPage.Page_1,
                InventoryPage.Page_2,
                InventoryPage.Page_3,
                InventoryPage.Page_4
            };
            foreach (InventoryPage page in pages)
            {
                campfireItems = charThings.CheckObjectInventory(imageObjects.arrayKampIcon,
                    coordinate.RectItemSlotSizeSample(), page);
                if (campfireItems.Length > 0) break;
            }

            if (campfireItems == null || campfireItems.Length == 0)
            {
                DebugPfCnsl.println("Mevcut envanterde kamp ateşi yok; karakter/kamera hareket ettirilmedi");
                return Rectangle.Empty;
            }

            bool[] campfireWord = imageObjects.RecordWantedColorAsBool(
                ColorGame.MAP_CAMP_FIRE_GREEN, imageObjects.arrayKampAtesiWords);
            Rectangle gameArea = coordinate.RectMetin2GameScreen();
            Rectangle[] visibleFire = imageObjects.FindAllImagesBoolArrays(campfireWord,
                coordinate.RectKampGreenSample(), gameArea, ColorGame.MAP_CAMP_FIRE_GREEN);
            if (visibleFire.Length > 0) return visibleFire[0];

            if (ThreadGlobals.isFishingStopped || ThreadGlobals.isCharKilled) return Rectangle.Empty;
            inputGame.MouseMoveAndPressRight(
                campfireItems[0].X + campfireItems[0].Width / 2,
                campfireItems[0].Y);
            TimerGame.SleepRandom(300, 400);
            if (DismissAtmkConfirmation()) return Rectangle.Empty;

            // Ateşin adını yalnızca mevcut kamera görüntüsünde ara; S/W gibi kamera
            // tuşları veya karakter hareketi kullanılmaz.
            for (int attempt = 0; attempt < 8; attempt++)
            {
                if (ThreadGlobals.isFishingStopped || ThreadGlobals.isCharKilled) return Rectangle.Empty;
                visibleFire = imageObjects.FindAllImagesBoolArrays(campfireWord,
                    coordinate.RectKampGreenSample(), gameArea, ColorGame.MAP_CAMP_FIRE_GREEN);
                if (visibleFire.Length > 0) return visibleFire[0];
                TimerGame.SleepRandom(250, 350);
            }

            DebugPfCnsl.println("Kamp ateşi mevcut kamera açısından görünmedi; kamera değiştirilmedi");
            return Rectangle.Empty;
        }

        private Rectangle FireKampAtesi()
        {
            CloseFisherShopPage();
            TimerGame timerFireKamp = new TimerGame();

            Rectangle[] kampAtesiIconInvent = null;
            InventoryPage[] inventoryPages =
            {
                InventoryPage.Page_1,
                InventoryPage.Page_2,
                InventoryPage.Page_3,
                InventoryPage.Page_4
            };
            foreach (InventoryPage page in inventoryPages)
            {
                kampAtesiIconInvent = charThings.CheckObjectInventory(imageObjects.arrayKampIcon,
                    coordinate.RectItemSlotSizeSample(), page);
                if (kampAtesiIconInvent.Length > 0) break;
            }

            if (kampAtesiIconInvent != null && kampAtesiIconInvent.Length > 0)
            {

                bool[] sourceKampAtesiGreen = imageObjects.RecordWantedColorAsBool(ColorGame.MAP_CAMP_FIRE_GREEN,
                    imageObjects.arrayKampAtesiWords);

                while (imageObjects.FindAllImagesBoolArrays(sourceKampAtesiGreen, coordinate.RectKampGreenSample(),
                               coordinate.RectKampAtesiAsagiTarafKoordinat(), ColorGame.MAP_CAMP_FIRE_GREEN).Length <= 0)
                {
                    if (timerFireKamp.CheckDelayTimeInSecond(20))
                    {
                        if (ThreadGlobals.isFishingStopped || ThreadGlobals.isCharKilled) { return Rectangle.Empty; }

                        inputGame.KeyDown(KeyboardInput.ScanCodeShort.KEY_S);
                        TimerGame.SleepRandom(400, 500);
                        inputGame.KeyRelease(KeyboardInput.ScanCodeShort.KEY_S);

                        inputGame.MouseMoveAndPressRight(kampAtesiIconInvent[0].X + kampAtesiIconInvent[0].Width / 2,
                            kampAtesiIconInvent[0].Y);

                        TimerGame.SleepRandom(300, 400);
                        if (DismissAtmkConfirmation()) return Rectangle.Empty;
                    }
                    else
                    {
                        DebugPfCnsl.println("FireKampAtesi checkDelayTimeSecond else statement started");
                        return Rectangle.Empty;
                    }
                }

                return imageObjects.FindAllImagesBoolArrays(sourceKampAtesiGreen, coordinate.RectKampGreenSample(),
                    coordinate.RectKampAtesiAsagiTarafKoordinat(), ColorGame.MAP_CAMP_FIRE_GREEN)[0];
            }
            else
            {
                DebugPfCnsl.println("kamp ateşi bulunamadi");
            return Rectangle.Empty; 
            }

          
        }
        /// <summary>
        /// Kullanıcının seçtiği balık türlerinin referans ikonlarını liste olarak döner.
        /// </summary>
        /// <remarks>
        /// Eski sürüm sabit 5 elemanlı `int[][]` döndürüyor ve Denizkızı'nı hiç içermiyordu.
        /// Liste dönmek hem null indeks hatalarını kaldırır hem de yeni balık türü eklemeyi
        /// tek satıra indirir.
        /// </remarks>
        private List<FishIconForGrilling> GetFishTypesForGrilling()
        {
            List<FishIconForGrilling> fishTypes = new List<FishIconForGrilling>();

            if (ThreadGlobals.isYabbieSelected)
            {
                fishTypes.Add(new FishIconForGrilling(imageObjects.arrayYabbieIcon, 32, 16));
                fishTypes.Add(new FishIconForGrilling(imageObjects.arrayYabbFishIcon, 42, 34));
            }
            if (ThreadGlobals.isAltinSudakSelected)
            {
                fishTypes.Add(new FishIconForGrilling(imageObjects.arrayAltinSudakIcon, 32, 16));
                fishTypes.Add(new FishIconForGrilling(imageObjects.arrayAltinFishIcon, 34, 32));
            }
            if (ThreadGlobals.isPalamutSelected)
            {
                fishTypes.Add(new FishIconForGrilling(imageObjects.arrayPalamutIcon, 32, 16));
                fishTypes.Add(new FishIconForGrilling(imageObjects.arrayPalamutFishIcon, 39, 38));
            }
            if (ThreadGlobals.isKurbagaSelected)
                fishTypes.Add(new FishIconForGrilling(imageObjects.arrayKurbagaIcon, 32, 16));
            if (ThreadGlobals.isKadifeSelected)
                fishTypes.Add(new FishIconForGrilling(imageObjects.arrayKadifeIcon, 32, 16));

            // NOT: Denizkızı için Fishes klasöründe ENVANTER ikonu bulunmuyor
            // (yalnızca sohbet yazısı referansı 'denizChat.png' var). Bu yüzden
            // kızartma listesine eklenemiyor; "Hepsi" seçeneği ile kızartılır.

            return fishTypes;
        }

        /// <summary>Envanter ikonu bulunan bütün balık türleri.</summary>
        private List<FishIconForGrilling> GetAllFishTypesForGrilling()
        {
            return new List<FishIconForGrilling>
            {
                new FishIconForGrilling(imageObjects.arrayYabbieIcon, 32, 16),
                new FishIconForGrilling(imageObjects.arrayAltinSudakIcon, 32, 16),
                new FishIconForGrilling(imageObjects.arrayPalamutIcon, 32, 16),
                new FishIconForGrilling(imageObjects.arrayKurbagaIcon, 32, 16),
                new FishIconForGrilling(imageObjects.arrayKadifeIcon, 32, 16),
                new FishIconForGrilling(imageObjects.arrayHamsiIcon, 32, 16),
                new FishIconForGrilling(imageObjects.arrayZarganaIcon, 32, 16),
                new FishIconForGrilling(imageObjects.arrayKralYengeciIcon, 32, 16),
                new FishIconForGrilling(imageObjects.arrayAltinFishIcon, 34, 32),
                new FishIconForGrilling(imageObjects.arrayPalamutFishIcon, 39, 38),
                new FishIconForGrilling(imageObjects.arrayYabbFishIcon, 42, 34)
            };
        }

        public void GoToFishPlace()
        {
            int zigZagWalking = 0;
            int walkSide = 1;

            for (int k=0; k < 10 ; k++) 
            {
                if (ThreadGlobals.isFishingStopped) return;

                Rectangle walkingRect = imageObjects.FindBorderAreaBetweenColors(
                       coordinate.RectReverseWoodDetectionArea(),
                       ColorGame.MIN_WOOD_VALUE, ColorGame.MAX_WOOD_VALUE);

                int xPos, yPos;

                if (walkingRect != Rectangle.Empty)
                {
                    zigZagWalking = TimerGame.MakeRandomValue(0,
                        120);



                    if (walkSide == 1)
                    {
                        xPos = walkingRect.X + (walkingRect.Width / 2) + zigZagWalking;
                        yPos = walkingRect.Y;
                        walkSide = 2;
                    }
                    else
                    {
                        xPos = walkingRect.X + (walkingRect.Width / 2) - zigZagWalking;
                        yPos = walkingRect.Y;
                        walkSide = 1;
                    }

                    inputGame.MouseMoveAndPressLeft(xPos, yPos);
                   
                }
                else
                {
                    xPos = coordinate.RectReverseWoodDetectionArea().X + (coordinate.RectReverseWoodDetectionArea().Width / 2);
                    yPos = coordinate.RectReverseWoodDetectionArea().Y;

                    inputGame.MouseMoveAndPressLeft(xPos, yPos);
                    
                   
                }
                TimerGame.SleepRandom(50, 100);
            }
        }
       

        public  bool CheckFisherShopPage()
        {
            if (imageObjects.CompareTwoArrayAdvanced(imageObjects.arrayFisherShopPage,
               screenShot.ImageArraySpecifiedArea(coordinate.RectFisherShopPage()),
               ImageSensibilityLevel.SENSIBILTY_HIGH))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public void CloseFisherShopPage()
        {
            TimerGame timerCloseFisher = new TimerGame();

            
                while (CheckFisherShopPage())
                {
                if (timerCloseFisher.CheckDelayTimeInSecond(20))
                {
                    if (ThreadGlobals.isFishingStopped || ThreadGlobals.isCharKilled || !ThreadGlobals.isSettingButtonSeemed)
                    {
                        return;
                    }
                    DebugPfCnsl.println("closing shope page");
                    inputGame.MouseMoveAndPressLeft(coordinate.PointFisherShopCloseButton().X,
                        coordinate.PointFisherShopCloseButton().Y);
                    TimerGame.SleepRandom(400, 600);
                }
                else
                {
                    DebugPfCnsl.println("CloseFisherShopPage CheckDelaytimeSecond else statemet started");
                    return;
                }
            }
            
            
        }
    }
}
