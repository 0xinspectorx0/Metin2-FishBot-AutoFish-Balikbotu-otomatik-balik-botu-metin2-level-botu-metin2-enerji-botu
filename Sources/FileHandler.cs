using MusicPlayerApp.Debugs;
using System;
using System.Drawing;
using System.IO;
using System.Reflection;

namespace MusicPlayerApp.Sources
{
    /// <summary>
    /// Programin kullandigi klasor yapisini (Images, Fishes, ChatResources ...)
    /// ve dosya okuma/yazma islemlerini yonetir.
    /// </summary>
    public enum PathWayStruct
    {
        PATH_STANDART,
        PATH_DESTKOP,
        PATH_IMAGE,
        PATH_FISHES,
        PATH_CHAT_ALPHABETS,
        PATH_TESTIMAGES,
        PATH_CHAT_Q_A,
        PATH_SCREENSHOTS
    }

    public enum ReturOrFind
    {
        RETURN_PATH,
        FIND_PATH,
    }

    internal class FileHandler
    {
        /// <summary>
        /// Program kurulumunda (zip'ten cikarildiginda) exe'nin bulundugu klasor.
        /// Eski surum bu yolu "exe yolunun son iki segmentini atarak" hesapliyordu
        /// (<c>bin\Debug\..\..\</c>); exe tasinirsa veya ClickOnce ile kurulursa
        /// butun kaynaklar kayboluyordu. Artik dogrudan exe'nin kendi klasoru
        /// kullanilir ve csproj <c>CopyToOutputDirectory</c> ile kaynaklari oraya kopyalar.
        /// </summary>
        public static string BaseDirectory
        {
            get
            {
                try
                {
                    string location = Assembly.GetEntryAssembly() != null
                        ? Assembly.GetEntryAssembly().Location
                        : Assembly.GetExecutingAssembly().Location;

                    if (!string.IsNullOrEmpty(location))
                    {
                        return Path.GetDirectoryName(location);
                    }
                }
                catch (Exception ex)
                {
                    FileLogger.Error("BaseDirectory (Assembly.Location) okunamadi", ex);
                }

                // Yedek: AppDomain.BaseDirectory her zaman doludur.
                return AppDomain.CurrentDomain.BaseDirectory;
            }
        }

        /// <summary>
        /// Bitmap'i PNG olarak kaydeder. Bitmap null ise veya bos ise HICBIR sey yapmaz.
        /// </summary>
        /// <remarks>
        /// Eski kodda kosul <c>if (bitmap == null || bitmap.Width > 0)</c> seklindeydi;
        /// yani bitmap null oldugunda iceri girip <c>bitmap.Save(...)</c> cagiriyor ve
        /// <see cref="NullReferenceException"/> uretiyordu. Duzeltildi.
        /// </remarks>
        public static void SaveImageAsPng(Bitmap bitmap, string fileName, PathWayStruct pathWay)
        {
            if (bitmap == null || bitmap.Width <= 0 || bitmap.Height <= 0)
            {
                FileLogger.Warning("SaveImageAsPng: kaydedilecek bitmap null veya bos (" + fileName + ")");
                return;
            }
            if (string.IsNullOrWhiteSpace(fileName))
            {
                FileLogger.Warning("SaveImageAsPng: dosya adi bos, kayit atlandi");
                return;
            }

            try
            {
                string path = ReturnOrFindPathWay(fileName, pathWay, ReturOrFind.RETURN_PATH);
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                FileLogger.Debug("Goruntu kaydedildi: " + path);
            }
            catch (Exception ex)
            {
                FileLogger.Error("SaveImageAsPng basarisiz (" + fileName + ")", ex);
            }
        }

        /// <summary>
        /// Belirtilen ekran bolgesini yakalar, PNG olarak kaydeder ve aradaki bitmap'i
        /// hemen serbest birakir.
        /// </summary>
        /// <remarks>
        /// Eski kod <c>SaveImageAsPng(screenshot.CaptureSpecifiedScreen(rect), ...)</c>
        /// seklinde cagrilip ara bitmap'i hic dispose etmiyordu; her kayitta bir GDI
        /// nesnesi siziyordu.
        /// </remarks>
        public static void CaptureAndSavePng(ScreenShotWinAPI screenshot, Rectangle area, string fileName, PathWayStruct pathWay)
        {
            if (screenshot == null)
            {
                FileLogger.Warning("CaptureAndSavePng: screenshot nesnesi null");
                return;
            }
            using (Bitmap captured = screenshot.CaptureSpecifiedScreen(area))
            {
                SaveImageAsPng(captured, fileName, pathWay);
            }
        }

        /// <summary>
        /// Exe klasorunun altindaki klasorleri birlestirerek tam yol uretir.
        /// </summary>
        /// <param name="fileName">Dosya adi</param>
        /// <param name="folderNames">Exe klasorune gore alt klasorler</param>
        public static string PathWantedWayFromBase(string fileName, string[] folderNames)
        {
            string path = BaseDirectory;

            if (folderNames != null)
            {
                for (int k = 0; k < folderNames.Length; k++)
                {
                    if (string.IsNullOrEmpty(folderNames[k]))
                    {
                        continue;
                    }
                    path = Path.Combine(path, folderNames[k]);
                }
            }

            if (!string.IsNullOrEmpty(fileName))
            {
                path = Path.Combine(path, fileName);
            }

            return path;
        }

