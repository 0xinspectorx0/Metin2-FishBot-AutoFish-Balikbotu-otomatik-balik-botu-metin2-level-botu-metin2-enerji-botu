using MusicPlayerApp.Debugs;
using MusicPlayerApp.Sources;
using System;

namespace Metin2AutoFishCSharp.Sources.LevelAndFarms
{
    /// <summary>
    /// Hızlı erişim tuşlarına (1-4 ve F1-F4) atanan becerileri, kullanıcıdan alınan
    /// süre aralıklarına göre basar.
    /// </summary>
    /// <remarks>
    /// Beceriler "hedef seçimi" yapılmadan, yalnızca süre dolduğunda basılır (README'de
    /// belirtildiği gibi pasif/uzaktan vuran beceriler daha verimlidir).
    ///
    /// <para><b>Bakım turunda düzeltilenler:</b></para>
    /// <list type="bullet">
    /// <item><c>StartSkillUsing</c> içindeki erken çıkış koşulu yalnızca log yazıp
    /// <b>return etmiyordu</b>; yani level/farm durdurulduğunda veya ayar düğmesi
    /// görünmediğinde (örneğin karakter ekranı açıkken) beceriler basılmaya devam
    /// ediyordu. <c>return</c> eklendi.</item>
    /// <item>Süre dizisi her tuş kontrolünde kopyalanıyordu; artık tur başına bir kez
    /// okunuyor.</item>
    /// <item>Süresi <c>0</c> girilen beceri hiç basılmıyordu (zamanlayıcı 0 saniyeyi
    /// "doldu" kabul etmiyordu). Artık 0 = "her turda bas".</item>
    /// </list>
    /// </remarks>
    internal class SkillsHandler
    {
        /// <summary>Hızlı erişim tuşlarının tarama kodları (1-4 ve F1-F4).</summary>
        private static readonly KeyboardInput.ScanCodeShort[] skillKeys = new KeyboardInput.ScanCodeShort[]
        {
            KeyboardInput.ScanCodeShort.KEY_1,
            KeyboardInput.ScanCodeShort.KEY_2,
            KeyboardInput.ScanCodeShort.KEY_3,
            KeyboardInput.ScanCodeShort.KEY_4,
            KeyboardInput.ScanCodeShort.F1,
            KeyboardInput.ScanCodeShort.F2,
            KeyboardInput.ScanCodeShort.F3,
            KeyboardInput.ScanCodeShort.F4,
        };

        /// <summary>Beceri süresi 0 girildiğinde kullanılan asgari bekleme (saniye).</summary>
        private const int MINIMUM_SKILL_INTERVAL_SECOND = 1;

        private readonly TimerGame[] timersGameSkillTime;
        private readonly bool[] stateKeyPresses;
        private readonly GameInputHandler inputHandler;

        public SkillsHandler()
        {
            timersGameSkillTime = new TimerGame[skillKeys.Length];
            for (int i = 0; i < timersGameSkillTime.Length; i++)
            {
                timersGameSkillTime[i] = new TimerGame();
            }

            stateKeyPresses = new bool[skillKeys.Length];
            inputHandler = new GameInputHandler();
        }

        /// <summary>
        /// Süresi gelen becerileri basar. Yalnızca otomatik av açıkken ve oyun
        /// oynanabilir durumdayken çalışır.
        /// </summary>
        public void StartSkillUsing()
        {
            if (ThreadGlobals.isLevelFarmStopped || !ThreadGlobals.isSettingButtonSeemed)
            {
                DebugPfCnsl.println("StartSkillUsing: level/farm kapalı veya oyun hazır değil, beceri basılmadı");
                return;
            }

            if (!AutoHunter.IS_AUTO_HUNTER_STARTED)
            {
                return;
            }

            // Kullanıcı süreleri tek seferde okunur (her tuş için dizi kopyalanmaz).
            int[] skillTimes = ThreadGlobals.GetSkillTimeForKeys();

            for (int i = 0; i < skillKeys.Length; i++)
            {
                if (skillTimes[i] < 0)
                {
                    continue;
                }

                PressWantedSkill(i, skillTimes[i]);
            }
        }

        /// <summary>
        /// Verilen slottaki beceriyi, süresi dolduysa basar.
        /// </summary>
        /// <param name="slotIndex">0-7 arası tuş indeksi</param>
        /// <param name="delayTime">Beklenecek saniye (0 = her turda bas)</param>
        private void PressWantedSkill(int slotIndex, int delayTime)
        {
            TimerGame timer = timersGameSkillTime[slotIndex];
            KeyboardInput.ScanCodeShort code = skillKeys[slotIndex];

            // Süre 0 ise zamanlayıcı hiçbir zaman "dolmadı" demez; bu yüzden en az
            // 1 saniyelik aralık kullanılır ve beceri her turda basılabilir.
            int effectiveDelay = Math.Max(delayTime, MINIMUM_SKILL_INTERVAL_SECOND);

            if (timer.CheckDelayTimeInSecond(effectiveDelay))
            {
                // Süre henüz dolmadı: beceri bir kez basıldıysa yeniden basma.
                if (!stateKeyPresses[slotIndex])
                {
                    inputHandler.KeyPress(KeyboardInput.ScanCodeShort.TAB);
                    inputHandler.KeyPress(code);
                    stateKeyPresses[slotIndex] = true;
                    DebugPfCnsl.println("Beceri tuşu basıldı: " + code);
                    TimerGame.SleepRandom(3000, 3200);
                }
            }
            else
            {
                // Süre doldu: yeni periyot başlatılır.
                stateKeyPresses[slotIndex] = false;
                timer.SetStartedSecondTime();
            }
        }
    }
}
