using System;
using System.Collections.Generic;
using OpenCvSharp;

namespace QRCodeDecoder
{
    public class ClsQRCodeDecorder : IDisposable
    {
        #region 宣言
        enum lblIdxEnum
        {
            posX = 0,
            posY,
            width,
            height
        }
        enum scanDirEnum
        {
            dirX,
            dirY
        }

        // 基本仕様 表D.1
        // 型番7以上の型番情報(BCH符号)
        // 有効ビット数：16bit
        // データビット：上位4bit
        // 誤り訂正ビット：下位12bit
        int[] VerInfoAry = new int[] {
            0x07C94,0x085BC,0x09A99,0x0A4D3, // 7～10
            0x0BBF6,0x0C762,0x0D847,0x0E60D,0x0F928,0x10B78,0x1145D,0x12A17,0x13532,0x149A6, // 11～20
            0x15683,0x168C9,0x177EC,0x18EC4,0x191E1,0x1AFAB,0x1B08E,0x1CC1A,0x1D33F,0x1ED75, // 21～30
            0x1F250,0x209D5,0x216F0,0x228BA,0x2379F,0x24B0B,0x2542E,0x26A64,0x27541,0x28C69  // 31～40
        };

        // 基本仕様 表C.1
        // 形式情報(BCH符号)
        // 有効ビット数：15bit
        // データビット：上位5bit
        // 誤り訂正ビット：下位10bit
        int[] FmtInfoAry = new int[] {
            0x0000,0x0537,0x0A6E,0x0F59,0x11EB,0x14DC,0x1B85,0x1EB2,
            0x23D6,0x26E1,0x29B8,0x2C8F,0x323D,0x370A,0x3853,0x3D64,
            0x429B,0x47AC,0x48F5,0x4DC2,0x5370,0x5647,0x591E,0x5C29,
            0x614D,0x647A,0x6B23,0x6E14,0x70A6,0x7591,0x7AC8,0x7FFF
        };

        private const int quietPxl = 10;
        private const int FmtInfoMask = 0x5413;
        private string imgPath;
        private int modPxl;
        private int fndPtnTopLeft;
        private int fndPtnTopRight;
        //private int fndPtnBottom; // 使い途がありそうなら復活する
        private int qrCodeModNum;
        private int qrTop;
        private int qrLeft;
        private int qrPxl;
        private int qrVer;
        private int qrMask;
        private int qrErrLvl;
        private byte[] encodeData;
        private byte modBlk = 0; // 白黒反転画像の場合、255(白)を暗とする

        private Mat matQr;
        private Rect[] rctFinderPtn;
        private bool[,] modAry;

        private int errRange = 0;
        #endregion

        #region コンストラクタ・デストラクタ
        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="imgPath"></param>
        public ClsQRCodeDecorder(string imgPath)
        {
            this.imgPath = imgPath;
            matQr = null;
            rctFinderPtn = new Rect[3];
        }

        /// <summary>
        /// デストラクタ
        /// </summary>
        ~ClsQRCodeDecorder(){
            this.Dispose();
        }
        #endregion

        #region パブリックメソッド
        /// <summary>
        /// UnManagedなリソースの解放
        /// </summary>
        public void Dispose()
        {
            // 生成したMatのImEncode()をClassの外に返すと、呼び出し元がイメージデータ破棄するまでDisposeできなくなる
            //if (qZoneMat != null) {
            //    qZoneMat.Dispose();
            //    qZoneMat = null;
            //}
            //if (binMat != null)
            //{
            //    binMat.Dispose();
            //    binMat = null;
            //}
        }

        /// <summary>
        /// 試験用処理、Mat上に画像データを適宜、生成して、その結果を返す
        /// </summary>
        /// <returns></returns>
        public object GetTestImg()
        {
            Mat matTest = new Mat(imgPath);


            return matTest.ImEncode();
        }

