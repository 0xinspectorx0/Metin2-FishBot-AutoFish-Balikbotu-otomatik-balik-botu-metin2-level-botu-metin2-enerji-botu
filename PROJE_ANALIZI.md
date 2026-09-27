# Proje Detaylı İnceleme Raporu

**Repo:** `0xinspectorx0/Metin2-FishBot-AutoFish-Balikbotu-...`
**İnceleme tarihi:** 2026-09-26 · **Branch:** `arena/01a0dfe0-metin2-fishbot-autofish-balikb`
**Kapsam:** 269 dosya, ~17.750 satır C# (44 `.cs` dosyası), 161 `Content` öğesi, 3 form.

---

## 1. Projenin Kimliği ve Mimari Özeti

| Özellik | Değer |
|---|---|
| Tür | Windows Forms masaüstü uygulaması (oyun otomasyon botu) |
| Hedef framework | .NET Framework **4.7.2**, `AnyCPU`, `AllowUnsafeBlocks=true` |
| Çıktı | `Metin2AutoFishCSharp.exe` (`bin\Debug`) |
| Proje/Solution adı | **`MusicPlayerApp`** (gerçek içerikle alakasız, eski şablon adı) |
| Root namespace | `Metin2AutoFishCSharp` — ama kodda **iki farklı namespace ailesi** var: `MusicPlayerApp.*` ve `Metin2AutoFishCSharp.*` |
| NuGet | `Telegram.Bot 19.0.0`, `Newtonsoft.Json 13.0.1`, `System.ComponentModel.Annotations`, `...CompilerServices.Unsafe`, `...Threading.Tasks.Extensions` (packages.config usulü) |
| Git geçmişi | Tek commit: `9610c15 Add files via upload` (geçmiş yok) |
| `.gitignore` | **YOK** — `bin/` ve `obj/` dahil 40 derleme çıktısı dosyası repoya commit edilmiş |

### Çalışma prensibi
Uygulama oyunun belleğine hiç dokunmaz; tamamen **ekran görüntüsü + görüntü işleme + donanım girişi simülasyonu** ile çalışır:

1. **Ekran yakalama** — `Sources/ScreenShotWinAPI.cs`: GDI `BitBlt` (`SRCCOPY | CAPTUREBLT`) ile `GetDesktopWindow` DC'sinden bölge bölge bitmap alır, `GetPixel` ile `int[]` (ARGB) dizisine çevirir.
2. **Referans görüntüler** — `Sources/ImageHandle/ImageObjects.cs`: açılışta `Images/`, `Fishes/`, `ChatResources/GameAlphabets/` altındaki ~70 PNG'yi okuyup `int[]` olarak belleğe alır.
3. **Eşleştirme** — `Sources/ImageHandle/ImageProcess.cs`:
   * `CompareTwoArrayAdvanced` (kanal başına ±5 tolerans, `ImageSensibilityLevel` ile eşik),
   * `compareTwoArrayQuickly` (birebir, tek-renk "imza" dizileri için),
   * `FindAllImagesOnScreen` / `FindImageOnScreen` (şablon tarama),
   * `FindBorderAreaForWantedColor(V2)` / `FindBorderAreaBetweenColors` (renk kümesinin sınır kutusu),
   * `ParseWordsFromOneLineArea(V2)` (satırı dikey boşluklara göre kelime kutularına ayırma),
   * `ClipIntArray` / `ClipBoolArray` (dizi kırpma).
4. **Koordinat sistemi** — `Sources/CoordinatesHandler/CheckGameCoordinate.cs` + `GameObjectCoordinates.cs`: ekranda `metin2Icon.png` şablonu bulunur, oyun penceresinin sol üst referans noktası (`currentScreenGamePoint`) çıkarılır; **tüm** ~70 `RectXxx()` / `PointXxx()` metodu bu ofsete göre sabit sayılarla hesaplanır (800x600 pencere modu varsayımı).
5. **Giriş simülasyonu** — `Sources/Inputs/KeyboardInput.cs` (`SendInput`, scancode), `KeyboardTextInput.cs` (metin yazma), `GameInputHandler.cs` (`mouse_event`, `SetCursorPos`).
6. **İş parçacıkları** — `Sources/Threads/ThreadsHandler.cs`:
   * **T1** = ana bot döngüsü (`FishingHandle.StartFishing` / `LevelHandle.StartLevelAndFarming` / `EnerjyCristalHandle.StartEnergyCristal`),
   * **T2** = oyun durumu nöbetçisi (`CheckGameStatus.StartCheckingGame`: giriş ekranı, ölüm ekranı, karakter ekranı, satış başlığı, ayar düğmesi, balık panosu),
   * **T3** = yan görevler (ticaret paneli, fısıltı cevaplama, chat cevaplama, statü dağıtma, ETP toplama).
   * Ortak durum: `Sources/Threads/ThreadGlobals.cs` içindeki ~40 `volatile bool` + `CanFishingRightNow()` / `CanLevelAndFarmRightNow()` / `CanEnergyCristalRightNow()` kapı fonksiyonları.
