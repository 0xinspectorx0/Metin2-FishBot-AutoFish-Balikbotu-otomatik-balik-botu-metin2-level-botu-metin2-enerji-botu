using MusicPlayerApp.Debugs;
using MusicPlayerApp.Sources;
using MusicPlayerApp.Sources.CharacterHandle;
using MusicPlayerApp.Sources.ImageHandle;
using System;
using System.Configuration;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;

namespace Metin2AutoFishCSharp.Sources
{
    /// <summary>
    /// Telegram üzerinden botu uzaktan durdurma ve durum bildirimleri alma entegrasyonu.
    /// </summary>
    /// <remarks>
    /// <para><b>Bakım turunda düzeltilenler:</b></para>
    /// <list type="bullet">
    /// <item>Token kaynak kodda <c>"Add Your Token"</c> olarak duruyordu ve istemci
    /// <b>her açılışta</b> (kullanıcı Telegram'ı hiç açmasa bile) oluşturuluyordu; geçersiz
    /// token ile uygulama açılışında yakalanmayan hata üretiliyordu. Artık token
    /// <c>telegram.ini</c> veya <c>App.config</c> içinden okunuyor ve istemci yalnızca
    /// kullanıcı özelliği etkinleştirdiğinde oluşturuluyor.</item>
    /// <item><c>message.Text</c> null olabiliyor (örneğin fotoğraf/sticker gönderildiğinde);
    /// <see cref="NullReferenceException"/> koruması eklendi.</item>
    /// <item>README'de "DURDUR" yazmasına rağmen kod <c>"Durdur"</c> bekliyordu. Artık
    /// büyük/küçük harf duyarsız ve birkaç eş anlamlı komut kabul ediliyor.</item>
    /// <item><see cref="ErrorMessage"/> boş bir sohbet kimliğine mesaj göndermeye
    /// çalışıp ikinci bir hata üretiyordu; artık yalnızca loglanıyor.</item>
    /// </list>
    /// </remarks>
    internal class TelegramBot
    {
        /// <summary>Token'ın kaydedildiği dosya (exe klasöründe).</summary>
        private const string TOKEN_FILE_NAME = "telegram.ini";
        private const string TOKEN_CONFIG_KEY = "TelegramBotToken";
        private const string TOKEN_PLACEHOLDER = "Add Your Token";

        /// <summary>Durdurma komutu olarak kabul edilen metinler (küçük harfle karşılaştırılır).</summary>
        private static readonly string[] stopCommands = new string[]
        {
            "durdur", "dur", "/durdur", "/stop", "stop", "kapat"
        };

        /// <summary>Kullanıcı onayından geçen son mesaj (sohbet kimliği buradan alınır).</summary>
        public static Message TELEGRAM_SEND_MESSAGE = null;

        /// <summary>Kullanıcı "Test Et" ile bağlantıyı onayladı mı?</summary>
        public static bool TELEGRAM_BOT_IS_READY = false;

        /// <summary>Onay penceresi açık mı? (Aynı anda ikinci pencere açılmasın diye.)</summary>
        public static bool TELEGRAM_DIALOG_PANEL_ACTIVE = false;

        private static TelegramBotClient botClient;
        private static ScreenShotWinAPI screenShot;
        private static ImageObjects imagesObject;
        private static GameObjectCoordinates coor;

        private static CancellationTokenSource receiveCancellation;
        private static bool isReceiving;

        private GameInputHandler inputs;

        /// <summary>Uygulama genelindeki tek örnek (arayüz Start/Stop için kullanır).</summary>
        public static TelegramBot Instance { get; private set; }

        public TelegramBot(ImageObjects imageObject)
        {
            // imageObject null gelirse singleton'a düşülür.
            imagesObject = imageObject ?? ImageObjects.Instance;
            screenShot = new ScreenShotWinAPI();
            coor = new GameObjectCoordinates(imagesObject);
            inputs = new GameInputHandler();
            Instance = this;

            FileLogger.Info("TelegramBot hazır (token yapılandırması: " +
                (IsTokenConfigured ? "var" : "YOK") + ")");
        }