        /// <summary>
        /// デコードメイン処理
        /// </summary>
        public byte[] Decode()
        {
            byte[] qrDecodeData;

            // 傾き補正、QRコードであることを確認、位置検出パターンの取得
            if (!JudgeQRPtn()) return null;

            // 基本仕様 12.e
            // シンボルの公称Ｘ寸法(モジュールピクセルサイズ)を求める
            modPxl = (rctFinderPtn[fndPtnTopLeft].Width + rctFinderPtn[fndPtnTopRight].Width) / 14; // 一応、仕様書通りの書き方をしておく(冗長だけど)

            // 基本仕様 12.i
            // 本来であれば歪みがあることを想定して、型番取得後にマトリックス情報を取得するが、
            // 本実装では簡略化する
            // 先にマトリックス情報を取得し、そこから型番情報を取得してしまう
            qrPxl = rctFinderPtn[fndPtnTopRight].X + modPxl * 7 - rctFinderPtn[fndPtnTopLeft].X;
            qrCodeModNum = qrPxl / modPxl;
            qrLeft = rctFinderPtn[fndPtnTopLeft].X;
            qrTop = rctFinderPtn[fndPtnTopLeft].Y;
            GetModAry();

            // 基本仕様 12.d,f,g
            // 型番算出
            GetVerInfo();
            if (qrVer < 0) return null;

            // 基本仕様 12.h
            // 本来であれば歪みがあることを想定して位置合わせパターンの確認などを行うが、割愛

            // 基本仕様 12.j
            // 形式情報を復号する
            GetFormatInfo();
            if (qrMask < 0 || qrErrLvl < 0) return null;

            // 基本情報 12.y
            // 符号化領域のデータを取得してマスクパターンにてマスク解除する

            qrDecodeData = new byte[1];
            return qrDecodeData;
        }
        #endregion

        #region プライベートメソッド
        #region 画像処理（位置検出パターンの抽出→傾き補正→明暗データ取得）
        /// <summary>
        /// 傾き補正→QRコード判定
        /// </summary>
        /// <returns></returns>
        private bool JudgeQRPtn()
        {
            List<RotatedRect> rctCandid;
            using (Mat matLoad = new Mat(imgPath))
            {
                rctCandid = GetFinderPtn(matLoad);
                if (rctCandid == null) return false;

                // 位置検出パターンが３つ見つかったので、傾き補正する
                using (Mat matRotCol = RotMat(matLoad, rctCandid))
                {
                    // 回転後の位置検出パターンを探す
                    // 回転(Affine)行列の計算で位置検出パターンを算出すると少しずれるので、もう１度、位置検出パターン検索処理を使う
                    rctCandid = GetFinderPtn(matRotCol);

                    if (rctCandid == null) return false;
                    GetRotAngle(rctCandid); // 配列が変わったので、要素番号取り直しのため、実行する
                    for (int cnt = 0; cnt < rctCandid.Count; cnt++)
                    {
                        double ptX = rctCandid[cnt].Center.X - rctCandid[cnt].Size.Width / 2.0;
                        double ptY = rctCandid[cnt].Center.Y - rctCandid[cnt].Size.Height / 2.0;
                        Point pt = new Point(ptX, ptY);
                        rctFinderPtn[cnt] = new Rect(pt, new Size(rctCandid[cnt].Size.Width, rctCandid[cnt].Size.Height));
                    }
                    matQr = GetBinMat(matRotCol);
                }
            }

            return true;
        }

        /// <summary>
        /// 指定矩形の２つの中線をスキャンして、位置検出パターンかどうか判定する
        /// </summary>
        /// <param name="matTarget">2値画像</param>
        /// <param name="pts"></param>
        /// <returns></returns>
        bool IsFinderPtn(Mat matTarget, Point2f[] pts)
        {
            int[] finderPtnAry;
            Point2f ptMid1 = new Point2f((pts[0].X + pts[1].X) / 2.0f, (pts[0].Y + pts[1].Y) / 2.0f);
            Point2f ptMid2 = new Point2f((pts[2].X + pts[3].X) / 2.0f, (pts[2].Y + pts[3].Y) / 2.0f);
            finderPtnAry = ScanPxlSeries(matTarget, ptMid1, ptMid2);
            if (!ChkFinderPtn(finderPtnAry)) return false;

            ptMid1 = new Point2f((pts[1].X + pts[2].X) / 2.0f, (pts[1].Y + pts[2].Y) / 2.0f);
            ptMid2 = new Point2f((pts[3].X + pts[0].X) / 2.0f, (pts[3].Y + pts[0].Y) / 2.0f);
            finderPtnAry = ScanPxlSeries(matTarget, ptMid1, ptMid2);
            return ChkFinderPtn(finderPtnAry);
        }