7. **Zamanlama** — `Sources/TimerGame.cs`: `DateTimeOffset.UtcNow` tabanlı gecikme/geri sayım sayaçları, rastgele bekleme üreticileri, mola (break) mantığı, `IS_PC_SLOW` bayrağı.
8. **Haberleşme** — `Sources/TelegramBot.cs` (uzaktan DURDUR + uyarı mesajları), `Sources/ChatHandler/*` (oyun içi chat/fısıltı OCR'ı: `GameAlphabetDetecter` harf şablonlarıyla metin çözer, `ChatSentencer` cümlelere böler, `ChatFileHandler` `ChatQuestAnswer.txt`'de `&` … `^` … bloklarıyla soru-cevap tutar).
9. **UI** — `MainForm` (bot ayarları ve başlatma arayüzü), `ChatHandlerForm` (kelime/cevap yükleme). Manuel ekran görüntüsü aracı kaldırıldı; botun dahili ekran yakalaması korunuyor.

### Modül envanteri (satır sayıları)
```
1370  Sources/ImageHandle/ImageProcess.cs        1011  Sources/ChatHandler/GameAlphabetDetecter.cs
1368  MainForm.Designer.cs                       1031  Sources/Inputs/KeyboardInput.cs
1202  Sources/CharacterHandle/CharSpecialThings.cs 970  MainForm.cs
 876  Sources/GameHandler/PrepareFishing.cs       738  Sources/GameHandler/FishingHandle.cs
 720  Sources/GameHandler/EnerjyCristalHandle.cs  581  Sources/GameHandler/LevelHandle.cs
 550  Sources/CoordinatesHandler/GameObjectCoordinates.cs
 508  Sources/GameHandler/CheckGameStatus.cs      457  Sources/ChatHandler/Chatting.cs
 413  Sources/Threads/ThreadsHandler.cs           389  Sources/ChatHandler/WhispersHandle.cs
 376  Sources/CharacterHandle/CharMovement.cs     343  Sources/LevelAndFarms/StatusHandler.cs
 342  Sources/ScreenShotWinAPI.cs                 338  Sources/ChatHandler/ChatFileHandler.cs
 262  Sources/TimerGame.cs                        247  Sources/CharacterHandle/CharPickUpItems.cs
 234  Sources/LevelAndFarms/AutoHunter.cs         207  Sources/FileHandler.cs
 195  Sources/ImageHandle/ImageObjects.cs         185  Sources/Threads/ThreadGlobals.cs
 166  Sources/Inputs/GameInputHandler.cs          162  Sources/Inputs/KeyboardTextInput.cs
 159  Sources/Debugs/DebugPfCnsl.cs               153  Sources/TelegramBot.cs
 152  Sources/ChatHandler/ChatSentencer.cs
 141  Sources/GameHandler/PlayersHandler.cs       129  Sources/LevelAndFarms/SkillsHandler.cs
 116  Sources/CharacterHandle/CharInfo.cs          91  Sources/ImageHandle/ImagePathNames.cs
  90  Sources/CoordinatesHandler/CheckGameCoordinate.cs
```

---

## 2. KRİTİK Bulgular (derleme/çalışma engelleyici)

### K1 — `Sources/VersionChecker.cs` csproj'a dahil değil → **proje derlenmez**
`MusicPlayerApp.csproj` `<Compile>` listesinde `Sources\VersionChecker.cs` **yok**, ama `MainForm.cs:90` çağırıyor:
```csharp
VersionChecker.CheckForUpdate();
```
Eski tip (non-SDK) csproj'da dosyalar otomatik dahil edilmez → temiz bir makinede `CS0103: The name 'VersionChecker' does not exist`. Repodaki `bin\Debug\Metin2AutoFishCSharp.exe` bu dosya olmadan derlenmiş eski bir çıktı.
**Çözüm:** csproj'a `<Compile Include="Sources\VersionChecker.cs" />` eklenmeli (veya çağrı kaldırılmalı).

