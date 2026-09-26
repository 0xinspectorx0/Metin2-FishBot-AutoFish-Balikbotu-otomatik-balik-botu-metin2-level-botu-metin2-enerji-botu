# Metin2 Otomatik Balık / Level / Enerji Botu

## GENEL

**Sadece 1 oyun hesabını destekler.** Program, ekran görüntüsü alma (yani görüntü işleme)
mantığıyla çalışır; alınan görüntü işlenerek gerekli işlemler yapılır. **Yani gerçek bir
kişi balık tutuyormuş gibi davranır ve bunun dışında bilgisayarınızda başka bir program
çalıştırmanız ya da program açıkken başka bir işlem yapmanız mümkün DEĞİLDİR.**

Telegram hariç; harf/sayı tespiti, nesne tespiti vb. algoritmaların hepsi proje sahibine aittir.

> [!CAUTION]
> Makro kullanmak **ban** sebebidir. Bu proje eğitim amaçlı yazılmıştır ve hiçbir sorumluluk
> **kabul edilmemektedir**.

> [!WARNING]
> **800x600 pencere modunda oynanmalı** ve **oyun penceresinin yeri asla kıpırdatılmamalı.**
> Windows güncelleştirmelerinde **.NET Framework** güncelleştirmelerini mutlaka yapın ya da
> bilgisayarınızın **en güncel** durumda olduğundan emin olun.

> [!TIP]
> Videolu anlatım için https://www.youtube.com/watch?v=MEqj1Hd0eiI adresini ziyaret edebilirsiniz.

> [!NOTE]
> Karakter birileriyle yazışmışsa **ChatResources/ChatQuestionAnswer/ChatQuestAnswer.txt** ve
> **ChatResources/ChatQuestionAnswer/RecordWhisperChat.txt** dosyalarından inceleyebilir,
> çekilen ekran görüntülerini **ScreenShot** klasöründe bulabilirsiniz.
> **Ctrl + O** tuş kombinasyonu ile programı her an durdurabilirsiniz.

---

## PROGRAMIN ÇALIŞTIRILMASI

1. Arşivi istediğiniz bir klasöre çıkartın.
2. `Bin/Debug` içindeki **Metin2AutoFishCSharp.exe** dosyasını **yönetici olarak çalıştırın**.
   (Program artık `app.manifest` içinde `requireAdministrator` bildiriyor; yönetici olmadan
   çalıştırıldığında Windows zaten yetki ister.)
3. Metin2'yi **800x600 pencere modunda** açın ve pencereyi **taşımayın**.

### Gereksinimler

| Gereksinim | Açıklama |
|---|---|
| İşletim sistemi | Windows 8 / 10 / 11 |
| .NET Framework | 4.7.2 veya üzeri |
| İşlemci | En az 2.80 GHz (daha düşük hızlarda da çalışır ama balık tutma sayısı ciddi biçimde düşer) |
| RAM | Başka program çalışmıyorsa 2 GB yeterli |
| Çözünürlük/ölçeklendirme | Oyun penceresi 800x600; Windows ölçeklendirme (DPI) fark etmez — program `PerMonitorV2` DPI bildirimiyle derlenir |

### Hazır exe'yi nereden alırım? (otomatik derleme)

Depoya `.github/workflows/build.yml` eklendi. Bu iş akışı GitHub'ın **Windows**
koşucusunda gerçek bir MSBuild derlemesi yapar:

* `v*` biçiminde bir etiket push edildiğinde → **Release** oluşturur ve
  `Metin2AutoFishCSharp-vX.Y.Z-win-x64.zip` dosyasını (exe + dll + `Images/`,
  `Fishes/`, `ChatResources/` … içerik klasörleri) asset olarak ekler.
* `main` / `arena/**` dallarına push ve PR'larda → derlemeyi doğrular, zip'i
  **artifact** olarak saklar (Actions sayfasından indirilebilir).
