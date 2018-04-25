using System;
using System.Collections.Generic;
using OpenCvSharp;

namespace QRCodeDecoder
{
    public class ClsQRCodeDecorder : IDisposable
    {
        #region 宣言
        #region 列挙型
        private enum lblIdxEnum
        {
            posX = 0,
            posY,
            width,
            height
        }
        private enum scanDirEnum
        {
            dirX,
            dirY
        }
        // エラー番号
        public enum ErrNoEnum : int
        {
            emptyData = 1,
            outOfVer = 2,
            capOver = 3,

            // 内部エラー(バグ？)
            failGetModule = 100,
            failMaskModule = 101,

            normal = 0
        }
        // エラー訂正レベル
        private enum ErrLevelEnum : int
        {
            l = 0,
            m = 1,
            q = 2,
            h = 3,
            na = -1
        }
        // モジュール取得方向
        private enum DirEnum : int
        {
            down = 0,
            up = 1,
            right = 2,
            left = 3
        }
        // モジュール設定値
        private enum ModuleEnum : int
        {
            d0 = 0, // データ明モジュール
            d1 = 1, // データ暗モジュール
            f = 2   // その他、機能パターン(データ符号か領域が取れればよいので、詳細な区分は不要)
        }
        #endregion

        #region 定数
        // 基本仕様 表D.1
        // 型番7以上の型番情報(BCH符号)
        // 有効ビット数：16bit
        // データビット：上位4bit
        // 誤り訂正ビット：下位12bit
        private readonly int[] VerInfoAry = new int[] {
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
        private readonly int[] FmtInfoAry = new int[] {
            0x5412,0x5125,0x5E7C,0x5B4B,0x45F9,0x40CE,0x4F97,0x4AA0,
            0x77C4,0x72F3,0x7DAA,0x789D,0x662F,0x6318,0x6C41,0x6976,
            0x1689,0x13BE,0x1CE7,0x19D0,0x0762,0x0255,0x0D0C,0x083B,
            0x355F,0x3068,0x3F31,0x3A06,0x24B4,0x2183,0x2EDA,0x2BED
        };

        // RSブロックごとのデータ容量(Byte)
        // 誤り訂正レベル(L,M,Q,H)の[第１分割：分割ブロック数・総コード語数・データ容量・誤り訂正に利用可能なコード語数（シンドローム数）],
        //                          [第２分割：分割ブロック数・総コード語数・データ容量・誤り訂正に利用可能なコード語数（シンドローム数）]×型番１～４０
        // 基本仕様 表9をもとに作成

        //--------------------------------第１分割-----------------,  ------------------------------第２分割--------------------
        //----レベルL ------------- レベルM ------------- レベルQ ------------- レベルH -----------
        private readonly int[, ,] RsBlockCapa = {
            {{  1,  26,  19,    4},{  1,  26,  16,    8},{  1,  26,  13,   12},{  1,  26,   9,   16},  // Ver 1 第１分割
             {  0,   0,   0,    0},{  0,   0,   0,    0},{  0,   0,   0,    0},{  0,   0,   0,    0}}, // ver 1 第２分割
            {{  1,  44,  34,    8},{  0,   0,   0,    0},{  0,   0,   0,    0},{  0,   0,   0,    0},
             {  1,  44,  28,   16},{  1,  44,  22,   22},{  1,  44,  16,   28},{  1,  70,  55,   14}},
            {{  1,  70,  55,   14},{  0,   0,   0,    0},{  0,   0,   0,    0},{  0,   0,   0,    0},
             {  1,  70,  44,   26},{  2,  35,  17,   36},{  2,  35,  13,   44},{  1,   0,  80,    0}},
            {{  1,   0,  80,    0},{  2,  50,  32,   36},{  2,  50,  24,   52},{  4,  25,   9,   64},
             {  0,   0,   0,    0},{  0,   0,   0,    0},{  0,   0,   0,    0},{  0,   0,   0,    0}},
            {{  1,  34, 108,   26},{  2,  67,  43,   48},{  2,  33,  15,   72},{  2,  33,  11,   88},
             {  0,   0,   0,    0},{  0,   0,   0,    0},{  2,  34,  16,   72},{  2,  34,  12,   88}},
            {{  2,  86,  68,   36},{  4,  43,  27,   64},{  4,  43,  19,   96},{  4,  43,  15,  112},
             {  0,   0,   0,    0},{  0,   0,   0,    0},{  0,   0,   0,    0},{  0,   0,   0,    0}},
            {{  2,  98,  78,   40},{  0,   0,   0,    0},{  4,  33,  15,  108},{  1,  40,  14,  130},
             {  4,  49,  31,   72},{  2,  32,  14,  108},{  4,  39,  13,  130},{  2, 121,  97,   48}},
            {{  2, 121,  97,   48},{  2,  60,  38,   88},{  4,  40,  18,  132},{  4,  40,  14,  156},
             {  0,   0,   0,    0},{  2,  61,  39,   88},{  2,  41,  19,  132},{  2,  41,  15,  156}},
            {{  2, 146, 116,   60},{  3,  58,  36,  110},{  4,  36,  16,  160},{  4,  36,  12,  192},
             {  0,   0,   0,    0},{  2,  59,  37,  110},{  4,  37,  17,  160},{  4,  37,  13,  192}},
            {{  2,  86,  68,   72},{  4,  69,  43,  130},{  6,  43,  19,  192},{  6,  43,  15,  224},  // Ver 10
             {  2,  87,  69,   72},{  1,  70,  44,  130},{  2,  44,  20,  192},{  2,  44,  16,  224}},
            {{  4, 101,  81,   80},{  1,  80,  50,  150},{  4,  50,  22,  224},{  3,  36,  12,  264},
             {  0,   0,   0,    0},{  4,  81,  51,  150},{  4,  51,  23,  224},{  8,  37,  13,  264}},
            {{  2, 116,  92,   96},{  6,  58,  36,  176},{  4,  46,  20,  260},{  7,  42,  14,  308},
             {  2, 117,  93,   96},{  2,  59,  37,  176},{  6,  47,  21,  260},{  4,  43,  15,  308}},
            {{  4, 133, 107,  104},{  8,  59,  37,  198},{  8,  44,  20,  288},{ 12,  33,  11,  352},
             {  0,   0,   0,    0},{  1,  60,  38,  198},{  4,  45,  21,  288},{  4,  34,  12,  352}},
            {{  3, 145, 115,  120},{  4,  64,  40,  216},{ 11,  36,  16,  320},{ 11,  36,  12,  384},
             {  1, 146, 116,  120},{  5,  65,  41,  216},{  5,  37,  17,  320},{  5,  37,  13,  384}},
            {{  5, 109,  87,  132},{  5,  65,  41,  240},{  5,  54,  24,  360},{ 11,  36,  12,  432},
             {  1, 110,  88,  132},{  5,  66,  42,  240},{  7,  55,  25,  360},{  7,  37,  13,  432}},
            {{  5, 122,  98,  144},{  7,  73,  45,  280},{ 15,  43,  19,  408},{  3,  45,  15,  480},
             {  1, 123,  99,  144},{  3,  74,  46,  280},{  2,  44,  20,  408},{ 13,  46,  16,  480}},
            {{  1, 135, 107,  168},{ 10,  74,  46,  308},{  1,  50,  22,  448},{  2,  42,  14,  532},
             {  5, 136, 108,  168},{  1,  75,  47,  308},{ 15,  51,  23,  448},{ 17,  43,  15,  532}},
            {{  5, 150, 120,  180},{  9,  69,  43,  338},{ 17,  50,  22,  504},{  2,  42,  14,  588},
             {  1, 151, 121,  180},{  4,  70,  44,  338},{  1,  51,  23,  504},{ 19,  43,  15,  588}},
            {{  3, 141, 113,  196},{  3,  70,  44,  364},{ 17,  47,  21,  546},{  9,  39,  13,  650},
             {  4, 142, 114,  196},{ 11,  71,  45,  364},{  4,  48,  22,  546},{ 16,  40,  14,  650}},
            {{  3, 135, 107,  224},{  3,  67,  41,  416},{ 15,  54,  24,  600},{ 15,  43,  15,  700},  // Ver 20
             {  5, 136, 108,  224},{ 13,  68,  42,  416},{  5,  55,  25,  600},{ 10,  44,  16,  700}},
            {{  4, 144, 116,  224},{ 17,  68,  42,  442},{  6,  51,  23,  644},{  6,  47,  17,  750},
             {  4, 145, 117,  224},{ 17,  50,  22,  644},{ 19,  46,  16,  750},{  2, 139, 111,  252}},
            {{  2, 139, 111,  252},{ 17,  74,  46,  476},{  7,  54,  24,  690},{ 34,  37,  13,  816},
             {  7, 140, 112,  252},{  0,   0,   0,    0},{ 16,  55,  25,  690},{  0,   0,   0,    0}},
            {{  4, 151, 121,  270},{  4,  75,  47,  504},{ 11,  54,  24,  750},{ 16,  45,  15,  900},
             {  5, 152, 122,  270},{ 14,  76,  48,  504},{ 14,  55,  25,  750},{ 14,  46,  16,  900}},
            {{  6, 147, 117,  300},{  6,  73,  45,  560},{ 11,  54,  24,  810},{ 30,  46,  16,  960},
             {  4, 148, 118,  300},{ 14,  74,  46,  560},{ 16,  55,  25,  810},{  2,  47,  17,  960}},
            {{  8, 132, 106,  312},{  8,  75,  47,  588},{  7,  54,  24,  870},{ 22,  45,  15, 1050},
             {  4, 133, 107,  312},{ 13,  76,  48,  588},{ 22,  55,  25,  870},{ 13,  46,  16, 1050}},
            {{ 13,  46,  16, 1050},{  2, 143, 115,  336},{  4,  75,  47,  644},{  6,  51,  23,  952},
             { 10, 142, 114,  336},{ 19,  74,  46,  644},{ 28,  50,  22,  952},{ 33,  46,  16, 1110}},
            {{  8, 152, 122,  360},{ 22,  73,  45,  700},{  8,  53,  23, 1020},{ 12,  45,  15, 1200},
             {  4, 153, 123,  360},{  3,  74,  46,  700},{ 26,  54,  24, 1020},{ 28,  46,  16, 1200}},
            {{  3, 147, 117,  390},{  3,  73,  45,  728},{  4,  54,  24, 1050},{ 11,  45,  15, 1260},
             { 10, 148, 118,  390},{ 23,  74,  46,  728},{ 31,  55,  25, 1050},{ 31,  46,  16, 1260}},
            {{  7, 146, 116,  420},{ 21,  73,  45,  784},{  1,  53,  23, 1140},{ 19,  45,  15, 1350},
             {  7, 147, 117,  420},{  7,  74,  46,  784},{ 37,  54,  24, 1140},{ 26,  46,  16, 1350}},
            {{  5, 145, 115,  450},{ 19,  75,  47,  812},{ 15,  54,  24, 1200},{ 23,  45,  15, 1440},  // Ver 30
             { 10, 146, 116,  450},{ 10,  76,  48,  812},{ 25,  55,  25, 1200},{ 25,  46,  16, 1440}},
            {{ 13, 145, 115,  480},{  2,  74,  46,  868},{ 42,  54,  24, 1290},{ 23,  45,  15, 1530},
             {  3, 146, 116,  480},{ 29,  75,  47,  868},{  1,  55,  25, 1290},{ 28,  46,  16, 1530}},
            {{ 17, 145, 115,  510},{ 23,  75,  47,  924},{ 35,  55,  25, 1350},{ 35,  46,  16, 1620},
             { 10,  74,  46,  924},{ 10,  54,  24, 1350},{ 19,  45,  15, 1620},{ 17, 145, 115,  540}},
            {{ 17, 145, 115,  540},{ 14,  74,  46,  980},{ 29,  54,  24, 1440},{ 11,  45,  15, 1710},
             {  1, 146, 116,  540},{ 21,  75,  47,  980},{ 19,  55,  25, 1440},{ 46,  46,  16, 1710}},
            {{ 13, 145, 115,  570},{ 14,  74,  46, 1036},{ 44,  54,  24, 1530},{ 59,  46,  16, 1800},
             {  6, 146, 116,  570},{ 23,  75,  47, 1036},{  7,  55,  25, 1530},{  1,  47,  17, 1800}},
            {{ 12, 151, 121,  570},{ 12,  75,  47, 1064},{ 39,  54,  24, 1590},{ 22,  45,  15, 1890},
             {  7, 152, 122,  570},{ 26,  76,  48, 1064},{ 14,  55,  25, 1590},{ 41,  46,  16, 1890}},
            {{  6, 151, 121,  600},{  6,  75,  47, 1120},{ 46,  54,  24, 1680},{  2,  45,  15, 1980},
             { 14, 152, 122,  600},{ 34,  76,  48, 1120},{ 10,  55,  25, 1680},{ 64,  46,  16, 1980}},
            {{ 17, 152, 122,  630},{ 29,  74,  46, 1204},{ 49,  54,  24, 1770},{ 24,  45,  15, 2100},
             {  4, 153, 123,  630},{ 14,  75,  47, 1204},{ 10,  55,  25, 1770},{ 46,  46,  16, 2100}},
            {{  4, 152, 122,  660},{ 13,  74,  46, 1260},{ 48,  54,  24, 1860},{ 42,  45,  15, 2220},
             { 18, 153, 123,  660},{ 32,  75,  47, 1260},{ 14,  55,  25, 1860},{ 32,  46,  16, 2220}},
            {{ 20, 147, 117,  720},{ 40,  75,  47, 1316},{ 43,  54,  24, 1950},{ 10,  45,  15, 2310},
             {  4, 148, 118,  720},{  7,  76,  48, 1316},{ 22,  55,  25, 1950},{ 67,  46,  16, 2310}},
            {{ 19, 148, 118,  750},{ 18,  75,  47, 1372},{ 34,  54,  24, 2040},{ 20,  45,  15, 2430},  // Ver 40
             {  6, 149, 119,  750},{ 31,  76,  48, 1372},{ 34,  55,  25, 2040},{ 61,  46,  16, 2430}}
        };

        // 型番ごとの位置合せパターン設置個所
        // 附属書Eには中心座標が記載されているが、実装には使いづらいので左上の座標を記載した
        // ※位置検出パターンと重なる箇所があることに注意
        private readonly int[][] AdjPosPtn = {
            new int[] {},                              // Ver 1(位置合せパターンなし)
            new int[] {4, 16},                         // Ver 2
            new int[] {4, 20},                         // Ver 3
            new int[] {4, 24},
            new int[] {4, 28},
            new int[] {4, 32},
            new int[] {4, 20, 36},
            new int[] {4, 22, 40},
            new int[] {4, 24, 44},
            new int[] {4, 26, 48},                     // Ver 10
            new int[] {4, 28, 52},
            new int[] {4, 30, 56},
            new int[] {4, 32, 60},
            new int[] {4, 24, 44,  64},
            new int[] {4, 24, 46,  68},
            new int[] {4, 24, 48,  72},
            new int[] {4, 28, 52,  76},
            new int[] {4, 28, 54,  80},
            new int[] {4, 28, 56,  84},
            new int[] {4, 32, 60,  88},                // Ver 20
            new int[] {4, 26, 48,  92, 70},
            new int[] {4, 24, 48,  96, 72},
            new int[] {4, 28, 52, 100, 76},
            new int[] {4, 26, 52, 104, 78},
            new int[] {4, 30, 56, 108, 82},
            new int[] {4, 28, 56, 112, 84},
            new int[] {4, 32, 60, 116, 88},
            new int[] {4, 24, 48,  96, 72, 120},
            new int[] {4, 28, 52, 100, 76, 124},
            new int[] {4, 24, 50, 102, 76, 128},       // Ver 30
            new int[] {4, 28, 54, 106, 80, 132},
            new int[] {4, 32, 58, 110, 84, 136},
            new int[] {4, 28, 56, 112, 84, 140},
            new int[] {4, 32, 60, 116, 88, 144},
            new int[] {4, 28, 52, 100, 76, 124, 148},
            new int[] {4, 22, 48, 100, 74, 126, 152},
            new int[] {4, 26, 52, 104, 78, 130, 156},
            new int[] {4, 30, 56, 108, 82, 134, 160},
            new int[] {4, 24, 52, 108, 80, 136, 164},
            new int[] {4, 28, 56, 112, 84, 140, 168}   // Ver 40
        };

        // 型番ごとの位置合せパターン設置個所
        // 附属書Eを参考に、実際に設置されるパターン位置の左上座標を記載した
        private readonly int[][][] AdjPosPtn2 =
        {
            new int[][] {}, // Ver 1
            new int[][] { new int[] {16, 16}},
            new int[][] { new int[] {20, 20}},
            new int[][] { new int[] {24, 24}},
            new int[][] { new int[] {28, 28}},
            new int[][] { new int[] {32, 32}},
            new int[][] { new int[] { 4, 20}, new int[] {20,  4}, new int[] {20, 20}, new int[] {20,  36}, new int[] {36, 20}, new int[] {36, 36}},
            new int[][] { new int[] { 4, 22}, new int[] {22,  4}, new int[] {22, 22}, new int[] {22,  40}, new int[] {40, 22}, new int[] {40, 40}},
            new int[][] { new int[] { 4, 24}, new int[] {24,  4}, new int[] {24, 24}, new int[] {24,  44}, new int[] {44, 24}, new int[] {44, 44}},
            new int[][] { new int[] { 4, 26}, new int[] {26,  4}, new int[] {26, 26}, new int[] {26,  48}, new int[] {48, 26}, new int[] {48, 48}}, // Ver 10
            new int[][] { new int[] { 4, 28}, new int[] {28,  4}, new int[] {28, 28}, new int[] {28,  52}, new int[] {52, 28}, new int[] {52, 52}},
            new int[][] { new int[] { 4, 30}, new int[] {30,  4}, new int[] {30, 30}, new int[] {30,  56}, new int[] {56, 30}, new int[] {56, 56}},
            new int[][] { new int[] { 4, 32}, new int[] {32,  4}, new int[] {32, 32}, new int[] {32,  60}, new int[] {60, 32}, new int[] {60, 60}},
            new int[][] { new int[] { 4, 24}, new int[] { 4, 44}, new int[] {24,  4}, new int[] {24,  24}, new int[] {24, 44}, new int[] {24, 64},
                          new int[] {44,  4}, new int[] {44, 24}, new int[] {44, 44}, new int[] {44,  64}, new int[] {64, 24}, new int[] {64, 44}, new int[] {64,  64}},
            new int[][] { new int[] { 4, 24}, new int[] { 4, 46}, new int[] {24,  4}, new int[] {24,  24}, new int[] {24, 46}, new int[] {24, 68},
                          new int[] {46,  4}, new int[] {46, 24}, new int[] {46, 46}, new int[] {46,  68}, new int[] {68, 24}, new int[] {68, 46}, new int[] {68,  68}},
            new int[][] { new int[] { 4, 24}, new int[] { 4, 48}, new int[] {24,  4}, new int[] {24,  24}, new int[] {24, 48}, new int[] {24, 72},
                          new int[] {48,  4}, new int[] {48, 24}, new int[] {48, 48}, new int[] {48,  72}, new int[] {72, 24}, new int[] {72, 48}, new int[] {72, 72}},
            new int[][] { new int[] { 4, 28}, new int[] { 4, 52}, new int[] {28,  4}, new int[] {28,  28}, new int[] {28, 52}, new int[] {28, 76},
                          new int[] {52,  4}, new int[] {52, 28}, new int[] {52, 52}, new int[] {52,  76}, new int[] {76, 28}, new int[] {76, 52}, new int[] {76,  76}},
            new int[][] { new int[] { 4, 28}, new int[] { 4, 54}, new int[] {28,  4}, new int[] {28,  28}, new int[] {28, 54}, new int[] {28, 80},
                          new int[] {54,  4}, new int[] {54, 28}, new int[] {54, 54}, new int[] {54,  80}, new int[] {80, 28}, new int[] {80, 54}, new int[] {80,  80}},
            new int[][] { new int[] { 4, 28}, new int[] { 4, 56}, new int[] {28,  4}, new int[] {28,  28}, new int[] {28, 56}, new int[] {28, 84},
                          new int[] {56,  4}, new int[] {56, 28}, new int[] {56, 56}, new int[] {56,  84}, new int[] {84, 28}, new int[] {84, 56}, new int[] {84,  84}},
            new int[][] { new int[] { 4, 32}, new int[] { 4, 60}, new int[] {32,  4}, new int[] {32,  32}, new int[] {32, 60}, new int[] {32, 88},
                          new int[] {60,  4}, new int[] {60, 32}, new int[] {60, 60}, new int[] {60,  88}, new int[] {88, 32}, new int[] {88, 60}, new int[] {88,  88}}, // Ver 20
            new int[][] { new int[] { 4, 26}, new int[] { 4, 48}, new int[] { 4, 70}, new int[] {26,   4}, new int[] {26, 26}, new int[] {26, 48},
                          new int[] {26, 70}, new int[] {26, 92}, new int[] {48,  4}, new int[] {48,  26}, new int[] {48, 48}, new int[] {48, 70},
                          new int[] {48, 92}, new int[] {70,  4}, new int[] {70, 26}, new int[] {70,  48}, new int[] {70, 70}, new int[] {70, 92},
                          new int[] {92, 26}, new int[] {92, 48}, new int[] {92, 70}, new int[] {92,  92}},
            new int[][] { new int[] { 4, 24}, new int[] { 4, 48}, new int[] { 4, 72}, new int[] {24,   4}, new int[] {24, 24}, new int[] {24, 48},
                          new int[] {24, 72}, new int[] {24, 96}, new int[] {48,  4}, new int[] {48,  24}, new int[] {48, 48}, new int[] {48, 72},
                          new int[] {48, 96}, new int[] {72,  4}, new int[] {72, 24}, new int[] {72,  48}, new int[] {72, 72}, new int[] {72, 96},
                          new int[] {96, 24}, new int[] {96, 48}, new int[] {96, 72}, new int[] {96,  96}},
            new int[][] { new int[] { 4, 28}, new int[] { 4, 52}, new int[] { 4, 76}, new int[] {28,   4}, new int[] {28, 28}, new int[] {28, 52},
                          new int[] {28, 76}, new int[] {28,100}, new int[] {52,  4}, new int[] {52,  28}, new int[] {52, 52}, new int[] {52, 76},
                          new int[] {52,100}, new int[] {76,  4}, new int[] {76, 28}, new int[] {76,  52}, new int[] {76, 76}, new int[] {76,100},
                          new int[] {100,28}, new int[] {100,52}, new int[] {100,76}, new int[] {100,100}},
            new int[][] { new int[] { 4, 26}, new int[] { 4, 52}, new int[] { 4, 78}, new int[] {26,   4}, new int[] {26, 26}, new int[] {26, 52},
                          new int[] {26, 78}, new int[] {26,104}, new int[] {52,  4}, new int[] {52,  26}, new int[] {52, 52}, new int[] {52, 78},
                          new int[] {52,104}, new int[] {78,  4}, new int[] {78, 26}, new int[] {78,  52}, new int[] {78, 78}, new int[] {78,104},
                          new int[] {104,26}, new int[] {104,52}, new int[] {104,78}, new int[] {104,104}},
            new int[][] { new int[] { 4, 30}, new int[] { 4, 56}, new int[] { 4, 82}, new int[] {30,   4}, new int[] {30, 30}, new int[] {30, 56},
                          new int[] {30, 82}, new int[] {30,108}, new int[] {56,  4}, new int[] {56,  30}, new int[] {56, 56}, new int[] {56, 82},
                          new int[] {56,108}, new int[] {82,  4}, new int[] {82, 30}, new int[] {82,  56}, new int[] {82, 82}, new int[] {82,108},
                          new int[] {108,30}, new int[] {108,56}, new int[] {108,82}, new int[] {108,108}},
            new int[][] { new int[] { 4, 28}, new int[] { 4, 56}, new int[] { 4, 84}, new int[] {28,   4}, new int[] {28, 28}, new int[] {28, 56},
                          new int[] {28, 84}, new int[] {28,112}, new int[] {56,  4}, new int[] {56,  28}, new int[] {56, 56}, new int[] {56, 84},
                          new int[] {56,112}, new int[] {84,  4}, new int[] {84, 28}, new int[] {84,  56}, new int[] {84, 84}, new int[] {84,112},
                          new int[] {112,28}, new int[] {112,56}, new int[] {112,84}, new int[] {112,112}},
            new int[][] { new int[] { 4, 32}, new int[] { 4, 60}, new int[] { 4, 88}, new int[] {32,   4}, new int[] {32, 32}, new int[] {32, 60},
                          new int[] {32, 88}, new int[] {32,116}, new int[] {60,  4}, new int[] {60,  32}, new int[] {60, 60}, new int[] {60, 88},
                          new int[] {60,116}, new int[] {88,  4}, new int[] {88, 32}, new int[] {88,  60}, new int[] {88, 88}, new int[] {88,116},
                          new int[] {116,32}, new int[] {116,60}, new int[] {116,88}, new int[] {116,116}},
            new int[][] { new int[] { 4, 24}, new int[] { 4, 48}, new int[] { 4, 72}, new int[] { 4,  96}, new int[] {24,  4}, new int[] {24, 24},
                          new int[] {24, 48}, new int[] {24, 72}, new int[] {24, 96}, new int[] {24, 120}, new int[] {48,  4}, new int[] {48, 24},
                          new int[] {48, 48}, new int[] {48, 72}, new int[] {48, 96}, new int[] {48, 120}, new int[] {72,  4}, new int[] {72, 24},
                          new int[] {72, 48}, new int[] {72, 72}, new int[] {72, 96}, new int[] {72, 120}, new int[] {96,  4}, new int[] {96, 24},
                          new int[] {96, 48}, new int[] {96, 72}, new int[] {96, 96}, new int[] {96, 120}, new int[] {120,24}, new int[] {120,48},
                          new int[] {120,72}, new int[] {120,96}, new int[] {120,120}},
            new int[][] { new int[] { 4, 28}, new int[] { 4, 52}, new int[] { 4, 76}, new int[] { 4, 100}, new int[] {28,  4}, new int[] {28, 28},
                          new int[] {28, 52}, new int[] {28, 76}, new int[] {28,100}, new int[] {28, 124}, new int[] {52,  4}, new int[] {52, 28},
                          new int[] {52, 52}, new int[] {52, 76}, new int[] {52,100}, new int[] {52, 124}, new int[] {76,  4}, new int[] {76, 28},
                          new int[] {76, 52}, new int[] {76, 76}, new int[] {76,100}, new int[] {76, 124}, new int[] {100, 4}, new int[] {100,28},
                          new int[] {100,52}, new int[] {100,76}, new int[] {100,100},new int[] {100,124}, new int[] {124,28}, new int[] {124,52},
                          new int[] {124,76}, new int[] {124,100},new int[] {124,124}},
            new int[][] { new int[] { 4, 24}, new int[] { 4, 50}, new int[] { 4, 76}, new int[] { 4, 102}, new int[] {24,  4}, new int[] {24, 24}, // Ver 30
                          new int[] {24, 50}, new int[] {24, 76}, new int[] {24,102}, new int[] {24, 128}, new int[] {50,  4}, new int[] {50, 24},
                          new int[] {50, 50}, new int[] {50, 76}, new int[] {50,102}, new int[] {50, 128}, new int[] {76,  4}, new int[] {76, 24},
                          new int[] {76, 50}, new int[] {76, 76}, new int[] {76,102}, new int[] {76, 128}, new int[] {102, 4}, new int[] {102,24},
                          new int[] {102,50}, new int[] {102,76}, new int[] {102,102},new int[] {102,128}, new int[] {128,24}, new int[] {128,50},
                          new int[] {128,76}, new int[] {128,102},new int[] {128,128}},
            new int[][] { new int[] { 4, 28}, new int[] { 4, 54}, new int[] { 4, 80}, new int[] { 4, 106}, new int[] {28,  4}, new int[] {28, 28},
                          new int[] {28, 54}, new int[] {28, 80}, new int[] {28,106}, new int[] {28, 132}, new int[] {54,  4}, new int[] {54, 28},
                          new int[] {54, 54}, new int[] {54, 80}, new int[] {54,106}, new int[] {54, 132}, new int[] {80,  4}, new int[] {80, 28},
                          new int[] {80, 54}, new int[] {80, 80}, new int[] {80,106}, new int[] {80, 132}, new int[] {106, 4}, new int[] {106,28},
                          new int[] {106,54}, new int[] {106,80}, new int[] {106,106},new int[] {106,132}, new int[] {132,28}, new int[] {132,54},
                          new int[] {132,80}, new int[] {132,106},new int[] {132,132}},
            new int[][] { new int[] { 4, 32}, new int[] { 4, 58}, new int[] { 4, 84}, new int[] { 4, 110}, new int[] {32,  4}, new int[] {32, 32},
                          new int[] {32, 58}, new int[] {32, 84}, new int[] {32,110}, new int[] {32, 136}, new int[] {58,  4}, new int[] {58, 32},
                          new int[] {58, 58}, new int[] {58, 84}, new int[] {58,110}, new int[] {58, 136}, new int[] {84,  4}, new int[] {84, 32},
                          new int[] {84, 58}, new int[] {84, 84}, new int[] {84,110}, new int[] {84, 136}, new int[] {110, 4}, new int[] {110,32},
                          new int[] {110,58}, new int[] {110,84}, new int[] {110,110},new int[] {110,136}, new int[] {136,32}, new int[] {136,58},
                          new int[] {136,84}, new int[] {136,110},new int[] {136,136}},
            new int[][] { new int[] { 4, 28}, new int[] { 4, 56}, new int[] { 4, 84}, new int[] { 4, 112}, new int[] {28,  4}, new int[] {28, 28},
                          new int[] {28, 56}, new int[] {28,  84},new int[] {28,112}, new int[] {28, 140}, new int[] {56,  4}, new int[] {56, 28},
                          new int[] {56, 56}, new int[] {56, 84}, new int[] {56,112}, new int[] {56, 140}, new int[] {84,  4}, new int[] {84, 28},
                          new int[] {84, 56}, new int[] {84, 84}, new int[] {84,112}, new int[] {84, 140}, new int[] {112, 4}, new int[] {112,28},
                          new int[] {112,56}, new int[] {112,84}, new int[] {112,112},new int[] {112,140}, new int[] {140,28}, new int[] {140,56},
                          new int[] {140,84}, new int[] {140,112},new int[] {140,140}},
            new int[][] { new int[] { 4, 32}, new int[] { 4, 60}, new int[] { 4, 88}, new int[] { 4, 116}, new int[] {32,  4}, new int[] {32, 32},
                          new int[] {32, 60}, new int[] {32,  88},new int[] {32, 116},new int[] {32, 144}, new int[] {60,  4}, new int[] {60, 32},
                          new int[] {60, 60}, new int[] {60, 88}, new int[] {60,116}, new int[] {60,144},  new int[] {88,  4}, new int[] {88, 32},
                          new int[] { 88,60}, new int[] {88, 88}, new int[] {88,116}, new int[] {88, 144}, new int[] {116, 4}, new int[] {116,32},
                          new int[] {116,60}, new int[] {116, 88},new int[] {116,116},new int[] {116,144}, new int[] {144,32}, new int[] {144,60},
                          new int[] {144,88}, new int[] {144,116},new int[] {144,144}},
            new int[][] { new int[] { 4, 28}, new int[] { 4, 52}, new int[] { 4, 76}, new int[] { 4, 100}, new int[] { 4,124}, new int[] {28,  4},
                          new int[] {28, 28}, new int[] {28,  52},new int[] {28, 76}, new int[] {28, 100}, new int[] {28,124}, new int[] {28,148},
                          new int[] {52,  4}, new int[] {52, 28}, new int[] {52, 52}, new int[] {52,  76}, new int[] {52,100}, new int[] {52,124},
                          new int[] {52,148}, new int[] {76,  4}, new int[] {76, 28}, new int[] {76,  52}, new int[] {76, 76}, new int[] {76,100},
                          new int[] {76,124}, new int[] {76,148}, new int[] {100, 4}, new int[] {100, 28}, new int[] {100,52}, new int[] {100,76},
                          new int[] {100,100},new int[] {100,124},new int[] {100,148},new int[] {124,  4}, new int[] {124,28}, new int[] {124,52},
                          new int[] {124,76}, new int[] {124,100},new int[] {124,124},new int[] {124,148}, new int[] {148,28}, new int[] {148,52},
                          new int[] {148,76}, new int[] {148,100},new int[] {148,124},new int[] {148,148}},
            new int[][] { new int[] { 4, 22}, new int[] { 4, 48}, new int[] { 4, 74}, new int[] { 4, 100}, new int[] { 4,126}, new int[] {22,  4},
                          new int[] {22, 22}, new int[] {22, 48}, new int[] {22, 74}, new int[] {22, 100}, new int[] {22,126}, new int[] {22,152},
                          new int[] {48,  4}, new int[] {48, 22}, new int[] {48, 48}, new int[] {48,  74}, new int[] {48,100}, new int[] {48,126},
                          new int[] {48,152}, new int[] {74,  4}, new int[] {74, 22}, new int[] {74,  48}, new int[] {74, 74}, new int[] {74,100},
                          new int[] {74,126}, new int[] {74, 152},new int[] {100, 4}, new int[] {100, 22}, new int[] {100,48}, new int[] {100,74},
                          new int[] {100,100},new int[] {100,126},new int[] {100,152},new int[] {126,  4}, new int[] {126,22}, new int[] {126,48},
                          new int[] {126,74}, new int[] {126,100},new int[] {126,126},new int[] {126,152}, new int[] {152,22}, new int[] {152,48},
                          new int[] {152,74}, new int[] {152,100},new int[] {152,126},new int[] {152,152}},
            new int[][] { new int[] { 4, 26}, new int[] { 4, 52}, new int[] { 4, 78}, new int[] { 4, 104}, new int[] { 4,130}, new int[] {26,  4},
                          new int[] {26, 26}, new int[] {26, 52}, new int[] {26, 78}, new int[] {26, 104}, new int[] {26,130}, new int[] {26,156},
                          new int[] {52,  4}, new int[] {52, 26}, new int[] {52, 52}, new int[] {52,  78}, new int[] {52,104}, new int[] {52,130},
                          new int[] {52,156}, new int[] {78,  4}, new int[] {78, 26}, new int[] {78,  52}, new int[] {78, 78}, new int[] {78,104},
                          new int[] {78,130}, new int[] {78,156}, new int[] {104, 4}, new int[] {104, 26}, new int[] {104,52}, new int[] {104,78},
                          new int[] {104,104},new int[] {104,130},new int[] {104,156},new int[] {130,  4}, new int[] {130,26}, new int[] {130,52},
                          new int[] {130,78}, new int[] {130,104},new int[] {130,130},new int[] {130,156}, new int[] {156,26}, new int[] {156,52},
                          new int[] {156,78}, new int[] {156,104},new int[] {156,130},new int[] {156,156}},
            new int[][] { new int[] { 4, 30}, new int[] { 4, 56}, new int[] { 4, 82}, new int[] { 4, 108}, new int[] { 4,134}, new int[] {30,  4},
                          new int[] {30, 30}, new int[] {30, 56}, new int[] {30, 82}, new int[] {30, 108}, new int[] {30,134}, new int[] {30,160},
                          new int[] {56,  4}, new int[] {56, 30}, new int[] {56, 56}, new int[] {56,  82}, new int[] {56,108}, new int[] {56,134},
                          new int[] {56,160}, new int[] {82,  4}, new int[] {82, 30}, new int[] {82,  56}, new int[] {82, 82}, new int[] {82,108},
                          new int[] {82,134}, new int[] {82,160}, new int[] {108, 4}, new int[] {108, 30}, new int[] {108,56}, new int[] {108,82},
                          new int[] {108,108},new int[] {108,134},new int[] {108,160},new int[] {134,  4}, new int[] {134,30}, new int[] {134,56},
                          new int[] {134,82}, new int[] {134,108},new int[] {134,134},new int[] {134,160}, new int[] {160,30}, new int[] {160,56},
                          new int[] {160,82}, new int[] {160,108},new int[] {160,134},new int[] {160,160}},
            new int[][] { new int[] { 4, 24}, new int[] { 4, 52}, new int[] { 4, 80}, new int[] { 4, 108}, new int[] { 4,136}, new int[] {24,  4},
                          new int[] {24, 24}, new int[] {24, 52}, new int[] {24, 80}, new int[] {24, 108}, new int[] {24,136}, new int[] {24,164},
                          new int[] {52,  4}, new int[] {52, 24}, new int[] {52, 52}, new int[] {52,  80}, new int[] {52,108}, new int[] {52,136},
                          new int[] {52,164}, new int[] {80,  4}, new int[] {80, 24}, new int[] {80,  52}, new int[] {80, 80}, new int[] {80,108},
                          new int[] {80,136}, new int[] {80,164}, new int[] {108, 4}, new int[] {108, 24}, new int[] {108,52}, new int[] {108,80},
                          new int[] {108,108},new int[] {108,136},new int[] {108,164},new int[] {136,  4}, new int[] {136,24}, new int[] {136,52},
                          new int[] {136,80}, new int[] {136,108},new int[] {136,136},new int[] {136,164}, new int[] {164,24}, new int[] {164,52},
                          new int[] {164,80}, new int[] {164,108},new int[] {164,136},new int[] {164,164}},
            new int[][] { new int[] { 4, 28}, new int[] { 4, 56}, new int[] { 4, 84}, new int[] { 4, 112}, new int[] { 4,140}, new int[] {28,  4}, // Ver 40
                          new int[] {28, 28}, new int[] {28, 56}, new int[] {28, 84}, new int[] {28, 112}, new int[] {28,140}, new int[] {28,168},
                          new int[] {56,  4}, new int[] {56, 28}, new int[] {56, 56}, new int[] {56,  84}, new int[] {56,112}, new int[] {56,140},
                          new int[] {56,168}, new int[] {84,  4}, new int[] {84, 28}, new int[] {84,  56}, new int[] {84, 84}, new int[] {84,112},
                          new int[] {84,140}, new int[] {84,168}, new int[] {112, 4}, new int[] {112, 28}, new int[] {112,56}, new int[] {112,84},
                          new int[] {112,112},new int[] {112,140},new int[] {112,168},new int[] {140,  4}, new int[] {140,28}, new int[] {140,56},
                          new int[] {140,84}, new int[] {140,112},new int[] {140,140},new int[] {140,168}, new int[] {168,28}, new int[] {168,56},
                          new int[] {168,84}, new int[] {168,112},new int[] {168,140},new int[] {168,168}}
        };

        // 指数⇒数値対応表
        // 【算出ロジック】
        // 数値：v=0～255
        // 指数：e=2^v (ただし、255を越えたら e=(29 Xor (v^2 And 255)、v=255のときは計算せずにe=0を設定)
        // 指数eの性質：0 <= v <= 254の範囲において、eは重複しない値(1～255のいずれか)を持つ
        //              v=255として計算するとe=1となり(つまり、周期255)、v=0の時と重複するので、e=0としておく
        private readonly int[] vector2exp = {
            255,   0,   1,  25,   2,  50,  26, 198,   3, 223,  51, 238,  27, 104, 199,  75,
              4, 100, 224,  14,  52, 141, 239, 129,  28, 193, 105, 248, 200,   8,  76, 113,
              5, 138, 101,  47, 225,  36,  15,  33,  53, 147, 142, 218, 240,  18, 130,  69,
             29, 181, 194, 125, 106,  39, 249, 185, 201, 154,   9, 120,  77, 228, 114, 166,
              6, 191, 139,  98, 102, 221,  48, 253, 226, 152,  37, 179,  16, 145,  34, 136,
             54, 208, 148, 206, 143, 150, 219, 189, 241, 210,  19,  92, 131,  56,  70,  64,
             30,  66, 182, 163, 195,  72, 126, 110, 107,  58,  40,  84, 250, 133, 186,  61,
            202,  94, 155, 159,  10,  21, 121,  43,  78, 212, 229, 172, 115, 243, 167,  87,
              7, 112, 192, 247, 140, 128,  99,  13, 103,  74, 222, 237,  49, 197, 254,  24,
            227, 165, 153, 119,  38, 184, 180, 124,  17,  68, 146, 217,  35,  32, 137,  46,
             55,  63, 209,  91, 149, 188, 207, 205, 144, 135, 151, 178, 220, 252, 190,  97,
            242,  86, 211, 171,  20,  42,  93, 158, 132,  60,  57,  83,  71, 109,  65, 162,
             31,  45,  67, 216, 183, 123, 164, 118, 196,  23,  73, 236, 127,  12, 111, 246,
            108, 161,  59,  82,  41, 157,  85, 170, 251,  96, 134, 177, 187, 204,  62,  90,
            203,  89,  95, 176, 156, 169, 160,  81,  11, 245,  22, 235, 122, 117,  44, 215,
             79, 174, 213, 233, 230, 231, 173, 232, 116, 214, 244, 234, 168,  80,  88, 175
        };

        private const int quietPxl = 10; // 最初の画像解析で周りに付ける白縁の幅
        private const int FmtInfoMask = 0x5412; // 形式情報のXORマスクパターン
        #endregion

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
        private ErrLevelEnum qrErrLvl;
        private byte modBlk = 0; // 白黒反転画像の場合、255(白)を暗とする

        private Mat matQr;
        private Rect[] rctFinderPtn;
        private bool[,] modAry;

        private int errRange = 0;

        public ErrNoEnum errNo;
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
            errNo = ErrNoEnum.normal;
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

            // 基本仕様 12.y
            // 符号化領域のデータを取得してマスクパターンにてマスク解除する
            ModuleEnum[,] modClsAry = UnMaskData();

            // 基本仕様 12.z
            // シンボルのコード語を求める
            byte[] encodeData = getCodeWord(modClsAry);

            // 基本仕様 12.aa
            // コード語列からブロックコード毎の符号化を取り出す
            byte[][] blockData = getBlockCode(encodeData);

            // 基本仕様 12.ab
            // 各ブロックコードの誤り訂正
            correctErr(blockData);

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

        #region エラー訂正符号の復号(BCH)
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
        /// <param name="fmtInfo">要素０が最下位ビット</param>
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
                    return FmtInfoAry[cnt];
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
            qrErrLvl = ErrLevelEnum.na;

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
            qrMask = (fmtInfo >> 10) & 7;
            int tmp = fmtInfo >> 13;
            if (tmp == 0)
            {
                qrErrLvl = ErrLevelEnum.m;
            }
            else if (tmp == 1)
            {
                qrErrLvl = ErrLevelEnum.h;
            }
            else if (tmp == 2)
            {
                qrErrLvl = ErrLevelEnum.l;
            }
            else
            {
                // 2bitデータなので、残りは3のみ
                qrErrLvl = ErrLevelEnum.q;
            }
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
                for (int cnt = qrCodeModNum - 1; cnt >= qrCodeModNum - 8; cnt--)
                {
                    fmtInfo[cur] = modAry[cnt, 8]; cur++;
                }
                for (int cnt = qrCodeModNum - 7; cnt < qrCodeModNum; cnt++)
                {
                    fmtInfo[cur] = modAry[8, cur]; cur++;
                }
            }

