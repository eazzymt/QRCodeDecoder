using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using QRCodeDecoder;

namespace testForm
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

            using (ClsQRCodeDecorder objDecoder = new ClsQRCodeDecorder(pbQrImg.ImageLocation))
            {
                byte[] decodeData = objDecoder.Decode();
                if (decodeData == null)
                {
                    MessageBox.Show("判定：NG");
                    return;
                }

                ImageConverter imgCnv = new ImageConverter();
                pbQrImgMask.Image = (Image)imgCnv.ConvertFrom(objDecoder.GetTestImg());
            }

#if AAA
            using (ClsQRDecorder objDecoder = new ClsQRDecorder(pbQrImg.ImageLocation))
            {
                ImageConverter imgCnv = new ImageConverter();
                pbQrImgMask.Image = (Image)imgCnv.ConvertFrom(objDecoder.GetTestImg());
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