        /// <summary>
        /// 位置検出パターンをみつけるため、指定開始・終了位置で示された線分上の画素の明暗連続状況を調べる
        /// </summary>
        /// <param name="matTarget">2値画像</param>
        /// <param name="ptSt"></param>
        /// <param name="ptEd"></param>
        /// <returns></returns>
        private int[] ScanPxlSeries(Mat matTarget, Point2f pt1, Point2f pt2){
            float stMst, stSlv, edMst, edSlv;
            scanDirEnum scanDir;

            // XとYのどちらを独立変数にするか決める
            // 差分が大きい方を独立変数とする
            if (Math.Abs(pt1.X - pt2.X) < Math.Abs(pt1.Y - pt2.Y))
            {
                scanDir = scanDirEnum.dirY;
                // 始点・終点を決める
                if (pt1.Y < pt2.Y)
                {
                    // 独立変数：Y、始点：pt1、終点：pt2
                    stMst = pt1.Y;
                    stSlv = pt1.X;
                    edMst = pt2.Y;
                    edSlv = pt2.X;
                }
                else
                {
                    // 独立変数：Y、始点：pt2、終点：pt1
                    stMst = pt2.Y;
                    stSlv = pt2.X;
                    edMst = pt1.Y;
                    edSlv = pt1.X;
                }
            }
            else
            {
                scanDir = scanDirEnum.dirX;
                if (pt1.X < pt2.X)
                {
                    // 独立変数：X、始点：pt1、終点：pt2
                    stMst = pt1.X;
                    stSlv = pt1.Y;
                    edMst = pt2.X;
                    edSlv = pt2.Y;
                }
                else
                {
                    // 独立変数：X、始点：pt2、終点：pt1
                    stMst = pt2.X;
                    stSlv = pt2.Y;
                    edMst = pt1.X;
                    edSlv = pt1.Y;
                }
            }

            // 線分の傾き、切片を求める
            // 傾きといってもdY/dXではなく０除算を避けるため、(従属変数差分)/(独立変数差分)とする
            // 切片も同様にY切片ではない
            double col = (edSlv - stSlv) / (edMst-stMst);
            double v0 = stSlv - col * stMst;

            int aryIdx = 0;
            int[] cuttingAry = { 0, 0, 0, 0, 0 };

            for (int mstPos = (int)stMst; mstPos < edMst; mstPos++)
            {
                int slvPos = (int)(mstPos * col + v0);
                bool modVal; // 黒=True
                if (scanDir == scanDirEnum.dirX)
                {
                    modVal = (matTarget.At<byte>(slvPos, mstPos) == modBlk);
                }
                else
                {
                    modVal = (matTarget.At<byte>(mstPos, slvPos) == modBlk);
                }

                // currintAry[aryIdx]には
                // [黒の連続数,白の連続数,黒の…,白の…,黒の…]
                // が入る。つまり
                // ・偶数番目→黒の連続数
                // ・奇数番目→白の連続数
                // となる。
                if (aryIdx % 2 == 0)
                {
                    if (!modVal)
                    {
                        if (aryIdx == 0 && cuttingAry[aryIdx] == 0) continue; // 最初の黒まで飛ばす
                        aryIdx++;
                        if (aryIdx >= 5) break;
                    }
                }
                else
                {
                    if (modVal)
                    {
                        aryIdx++;
                        if (aryIdx >= 5) break;
                    }
                }
                cuttingAry[aryIdx]++;
            }

            return cuttingAry;
        }