            return DecodeFormatInfo(fmtInfo);
        }

        /// <summary>
        /// マスクパターンを利用して符号化領域のマスクを解除したバイト列を返す
        /// </summary>
        /// <returns></returns>
        ModuleEnum[,] UnMaskData()
        {
            ModuleEnum[,] modClsAry = setFunctionCode(); // 機能コードと符号化領域を区別する

            // マスク解除
            for (int cntX = 0; cntX < qrCodeModNum; cntX++)
            {
                for (int cntY = 0; cntY < qrCodeModNum; cntY++)
                {
                    // 符号化情報（機能モジュール以外）についてマスク解除処理を実施
                    if (modClsAry[cntX, cntY] == ModuleEnum.f) continue;

                    bool mskFlg = false;
                    switch (qrMask)
                    {
                        case 0: // パターン 000
                            mskFlg = ((cntX + cntY) % 2 == 0);
                            break;

                        case 1: // パターン 001
                            mskFlg = (cntY % 2 == 0);
                            break;

                        case 2: // パターン 010
                            mskFlg = (cntX % 3 == 0);
                            break;

                        case 3: // パターン 011
                            mskFlg = ((cntX + cntY) % 3 == 0);
                            break;

                        case 4: // パターン 100
                            mskFlg = (((cntY >> 1) + Math.Floor(cntX / 3.0)) % 2 == 0);
                            break;

                        case 5: // パターン 101
                            mskFlg = (((cntX * cntY) % 2) + ((cntX * cntY) % 3) == 0);
                            break;

                        case 6: // パターン 110
                            mskFlg = ((((cntX * cntY) % 2) + ((cntX * cntY) % 3)) % 2 == 0);
                            break;

                        default: // パターン 111
                            mskFlg = ((((cntX * cntY) % 3) + ((cntX + cntY) % 2)) % 2 == 0);
                            break;

                    }

                    // XORでマスク解除処理
                    if (mskFlg) modClsAry[cntX, cntY] ^= ModuleEnum.d1;
                }
            }

            return modClsAry;
        }