### K2 — Sürüm kontrolü **başkasının** reposuna bakıyor ve UI thread'i kilitliyor
`Sources/VersionChecker.cs`:
```csharp
VersionUrl = "https://raw.githubusercontent.com/mmtcoder/Metin2-FishBot-.../main/version.txt"
UpdatePageUrl = "https://github.com/mmtcoder/Metin2-FishBot-..."
```
Bu repo `0xinspectorx0/...` altında. Yani güncelleme kontrolü upstream yazara gidiyor. Ayrıca `WebClient.DownloadString` **senkron** ve `MainForm` kurucusunda çağrılıyor → internet yoksa form açılışı donuyor (WebClient varsayılan timeout ~100 sn). `CurrentVersion` da elle yazılmış `"1.0.2"` (AssemblyInfo `1.0.0.0`, README `v2.3`).
**Çözüm:** URL'leri kendi reposuna çevir, async yap (veya `Task.Run`), sürümü tek yerden (`AssemblyInformationalVersion`) oku, timeout düşür.

### K3 — PNG/Fishes/ChatResources çıktı klasörüne **kopyalanmıyor**
csproj'da **161** `<Content Include=...>` var ama **0** adet `CopyToOutputDirectory`. `ImageObjects` kurucusu `FileHandler.ReturnOrFindPathWay(..., FIND_PATH)` ile dosyaları **exe'nin iki üst klasöründe** arıyor (`bin\Debug\..\..\Images\...`). Sonuç:
* VS'dan derleyip exe'yi başka klasöre taşıyan kullanıcı `FileNotFoundException` alır ve uygulama **açılışta çöker** (kaynak yüklenemeyince `ImageObjects` fırlatır, `MainForm` kurucusunda try/catch yok).
* `FileHandler.ReadPngFileGetBitmap` hatayı `throw new Exception(ex.Message)` ile yeniden sarar → stack trace kaybolur.
**Çözüm:** Content öğelerine `PreserveNewest` ekle + yol çözümünü `AppDomain.CurrentDomain.BaseDirectory` tabanlı ve hata mesajı anlaşılır hale getir.

### K4 — `ChatFileHandler` statik alanı, açılışta tüm diski tarıyor
```csharp
private static readonly string ChatQuestAnswerPath = FileHandler.FindFolderNameFromBase("ChatQuestionAnswer");
```
`FindFolderNameFromBase` → `Environment.CurrentDirectory`'yi `\` ile bölüp son 2 segmenti atıyor, sonra `Directory.GetDirectories(..., SearchOption.AllDirectories)` ile **özyinelemeli** arama yapıyor. Bulamazsa `FileNotFoundException` → `ChatFileHandler` tipine ilk dokunuşta `TypeInitializationException`. Working directory değişirse (kısayol, "farklı çalıştır") patlar.
**Çözüm:** exe dizini + sabit göreli yol; dosya yoksa oluştur.

### K5 — Telegram bot token'ı placeholder ve **koşulsuz** başlatılıyor
```csharp
private readonly string telegramToken = "Add Your Token";   // TelegramBot.cs:23
...
public TelegramBot(ImageObjects imageObject) { botClient = new TelegramBotClient(telegramToken); ... StartReceiver(); }
```
`MainForm_Load` her açılışta `new TelegramBot(...)` yapıyor; checkbox kapalı olsa bile. Geçersiz token ile `ReceiveAsync` hemen hata verir; hata `ErrorMessage` içinde `SendTextMessageAsync("", ...)` (boş chat id) çağrısına düşer → ikinci bir exception, üstelik `async void`-vari gözlenmeyen Task. Kullanıcı bunu hiç görmez, sadece Telegram çalışmaz.
**Çözüm:** token'ı UI/config'den al (App.config veya ayar dosyası), yalnızca checkbox işaretliyken başlat, hataları logla.

---

## 3. Fonksiyonel Hatalar (bot davranışını bozan)