        #region Token yönetimi

        /// <summary>
        /// Kullanılabilir bir token yapılandırılmış mı?
        /// Önce <c>telegram.ini</c>, sonra <c>App.config</c> kontrol edilir.
        /// </summary>
        public static bool IsTokenConfigured
        {
            get { return !string.IsNullOrWhiteSpace(GetToken()); }
        }

        /// <summary>Yapılandırılmış token'ı döner; yoksa <see cref="string.Empty"/>.</summary>
        public static string GetToken()
        {
            string token = ReadTokenFromFile();
            if (!string.IsNullOrWhiteSpace(token))
            {
                return token.Trim();
            }

            try
            {
                token = ConfigurationManager.AppSettings[TOKEN_CONFIG_KEY];
            }
            catch (Exception ex)
            {
                FileLogger.Warning("App.config üzerinden Telegram token'ı okunamadı: " + ex.Message);
                token = null;
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                return string.Empty;
            }

            token = token.Trim();
            if (string.Equals(token, TOKEN_PLACEHOLDER, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }
            return token;
        }

        /// <summary>
        /// Token'ı exe klasöründeki <c>telegram.ini</c> dosyasına kaydeder.
        /// </summary>
        /// <returns>Kayıt başarılıysa <c>true</c>.</returns>
        public static bool SaveToken(string token)
        {
            try
            {
                string path = Path.Combine(FileHandler.BaseDirectory, TOKEN_FILE_NAME);
                string value = token == null ? string.Empty : token.Trim();

                File.WriteAllText(path, "TelegramBotToken=" + value + Environment.NewLine,
                    System.Text.Encoding.UTF8);

                FileLogger.Info("Telegram token'ı kaydedildi: " + path +
                    (value.Length == 0 ? " (boş — özellik devre dışı)" : string.Empty));
                return true;
            }
            catch (Exception ex)
            {
                FileLogger.Error("Telegram token'ı kaydedilemedi", ex);
                return false;
            }
        }

        private static string ReadTokenFromFile()
        {
            try
            {
                string path = Path.Combine(FileHandler.BaseDirectory, TOKEN_FILE_NAME);
                if (!File.Exists(path))
                {
                    return null;
                }

                foreach (string rawLine in File.ReadAllLines(path))
                {
                    string line = rawLine == null ? string.Empty : rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";"))
                    {
                        continue;
                    }

                    int separatorIndex = line.IndexOf('=');
                    if (separatorIndex <= 0)
                    {
                        continue;
                    }

                    string key = line.Substring(0, separatorIndex).Trim();
                    if (string.Equals(key, TOKEN_CONFIG_KEY, StringComparison.OrdinalIgnoreCase))
                    {
                        string value = line.Substring(separatorIndex + 1).Trim();
                        if (string.Equals(value, TOKEN_PLACEHOLDER, StringComparison.OrdinalIgnoreCase))
                        {
                            return string.Empty;
                        }
                        return value;
                    }
                }
            }
            catch (Exception ex)
            {
                FileLogger.Warning("telegram.ini okunamadı: " + ex.Message);
            }
            return null;
        }

        #endregion

        #region Alma döngüsü (receiver)

