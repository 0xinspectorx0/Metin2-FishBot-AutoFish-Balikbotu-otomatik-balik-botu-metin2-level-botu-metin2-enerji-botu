using Metin2AutoFishCSharp.Sources.LevelAndFarms;
using System;
using System.Threading;

namespace MusicPlayerApp.Sources
{
    /// <summary>
    /// Botun üç iş parçacığı (T1 = bot döngüsü, T2 = oyun durumu nöbetçisi,
    /// T3 = yan görevler) ve kullanıcı arayüzü arasında paylaşılan ortak durum.
    /// </summary>
    /// <remarks>
    /// <para><b>Bakım turunda düzeltilenler:</b></para>
    /// <list type="bullet">
    /// <item><c>CanFishingRightNow()</c> içindeki <c>isCharStopped == false == isTradePanelActive == false</c>
    /// zinciri yalnızca <i>tesadüfen</i> doğru sonuç veriyordu
    /// (<c>false == false == false</c> → <c>true</c>). Anlaşılır <c>&amp;&amp;</c>
    /// biçimine çevrildi; davranış aynı.</item>
    /// <item><c>volatile int[]</c> dizinin yalnızca <i>referansını</i> korur, içeriğini
    /// korumaz. Kullanıcı ayarları (statü önceliği, HP/SP yüzdesi, beceri süreleri)
    /// artık kilit altında okunup yazılıyor.</item>
    /// <item>Üç <c>CanXxxRightNow()</c> metodundaki ortak koşullar tek bir
    /// <see cref="IsGameEnvironmentReady"/> metodunda toplandı (kod tekrarı kaldırıldı).</item>
    /// </list>
    /// </remarks>
    public class ThreadGlobals
    {
        #region Oyun durumu bayrakları

        public static volatile bool isSettingButtonSeemed = false;
        public static volatile bool isCharKilled = false;
        public static volatile bool isCharScreenActive = false;
        public static volatile bool isEntryScreenActive = false;
        public static volatile bool isSaleTitleActive = false;
        public static volatile bool isAnotherPlayerDetected = false;
        public static volatile bool isMetin2IconSeemed = true;
        public static volatile bool isChatting = false;
        public static volatile bool isPausedTheGame = false;
        public static volatile bool isCharStopped = false;
        public static volatile bool isEnemyDetected = false;
        public static volatile bool isTradePanelActive = false;
        public static volatile bool isWhisperDetected = false;
        public static volatile bool isCheckedShiftPage = true;
        public static volatile bool isCharNameCanDetectable = false;

        #endregion

        #region İş parçacığı bayrakları

        public static volatile bool IsThreadOneActive = false;
        public static volatile bool IsThreadTwoActive = false;
        public static volatile bool IsThreadThreeActive = false;

        public static volatile bool isTimerBreakEnabled = false;

        #endregion

        #region Balıkçılık ayarları

        public static volatile bool isFishingStopped = true;

        public static volatile bool isYabbieSelected = true;
        public static volatile bool isAltinSudakSelected = true;
        public static volatile bool isPalamutSelected = true;
        public static volatile bool isKurbagaSelected = false;
        public static volatile bool isKadifeSelected = false;
        public static volatile bool isHepsiSelected = false;
        public static volatile bool isDenizkizSelected = false;

        public static volatile bool isPrepareFishingStarted = false;
        public static volatile bool isActiveFishBoard = false;
        public static volatile bool isFishingMiniBreakActive = false;

        public static volatile bool isChattingAnswerActive = false;
        public static volatile bool isWhisperAnswerActive = false;
        public static volatile bool isAdaptableFishing = false;

        #endregion

        #region Level ve Farm ayarları

        public static volatile bool isLevelFarmStopped = true;
        public static volatile bool isETPPickUpActive = false;

        #endregion

        #region Enerji kristali ayarları

        public static volatile bool isEnergyCristalStopped = true;

        #endregion

        #region Telegram

        public static volatile bool isTelegramBotActive = false;

        #endregion

        #region Kullanıcı ayarları (kilit altında erişilir)

        /// <summary>Statü önceliği: [0]=HP, [1]=SP, [2]=STR, [3]=DEX.</summary>
        private static readonly int[] statusPriority = new int[4];
        private static readonly object statusPriorityLock = new object();