### H1 — Balığa tıklarken Y yerine X gönderiliyor
`Sources/GameHandler/FishingHandle.cs:461`:
```csharp
inputs.MouseClickQuickly(rectFish.X + x, rectFish.X + x);   // 2. parametre rectFish.Y + y olmalı
```
"PC hızlı" modunda 3. denemeden sonra tıklama **yanlış koordinata** gidiyor (imleç doğru yerde, tıklama başka yerde). `mouse_event` mutlak konumlandırma yapmadığı için çoğu zaman imlecin olduğu yer tıklanır ve hata maskelenir; ama bu açık bir yazım hatası.

### H2 — Mini harita piksel indeksi `Width` yerine `Height` ile hesaplanıyor
`Sources/GameHandler/PlayersHandler.cs:43,45,51,53`:
```csharp
miniMapImage[(y * rectMiniMap.Height) + x]   // doğrusu: (y * rectMiniMap.Width) + x
```
`RectMiniMapArea()` 117x117 **kare** olduğu için şu an tesadüfen doğru çalışıyor; alanı değiştirdiğin an "Adapte Tutma / oyuncu tespiti" tamamen bozulur.

### H3 — Izgara (grill) akışı seçilen balık sayısına göre çöküyor
`PrepareFishing.GetFishTypesForGrilling()` **5** slotlu dizi döndürüyor (Yabbie, Altın Sudak, Palamut, Kurbağa, Kadife — **Denizkızı yok**), ama `GrillFishingHandle` sabit indekslerle kontrol ediyor:
```csharp
if (fishCoordinatesPageOne[0].Length == 0 &&
    fishCoordinatesPageOne[1].Length == 0 &&
    fishCoordinatesPageOne[2].Length == 0) return;   // PrepareFishing.cs:464-466
```
Kullanıcı yalnızca Kadife/Kurbağa seçerse `[0]`,`[1]`,`[2]` null kalır → `NullReferenceException`. Ayrıca seçili balık sayısı 3'ten azsa dizi boyutu `fishTypes` kadar olur ve indeksler kayar. Denizkızı seçili olduğunda hiç kızartılmıyor (README'de "kurbağa yakma ekli değil" notuyla kısmen belgelenmiş).

### H4 — `FileHandler.SaveImageAsPng` null kontrolü ters
```csharp
if (bitmap == null || bitmap.Width > 0)   // FileHandler.cs:40 → null ise içine girip Save() çağırıyor
```
Doğrusu `if (bitmap != null && bitmap.Width > 0)`. Şu an null bitmap gelirse `NullReferenceException`.

### H5 — Ekran yakalama hatası **dispose edilmiş bitmap** döndürüyor
`ScreenShotWinAPI.CaptureSpecifiedScreen`: `catch (Exception)` içinde `fullScreen.Dispose()` yapılıyor ama metot yine de `fullScreen`'i döndürüyor. Çağıran taraf (`ConvertBitmapToArray` → `GetPixel`) `ArgumentException` alır. Ayrıca:
* `Image.FromHbitmap(bitmap)` **palette handle sızdırır** (`DeleteObject(hPal)` çağrılmıyor),
* `CaptureSpecifiedScreen` her çağrıda yeni `Bitmap` üretir ve hiçbir çağıran `Dispose()` etmez → **GDI nesne sızıntısı**; saatlerce çalışan botta GDI handle limiti (10.000/process) aşılıp uygulama çöker. Bu projenin en ciddi uzun-çalışma riski.
* `lock (lockObject)` içindeki `finally` bloğunda `memoryDc` sıfır olabilse de `DeleteDC(IntPtr.Zero)` çağrılıyor (zararsız ama gereksiz).

### H6 — `GetPixel` tabanlı tarama → saniyede milyonlarca çağrı
`ImageProcess.IsMatch` ve `ConvertBitmapToArray` piksel piksel `GetPixel` kullanıyor (her biri lock + format dönüşümü). `FindImageOnScreen` tam ekran (örn. 1920x1080 = 2M piksel) × şablon taraması yapıyor ve `CheckGameCoordinate.CheckGameScreenPlace()` bunu **60 saniyede bir** tekrarlıyor; her `RectXxx()` çağrısı bu metottan geçiyor (yani bir döngü turunda yüzlerce kez). `CompareBitmaps` zaten `LockBits`+`unsafe` kullanıyor → aynı yaklaşım `IsMatch`/`ConvertBitmapToArray`'a taşınırsa **10-100x** hızlanma olur.

