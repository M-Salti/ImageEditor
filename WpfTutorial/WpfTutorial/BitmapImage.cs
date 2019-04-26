using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ImageEditor
{
    class EditableBitmap
    {
        public BitmapFileHeader FileHeader;
        public DIBHeader InfoHeader;
        public UInt32 RMask;
        public UInt32 GMask;
        public UInt32 BMask;
        public Color[] Palette;
        public byte[] ByteArray;

        public WriteableBitmap writeableBitmap;
        public Bitmap underlyingBitmap;
        public PinnedByteArray underlyingBytes;

        public readonly string FileName;
        public int Width;
        public int Height;
        public int RowSize;
        public int BitsPerPixel;

        public EditableBitmap()
        {
            FileName = "";
            Height = 0;
            Width = 0;
            RowSize = 0;
            BitsPerPixel = 0;
            FileHeader = new BitmapFileHeader();
            InfoHeader = new DIBHeader();
        }

        public EditableBitmap(string fileName)
        {
            FileName = fileName;
            LoadBitmap();
        }

        public EditableBitmap(int width, int height)
        {
            Width = width;
            Height = height;

            writeableBitmap = new WriteableBitmap(Width, Height, 96, 96, PixelFormats.Bgra32, palette: null);

            for (int i = 0; i < height; i++)
            {
                for (int j = 0; j < width; j++)
                {
                    SetPixelRowColumn(i, j, Color.White);
                }
            }
        }

        private void LoadBitmap()
        {
            MemoryStream memoryStream = new MemoryStream(File.ReadAllBytes(FileName));

            var reader = new BinaryReader(memoryStream);

            // read Bitmap file header
            FileHeader = new BitmapFileHeader();
            ReadFileHeader(reader);

            // read DIB header
            InfoHeader = new DIBHeader();
            ReadDIBHeader(reader);

            Height = InfoHeader.Height;
            Width = InfoHeader.Width;
            BitsPerPixel = InfoHeader.BitCount;
            CalcRowSize();  // (in bytes), in addition to padding

            if (InfoHeader.Compression != CompressionMethod.BI_RGB
             && InfoHeader.Compression != CompressionMethod.BI_RLE4
             && InfoHeader.Compression != CompressionMethod.BI_RLE8
             && InfoHeader.Compression != CompressionMethod.BI_BITFIELDS)
            {
                throw new Exception("Unsupprted format: " + InfoHeader.Compression);
            }

            // read Extra bit masks
            // 8.8.8.0
            BMask = 0x000000FF;
            GMask = 0x0000FF00;
            RMask = 0x00FF0000;

            if (BitsPerPixel == 16)
            {
                //5.5.5.0
                BMask = 0x001F;
                GMask = 0x03E0;
                RMask = 0x7C00;
            }

            if (InfoHeader.Size == (uint)DIBHeaderVersion.BITMAPV4HEADER || InfoHeader.Size == (uint)DIBHeaderVersion.BITMAPV5HEADER)
            {
                BMask = InfoHeader.BlueMask;
                GMask = InfoHeader.GreenMask;
                RMask = InfoHeader.RedMask;
            }

            if (InfoHeader.Compression == CompressionMethod.BI_BITFIELDS)
            {
                RMask = reader.ReadUInt32();
                GMask = reader.ReadUInt32();
                BMask = reader.ReadUInt32();
            }

            // it's illegal to combine 1/4/8/24bpp with BI_BITFIELDS or with BITMAPV4HEADER or BITMAPV5HEADER, so I'll ignore the colors bit masks
            if (BitsPerPixel != 16 || BitsPerPixel != 32)
            {
                // default colors bit masks for 24bpp
                BMask = 0x000000FF;
                GMask = 0x0000FF00;
                RMask = 0x00FF0000;
            }

            // Top-down DIBs cannot be compressed
            if (Height < 0 &&
                (InfoHeader.Compression != CompressionMethod.BI_RGB || InfoHeader.Compression != CompressionMethod.BI_BITFIELDS))
            {
                throw new Exception("Top-down DIBs can't be compressed. DIBHeader.Compression=" + InfoHeader.Compression);
            }

            if (BitsPerPixel <= 8 || InfoHeader.ClrUsed > 0)
                ReadPalette(reader);
            else
                Palette = null;

            SeekToPixelArray(reader);

            if (BitsPerPixel == 32)
                Read32bitBitmap(memoryStream, Height < 0);
            else if (BitsPerPixel == 24)
                Read24bitBitmap(memoryStream, Height < 0);
            else if (BitsPerPixel == 16)
                Read16bitBitmap(memoryStream, Height < 0);
            else if (BitsPerPixel == 8)
            {
                if (InfoHeader.Compression == CompressionMethod.BI_RLE4)
                    ReadRLE4Bitmap(reader);
                else if (InfoHeader.Compression == CompressionMethod.BI_RLE8)
                    ReadRLE8Bitmap(reader);
                else
                    ReadIndexedBitmap(memoryStream, Height < 0);
            }

            // no need to keep it negative, in case it is
            Height = Math.Abs(Height);

            memoryStream.Dispose();
            reader.Dispose();
        }


        private void ReadFileHeader(BinaryReader reader)
        {
            FileHeader.Type = reader.ReadUInt16();

            if (FileHeader.Type != BitmapFileHeader.BitmapType)
                throw new Exception("Not a bitmap file. FileHeader.Type = " + FileHeader.Type);

            FileHeader.Size = reader.ReadUInt32();
            FileHeader.Reserved1 = reader.ReadUInt16();
            FileHeader.Reserved2 = reader.ReadUInt16();
            FileHeader.OffBits = reader.ReadUInt32();
        }

        private void ReadDIBHeader(BinaryReader reader)
        {
            InfoHeader.Size = reader.ReadUInt32();

            DIBHeaderVersion version = new DIBHeaderVersion();

            switch (InfoHeader.Size)
            {
                case 12:
                    version = DIBHeaderVersion.BITMAPCOREHEADER;
                    break;
                case 40:
                    version = DIBHeaderVersion.BITMAPINFOHEADER;
                    break;
                case 108:
                    version = DIBHeaderVersion.BITMAPV4HEADER;
                    break;
                case 124:
                    version = DIBHeaderVersion.BITMAPV5HEADER;
                    break;
                default:
                    throw new Exception("DIB Header version is unrecognized:" + version);
            }

            if (version == DIBHeaderVersion.BITMAPCOREHEADER)
            {
                InfoHeader.Width = reader.ReadUInt16();
                InfoHeader.Height = reader.ReadUInt16();
                InfoHeader.Planes = reader.ReadUInt16(); ;
                InfoHeader.BitCount = reader.ReadUInt16();
                if (InfoHeader.BitCount == 2)
                    throw new Exception("2bpp are only supported on Windows CE");
                return;
            }

            // info header fields, they are also common to v4 and v5.
            InfoHeader.Width = reader.ReadInt32();
            InfoHeader.Height = reader.ReadInt32();
            InfoHeader.Planes = reader.ReadUInt16();
            if (InfoHeader.Planes != 1)
                throw new Exception("DIBHEADER.Planes must be equal to 1. Found value=" + InfoHeader.Planes);
            InfoHeader.BitCount = reader.ReadUInt16();
            if (InfoHeader.BitCount == 2)
                throw new Exception("2bpp are only supported on Windows CE");
            InfoHeader.Compression = (CompressionMethod)reader.ReadUInt32();
            if (InfoHeader.Compression == CompressionMethod.BI_RLE4 && InfoHeader.BitCount != 4)
                throw new Exception("Compression method is RLE-4, expected bit depth is 4bpp, found " + InfoHeader.BitCount);
            if (InfoHeader.Compression == CompressionMethod.BI_RLE8 && InfoHeader.BitCount != 8)
                throw new Exception("Compression method is RLE-8, expected bit depth is 8bpp, found " + InfoHeader.BitCount);
            InfoHeader.SizeImage = reader.ReadUInt32();
            InfoHeader.XPelsPerMeter = reader.ReadInt32();
            InfoHeader.YPelsPerMeter = reader.ReadInt32();
            InfoHeader.ClrUsed = reader.ReadUInt32();
            InfoHeader.ClrImportant = reader.ReadUInt32();

            if (version == DIBHeaderVersion.BITMAPINFOHEADER)
                return;

            InfoHeader.RedMask = reader.ReadUInt32();
            InfoHeader.GreenMask = reader.ReadUInt32();
            InfoHeader.BlueMask = reader.ReadUInt32();
            InfoHeader.AlphaMask = reader.ReadUInt32();
            InfoHeader.CSType = reader.ReadUInt32();

            InfoHeader.Endpoints = new CIEXYZTRIPLE();
            InfoHeader.Endpoints.ciexyzRed.ciexyzX = new FXPT2DOT30(reader.ReadUInt32());
            InfoHeader.Endpoints.ciexyzRed.ciexyzY = new FXPT2DOT30(reader.ReadUInt32());
            InfoHeader.Endpoints.ciexyzRed.ciexyzZ = new FXPT2DOT30(reader.ReadUInt32());

            InfoHeader.Endpoints.ciexyzGreen.ciexyzX = new FXPT2DOT30(reader.ReadUInt32());
            InfoHeader.Endpoints.ciexyzGreen.ciexyzY = new FXPT2DOT30(reader.ReadUInt32());
            InfoHeader.Endpoints.ciexyzGreen.ciexyzZ = new FXPT2DOT30(reader.ReadUInt32());

            InfoHeader.Endpoints.ciexyzBlue.ciexyzX = new FXPT2DOT30(reader.ReadUInt32());
            InfoHeader.Endpoints.ciexyzBlue.ciexyzY = new FXPT2DOT30(reader.ReadUInt32());
            InfoHeader.Endpoints.ciexyzBlue.ciexyzZ = new FXPT2DOT30(reader.ReadUInt32());

            InfoHeader.GammaRed = reader.ReadUInt32();
            InfoHeader.GammaGreen = reader.ReadUInt32();
            InfoHeader.GammaBlue = reader.ReadUInt32();

            if (version == DIBHeaderVersion.BITMAPV4HEADER)
                return;

            InfoHeader.Intent = reader.ReadUInt32();
            InfoHeader.ProfileData = reader.ReadUInt32();
            InfoHeader.ProfileSize = reader.ReadUInt32();
            InfoHeader.Reserved = reader.ReadUInt32();
        }

        private void ReadPalette(BinaryReader reader)
        {
            UInt32 paletteSize = InfoHeader.ClrUsed;
            if (paletteSize == 0)
                paletteSize = 1u << BitsPerPixel;
            Palette = new Color[paletteSize];
            for (int i = 0; i < paletteSize; i++)
            {
                byte B = reader.ReadByte();
                byte G = reader.ReadByte();
                byte R = reader.ReadByte();
                byte A = reader.ReadByte();

                Palette[i] = Color.FromArgb(R, G, B);
            }
        }

        private void Read32bitBitmap(MemoryStream stream, bool TopDownDIB)
        {
            int begin = TopDownDIB ? 0 : Height - 1;
            int end = TopDownDIB ? Height : -1;
            int inc = TopDownDIB ? 1 : -1;

            ByteArray = new byte[Height * Width * 4];

            for (int i = begin; i != end; i = i + inc)
            {
                stream.Read(ByteArray, Width * 4 * i, Width * 4);
            }

            int shiftB = Utility.CountTrailingZeros(BMask);
            int shiftG = Utility.CountTrailingZeros(GMask);
            int shiftR = Utility.CountTrailingZeros(RMask);

            if (shiftB == 0 && shiftG == 8 && shiftR == 16)
                writeableBitmap = new WriteableBitmap(Width, Height, InfoHeader.XPelsPerMeter / 39.3701, InfoHeader.YPelsPerMeter / 39.3701, PixelFormats.Bgra32, palette: null);
            else if (shiftB == 0 && shiftG == 10 && shiftR == 20)
                writeableBitmap = new WriteableBitmap(Width, Height, InfoHeader.XPelsPerMeter / 39.3701, InfoHeader.YPelsPerMeter / 39.3701, PixelFormats.Bgr101010, palette: null);
            else
                throw new Exception("Unsupported pixel format.");

            int pBackBuffer = (int)writeableBitmap.BackBuffer;
            int start = (int)writeableBitmap.BackBuffer;
            int stride = writeableBitmap.BackBufferStride;

            for (int i = 0; i < Height; i++)
            {
                pBackBuffer = start + i * stride;
                for (int j = 0; j < Width; j++)
                {
                    unsafe
                    {
                        int idx = i * Width * 4 + j * 4;
                        (*(int*)pBackBuffer) = (ByteArray[idx + 3] << 24) | (ByteArray[idx + 2] << 16) | (ByteArray[idx + 1] << 8) | (ByteArray[idx + 0]);
                        pBackBuffer += 4;
                    }
                }
            }

            writeableBitmap.Lock();
            writeableBitmap.AddDirtyRect(new Int32Rect(0, 0, Width, Height));
            writeableBitmap.Unlock();
        }

        private void Read24bitBitmap(MemoryStream stream, bool TopDownDIB)
        {
            int begin = TopDownDIB ? 0 : Height - 1;
            int end = TopDownDIB ? Height : -1;
            int inc = TopDownDIB ? 1 : -1;

            underlyingBytes = new PinnedByteArray(RowSize * Math.Abs(Height));
            underlyingBitmap = new Bitmap(Width, Math.Abs(Height), RowSize, System.Drawing.Imaging.PixelFormat.Format24bppRgb, underlyingBytes.BytesPtr);

            for (int row = begin; row != end; row = row + inc)
            {
                stream.Read(underlyingBytes.ByteArray, RowSize * row, RowSize);
                //stream.Read(ByteArray, Width * 3 * i, Width * 3);
                //for (int k = 0; k < paddingSize; k++)
                //{
                //    stream.ReadByte();
                //}
            }
            return;
            /*
            int paddingSize = RowSize - (Width * 3); // RowSize - (Width * BitsPerPixel / 8)
            ByteArray = new byte[Math.Abs(Height) * Width * 3];

            writeableBitmap = new WriteableBitmap(Width, Height, InfoHeader.XPelsPerMeter / 39.3701, InfoHeader.YPelsPerMeter / 39.3701, PixelFormats.Bgra32, palette: null);

            int pBackBuffer = (int)writeableBitmap.BackBuffer;
            int start = (int)writeableBitmap.BackBuffer;
            int stride = writeableBitmap.BackBufferStride;

            for (int i = 0; i < Height; i++)
            {
                pBackBuffer = start + i * stride;
                for (int j = 0; j < Width; j++)
                {
                    unsafe
                    {
                        int idx = i * Width * 3 + j * 3;
                        (*(int*)pBackBuffer) = (255 << 24) | (ByteArray[idx + 2] << 16) | (ByteArray[idx + 1] << 8) | (ByteArray[idx + 0]);
                        pBackBuffer += 4;
                    }
                }
            }

            writeableBitmap.Lock();
            writeableBitmap.AddDirtyRect(new Int32Rect(0, 0, Width, Height));
            writeableBitmap.Unlock();
            */
        }

        private void Read16bitBitmap(MemoryStream stream, bool TopDownDIB)
        {
            int begin = TopDownDIB ? 0 : Height - 1;
            int end = TopDownDIB ? Height : -1;
            int inc = TopDownDIB ? 1 : -1;

            int paddingSize = RowSize - (Width * 2); // RowSize - (Width * BitsPerPixel / 8)

            ByteArray = new byte[Height * Width * 3];

            for (int i = begin; i != end; i = i + inc)
            {
                stream.Read(ByteArray, Width * 2 * i, Width * 2);
                for (int k = 0; k < paddingSize; k++)
                {
                    stream.ReadByte();
                }
            }

            int shiftB = Utility.CountTrailingZeros(BMask);
            int shiftG = Utility.CountTrailingZeros(GMask);
            int shiftR = Utility.CountTrailingZeros(RMask);

            int pBackBuffer = (int)writeableBitmap.BackBuffer;
            int start = (int)writeableBitmap.BackBuffer;
            int stride = writeableBitmap.BackBufferStride;

            for (int i = 0; i < Height; i++)
            {
                pBackBuffer = start + i * stride;
                for (int j = 0; j < Width; j++)
                {
                    unsafe
                    {
                        int idx = i * Width * 2 + j * 2;
                        int pixel = (ByteArray[idx + 1] << 8) | (ByteArray[idx + 0]);

                        int blue = (byte)((pixel & BMask) >> shiftB);
                        int green = (byte)((pixel & GMask) >> shiftG);
                        int red = (byte)((pixel & RMask) >> shiftR);

                        (*(int*)pBackBuffer) = (255 << 24) | (red << 16) | (green << 8) | blue;
                        pBackBuffer += 4;
                    }
                }
            }

            writeableBitmap.Lock();
            writeableBitmap.AddDirtyRect(new Int32Rect(0, 0, Width, Height));
            writeableBitmap.Unlock();
        }

        private void ReadIndexedBitmap(MemoryStream stream, bool TopDownDIB)
        {
            int begin = TopDownDIB ? 0 : Height - 1;
            int end = TopDownDIB ? Height : -1;
            int inc = TopDownDIB ? 1 : -1;

            int paddingSize = RowSize - (Width); // RowSize - (Width * BitsPerPixel / 8)

            ByteArray = new byte[Height * Width];

            for (int i = begin; i != end; i = i + inc)
            {
                stream.Read(ByteArray, Width * i, Width);
                for (int k = 0; k < paddingSize; k++)
                {
                    stream.ReadByte();
                }
            }

            writeableBitmap = new WriteableBitmap(Width, Height, InfoHeader.XPelsPerMeter / 39.3701, InfoHeader.YPelsPerMeter / 39.3701, PixelFormats.Bgra32, palette: null);

            int pBackBuffer = (int)writeableBitmap.BackBuffer;
            int start = (int)writeableBitmap.BackBuffer;
            int stride = writeableBitmap.BackBufferStride;

            for (int i = 0; i < Height; i++)
            {
                pBackBuffer = start + i * stride;
                for (int j = 0; j < Width; j++)
                {
                    unsafe
                    {
                        int idx = i * Width + j;
                        (*(int*)pBackBuffer) = Palette[ByteArray[idx]].ToArgb();
                        pBackBuffer += 4;
                    }
                }
            }

            writeableBitmap.Lock();
            writeableBitmap.AddDirtyRect(new Int32Rect(0, 0, Width, Height));
            writeableBitmap.Unlock();
        }

        private void ReadRLE4Bitmap(BinaryReader reader)
        {

        }

        private void ReadRLE8Bitmap(BinaryReader reader)
        {

        }


        public void SaveImage(string fileName)
        {
            var stream = new FileStream(fileName, FileMode.Create);
            var writer = new BinaryWriter(stream);

            InfoHeader = new DIBHeader
            {
                Size = (uint)DIBHeaderVersion.BITMAPINFOHEADER,
                Width = Width,
                Height = Height,
                Planes = 1,
                BitCount = 24,
                Compression = CompressionMethod.BI_RGB,
                SizeImage = (uint)(((Width * 3 + 3) & 0xFFFFFFFCu) * Height),
                XPelsPerMeter = 2835,
                YPelsPerMeter = 2835,
                ClrUsed = 0,
                ClrImportant = 0
            };

            FileHeader = new BitmapFileHeader
            {
                Size = 14 + InfoHeader.Size + InfoHeader.SizeImage,
                OffBits = 14 + InfoHeader.Size
            };

            WriteFileHeader(writer);
            WriteDIBHeader(writer);

            int paddingSize = RowSize - (Width * BitsPerPixel) / 8;
            byte[] paddingData = { 0, 0, 0, 0 };

            for (int i = Height - 1; i >= 0; i--)
            {
                for (int j = 0; j < Width; j++)
                {
                    var cur = GetPixelRowColumn(i, j);

                    writer.Write(cur.B);
                    writer.Write(cur.G);
                    writer.Write(cur.R);
                }
                writer.Write(paddingData, 0, paddingSize);
            }

            writer.Close();
            stream.Close();
        }

        private void WriteFileHeader(BinaryWriter writer)
        {
            writer.Write(FileHeader.Type);
            writer.Write(FileHeader.Size);
            writer.Write(FileHeader.Reserved1);
            writer.Write(FileHeader.Reserved2);
            writer.Write(FileHeader.OffBits);
        }

        private void WriteDIBHeader(BinaryWriter writer)
        {
            writer.Write(InfoHeader.Size);
            writer.Write(InfoHeader.Width);
            writer.Write(InfoHeader.Height);
            writer.Write(InfoHeader.Planes);
            writer.Write(InfoHeader.BitCount);
            writer.Write((UInt32)InfoHeader.Compression);
            writer.Write(InfoHeader.SizeImage);
            writer.Write(InfoHeader.XPelsPerMeter);
            writer.Write(InfoHeader.YPelsPerMeter);
            writer.Write(InfoHeader.ClrUsed);
            writer.Write(InfoHeader.ClrImportant);
        }


        public Color GetPixelXY(int x, int y)
        {
            return GetPixelRowColumn(y, x);
        }

        public Color GetPixelRowColumn(int row, int column)
        {
            int idx = RowSize * row + column * 3;

            int colorData = underlyingBytes.ByteArray[idx] | underlyingBytes.ByteArray[idx + 1] << 8 | underlyingBytes.ByteArray[idx + 2] << 16;

            //writeableBitmap.Lock();

            //unsafe
            //{
            //    int pBackBuffer = (int)writeableBitmap.BackBuffer + row * writeableBitmap.BackBufferStride + 4 * column;
            //    colorData = *((int*)pBackBuffer);
            //}
            //writeableBitmap.Unlock();

            return Color.FromArgb(colorData);
        }

        public void SetPixelPoint(Position p, Color color)
        {
            SetPixelXY(p.X, p.Y, color);
        }

        public void SetPixelXY(int x, int y, Color color)
        {
            SetPixelRowColumn(y, x, color);
        }

        public void SetPixelRowColumn(int row, int column, Color color)
        {
            // do alpha blending
            if (color.A != 255)
            {
                color = AlphaBlend(GetPixelRowColumn(row, column), color);
            }

            int idx = RowSize * row + column * 3;
            underlyingBytes.ByteArray[idx + 0] = color.B;
            underlyingBytes.ByteArray[idx + 1] = color.G;
            underlyingBytes.ByteArray[idx + 2] = color.R;

            //byte[] colorArray = { color.B, color.G, color.R, color.A };
            //writeableBitmap.WritePixels(new Int32Rect(column, row, 1, 1), colorArray, 4, 0);
        }

        public void SetPixelRowColumnOfArray(int row, int column, Color color, byte[] array)
        {
            int idx = RowSize * row + column * 3;
            array[idx + 0] = color.B;
            array[idx + 1] = color.G;
            array[idx + 2] = color.R;
        }

        private Color AlphaBlend(Color oldColor, Color newColor)
        {
            int alpha = newColor.A;
            int red = ((newColor.R * alpha) + (oldColor.R * (255 - alpha))) / 255;
            int green = ((newColor.G * alpha) + (oldColor.G * (255 - alpha))) / 255;
            int blue = ((newColor.B * alpha) + (oldColor.B * (255 - alpha))) / 255;

            return Color.FromArgb(255, red, green, blue);
        }

        private void CalcRowSize()
        {
            RowSize = ((BitsPerPixel * Width + 31) / 32) * 4;
        }

        private void SeekToPixelArray(BinaryReader reader)
        {
            reader.BaseStream.Seek(FileHeader.OffBits, SeekOrigin.Begin);
        }

        public void BlackAndWhite()
        {
            for (int i = 0; i < Height; i++)
            {
                for (int j = 0; j < Width; j++)
                {
                    Color p = GetPixelRowColumn(i, j);
                    byte gs = (byte)(p.R * 0.3 + p.G * 0.59 + p.B * 0.11);
                    SetPixelRowColumn(i, j, Color.FromArgb(gs, gs, gs));
                }
            }
        }

        public void SepiaTone()
        {
            for (int i = 0; i < Height; i++)
            {
                for (int j = 0; j < Width; j++)
                {
                    byte r = 0, g = 0, b = 0;
                    Color p = GetPixelRowColumn(i, j);
                    int tr = (int)(0.393 * p.R + 0.769 * p.G + 0.189 * p.B);
                    int tg = (int)(0.349 * p.R + 0.686 * p.G + 0.168 * p.B);
                    int tb = (int)(0.272 * p.R + 0.534 * p.G + 0.131 * p.B);
                    if (tr > 255) r = 255; else r = (byte)tr;
                    if (tg > 255) g = 255; else g = (byte)tg;
                    if (tb > 255) b = 255; else b = (byte)tb;
                    SetPixelRowColumn(i, j, Color.FromArgb(r, g, b));
                }
            }
        }

        static class Kernel
        {
            // blur filter
            public static int[,] blur = new int[3, 3] { { 1, 1, 1 }, { 1, 1, 1 }, { 1, 1, 1 } };
            //sharpen
            public static int[,] sharp = new int[3, 3] { { 0, -1, 0 }, { -1, 5, -1 }, { 0, -1, 0 } };
            //edge verticle
            public static int[,] ver = new int[3, 3] { { 0, 0, 0 }, { -1, 1, 0 }, { 0, 0, 0 } };
            //edge horizanl
            public static int[,] hor = new int[3, 3] { { 0, -1, 0 }, { 0, 1, 0 }, { 0, 0, 0 } };
            //edge diagonl
            public static int[,] diag = new int[3, 3] { { -1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 0 } };
            //all
            public static int[,] all = new int[3, 3] { { -1, -1, -1 }, { -1, 8, -1 }, { -1, -1, -1 } };
            //east
            public static int[,] east = new int[3, 3] { { -1, 0, 1 }, { -1, 1, 1 }, { -1, 0, 1 } };
            //emboss south
            public static int[,] south = new int[3, 3] { { -1, -1, -1 }, { 0, 1, 0 }, { 1, 1, 1 } };
            //emboss south east
            public static int[,] southeast = new int[3, 3] { { -1, -1, 0 }, { -1, 1, 1 }, { 0, 1, 1 } };
        }

        public enum filters
        {
            blur, sharp, ver, hor, diag, all, east, south, southeast
        };

        public int multiplication(int[,] a, int[,] b)
        {
            int sum = 0;
            int divisor = 0;
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; ++j)
                {
                    sum += a[i, j] * b[i, j];
                    divisor += a[i, j];
                }
            }
            if (divisor == 0)
            {
                divisor++;
            }
            return (int)((1.0 * sum) / divisor);
        }

        public void globafun(filters par)
        {
            int[,] now = new int[3, 3];
            if (par == filters.blur)
            {
                now = Kernel.blur;
            }
            else if (par == filters.sharp)
            {
                now = Kernel.sharp;
            }
            else if (par == filters.hor)
            {
                now = Kernel.hor;
            }
            else if (par == filters.ver)
            {
                now = Kernel.ver;
            }
            else if (par == filters.diag)
            {
                now = Kernel.diag;
            }
            else if (par == filters.all)
            {
                now = Kernel.all;
            }
            else if (par == filters.east)
            {
                now = Kernel.east;
            }
            else if (par == filters.south)
            {
                now = Kernel.south;
            }
            else if (par == filters.southeast)
            {
                now = Kernel.southeast;
            }

            //Color[,] alter = new Color[this.Height, this.Width];
            byte[] temp = new byte[underlyingBytes.ByteArray.Length];
            Array.Copy(underlyingBytes.ByteArray, temp, temp.Length);

            for (int i = 0; i < this.Height - 2; ++i)
            {
                for (int j = 0; j < this.Width - 2; j++)
                {
                    int[,] matRed = new int[3, 3];
                    int[,] matGreen = new int[3, 3];
                    int[,] matBlue = new int[3, 3];
                    for (int a = 0; a < 3; a++)
                    {
                        for (int b = 0; b < 3; b++)
                        {
                            Color color = GetPixelRowColumn(i + a, j + b);
                            matRed[a, b] = color.R;
                            matGreen[a, b] = color.G;
                            matBlue[a, b] = color.B;
                        }
                    }

                    byte newRed = (byte)Utility.Clamp(0, 255, multiplication(now, matRed));
                    byte newGreen = (byte)Utility.Clamp(0, 255, multiplication(now, matGreen));
                    byte newBlue = (byte)Utility.Clamp(0, 255, multiplication(now, matBlue));

                    SetPixelRowColumnOfArray(i + 1, j + 1, Color.FromArgb(newRed, newGreen, newBlue), temp);
                    //alter[i + 1, j + 1] = Color.FromArgb(newRed, newGreen, newBlue);
                }
            }

            Array.Copy(temp, underlyingBytes.ByteArray, temp.Length);

            //COPY ALTER TO THIS
            //for (int i = 1; i < Height - 2; i++)
            //{
            //    for (int j = 1; j < Width - 2; j++)
            //    {
            //        SetPixelRowColumn(i, j, alter[i, j]);
            //    }
            //}
        }

        public void FlipVertical()
        {
            for (int i = 0; i < Height / 2; i++)
            {
                for (int j = 0; j < Width; j++)
                {
                    var temp = GetPixelRowColumn(i, j);
                    SetPixelRowColumn(i, j, GetPixelRowColumn(Height - 1 - i, j));
                    SetPixelRowColumn(Height - 1 - i, j, temp);
                }
            }
        }

        public void FlipHorizontal()
        {
            for (int i = 0; i < Height; i++)
            {
                for (int j = 0; j < Width / 2; j++)
                {
                    var temp = GetPixelRowColumn(i, j);
                    SetPixelRowColumn(i, j, GetPixelRowColumn(i, Width - 1 - j));
                    SetPixelRowColumn(i, Width - 1 - j, temp);
                }
            }
        }

        public void Rotate180()
        {
            int start = 0, end = Height * Width - 1;
            while (start < end)
            {
                var temp = GetPixelRowColumn(start / Width, start % Width);
                SetPixelRowColumn(start / Width, start % Width, GetPixelRowColumn(end / Width, end % Width));
                SetPixelRowColumn(end / Width, end % Width, temp);
                ++start;
                --end;
            }
        }

        public void RotateRight90()
        {
            Color[,] newData = new Color[Width, Height];

            for (int i = 0; i < Width; i++)
            {
                for (int j = 0; j < Height; j++)
                {
                    newData[i, j] = GetPixelRowColumn(Height - 1 - j, i);
                }
            }

            writeableBitmap = new WriteableBitmap(Height, Width, InfoHeader.YPelsPerMeter / 39.3701, InfoHeader.XPelsPerMeter / 39.3701, PixelFormats.Bgra32, palette: null);

            for (int i = 0; i < Width; i++)
            {
                for (int j = 0; j < Height; j++)
                {
                    SetPixelRowColumn(i, j, newData[i, j]);
                }
            }

            Utility.Swap(ref Height, ref Width);
            CalcRowSize();
        }

        public void RotateLeft90()
        {
            Color[,] newData = new Color[Width, Height];

            for (int i = 0; i < Width; i++)
            {
                for (int j = 0; j < Height; j++)
                {
                    newData[i, j] = GetPixelRowColumn(j, Width - 1 - i);
                }
            }

            writeableBitmap = new WriteableBitmap(Height, Width, InfoHeader.YPelsPerMeter / 39.3701, InfoHeader.XPelsPerMeter / 39.3701, PixelFormats.Bgra32, palette: null);

            for (int i = 0; i < Width; i++)
            {
                for (int j = 0; j < Height; j++)
                {
                    SetPixelRowColumn(i, j, newData[i, j]);
                }
            }

            Utility.Swap(ref Height, ref Width);
            CalcRowSize();
        }

        public void Brighten(int add)
        {
            for (int i = 0; i < Height; i++)
            {
                for (int j = 0; j < Width; j++)
                {
                    Color color = GetPixelRowColumn(i, j);
                    int newRed = Utility.Clamp(0, 255, color.R + add);
                    int newGreen = Utility.Clamp(0, 255, color.G + add);
                    int newBlue = Utility.Clamp(0, 255, color.B + add);
                    SetPixelRowColumn(i, j, Color.FromArgb((byte)newRed, (byte)newGreen, (byte)newBlue));
                }
            }
        }

        public void InvertColors()
        {
            for (int i = 0; i < Height; ++i)
            {
                for (int j = 0; j < Width; j++)
                {
                    Color color = GetPixelRowColumn(i, j);
                    SetPixelRowColumn(i, j, Color.FromArgb((byte)(255 - color.R), (byte)(255 - color.G), (byte)(255 - color.B)));
                }
            }
        }

        public void Resize(int precentage)
        {
            Resize(Height * precentage / 100, Width * precentage / 100);
        }

        public void Resize(int newHeight, int newWidth)
        {
            Color[,] temp = new Color[newHeight, newWidth];
            double xRatio = Width / (double)newWidth;
            double yRatio = Height / (double)newHeight;
            double px, py;
            for (int i = 0; i < newHeight; i++)
            {
                for (int j = 0; j < newWidth; j++)
                {
                    px = Math.Floor(j * xRatio);
                    py = Math.Floor(i * yRatio);
                    int index = (int)((py * Width) + px);
                    temp[i, j] = GetPixelRowColumn(index / Width, index % Width);
                }
            }

            writeableBitmap = new WriteableBitmap(newHeight, newHeight, InfoHeader.XPelsPerMeter / 39.3701, InfoHeader.YPelsPerMeter / 39.3701, PixelFormats.Bgra32, palette: null);

            for (int i = 0; i < newHeight; i++)
            {
                for (int j = 0; j < newWidth; j++)
                {
                    SetPixelRowColumn(i, j, temp[i, j]);
                }
            }

            Height = newHeight;
            Width = newWidth;
            CalcRowSize();
        }
    }

    class BitmapFileHeader
    {
        public UInt16 Type;
        public UInt32 Size;
        public UInt16 Reserved1;
        public UInt16 Reserved2;
        public UInt32 OffBits;

        public static UInt16 BitmapType = 0x4D42; // 0x4D42 = "BM" in little endian.

        public BitmapFileHeader()
        {
            Type = BitmapType;
            Reserved1 = 0;
            Reserved2 = 0;
        }

        public override string ToString()
        {
            string s = "Type= " + Type + " Size= " + Size;
            return s;
        }
    }

    class DIBHeader
    {
        public override string ToString()
        {
            return "Size= " + Size + " Width= " + Width + " Height=" + Height + " BitCount= " + BitCount;
        }

        public UInt32 Size;
        public Int32 Width;  // UInt16 in BITMAPCOREHEADER
        public Int32 Height; // UInt16 in BITMAPCOREHEADER
        public UInt16 Planes; // must be 1
        public UInt16 BitCount;
        // Core header ends here
        public CompressionMethod Compression = CompressionMethod.BI_RGB;
        public UInt32 SizeImage;
        public Int32 XPelsPerMeter;
        public Int32 YPelsPerMeter;
        public UInt32 ClrUsed = 0;
        public UInt32 ClrImportant = 0;
        // Info header ends here
        public UInt32 RedMask;
        public UInt32 GreenMask;
        public UInt32 BlueMask;
        public UInt32 AlphaMask;
        public UInt32 CSType;
        public CIEXYZTRIPLE Endpoints;
        public UInt32 GammaRed;
        public UInt32 GammaGreen;
        public UInt32 GammaBlue;
        // Version 4 header ends here
        public UInt32 Intent;
        public UInt32 ProfileData;
        public UInt32 ProfileSize;
        public UInt32 Reserved;
        // Version 5 header ends here

        private const int DefaultXPelsPerMeter = 3780;  // 96 dpi (96 * 39.3701 = 3779.5296)
        private const int DefaultYPelsPerMeter = 3780;

        public DIBHeader()
        {
            Planes = 1;
            XPelsPerMeter = DefaultXPelsPerMeter;
            YPelsPerMeter = DefaultYPelsPerMeter;
        }
    }

    enum DIBHeaderVersion
    {
        BITMAPCOREHEADER = 12,
        BITMAPINFOHEADER = 40,
        BITMAPV4HEADER = 108,
        BITMAPV5HEADER = 124
    }

    class CIEXYZTRIPLE
    {
        public CIEXYZTRIPLE()
        {
            ciexyzRed = new CIEXYZ();
            ciexyzGreen = new CIEXYZ();
            ciexyzBlue = new CIEXYZ();
        }
        public CIEXYZ ciexyzRed;
        public CIEXYZ ciexyzGreen;
        public CIEXYZ ciexyzBlue;
    }

    class CIEXYZ
    {
        public FXPT2DOT30 ciexyzX;
        public FXPT2DOT30 ciexyzY;
        public FXPT2DOT30 ciexyzZ;
    }

    class FXPT2DOT30
    {
        public float value;
        public FXPT2DOT30(UInt32 rawForm)
        {
            value = 0;
            value += (rawForm >> 30) & 3;
            for (int i = 29; i >= 0; --i)
            {
                value += (rawForm & (1 << i)) * (float)Math.Pow(2.0, i - 29.0);
            }
        }
    }

    enum CompressionMethod
    {
        BI_RGB = 0, // none, most common
        BI_RLE8 = 1, // can only be used with 8bpp bitmaps
        BI_RLE4 = 2, // can only be used with 4bpp bitmaps
        BI_BITFIELDS = 3, // RGB bit field masks
        BI_JPEG = 4, // used with BITMAPV4INFOHEADER
        BI_PNG = 5, // used with BITMAPV4INFOHEADER
    }

    class PinnedByteArray : IDisposable
    {
        byte[] byteArray;
        GCHandle handle;
        IntPtr ptr;

        public PinnedByteArray(int length)
        {
            byteArray = new byte[length];
            handle = GCHandle.Alloc(byteArray, GCHandleType.Pinned);
            ptr = Marshal.UnsafeAddrOfPinnedArrayElement(byteArray, 0);
        }

        public IntPtr BytesPtr { get => ptr; set => ptr = value; }
        public byte[] ByteArray { get => byteArray; set => byteArray = value; }

        public void Dispose()
        {
            handle.Free();
            byteArray = null;
        }
    }
}