        /// <summary>
        /// 位置検出パターンを取得
        /// 基本仕様 12.b
        /// </summary>
        /// <param name="matOrg"></param>
        /// <returns></returns>
        private List<RotatedRect> GetFinderPtn(Mat matOrg)
        {
            List<RotatedRect> rctCandid = new List<RotatedRect>();
            Size qrSize = new Size(matOrg.Cols + quietPxl * 2, matOrg.Rows + quietPxl * 2);
            errRange = Math.Max(qrSize.Height, qrSize.Width) / 100;

            using (Mat matBin = GetBinMat(matOrg))
            using (Mat matQuiet = new Mat(qrSize, matBin.Type(), Scalar.White))
            {
                // 最外輪郭を確実に取れるように、QuietZoneを設定
                Rect rctQz = new Rect(quietPxl, quietPxl, matBin.Cols, matBin.Rows);
                using (Mat matRoi = new Mat(matQuiet, rctQz))
                {
                    matBin.CopyTo(matRoi); // matQuiet = matOrg + matRoi(rctQz)
                }

                // 輪郭抽出
                Point[][] shapes = matQuiet.Clone().FindContoursAsArray(RetrievalModes.List,
                    ContourApproximationModes.ApproxSimple, new Point(0, 0));

                // 位置検出パターンを探す
                foreach (Point[] shape in shapes)
                {
                    RotatedRect rotRct = Cv2.MinAreaRect(shape);
                    // 抽出条件
                    // ・正方形であること
                    // ・領域の幅・高さが１／４（テキトー）以下であること
                    if (CompareVal(rotRct.Size.Height, rotRct.Size.Width) != 0) continue;
                    if (CompareVal(rotRct.Size.Height, matQuiet.Rows / 4) == 1) continue;

                    // 傾き補正せずとも、矩形を縦断・横断する２つの中線上の明暗が位置検出パターンの比率に合っていればよい
                    Point2f[] rctPts = rotRct.Points();
                    if (IsFinderPtn(matQuiet, rctPts))
                    {
                        // 矩形位置情報から、QuietZone分を引いておく
                        RotatedRect wkRot = new RotatedRect(rotRct.Center, rotRct.Size, rotRct.Angle);
                        wkRot.Center.X -= quietPxl;
                        wkRot.Center.Y -= quietPxl;
                        rctCandid.Add(wkRot);
                    }
                }
            }

            // 結果検証
            // ３つの同じ大きさ、同じ傾きの正方形が得られていれば成功
            if (rctCandid.Count != 3) return null;
            if (CompareVal(rctCandid[0].Size.Width, rctCandid[0].Size.Height) != 0 ||
                CompareVal(rctCandid[1].Size.Width, rctCandid[1].Size.Height) != 0 ||
                CompareVal(rctCandid[2].Size.Width, rctCandid[2].Size.Height) != 0 ||
                CompareVal(rctCandid[0].Size.Width, rctCandid[1].Size.Width) != 0 ||
                CompareVal(rctCandid[1].Size.Width, rctCandid[2].Size.Width) != 0) return null;
            double dAngle = Math.Abs(rctCandid[0].Angle - rctCandid[1].Angle);
            if (CompareVal(dAngle, 0.0) != 0 &&
                CompareVal(dAngle, Math.PI * 0.5) != 0 &&
                CompareVal(dAngle, Math.PI) != 0 &&
                CompareVal(dAngle, Math.PI * 1.5) != 0 &&
                CompareVal(dAngle, Math.PI * 2.0) != 0) return null;
            dAngle = Math.Abs(rctCandid[1].Angle - rctCandid[2].Angle);
            if (CompareVal(dAngle, 0.0) != 0 &&
                CompareVal(dAngle, Math.PI * 0.5) != 0 &&
                CompareVal(dAngle, Math.PI) != 0 &&
                CompareVal(dAngle, Math.PI * 1.5) != 0 &&
                CompareVal(dAngle, Math.PI * 2.0) != 0) return null;
            dAngle = Math.Abs(rctCandid[2].Angle - rctCandid[0].Angle);
            if (CompareVal(dAngle, 0.0) != 0 &&
                CompareVal(dAngle, Math.PI * 0.5) != 0 &&
                CompareVal(dAngle, Math.PI) != 0 &&
                CompareVal(dAngle, Math.PI * 1.5) != 0 &&
                CompareVal(dAngle, Math.PI * 2.0) != 0) return null;

            return rctCandid;
        }