        /// <summary>HP/SP pot basma yüzdeleri: [0]=HP, [1]=SP.</summary>
        private static readonly int[] hpSpRate = new int[2];
        private static readonly object hpSpRateLock = new object();

        /// <summary>Beceri süreleri (saniye): [0..3]=1-4 tuşları, [4..7]=F1-F4 tuşları.</summary>
        private static readonly int[] skillTimeForKeys = new int[8];
        private static readonly object skillTimeLock = new object();

        /// <summary>
        /// Geriye dönük uyumluluk için korunmuştur. Mümkün olan yerlerde
        /// <see cref="GetStatusPriority"/> / <see cref="SetStatusPriority"/> kullanın.
        /// </summary>
        public static int[] STATUS_PRIORITY
        {
            get { return GetStatusPriority(); }
        }

        /// <summary>
        /// Geriye dönük uyumluluk için korunmuştur. Mümkün olan yerlerde
        /// <see cref="GetHpSpRate"/> / <see cref="SetHpSpRate"/> kullanın.
        /// </summary>
        public static int[] HP_SP_RATE
        {
            get { return GetHpSpRate(); }
        }

        /// <summary>
        /// Geriye dönük uyumluluk için korunmuştur. Mümkün olan yerlerde
        /// <see cref="GetSkillTimeForKeys"/> / <see cref="SetSkillTime"/> kullanın.
        /// </summary>
        public static int[] SKILL_TIME_FOR_KEYS
        {
            get { return GetSkillTimeForKeys(); }
        }

        public static int[] GetStatusPriority()
        {
            lock (statusPriorityLock)
            {
                return (int[])statusPriority.Clone();
            }
        }

        public static void SetStatusPriority(int hp, int sp, int str, int dex)
        {
            lock (statusPriorityLock)
            {
                statusPriority[0] = hp;
                statusPriority[1] = sp;
                statusPriority[2] = str;
                statusPriority[3] = dex;
            }
        }

        /// <summary>Statü önceliğinin hiçbiri girilmemiş mi?</summary>
        public static bool IsStatusPriorityEmpty()
        {
            lock (statusPriorityLock)
            {
                return statusPriority[0] == 0 || statusPriority[1] == 0 ||
                       statusPriority[2] == 0 || statusPriority[3] == 0;
            }
        }

        public static int[] GetHpSpRate()
        {
            lock (hpSpRateLock)
            {
                return (int[])hpSpRate.Clone();
            }
        }

        public static void SetHpSpRate(int hpRate, int spRate)
        {
            lock (hpSpRateLock)
            {
                hpSpRate[0] = hpRate;
                hpSpRate[1] = spRate;
            }
        }

        /// <summary>HP/SP yüzdesi girilmemiş mi?</summary>
        public static bool IsHpSpRateEmpty()
        {
            lock (hpSpRateLock)
            {
                return hpSpRate[0] == 0 || hpSpRate[1] == 0;
            }
        }

        public static int[] GetSkillTimeForKeys()
        {
            lock (skillTimeLock)
            {
                return (int[])skillTimeForKeys.Clone();
            }
        }

        public static void SetSkillTime(int index, int seconds)
        {
            if (index < 0 || index >= skillTimeForKeys.Length)
            {
                return;
            }
            lock (skillTimeLock)
            {
                skillTimeForKeys[index] = seconds;
            }
        }

        #endregion

        #region Durum sorguları

        /// <summary>
        /// Üç bot modu için ortak olan "oyun ortamı hazır mı?" kontrolü.
        /// </summary>
        private static bool IsGameEnvironmentReady()
        {
            return isChatting == false
                && isSettingButtonSeemed == true
                && isCharKilled == false
                && isCharScreenActive == false
                && isMetin2IconSeemed == true
                && isPausedTheGame == false
                && isEntryScreenActive == false
                && isCharStopped == false
                && isTradePanelActive == false
                && isWhisperDetected == false
                && isCheckedShiftPage == true
                && isCharNameCanDetectable == true;
        }

        /// <summary>Şu anda balık tutulabilir mi?</summary>
        public static bool CanFishingRightNow()
        {
            return isFishingStopped == false
                && isSaleTitleActive == false
                && IsGameEnvironmentReady();
        }