* Elle de çalıştırılabilir: depo → **Actions** → *Derle ve paketle* → **Run workflow**.

> [!NOTE]
> Depo ayarlarında Actions kapalıysa iş akışı çalışmaz:
> **Settings → Actions → General → "Allow all actions and workflows"**.

### Geliştirici olarak derleme

```powershell
# NuGet paketleri depoda TUTULMUYOR (packages/ .gitignore içinde).
# Bu yüzden temiz bir kopyada önce paketleri geri yükleyin:
nuget restore MusicPlayerApp.sln
msbuild MusicPlayerApp.sln /p:Configuration=Release
```

> [!IMPORTANT]
> `ScreenShot`, `Fishes`, `ChatResources` gibi klasörler `.csproj` içinde `Content` olarak
> tanımlıdır ve `CopyToOutputDirectory=PreserveNewest` ile çıktı klasörüne kopyalanır.
> Bu dosyaları **silmezseniz veya taşımazsanız** program açılışta hata verir.

---

## PROGRAMIN YAPABİLDİKLERİ

### Balık Botu (Fishing)

- Yabbie, Palamut, Altın Sudak, Kurbağa, Kadife ve Denizkızı tutma; kurbağa/kadifeyi kızartma
- **Hepsi** seçeneği ile tümünü tutma ve envanterdeki balıkları kamp ateşinde yakma
  (envanterdeki bütün nesneleri ateşe sürükler, yalnızca balıklar yanar)
- Karakter öldüğünde kanal değiştirme
- Haritada tespit edilen oyuncu varsa yavaş tutma (**Adapte tutma**)
- Balıkçıdan kamp ateşi alıp envanterdeki balıkları yakma
- Solucan alıp slotlara dizme
- Oyundan atıldığında hesaba otomatik giriş (GameForge simgesi yalnızca görev çubuğunda
  olmalı ve sadece balık tutan hesap açık olmalı)
- Fısıltı ve sohbet yazılarını tespit edip cevap verme
- Zamanlayıcı ile belirli dakikalarda çalışma/mola verme/tamamen durdurma
- Mola verdiğinde rastgele olarak karakter atıp bekler ya da çıkış atıp bekler

#### Zamanlayıcı değerleri

- Buradaki değerler **dakika** cinsindendir.
- **Min Aktiflik** ve **Max Aktiflik**, balık tutma aralıklarıdır.
- **Min-Max Mola**, aktiflik süresi bittikten sonra verilecek moladır. Örneğin `3 6`
  (aralarında boşluk olacak şekilde iki sayı; birden fazla basamaklı olabilir).
- Zamanlayıcıyı aktif ettiyseniz **Oyun Durdurma** değerini mutlaka girin
  (toplam çalışılacak süreyi ifade eder).

#### "Sohbeti Denetle" (Check Chat) butonu

- Butona basıldığında "Tespit Edilecek Kelimeler", "Edilen Tespite Göre Verilecek Cevaplar"
  ve en altta kayıtlı olanları gösteren bir pencere açılır.
- Birden fazla kelime eklerken mutlaka `,` (virgül) ile ayırın.
- İki alanı da doldurduktan sonra **Verileri Yükle** butonuna basın.
- Örnek — üst kısma `ne yapıyorsun,ne yaptın,ne yaparsın`, cevap kısmına
  `iyidir senden ne haber,iyiyim balık tutuyorum,sağol çalışıyorum`.

### Level / Farm Bölümü

- Belirlediğiniz oranlara göre HP ve SP potu basma (envanterinizde iki türden de pot olmalı)
- Karakter öldüğünde çıkış atıp rastgele kanal değiştirme
- 60 levele kadar, hangi statü değeri yüksekse ona göre statü verme
  (ör. **HP=4, SP=3, STR=2, DEX=1** ise öncelik sırası HP → SP → STR → DEX)