        /// <summary>
        /// ３つの正方形の位置関係より、補正角度を求める
        /// </summary>
        /// <param name="rctRot"></param>
        /// <remarks>正方形が３つあること、３つの正方形の大きさが等しいこと、同一方向に傾いていることは保証済であること</remarks>
        /// <returns></returns>
        private double GetRotAngle(List<RotatedRect> rctRot)
        {
            double angle = double.NaN; // 補正角をradianで返す
            double rightAngle;

            // 【検証】
            // ３つの正方形の中心を結ぶ正三角形は直角二等辺三角形であること
            // 【補正角度】
            // その直角二等辺三角形の直角となる頂点が右上になるように回転する
            double len01 = Math.Sqrt(
                Math.Pow(rctRot[0].Center.X - rctRot[1].Center.X, 2.0) +
                Math.Pow(rctRot[0].Center.Y - rctRot[1].Center.Y, 2.0));
            double len12 = Math.Sqrt(
                Math.Pow(rctRot[1].Center.X - rctRot[2].Center.X, 2.0) +
                Math.Pow(rctRot[1].Center.Y - rctRot[2].Center.Y, 2.0));
            double len20 = Math.Sqrt(
                Math.Pow(rctRot[2].Center.X - rctRot[0].Center.X, 2.0) +
                Math.Pow(rctRot[2].Center.Y - rctRot[0].Center.Y, 2.0));

            if (CompareVal(len01, len20) == 0 && CompareVal(len01 * Math.Sqrt(2.0), len12) == 0)
            {
                // 要素０で直角（左上）
                rightAngle = GetAngle(rctRot[0].Center, rctRot[1].Center, rctRot[2].Center);
                fndPtnTopLeft = 0;
                if (CompareVal(rightAngle, Math.PI * 0.5) == 0)
                {
                    // 求めた角が90°
                    // 要素１：右上、要素２：下
                    // [0][1]
                    // [2]
                    // 要素０→要素１へのベクトルを水平になるように補正する
                    // Y軸が下向きなので、補正角を×（－１）する必要はない
                    angle = GetAngle(rctRot[0].Center, rctRot[1].Center);
                    fndPtnTopRight = 1;
                    //fndPtnBottom = 2;
                }
                else
                {
                    // 求めた角が270°
                    // 要素２：右上、要素１：下
                    // [0][2]
                    // [1]
                    // 要素０→要素２へのベクトルを水平になるように補正する
                    angle = GetAngle(rctRot[0].Center, rctRot[2].Center);
                    fndPtnTopRight = 2;
                    //fndPtnBottom = 1;
                }
            }
            else if (CompareVal(len01, len12) == 0 && CompareVal(len01 * Math.Sqrt(2.0), len20) == 0)
            {
                // 要素１で直角（左上）
                fndPtnTopLeft = 1;

                // 以下、同様に処理する
                rightAngle = GetAngle(rctRot[1].Center, rctRot[0].Center, rctRot[2].Center);
                if (CompareVal(rightAngle, Math.PI * 0.5) == 0)
                {
                    angle = GetAngle(rctRot[1].Center, rctRot[0].Center);
                    fndPtnTopRight = 0;
                    //fndPtnBottom = 2;
                }
                else
                {
                    angle = GetAngle(rctRot[1].Center, rctRot[2].Center);
                    fndPtnTopRight = 2;
                    //fndPtnBottom = 0;
                }
            }
            else if (CompareVal(len12, len20) == 0 && CompareVal(len12 * Math.Sqrt(2.0), len01) == 0)
            {
                // 要素２で直角（左上）
                fndPtnTopLeft = 2;

                // 以下、同様に処理する
                rightAngle = GetAngle(rctRot[2].Center, rctRot[0].Center, rctRot[1].Center);
                if (CompareVal(rightAngle, Math.PI * 0.5) == 0)
                {
                    angle = GetAngle(rctRot[2].Center, rctRot[0].Center);
                    fndPtnTopRight = 0;
                    //fndPtnBottom = 1;
                }
                else
                {
                    angle = GetAngle(rctRot[2].Center, rctRot[1].Center);
                    fndPtnTopRight = 1;
                    //fndPtnBottom = 0;
                }
            }
            else
            {
                // 直角二等辺三角形になっていない→初期値(NaN)のまま値を返す(do nothing)
            }

            return angle;
        }

        /// <summary>
        /// 画像データを指定角度回転する
        /// 基本仕様 12.c
        /// </summary>
        /// <param name="matOrg"></param>
        /// <param name="deg"></param>
        /// <returns></returns>
        private Mat RotMat(Mat matOrg, List<RotatedRect> rctRot)
        {
            Mat matRot = new Mat();

            // 傾き角、回転中央座標を算出
            // ※各位置検出パターンの角度は（傾き角度×－１）となっている
            //double angle = (rctRot[0].Angle + rctRot[1].Angle + rctRot[2].Angle) / 3.0;
            double angle = GetRotAngle(rctRot) / Math.PI * 180.0;
            Point2f cenPt = new Point2f(matOrg.Cols / 2, matOrg.Rows / 2);
            using (Mat matAff = Cv2.GetRotationMatrix2D(cenPt, angle, 1.0))
            {
                // 回転してできた余白は、白で塗りつぶす
                Cv2.WarpAffine(matOrg, matRot, matAff, matOrg.Size(), InterpolationFlags.Linear, BorderTypes.Constant, Scalar.White);
            }

            return matRot;
        }

