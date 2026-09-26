using MusicPlayerApp.Debugs;
using MusicPlayerApp.Sources;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Metin2AutoFishCSharp.Sources.ChatHandler
{
    /// <summary>
    /// Chat soru/cevap dosyalarini ve kayit dosyalarini yonetir.
    /// </summary>
    /// <remarks>
    /// Dosya bicimi (<c>ChatQuestAnswer.txt</c>):
    /// <code>
    /// &amp;                  -> tespit edilecek kelimelerin baslangici
    /// bot musun
    /// botcu
    /// ^                  -> bu bloğa verilecek cevaplarin baslangici
    /// ben bot değilim
    /// </code>
    ///
    /// BAKIM TURUNDA YAPILAN DEGISIKLIKLER:
    ///  1) Klasor yolu artik <c>Environment.CurrentDirectory</c> + "tum diski tara"
    ///     mantigiyla degil, dogrudan exe klasoru altindaki
    ///     <c>ChatResources\ChatQuestionAnswer</c> olarak cozulur. Eski yontem calisma
    ///     dizini degistiginde <see cref="FileNotFoundException"/> uretip tip
    ///     baslatmasini (static constructor) tamamen patlatiyordu.
    ///  2) Dosya yoksa silinip hata verilmiyor; bos olarak olusturuluyor.
    ///  3) <c>do { reader.ReadLine() } while (reader.Peek() != -1)</c> deseni bos
    ///     dosyada <see cref="NullReferenceException"/> uretiyordu; null-safe okumaya
    ///     cevrildi ve son satirin atlanmasi sorunu giderildi.
    ///  4) Kayit dosyalari "tumunu oku + tumunu yeniden yaz" yerine ekleyerek
    ///     (append) yazilir; dosya buyudukce performans dusmuyordu.
    ///  5) Yol birlestirmede "\\" yerine <see cref="Path.Combine"/> kullanilir.
    /// </remarks>
    internal class ChatFileHandler : ChatSentencer
    {
        private const string CHAT_QUESTION_ANSWER_FOLDER = "ChatQuestionAnswer";
        private const string CHAT_RESOURCES_FOLDER = "ChatResources";

        private const string CHAT_QUEST_ANSWER_FILE = "ChatQuestAnswer.txt";
        private const string RECORD_CHATS_FILE = "RecordChats.txt";
        private const string RECORD_WHISPER_CHAT_FILE = "RecordWhisperChat.txt";

        private const string DetectWordMark = "&";
        private const string AnswerWordMark = "^";

        /// <summary>Son eslesen tespit kelimesi (Telegram mesajlarinda kullanilir).</summary>
        public static string DetectedWord = "";

        private static readonly Encoding fileEncoding = new UTF8Encoding(false);

        /// <summary>
        /// Chat dosyalarinin bulundugu klasor. Klasor/dosyalar yoksa olusturulur.
        /// </summary>
        private static readonly string ChatQuestAnswerPath = EnsureChatFolder();

        public ChatFileHandler()
        {
        }

        /// <summary>ChatQuestAnswer.txt dosyasinin tam yolu.</summary>
        public static string ChatQuestAnswerFilePath
        {
            get { return Path.Combine(ChatQuestAnswerPath, CHAT_QUEST_ANSWER_FILE); }
        }

        /// <summary>
        /// Chat klasorunu ve icindeki uc dosyayi hazirlar. Basarisiz olursa exe
        /// klasorune dusulur (bot yine de calismaya devam eder).
        /// </summary>
        private static string EnsureChatFolder()
        {
            try
            {
                string folder = Path.Combine(FileHandler.BaseDirectory, CHAT_RESOURCES_FOLDER, CHAT_QUESTION_ANSWER_FOLDER);
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                    FileLogger.Info("Chat klasoru olusturuldu: " + folder);
                }

                EnsureFileExists(Path.Combine(folder, CHAT_QUEST_ANSWER_FILE));
                EnsureFileExists(Path.Combine(folder, RECORD_CHATS_FILE));
                EnsureFileExists(Path.Combine(folder, RECORD_WHISPER_CHAT_FILE));

                return folder;
            }
            catch (Exception ex)
            {
                FileLogger.Error("Chat klasoru hazirlanamadi, exe klasoru kullanilacak", ex);
                return FileHandler.BaseDirectory;
            }
        }

        private static void EnsureFileExists(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    File.WriteAllText(filePath, string.Empty, fileEncoding);
                    FileLogger.Info("Bos chat dosyasi olusturuldu: " + filePath);
                }
            }
            catch (Exception ex)
            {
                FileLogger.Error("Chat dosyasi olusturulamadi: " + filePath, ex);
            }
        }

        /// <summary>
        /// Bir metin dosyasinin tum satirlarini null-safe sekilde okur.
        /// </summary>
        private static List<string> ReadAllLinesSafe(string filePath)
        {
            List<string> lines = new List<string>();
            try
            {
                if (!File.Exists(filePath))
                {
                    EnsureFileExists(filePath);
                    return lines;
                }

                foreach (string line in File.ReadAllLines(filePath, fileEncoding))
                {
                    lines.Add(line);
                }
            }
            catch (Exception ex)
            {
                FileLogger.Error("Dosya okunamadi: " + filePath, ex);
            }
            return lines;
        }

        /// <summary>
        /// Satirlari dosyaya yazar (uzerine). Bos satirlar atlanir.
        /// </summary>
        private static void WriteAllLinesSafe(string filePath, IEnumerable<string> lines)
        {
            try
            {
                using (StreamWriter writer = new StreamWriter(filePath, false, fileEncoding))
                {
                    foreach (string line in lines)
                    {
                        if (line == null)
                        {
                            continue;
                        }
                        writer.WriteLine(line);
                    }
                }
            }
            catch (Exception ex)
            {
                FileLogger.Error("Dosyaya yazilamadi: " + filePath, ex);
            }
        }

        /// <summary>
        /// Satirlari dosyanin sonuna ekler.
        /// </summary>
        private static void AppendLinesSafe(string filePath, IEnumerable<string> lines)
        {
            try
            {
                EnsureFileExists(filePath);
                using (StreamWriter writer = new StreamWriter(filePath, true, fileEncoding))
                {
                    foreach (string line in lines)
                    {
                        if (line == null)
                        {
                            continue;
                        }
                        writer.WriteLine(line);
                    }
                }
            }
            catch (Exception ex)
            {
                FileLogger.Error("Dosyaya ekleme yapilamadi: " + filePath, ex);
            }
        }

        /// <summary>
        /// Kullanici arayuzunden girilen "tespit edilecek kelimeler" ve "verilecek
        /// cevaplar" bloğunu dosyanin sonuna ekler.
        /// </summary>
        public void ChatFileWriter(string[] detectWantedWord, string[] answerDetectedWord)
        {
            if (detectWantedWord == null || detectWantedWord.Length == 0 ||
                answerDetectedWord == null || answerDetectedWord.Length == 0)
            {
                FileLogger.Warning("ChatFileWriter: bos kelime/cevap listesi, kayit yapilmadi");
                return;
            }

            List<string> newBlock = new List<string>();
            newBlock.Add(DetectWordMark);
            foreach (string word in detectWantedWord)
            {
                if (!string.IsNullOrWhiteSpace(word))
                {
                    newBlock.Add(word.Trim());
                }
            }
            newBlock.Add(AnswerWordMark);
            foreach (string answer in answerDetectedWord)
            {
                if (!string.IsNullOrWhiteSpace(answer))
                {
                    newBlock.Add(answer.Trim());
                }
            }

            string filePath = ChatQuestAnswerFilePath;

            // Eski davranis korunur: mevcut icerik yeniden yazilir (bos satirlar temizlenir),
            // ardindan yeni blok eklenir.
            List<string> existing = ReadAllLinesSafe(filePath);
            List<string> cleaned = new List<string>();
            foreach (string line in existing)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    cleaned.Add(line);
                }
            }
            cleaned.AddRange(newBlock);

            WriteAllLinesSafe(filePath, cleaned);
            FileLogger.Info("ChatQuestAnswer.txt guncellendi: " + (detectWantedWord.Length) +
                " tespit kelimesi, " + answerDetectedWord.Length + " cevap eklendi");
        }

        /// <summary>
        /// Chat uzerinden gelen bir konusmayi <c>RecordChats.txt</c> dosyasina kaydeder.
        /// </summary>
        public void RecordChatToFile(string detectedWord, string[] anotherPlayerSentence, params string[] answeredSentence)
        {
            if (anotherPlayerSentence == null || answeredSentence == null)
            {
                FileLogger.Warning("RecordChatToFile: oyuncu cumlesi veya cevap null, kayit atlandi");
                return;
            }

            string now = DateTime.Now.ToString(CultureInfo.CurrentCulture);
            List<string> record = new List<string>();
            record.Add("Diğer Oyuncudan Tespit Edilenler = ");
            record.Add("İsmi = " + SafeAt(anotherPlayerSentence, 0));
            record.Add("Cümlesi = " + SafeAt(anotherPlayerSentence, 1) + "     " + now);
            record.Add("Tespit edilen kelime = " + (detectedWord ?? string.Empty));
            record.Add("Verilen Cevap = ");
            for (int i = 0; i < answeredSentence.Length; i++)
            {
                record.Add((answeredSentence[i] ?? string.Empty) + "      " + now);
            }
            record.Add(string.Empty);

            AppendLinesSafe(Path.Combine(ChatQuestAnswerPath, RECORD_CHATS_FILE), record);
        }

        /// <summary>
        /// Fisilti ile gelen bir konusmayi <c>RecordWhisperChat.txt</c> dosyasina kaydeder.
        /// </summary>
        public void RecordWhisperToFile(string detectedWord, string[] anPlayerSentence, params string[] answeredSentence)
        {
            if (anPlayerSentence == null || answeredSentence == null)
            {
                FileLogger.Warning("RecordWhisperToFile: oyuncu cumlesi veya cevap null, kayit atlandi");
                return;
            }

            string now = DateTime.Now.ToString(CultureInfo.CurrentCulture);
            List<string> record = new List<string>();
            record.Add("Fısıltı İle Tespit Edilen = ");
            record.Add("İsmi = " + SafeAt(anPlayerSentence, 0));
            record.Add("Cümlesi = " + SafeAt(anPlayerSentence, 1) + "     " + now);
            record.Add("Fısıltı Tespit edilen kelime = " + (detectedWord ?? string.Empty));
            record.Add("Verilen Cevap = ");
            for (int i = 0; i < answeredSentence.Length; i++)
            {
                record.Add((answeredSentence[i] ?? string.Empty) + "      " + now);
            }
            record.Add(string.Empty);

            AppendLinesSafe(Path.Combine(ChatQuestAnswerPath, RECORD_WHISPER_CHAT_FILE), record);
        }

        /// <summary>
        /// ChatQuestAnswer.txt icerigini kullanici arayuzundeki zengin metin kutusuna
        /// renkli olarak basar.
        /// </summary>
        public static void ReadAllFileForTextBox(RichTextBox textBox)
        {
            if (textBox == null)
            {
                return;
            }

            List<string> lines = ReadAllLinesSafe(ChatQuestAnswerFilePath);
            if (lines.Count == 0)
            {
                textBox.AppendText("Dosya boş. 'Tespit Edilecek Kelimeler' ve 'Verilecek Cevaplar' " +
                    "kısımlarını doldurup 'verileri yükle' düğmesine basınız.");
                return;
            }

            textBox.Clear();
            foreach (string rawLine in lines)
            {
                string line = rawLine ?? string.Empty;

                if (line.Equals(DetectWordMark))
                {
                    AppendColored(textBox, Environment.NewLine, Color.Black);
                    AppendColored(textBox, "Tespit Edilmek İstenen Kelimeler = ", Color.Red);
                    AppendColored(textBox, Environment.NewLine, Color.Black);
                }
                else if (line.Equals(AnswerWordMark))
                {
                    AppendColored(textBox, Environment.NewLine, Color.Black);
                    AppendColored(textBox, "Tespit edilen kelimelere göre cevaplar = ", Color.Blue);
                    AppendColored(textBox, Environment.NewLine, Color.Black);
                }
                else
                {
                    AppendColored(textBox, line + ",", Color.Black);
                }
            }
        }

        /// <summary>
        /// Dosyadaki cevaplardan, gelen cumleyle eslesen bloğa ait rastgele bir cevap doner.
        /// Eslesen cevap yoksa <see cref="string.Empty"/> doner.
        /// </summary>
        public string ChatGetAnswerFromFile(string gameChatWords)
        {
            if (string.IsNullOrEmpty(gameChatWords))
            {
                return string.Empty;
            }

            List<string> lines = ReadAllLinesSafe(ChatQuestAnswerFilePath);
            List<string> answers = new List<string>();

            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i] ?? string.Empty;
                if (!line.Equals(DetectWordMark))
                {
                    continue;
                }

                // '&' bloğundan '^' isaretine kadar olan satirlar tespit kelimeleridir.
                int answerStart = -1;
                for (int k = i + 1; k < lines.Count; k++)
                {
                    string detectLine = lines[k] ?? string.Empty;
                    if (detectLine.Equals(AnswerWordMark))
                    {
                        answerStart = k + 1;
                        break;
                    }
                    if (detectLine.Equals(DetectWordMark))
                    {
                        break;
                    }
                    if (Contains(gameChatWords, detectLine))
                    {
                        DetectedWord = detectLine;
                        FileLogger.Debug("Tespit edilen kelime = " + detectLine);
                    }
                }

                if (answerStart < 0)
                {
                    continue;
                }

                // '^' bloğundan sonraki '&' isaretine kadar olan satirlar cevaplardir.
                for (int k = answerStart; k < lines.Count; k++)
                {
                    string answerLine = lines[k] ?? string.Empty;
                    if (answerLine.Equals(DetectWordMark))
                    {
                        break;
                    }
                    if (!string.IsNullOrWhiteSpace(answerLine))
                    {
                        answers.Add(answerLine);
                    }
                }

                if (answers.Count > 0)
                {
                    break;
                }
            }

            if (answers.Count == 0)
            {
                return string.Empty;
            }

            string result = answers.ElementAt(TimerGame.MakeRandomValue(0, answers.Count));
            FileLogger.Debug("Oyuncuya verilecek cevap = " + result);
            return result;
        }

        private static void AppendColored(RichTextBox textBox, string text, Color color)
        {
            textBox.SelectionStart = textBox.TextLength;
            textBox.SelectionLength = 0;
            textBox.SelectionColor = color;
            textBox.AppendText(text);
            textBox.SelectionColor = textBox.ForeColor;
        }

        private static string SafeAt(string[] values, int index)
        {
            if (values == null || index < 0 || index >= values.Length || values[index] == null)
            {
                return string.Empty;
            }
            return values[index];
        }
    }
}