        /// <summary>
        /// Telegram güncellemelerini almaya başlar. Token yapılandırılmamışsa
        /// <c>false</c> döner ve hiçbir istemci oluşturmaz.
        /// </summary>
        public bool StartReceiver()
        {
            if (isReceiving)
            {
                FileLogger.Debug("Telegram alıcı döngüsü zaten çalışıyor");
                return true;
            }

            string token = GetToken();
            if (string.IsNullOrWhiteSpace(token))
            {
                FileLogger.Warning("Telegram token'ı yapılandırılmamış, alıcı başlatılmadı");
                return false;
            }

            try
            {
                botClient = new TelegramBotClient(token);
                receiveCancellation = new CancellationTokenSource();

                ReceiverOptions receiverOptions = new ReceiverOptions
                {
                    AllowedUpdates = { }
                };

                Task receiveTask = botClient.ReceiveAsync(OnMessage, ErrorMessage, receiverOptions,
                    receiveCancellation.Token);

                // Beklenmeyen hataların "gözlenmeyen Task" olarak süreci düşürmesini engelle.
                receiveTask.ContinueWith(task =>
                {
                    if (task.IsFaulted && task.Exception != null)
                    {
                        FileLogger.Error("Telegram alıcı döngüsü hata ile sonlandı", task.Exception.GetBaseException());
                    }
                }, TaskContinuationOptions.OnlyOnFaulted);

                isReceiving = true;
                FileLogger.Info("Telegram alıcı döngüsü başlatıldı");
                return true;
            }
            catch (Exception ex)
            {
                FileLogger.Error("Telegram alıcı döngüsü başlatılamadı (token hatalı olabilir)", ex);
                isReceiving = false;
                return false;
            }
        }

        /// <summary>Alma döngüsünü durdurur.</summary>
        public void StopReceiver()
        {
            try
            {
                if (receiveCancellation != null)
                {
                    receiveCancellation.Cancel();
                    receiveCancellation.Dispose();
                    receiveCancellation = null;
                }
            }
            catch (Exception ex)
            {
                FileLogger.Warning("Telegram alıcı döngüsü durdurulurken hata: " + ex.Message);
            }
            finally
            {
                isReceiving = false;
                TELEGRAM_BOT_IS_READY = false;
                FileLogger.Info("Telegram alıcı döngüsü durduruldu");
            }
        }

        /// <summary>Alıcı döngüsü çalışıyor mu?</summary>
        public static bool IsReceiving
        {
            get { return isReceiving; }
        }

        #endregion

        #region Mesaj işleme