        /// <summary>
        /// 位置検出パターン判定
        /// </summary>
        /// <param name="finderPtnAry"></param>
        /// <returns></returns>
        private bool ChkFinderPtn(int[] finderPtnAry)
        {
            // 黒白黒白黒の画素数比が「1:1:3:1:1」になっていることを確認する
            // (判定方法はQRコードJISコード基本仕様 13.bに従う）
            double modSizeAvr = (finderPtnAry[0] + finderPtnAry[1] + finderPtnAry[2] / 3.0 + finderPtnAry[3] + finderPtnAry[4]) / 5.0;
            if (finderPtnAry[0] == 0) return false;
            if (0.5 > finderPtnAry[0] / modSizeAvr) return false;
            if (1.5 < finderPtnAry[0] / modSizeAvr) return false;
            if (finderPtnAry[1] == 0) return false;
            if (0.5 > finderPtnAry[1] / modSizeAvr) return false;
            if (1.5 < finderPtnAry[1] / modSizeAvr) return false;
            if (finderPtnAry[2] == 0) return false;
            if (2.5 > finderPtnAry[2] / modSizeAvr) return false;
            if (3.5 < finderPtnAry[2] / modSizeAvr) return false;
            if (finderPtnAry[3] == 0) return false;
            if (0.5 > finderPtnAry[3] / modSizeAvr) return false;
            if (1.5 < finderPtnAry[3] / modSizeAvr) return false;
            if (finderPtnAry[4] == 0) return false;
            if (0.5 > finderPtnAry[4] / modSizeAvr) return false;
            if (1.5 < finderPtnAry[4] / modSizeAvr) return false;

            return true;
        }

        /// <summary>
        /// 多色画像あるいはグレースケール画像データをもとに、２値画像データを生成
        /// </summary>
        /// <param name="matColor"></param>
        /// <returns></returns>
        Mat GetBinMat(Mat matColor)
        {
            Mat matBin;
            using (Mat matGray = new Mat())
            {
                // 基本仕様 12.a
                // 最大値(=255)、最小値(=0)の中間値を閾値として２値画像を生成する
                if (matColor.Type() != MatType.CV_8UC1)
                {
                    Cv2.CvtColor(matColor, matGray, ColorConversionCodes.BGR2GRAY);
                    matBin = matGray.Threshold(255 / 2.0, 255, ThresholdTypes.Binary);
                }
                else
                {
                    matBin = matColor.Threshold(255 / 2.0, 255, ThresholdTypes.Binary);
                }
            }

            return matBin;
        }

                /// <summary>
        /// モジュール明暗情報を取得
        /// </summary>
        /// <remarks>基本仕様 12.a</remarks>
        private void GetModAry()
        {
            modAry = new bool[qrCodeModNum, qrCodeModNum];

            for (int cntX = 0; cntX < qrCodeModNum; cntX++)
            {
                for (int cntY = 0; cntY < qrCodeModNum; cntY++)
                {
                    // 各モジュール画像を縦横１ピクセルの画像に圧縮して、明暗を判別する
                    Rect rctCut = new Rect(qrLeft + cntX * modPxl, qrTop + cntY * modPxl, modPxl, modPxl);
                    Mat matRct = new Mat(matQr, rctCut);
                    Size sz1px = new Size(1, 1);
                    Mat mat1px = GetBinMat(matRct.Resize(sz1px, 0, 0, InterpolationFlags.Cubic));
                    modAry[cntX, cntY] = (mat1px.At<byte>(0, 0) == modBlk); // 黒をTrueとする
                }
            }
        }

        /// <summary>
        /// 許容誤差付き大小比較(int)
        /// </summary>
        /// <param name="v1"></param>
        /// <param name="v2"></param>
        /// <returns></returns>
        /// <remarks>許容誤差はQRコード画像のサイズより求める</remarks>
        private int CompareVal(int v1, int v2)
        {
            if (Math.Abs(v1 - v2) < errRange) return 0;

            if (v1 < v2)
            {
                return -1;
            }
            else
            {
                return 1;
            }
        }

        /// <summary>
        /// 許容誤差付き大小比較(float,double)
        /// </summary>
        /// <param name="v1"></param>
        /// <param name="v2"></param>
        /// <returns></returns>
        /// <remarks>許容誤差はQRコード画像のサイズより求める</remarks>
        private int CompareVal(double v1, double v2)
        {
            if (Math.Abs(v1 - v2) < errRange) return 0;

            if (v1 < v2)
            {
                return -1;
            }
            else
            {
                return 1;
            }
        }