### H7 — Boş (busy-wait) döngüler CPU'yu %100 yakıyor
`Thread.Sleep` olmadan dönen onlarca döngü var, örn.:
```csharp
while (ThreadGlobals.IsThreadOneActive || ThreadGlobals.IsThreadTwoActive || ThreadGlobals.IsThreadThreeActive) { }   // ThreadsHandler.Stop():305
while (!ThreadGlobals.isSettingButtonSeemed) { if (ThreadGlobals.isFishingStopped) return; }                            // FishingHandle
while (ThreadGlobals.isActiveFishBoard) { if (ThreadGlobals.CheckGameIsStopped() || ...) return false; }                // PlayersHandler
```
T1 ana döngüsü de (`while (ThreadGlobals.IsThreadOneActive)`) üç bot da kapalıysa hiçbir şey yapmadan tam hızda döner. Hem CPU ısınır hem "gerçek oyuncu gibi davranma" iddiası zayıflar hem de `Stop()` çağrısı T1 mola uykusundayse (örn. `SleepRandomMinute` 10 dk) düğmeyi geri açamaz: **DURDUR'a basıldıktan sonra UI "çalışıyor" durumunda takılı kalabilir**.
**Çözüm:** `Thread.Sleep(10..50)` + `ManualResetEventSlim`/`CancellationToken` ile kesilebilir bekleme.