        /// <summary>
        /// 符号化領域と機能コードを区別した配列を返す
        /// </summary>
        /// <returns></returns>
        ModuleEnum[,] setFunctionCode()
        {
            ModuleEnum[,] modClsAry = new ModuleEnum[qrCodeModNum, qrCodeModNum];

            // 先ずは全領域を符号化領域とみなして初期化
            for (int cntX = 0; cntX < qrCodeModNum; cntX++)
            {
                for (int cntY = 0; cntY < qrCodeModNum; cntY++)
                {
                    modClsAry[cntX, cntY] = modAry[cntX, cntY] ? ModuleEnum.d1 : ModuleEnum.d0;
                }
            }

            // 位置検出パターン（周囲の明モジュールを含む）
            for (int cntX = 0; cntX < 8; cntX++)
            {
                for (int cntY = 0; cntY < 8; cntY++)
                {
                    modClsAry[cntX, cntY] = ModuleEnum.f; // 左上
                    modClsAry[cntX + qrCodeModNum - 8, cntY] = ModuleEnum.f; // 右上
                    modClsAry[cntX, cntY + qrCodeModNum - 8] = ModuleEnum.f; // 左下
                }
            }

            // 形式情報（一部、タイミングパターンとダブるが、気にしない）
            for (int cntY = 0; cntY < 8; cntY++)
            {
                modClsAry[8, cntY] = ModuleEnum.f; // 左上
                modClsAry[8, cntY + qrCodeModNum - 8] = ModuleEnum.f; // 左下（＋固定暗モジュール１つ）
            }
            for (int cntX = 0; cntX < 8; cntX++)
            {
                modClsAry[cntX, 8] = ModuleEnum.f; // 左上
                modClsAry[cntX + qrCodeModNum - 8, 8] = ModuleEnum.f; // 右上
            }

            // 位置合せパターン
            if (qrVer >= 2)
            {
                int len = AdjPosPtn2[qrVer - 1].Length;
                for (int cnt = 0; cnt < len; cnt++)
                {
                    int stX = AdjPosPtn2[qrVer - 1][cnt][0];
                    int stY = AdjPosPtn2[qrVer - 1][cnt][1];
                    for (int cntX = 0; cntX < 5; cntX++)
                    {
                        for (int cntY = 0; cntY < 5; cntY++)
                        {
                            modClsAry[stX + cntX, stY + cntY] = ModuleEnum.f;
                        }
                    }
                }
            }

            // 型番情報
            if (qrVer >= 7)
            {
                for (int cnt1 = 0; cnt1 < 6; cnt1++)
                {
                    for (int cnt2 = 0; cnt2 < 3; cnt2++)
                    {
                        modClsAry[cnt2 + qrCodeModNum - 11, cnt1] = ModuleEnum.f; // 右上
                        modClsAry[cnt1, cnt2 + qrCodeModNum - 11] = ModuleEnum.f; // 左下
                    }
                }
            }

            // タイミングパターン
            for (int cnt = 8; cnt < qrCodeModNum - 8; cnt++)
            {
                modClsAry[7, cnt] = ModuleEnum.f; // 左上～左下
                modClsAry[cnt, 7] = ModuleEnum.f; // 左上～右上
            }

            return modClsAry;
        }

