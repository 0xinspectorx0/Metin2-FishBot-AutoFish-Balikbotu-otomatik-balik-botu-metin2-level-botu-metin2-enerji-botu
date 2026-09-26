using MusicPlayerApp.Debugs;
using System;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;

namespace MusicPlayerApp
{
    internal static class Program
    {
        /// <summary>Uygulamanın giriş noktası.</summary>
        /// <remarks>
        /// <para><b>Bakım turunda eklenenler:</b></para>
        /// <list type="bullet">
        /// <item>Yakalanmayan tüm hatalar (arayüz iş parçacığı + arka plan iş parçacıkları)
        /// artık <c>Logs/bot-yyyyMMdd.log</c> dosyasına yazılıyor ve kullanıcıya Türkçe bir
        /// açıklama gösteriliyor. Eskiden program sessizce kapanıyor, hatanın nedeni
        /// hiçbir yerde bulunamıyordu.</item>
        /// <item>Başlangıç bilgisi (sürüm, çalışma klasörü, kültür) loglanıyor; destek
        /// istendiğinde ortam bilgisi hazır oluyor.</item>
        /// </list>
        /// </remarks>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Arayüz iş parçacığındaki yakalanmayan hatalar (WinForms).
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += OnUiThreadException;

            // Arka plan iş parçacıklarındaki yakalanmayan hatalar.
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

            LogStartupInfo();

            try
            {
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                FileLogger.Error("Uygulama beklenmeyen bir hata ile kapandı", ex);
                ShowFatalError(ex);
            }
            finally
            {
                FileLogger.Info("Uygulama kapatıldı");
            }
        }

        private static void LogStartupInfo()
        {
            try
            {
                FileLogger.Info("==================== UYGULAMA BAŞLADI ====================");
                FileLogger.Info("Sürüm        : " + Sources.VersionChecker.CurrentVersion);
                FileLogger.Info("Çalışma klas.: " + Sources.FileHandler.BaseDirectory);
                FileLogger.Info("Log klasörü  : " + FileLogger.LogDirectory);
                FileLogger.Info("Kültür       : " + CultureInfo.CurrentCulture.Name);
                FileLogger.Info("64-bit süreç : " + (IntPtr.Size == 8));
                FileLogger.Info("Ekran        : " + Screen.PrimaryScreen.Bounds.Width + "x" +
                    Screen.PrimaryScreen.Bounds.Height);
            }
            catch (Exception ex)
            {
                FileLogger.Warning("Başlangıç bilgisi loglanamadı: " + ex.Message);
            }
        }

        private static void OnUiThreadException(object sender, ThreadExceptionEventArgs e)
        {
            FileLogger.Error("Arayüz iş parçacığında yakalanmayan hata", e.Exception);
            ShowFatalError(e.Exception);
        }

        private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = e.ExceptionObject as Exception;
            if (ex == null)
            {
                FileLogger.Error("Yakalanmayan hata (tür: " +
                    (e.ExceptionObject == null ? "bilinmiyor" : e.ExceptionObject.GetType().FullName) + ")");
                return;
            }

            FileLogger.Error("Arka plan iş parçacığında yakalanmayan hata (sonlanıyor: " +
                e.IsTerminating + ")", ex);

            if (e.IsTerminating)
            {
                ShowFatalError(ex);
            }
        }

        /// <summary>
        /// Kullanıcıya Türkçe, anlaşılır bir hata penceresi gösterir. Pencerenin kendisi
        /// hata verirse (ör. arayüz çökmüşse) sessizce yutulur.
        /// </summary>
        private static void ShowFatalError(Exception ex)
        {
            try
            {
                MessageBox.Show(
                    "Beklenmeyen bir hata oluştu." + Environment.NewLine + Environment.NewLine +
                    "Hata: " + ex.Message + Environment.NewLine + Environment.NewLine +
                    "Ayrıntılar şu klasördeki günlük dosyasına kaydedildi:" + Environment.NewLine +
                    FileLogger.LogDirectory + Environment.NewLine + Environment.NewLine +
                    "Sorunu bildirirken bu dosyayı paylaşmanız çözüm süresini kısaltır.",
                    "Hata",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            catch
            {
                // Hata penceresi gösterilemiyorsa yapacak bir şey yok; log dosyası yeterli.
            }
        }
    }
}
