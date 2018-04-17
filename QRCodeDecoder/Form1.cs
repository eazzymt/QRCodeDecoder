using System;
using System.Drawing;
using System.Windows.Forms;

namespace QRCodeDecoder
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
            pbQrImg.Visible = false;
        }

        private void btnLoadFile_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Filter = "画像ファイル(*.jpg)|*.jpg";
                ofd.Title = "開く画像ファイルを選択して下さい";
                ofd.CheckFileExists = true;
                ofd.CheckPathExists = true;
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    pbQrImg.ImageLocation = ofd.FileName;
                    btnDecode.Enabled = true;
                }
            }
        }

        private void btnDecode_Click(object sender, EventArgs e)
        {
#if ABC
            if (pbQrImg.Image == null)
            {
                MessageBox.Show("画像が読み込まれていません。");
                return;
            }
#endif

            using (clsQRDecorder objDecoder = new clsQRDecorder(pbQrImg.ImageLocation))
            {
                if (objDecoder.judgeQRPtn())
                {
                    objDecoder.decode();

                    ImageConverter imgCnv = new ImageConverter();
                    pbQrImgMask.Image = (Image)imgCnv.ConvertFrom(objDecoder.getTestImg());
                }
                else
                {
                    MessageBox.Show("判定：NG");
                }
            }

#if AAA
            using (clsQRDecorder objDecoder = new clsQRDecorder(pbQrImg.ImageLocation))
            {
                ImageConverter imgCnv = new ImageConverter();
                pbQrImgMask.Image = (Image)imgCnv.ConvertFrom(objDecoder.getTestImg());
            }
#endif

        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            pbQrImgMask.Image.Save("rotImg.bmp", System.Drawing.Imaging.ImageFormat.Bmp);
            MessageBox.Show("保存しました。");
        }
    }
}