        /// <summary>
        /// シンボル符号化領域からコード語を取得する
        /// </summary>
        /// <param name="modClsAry"></param>
        /// <returns></returns>
        byte[] getCodeWord(ModuleEnum[,] modClsAry)
        {
            // 型番からバイト長を算出して、領域確保
            int codeLen = RsBlockCapa[qrVer - 1, (int)qrErrLvl, 1] + RsBlockCapa[qrVer, (int)qrErrLvl + 4, 1];
            byte[] codeWord = new byte[codeLen];

            int baseX = qrCodeModNum - 1;
            int x = baseX + 1;
            int y = qrCodeModNum;
            DirEnum dir = DirEnum.up;

            for (int cntDt = 0; cntDt < codeLen; cntDt++)
            {
                byte byteItem = 0;

                for (int cntWd = 0; cntWd < 8; cntWd++)
                {
                    ModuleEnum binData = ModuleEnum.f;

                    // 設置位置を探す
                    do
                    {
                        if (x == baseX)
                        {
                            // 隣の列へ
                            x--;
                        }
                        else
                        {
                            x = baseX;
                            if (dir == DirEnum.up)
                            {
                                // １つ上の行に行けるか確認
                                if (y == 0)
                                {
                                    // 上限までたどり着いたので、方向転換して次の列へ
                                    dir = DirEnum.down;
                                    baseX -= 2;
                                    if (baseX == 6) baseX = 5; // タイミングパターン列を飛ばす
                                    x = baseX;
                                    if (baseX < 0)
                                    {
                                        // なんかおかしい
                                        errNo = ErrNoEnum.failGetModule;
                                        return null;
                                    }
                                }
                                else
                                {
                                    y--;
                                }
                            }
                            else
                            {
                                // １つ下の行に行けるか確認
                                if (y + 1 >= qrCodeModNum)
                                {
                                    // 下限までたどり着いたので、方向転換して次の列へ
                                    dir = DirEnum.up;
                                    baseX -= 2;
                                    if (baseX == 6) baseX = 5; // タイミングパターン列を飛ばす
                                    x = baseX;
                                    if (baseX < 0)
                                    {
                                        // なんかおかしい
                                        errNo = ErrNoEnum.failGetModule;
                                        return null;
                                    }
                                }
                                else
                                {
                                    y++;
                                }
                            }
                        }
                        if (0 <= x && x < qrCodeModNum &&
                            0 <= y && y < qrCodeModNum &&
                            modClsAry[x, y] != ModuleEnum.f)
                        {
                            // モジュール取得
                            binData = modClsAry[x, y];
                            break;
                        }
                    } while (x >= 0 && y < qrCodeModNum); // 左下でおしまい
                    if (x < 0 && y >= qrCodeModNum)
                    {
                        // データをもらえずに最後（左下）まで行き着いた
                        errNo = ErrNoEnum.failGetModule;
                        return null;

                    }
                    if (binData == ModuleEnum.f)
                    {
                        // データをもらえずに最後（左下）まで行き着いた
                        errNo = ErrNoEnum.failGetModule;
                        return null;
                    }

                    // MSBからもらう
                    byteItem = (byte)((byteItem << 1) + binData);
                }
                codeWord[cntDt] = byteItem;
            }

            return codeWord;
        }

