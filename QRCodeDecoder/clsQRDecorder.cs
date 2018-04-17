using System;
using System.Collections.Generic;
using OpenCvSharp;

namespace QRCodeDecoder
{
    class clsQRDecorder : IDisposable
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

        private const int quietPxl = 10;
        private string imgPath;
        private int modPxl;
        private int qrCodeModNum;
        private int qrTop;
        private int qrLeft;
        private int qrPxl;
        private int qrVer;

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
        public clsQRDecorder(string imgPath)
        {
            this.imgPath = imgPath;
            matQr = null;
            rctFinderPtn = new Rect[3];
        }

        /// <summary>
        /// デストラクタ
        /// </summary>
        ~clsQRDecorder(){
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
        public object getTestImg()
        {
            Mat matTest = new Mat(imgPath);


            return matTest.ImEncode();
        }

        /// <summary>
        /// 傾き補正→QRコード判定
        /// </summary>
        /// <returns></returns>
        public bool judgeQRPtn()
        {
            List<RotatedRect> rctCandid;
            using (Mat matLoad = new Mat(imgPath))
            {
                rctCandid = getFinderPtn(matLoad);
                if (rctCandid == null) return false;

                // 位置検出パターンが３つ見つかったので、傾き補正する
                using (Mat matRotCol = rotMat(matLoad, rctCandid))
                {
                    // 回転後の位置検出パターンを探す
                    // 回転(Affine)行列の計算で位置検出パターンを算出すると少しずれるので、もう１度、位置検出パターン検索処理を使う
                    rctCandid = getFinderPtn(matRotCol);
                    if (rctCandid == null) return false;

                    for (int cnt = 0; cnt < rctCandid.Count; cnt++)
                    {
                        double ptX = rctCandid[cnt].Center.X - rctCandid[cnt].Size.Width / 2.0;
                        double ptY = rctCandid[cnt].Center.Y - rctCandid[cnt].Size.Height / 2.0;
                        Point pt = new Point(ptX, ptY);
                        rctFinderPtn[cnt] = new Rect(pt, new Size(rctCandid[cnt].Size.Width, rctCandid[cnt].Size.Height));
                    }
                    matQr = getBinMat(matRotCol);
                }
            }

            // 位置検出パターンの位置関係を修正(90°単位の回転)
            return correctFinderPtnPos();
        }

        /// <summary>
        /// デコードメイン処理
        /// </summary>
        public void decode()
        {
            // 基本仕様 12.a
            // 各モジュールの明暗を取得する
            getModAry();

            // 基本仕様 12.b
            // 形式情報を取得する

            // 基本仕様 13.e
            // １モジュールの大きさ、QRコード全体の大きさ(モジュールの縦横数)を求める
            modPxl = (rctFinderPtn[0].Width + rctFinderPtn[1].Width) / 14; // 一応、仕様書通りの書き方をしておく(冗長だけど)
            qrCodeModNum = qrPxl / modPxl;

            // 基本仕様 13.f
            // 型番を求める(仮値)
            int verTmp = (((rctFinderPtn[1].X - rctFinderPtn[0].X)) / modPxl - 10) / 4;
            if (verTmp <= 6)
            {
                qrVer = verTmp; // 算出した仮値をそのまま採用
            }
            else
            {
                // 基本仕様 13.g
                // 基本仕様 13.fで求めた値が7以上の場合、型番情報を取得して型番を算出する
                qrVer = verTmp;
            }
        }
        #endregion

        #region プライベートメソッド
        #region 位置検出パターン判定
        /// <summary>
        /// 指定矩形の２つの中線をスキャンして、位置検出パターンかどうか判定する
        /// </summary>
        /// <param name="matTarget">2値画像</param>
        /// <param name="pts"></param>
        /// <returns></returns>
        bool isFinderPtn(Mat matTarget, Point2f[] pts)
        {
            int[] finderPtnAry;
            Point2f ptMid1 = new Point2f((pts[0].X + pts[1].X) / 2.0f, (pts[0].Y + pts[1].Y) / 2.0f);
            Point2f ptMid2 = new Point2f((pts[2].X + pts[3].X) / 2.0f, (pts[2].Y + pts[3].Y) / 2.0f);
            finderPtnAry = scanPxlSeries(matTarget, ptMid1, ptMid2);
            if (!chkFinderPtn(finderPtnAry)) return false;

            ptMid1 = new Point2f((pts[1].X + pts[2].X) / 2.0f, (pts[1].Y + pts[2].Y) / 2.0f);
            ptMid2 = new Point2f((pts[3].X + pts[0].X) / 2.0f, (pts[3].Y + pts[0].Y) / 2.0f);
            finderPtnAry = scanPxlSeries(matTarget, ptMid1, ptMid2);
            return chkFinderPtn(finderPtnAry);
        }

        /// <summary>
        /// 位置検出パターンをみつけるため、指定開始・終了位置で示された線分上の画素の明暗連続状況を調べる
        /// </summary>
        /// <param name="matTarget">2値画像</param>
        /// <param name="ptSt"></param>
        /// <param name="ptEd"></param>
        /// <returns></returns>
        private int[] scanPxlSeries(Mat matTarget, Point2f pt1, Point2f pt2){
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
                    modVal = (matTarget.At<byte>(slvPos, mstPos) == 0);
                }
                else
                {
                    modVal = (matTarget.At<byte>(mstPos, slvPos) == 0);
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
        /// </summary>
        /// <param name="matOrg"></param>
        /// <returns></returns>
        private List<RotatedRect> getFinderPtn(Mat matOrg)
        {
            List<RotatedRect> rctCandid = new List<RotatedRect>();
            Size qrSize = new Size(matOrg.Cols + quietPxl * 2, matOrg.Rows + quietPxl * 2);
            errRange = Math.Max(qrSize.Height, qrSize.Width) / 100;

            using (Mat matBin = getBinMat(matOrg))
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
                    if (compareVal(rotRct.Size.Height, rotRct.Size.Width) != 0) continue;
                    if (compareVal(rotRct.Size.Height, matQuiet.Rows / 4) == 1) continue;

                    // 傾き補正せずとも、矩形を縦断・横断する２つの中線上の明暗が位置検出パターンの比率に合っていればよい
                    Point2f[] rctPts = rotRct.Points();
                    if (isFinderPtn(matQuiet, rctPts))
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
            if (compareVal(rctCandid[0].Size.Width, rctCandid[0].Size.Height) != 0 ||
                compareVal(rctCandid[1].Size.Width, rctCandid[1].Size.Height) != 0 ||
                compareVal(rctCandid[2].Size.Width, rctCandid[2].Size.Height) != 0 ||
                compareVal(rctCandid[0].Size.Width, rctCandid[1].Size.Width) != 0 ||
                compareVal(rctCandid[1].Size.Width, rctCandid[2].Size.Width) != 0) return null;
            double dAngle = Math.Abs(rctCandid[0].Angle - rctCandid[1].Angle);
            if (compareVal(dAngle, 0.0) != 0 ||
                compareVal(dAngle, Math.PI * 0.5) != 0 ||
                compareVal(dAngle, Math.PI) != 0 ||
                compareVal(dAngle, Math.PI * 1.5) != 0 ||
                compareVal(dAngle, Math.PI * 2.0) != 0) return null;
            dAngle = Math.Abs(rctCandid[1].Angle - rctCandid[2].Angle);
            if (compareVal(dAngle, 0.0) != 0 ||
                compareVal(dAngle, Math.PI * 0.5) != 0 ||
                compareVal(dAngle, Math.PI) != 0 ||
                compareVal(dAngle, Math.PI * 1.5) != 0 ||
                compareVal(dAngle, Math.PI * 2.0) != 0) return null;
            dAngle = Math.Abs(rctCandid[2].Angle - rctCandid[0].Angle);
            if (compareVal(dAngle, 0.0) != 0 ||
                compareVal(dAngle, Math.PI * 0.5) != 0 ||
                compareVal(dAngle, Math.PI) != 0 ||
                compareVal(dAngle, Math.PI * 1.5) != 0 ||
                compareVal(dAngle, Math.PI * 2.0) != 0) return null;

            return rctCandid;
        }

        /// <summary>
        /// ３つの正方形の位置関係より、補正角度を求める
        /// </summary>
        /// <param name="rctRot"></param>
        /// <remarks>正方形が３つあること、３つの正方形の大きさが等しいこと、同一方向に傾いていることは保証済であること</remarks>
        /// <returns></returns>
        private double getRotAngle(List<RotatedRect> rctRot)
        {
            double angle = double.NaN; // 補正角をradianで返す
            double rightAngle;

            // 【検証】
            // ３つの正方形の中心を結ぶ正三角形は直角二等辺三角形である
            // 【補正角度】
            // 直角となる頂点が右上になるように回転する
            double len01 = Math.Sqrt(
                Math.Pow(rctRot[0].Center.X - rctRot[1].Center.X, 2.0) +
                Math.Pow(rctRot[0].Center.Y - rctRot[1].Center.Y, 2.0));
            double len12 = Math.Sqrt(
                Math.Pow(rctRot[1].Center.X - rctRot[2].Center.X, 2.0) +
                Math.Pow(rctRot[1].Center.Y - rctRot[2].Center.Y, 2.0));
            double len20 = Math.Sqrt(
                Math.Pow(rctRot[2].Center.X - rctRot[0].Center.X, 2.0) +
                Math.Pow(rctRot[2].Center.Y - rctRot[0].Center.Y, 2.0));

            if (compareVal(len01, len20) == 0 && compareVal(len01, len20 * Math.Sqrt(2)) == 0)
            {
                // 要素０で直角（右上）
                rightAngle = getAngle(rctRot[0].Center, rctRot[1].Center, rctRot[2].Center);
                if (compareVal(rightAngle, Math.PI * 0.5) == 0)
                {
                    // 求めた角が90°
                    // 要素１：左上、要素２：右下
                    // [0][1]
                    // [2]
                    // 要素０→要素１へのベクトルを水平になるように補正する
                    angle = -getAngle(rctRot[0].Center, rctRot[1].Center);
                }
                else
                {
                    // 求めた角が270°
                    // 要素２：左上、要素１：右下
                    // [0][2]
                    // [1]
                    // 要素０→要素２へのベクトルを水平になるように補正する
                    angle = -getAngle(rctRot[0].Center, rctRot[2].Center);
                }
            }
            else if (compareVal(len01, len12) == 0 && compareVal(len01, len20 * Math.Sqrt(2)) == 0)
            {
                // 要素１で直角（右上）
                // 以下、同様に処理する
                rightAngle = getAngle(rctRot[1].Center, rctRot[0].Center, rctRot[2].Center);
                if (compareVal(rightAngle, Math.PI * 0.5) == 0)
                {
                    angle = -getAngle(rctRot[1].Center, rctRot[0].Center);
                }
                else
                {
                    angle = -getAngle(rctRot[1].Center, rctRot[2].Center);
                }
            }
            else if (compareVal(len01, len12) == 0 && compareVal(len01, len20 * Math.Sqrt(2)) == 0)
            {
                // 要素２で直角（右上）
                // 以下、同様に処理する
                rightAngle = getAngle(rctRot[2].Center, rctRot[0].Center, rctRot[1].Center);
                if (compareVal(rightAngle, Math.PI * 0.5) == 0)
                {
                    angle = -getAngle(rctRot[2].Center, rctRot[0].Center);
                }
                else
                {
                    angle = -getAngle(rctRot[2].Center, rctRot[1].Center);
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
        /// </summary>
        /// <param name="matOrg"></param>
        /// <param name="deg"></param>
        /// <returns></returns>
        private Mat rotMat(Mat matOrg, List<RotatedRect> rctRot)
        {
            Mat matRot = new Mat();

            // 傾き角、回転中央座標を算出
            // ※各位置検出パターンの角度は（傾き角度×－１）となっている
            //double angle = (rctRot[0].Angle + rctRot[1].Angle + rctRot[2].Angle) / 3.0;
            double angle = getRotAngle(rctRot);
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
        private bool chkFinderPtn(int[] finderPtnAry)
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
        /// ３つの位置検出パターンの位置関係を求めて、回転する
        /// </summary>
        /// <returns></returns>
        private bool correctFinderPtnPos()
        {
            int[,] idx = new int[,]{
            {0,1,2},{0,2,1},{1,0,2},{1,2,0},{2,0,1},{2,1,0}
            }; // 全組み合わせ

            for (int loopCnt = 0; loopCnt < idx.GetLength(0) * idx.GetLength(1); loopCnt++)
            {
                int rotResult = execCorrectFinderPtn(idx[loopCnt, 0], idx[loopCnt, 1], idx[loopCnt, 2]);
                if (rotResult == 0)
                {
                    // 位置検出パターンが確定したので、QR画像右上座標、縦横画素数を取得
                    qrTop = rctFinderPtn[idx[loopCnt, 0]].Top;
                    qrLeft = rctFinderPtn[idx[loopCnt, 0]].Left;
                    qrPxl = rctFinderPtn[idx[loopCnt, 1]].Left + rctFinderPtn[idx[loopCnt, 1]].Width - qrLeft;
                    return true;
                }
                else if (rotResult == -1)
                {
                    return false;
                }
                // 1が返ってきた場合、次の組み合わせへ
            }
            return false;
        }

        /// <summary>
        /// ３つの位置検出パターンの位置関係を求めて、回転する(実処理)
        /// 位置検出パターン自体も回転する
        /// </summary>
        /// <param name="i0"></param>
        /// <param name="i1"></param>
        /// <param name="i2"></param>
        /// <returns>傾き補正は済んでいるので、90dec毎の回転のみ</returns>
        private int execCorrectFinderPtn(int i0, int i1, int i2)
        {
            // 3つのパターンの位置関係を確認する(左上原点)

            // [0][1]
            // [2][-]
            if (compareVal(rctFinderPtn[i0].Y, rctFinderPtn[i1].Y) == 0 &&
                compareVal(rctFinderPtn[i0].X, rctFinderPtn[i2].X) == 0 &&
                compareVal(rctFinderPtn[i0].X, rctFinderPtn[i1].X) == -1 &&
                compareVal(rctFinderPtn[i0].Y, rctFinderPtn[i2].Y) == -1)
            {
                if (compareVal(rctFinderPtn[i1].Y - rctFinderPtn[i0].Y, rctFinderPtn[i2].X - rctFinderPtn[i0].X) != 0) return -1;
                return 0; // 回転不要
            }
            // [-][2]       [0][1]
            // [1][0]→rot→[2][-]
            else if (compareVal(rctFinderPtn[i0].X, rctFinderPtn[i2].X) == 0 &&
                compareVal(rctFinderPtn[i0].Y, rctFinderPtn[i1].Y) == 0 &&
                compareVal(rctFinderPtn[i0].Y, rctFinderPtn[i2].Y) == 1 &&
                compareVal(rctFinderPtn[i0].X, rctFinderPtn[i1].X) == 1)
            {
                if (compareVal(rctFinderPtn[i0].X - rctFinderPtn[i1].X, rctFinderPtn[i0].Y - rctFinderPtn[i2].Y) != 0) return -1;
                // 180deg 回転(X軸回転＋Y軸回転)
                using (Mat workMat = new Mat())
                {
                    Cv2.Flip(matQr, workMat, FlipMode.XY);
                    matQr = workMat.Clone();
                }
                rctFinderPtn[i0] = new Rect(rctFinderPtn[i1].X, rctFinderPtn[i2].Y, rctFinderPtn[i0].Width, rctFinderPtn[i0].Height);
               
                return 0;
            }
            // [2][0]       [0][1]
            // [-][1]→rot→[2][-]
            else if (compareVal(rctFinderPtn[i0].X, rctFinderPtn[i1].X) == 0 &&
                compareVal(rctFinderPtn[i0].Y, rctFinderPtn[i2].Y) == 0 &&
                compareVal(rctFinderPtn[i0].Y, rctFinderPtn[i1].Y) == -1 &&
                compareVal(rctFinderPtn[i0].X, rctFinderPtn[i2].X) == 1)
            {
                if (compareVal(rctFinderPtn[i0].Y - rctFinderPtn[i1].Y, rctFinderPtn[i2].X - rctFinderPtn[i0].X) != 0) return -1;
                // 90deg 回転(転置＋X軸回転)
                using (Mat workMat = new Mat())
                {
                    Cv2.Flip(matQr.Transpose(), workMat, FlipMode.X);
                    matQr = workMat.Clone();
                }
                rctFinderPtn[i2] = new Rect(rctFinderPtn[i2].X, rctFinderPtn[i1].Y, rctFinderPtn[i2].Width, rctFinderPtn[i2].Height);
                rctFinderPtn[i1] = new Rect(rctFinderPtn[i1].X, rctFinderPtn[i0].Y, rctFinderPtn[i1].Width, rctFinderPtn[i1].Height);
                rctFinderPtn[i0] = new Rect(rctFinderPtn[i2].X, rctFinderPtn[i0].Y, rctFinderPtn[i0].Width, rctFinderPtn[i0].Height);

                return 0;
            }
            // [1][-]       [0][1]
            // [0][2]→rot→[2][-]
            else if (compareVal(rctFinderPtn[i0].X, rctFinderPtn[i1].X) == 0 &&
                compareVal(rctFinderPtn[i0].Y, rctFinderPtn[i2].Y) == 0 &&
                compareVal(rctFinderPtn[i0].Y, rctFinderPtn[i1].Y) == 1 &&
                compareVal(rctFinderPtn[i0].X, rctFinderPtn[i2].X) == -1)
            {
                if (compareVal(rctFinderPtn[i0].Y - rctFinderPtn[i1].Y, rctFinderPtn[i2].X - rctFinderPtn[i0].X) != 0) return -1;
                //270deg 回転(転置＋Y軸回転)
                using (Mat workMat = new Mat())
                {
                    Cv2.Flip(matQr.Transpose(), workMat, FlipMode.Y);
                    matQr = workMat.Clone();
                }
                rctFinderPtn[i1] = new Rect(rctFinderPtn[i2].X, rctFinderPtn[i1].Y, rctFinderPtn[i1].Width, rctFinderPtn[i1].Height);
                rctFinderPtn[i0] = new Rect(rctFinderPtn[i0].X, rctFinderPtn[i1].Y, rctFinderPtn[i0].Width, rctFinderPtn[i0].Height);
                rctFinderPtn[i2] = new Rect(rctFinderPtn[i0].X, rctFinderPtn[i2].Y, rctFinderPtn[i2].Width, rctFinderPtn[i2].Height);

                return 0;
            }
            else
            {
                return 1; // いずれでもないので、i0～i2を入れ替えて試す
            }
        }

        /// <summary>
        /// 多色画像データをもとに、２値画像データを生成
        /// </summary>
        /// <param name="matColor"></param>
        /// <returns></returns>
        Mat getBinMat(Mat matColor)
        {
            Mat matBin;
            using (Mat matGray = new Mat())
            {
                // 基本仕様 12.a
                // 最大値(=255)、最小値(=0)の中間値を閾値として２値画像を生成する
                Cv2.CvtColor(matColor, matGray, ColorConversionCodes.BGR2GRAY);
                matBin =  matGray.Threshold(255 / 2.0, 255, ThresholdTypes.Binary);
            }

            return matBin;
        }
        #endregion

        #region エラー訂正符号(BCH)
        #endregion

        #region その他
        /// <summary>
        /// モジュール明暗情報を取得
        /// </summary>
        /// <remarks>基本仕様 12.a</remarks>
        private void getModAry()
        {
            modAry = new bool[qrCodeModNum, qrCodeModNum];

            for (int cntX = 0; cntX < qrCodeModNum; cntX++)
            {
                for (int cntY = 0; cntY < qrCodeModNum; cntY++)
                {
                    // 各モジュール画像を縦横１ピクセルの画像に圧縮して、明暗を判別する
                    Rect rctCut = new Rect(qrLeft + cntX * modPxl, qrTop + cntY * modPxl, modPxl, modPxl);
                    Mat rctMat = new Mat(matQr, rctCut);
                    Size sz1px = new Size(1, 1);
                    Mat mat1px = rctMat.Resize(sz1px, 0, 0, InterpolationFlags.Cubic);
                    modAry[cntX, cntY] = (mat1px.At<byte>(0, 0) == 0); // 黒をTrueとする
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
        private int compareVal(int v1, int v2)
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
        private int compareVal(double v1, double v2)
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
        private double getLen(Point2f p1, Point2f p2)
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
        private double getAngle(Point2f p1, Point2f p2, Point2f p3)
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
        private double getAngle(Point2f p1, Point2f p2)
        {
            return Math.Atan2(p2.Y - p1.Y, p2.X - p1.X);
        }
        #endregion
        #endregion
    }
}