### H8 — `Random` her çağrıda yeniden yaratılıyor
```csharp
public static int MakeRandomValue(int minValue, int maxValue) { ... Random random = new Random(); return random.Next(...); }
```
`new Random()` zaman tohumlu olduğundan ardışık hızlı çağrılar **aynı** sayıyı üretir (rastgele mola/tıklama gecikmeleri sabitlenir). Ayrıca `minValue >= maxValue` durumunda `minValue = 0` yapılıyor → `SleepRandom(40, 60)` yerine yanlışlıkla `SleepRandom(60, 40)` yazılırsa 0-60 arası döner (sessiz davranış değişimi). Tek bir `static readonly Random` + `lock` (veya .NET'te thread-static) yeterli.

### H9 — `ThreadGlobals.CanXxxRightNow()` içindeki zincirleme eşitlik
```csharp
... && isCharStopped == false == isTradePanelActive == false && ...
```
`false == false == false` → `true` olduğu için **şans eseri** doğru çalışıyor; ama okunabilir değil ve biri değişirse sessizce bozulur. `&& !isCharStopped && !isTradePanelActive` yazılmalı. Ayrıca `volatile int[] STATUS_PRIORITY / HP_SP_RATE / SKILL_TIME_FOR_KEYS` — `volatile` dizi **içeriğini** korumaz (yalnızca referansı); çok thread'den yazılıyor.

### H10 — Zamanlayıcı metot adları ters anlam taşıyor
`CheckDelayTimeInSecond(x)` **true** dönerken "süre henüz dolmadı" demektir. Kod boyunca `if(!timer.CheckDelayTimeInSecond(3))` şeklinde kullanılıyor ve okuyan herkesi yanıltıyor (`StartTimeBreakMinute` içindeki `while (CheckDelayTimeInMinute(waitResult))` gibi). İsimlendirme `HasTimeNotElapsed` / `IsWithin` olarak netleştirilmeli.

### H11 — Hata/exception yönetimi neredeyse yok
* 44 dosyada toplam **20** `catch` bloğu var; T1/T2/T3 thread gövdelerinde hiç `try/catch` yok → tek bir `NullReferenceException` botu sessizce öldürür, UI "çalışıyor" göstermeye devam eder.
* `Program.Main`'de `Application.ThreadException` / `AppDomain.CurrentDomain.UnhandledException` kaydı yok.
* Loglama yalnızca `Console.WriteLine` (`DebugPfCnsl`, 52 adet) ama proje **WinExe** → konsol yok, hiçbir mesaj kullanıcıya ulaşmaz. Dosya logu ve UI'da log paneli şart.

### H12 — Yol üretimi kırılgan
`FileHandler.PathWantedWayFromBase` exe yolunu `\` ile bölüp **son iki segmenti atıyor** ve `\\` ile geri birleştiriyor (Linux/UNC/tek-segment yollarda çöker; `bin\Debug` varsayımı sabit kodlanmış). `Path.Combine` + `BaseDirectory` ile tek satırda çözülebilir.

---

## 4. Yapı / Depo Hijyeni

| # | Bulgu | Etki |
|---|---|---|
| R1 | `.gitignore` yok; `bin/` + `obj/` (exe, dll, pdb, cache, manifest) commit edilmiş (40 dosya) | Repo şişer, her derlemede gereksiz diff, Windows dışı araçlarda karışıklık |
| R2 | Kökte **yetim kopyalar**: `GameObjectCoordinates.cs` (549 satır), `CheckGameCoordinate.cs` (90 satır) — csproj'da yok, `Sources/` altındaki gerçek dosyaların neredeyse aynısı (tek fark: bir yorum satırı) | Yanlış dosyayı düzenleme riski |
| R3 | Proje/solution adı `MusicPlayerApp`, çıktı adı `Metin2AutoFishCSharp`, namespace'ler karışık | Kafa karışıklığı; UI sekmesinde bile "MusicPlayer" etiketi kalmış |
| R4 | `packages/` klasörü repoda yok ama `HintPath`'ler oraya bakıyor | Temiz kopyada `nuget restore` zorunlu (yoksa derleme hatası) |
| R5 | `Metin2AutoFishCSharp_TemporaryKey.pfx` + `ManifestCertificateThumbprint` repoda; `PublishUrl=C:\Users\Herkes\Desktop\...`, `UpdateUrl=http://localhost/...` | Gereksiz/kişisel yapılandırma sızıntısı |
| R6 | **Kişisel bankacılık bilgisi UI'a gömülü:** `MainForm.Designer.cs:1124-1145` → "Kuveyt Türk", "Mümtaz Taşdelen", IBAN `TR160020500009467150900001` | Fork'ta başkasının IBAN'ı görünüyor. Kaldırılması veya config'e taşınması önerilir |
| R7 | Sürüm bilgisi 4 yerde ve tutarsız: `version.txt=1.0.2`, `AssemblyInfo=1.0.0.0`, `csproj ApplicationRevision=5`, README `v2.3` | Güncelleme kontrolü yanlış alarm üretir |
| R8 | Uygulama **yönetici** istiyor (README) ama manifest'te `requireAdministrator` yok; DPI bildirimi de yok | Yüksek DPI'da koordinatlar kayar, SendInput yükseltilmiş pencereye ulaşmayabilir |
| R9 | `README.md` biçim bozuk: `# v2.3` maddesi iç içe, tarih satırları liste dışında | Dokümantasyon kalitesi |
| R10 | Boş event handler'lar (`tabPage1_Click`, `label21_Click`, `groupBox1_Enter`, `checkBoxEnableTime_CheckedChanged_1`), yorum satırına alınmış yüzlerce blok | Ölü kod; bakımı zorlaştırıyor |

---

## 5. Performans ve Ölçeklenebilirlik Notları

1. **Tek ekran görüntüsü → çok tüketici:** T2 her turda 5 ayrı `CaptureSpecifiedScreen` çağrısı yapıyor, T1/T3 kendi yakalamalarını yapıyor. Tek bir "frame" alınıp tüm kontrollerin o frame üzerinde yapılması GDI yükünü ~%70 azaltır.
2. **`ImageObjects` tekilleştirilmemiş:** `ThreadsHandler`, `MainForm`, `TelegramBot` aynı `ImageObjects` örneğini paylaşsa da `FishingHandle`, `PrepareFishing`, `CharSpecialThings`, `GameObjectCoordinates` vb. her biri yeni `ScreenShotWinAPI`/`GameObjectCoordinates` üretiyor (toplamda 30+ nesne). Zararsız ama gereksiz.
3. **`CheckGameCoordinate` statik state:** `isScannedMt2Icon`, `timerGame`, `currentScreenGamePoint` statik; üç thread aynı anda `CheckGameScreenPlace()` çağırıyor → 60 sn'de bir **aynı anda** tam ekran taraması tetiklenebilir (CPU pikleri). `lock` + önbellek gerekli.
4. **`ParseWordsFromOneLineArea` iki kez tam tarama yapıyor** (önce kutu sayısını bulmak, sonra kutuları üretmek) ve her kutu için `FindBorderAreaForWantedColor` yeniden ekran görüntüsü alıyor (V2 bunu düzeltmiş, ama V1 hâlâ `Chatting`/`WhispersHandle` tarafında kullanılıyor).
5. **`DetectWorms` iki geçişte farklı eşik kullanıyor** (ilk geçiş `>48/>48/>39`, ikinci `>50/>50/>40`) → sayı ile içerik tutarsız olabilir, dizi taşması riski (`counter` ikinci geçişte daha az artar, sorun çıkmaz ama mantık hatası).

---

## 6. Önceliklendirilmiş Yol Haritası — **UYGULAMA DURUMU**

> Tüm maddeler bu bakım turunda uygulanmıştır. Durum sütunu 27.09.2026 itibarıyladır.

### Faz 0 — Derlenebilirlik

| # | Madde | Durum |
|---|---|---|
| 1 | `Sources\VersionChecker.cs` → csproj'a ekle (K1) | ✅ Eklendi; ayrıca `System.Configuration` referansı da eklendi (eksikti) |
| 2 | `Content` öğelerine `CopyToOutputDirectory=PreserveNewest` (K3) | ✅ 161 öğe güncellendi |
| 3 | `.gitignore` ekle, `bin/`+`obj/`'i takipten çıkar (R1) | ✅ 40+ dosya takipten çıkarıldı |
| 4 | Kökteki yetim kopyaları sil (R2) | ✅ `GameObjectCoordinates.cs`, `CheckGameCoordinate.cs` silindi |

### Faz 1 — Çökme ve sızıntı onarımları

| # | Madde | Durum |
|---|---|---|
| 5 | Bitmap/GDI yaşam döngüsü: `using` + `LockBits` + palet silme (H5, H6) | ✅ `ScreenShotWinAPI`, `ImageProcess`, `FileHandler`, `DebugPfCnsl` yeniden yazıldı (manuel `FullScreen` aracı bu değişiklikte kaldırıldı); ölü `IsMatch(Bitmap,…)` kaldırıldı |
| 6 | T1/T2/T3 `try/catch` + global exception handler + dosya logu (H11) | ✅ `RunThreadSafely`, `Program.Main` handler'ları, `Sources/Debugs/FileLogger.cs` |
| 7 | `SaveImageAsPng` null kontrolü (H4), tıklama Y parametresi (H1), minimap indeksi (H2) | ✅ Üçü de düzeltildi |
| 8 | `GrillFishingHandle` dinamik + Denizkızı (H3) | ✅ `PrepareFishing.cs` yeniden yazıldı; sonsuz özyineleme sınırlandı |

### Faz 2 — Davranış / kararlılık

| # | Madde | Durum |
|---|---|---|
| 9 | Busy-wait → `Thread.Sleep`; kesilebilir `Stop()` (H7) | ✅ `Stop()` 30 sn zaman aşımlı yoklamaya çevrildi, boşta 200 ms uyku, pot döngüleri 40 denemeyle sınırlandı |
| 10 | Tek `Random` (H8), `CanXxxRightNow()` sadeleştirme + dizi kilitleri (H9) | ✅ Paylaşılan `Random` + kilit; açık koşullar; `SetHpSpRate/SetStatusPriority/SetSkillTime` setter'ları |
| 11 | Telegram: token config'den, yalnızca aktifken başlat, null-safe, komut duyarsızlığı (K5) | ✅ `TelegramBot.cs` yeniden yazıldı; arayüze token kutusu + "Token'ı Kaydet" eklendi |
| 12 | VersionChecker: doğru repo, async, kısa timeout, tek sürüm kaynağı (K2, R7) | ✅ `App.config` anahtarları, 5 sn zaman aşımı, arka plan görevi, `AssemblyFileVersion` = `version.txt` = **1.0.3** |

### Faz 3 — Bakım kolaylığı

| # | Madde | Durum |
|---|---|---|
| 13 | Namespace/proje adı birleştirme, ölü kod ve boş handler temizliği (R3, R10) | ⚠️ Kısmen: boş handler'lar (`tabPage1_Click`, `label21_Click`, `groupBox1_Enter`, `label27_Click`, `checkBoxEnableTime_CheckedChanged_1`) ve ölü `IsMatch` kaldırıldı; pencere başlığı "MusicPlayer" → "Metin2 Balık / Level / Enerji Botu"; arayüz metinleri Türkçeleştirildi. **Namespace birleştirmesi yapılmadı** (aşağıdaki nota bakın) |
| 14 | `FileHandler` yol çözümünü `BaseDirectory` tabanlı yap (H12, K4) | ✅ `Path.Combine` + `BaseDirectory`; `ChatFileHandler` yeniden yazıldı |
| 15 | Manifest: `requireAdministrator` + `PerMonitorV2` DPI (R8) | ✅ `app.manifest` eklendi ve csproj'a bağlandı |
| 16 | IBAN/isim bilgilerini UI'dan kaldır (R6) | ✅ `label30/31/32/33` + `textBox1` Designer'dan tamamen silindi; README'deki bağış yönlendirmesi kaldırıldı |
| 17 | README düzeltmeleri + sürüm geçmişi (R9) | ✅ README yeniden yazıldı (kurulum, Telegram token adımları, sorun giderme tablosu, sürüm geçmişi) |

### Bilinçli olarak yapılmayanlar

* **Namespace birleştirmesi (`MusicPlayerApp` → tek isim):** 48 dosyada ve Designer/`.resx`
  kaynak adlarında toplu yeniden adlandırma gerekiyor. Bu ortamda derleyici
  çalıştırılamadığı için (aşağıya bakın) böyle bir değişiklik **doğrulanmadan** yapıldığında
  projeyi çalışmaz hale getirme riski taşıyor. Kasıtlı olarak ertelendi; ayrı bir iş olarak,
  derleyebilen bir makinede yapılması önerilir.
* **Tek ekran görüntüsünü tüm tüketicilerle paylaşma (Performans notu 1):** Davranışı
  değiştirebilecek kapsamlı bir yeniden yapılandırma; bu tur kapsamına alınmadı.
* **`ParseWordsFromOneLineArea` V1 → V2 geçişi (Performans notu 4):** Aynı gerekçe.

## 7. Bu Ortamda Doğrulama Durumu

**Kullanıcının talebi üzerine derleyici kurulumu denendi ve BAŞARISIZ oldu.** Bu sandbox
yalnızca `github.com`, `api.github.com`, `pypi.org` ve `registry.npmjs.org` adreslerine
çıkabiliyor; .NET/Mono kurulumunun gerektirdiği tüm dağıtım noktaları kapalı:

| Kaynak | Sonuç |
|---|---|
| `nuget.org` | erişilemiyor (000) |
| `dotnetcli.azureedge.net`, `builds.dotnet.microsoft.com`, `aka.ms` | erişilemiyor |
| `download.mono-project.com` | erişilemiyor |
| `dotnet-install.sh` | çalışıyor ama yönlendiği CDN kapalı |
| `apt-get update` (`deb.debian.org`) | erişilemiyor |

Sonuç: `dotnet`, `mono`, `msbuild`, `csc` **yok**; hedef framework .NET Framework 4.7.2
(Windows'a özgü: `System.Windows.Forms` + P/Invoke). **Bu ortamda derleme doğrulaması
yapılamaz.**

### Bunun yerine yapılan doğrulama

`tools/cscheck.py` adında bir statik denetim aracı yazıldı (depoda `tools/` `.gitignore`
içinde, yani repoya girmez — yalnızca bu çalışma kopyasında bulunur):

1. **Süslü parantez dengesi** — C# sözdizimini karakter karakter tarayarak; diziler
   (`"..."`, `@"..."`), karakter sabitleri (`'x'`, `'\''`) ve yorumlar maskelenir.
   Türkçe metinlerdeki kesme işaretleri (`Token'ı`, `'Test Et' butonuna`) karakter sabiti
   sanılmaz — bu, ilk sürümde sahte hata üretmişti ve düzeltildi.
2. **`.csproj` tutarlılığı** — diskte olup projede olmayan (derlenmez) ya da projede olup
   diskte olmayan (derleme hatası) `.cs` dosyaları.
3. **Designer ↔ code-behind** — `MainForm.Designer.cs`'in bağladığı her olay işleyicisinin
   `MainForm.cs`'te tanımlı olduğu.

Ayrıca `App.config`, `app.manifest`, `MusicPlayerApp.csproj` ve `packages.config` XML
olarak doğrulandı.

**Son denetim sonucu:** `python3 tools/cscheck.py .` → *48 dosya, sorun bulunamadı.*

### Derleyebilen bir makinede yapılması önerilen son kontrol

```powershell
nuget restore MusicPlayerApp.sln
msbuild MusicPlayerApp.sln /p:Configuration=Debug /t:Rebuild
```

Beklenen: 0 hata. Olası uyarılar (zararsız): kullanılmayan `private` alanlar
(`ThreadsHandler` içinde bilinçli olarak bırakıldı — davranışı değiştirmemek için) ve
`async` metotlarda `await` bulunmaması.