        /// <summary>
        /// 全コード語列からブロックコード毎のコード語列に振り分ける
        /// </summary>
        /// <param name="encodeData"></param>
        /// <returns></returns>
        byte[][] getBlockCode(byte[] encodeData)
        {
            byte[][] blockCode;
            int blockNum1 = RsBlockCapa[qrVer - 1, (int)qrErrLvl, 0];
            int blockNum2 = RsBlockCapa[qrVer - 1, (int)qrErrLvl + 4, 1];
            int[] blockMax = new int[blockNum1];
            int blockMaxMax = 0;
            int codeCnt = 0;

            // 各ブロックコードの領域確保と各ブロックコードの容量を取得
            blockCode = new byte[blockNum1][];
            for (int cnt = 0; cnt < blockNum1; cnt++)
            {
                blockMax[cnt] = RsBlockCapa[qrVer - 1, (int)qrErrLvl, 1];
                blockMaxMax = Math.Max(blockMax[cnt], blockMaxMax);
                blockCode[cnt] = new byte[RsBlockCapa[qrVer-1,(int)qrErrLvl,1]];
            }
            blockCode = new byte[blockNum2][];
            for (int cnt = 0; cnt < blockNum2; cnt++)
            {
                blockMax[cnt] = RsBlockCapa[qrVer - 1, (int)qrErrLvl + 4, 1];
                blockMaxMax = Math.Max(blockMax[cnt], blockMaxMax);
                blockCode[cnt] = new byte[RsBlockCapa[qrVer - 1, (int)qrErrLvl, 1]];
            }

            // コード語列からブロックコード毎にデータを振り分ける
            for (int blkCnt = 0; blkCnt < blockNum1; blkCnt++)
            {
                for (int cnt = 0; cnt < blockMaxMax; cnt++)
                {
                    if (blockMax[blkCnt] <= cnt) continue; // すでに埋まっているブロック
                    blockCode[blkCnt][cnt] = encodeData[codeCnt];
                    codeCnt++;
                }
            }

            return blockCode;
        }