        /// <summary>
        /// ２点間の距離を求める
        /// </summary>
        /// <param name="p1"></param>
        /// <param name="p2"></param>
        /// <returns></returns>
        private double GetLen(Point2f p1, Point2f p2)
        {
            return Math.Sqrt(Math.Pow(Math.Abs(p1.X - p2.X), 2.0) + Math.Pow(Math.Abs(p1.Y - p2.Y), 2.0));
        }

        /// <summary>
        /// ベクトルp1→p2とベクトルp1→p3がなす角を求める
        /// 角度は-PI～+PIに正規化する
        /// </summary>
        /// <param name="p1"></param>
        /// <param name="p2"></param>
        /// <param name="p3"></param>
        /// <returns></returns>
        private double GetAngle(Point2f p1, Point2f p2, Point2f p3)
        {
            double retAngle;
            double a12 = Math.Atan2(p2.Y - p1.Y, p2.X - p1.X);
            double a13 = Math.Atan2(p3.Y - p1.Y, p3.X - p1.X);
            retAngle = a13 - a12;

            return Math.Atan2(Math.Sin(retAngle), Math.Cos(retAngle)); // 正規化
        }

        /// <summary>
        /// ベクトルp1→p2がｘ軸となす角を求める
        /// 角度は-PI～+PIに正規化する
        /// </summary>
        /// <param name="p1"></param>
        /// <param name="p2"></param>
        /// <returns></returns>
        private double GetAngle(Point2f p1, Point2f p2)
        {
            return Math.Atan2(p2.Y - p1.Y, p2.X - p1.X);
        }
        #endregion

        #region エラー訂正符号(BCH)
        /// <summary>
        /// 型番情報を復号
        /// </summary>
        /// <param name="verInfo"></param>
        /// <returns></returns>
        private int DecodeVerInfo(bool[] verInfo)
        {
            int verVal = 0;

            // ビット(bool)配列→数値(18bit幅)に変換
            for (int cnt = 17; cnt >= 0; cnt--)
            {
                verVal = verVal << 1;
                if (verInfo[cnt]) verVal++;
            }

            // ハミング距離が4未満の型番情報をみつける
            // 正しい型番情報はハミング距離が8なので、正しい型番情報はハミング情報が4未満のはず
            for (int cnt = 0; cnt < VerInfoAry.Length; cnt++)
            {
                int errCnt = VerInfoAry[cnt] ^ verVal;
                if (countBits(errCnt) < 4)
                {
                    return VerInfoAry[cnt] >> 12;
                }
                
            }

            return -1;
        }

        /// <summary>
        /// 形式情報を復号
        /// </summary>
        /// <param name="fmtInfo"></param>
        /// <returns></returns>
        private int DecodeFormatInfo(bool[] fmtInfo)
        {
            int fmtVal = 0;

            // ビット(bool)配列→数値(15bit幅)に変換
            for (int cnt = 14; cnt >= 0; cnt--)
            {
                fmtVal = fmtVal << 1;
                if (fmtInfo[cnt]) fmtVal++;
            }

            // ハミング距離が3以下の形式情報を見つける
            // 正しい形式情報はハミング距離が7なので、正しい形式情報はハミング情報が3以下のはず
            for (int cnt = 0; cnt < FmtInfoAry.Length; cnt++)
            {
                int errCnt = FmtInfoAry[cnt] ^ fmtVal;
                if (countBits(errCnt) < 4)
                {
                    return FmtInfoAry[cnt] >> 10;
                }
            }

            return -1;
        }

        /// <summary>
        /// 立っているビットの数を数える
        /// </summary>
        /// <param name="val"></param>
        /// <returns></returns>
        private int countBits(int val)
        {
            int bits = val;
            bits = (bits & 0x55555555) + ((bits >> 1) & 0x55555555);
            bits = (bits & 0x33333333) + ((bits >> 2) & 0x33333333);
            bits = (bits & 0x0F0F0F0F) + ((bits >> 4) & 0x0F0F0F0F);
            bits = (bits & 0x00FF00FF) + ((bits >> 8) & 0x00FF00FF);
            bits = (bits & 0x0000FFFF) + ((bits >> 16) & 0x0000FFFF);

            return bits;
        }
        #endregion