- ETP görevini aldıysanız yakınınıza düşen ETP'yi toplama
- Beceriler süre mantığıyla çalışır: süresi gelince hedef olsun olmasın atanan tuşa basar.
  Bu yüzden **hava kılıcı** gibi pasif/uzaktan vuran beceriler daha işlevlidir.
- Hızlı erişim tuşlarında pot yoksa envanterdeki potları oraya taşır (XXL potları algılamaz).

### Enerji Kristali Bölümü

- Sadece **kırmızı bayrakta** ve **35 level üstü** hesapta, simyacıdan görev alınmışken çalışır.
- Telegram aktifse, hesabınızda **yer yoksa veya para bittiyse** size mesaj gönderir ve karakter çalışmaz.
- Silah satıcısından alıp simyacıya gider, sürükler; bittikten sonra döngü devam eder.
- Takılma olduğunda otomatik kanal değiştirir.

---

## Haberleşme ve Diğer Bölümü

### 1) Telegram bot token'ınızı girin

> [!IMPORTANT]
> Eski sürümlerde token kaynak kodun içinde `"Add Your Token"` olarak duruyordu ve bot her
> açılışta başlatılmaya çalışılıyordu. Artık token **sizden** isteniyor.

1. Telegram'da **@BotFather** ile bir bot oluşturun (`/newbot`) ve size verilen token'ı kopyalayın.
2. Programda **Haberleşme ve Diğer** sekmesindeki **Bot Token** kutusuna yapıştırın ve
   **Token'ı Kaydet** düğmesine basın. Token, exe ile aynı klasördeki `telegram.ini`
   dosyasına yazılır.
3. Alternatif olarak token'ı `Metin2AutoFishCSharp.exe.config` (yani `App.config`) içindeki
   `TelegramBotToken` anahtarına da yazabilirsiniz. `telegram.ini` doluysa o önceliklidir.

### 2) Bağlantıyı onaylayın

- **Telegram Bot Aktif Et** kutucuğunu işaretleyin.
- Telegram'da kendi botunuza `/start` yazın (veya herhangi bir mesaj gönderin).
- Programda **Test Et** düğmesine basın; "*… adlı kullanıcı size mi ait?*" sorusuna **Evet** deyin.
- Durum yazısı yeşile döner: **Bağlantı kuruldu. Kullanıma hazır**.

### 3) Uzaktan durdurma

- Bota **DURDUR** yazın (büyük/küçük harf fark etmez; `dur`, `stop`, `/stop`, `kapat` da kabul edilir).
- Bot, program içindeki **Ctrl + O** kısayolunu tetikleyerek aktif modu durdurur ve size
  "Program durduruluyor." mesajı gönderir.

---

## Günlük (log) dosyaları ve sorun giderme

Program artık tüm önemli olayları ve hataları exe klasöründeki **`Logs/bot-yyyyMMdd.log`**
dosyasına yazar (günlük dosya, iş parçacığı güvenli). Bir sorun yaşadığınızda bu dosyayı
paylaşmanız çözüm süresini ciddi biçimde kısaltır.

| Belirti | Olası neden / çözüm |
|---|---|
| "Program başlatılamadı" penceresi | `ScreenShot/*.png` dosyaları eksik/taşınmış, Metin2 açık değil ya da program yönetici değil. Log dosyasına bakın. |
| Bot hiçbir şeye tıklamıyor | Oyun penceresi taşınmış ya da 800x600 değil. Programı kapatıp oyunu istenen moda alın. |
| "Telegram token'ı geçersiz" uyarısı | Token'ı yanlış kopyalamışsınız; BotFather'dan yeniden alıp kaydedin. |
| Program aniden duruyor | Log dosyasındaki `HATA` satırlarına bakın; iş parçacığı hataları artık programı öldürmüyor, loglanıp arayüzde bildiriliyor. |

### Sürüm denetimi

