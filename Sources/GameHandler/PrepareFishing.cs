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

        /// <summary>CheckFisherIsThere özyineleme derinliği sayacı.</summary>
        private static int fisherSearchDepth = 0;
        private const int MAX_FISHER_SEARCH_DEPTH = 6;

        private bool isGrillFishesFailed = false;
        private bool isStandaloneGrillAction;
        private Stopwatch standaloneGrillStopwatch;
        private bool standaloneGrillTimedOut;
        private const int MAX_STANDALONE_GRILL_SECONDS = 240;

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
            GrillFishingHandle();
            WormsHandle();
            ThreadGlobals.isPrepareFishingStarted = false;
        }

        /// <summary>
        /// Envanterde tespit edilebilen tüm balıkları yalnızca bir kez kızartır.
        /// Balık tutma hazırlığının devamındaki yem alma adımını özellikle çağırmaz.
        /// </summary>
        public bool GrillAllFishOnly(out string result)
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
            standaloneGrillStopwatch = Stopwatch.StartNew();
            ThreadGlobals.isFishingStopped = false;
            ThreadGlobals.isSettingButtonSeemed = true;
            ThreadGlobals.isHepsiSelected = false;
            ThreadGlobals.isPrepareFishingStarted = true;

            try
            {
                List<Rectangle[]> fishCoordinatesPageOne = new List<Rectangle[]>();
                List<Rectangle[]> fishCoordinatesPageTwo = new List<Rectangle[]>();
                List<int[]> allFishIcons = GetAllFishTypesForGrilling();

                charThings.OpenCloseInventory(true);
                foreach (int[] fishIcon in allFishIcons)
                {
                    if (ThreadGlobals.isFishingStopped || ThreadGlobals.isCharKilled)
                    {
                        result = "Balık pişirme işlemi durduruldu.";
                        return false;
                    }

                    fishCoordinatesPageOne.Add(charThings.CheckObjectInventory(fishIcon,
                        coordinate.RectItemSlotSizeSample(), InventoryPage.Page_1));
                    fishCoordinatesPageTwo.Add(charThings.CheckObjectInventory(fishIcon,
                        coordinate.RectItemSlotSizeSample(), InventoryPage.Page_2));
                }
                charThings.OpenCloseInventory(false);

                int fishCount = CountTotalFish(fishCoordinatesPageOne) +
                    CountTotalFish(fishCoordinatesPageTwo);
                if (fishCount == 0)
                {
                    result = "Envanterde tanınan balık bulunamadı.";
                    return false;
                }

                // Balıkçı dükkânı zaten açıksa tekrar aramaya çıkma.
                if (!CheckFisherShopPage())
                {
                    FindFisher();
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

                BuyKampAtasiFromFisher();
                if (ThreadGlobals.isFishingStopped || ThreadGlobals.isCharKilled)
                {
                    result = "Balık pişirme işlemi durduruldu.";
                    return false;
                }

                Rectangle campfire = FireKampAtesi();
                if (campfire == Rectangle.Empty)
                {
                    result = "Kamp ateşi hazırlanamadı; işlem durduruldu.";
                    return false;
                }

                bool completed = RetryGrillUntilDone(fishCoordinatesPageOne.ToArray(),
                    fishCoordinatesPageTwo.ToArray(), campfire);
                result = completed
                    ? "Envanterde tanınan tüm balıklar pişirildi."
                    : (ThreadGlobals.isFishingStopped
                        ? "Balık pişirme işlemi durduruldu."
                        : "Balıkların tamamı pişirilemedi; başka işlem başlatılmadı.");
                return completed;
            }
            catch (Exception ex)
            {
                FileLogger.Error("Tek seferlik balık pişirme işlemi başarısız oldu", ex);
                result = "Balık pişirme sırasında hata oluştu: " + ex.Message;
                return false;
            }
            finally
            {
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
            }
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
        private void BuyFiftyWormAsNeeded(int wormsCharHave)
        {
            if(CheckFisherShopPage())
            {
                int neededWorms = NEEDED_WORM200_COUNT - wormsCharHave;

                for (int i = 0; i < neededWorms * 4; i++)
                {
                    inputGame.MouseMoveAndPressRight(coordinate.PointFisherShopFiftyWorm().X,
                       coordinate.PointFisherShopFiftyWorm().Y);

                    TimerGame.SleepRandom(500, 600);

                    if(imageObjects.CompareTwoArrayAdvanced(imageObjects.arrayFullDialogInFisherShop,
                        screenShot.ImageArraySpecifiedArea(coordinate.RectYereAtmaAlgilama()),
                        ImageSensibilityLevel.SENSIBILTY_HIGH))
                    {
                        DebugPfCnsl.println("There aren't any place to buy worm");
                        inputGame.KeyPress(KeyboardInput.ScanCodeShort.ESCAPE);
                        return;
                    }
                }
            }
            else
            {
                DebugPfCnsl.println("Fisher shop page is not open");
                return;
            }
           
        }

        private void BuyKampAtasiFromFisher()
        {
            DebugPfCnsl.println("BuyKampAtasiFromFisher is called");
            TimerGame timerBuyKamp = new TimerGame();
            if (CheckFisherShopPage())
            {
                
                //charThings.ClickWantedInventoryPage(InventoryPage.Page_1);
                
                while (charThings.CheckObjectInventory(imageObjects.arrayKampIcon, coordinate.RectItemSlotSizeSample(), InventoryPage.Page_1).Length <= 0 &&
                    charThings.CheckObjectInventory(imageObjects.arrayKampIcon, coordinate.RectItemSlotSizeSample(), InventoryPage.Page_2).Length <= 0)
                {
                    if (timerBuyKamp.CheckDelayTimeInSecond(6))
                    {
                        if (ThreadGlobals.isFishingStopped && ThreadGlobals.isCharKilled) return;

                        inputGame.MouseMoveAndPressRight(coordinate.PointFisherShopKampAtesi().X,
                            coordinate.PointFisherShopKampAtesi().Y);
                        TimerGame.SleepRandom(500, 800);
                    }
                    else
                    {
                        DebugPfCnsl.println("kamp atesi alinamadi");
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
                // Özyineleme yerine sınırlı deneme.
                for (int retry = 0; retry < MAX_FISHER_RETRY; retry++)
                {
                    if (ThreadGlobals.isFishingStopped || ThreadGlobals.isCharKilled) return;
                    if (!CheckFisherIsThere()) return;

                    BuyKampAtasiFromFisher();
                    return;
                }
            }
           
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
                BuyKampAtasiFromFisher();

                if (!ThreadGlobals.isHepsiSelected)
                {
                    List<int[]> selectedFishIcons = GetFishTypesForGrilling();
                    if (selectedFishIcons.Count == 0)
                    {
                        DebugPfCnsl.println("Kızartılacak balık seçilmemiş, kızartma atlandı");
                        return;
                    }

                    List<Rectangle[]> fishCoordinatesPageOne = new List<Rectangle[]>();
                    List<Rectangle[]> fishCoordinatesPageTwo = new List<Rectangle[]>();

                    foreach (int[] fishIcon in selectedFishIcons)
                    {
                        if (ThreadGlobals.isFishingStopped || ThreadGlobals.isCharKilled) return;

                        fishCoordinatesPageOne.Add(charThings.CheckObjectInventory(fishIcon,
                            coordinate.RectItemSlotSizeSample(), InventoryPage.Page_1));
                        fishCoordinatesPageTwo.Add(charThings.CheckObjectInventory(fishIcon,
                            coordinate.RectItemSlotSizeSample(), InventoryPage.Page_2));
                    }

                    // Eski kod yalnızca ilk üç türün birinci sayfasına bakıyordu; artık
                    // iki sayfadaki tüm seçili türler sayılıyor.
                    if (CountTotalFish(fishCoordinatesPageOne) + CountTotalFish(fishCoordinatesPageTwo) == 0)
                    {
                        DebugPfCnsl.println("Envanterde kızartılacak balık yok");
                        return;
                    }

                    Rectangle rectKampGreenResult = FireKampAtesi();
                    if (rectKampGreenResult != Rectangle.Empty)
                    {
                        RetryGrillUntilDone(fishCoordinatesPageOne.ToArray(),
                            fishCoordinatesPageTwo.ToArray(), rectKampGreenResult);
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
        private bool RetryGrillUntilDone(Rectangle[][] pageOne, Rectangle[][] pageTwo, Rectangle kampAtesiGreen)
        {
            for (int attempt = 0; attempt < MAX_GRILL_RETRY; attempt++)
            {
                if (ThreadGlobals.isFishingStopped || ThreadGlobals.isCharKilled || standaloneGrillTimedOut)
                    return false;

                if (GrillFishes(pageOne, pageTwo, kampAtesiGreen))
                {
                    return true;
                }

                DebugPfCnsl.println("Kızartma tamamlanamadı, kamp ateşi yenileniyor (deneme " + (attempt + 1) + ")");

                if (!CheckFisherIsThere())
                {
                    return false;
                }
                if (!CheckFisherShopPage())
                {
                    continue;
                }

                BuyKampAtasiFromFisher();
                Rectangle newFire = FireKampAtesi();
                if (newFire != Rectangle.Empty)
                {
                    kampAtesiGreen = newFire;
                }
            }

            FileLogger.Warning("Kızartma " + MAX_GRILL_RETRY + " denemede tamamlanamadı, işleme devam ediliyor");
            return false;
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

        private bool GrillFishes(Rectangle[][] rectPageOne , Rectangle[][] rectPageTwo,Rectangle kampAtesiGreen)
        {
            DebugPfCnsl.println("GrillFishes func is called");
            TimerGame timerGrillFishes = new TimerGame();

            if (!ThreadGlobals.isHepsiSelected)
            {
                if (rectPageOne.Length > 0)
                {
                    charThings.ClickWantedInventoryPage(InventoryPage.Page_1);
                    for (int pageOneLength = 0; pageOneLength < rectPageOne.Length; pageOneLength++)
                    {
                        for (int pageOneValue = 0; pageOneValue < rectPageOne[pageOneLength].Length; pageOneValue++)
                        {

                            Rectangle rectFish = rectPageOne[pageOneLength][pageOneValue];

                            int[] fishImageBeforeGrill = screenShot.ImageArraySpecifiedArea(rectFish);
                            int[] slotImageAfterGrill = screenShot.ImageArraySpecifiedArea(rectFish);

                            while (imageObjects.CompareTwoArrayAdvanced(fishImageBeforeGrill, slotImageAfterGrill
                                , ImageSensibilityLevel.SENSIBILTY_HIGH))
                            {
                                if (timerGrillFishes.CheckDelayTimeInSecond(60))
                                {
                                    if (ThreadGlobals.isFishingStopped || ThreadGlobals.isCharKilled) return false;



                                    inputGame.MouseMoveAndPressLeft(rectFish.X + rectFish.Width / 2, rectFish.Y);
                                    inputGame.MouseMoveAndPressLeft(kampAtesiGreen.X + kampAtesiGreen.Width / 2,
                                        kampAtesiGreen.Y + kampAtesiGreen.Height / 2);

                                    TimerGame.SleepRandom(300, 400);

                                    if (imageObjects.CompareTwoArrayAdvanced(imageObjects.arrayYereAtmaDialog,
                                        screenShot.ImageArraySpecifiedArea(coordinate.RectYereAtmaAlgilama()),
                                        ImageSensibilityLevel.SENSIBILTY_HIGH))
                                    {
                                        inputGame.KeyPress(KeyboardInput.ScanCodeShort.ESCAPE);
                                        return false;
                                    }

                                    slotImageAfterGrill = screenShot.ImageArraySpecifiedArea(rectFish);
                                }
                                else
                                {
                                    DebugPfCnsl.println("GrillFishes func CheckDelayTimeSecond else statement started");
                                    return false;
                                }

                            }

                        }
                    }
                }
                if (rectPageTwo.Length > 0)
                {
                    charThings.ClickWantedInventoryPage(InventoryPage.Page_2);

                    for (int pageTwoLength = 0; pageTwoLength < rectPageTwo.Length; pageTwoLength++)
                    {
                        for (int pageTwoValue = 0; pageTwoValue < rectPageTwo[pageTwoLength].Length; pageTwoValue++)
                        {
                            if (timerGrillFishes.CheckDelayTimeInSecond(60))
                            {
                                Rectangle rectFish = rectPageTwo[pageTwoLength][pageTwoValue];

                                int[] fishImageBeforeGrill = screenShot.ImageArraySpecifiedArea(rectFish);
                                int[] slotImageAfterGrill = screenShot.ImageArraySpecifiedArea(rectFish);

                                while (imageObjects.CompareTwoArrayAdvanced(fishImageBeforeGrill, slotImageAfterGrill
                                    , ImageSensibilityLevel.SENSIBILTY_HIGH))
                                {

                                    if (ThreadGlobals.isFishingStopped || ThreadGlobals.isCharKilled) return false;



                                    inputGame.MouseMoveAndPressLeft(rectFish.X + rectFish.Width / 2, rectFish.Y);
                                    inputGame.MouseMoveAndPressLeft(kampAtesiGreen.X + kampAtesiGreen.Width / 2,
                                        kampAtesiGreen.Y + kampAtesiGreen.Height / 2);

                                    TimerGame.SleepRandom(200, 400);

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
                            else
                            {
                                DebugPfCnsl.println("GrillFishes func CheckDelayTimeSecond else statement started");
                                return false;
                            }
                        }

                    }
                }
            }
            else
            {

                for (; pageGrillFisher <= 2; pageGrillFisher++)
                {
                    if(isGrillFishesFailed)
                    {
                        pageGrillFisher = 1;
                        xGrillFishes = 0;
                        yGrillFishes = 0;
                    }
                    if (pageGrillFisher == 1)
                    {
                        charThings.ClickWantedInventoryPage(InventoryPage.Page_1);
                    }
                    else
                    {
                        charThings.ClickWantedInventoryPage(InventoryPage.Page_2);
                    }

                    for (; yGrillFishes < 9; yGrillFishes++)
                    {
                        for (; xGrillFishes < 5; xGrillFishes++)
                        {
                            if (ThreadGlobals.isFishingStopped || ThreadGlobals.isCharKilled) return false;

                            if (timerGrillFishes.CheckDelayTimeInSecond(60))
                            {
                                Rectangle rectScanSlot = new Rectangle(coordinate.RectFirstSlotPlace().X + (GameObjectCoordinates.DISTANCE_BTWN_INV_SLOTS * xGrillFishes),
                               coordinate.RectFirstSlotPlace().Y + (GameObjectCoordinates.DISTANCE_BTWN_INV_SLOTS * yGrillFishes),
                               coordinate.RectFirstSlotPlace().Width, coordinate.RectFirstSlotPlace().Height);

                                int[] targetSlotImage = screenShot.ImageArraySpecifiedArea(rectScanSlot);

                                if (!imageObjects.CompareTwoArrayAdvanced(imageObjects.arrayEmptySlotPlace, targetSlotImage,
                                    ImageSensibilityLevel.SENSIBILTY_HIGH))
                                {
                                    inputGame.MouseMoveAndPressLeft(rectScanSlot.X + rectScanSlot.Width / 2,
                                            rectScanSlot.Y);
                                    inputGame.MouseMoveAndPressLeft(kampAtesiGreen.X + kampAtesiGreen.Width / 2,
                                            kampAtesiGreen.Y + kampAtesiGreen.Height / 2);

                                    TimerGame.SleepRandom(200, 400);

                                    if (imageObjects.CompareTwoArrayAdvanced(imageObjects.arrayYereAtmaDialog,
                                        screenShot.ImageArraySpecifiedArea(coordinate.RectYereAtmaAlgilama()),
                                        ImageSensibilityLevel.SENSIBILTY_HIGH))
                                    {
                                        inputGame.KeyPress(KeyboardInput.ScanCodeShort.ESCAPE);
                                        return false;
                                    }
                                }
                            }
                            else
                            {
                                isGrillFishesFailed = true;
                                return false;
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
            yGrillFishes =0;    

                return true;
                        
        }
        
        private Rectangle FireKampAtesi()
        {
            CloseFisherShopPage();
            TimerGame timerFireKamp = new TimerGame();

            Rectangle[] kampAtesiIconInvent = charThings.CheckObjectInventory(imageObjects.arrayKampIcon,
                coordinate.RectItemSlotSizeSample(),InventoryPage.Page_1);
            if(kampAtesiIconInvent.Length <= 0)
            {
                kampAtesiIconInvent = charThings.CheckObjectInventory(imageObjects.arrayKampIcon,
                coordinate.RectItemSlotSizeSample(), InventoryPage.Page_2);
            }

            if(kampAtesiIconInvent != null && kampAtesiIconInvent.Length > 0)
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
        private List<int[]> GetFishTypesForGrilling()
        {
            List<int[]> fishTypes = new List<int[]>();

            if (ThreadGlobals.isYabbieSelected) fishTypes.Add(imageObjects.arrayYabbieIcon);
            if (ThreadGlobals.isAltinSudakSelected) fishTypes.Add(imageObjects.arrayAltinSudakIcon);
            if (ThreadGlobals.isPalamutSelected) fishTypes.Add(imageObjects.arrayPalamutIcon);
            if (ThreadGlobals.isKurbagaSelected) fishTypes.Add(imageObjects.arrayKurbagaIcon);
            if (ThreadGlobals.isKadifeSelected) fishTypes.Add(imageObjects.arrayKadifeIcon);

            // NOT: Denizkızı için Fishes klasöründe ENVANTER ikonu bulunmuyor
            // (yalnızca sohbet yazısı referansı 'denizChat.png' var). Bu yüzden
            // kızartma listesine eklenemiyor; "Hepsi" seçeneği ile kızartılır.
            // Denizkızı envanter ikonu eklenirse aşağıdaki satır açılmalıdır:
            // if (ThreadGlobals.isDenizkizSelected) fishTypes.Add(imageObjects.arrayDenizkizIcon);

            return fishTypes;
        }

        /// <summary>Envanter ikonu bulunan bütün balık türleri.</summary>
        private List<int[]> GetAllFishTypesForGrilling()
        {
            return new List<int[]>
            {
                imageObjects.arrayYabbieIcon,
                imageObjects.arrayAltinSudakIcon,
                imageObjects.arrayPalamutIcon,
                imageObjects.arrayKurbagaIcon,
                imageObjects.arrayKadifeIcon,
                imageObjects.arrayHamsiIcon,
                imageObjects.arrayZarganaIcon,
                imageObjects.arrayKralYengeciIcon
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