        #region その他
        /// <summary>
        /// 型番情報を算出
        /// 基本仕様 12.d,12.f,12.g
        /// </summary>
        private void GetVerInfo()
        {
            qrVer = -1;

            // 基本仕様 12.d
            // シンボルの全幅を横切る、左上の位置検出パターン及び右上の位置検出パターンの中心間の距離Ｄを求める
            int dstFndPtn = rctFinderPtn[fndPtnTopRight].X - rctFinderPtn[fndPtnTopLeft].X;

            // 基本仕様 12.f
            // シンボルの型番を仮に決める
            int verTmp = ((dstFndPtn / modPxl) - 10) / 4;

            // 基本仕様 12.g
            // 仮のシンボル型番が６以下の場合、この値を型番として使用する
            // ７以上の場合は型番情報を復号する
            if (verTmp <= 6)
            {
                qrVer = verTmp;
            }
            else
            {
                // 型番情報１を復号する
                qrVer = GetVerInfo(1);
                if (qrVer < 0)
                {
                    // 型番情報１が訂正不能→型番情報２を復号する
                    qrVer = GetVerInfo(2);
                    if (qrVer < 0) return;
                }
            }
        }

        /// <summary>
        /// 型番情報を復号して、型番算出
        /// 基本仕様 12.g.1～4
        /// </summary>
        /// <param name="verKind"></param>
        /// <returns></returns>
        private int GetVerInfo(int verKind)
        {
            bool[] verInfo = new bool[6 * 3];

            // 基本仕様 12.g.1は取得済

            // 基本仕様 12.g.2
            // 型番情報の位置を求める
            int startPos = ((rctFinderPtn[fndPtnTopRight].X - rctFinderPtn[fndPtnTopLeft].X) / modPxl) - 4;

            int modCnt = 0;
            for (int cnt1 = 0; cnt1 < 6; cnt1++)
            {
                for (int cnt2 = 0; cnt2 < 3; cnt2++)
                {
                    if (verKind == 1)
                    {
                        // 基本仕様 12.g.3
                        // 型番情報１（右上の型番情報）より型番を決定する
                        verInfo[modCnt] = modAry[cnt2 + startPos, cnt1];
                    }
                    else
                    {
                        // 基本仕様 12.g.4
                        // 型番情報１が訂正不能だった場合、型番情報２（左下の型番情報）より型番を決定する
                        verInfo[modCnt] = modAry[cnt1, cnt2 + startPos];
                    }
                    modCnt++;
                }
            }

            return DecodeVerInfo(verInfo);
        }

        /// <summary>
        /// 形式情報算出
        /// </summary>
        private void GetFormatInfo()
        {
            qrMask = -1;
            qrErrLvl = -1;

            // 基本仕様 12.j
            // 形式情報１（左上）を復号する
            int fmtInfo = GetFormatInfo(1);
            if (fmtInfo < 0)
            {
                // 形式情報１が訂正不能だった場合、形式情報２（右上＋左下）を復号する
                fmtInfo = GetFormatInfo(2);
                if (fmtInfo < 0) return;
            }

            // マスクを外して、マスクパターン、誤り訂正レベルを取得
            fmtInfo ^= FmtInfoMask;
            qrMask = fmtInfo >> 3;
            qrErrLvl = fmtInfo & 0x08;
        }

        /// <summary>
        /// 指定位置の形式情報を取得して復号する
        /// </summary>
        /// <param name="fKind"></param>
        /// <returns></returns>
        private int GetFormatInfo(int fKind)
        {
            int cur = 0;
            bool[] fmtInfo = new bool[15];

            if (fKind == 1)
            {
                // 左上から取得
                for (int cnt = 0; cnt < 6; cnt++)
                {
                    fmtInfo[cur] = modAry[8, cnt]; cur++;
                }
                fmtInfo[cur] = modAry[8, 7]; cur++;
                fmtInfo[cur] = modAry[8, 8]; cur++;
                fmtInfo[cur] = modAry[7, 8]; cur++;
                for (int cnt = 5; cnt >= 0; cnt--)
                {
                    fmtInfo[cur] = modAry[cnt, 8]; cur++;
                }
            }
            else
            {
                // 右上＋左下から取得
                for (int cnt = qrCodeModNum; cnt >= qrCodeModNum - 7; cnt--)
                {
                    fmtInfo[cur] = modAry[cnt, 8]; cur++;
                }
                for (int cnt = qrCodeModNum - 6; cnt <= qrCodeModNum; cnt++)
                {
                    fmtInfo[cur] = modAry[8, cur]; cur++;
                }
            }

            return DecodeFormatInfo(fmtInfo);
        }
        #endregion
        #endregion
    }
}
