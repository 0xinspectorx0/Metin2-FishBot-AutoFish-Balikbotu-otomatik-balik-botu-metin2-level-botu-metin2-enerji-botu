using MusicPlayerApp.Debugs;
using MusicPlayerApp.Sources;
using MusicPlayerApp.Sources.ImageHandle;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MusicPlayerApp
{
    public partial class FullScreen : Form
    {
        private ScreenShotWinAPI screenShot;
        private DebugDrawingHandle drawingDebug;
        public  bool isFormLoaded = false;

        static Rectangle ScreenBounds = Screen.PrimaryScreen.Bounds;
        private Bitmap fullScreenImageBtmap = new Bitmap(ScreenBounds.Width,ScreenBounds.Height);
        public FullScreen()
        {
            InitializeComponent();
            DebugPfCnsl.println("FullScreen constructor is called");
            this.FormBorderStyle = FormBorderStyle.None; // Kenarlık olmadan
            this.WindowState = FormWindowState.Maximized; // Tam ekran
            this.TopMost = true; // Diğer pencerelerin üstünde

            screenShot = new ScreenShotWinAPI();
            fullScreenImageBtmap = screenShot.CaptureScreen();
            if (fullScreenImageBtmap == null)
            {
                FileLogger.Error("FullScreen: tam ekran goruntusu alinamadi, form kapatiliyor", null);
                this.Close();
                return;
            }
            this.BackgroundImage = fullScreenImageBtmap;
            this.BackgroundImageLayout = ImageLayout.Stretch;

            drawingDebug = new DebugDrawingHandle();
            this.DoubleBuffered = true; // Çift tamponlamayı etkinleştirme, ekran titremesini önler
        }

        private void FullScreen_Paint(object sender, PaintEventArgs e)
        {

            // base.OnPaint(e);
         //   Console.WriteLine("Fullscreen paint is called");
                drawingDebug.OnPaint(e);
            
         
        }

        private void FullScreen_MouseMove(object sender, MouseEventArgs e)
        {
            drawingDebug.OnMouseMove(e);
            this.Invalidate(); // Yeniden çizim talep et
        }

        private void FullScreen_MouseDown(object sender, MouseEventArgs e)
        {
            drawingDebug.OnMouseDown(e);
            
        }

        private void FullScreen_MouseUp(object sender, MouseEventArgs e)
        {
            drawingDebug.OnMouseUp(e);
            // Dikdörtgen çizimi bittiğinde ekranı yeniden çiz
            this.Invalidate();
            
            Rectangle selectedArea = drawingDebug.getResultRectangle();

            // Kullanici suruklemeden sadece tiklarsa alan 0x0 kalir; Clone bu durumda
            // OutOfMemoryException firlatir. Bu yuzden once alan dogrulanir.
            if (selectedArea.Width <= 0 || selectedArea.Height <= 0 || fullScreenImageBtmap == null)
            {
                FileLogger.Warning("FullScreen: gecerli bir alan secilmedi. Alan = "
                    + ScreenShotWinAPI.Describe(selectedArea));
                Close();
                return;
            }

            // Onceki gorsel serbest birakilmazsa PictureBox her secimde bir bitmap biriktirir.
            Image previousImage = MainForm.pictureBox.Image;
            MainForm.pictureBox.Image = fullScreenImageBtmap.Clone(selectedArea, fullScreenImageBtmap.PixelFormat);
            if (previousImage != null)
            {
                previousImage.Dispose();
            }

            ScreenShotWinAPI.EditBipMapEndSave(fullScreenImageBtmap, selectedArea,
                "test.png", PathWayStruct.PATH_DESTKOP);

            using (Bitmap bitmap = screenShot.ClipBitmap(fullScreenImageBtmap, selectedArea))
            {
                FileLogger.Info("Secilen alan = " + ScreenShotWinAPI.Describe(selectedArea));
                DebugPfCnsl.printIntArrayAsDescended(screenShot.ConvertBitmapToArray(bitmap));
            }

            Close();

        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            // Tam ekran bitmap'i form boyunca tutuluyor; form kapaninca serbest birakilir.
            if (this.BackgroundImage != null)
            {
                this.BackgroundImage = null;
            }
            if (fullScreenImageBtmap != null)
            {
                fullScreenImageBtmap.Dispose();
                fullScreenImageBtmap = null;
            }
            base.OnFormClosed(e);
        }

        private void FullScreen_Load(object sender, EventArgs e)
        {
            isFormLoaded = true;
           
        }
    }
}