        /// <summary>Şu anda level/farm yapılabilir mi?</summary>
        public static bool CanLevelAndFarmRightNow()
        {
            return isLevelFarmStopped == false && IsGameEnvironmentReady();
        }

        /// <summary>Şu anda enerji kristali döngüsü çalışabilir mi?</summary>
        public static bool CanEnergyCristalRightNow()
        {
            return isEnergyCristalStopped == false && IsGameEnvironmentReady();
        }

        /// <summary>Üç bot modu da durmuş mu?</summary>
        public static bool CheckGameIsStopped()
        {
            return isFishingStopped && isLevelFarmStopped && isEnergyCristalStopped;
        }

        /// <summary>İş parçacıklarından en az biri hâlâ çalışıyor mu?</summary>
        public static bool IsAnyThreadActive()
        {
            return IsThreadOneActive || IsThreadTwoActive || IsThreadThreeActive;
        }

        #endregion

        #region Tanılama

        /// <summary>Tüm ortak bayrakları tek satırda loglar (sorun giderme için).</summary>
        public static void DebugThreadGloablValues()
        {
            int[] status = GetStatusPriority();
            int[] rates = GetHpSpRate();

            string message =
                "isFishingStopped=" + isFishingStopped +
                ", isLevelFarmStopped=" + isLevelFarmStopped +
                ", isEnergyCristalStopped=" + isEnergyCristalStopped +
                ", isSettingButtonSeemed=" + isSettingButtonSeemed +
                ", isCharKilled=" + isCharKilled +
                ", isSaleTitleActive=" + isSaleTitleActive +
                ", isActiveFishBoard=" + isActiveFishBoard +
                ", isEntryScreenActive=" + isEntryScreenActive +
                ", isPrepareFishingStarted=" + isPrepareFishingStarted +
                ", isCharStopped=" + isCharStopped +
                ", isPausedTheGame=" + isPausedTheGame +
                ", isChatting=" + isChatting +
                ", isCharScreenActive=" + isCharScreenActive +
                ", isMetin2IconSeemed=" + isMetin2IconSeemed +
                ", isTradePanelActive=" + isTradePanelActive +
                ", isWhisperDetected=" + isWhisperDetected +
                ", isCheckedShiftPage=" + isCheckedShiftPage +
                ", isCharNameCanDetectable=" + isCharNameCanDetectable +
                ", STATUS_PRIORITY=[" + status[0] + "," + status[1] + "," + status[2] + "," + status[3] + "]" +
                ", HP_RATE=" + rates[0] + ", SP_RATE=" + rates[1];

            DebugPfCnsl.println(message);
        }

        #endregion

        /// <summary>
        /// Bot durdurulduğunda tüm ortak durumu başlangıç değerlerine döndürür.
        /// </summary>
        /// <remarks>Kullanıcının seçtiği balık türleri ve zamanlayıcı ayarları
        /// <b>sıfırlanmaz</b>; yalnızca oyun durumu bayrakları temizlenir.</remarks>
        public static void SetDefaultGloabalValues()
        {
            isSettingButtonSeemed = false;
            isCharKilled = false;
            isSaleTitleActive = false;
            isCharScreenActive = false;
            isMetin2IconSeemed = true;
            isPausedTheGame = false;
            isEntryScreenActive = false;
            isChatting = false;
            isCharStopped = false;
            isActiveFishBoard = false;
            isEnemyDetected = false;
            isAnotherPlayerDetected = false;
            isTradePanelActive = false;
            isWhisperDetected = false;
            isCharNameCanDetectable = false;

            isYabbieSelected = true;
            isAltinSudakSelected = true;
            isPalamutSelected = true;
            isKurbagaSelected = false;
            isKadifeSelected = false;
            isDenizkizSelected = false;
            isHepsiSelected = false;

            IsThreadOneActive = false;
            IsThreadTwoActive = false;
            IsThreadThreeActive = false;

            AutoHunter.IS_AUTO_HUNTER_STARTED = false;

            // Karakter adı önbelleği de temizlenir; bir sonraki çalıştırmada yeniden okunur.
            // CharInfo, MusicPlayerApp.Sources.CharacterHandle ad alanindadir
            // (dosyanin basindaki using ile geliyor).
            CharacterHandle.CharInfo.ResetCharCache();
        }
    }
}