        /// <summary>Gelen güncellemeleri işler.</summary>
        public async Task OnMessage(ITelegramBotClient client, Update update, CancellationToken cancelToken)
        {
            try
            {
                if (!ThreadGlobals.isTelegramBotActive)
                {
                    return;
                }

                Message message = update.Message;
                if (message == null)
                {
                    return;
                }

                // Fotoğraf/sticker gibi mesajlarda Text null olur (eski kod burada
                // NullReferenceException üretiyordu).
                string text = message.Text ?? string.Empty;
                string firstName = message.Chat != null && message.Chat.FirstName != null
                    ? message.Chat.FirstName
                    : "bilinmeyen";

                if (!TELEGRAM_BOT_IS_READY)
                {
                    if (TELEGRAM_DIALOG_PANEL_ACTIVE)
                    {
                        return;
                    }

                    TELEGRAM_SEND_MESSAGE = message;

                    string reply = text.Trim() == "/start"
                        ? "Bilgileriniz alındı. Programdaki 'Test Et' düğmesine basıp açılan " +
                          "onay penceresine EVET deyiniz. Sohbet kimliğiniz = " + message.Chat.Id +
                          ", adınız = " + firstName
                        : "Bilgileriniz alındı. Programdaki 'Test Et' düğmesine basıp açılan " +
                          "onay penceresine EVET deyiniz. " + firstName;

                    await client.SendTextMessageAsync(message.Chat.Id, reply, cancellationToken: cancelToken)
                        .ConfigureAwait(false);

                    FileLogger.Info("Telegram bağlantı isteği alındı: " + firstName + " (id=" + message.Chat.Id + ")");
                    return;
                }

                // Bağlantı kurulduktan sonra yalnızca durdurma komutu işlenir.
                if (IsStopCommand(text))
                {
                    FileLogger.Info("Telegram üzerinden DURDUR komutu alındı");

                    // Program içi Ctrl+O kısayolunu tetikleyen tuş kombinasyonu gönderilir.
                    // Böylece hangi mod aktifse o modun durdurma akışı birebir çalışır.
                    inputs.KeyDown(KeyboardInput.ScanCodeShort.LCONTROL);
                    inputs.KeyDown(KeyboardInput.ScanCodeShort.KEY_O);
                    TimerGame.SleepRandom(30, 50);
                    inputs.KeyRelease(KeyboardInput.ScanCodeShort.LCONTROL);
                    inputs.KeyRelease(KeyboardInput.ScanCodeShort.KEY_O);

                    await SendMessageTelegramAsync("Program durduruluyor.").ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                FileLogger.Error("Telegram gelen mesaj işlenemedi", ex);
            }
        }

        /// <summary>
        /// Alıcı döngüsündeki hataları işler.
        /// </summary>
        /// <remarks>Eski sürüm <c>SendTextMessageAsync("", ...)</c> ile boş bir sohbet
        /// kimliğine mesaj göndermeye çalışıyor ve ikinci bir hata üretiyordu.</remarks>
        public Task ErrorMessage(ITelegramBotClient client, Exception exception, CancellationToken cancelToken)
        {
            if (exception is ApiRequestException requestException)
            {
                FileLogger.Error("Telegram API hatası (kod: " + requestException.ErrorCode + ")", requestException);

                if (requestException.ErrorCode == 401 || requestException.ErrorCode == 404)
                {
                    FileLogger.Warning("Telegram token'ı geçersiz görünüyor. 'Bot Token' kutusundan " +
                        "veya App.config içindeki TelegramBotToken anahtarından düzeltiniz.");
                }
            }
            else
            {
                FileLogger.Error("Telegram alıcı hatası", exception);
            }
            return Task.CompletedTask;
        }

        private static bool IsStopCommand(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string normalized = text.Trim().ToLowerInvariant();
            for (int i = 0; i < stopCommands.Length; i++)
            {
                if (normalized == stopCommands[i])
                {
                    return true;
                }
            }
            return false;
        }

        #endregion

        #region Mesaj gönderme

        /// <summary>
        /// Bağlantısı onaylanmış kullanıcıya mesaj gönderir (ateşle-ve-unut).
        /// </summary>
        /// <remarks>Metot imzası geriye dönük uyumluluk için korunmuştur; tüm hatalar
        /// loglanır, çağıran tarafa yayılmaz.</remarks>
        public static Task SendMessageTelegram(string text)
        {
            Task sendTask = SendMessageTelegramAsync(text);
            if (sendTask != null)
            {
                sendTask.ContinueWith(task =>
                {
                    if (task.IsFaulted && task.Exception != null)
                    {
                        FileLogger.Error("Telegram mesajı gönderilemedi", task.Exception.GetBaseException());
                    }
                }, TaskContinuationOptions.OnlyOnFaulted);
            }
            return sendTask ?? Task.CompletedTask;
        }

        /// <summary>
        /// Bağlantısı onaylanmış kullanıcıya mesaj gönderir. Karakter adı biliniyorsa
        /// mesaja eklenir.
        /// </summary>
        public static async Task SendMessageTelegramAsync(string text)
        {
            if (!ThreadGlobals.isTelegramBotActive)
            {
                return;
            }
            if (botClient == null)
            {
                FileLogger.Debug("Telegram istemcisi hazır değil, mesaj gönderilmedi: " + text);
                return;
            }
            if (TELEGRAM_SEND_MESSAGE == null || TELEGRAM_SEND_MESSAGE.Chat == null)
            {
                FileLogger.Debug("Telegram alıcısı henüz onaylanmadı, mesaj gönderilmedi: " + text);
                return;
            }

            string fullText = string.IsNullOrEmpty(CharInfo.CharNameString)
                ? text
                : text + " | Karakter ismi = " + CharInfo.CharNameString;

            try
            {
                await botClient.SendTextMessageAsync(TELEGRAM_SEND_MESSAGE.Chat.Id, fullText)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                FileLogger.Error("Telegram mesajı gönderilemedi", ex);
            }
        }

        #endregion
    }
}