        /// <summary>
        /// 各ブロックコードのエラー訂正
        /// </summary>
        /// <param name="blockData"></param>
        /// <returns></returns>
        bool correctErr(byte[][] blockData)
        {
            int blockNum1 = RsBlockCapa[qrVer - 1, (int)qrErrLvl, 0];
            int blockNum2 = RsBlockCapa[qrVer - 1, (int)qrErrLvl + 4, 0];

            // スペックを求める
            int codeLen = RsBlockCapa[qrVer - 1, (int)qrErrLvl, 2];
            int synNum = RsBlockCapa[qrVer - 1, (int)qrErrLvl, 3];
            for (int blkCnt = 0; blkCnt < blockNum1; blkCnt++)
            {
                blockData[blkCnt] = execCorrErr(blockData[blkCnt], codeLen, synNum);
                if (blockData[blkCnt] == null) return false;
            }

            return true;
        }

        byte[] execCorrErr(byte[] blockData, int codeLen, int synNum)
        {
            byte[] corrData = new byte[blockData.Length];
            int[] synd = new int[synNum];

            // a シンドロームを求める
            for (int cntS = 0; cntS < synNum; cntS++)
            {
                synd[cntS] = 0;
                for (int cntV = 0; cntV < blockData.Length; cntV++)
                {
                    synd[cntS] += vector2exp[(cntV * cntS) % 25] * blockData[cntV];
                }
            }

            // b 誤り位置を求める

                return corrData;
        }
        #endregion
        #endregion
    }
}