Program açılırken GitHub'daki `version.txt` dosyasını **arka planda** denetler (arayüzü
bloklamaz, 5 saniye zaman aşımı). Hangi depoya bakılacağı `App.config` içindeki
`UpdateCheckRepositoryOwner` ve `UpdateCheckRepositoryName` anahtarlarıyla belirlenir.

> [!TIP]
> Projeyi **kendi hesabınıza fork'ladıysanız** `UpdateCheckRepositoryOwner` değerini kendi
> GitHub kullanıcı adınızla değiştirin; aksi halde program başka birinin deposundaki
> `version.txt`'ye bakar.

### Sürüm bilgisi nerede tutuluyor?

Tek kaynak **`Properties/AssemblyInfo.cs`** içindeki `AssemblyFileVersion` değeridir. Yeni bir
sürüm yayımlarken:

1. `Properties/AssemblyInfo.cs` → `AssemblyFileVersion` (ve isterseniz `AssemblyVersion`)
2. Kökteki `version.txt` (GitHub üzerinden yapılan güncelleme denetimi bunu okur)
3. Aşağıdaki **SÜRÜM GEÇMİŞİ** bölümü

---

## BALIK BOTU NASIL KULLANILMALI

* **Oyun penceresinin yeri hiç kıpırdamamalı** ve **800x600** pencere modunda oynanmalı.
* Balıkçı, karakterin hemen **arka kısmında** olmalı. Yani karakter geriye doğru gittiğinde
  balıkçı **mutlaka** kamera açısında görünmeli.
* Haritalar veya herhangi bir karakterin sohbet/isim kısmını kapatacak nesneler olmamalı.

