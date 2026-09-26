using MusicPlayerApp.Debugs;
using System;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MusicPlayerApp.Sources
{
    /// <summary>
    /// GitHub üzerindeki <c>version.txt</c> dosyasını okuyarak yeni sürüm olup
    /// olmadığını denetler.
    /// </summary>
    /// <remarks>
    /// <para><b>Bakım turunda düzeltilenler:</b></para>
    /// <list type="bullet">
    /// <item>Dosya csproj'a dahil değildi (<c>MainForm</c> çağırdığı halde), yani proje
    /// temiz bir makinede derlenmiyordu. Artık derleniyor.</item>
    /// <item>Sürüm adresi başka bir GitHub hesabına (<c>mmtcoder</c>) bakıyordu. Artık
    /// depo sahibi <c>App.config</c> içindeki <c>UpdateCheckRepositoryOwner</c>
    /// anahtarından okunuyor.</item>
    /// <item><see cref="WebClient.DownloadString"/> form kurucusunda <b>senkron</b>
    /// çağrılıyordu; internet yoksa arayüz ~100 saniye donuyordu. Artık denetim arka
    /// planda, 5 saniyelik zaman aşımıyla yapılır ve pencere açılışını bloklamaz.</item>
    /// <item>Yerel sürüm elle yazılmış bir sabit yerine derleme bilgisinden
    /// (<see cref="AssemblyInformationalVersion"/> / <see cref="AssemblyVersion"/>) okunur.</item>
    /// <item>Sürüm karşılaştırması "metin eşit mi?" yerine sayısal olarak yapılır; böylece
    /// <c>1.0.10</c> ile <c>1.0.9</c> doğru sıralanır.</item>
    /// </list>
    /// </remarks>
    public static class VersionChecker
    {
        private const string OWNER_SETTING_KEY = "UpdateCheckRepositoryOwner";
        private const string REPO_SETTING_KEY = "UpdateCheckRepositoryName";
        private const int DOWNLOAD_TIMEOUT_MILLISECONDS = 5000;

        private static readonly string defaultRepositoryOwner = "0xinspectorx0";
        private static readonly string defaultRepositoryName =
            "Metin2-FishBot-AutoFish-Balikbotu-otomatik-balik-botu-metin2-level-botu-metin2-enerji-botu";

        private static bool isCheckInProgress;

        /// <summary>
        /// Derleme bilgilerinden okunan yerel sürüm.
        /// </summary>
        /// <remarks>
        /// Tek sürüm kaynağı <c>Properties/AssemblyInfo.cs</c> içindeki
        /// <c>AssemblyFileVersion</c> değeridir; kökteki <c>version.txt</c> (GitHub'daki
        /// uzak sürüm) da aynı değere yazılmalıdır. Eskiden sürüm dört farklı yerde
        /// (version.txt=1.0.2, AssemblyInfo=1.0.0.0, csproj ApplicationRevision=5,
        /// README=v2.3) ve birbirinden farklı duruyordu; bu yüzden güncelleme denetimi
        /// sürekli yanlış alarm üretiyordu.
        /// </remarks>
        public static string CurrentVersion
        {
            get
            {
                Assembly assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();

                object[] fileVersions = assembly.GetCustomAttributes(typeof(AssemblyFileVersionAttribute), false);
                if (fileVersions.Length > 0)
                {
                    string value = ((AssemblyFileVersionAttribute)fileVersions[0]).Version;
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value.Trim();
                    }
                }

                object[] informational = assembly.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false);
                if (informational.Length > 0)
                {
                    string value = ((AssemblyInformationalVersionAttribute)informational[0]).InformationalVersion;
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value.Trim();
                    }
                }

                Version version = assembly.GetName().Version;
                return version != null ? version.ToString() : "0.0.0";
            }
        }

        /// <summary>Depo sahibi (GitHub kullanıcı/organizasyon adı).</summary>
        public static string RepositoryOwner
        {
            get { return ReadSetting(OWNER_SETTING_KEY, defaultRepositoryOwner); }
        }

        /// <summary>Depo adı.</summary>
        public static string RepositoryName
        {
            get { return ReadSetting(REPO_SETTING_KEY, defaultRepositoryName); }
        }

        private static string VersionFileUrl
        {
            get
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "https://raw.githubusercontent.com/{0}/{1}/main/version.txt",
                    RepositoryOwner, RepositoryName);
            }
        }

        private static string UpdatePageUrl
        {
            get
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "https://github.com/{0}/{1}", RepositoryOwner, RepositoryName);
            }
        }

        /// <summary>
        /// Güncelleme denetimini <b>arka planda</b> başlatır. Arayüzü bloklamaz; yeni
        /// sürüm varsa kullanıcıya soru penceresi gösterilir.
        /// </summary>
        public static void CheckForUpdate()
        {
            if (isCheckInProgress)
            {
                return;
            }
            isCheckInProgress = true;

            // Güvenlik: eski TLS sürümleriyle raw.githubusercontent.com bağlantısı kurulamıyor.
            try
            {
                ServicePointManager.SecurityProtocol |=
                    SecurityProtocolType.Tls12 | (SecurityProtocolType)3072 /* Tls13 */;
            }
            catch
            {
                // platform desteklemiyorsa yoksay
            }

            SynchronizationContext uiContext = SynchronizationContext.Current;
            string localVersion = CurrentVersion;

            Task.Run(() =>
            {
                string latestVersion = null;
                try
                {
                    latestVersion = DownloadLatestVersion();
                }
                catch (Exception ex)
                {
                    FileLogger.Warning("Sürüm denetimi yapılamadı (internet yok veya depo adı farklı): " + ex.Message);
                }
                finally
                {
                    isCheckInProgress = false;
                }

                if (string.IsNullOrWhiteSpace(latestVersion))
                {
                    return;
                }

                latestVersion = latestVersion.Trim();
                FileLogger.Info("Sürüm denetimi: yerel = " + localVersion + ", uzak = " + latestVersion);

                if (!IsNewerVersion(latestVersion, localVersion))
                {
                    return;
                }

                Action showPrompt = () => PromptUserForUpdate(latestVersion);
                if (uiContext != null)
                {
                    try
                    {
                        uiContext.Post(state => showPrompt(), null);
                        return;
                    }
                    catch (Exception ex)
                    {
                        FileLogger.Warning("Sürüm penceresi arayüz iş parçacığına gönderilemedi: " + ex.Message);
                    }
                }
                showPrompt();
            });
        }

        private static string DownloadLatestVersion()
        {
            // NOT: TaskCompletionSource IDisposable DEGİLDİR; eski taslakta
            // `using (TaskCompletionSource<string> ...)` yazılmıştı ve bu CS1674
            // derleme hatası veriyordu. Yalnızca WebClient using içinde.
            using (WebClient client = new WebClient())
            {
                TaskCompletionSource<string> completion = new TaskCompletionSource<string>();
                client.Headers[HttpRequestHeader.UserAgent] = "Metin2AutoFishCSharp/" + CurrentVersion;

                DownloadStringCompletedEventHandler handler = null;
                handler = (sender, e) =>
                {
                    client.DownloadStringCompleted -= handler;
                    if (e.Error != null)
                    {
                        completion.TrySetException(e.Error);
                    }
                    else if (e.Cancelled)
                    {
                        completion.TrySetResult(null);
                    }
                    else
                    {
                        completion.TrySetResult(e.Result);
                    }
                };
                client.DownloadStringCompleted += handler;

                try
                {
                    client.DownloadStringAsync(new Uri(VersionFileUrl));

                    Task delayTask = Task.Delay(DOWNLOAD_TIMEOUT_MILLISECONDS);
                    int completedIndex = Task.WaitAny(completion.Task, delayTask);
                    if (completedIndex == 1)
                    {
                        client.CancelAsync();
                        throw new TimeoutException("Sürüm dosyası " + DOWNLOAD_TIMEOUT_MILLISECONDS + " ms içinde indirilemedi");
                    }

                    return completion.Task.Result;
                }
                finally
                {
                    client.DownloadStringCompleted -= handler;
                }
            }
        }

        private static void PromptUserForUpdate(string latestVersion)
        {
            try
            {
                DialogResult result = MessageBox.Show(
                    "Yeni sürüm mevcut: " + latestVersion + "\nŞu anki sürüm: " + CurrentVersion +
                    "\n\nGüncelleme sayfasına gitmek ister misiniz?",
                    "Güncelleme Mevcut",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);

                if (result == DialogResult.Yes)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = UpdatePageUrl,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                FileLogger.Error("Güncelleme penceresi gösterilemedi", ex);
            }
        }

        /// <summary>
        /// <paramref name="candidate"/> sürümü <paramref name="current"/> sürümünden büyük mü?
        /// Sayısal karşılaştırma yapılır; çözümlenemeyen bölümler 0 sayılır.
        /// </summary>
        public static bool IsNewerVersion(string candidate, string current)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }
            if (string.IsNullOrWhiteSpace(current))
            {
                return true;
            }

            string[] candidateParts = NormalizeVersion(candidate).Split('.');
            string[] currentParts = NormalizeVersion(current).Split('.');

            int length = Math.Max(candidateParts.Length, currentParts.Length);
            for (int i = 0; i < length; i++)
            {
                int candidateValue = ParsePart(candidateParts, i);
                int currentValue = ParsePart(currentParts, i);

                if (candidateValue > currentValue)
                {
                    return true;
                }
                if (candidateValue < currentValue)
                {
                    return false;
                }
            }
            return false;
        }

        /// <summary>"v2.3-beta" gibi değerleri "2.3" biçimine indirger.</summary>
        private static string NormalizeVersion(string value)
        {
            string result = value.Trim();
            if (result.StartsWith("v", StringComparison.OrdinalIgnoreCase) ||
                result.StartsWith("V", StringComparison.Ordinal))
            {
                result = result.Substring(1);
            }

            int cutIndex = result.IndexOfAny(new char[] { '-', '+', ' ' });
            if (cutIndex > 0)
            {
                result = result.Substring(0, cutIndex);
            }
            return result;
        }

        private static int ParsePart(string[] parts, int index)
        {
            if (parts == null || index >= parts.Length)
            {
                return 0;
            }
            int value;
            return int.TryParse(parts[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : 0;
        }

        private static string ReadSetting(string key, string fallback)
        {
            try
            {
                string value = ConfigurationManager.AppSettings[key];
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }
            catch (Exception ex)
            {
                FileLogger.Warning("App.config '" + key + "' okunamadı: " + ex.Message);
            }
            return fallback;
        }
    }
}