        /// <summary>
        /// Verilen <see cref="PathWayStruct"/> degerine karsilik gelen tam yolu doner.
        /// <see cref="ReturOrFind.FIND_PATH"/> secilirse dosyanin varligi da dogrulanir.
        /// </summary>
        public static string ReturnOrFindPathWay(string fileName, PathWayStruct folderWay, ReturOrFind state)
        {
            string path;

            switch (folderWay)
            {
                case PathWayStruct.PATH_STANDART:
                    path = fileName;
                    break;

                case PathWayStruct.PATH_DESTKOP:
                    path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), fileName);
                    break;

                case PathWayStruct.PATH_IMAGE:
                    path = PathWantedWayFromBase(fileName, new string[] { "Images" });
                    break;

                case PathWayStruct.PATH_CHAT_ALPHABETS:
                    path = PathWantedWayFromBase(fileName, new string[] { "ChatResources", "GameAlphabets" });
                    break;

                case PathWayStruct.PATH_TESTIMAGES:
                    path = PathWantedWayFromBase(fileName, new string[] { "TestImages" });
                    break;

                case PathWayStruct.PATH_CHAT_Q_A:
                    path = PathWantedWayFromBase(fileName, new string[] { "ChatResources", "ChatQuestionAnswer" });
                    break;

                case PathWayStruct.PATH_SCREENSHOTS:
                    path = PathWantedWayFromBase(fileName, new string[] { "ScreenShot" });
                    break;

                case PathWayStruct.PATH_FISHES:
                default:
                    path = PathWantedWayFromBase(fileName, new string[] { "Fishes" });
                    break;
            }

            if (state == ReturOrFind.FIND_PATH)
            {
                if (File.Exists(path))
                {
                    return path;
                }

                // Gelistrime ortaminda (bin\Debug yerine proje kokunden calistirma) icin
                // bir ust-ust klasore de bakilir; yine bulunamazsa anlaşılır bir hata verilir.
                string legacyPath = Path.GetFullPath(Path.Combine(BaseDirectory, "..", "..", path));
                if (File.Exists(legacyPath))
                {
                    return legacyPath;
                }

                throw new FileNotFoundException(
                    "Program kaynak dosyasi bulunamadi: '" + Path.GetFileName(path) + "'.\n" +
                    "Aranan yer: " + path + "\n" +
                    "Cozum: Proje klasorundeki Images, Fishes ve ChatResources klasorlerinin " +
                    "exe'nin yaninda oldugundan emin olun (Visual Studio'da 'Derle' demek " +
                    "bu klasorleri otomatik kopyalar).",
                    path);
            }

            return path;
        }

        /// <summary>
        /// Exe klasorunun altinda verilen isimde klasor arar; yoksa OLUSTURUR.
        /// </summary>
        /// <remarks>
        /// Eski surum <c>Environment.CurrentDirectory</c>'yi bolup son iki segmenti
        /// atiyor ve ardindan <c>SearchOption.AllDirectories</c> ile TUM alt agaci
        /// tarayarak klasor ariyordu. Calisma dizini degistiginde (kisayol, "farkli
        /// calistir", gorev zamanlayici) hem yavasliyor hem de
        /// <see cref="FileNotFoundException"/> ile tip baslatma hatasi veriyordu.
        /// </remarks>
        /// <param name="folderName">Aranan/olusturulacak klasor adi</param>
        /// <returns>Klasorun tam yolu</returns>
        public static string FindFolderNameFromBase(string folderName)
        {
            if (string.IsNullOrWhiteSpace(folderName))
            {
                throw new ArgumentException("folderName bos olamaz", "folderName");
            }

            try
            {
                string path = Path.Combine(BaseDirectory, folderName);
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                    FileLogger.Info("Klasor olusturuldu: " + path);
                }
                return path;
            }
            catch (Exception ex)
            {
                FileLogger.Error("FindFolderNameFromBase basarisiz (" + folderName + ")", ex);
                throw;
            }
        }

        /// <summary>
        /// PNG dosyasini okuyup <see cref="Bitmap"/> doner.
        /// </summary>
        /// <remarks>
        /// Eski surum <c>Image.FromFile</c> kullaniyordu; bu metot dosyayi bitmap
        /// dispose edilene kadar KILITLI tutar. Artik dosya baytlari bellekten
        /// okunuyor, boylece PNG'ler kilitlenmiyor ve hata mesaji yolu da iceriyor.
        /// Cagiran taraf donen bitmap'i dispose etmelidir.
        /// </remarks>
        public static Bitmap ReadPngFileGetBitmap(string fileName, PathWayStruct pathWay)
        {
            string filePath = ReturnOrFindPathWay(fileName, pathWay, ReturOrFind.FIND_PATH);

            if (!filePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Dosya PNG formatinda degil: " + filePath, "fileName");
            }

            try
            {
                byte[] bytes = File.ReadAllBytes(filePath);
                using (MemoryStream stream = new MemoryStream(bytes))
                {
                    // FromStream akis kapatildiktan sonra da calisabilir; yine de
                    // Bitmap'i kopyalayarak akistan tamamen bagimsiz hale getiriyoruz.
                    using (Bitmap temporary = (Bitmap)Image.FromStream(stream))
                    {
                        return new Bitmap(temporary);
                    }
                }
            }
            catch (FileNotFoundException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new IOException("PNG dosyasi okunamadi: " + filePath + " (" + ex.Message + ")", ex);
            }
        }
    }
}
