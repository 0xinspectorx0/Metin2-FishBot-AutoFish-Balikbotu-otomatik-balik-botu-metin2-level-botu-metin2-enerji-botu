using MusicPlayerApp.Sources;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

namespace MusicPlayerApp.Debugs
{
    internal class DebugPfCnsl
    {
        public static readonly ConsoleColor COLOR_DARK_RED = ConsoleColor.DarkRed;
        public static readonly ConsoleColor COLOR_DARK_GREEN = ConsoleColor.DarkGreen;
        public static readonly ConsoleColor COLOR_DARK_YELLOW = ConsoleColor.DarkYellow;
        public static readonly ConsoleColor COLOR_DARK_BLUE = ConsoleColor.DarkBlue;
        public static readonly ConsoleColor COLOR_DARK_MAGENTA = ConsoleColor.DarkMagenta;
        public static readonly ConsoleColor COLOR_BRIGHT_RED = ConsoleColor.Red;
        public static readonly ConsoleColor COLOR_BRIGHT_GREEN = ConsoleColor.Green;
        public static readonly ConsoleColor COLOR_BRIGHT_BLUE = ConsoleColor.Blue;
        public static readonly ConsoleColor COLOR_BRIGHT_MAGENTA = ConsoleColor.Magenta;

        private static int colorChangerCounter;

        private static ConsoleColor[] arrayColors = new ConsoleColor[]
        {
        COLOR_DARK_RED,COLOR_DARK_GREEN,COLOR_DARK_YELLOW,
        COLOR_DARK_BLUE,COLOR_DARK_MAGENTA,COLOR_BRIGHT_RED,
        COLOR_BRIGHT_GREEN,COLOR_BRIGHT_BLUE,COLOR_BRIGHT_MAGENTA
        };

        private TimerGame timerDebug = new TimerGame();

        public static void println(string message)
        {

            // Console.BackgroundColor = colorPref;
            Console.WriteLine(message + "     " + DateTime.Now);
        }

        /// <summary>
        /// Hata ayıklama için bir bitmap'ın tüm piksellerini ARGB tamsayısı olarak döker.
        /// </summary>
        /// <remarks>
        /// Eski sürümde iki sorun vardı: (1) <c>bitmap.Width</c>, null kontrolünden ÖNCE
        /// okunuyordu; yani <c>bitmap == null</c> iken <see cref="NullReferenceException"/>
        /// fırlıyordu. (2) Her piksel için <c>GetPixel</c> çağrılıyordu; GDI+ üzerinde piksel
        /// başına ayrı kilitleme anlamına gelen bu yöntem büyük görüntülerde çok yavaştır.
        /// Artık <c>LockBits</c> ile tek seferde okunuyor.
        /// </remarks>
        public static void printBitMapArray(Bitmap bitmap)
        {
            if (bitmap == null)
            {
                println("bitmap value is NULL");
                return;
            }

            int width = bitmap.Width;
            int height = bitmap.Height;
            println("bitmap total size pixel = " + bitmap.Size);
            println("bitmap total width = " + width);

            int[] array = new int[width * height];
            BitmapData data = null;
            try
            {
                data = bitmap.LockBits(new Rectangle(0, 0, width, height),
                    ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

                int stride = data.Stride;
                byte[] buffer = new byte[stride * height];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);

                for (int y = 0; y < height; y++)
                {
                    int rowStart = y * stride;
                    for (int x = 0; x < width; x++)
                    {
                        int offset = rowStart + (x * 4);
                        // Bellek düzeni BGRA; ToArgb() ile aynı değer üretiliyor.
                        array[(y * width) + x] =
                            (255 << 24) | (buffer[offset + 2] << 16) | (buffer[offset + 1] << 8) | buffer[offset];
                    }
                }
            }
            finally
            {
                if (data != null)
                {
                    bitmap.UnlockBits(data);
                }
            }

            printIntArrayAsDescended(array);
        }

        public static void printIntArrayAsDescended(int[] array)
        {
            if(array == null || array.Length <= 0)
            { println("printInt fonction array variable = null"); return; }

            println("array lenght of the printIntAsDescended fun = " + array.Length);
            // Dizideki öğeleri gruplara ayırma ve her bir öğenin sayısını hesaplama
            var grouped = array.GroupBy(x => x).Select(g => new { Value = g.Key, Count = g.Count() });

            // Öğeleri sayılarına göre büyükten küçüğe sıralama
            var sorted = grouped.OrderByDescending(g => g.Count);

            // Sıralı öğeleri yeni bir diziye ekleyerek döndürme
            int[] result = sorted.SelectMany(g => Enumerable.Repeat(g.Value, g.Count)).ToArray();

            Console.Write("Descended common values result = ");
            // Sonucu yazdırma
            foreach (int num in result)
            {
                Console.WriteLine($"Decimal: {num}, Hexadecimal: 0x{num:X}");
            }
        }

        public static void printIntArray(int[] argbArray)
        {
            Console.WriteLine("PrintIntArray function result = ");
            foreach (int num in argbArray)
            {
                Console.WriteLine($"Decimal: {num}, Hexadecimal: 0x{num:X}");
                Console.WriteLine("Red value = " + ((num >> 16)& 0xFF) + " Blue value = " +
                    ((num >> 8)& 0xFF) + " Green value = " + (num & 0xFF));
            }
        }

        public void printlnTime(string message,int delayTimeSecond)
        {
           if(timerDebug.CheckDelayTimeInSecond(delayTimeSecond))
            {
             
            }
            else
            {
                Console.WriteLine(message + "     " + DateTime.Now);
                timerDebug.SetStartedSecondTime();
            }

           
        }

        public static void PrintArray<T>(T[] array)
        {
            if (array != null)
            {
                println("PrintArray func array length = " + array.Length.ToString());

                foreach (T item in array)
                {
                    Console.WriteLine(item);
                }
            }
            else
            {
                println("PrintArray function param is null");
            }

            
        }

        /* public static void printlnRndColor(string message)
         {
             if (colorChangerCounter >= arrayColors.Length) colorChangerCounter = 0;
             Console.BackgroundColor = arrayColors[colorChangerCounter++];
             Console.WriteLine(message + " " + DateTime.Now);

         }*/


    }
}
