using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace MusicPlayerApp.Debugs
{
    /// <summary>
    /// Proje <c>WinExe</c> olarak derlendigi icin <see cref="Console"/> ciktisi
    /// kullaniciya HICBIR sekilde ulasmaz. Bu sinif, botun urettigi tum tanilama
    /// mesajlarini <c>exeDizini\Logs\bot-yyyyAAgg.log</c> dosyasina yazar.
    ///
    /// Ozellikler:
    ///  - Thread-safe (uc bot thread'i + UI thread'i ayni anda yazar)
    ///  - Gunluk dosya dondurme, otomatik klasor olusturma
    ///  - Yazma hatasi uygulamayi ASLA durdurmaz (sessizce yutar)
    ///  - 60 gun'den eski log dosyalarini temizler
    /// </summary>
    internal static class FileLogger
    {
        public enum Level
        {
            DEBUG,
            INFO,
            WARNING,
            ERROR
        }

        private const string LOG_FOLDER_NAME = "Logs";
        private const string LOG_FILE_PREFIX = "bot-";
        private const int KEEP_LOG_DAY_COUNT = 60;

        private static readonly object writeLock = new object();
        private static readonly CultureInfo logCulture = CultureInfo.InvariantCulture;

        private static string logDirectory;
        private static bool isDirectoryReady;
        private static DateTime lastCleanUpDate = DateTime.MinValue;

        /// <summary>
        /// Log klasorunun tam yolu. Hata durumunda <see cref="string.Empty"/> doner.
        /// </summary>
        public static string LogDirectory
        {
            get
            {
                EnsureDirectory();
                return logDirectory ?? string.Empty;
            }
        }

        /// <summary>Log dosyasina acilma/kapanma bilgisi yazmadan sadece mesaj birakir.</summary>
        public static void Write(Level level, string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            string line = string.Format(
                logCulture,
                "{0:yyyy-MM-dd HH:mm:ss.fff} [{1,-7}] [T{2}] {3}",
                DateTime.Now,
                level.ToString(),
                CurrentThreadId,
                Flatten(message));

            try
            {
                lock (writeLock)
                {
                    string path = EnsureDirectory();
                    if (path == null)
                    {
                        return;
                    }
                    File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
                    CleanUpOldLogsIfNeeded();
                }
            }
            catch
            {
                // Loglama yuzunden bot asla cokmemeli.
            }

            // Konsol baglanmissa (ornegin AttachConsole ile debug edilirken) ayni anda oraya da yaz.
            try
            {
                Console.WriteLine(line);
            }
            catch
            {
                // yoksay
            }
        }

        public static void Debug(string message)
        {
            Write(Level.DEBUG, message);
        }

        public static void Info(string message)
        {
            Write(Level.INFO, message);
        }

        public static void Warning(string message)
        {
            Write(Level.WARNING, message);
        }

        public static void Error(string message)
        {
            Write(Level.ERROR, message);
        }

        /// <summary>
        /// Bir exception'i ic exception'lariyla birlikte okunabilir tek metne cevirir.
        /// </summary>
        public static string Describe(Exception exception)
        {
            if (exception == null)
            {
                return "(null exception)";
            }

            StringBuilder builder = new StringBuilder();
            Exception current = exception;
            int depth = 0;
            while (current != null && depth < 6)
            {
                if (depth > 0)
                {
                    builder.Append(" <-- ");
                }
                builder.Append(current.GetType().Name).Append(": ").Append(current.Message);
                current = current.InnerException;
                depth++;
            }

            if (exception.StackTrace != null)
            {
                builder.Append(" | Stack: ").Append(Flatten(exception.StackTrace));
            }
            return builder.ToString();
        }

        public static void Error(string message, Exception exception)
        {
            Write(Level.ERROR, message + " | " + Describe(exception));
        }

        /// <summary>
        /// Verilen is parçaciginda beklenmeyen bir hata oldugunda cagrilir; hem loglar
        /// hem de thread'i sessizce olmesine izin vermek yerine iz birakir.
        /// </summary>
        public static void ErrorForThread(string threadName, Exception exception)
        {
            Write(Level.ERROR, "!!! '" + threadName + "' thread'i beklenmeyen hata nedeniyle DURDU. " + Describe(exception));
        }

        private static int CurrentThreadId
        {
            get
            {
                try
                {
                    return Thread.CurrentThread.ManagedThreadId;
                }
                catch
                {
                    return -1;
                }
            }
        }

        private static string Flatten(string value)
        {
            if (value == null)
            {
                return string.Empty;
            }
            return value.Replace("\r\n", " | ").Replace('\r', ' ').Replace('\n', ' ');
        }

        /// <summary>
        /// Log klasorunu hazirlar ve o gunun log dosyasinin tam yolunu doner.
        /// Basarisiz olursa <c>null</c> doner (cagiran taraf sessizce gecmeli).
        /// </summary>
        private static string EnsureDirectory()
        {
            try
            {
                if (!isDirectoryReady)
                {
                    string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                    logDirectory = Path.Combine(baseDirectory, LOG_FOLDER_NAME);
                    if (!Directory.Exists(logDirectory))
                    {
                        Directory.CreateDirectory(logDirectory);
                    }
                    isDirectoryReady = true;
                }

                return Path.Combine(
                    logDirectory,
                    LOG_FILE_PREFIX + DateTime.Now.ToString("yyyyMMdd", logCulture) + ".log");
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Gunde en fazla bir kez calisir; eski log dosyalarini siler.
        /// writeLock icinde cagrilmalidir.
        /// </summary>
        private static void CleanUpOldLogsIfNeeded()
        {
            try
            {
                if ((DateTime.Now - lastCleanUpDate).TotalHours < 24)
                {
                    return;
                }
                lastCleanUpDate = DateTime.Now;

                if (logDirectory == null || !Directory.Exists(logDirectory))
                {
                    return;
                }

                DateTime threshold = DateTime.Now.AddDays(-KEEP_LOG_DAY_COUNT);
                foreach (string file in Directory.GetFiles(logDirectory, LOG_FILE_PREFIX + "*.log"))
                {
                    try
                    {
                        if (File.GetLastWriteTime(file) < threshold)
                        {
                            File.Delete(file);
                        }
                    }
                    catch
                    {
                        // tek bir dosya silinemese bile sorun degil
                    }
                }
            }
            catch
            {
                // yoksay
            }
        }
    }
}