![ı1ı1](https://github.com/user-attachments/assets/98d1c4ed-6728-4761-b873-b8842fe8c994)

---

## SÜRÜM GEÇMİŞİ

### 1.0.3 — Bakım turu (27.09.2026)

**Derlenebilirlik**
- `Sources/VersionChecker.cs` `.csproj`'a eklendi (dosya diskte vardı ama derlenmiyordu → proje temiz kopyada hiç derlenmiyordu).
- 161 `Content` öğesine (`ScreenShot`, `Fishes`, `ChatResources` …) `CopyToOutputDirectory=PreserveNewest` eklendi.
- `.gitignore` eklendi; `bin/` ve `obj/` takipten çıkarıldı.
- Kökteki yetim `GameObjectCoordinates.cs` / `CheckGameCoordinate.cs` kopyaları silindi.
- ClickOnce/imza artıkları (`ManifestCertificateThumbprint`, `ManifestKeyFile`, `PublishUrl`, `TargetZone`) ve depodaki `Metin2AutoFishCSharp_TemporaryKey.pfx` kaldırıldı.
- `<StartupObject>` artık `MusicPlayerApp.Program`.

**Çökme ve bellek sızıntıları**
- Ekran yakalama yeniden yazıldı: GDI palet sızıntısı giderildi, `GetPixel` yerine `LockBits` kullanıldı.
- `ImageObjects` tekilleştirildi (singleton); referans PNG'ler bir kez yükleniyor ve dispose ediliyor.
- `FullScreen` sıfır-alan çökmesi ve bitmap sızıntısı düzeltildi.
- `FileHandler.SaveImageAsPng` içindeki ters null kontrolü düzeltildi; ekran görüntüsü kayıtlarındaki 4 sızıntı kapatıldı.
- İş parçacıklarının (T1/T2/T3) gövdeleri `try/catch` ile sarıldı: tek bir hata artık botu sessizce öldürmüyor; loglanıyor, arayüzde bildiriliyor ve butonlar yeniden etkinleşiyor.
- `Program.Main`'e `Application.ThreadException` ve `AppDomain.UnhandledException` kaydı eklendi; yakalanmayan hatalar Türkçe bir pencereyle raporlanıyor.
- Ana form kurucusu `try/catch` içine alındı; açılış hatasının nedeni kullanıcıya Türkçe açıklanıyor.

**Performans**
- Boş (busy-wait) döngüler kaldırıldı: `Thread.Sleep`, `Stop()` için 30 sn zaman aşımlı yoklama, mod seçilmediğinde 200 ms boşta bekleme.
- `Random` her çağrıda yeniden yaratılmıyor (paylaşılan örnek + kilit).
- Oyun penceresi koordinat taraması paylaşımlı önbelleğe alındı (3 sn / 60 sn).
- Görüntü eşleştirme dizi tabanlı hale getirildi; `GetPixel` kullanan tüm sıcak yollar temizlendi.
- Pot doldurma döngüleri sınırlandırıldı (en fazla 40 deneme) — sonsuz döngü riski kalktı.

**Davranış düzeltmeleri**
- Balığa tıklarken Y yerine X gönderilmesi düzeltildi.
- Mini harita piksel indeksi `Height` yerine `Width` ile hesaplanıyor.
- Izgara (grill) akışı seçilen balık sayısına göre dinamik hale getirildi; Denizkızı eklendi.
- Beceri işleyicide eksik `return` düzeltildi (bot durdurulduğunda beceri basmaya devam ediyordu) ve 0 gecikmeli beceriler artık en az 1 saniye arayla basılıyor.
- `CanXxxRightNow()` içindeki zincirleme `== false ==` ifadeleri açık koşullara çevrildi; paylaşılan diziler kilit altına alındı.

**Telegram**
- Token artık `telegram.ini` veya `App.config`'ten okunuyor; arayüze **Bot Token** kutusu ve **Token'ı Kaydet** düğmesi eklendi.
- Bot yalnızca kullanıcı etkinleştirdiğinde başlatılıyor (eskiden her açılışta geçersiz token ile deneniyordu).
- `message.Text` null koruması eklendi (fotoğraf/sticker gelince program çöküyordu).
- Durdurma komutu büyük/küçük harf duyarsız: `durdur`, `dur`, `stop`, `/stop`, `kapat`.
- Arayüzdeki **IBAN/bağış** bloğu (Kuveyt Türk, isim ve hesap numarası) tamamen kaldırıldı.

**Arayüz ve bakım**
- Tüm arayüz metinleri Türkçeleştirildi (`Fishing` → `Balık Tutma`, `Waiting` → `Bekliyor`, `START` → `BAŞLAT`, hata başlıkları `Error` → `Hata` …).
- Boş olay işleyicileri (`tabPage1_Click`, `label21_Click`, `groupBox1_Enter`, `label27_Click`) ve ölü kod kaldırıldı.
- `Zamanlayıcı Aktif Et` kutusu artık klavye ile işaretlendiğinde de çalışıyor (boş `CheckedChanged` yerine gerçek işleyiciye bağlı).
- `app.manifest` eklendi: `requireAdministrator`, `PerMonitorV2` DPI bildirimi, desteklenen işletim sistemleri ve Common Controls v6.
- Sürüm bilgisi tek kaynağa indirildi (`AssemblyFileVersion` = `version.txt` = `1.0.3`).
- Dosya günlükçüsü eklendi: `Logs/bot-yyyyMMdd.log`.

### 2.3 (11.09.2024)
- Çok yavaş bilgisayarlar için "PC Yavaşsa" butonu eklendi.

### 2.2 (09.09.2024)
- Sale Title (sol altta çıkan nesne market eşya penceresi) açıkken hesabın donup kalması sorunu çözüldü; sale ekranı otomatik kapatılıyor.

### 2.1 (08.08.2024)
- "Hepsi" seçeneği aktifken "Oltaya bir şey takıldı" durumunda geçme davranışı düzeltildi.
- Tıklamalar eskisine göre daha isabetli.

### 2.0
- Kadife ve Denizkızı tutma özelliği eklendi.
- Kurbağa ve kadife artık kızartılıyor.
- "Adapte tutma" seçeneği eklendi: etrafınızda oyuncu varsa normal hızda tutar (aktif edilmezse hızlı tutar).
