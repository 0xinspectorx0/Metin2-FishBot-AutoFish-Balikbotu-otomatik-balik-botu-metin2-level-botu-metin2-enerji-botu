using MusicPlayerApp.Debugs;
using MusicPlayerApp.Sources;
using MusicPlayerApp.Sources.ImageHandle;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Metin2AutoFishCSharp.Sources.ChatHandler
{
    internal class GameAlphabetRectangle
    {
        ImageObjects imagesAlgorithm;




        public Rectangle[] tempArrays;
        public GameAlphabetRectangle(ImageObjects imagesAlgorithm)
        {
            this.imagesAlgorithm = imagesAlgorithm;

            tempArrays = convertAlphabetsRect();

        }
        
        /// <summary>
        /// Her harf sablonunun boyutunu (0,0 tabanli Rectangle) doner.
        /// Dosya listesi <see cref="GameAlphabetDetecter.LoadAlphabetFileNames"/> ile ayni
        /// sirada gelir; boylece index'ler iki dizi arasinda birebir eslesir.
        /// </summary>
        private Rectangle[] convertAlphabetsRect()
        {
            string[] fileNames = GameAlphabetDetecter.LoadAlphabetFileNames();
            Rectangle[] alphabetArrays = new Rectangle[fileNames.Length];

            for (int i = 0; i < fileNames.Length; i++)
            {
                // Bitmap dispose edilir; sadece boyut bilgisi saklanir (GDI sizintisi giderildi).
                using (Bitmap tempBitmap = FileHandler.ReadPngFileGetBitmap(
                    Path.GetFileName(fileNames[i]), PathWayStruct.PATH_CHAT_ALPHABETS))
                {
                    alphabetArrays[i] = new Rectangle(0, 0, tempBitmap.Width, tempBitmap.Height);
                }
            }

            return alphabetArrays;
        }
    }
}
