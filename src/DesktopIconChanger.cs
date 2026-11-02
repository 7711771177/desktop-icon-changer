using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace DesktopIconChanger
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                try
                {
                    string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopIconChanger");
                    Directory.CreateDirectory(folder);
                    File.AppendAllText(Path.Combine(folder, "error.log"), DateTime.Now.ToString("s") + " " + ex.ToString() + Environment.NewLine);
                }
                catch
                {
                }
                MessageBox.Show(ex.ToString(), "程序出错", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    internal class IconEntry
    {
        public string Path;
        public string Name;
        public bool IsFolder;
        public string Source;
        public ListViewItem Row;
    }

    internal class UndoRecord
    {
        public string Path;
        public bool IsFolder;
        public string OldIconLocation;
        public bool OldHadIni;
        public byte[] OldIni;
        public FileAttributes OldFolderAttrs;
    }

    internal static class Native
    {
        public const int SHCNE_ASSOCCHANGED = 0x08000000;
        public const int SHCNE_UPDATEITEM = 0x00002000;
        public const uint SHCNF_IDLIST = 0x0000;
        public const uint SHCNF_PATHW = 0x0005;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern uint ExtractIconEx(string file, int index, IntPtr[] large, IntPtr[] small, uint icons);

        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("shell32.dll", EntryPoint = "SHChangeNotify")]
        public static extern void SHChangeNotifyIdList(int eventId, uint flags, IntPtr item1, IntPtr item2);

        [DllImport("shell32.dll", EntryPoint = "SHChangeNotify", CharSet = CharSet.Unicode)]
        public static extern void SHChangeNotifyPath(int eventId, uint flags, string item1, IntPtr item2);
    }

    internal static class Shortcut
    {
        private static object NewShell()
        {
            Type type = Type.GetTypeFromProgID("WScript.Shell");
            if (type == null) throw new InvalidOperationException("WScript.Shell 不可用");
            return Activator.CreateInstance(type);
        }

        public static object Open(string lnkPath)
        {
            object shell = NewShell();
            return shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
        }

        public static string Get(object shortcut, string property)
        {
            object value = shortcut.GetType().InvokeMember(property, BindingFlags.GetProperty, null, shortcut, null);
            return value == null ? string.Empty : value.ToString();
        }

        public static void Set(object shortcut, string property, string value)
        {
            shortcut.GetType().InvokeMember(property, BindingFlags.SetProperty, null, shortcut, new object[] { value });
        }

        public static void Save(object shortcut)
        {
            shortcut.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
        }
    }

    internal static class IconFile
    {
        public static string SplitLocation(string location, out int index)
        {
            index = 0;
            if (string.IsNullOrEmpty(location)) return null;
            string value = location.Trim();
            int comma = value.LastIndexOf(',');
            if (comma > 0)
            {
                int parsed;
                if (int.TryParse(value.Substring(comma + 1).Trim(), out parsed))
                {
                    index = parsed;
                    value = value.Substring(0, comma);
                }
            }
            value = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"').Trim());
            if (value.Length == 0) return null;
            return value;
        }

        public static Bitmap FromResource(string file, int index, int size)
        {
            if (string.IsNullOrEmpty(file) || !File.Exists(file)) return null;
            IntPtr[] large = new IntPtr[1];
            IntPtr[] small = new IntPtr[1];
            try
            {
                uint count = Native.ExtractIconEx(file, index, large, small, 1);
                if (count == 0) return null;
                IntPtr handle = size > 16 ? large[0] : small[0];
                if (handle == IntPtr.Zero) handle = large[0] != IntPtr.Zero ? large[0] : small[0];
                if (handle == IntPtr.Zero) return null;
                using (Icon icon = Icon.FromHandle(handle))
                using (Bitmap source = icon.ToBitmap())
                {
                    Bitmap result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
                    using (Graphics g = Graphics.FromImage(result))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.SmoothingMode = SmoothingMode.HighQuality;
                        g.CompositingQuality = CompositingQuality.HighQuality;
                        g.DrawImage(source, new Rectangle(0, 0, size, size));
                    }
                    return result;
                }
            }
            catch
            {
                return null;
            }
            finally
            {
                if (large[0] != IntPtr.Zero) Native.DestroyIcon(large[0]);
                if (small[0] != IntPtr.Zero) Native.DestroyIcon(small[0]);
            }
        }

        public static Bitmap System(int index, int size)
        {
            return FromResource(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shell32.dll"), index, size);
        }

        public static Bitmap Square(Image image)
        {
            int side = Math.Min(image.Width, image.Height);
            int left = (image.Width - side) / 2;
            int top = (image.Height - side) / 2;
            Bitmap result = new Bitmap(side, side, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.DrawImage(image, new Rectangle(0, 0, side, side), new Rectangle(left, top, side, side), GraphicsUnit.Pixel);
            }
            return result;
        }

        // ===== 新增：按裁剪框裁出正方形（1:1，可限制输出边长） =====
        public static Bitmap Crop(Image image, RectangleF region, int maxSide)
        {
            float side = Math.Min(region.Width, Math.Min(image.Width, image.Height));
            if (side < 1f) side = 1f;
            float x = region.X;
            float y = region.Y;
            if (x < 0f) x = 0f;
            if (y < 0f) y = 0f;
            if (x + side > image.Width) x = image.Width - side;
            if (y + side > image.Height) y = image.Height - side;
            if (x < 0f) x = 0f;
            if (y < 0f) y = 0f;

            int output = (int)Math.Round(side);
            if (maxSide > 0 && output > maxSide) output = maxSide;
            if (output < 1) output = 1;

            Bitmap result = new Bitmap(output, output, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.CompositingMode = CompositingMode.SourceCopy;
                using (ImageAttributes attributes = new ImageAttributes())
                {
                    attributes.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(image, new Rectangle(0, 0, output, output), x, y, side, side, GraphicsUnit.Pixel, attributes);
                }
            }
            return result;
        }
        // ===== 裁剪结束 =====

        // ===== 新增：圆形蒙版 =====
        // 输入已居中裁好的正方形图片，输出圆形透明底图片。
        // 1) 先当普通图片铺满整张透明画布；
        // 2) 再用 GDI+ 绘制抗锯齿圆（SmoothingMode.AntiAlias），取圆蒙版的 alpha；
        // 3) 逐像素把 alpha 交叉相乘，圆外 alpha=0 变成完全透明，圆边缘保留半透明过渡，无锯齿毛边。
        public static Bitmap Circle(Image square)
        {
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddEllipse(new RectangleF(0f, 0f, Math.Min(square.Width, square.Height), Math.Min(square.Width, square.Height)));
                return ApplyMask(square, path);
            }
        }
        // ===== 圆形蒙版结束 =====

        // ===== 新增：圆角矩形蒙版 =====
        // 与圆形蒙版同一套做法，只把蒙版路径换成「四角为圆弧的矩形」。
        // ratio 为圆角半径占边长的比例，0.5 时退化为圆形，0 时就是方形。
        public static Bitmap RoundedSquare(Image square, float ratio)
        {
            int side = Math.Min(square.Width, square.Height);
            if (ratio < 0f) ratio = 0f;
            if (ratio > 0.5f) ratio = 0.5f;
            float radius = side * ratio;
            if (radius < 0.5f) radius = 0.5f;

            using (GraphicsPath path = new GraphicsPath())
            {
                float d = radius * 2f;
                path.AddArc(0f, 0f, d, d, 180f, 90f);
                path.AddArc(side - d, 0f, d, d, 270f, 90f);
                path.AddArc(side - d, side - d, d, d, 0f, 90f);
                path.AddArc(0f, side - d, d, d, 90f, 90f);
                path.CloseFigure();
                return ApplyMask(square, path);
            }
        }
        // ===== 圆角矩形蒙版结束 =====

        // ===== 新增：智能抠图（离线抠出主体，背景变透明） =====
        // 不依赖第三方库、不联网、不用模型权重，全部是 GDI+ 加队列泛洪的本机计算：
        // 1) 统计边框像素的颜色直方图，取出现最多的一档作为背景色；
        // 2) 从「贴着边框且接近背景色」的像素出发做四连通泛洪，颜色阈值放宽到能跟着渐变走；
        // 3) 前景里面积过小的连通块当噪点清掉；
        // 4) alpha 做两次 [1 2 1] 可分离模糊后二次映射，得到 1~2 像素宽的抗锯齿过渡；
        // 5) 半透明像素的颜色用最近的不透明像素颜色回填（颜色去污），避免背景色残留形成白边；
        // 6) 最后按主体外框等比缩放居中，铺满正方形画布。
        public static Bitmap Cutout(Image square)
        {
            int side = Math.Min(square.Width, square.Height);
            if (side < 16) return (Bitmap)square.Clone();

            Bitmap work = new Bitmap(side, side, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(work))
            {
                g.Clear(Color.Transparent);
                g.CompositingMode = CompositingMode.SourceCopy;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                using (ImageAttributes attributes = new ImageAttributes())
                {
                    attributes.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(square, new Rectangle(0, 0, side, side), 0, 0, square.Width, square.Height, GraphicsUnit.Pixel, attributes);
                }
            }

            int n = side * side;
            Rectangle content = Rectangle.Empty;
            BitmapData data = work.LockBits(new Rectangle(0, 0, side, side), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride;
                byte[] buf = new byte[stride * side];
                Marshal.Copy(data.Scan0, buf, 0, buf.Length);

                int bgR, bgG, bgB;
                EstimateBackground(buf, stride, side, out bgR, out bgG, out bgB);

                bool[] background = new bool[n];
                FloodBackground(buf, stride, side, background, bgR, bgG, bgB);

                // 开运算：先腐蚀再膨胀，断开被细线连在主体上的杂字、毛刺
                bool[] foreground = new bool[n];
                for (int i = 0; i < n; i++) foreground[i] = !background[i];
                OpenMask(foreground, side);
                for (int i = 0; i < n; i++) background[i] = !foreground[i];

                KeepMainSubject(background, side);
                int x0, y0, x1, y1;
                BuildAlpha(buf, stride, side, background, out x0, out y0, out x1, out y1);

                Marshal.Copy(buf, 0, data.Scan0, buf.Length);
                if (x1 >= x0 && y1 >= y0) content = new Rectangle(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
            }
            finally
            {
                work.UnlockBits(data);
            }

            if (content.Width <= 0 || content.Height <= 0) return work;

            Bitmap fitted = FitToSquare(work, side, content);
            work.Dispose();
            return fitted;
        }

        // 统计边框像素的主色（16 级量化直方图取最大值，再把这些像素求平均）
        private static void EstimateBackground(byte[] buf, int stride, int side, out int r, out int g, out int b)
        {
            int[] histogram = new int[4096];
            int[] offsets = BorderOffsets(stride, side);
            for (int i = 0; i < offsets.Length; i++)
            {
                int off = offsets[i];
                int key = ((buf[off + 2] >> 4) << 8) | ((buf[off + 1] >> 4) << 4) | (buf[off] >> 4);
                histogram[key]++;
            }

            int best = 0;
            for (int i = 1; i < histogram.Length; i++)
            {
                if (histogram[i] > histogram[best]) best = i;
            }

            long sumR = 0, sumG = 0, sumB = 0, count = 0;
            for (int i = 0; i < offsets.Length; i++)
            {
                int off = offsets[i];
                int key = ((buf[off + 2] >> 4) << 8) | ((buf[off + 1] >> 4) << 4) | (buf[off] >> 4);
                if (key != best) continue;
                sumB += buf[off];
                sumG += buf[off + 1];
                sumR += buf[off + 2];
                count++;
            }

            if (count == 0)
            {
                r = 255;
                g = 255;
                b = 255;
                return;
            }
            r = (int)(sumR / count);
            g = (int)(sumG / count);
            b = (int)(sumB / count);
        }

        private static int[] BorderOffsets(int stride, int side)
        {
            int[] offsets = new int[side * 4];
            int n = 0;
            for (int k = 0; k < side; k++)
            {
                offsets[n++] = k * 4;
                offsets[n++] = (side - 1) * stride + k * 4;
                offsets[n++] = k * stride;
                offsets[n++] = k * stride + (side - 1) * 4;
            }
            return offsets;
        }

        // 从边框出发的泛洪：只把「接近背景色」的边框像素当种子，主体压边时不会被啃掉
        private static void FloodBackground(byte[] buf, int stride, int side, bool[] background, int bgR, int bgG, int bgB)
        {
            const int seedTolerance = 48;
            const int nearTolerance = 42;
            const int refTolerance = 62;

            Queue<int> queue = new Queue<int>(side * 4);
            for (int k = 0; k < side; k++)
            {
                Seed(buf, stride, side, background, queue, k, 0, bgR, bgG, bgB, seedTolerance);
                Seed(buf, stride, side, background, queue, k, side - 1, bgR, bgG, bgB, seedTolerance);
                Seed(buf, stride, side, background, queue, 0, k, bgR, bgG, bgB, seedTolerance);
                Seed(buf, stride, side, background, queue, side - 1, k, bgR, bgG, bgB, seedTolerance);
            }

            while (queue.Count > 0)
            {
                int index = queue.Dequeue();
                int x = index % side;
                int y = index / side;
                int off = y * stride + x * 4;
                int pr = buf[off + 2];
                int pg = buf[off + 1];
                int pb = buf[off];

                Spread(buf, stride, side, background, queue, x - 1, y, pr, pg, pb, bgR, bgG, bgB, nearTolerance, refTolerance);
                Spread(buf, stride, side, background, queue, x + 1, y, pr, pg, pb, bgR, bgG, bgB, nearTolerance, refTolerance);
                Spread(buf, stride, side, background, queue, x, y - 1, pr, pg, pb, bgR, bgG, bgB, nearTolerance, refTolerance);
                Spread(buf, stride, side, background, queue, x, y + 1, pr, pg, pb, bgR, bgG, bgB, nearTolerance, refTolerance);
            }
        }

        private static void Seed(byte[] buf, int stride, int side, bool[] background, Queue<int> queue, int x, int y, int bgR, int bgG, int bgB, int tolerance)
        {
            if (x < 0 || y < 0 || x >= side || y >= side) return;
            int index = y * side + x;
            if (background[index]) return;
            int off = y * stride + x * 4;
            if (buf[off + 3] < 10) { background[index] = true; queue.Enqueue(index); return; }
            int d = Math.Abs(buf[off + 2] - bgR) + Math.Abs(buf[off + 1] - bgG) + Math.Abs(buf[off] - bgB);
            if (d > tolerance) return;
            background[index] = true;
            queue.Enqueue(index);
        }

        private static void Spread(byte[] buf, int stride, int side, bool[] background, Queue<int> queue, int x, int y, int pr, int pg, int pb, int bgR, int bgG, int bgB, int nearTolerance, int refTolerance)
        {
            if (x < 0 || y < 0 || x >= side || y >= side) return;
            int index = y * side + x;
            if (background[index]) return;
            int off = y * stride + x * 4;
            if (buf[off + 3] < 10)
            {
                background[index] = true;
                queue.Enqueue(index);
                return;
            }

            int cr = buf[off + 2];
            int cg = buf[off + 1];
            int cb = buf[off];
            int near = Math.Abs(cr - pr) + Math.Abs(cg - pg) + Math.Abs(cb - pb);
            int refd = Math.Abs(cr - bgR) + Math.Abs(cg - bgG) + Math.Abs(cb - bgB);
            if (near > nearTolerance && refd > refTolerance) return;

            background[index] = true;
            queue.Enqueue(index);
        }

        // 形态学开运算（3x3 十字）：1~2 像素宽的细桥会被断开，粗的形体基本不变
        private static void OpenMask(bool[] foreground, int side)
        {
            int n = side * side;
            bool[] eroded = new bool[n];
            for (int y = 1; y < side - 1; y++)
            {
                for (int x = 1; x < side - 1; x++)
                {
                    int i = y * side + x;
                    if (!foreground[i]) continue;
                    if (!foreground[i - 1] || !foreground[i + 1] || !foreground[i - side] || !foreground[i + side]) continue;
                    eroded[i] = true;
                }
            }

            bool[] dilated = new bool[n];
            for (int y = 0; y < side; y++)
            {
                for (int x = 0; x < side; x++)
                {
                    int i = y * side + x;
                    if (foreground[i])
                    {
                        dilated[i] = true;
                        continue;
                    }
                    if (x > 0 && eroded[i - 1]) { dilated[i] = true; continue; }
                    if (x + 1 < side && eroded[i + 1]) { dilated[i] = true; continue; }
                    if (y > 0 && eroded[i - side]) { dilated[i] = true; continue; }
                    if (y + 1 < side && eroded[i + side]) { dilated[i] = true; continue; }
                }
            }
            Array.Copy(dilated, foreground, n);
        }

        // 只保留主体：把前景连通块里「面积远小于最大块」的部分当噪点、杂字、碎屑去掉
        private static void KeepMainSubject(bool[] background, int side)
        {
            int n = side * side;
            int[] label = new int[n];
            List<int> areas = new List<int>();
            Queue<int> queue = new Queue<int>();
            int nextLabel = 0;

            for (int start = 0; start < n; start++)
            {
                if (background[start] || label[start] != 0) continue;
                nextLabel++;
                int area = 0;
                queue.Clear();
                queue.Enqueue(start);
                label[start] = nextLabel;
                while (queue.Count > 0)
                {
                    int c = queue.Dequeue();
                    area++;
                    int x = c % side;
                    int y = c / side;
                    LabelNeighbour(background, label, queue, x - 1, y, side, nextLabel);
                    LabelNeighbour(background, label, queue, x + 1, y, side, nextLabel);
                    LabelNeighbour(background, label, queue, x, y - 1, side, nextLabel);
                    LabelNeighbour(background, label, queue, x, y + 1, side, nextLabel);
                }
                areas.Add(area);
            }

            if (areas.Count == 0) return;

            int largest = 0;
            for (int i = 0; i < areas.Count; i++)
            {
                if (areas[i] > largest) largest = areas[i];
            }

            int floorArea = Math.Max(Math.Max(24, n / 2000), (int)(largest * 0.10f));
            for (int i = 0; i < n; i++)
            {
                int id = label[i];
                if (id <= 0) continue;
                if (areas[id - 1] >= floorArea) continue;
                background[i] = true;
            }
        }

        private static void LabelNeighbour(bool[] background, int[] label, Queue<int> queue, int x, int y, int side, int id)
        {
            if (x < 0 || y < 0 || x >= side || y >= side) return;
            int index = y * side + x;
            if (background[index] || label[index] != 0) return;
            label[index] = id;
            queue.Enqueue(index);
        }

        // 生成 alpha：模糊 -> 二次映射 -> 颜色去污 -> 写回，并顺便算出主体外框
        private static void BuildAlpha(byte[] buf, int stride, int side, bool[] background, out int x0, out int y0, out int x1, out int y1)
        {
            int n = side * side;
            float[] alpha = new float[n];
            for (int i = 0; i < n; i++) alpha[i] = background[i] ? 0f : 1f;

            float[] temp = new float[n];
            for (int pass = 0; pass < 2; pass++)
            {
                BlurPass(alpha, temp, side, true);
                BlurPass(temp, alpha, side, false);
            }

            bool[] solid = new bool[n];
            for (int i = 0; i < n; i++)
            {
                float v = (alpha[i] - 0.34f) / 0.66f;
                if (v < 0f) v = 0f;
                if (v > 1f) v = 1f;
                alpha[i] = v;
                solid[i] = v >= 0.995f;
            }

            Decontaminate(buf, stride, side, solid);

            int minX = side, minY = side, maxX = -1, maxY = -1;
            for (int y = 0; y < side; y++)
            {
                for (int x = 0; x < side; x++)
                {
                    int index = y * side + x;
                    int value = (int)Math.Round(alpha[index] * 255f);
                    if (value < 0) value = 0;
                    if (value > 255) value = 255;
                    int off = y * stride + x * 4;
                    buf[off + 3] = (byte)value;
                    if (value <= 16) continue;
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
            }

            if (maxX < minX || maxY < minY)
            {
                x0 = 0;
                y0 = 0;
                x1 = -1;
                y1 = -1;
                return;
            }
            x0 = minX;
            y0 = minY;
            x1 = maxX;
            y1 = maxY;
        }

        private static void BlurPass(float[] source, float[] target, int side, bool horizontal)
        {
            for (int y = 0; y < side; y++)
            {
                for (int x = 0; x < side; x++)
                {
                    int index = y * side + x;
                    float center = source[index];
                    float left;
                    float right;
                    if (horizontal)
                    {
                        left = x > 0 ? source[index - 1] : center;
                        right = x + 1 < side ? source[index + 1] : center;
                    }
                    else
                    {
                        left = y > 0 ? source[index - side] : center;
                        right = y + 1 < side ? source[index + side] : center;
                    }
                    target[index] = (left + 2f * center + right) * 0.25f;
                }
            }
        }

        // 颜色去污：从所有不透明像素出发做多源 BFS，把最近的不透明颜色铺到半透明像素上
        private static void Decontaminate(byte[] buf, int stride, int side, bool[] solid)
        {
            int n = side * side;
            bool[] filled = new bool[n];
            Queue<int> queue = new Queue<int>();
            for (int i = 0; i < n; i++)
            {
                if (!solid[i]) continue;
                filled[i] = true;
                queue.Enqueue(i);
            }

            while (queue.Count > 0)
            {
                int index = queue.Dequeue();
                int x = index % side;
                int y = index / side;
                int off = y * stride + x * 4;
                byte r = buf[off + 2];
                byte g = buf[off + 1];
                byte b = buf[off];
                SpreadColor(buf, stride, side, filled, queue, x - 1, y, r, g, b);
                SpreadColor(buf, stride, side, filled, queue, x + 1, y, r, g, b);
                SpreadColor(buf, stride, side, filled, queue, x, y - 1, r, g, b);
                SpreadColor(buf, stride, side, filled, queue, x, y + 1, r, g, b);
            }
        }

        private static void SpreadColor(byte[] buf, int stride, int side, bool[] filled, Queue<int> queue, int x, int y, byte r, byte g, byte b)
        {
            if (x < 0 || y < 0 || x >= side || y >= side) return;
            int index = y * side + x;
            if (filled[index]) return;
            filled[index] = true;
            int off = y * stride + x * 4;
            buf[off] = b;
            buf[off + 1] = g;
            buf[off + 2] = r;
            queue.Enqueue(index);
        }

        // 按主体外框等比缩放居中，四周留 3% 透明边
        private static Bitmap FitToSquare(Bitmap source, int side, Rectangle content)
        {
            int target = (int)Math.Round(side * 0.94f);
            float scale = Math.Min((float)target / content.Width, (float)target / content.Height);
            int width = Math.Max(1, Math.Min(side, (int)Math.Round(content.Width * scale)));
            int height = Math.Max(1, Math.Min(side, (int)Math.Round(content.Height * scale)));

            Bitmap output = new Bitmap(side, side, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(output))
            {
                g.Clear(Color.Transparent);
                g.CompositingMode = CompositingMode.SourceCopy;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                using (ImageAttributes attributes = new ImageAttributes())
                {
                    attributes.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(source, new Rectangle((side - width) / 2, (side - height) / 2, width, height), content.X, content.Y, content.Width, content.Height, GraphicsUnit.Pixel, attributes);
                }
            }
            return output;
        }
        // ===== 智能抠图结束 =====

        // ===== 新增：通用形状蒙版 =====
        // 1) 先把图片铺满透明画布；
        // 2) 用抗锯齿填充路径画出形状蒙版，取蒙版 alpha；
        // 3) 逐像素把 alpha 交叉相乘，形状外 alpha=0，边缘保留半透明过渡。
        private static Bitmap ApplyMask(Image square, GraphicsPath shape)
        {
            int side = Math.Min(square.Width, square.Height);
            Bitmap result = new Bitmap(side, side, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(square, new Rectangle(0, 0, side, side), new Rectangle(0, 0, square.Width, square.Height), GraphicsUnit.Pixel);
            }

            using (Bitmap mask = new Bitmap(side, side, PixelFormat.Format32bppArgb))
            {
                using (Graphics mg = Graphics.FromImage(mask))
                {
                    mg.SmoothingMode = SmoothingMode.AntiAlias;
                    mg.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    mg.CompositingMode = CompositingMode.SourceCopy;
                    mg.Clear(Color.Transparent);
                    using (SolidBrush brush = new SolidBrush(Color.FromArgb(255, 255, 255, 255)))
                    {
                        mg.FillPath(brush, shape);
                    }
                }

                Rectangle rect = new Rectangle(0, 0, side, side);
                BitmapData dst = result.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
                BitmapData src = mask.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    int rowBytes = side * 4;
                    byte[] dline = new byte[rowBytes];
                    byte[] sline = new byte[rowBytes];
                    for (int y = 0; y < side; y++)
                    {
                        Marshal.Copy(new IntPtr(dst.Scan0.ToInt64() + (long)y * dst.Stride), dline, 0, rowBytes);
                        Marshal.Copy(new IntPtr(src.Scan0.ToInt64() + (long)y * src.Stride), sline, 0, rowBytes);
                        for (int x = 0; x < rowBytes; x += 4)
                        {
                            int maskAlpha = sline[x + 3];
                            if (maskAlpha == 255) continue;
                            if (maskAlpha == 0)
                            {
                                dline[x + 3] = 0;
                                continue;
                            }
                            dline[x + 3] = (byte)((dline[x + 3] * maskAlpha + 127) / 255);
                        }
                        Marshal.Copy(dline, 0, new IntPtr(dst.Scan0.ToInt64() + (long)y * dst.Stride), rowBytes);
                    }
                }
                finally
                {
                    result.UnlockBits(dst);
                    mask.UnlockBits(src);
                }
            }
            return result;
        }
        // ===== 通用形状蒙版结束 =====

        private static byte[] ToDib(Bitmap bitmap)
        {
            int width = bitmap.Width;
            int height = bitmap.Height;
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            byte[] pixels = new byte[data.Stride * height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            bitmap.UnlockBits(data);

            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(40);
                writer.Write(width);
                writer.Write(height * 2);
                writer.Write((short)1);
                writer.Write((short)32);
                writer.Write(0);
                writer.Write(width * height * 4);
                writer.Write(0);
                writer.Write(0);
                writer.Write(0);
                writer.Write(0);
                for (int y = height - 1; y >= 0; y--) writer.Write(pixels, y * data.Stride, width * 4);
                int maskStride = ((width + 31) / 32) * 4;
                writer.Write(new byte[maskStride * height]);
                writer.Flush();
                return stream.ToArray();
            }
        }

        public static void SaveIco(string path, Bitmap image)
        {
            int[] sizes = new int[] { 256, 48, 32, 16 };
            List<byte[]> frames = new List<byte[]>();
            foreach (int size in sizes)
            {
                using (Bitmap frame = new Bitmap(size, size, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(frame))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.SmoothingMode = SmoothingMode.HighQuality;
                        g.CompositingQuality = CompositingQuality.HighQuality;
                        g.CompositingMode = CompositingMode.SourceCopy;
                        using (ImageAttributes attributes = new ImageAttributes())
                        {
                            attributes.SetWrapMode(WrapMode.TileFlipXY);
                            g.DrawImage(image, new Rectangle(0, 0, size, size), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
                        }
                    }
                    frames.Add(ToDib(frame));
                }
            }
            using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write((ushort)0);
                writer.Write((ushort)1);
                writer.Write((ushort)frames.Count);
                int offset = 6 + 16 * frames.Count;
                for (int i = 0; i < frames.Count; i++)
                {
                    int size = sizes[i];
                    writer.Write((byte)(size >= 256 ? 0 : size));
                    writer.Write((byte)(size >= 256 ? 0 : size));
                    writer.Write((byte)0);
                    writer.Write((byte)0);
                    writer.Write((ushort)1);
                    writer.Write((ushort)32);
                    writer.Write(frames[i].Length);
                    writer.Write(offset);
                    offset += frames[i].Length;
                }
                foreach (byte[] frame in frames) writer.Write(frame);
            }
        }
    }

    internal static class Store
    {
        public static string Root
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopIconChanger"); }
        }

        public static string IconsDir
        {
            get { return Path.Combine(Root, "icons"); }
        }

        public static string IniDir
        {
            get { return Path.Combine(Root, "ini"); }
        }

        public static string DataFile
        {
            get { return Path.Combine(Root, "backup.dat"); }
        }

        public static void Ensure()
        {
            Directory.CreateDirectory(IconsDir);
            Directory.CreateDirectory(IniDir);
        }

        public static string Hash(string text)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(text));
                StringBuilder builder = new StringBuilder();
                foreach (byte b in bytes) builder.Append(b.ToString("x2"));
                return builder.ToString();
            }
        }

        public static Dictionary<string, string[]> Load()
        {
            Dictionary<string, string[]> map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(DataFile)) return map;
            try
            {
                foreach (string line in File.ReadAllLines(DataFile, Encoding.UTF8))
                {
                    if (line.Trim().Length == 0) continue;
                    string[] parts = line.Split('\t');
                    if (parts.Length < 3) continue;
                    map[parts[1]] = parts;
                }
            }
            catch
            {
            }
            return map;
        }

        public static void Save(Dictionary<string, string[]> map)
        {
            Ensure();
            List<string> lines = new List<string>();
            foreach (string[] parts in map.Values) lines.Add(string.Join("\t", parts));
            File.WriteAllLines(DataFile, lines.ToArray(), Encoding.UTF8);
        }

        public static string BackupIni(string folder)
        {
            string ini = Path.Combine(folder, "desktop.ini");
            if (!File.Exists(ini)) return string.Empty;
            Ensure();
            string target = Path.Combine(IniDir, Hash(folder) + ".ini");
            File.Copy(ini, target, true);
            File.SetAttributes(target, FileAttributes.Normal);
            return target;
        }
    }

    // ===== 换肤：淡黄底色 + 云母卡片（新增） =====
    internal static class UiTheme
    {
        public static readonly Color PageBg = Color.FromArgb(255, 251, 240);
        public static readonly Color CardBg = Color.FromArgb(255, 253, 247);
        public static readonly Color CardBorder = Color.FromArgb(228, 212, 175);
        public static readonly Color Ink = Color.FromArgb(47, 42, 32);
        public static readonly Color Ink2 = Color.FromArgb(107, 99, 80);
        public static readonly Color Ink3 = Color.FromArgb(156, 146, 122);
        public static readonly Color Accent = Color.FromArgb(201, 162, 39);
        public static readonly Color AccentDeep = Color.FromArgb(138, 106, 20);
        public static readonly Color AccentSoft = Color.FromArgb(250, 240, 211);
        public static readonly Color CanvasBg = Color.FromArgb(251, 247, 236);
        public static readonly Color HeaderBg = Color.FromArgb(251, 243, 221);
        public static readonly Color RowSel = Color.FromArgb(247, 231, 184);
        public static readonly Color ChipIdle = Color.FromArgb(253, 249, 240);

        public static GraphicsPath RoundRect(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = Math.Max(1, Math.Min(radius * 2, Math.Min(rect.Width, rect.Height)));
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRound(Graphics g, Rectangle rect, int radius, Color fill)
        {
            using (GraphicsPath path = RoundRect(rect, radius))
            using (SolidBrush brush = new SolidBrush(fill))
            {
                g.FillPath(brush, path);
            }
        }

        public static void DrawRound(Graphics g, Rectangle rect, int radius, Color fill, Color border)
        {
            using (GraphicsPath path = RoundRect(rect, radius))
            {
                using (SolidBrush brush = new SolidBrush(fill))
                {
                    g.FillPath(brush, path);
                }
                if (border.A > 0)
                {
                    using (Pen pen = new Pen(border, 1f))
                    {
                        g.DrawPath(pen, path);
                    }
                }
            }
        }

        // 柔光斑：中心色向四周隐去，用于地面光斑
        public static void DrawGlow(Graphics g, Rectangle bounds, Color center)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddEllipse(bounds);
                using (PathGradientBrush brush = new PathGradientBrush(path))
                {
                    brush.CenterColor = center;
                    brush.SurroundColors = new Color[] { Color.FromArgb(0, center) };
                    g.FillEllipse(brush, bounds);
                }
            }
        }
    }

    // 圆角卡片容器：白底 + 细边 + 轻投影
    internal class SoftPanel : Panel
    {
        public int Radius = 14;

        public SoftPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = UiTheme.PageBg;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(UiTheme.PageBg);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle card = new Rectangle(0, 0, Width - 1, Height - 1);
            for (int i = 3; i >= 1; i--)
            {
                Rectangle shadow = new Rectangle(card.X + i, card.Y + i + 1, card.Width - i * 2, card.Height - i * 2);
                if (shadow.Width <= 0 || shadow.Height <= 0) continue;
                UiTheme.FillRound(e.Graphics, shadow, Radius, Color.FromArgb(7 + i * 2, 138, 110, 32));
            }
            UiTheme.DrawRound(e.Graphics, new Rectangle(1, 1, Width - 3, Height - 3), Radius, UiTheme.CardBg, UiTheme.CardBorder);
            base.OnPaint(e);
        }
    }

    // 圆角按钮：Filled = 主按钮（暖金填充），否则描边按钮
    internal class RoundButton : Button
    {
        public int Radius = 10;
        public bool Filled = true;
        private bool hover;
        private bool press;

        public RoundButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            BackColor = UiTheme.CardBg;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; press = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs mevent) { press = true; Invalidate(); base.OnMouseDown(mevent); }
        protected override void OnMouseUp(MouseEventArgs mevent) { press = false; Invalidate(); base.OnMouseUp(mevent); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent != null ? Parent.BackColor : UiTheme.PageBg);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle box = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill;
            Color border;
            Color ink;
            if (Filled)
            {
                fill = press ? Color.FromArgb(176, 139, 27) : (hover ? Color.FromArgb(214, 175, 52) : UiTheme.Accent);
                border = Color.FromArgb(0, 0, 0, 0);
                ink = Color.White;
            }
            else
            {
                fill = press ? Color.FromArgb(245, 236, 214) : (hover ? Color.FromArgb(252, 246, 232) : Color.FromArgb(255, 253, 247));
                border = hover ? UiTheme.Accent : UiTheme.CardBorder;
                ink = hover || press ? UiTheme.AccentDeep : UiTheme.Ink2;
            }
            UiTheme.DrawRound(e.Graphics, box, Radius, fill, border);
            TextRenderer.DrawText(e.Graphics, Text, Font, box, ink,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    internal class MainForm : Form
    {
        private readonly List<IconEntry> entries = new List<IconEntry>();
        private readonly List<List<UndoRecord>> undoStack = new List<List<UndoRecord>>();
        private readonly ImageList thumbs = new ImageList();
        private ListView list;
        private PictureBox canvas;
        private PictureBox previewBox;
        private Label lblImage;
        private Label lblStatus;
        private Button btnPick;
        private Button btnApply;
        private Button btnRestore;
        private Button btnResetCrop;
        private RadioButton rbSquare;
        private RadioButton rbCircle;
        private RadioButton rbRound;
        private RadioButton rbCut;
        private string currentIcon;
        private Bitmap fullImage;
        private Bitmap sourceImage;
        private RectangleF crop;
        private int dragMode;
        private Point dragStart;
        private RectangleF dragRect;
        private int shapeMode;
        // 新增：圆角矩形的圆角半径占边长的比例（按桌面常见应用图标量得 18%~22%，取中值）
        private const float RoundCornerRatio = 0.20f;

        public MainForm()
        {
            Text = "桌面图标一键修改";
            ClientSize = new Size(940, 900);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei", 9F);
            BackColor = UiTheme.PageBg;
            DoubleBuffered = true;
            AllowDrop = true;
            KeyPreview = true;
            DragEnter += FormDragEnter;
            DragDrop += FormDragDrop;
            KeyDown += FormKeyDown;
            BuildUi();
            StyleChips();
            LoadDesktop();
        }

        private void BuildUi()
        {
            // ===== 换肤：淡黄底色 + 云母卡片布局（整段重写） =====
            Label title = new Label();
            title.Location = new Point(30, 16);
            title.Size = new Size(600, 30);
            title.Text = "桌面图标一键修改";
            title.Font = new Font("Microsoft YaHei", 16F, FontStyle.Bold);
            title.ForeColor = UiTheme.Ink;
            Controls.Add(title);

            Label subtitle = new Label();
            subtitle.Location = new Point(30, 48);
            subtitle.Size = new Size(700, 22);
            subtitle.Text = "选一张图片，拖出裁剪框，挑一种形状，一键应用到桌面快捷方式或文件夹。";
            subtitle.ForeColor = UiTheme.Ink3;
            Controls.Add(subtitle);

            btnPick = new RoundButton();
            btnPick.Text = "选择图片";
            btnPick.Location = new Point(734, 16);
            btnPick.Size = new Size(174, 44);
            btnPick.Font = new Font("Microsoft YaHei", 11F, FontStyle.Bold);
            ((RoundButton)btnPick).Radius = 12;
            ((RoundButton)btnPick).Filled = true;
            btnPick.Click += btnPickClick;
            Controls.Add(btnPick);

            // 卡片一：裁剪画布 + 形状 + 预览
            SoftPanel cardImage = new SoftPanel();
            cardImage.Location = new Point(28, 84);
            cardImage.Size = new Size(884, 296);
            Controls.Add(cardImage);

            canvas = new PictureBox();
            canvas.Location = new Point(22, 22);
            canvas.Size = new Size(260, 260);
            canvas.BorderStyle = BorderStyle.None;
            canvas.BackColor = UiTheme.CanvasBg;
            canvas.Cursor = Cursors.Cross;
            canvas.Paint += canvasPaint;
            canvas.MouseDown += canvasMouseDown;
            canvas.MouseMove += canvasMouseMove;
            canvas.MouseUp += canvasMouseUp;
            canvas.MouseWheel += canvasMouseWheel;
            cardImage.Controls.Add(canvas);

            lblImage = new Label();
            lblImage.Location = new Point(300, 22);
            lblImage.Size = new Size(560, 24);
            lblImage.Text = "还没有选择图片";
            lblImage.Font = new Font("Microsoft YaHei", 10.5F, FontStyle.Bold);
            lblImage.ForeColor = UiTheme.Ink;
            cardImage.Controls.Add(lblImage);

            Label hint = new Label();
            hint.Location = new Point(300, 48);
            hint.Size = new Size(560, 40);
            hint.Text = "支持 JPG / PNG / BMP / GIF。" + Environment.NewLine + "裁剪框始终 1:1：框内拖动移动，边角拖动缩放，滚轮整体缩放。";
            hint.ForeColor = UiTheme.Ink3;
            cardImage.Controls.Add(hint);

            Label lblShape = new Label();
            lblShape.Location = new Point(300, 96);
            lblShape.Size = new Size(200, 18);
            lblShape.Text = "图标形状";
            lblShape.ForeColor = UiTheme.Ink3;
            cardImage.Controls.Add(lblShape);

            rbSquare = NewChip("方形", 300);
            rbSquare.Checked = true;
            rbSquare.CheckedChanged += rbShapeChanged;
            cardImage.Controls.Add(rbSquare);

            rbCircle = NewChip("圆形", 404);
            rbCircle.CheckedChanged += rbShapeChanged;
            cardImage.Controls.Add(rbCircle);

            rbRound = NewChip("圆角", 508);
            rbRound.CheckedChanged += rbShapeChanged;
            cardImage.Controls.Add(rbRound);

            rbCut = NewChip("智能抠图", 612);
            rbCut.Size = new Size(116, 34);
            rbCut.CheckedChanged += rbShapeChanged;
            cardImage.Controls.Add(rbCut);

            SoftPanel previewCard = new SoftPanel();
            previewCard.Location = new Point(300, 164);
            previewCard.Size = new Size(76, 76);
            previewCard.Radius = 12;
            cardImage.Controls.Add(previewCard);

            previewBox = new PictureBox();
            previewBox.Location = new Point(6, 6);
            previewBox.Size = new Size(64, 64);
            previewBox.SizeMode = PictureBoxSizeMode.Zoom;
            previewBox.BorderStyle = BorderStyle.None;
            previewBox.BackColor = Color.White;
            previewCard.Controls.Add(previewBox);

            Label lblPreview = new Label();
            lblPreview.Location = new Point(388, 192);
            lblPreview.Size = new Size(200, 20);
            lblPreview.Text = "实际效果预览";
            lblPreview.ForeColor = UiTheme.Ink3;
            cardImage.Controls.Add(lblPreview);

            btnResetCrop = new RoundButton();
            btnResetCrop.Text = "重置裁剪";
            btnResetCrop.Location = new Point(300, 246);
            btnResetCrop.Size = new Size(140, 34);
            btnResetCrop.Font = new Font("Microsoft YaHei", 9.5F);
            ((RoundButton)btnResetCrop).Radius = 9;
            ((RoundButton)btnResetCrop).Filled = false;
            btnResetCrop.Click += btnResetCropClick;
            cardImage.Controls.Add(btnResetCrop);
            // ===== 换肤布局结束 =====

            thumbs.ColorDepth = ColorDepth.Depth32Bit;
            thumbs.ImageSize = new Size(28, 28);

            SoftPanel cardList = new SoftPanel();
            cardList.Location = new Point(28, 396);
            cardList.Size = new Size(884, 344);
            Controls.Add(cardList);

            list = new ListView();
            list.Location = new Point(16, 16);
            list.Size = new Size(852, 312);
            list.View = View.Details;
            list.FullRowSelect = true;
            list.MultiSelect = true;
            list.HideSelection = false;
            list.SmallImageList = thumbs;
            list.BorderStyle = BorderStyle.None;
            list.BackColor = Color.White;
            list.OwnerDraw = true;
            list.DrawColumnHeader += listDrawColumnHeader;
            list.DrawItem += listDrawItem;
            list.DrawSubItem += listDrawSubItem;
            list.Columns.Add("名称", 440);
            list.Columns.Add("类型", 110);
            list.Columns.Add("来源", 280);
            cardList.Controls.Add(list);

            SoftPanel cardActions = new SoftPanel();
            cardActions.Location = new Point(28, 756);
            cardActions.Size = new Size(884, 74);
            Controls.Add(cardActions);

            btnApply = new RoundButton();
            btnApply.Text = "应用修改";
            btnApply.Location = new Point(16, 14);
            btnApply.Size = new Size(418, 46);
            btnApply.Font = new Font("Microsoft YaHei", 11F, FontStyle.Bold);
            ((RoundButton)btnApply).Radius = 11;
            ((RoundButton)btnApply).Filled = true;
            btnApply.Click += btnApplyClick;
            cardActions.Controls.Add(btnApply);

            btnRestore = new RoundButton();
            btnRestore.Text = "恢复默认";
            btnRestore.Location = new Point(450, 14);
            btnRestore.Size = new Size(418, 46);
            btnRestore.Font = new Font("Microsoft YaHei", 11F, FontStyle.Bold);
            ((RoundButton)btnRestore).Radius = 11;
            ((RoundButton)btnRestore).Filled = false;
            btnRestore.Click += btnRestoreClick;
            cardActions.Controls.Add(btnRestore);

            lblStatus = new Label();
            lblStatus.Location = new Point(30, 844);
            lblStatus.Size = new Size(880, 22);
            lblStatus.Text = "已就绪";
            lblStatus.ForeColor = UiTheme.Ink2;
            lblStatus.AutoEllipsis = true;
            Controls.Add(lblStatus);

            Label undoHint = new Label();
            undoHint.Location = new Point(30, 866);
            undoHint.Size = new Size(880, 20);
            undoHint.Text = "Ctrl+Z 撤销上一步（保留最近 20 步）";
            undoHint.ForeColor = UiTheme.Ink3;
            Controls.Add(undoHint);
        }

        // 形状选项：外观做成圆角胶囊，选中时用暖金描边与浅金底（新增）
        private RadioButton NewChip(string text, int x)
        {
            RadioButton chip = new RadioButton();
            chip.Appearance = Appearance.Button;
            chip.FlatStyle = FlatStyle.Flat;
            chip.FlatAppearance.BorderSize = 1;
            chip.AutoSize = false;
            chip.Location = new Point(x, 118);
            chip.Size = new Size(96, 34);
            chip.Text = text;
            chip.TextAlign = ContentAlignment.MiddleCenter;
            chip.Font = new Font("Microsoft YaHei", 9.5F);
            chip.Cursor = Cursors.Hand;
            StyleChip(chip);
            return chip;
        }

        private void StyleChip(RadioButton chip)
        {
            if (chip.Checked)
            {
                chip.BackColor = UiTheme.AccentSoft;
                chip.ForeColor = UiTheme.AccentDeep;
                chip.FlatAppearance.BorderColor = UiTheme.Accent;
            }
            else
            {
                chip.BackColor = UiTheme.ChipIdle;
                chip.ForeColor = UiTheme.Ink2;
                chip.FlatAppearance.BorderColor = UiTheme.CardBorder;
            }
        }

        private void StyleChips()
        {
            StyleChip(rbSquare);
            StyleChip(rbCircle);
            StyleChip(rbRound);
            StyleChip(rbCut);
        }

        // ===== 换肤：列表自绘（表头暖米色、选中浅金）新增 =====
        private void listDrawColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            using (SolidBrush back = new SolidBrush(UiTheme.HeaderBg))
            {
                e.Graphics.FillRectangle(back, e.Bounds);
            }
            using (Pen line = new Pen(UiTheme.CardBorder))
            {
                e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            }
            using (Font font = new Font("Microsoft YaHei", 9F, FontStyle.Bold))
            using (SolidBrush ink = new SolidBrush(UiTheme.Ink2))
            {
                e.Graphics.DrawString(e.Header.Text, font, ink, e.Bounds.Left + 12, e.Bounds.Top + (e.Bounds.Height - 18) / 2f);
            }
        }

        private void listDrawItem(object sender, DrawListViewItemEventArgs e)
        {
            e.DrawDefault = false;
            Color back = e.Item.Selected ? UiTheme.RowSel : Color.White;
            using (SolidBrush brush = new SolidBrush(back))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }
        }

        private void listDrawSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            Color back = e.Item.Selected ? UiTheme.RowSel : Color.White;
            using (SolidBrush brush = new SolidBrush(back))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }
            int left = e.Bounds.Left + 12;
            if (e.ColumnIndex == 0 && e.Item.ImageIndex >= 0 && e.Item.ImageIndex < thumbs.Images.Count)
            {
                e.Graphics.DrawImage(thumbs.Images[e.Item.ImageIndex], new Rectangle(e.Bounds.Left + 10, e.Bounds.Top + 2, 24, 24));
                left = e.Bounds.Left + 42;
            }
            Color ink = e.Item.Selected ? UiTheme.Ink : (e.ColumnIndex == 0 ? UiTheme.Ink : UiTheme.Ink2);
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, e.Item.Font, new Rectangle(left, e.Bounds.Top, e.Bounds.Width - (left - e.Bounds.Left) - 6, e.Bounds.Height), ink,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            using (Pen line = new Pen(Color.FromArgb(240, 233, 214)))
            {
                e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            }
        }

        // 地面光斑：窗口底部两枚柔光（新增）
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            UiTheme.DrawGlow(e.Graphics, new Rectangle(-160, ClientSize.Height - 300, 640, 360), Color.FromArgb(96, 255, 214, 102));
            UiTheme.DrawGlow(e.Graphics, new Rectangle(ClientSize.Width - 520, ClientSize.Height - 280, 600, 340), Color.FromArgb(96, 255, 236, 179));
        }

        private void LoadDesktop()
        {
            entries.Clear();
            list.Items.Clear();
            thumbs.Images.Clear();
            SetStatus("正在读取桌面图标…");

            List<string> folders = new List<string>();
            folders.Add(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
            folders.Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));

            List<IconEntry> found = new List<IconEntry>();
            foreach (string folder in folders)
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) continue;
                string source = folder == folders[0] ? "桌面" : "公共桌面";
                try
                {
                    foreach (string dir in Directory.GetDirectories(folder))
                    {
                        FileAttributes attributes = File.GetAttributes(dir);
                        if ((attributes & FileAttributes.Hidden) == FileAttributes.Hidden) continue;
                        if ((attributes & FileAttributes.System) == FileAttributes.System) continue;
                        IconEntry entry = new IconEntry();
                        entry.Path = dir;
                        entry.Name = Path.GetFileName(dir);
                        entry.IsFolder = true;
                        entry.Source = source;
                        found.Add(entry);
                    }
                }
                catch
                {
                }
                try
                {
                    foreach (string file in Directory.GetFiles(folder, "*.lnk"))
                    {
                        IconEntry entry = new IconEntry();
                        entry.Path = file;
                        entry.Name = Path.GetFileNameWithoutExtension(file);
                        entry.IsFolder = false;
                        entry.Source = source;
                        found.Add(entry);
                    }
                }
                catch
                {
                }
            }

            found.Sort(delegate(IconEntry a, IconEntry b) { return string.Compare(a.Name, b.Name, StringComparison.CurrentCulture); });

            foreach (IconEntry entry in found)
            {
                entries.Add(entry);
                Bitmap icon = GetDisplayIcon(entry, 28);
                int imageIndex = -1;
                if (icon != null)
                {
                    thumbs.Images.Add(icon);
                    imageIndex = thumbs.Images.Count - 1;
                }
                ListViewItem row = new ListViewItem(new string[] { entry.Name, entry.IsFolder ? "文件夹" : "快捷方式", entry.Source }, imageIndex);
                row.Tag = entry;
                entry.Row = row;
                list.Items.Add(row);
            }

            SetStatus("共读取到 " + entries.Count + " 个桌面图标");
        }

        private Bitmap GetDisplayIcon(IconEntry entry, int size)
        {
            try
            {
                if (entry.IsFolder)
                {
                    int index;
                    string iconPath = IconFile.SplitLocation(ReadFolderIconLocation(entry.Path), out index);
                    Bitmap bitmap = IconFile.FromResource(iconPath, index, size);
                    if (bitmap != null) return bitmap;
                    return IconFile.System(3, size);
                }
                object shortcut = Shortcut.Open(entry.Path);
                int iconIndex;
                string location = IconFile.SplitLocation(Shortcut.Get(shortcut, "IconLocation"), out iconIndex);
                Bitmap result = IconFile.FromResource(location, iconIndex, size);
                if (result != null) return result;
                string target = Shortcut.Get(shortcut, "TargetPath");
                result = IconFile.FromResource(target, 0, size);
                if (result != null) return result;
                return IconFile.System(1, size);
            }
            catch
            {
                return IconFile.System(1, size);
            }
        }

        private static string ReadFolderIconLocation(string folder)
        {
            string ini = Path.Combine(folder, "desktop.ini");
            if (!File.Exists(ini)) return null;
            try
            {
                foreach (string raw in File.ReadAllLines(ini))
                {
                    string line = raw.Trim();
                    if (line.StartsWith("IconResource=", StringComparison.OrdinalIgnoreCase))
                        return line.Substring("IconResource=".Length).Trim();
                }
            }
            catch
            {
            }
            return null;
        }

        private void ApplyIconTo(string path, bool isFolder, string iconPath)
        {
            if (isFolder)
            {
                WriteFolderIcon(path, iconPath);
            }
            else
            {
                object shortcut = Shortcut.Open(path);
                Shortcut.Set(shortcut, "IconLocation", iconPath + ",0");
                Shortcut.Save(shortcut);
            }
            Native.SHChangeNotifyPath(Native.SHCNE_UPDATEITEM, Native.SHCNF_PATHW, path, IntPtr.Zero);
        }

        private static void WriteFolderIcon(string folder, string iconPath)
        {
            string ini = Path.Combine(folder, "desktop.ini");
            List<string> lines = new List<string>();
            if (File.Exists(ini))
            {
                try { lines.AddRange(File.ReadAllLines(ini)); } catch { }
            }
            string resource = "IconResource=" + iconPath + ",0";
            int sectionIndex = -1;
            int resourceIndex = -1;
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i].Trim();
                if (sectionIndex < 0 && line.Equals("[.ShellClassInfo]", StringComparison.OrdinalIgnoreCase)) sectionIndex = i;
                if (line.StartsWith("IconResource=", StringComparison.OrdinalIgnoreCase)) { resourceIndex = i; break; }
            }
            if (resourceIndex >= 0) lines[resourceIndex] = resource;
            else if (sectionIndex >= 0) lines.Insert(sectionIndex + 1, resource);
            else
            {
                lines.Insert(0, resource);
                lines.Insert(0, "[.ShellClassInfo]");
            }

            if (File.Exists(ini)) File.SetAttributes(ini, FileAttributes.Normal);
            File.WriteAllLines(ini, lines.ToArray(), Encoding.Unicode);
            File.SetAttributes(ini, FileAttributes.Hidden | FileAttributes.System);

            FileAttributes folderAttributes = File.GetAttributes(folder);
            if ((folderAttributes & FileAttributes.ReadOnly) != FileAttributes.ReadOnly)
                File.SetAttributes(folder, folderAttributes | FileAttributes.ReadOnly);
        }

        private UndoRecord CaptureState(IconEntry entry)
        {
            UndoRecord record = new UndoRecord();
            record.Path = entry.Path;
            record.IsFolder = entry.IsFolder;
            if (entry.IsFolder)
            {
                string ini = Path.Combine(entry.Path, "desktop.ini");
                record.OldFolderAttrs = File.GetAttributes(entry.Path);
                record.OldHadIni = File.Exists(ini);
                if (record.OldHadIni)
                {
                    try { record.OldIni = File.ReadAllBytes(ini); } catch { record.OldIni = null; }
                }
            }
            else
            {
                object shortcut = Shortcut.Open(entry.Path);
                record.OldIconLocation = Shortcut.Get(shortcut, "IconLocation");
            }
            return record;
        }

        private void RevertRecord(UndoRecord record)
        {
            if (record.IsFolder)
            {
                string ini = Path.Combine(record.Path, "desktop.ini");
                try
                {
                    if (record.OldHadIni && record.OldIni != null)
                    {
                        File.SetAttributes(ini, FileAttributes.Normal);
                        File.WriteAllBytes(ini, record.OldIni);
                        File.SetAttributes(ini, FileAttributes.Hidden | FileAttributes.System);
                    }
                    else if (File.Exists(ini))
                    {
                        File.SetAttributes(ini, FileAttributes.Normal);
                        File.Delete(ini);
                    }
                    File.SetAttributes(record.Path, record.OldFolderAttrs);
                }
                catch
                {
                }
            }
            else
            {
                try
                {
                    object shortcut = Shortcut.Open(record.Path);
                    Shortcut.Set(shortcut, "IconLocation", record.OldIconLocation ?? string.Empty);
                    Shortcut.Save(shortcut);
                }
                catch
                {
                }
            }
            Native.SHChangeNotifyPath(Native.SHCNE_UPDATEITEM, Native.SHCNF_PATHW, record.Path, IntPtr.Zero);
        }

        private void RefreshRow(IconEntry entry)
        {
            if (entry.Row == null) return;
            Bitmap icon = GetDisplayIcon(entry, 28);
            if (icon == null) return;
            int index = entry.Row.ImageIndex;
            if (index >= 0 && index < thumbs.Images.Count)
            {
                thumbs.Images[index] = icon;
            }
            else
            {
                thumbs.Images.Add(icon);
                entry.Row.ImageIndex = thumbs.Images.Count - 1;
            }
        }

        private void RefreshShell()
        {
            Native.SHChangeNotifyIdList(Native.SHCNE_ASSOCCHANGED, Native.SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
        }

        private void btnPickClick(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "选择图片";
                dialog.Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.gif|所有文件|*.*";
                if (dialog.ShowDialog(this) == DialogResult.OK) LoadImageFile(dialog.FileName);
            }
        }

        private void LoadImageFile(string path)
        {
            try
            {
                Bitmap loaded;
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read))
                using (Image image = Image.FromStream(stream))
                {
                    loaded = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb);
                    using (Graphics g = Graphics.FromImage(loaded))
                    {
                        g.CompositingMode = CompositingMode.SourceCopy;
                        g.DrawImage(image, new Rectangle(0, 0, image.Width, image.Height));
                    }
                }

                if (fullImage != null) fullImage.Dispose();
                fullImage = loaded;
                lblImage.Text = "当前图片：" + Path.GetFileName(path) + "（" + fullImage.Width + " × " + fullImage.Height + "）";
                dragMode = 0;
                ResetCrop();
                UpdatePreview(true);
                canvas.Invalidate();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "图片读取失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ===== 新增：裁剪框（默认居中、边长取图片较短边） =====
        private void ResetCrop()
        {
            if (fullImage == null) return;
            float side = Math.Min(fullImage.Width, fullImage.Height);
            crop = new RectangleF((fullImage.Width - side) / 2f, (fullImage.Height - side) / 2f, side, side);
        }

        private RectangleF DisplayRect()
        {
            if (fullImage == null) return RectangleF.Empty;
            float scale = DisplayScale();
            float w = fullImage.Width * scale;
            float h = fullImage.Height * scale;
            return new RectangleF((canvas.ClientSize.Width - w) / 2f, (canvas.ClientSize.Height - h) / 2f, w, h);
        }

        private float DisplayScale()
        {
            if (fullImage == null) return 1f;
            return Math.Min((float)canvas.ClientSize.Width / fullImage.Width, (float)canvas.ClientSize.Height / fullImage.Height);
        }

        private RectangleF CropToDisplay()
        {
            RectangleF r = DisplayRect();
            float s = DisplayScale();
            return new RectangleF(r.X + crop.X * s, r.Y + crop.Y * s, crop.Width * s, crop.Height * s);
        }

        private RectangleF ClampCrop(RectangleF r)
        {
            float side = Math.Min(r.Width, Math.Min(fullImage.Width, fullImage.Height));
            float minSide = Math.Max(8f, Math.Min(fullImage.Width, fullImage.Height) * 0.05f);
            if (side < minSide) side = minSide;
            float x = r.X;
            float y = r.Y;
            if (x < 0f) x = 0f;
            if (y < 0f) y = 0f;
            if (x + side > fullImage.Width) x = fullImage.Width - side;
            if (y + side > fullImage.Height) y = fullImage.Height - side;
            if (x < 0f) x = 0f;
            if (y < 0f) y = 0f;
            return new RectangleF(x, y, side, side);
        }

        private static PointF[] HandlePoints(RectangleF c)
        {
            return new PointF[]
            {
                new PointF(c.Left, c.Top),
                new PointF(c.Left + c.Width / 2f, c.Top),
                new PointF(c.Right, c.Top),
                new PointF(c.Right, c.Top + c.Height / 2f),
                new PointF(c.Right, c.Bottom),
                new PointF(c.Left + c.Width / 2f, c.Bottom),
                new PointF(c.Left, c.Bottom),
                new PointF(c.Left, c.Top + c.Height / 2f)
            };
        }

        private static int HitHandle(Point p, RectangleF c)
        {
            PointF[] points = HandlePoints(c);
            for (int i = 0; i < points.Length; i++)
            {
                if (Math.Abs(p.X - points[i].X) <= 6f && Math.Abs(p.Y - points[i].Y) <= 6f) return i;
            }
            return -1;
        }

        private static Cursor HandleCursor(int handle)
        {
            if (handle == 0 || handle == 4) return Cursors.SizeNWSE;
            if (handle == 2 || handle == 6) return Cursors.SizeNESW;
            if (handle == 1 || handle == 5) return Cursors.SizeNS;
            return Cursors.SizeWE;
        }

        private void canvasPaint(object sender, PaintEventArgs e)
        {
            e.Graphics.Clear(canvas.BackColor);
            if (fullImage == null)
            {
                TextRenderer.DrawText(e.Graphics, "选择图片后，在这里拖动裁剪框", canvas.Font, canvas.ClientRectangle,
                    Color.FromArgb(150, 156, 172), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            RectangleF d = DisplayRect();
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            e.Graphics.DrawImage(fullImage, d);

            RectangleF c = CropToDisplay();
            using (Region dark = new Region(d))
            {
                dark.Exclude(c);
                using (SolidBrush shade = new SolidBrush(Color.FromArgb(130, 18, 22, 34)))
                {
                    e.Graphics.FillRegion(shade, dark);
                }
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen border = new Pen(Color.White, 1.6f))
            {
                e.Graphics.DrawRectangle(border, c.X, c.Y, c.Width, c.Height);
            }
            using (Pen guide = new Pen(Color.FromArgb(150, 255, 255, 255)))
            {
                guide.DashStyle = DashStyle.Dash;
                e.Graphics.DrawLine(guide, c.X + c.Width / 3f, c.Y, c.X + c.Width / 3f, c.Bottom);
                e.Graphics.DrawLine(guide, c.X + c.Width * 2f / 3f, c.Y, c.X + c.Width * 2f / 3f, c.Bottom);
                e.Graphics.DrawLine(guide, c.X, c.Y + c.Height / 3f, c.Right, c.Y + c.Height / 3f);
                e.Graphics.DrawLine(guide, c.X, c.Y + c.Height * 2f / 3f, c.Right, c.Y + c.Height * 2f / 3f);
            }
            foreach (PointF p in HandlePoints(c))
            {
                e.Graphics.FillRectangle(Brushes.White, p.X - 3.5f, p.Y - 3.5f, 7f, 7f);
                e.Graphics.DrawRectangle(Pens.SteelBlue, p.X - 3.5f, p.Y - 3.5f, 7f, 7f);
            }
        }

        private void canvasMouseDown(object sender, MouseEventArgs e)
        {
            if (fullImage == null || e.Button != MouseButtons.Left) return;
            RectangleF c = CropToDisplay();
            int handle = HitHandle(e.Location, c);
            if (handle >= 0) dragMode = 2 + handle;
            else if (c.Contains(e.Location)) dragMode = 1;
            else return;
            dragStart = e.Location;
            dragRect = crop;
            canvas.Capture = true;
        }

        private void canvasMouseMove(object sender, MouseEventArgs e)
        {
            if (fullImage == null) return;
            if (dragMode == 0)
            {
                RectangleF c = CropToDisplay();
                int handle = HitHandle(e.Location, c);
                canvas.Cursor = handle >= 0 ? HandleCursor(handle) : (c.Contains(e.Location) ? Cursors.SizeAll : Cursors.Cross);
                return;
            }

            float scale = DisplayScale();
            float dx = (e.X - dragStart.X) / scale;
            float dy = (e.Y - dragStart.Y) / scale;
            crop = dragMode == 1 ? MoveCrop(dragRect, dx, dy) : ResizeCrop(dragRect, dragMode - 2, dx, dy);
            UpdatePreview(false);
        }

        private RectangleF MoveCrop(RectangleF start, float dx, float dy)
        {
            float x = start.X + dx;
            float y = start.Y + dy;
            if (x < 0f) x = 0f;
            if (y < 0f) y = 0f;
            if (x + start.Width > fullImage.Width) x = fullImage.Width - start.Width;
            if (y + start.Height > fullImage.Height) y = fullImage.Height - start.Height;
            return new RectangleF(x, y, start.Width, start.Height);
        }

        private RectangleF ResizeCrop(RectangleF start, int handle, float dx, float dy)
        {
            bool west = handle == 0 || handle == 6 || handle == 7;
            bool east = handle == 2 || handle == 3 || handle == 4;
            bool north = handle == 0 || handle == 1 || handle == 2;
            bool south = handle == 4 || handle == 5 || handle == 6;

            float side = start.Width;
            if ((west || east) && (north || south))
            {
                float sx = east ? 1f : -1f;
                float sy = south ? 1f : -1f;
                side += (sx * dx + sy * dy) / 2f;
            }
            else if (west || east)
            {
                side += east ? dx : -dx;
            }
            else
            {
                side += south ? dy : -dy;
            }

            float left;
            if (east) left = start.Right - side;
            else if (west) left = start.X;
            else left = start.X + start.Width / 2f - side / 2f;

            float top;
            if (south) top = start.Bottom - side;
            else if (north) top = start.Y;
            else top = start.Y + start.Height / 2f - side / 2f;

            return ClampCrop(new RectangleF(left, top, side, side));
        }

        private void canvasMouseUp(object sender, MouseEventArgs e)
        {
            if (dragMode == 0) return;
            dragMode = 0;
            canvas.Capture = false;
            UpdatePreview(true);
        }

        private void canvasMouseWheel(object sender, MouseEventArgs e)
        {
            if (fullImage == null) return;
            float minSide = Math.Max(8f, Math.Min(fullImage.Width, fullImage.Height) * 0.05f);
            float maxSide = Math.Min(fullImage.Width, fullImage.Height);
            float side = crop.Width * (e.Delta > 0 ? 0.92f : 1.08f);
            if (side < minSide) side = minSide;
            if (side > maxSide) side = maxSide;
            float cx = crop.X + crop.Width / 2f;
            float cy = crop.Y + crop.Height / 2f;
            crop = ClampCrop(new RectangleF(cx - side / 2f, cy - side / 2f, side, side));
            UpdatePreview(false);
        }

        private void btnResetCropClick(object sender, EventArgs e)
        {
            if (fullImage == null) return;
            ResetCrop();
            UpdatePreview(true);
            canvas.Invalidate();
        }

        // ===== 新增：按裁剪框实时预览 / 生成 ICO（commit=true 时才写文件） =====
        private void UpdatePreview(bool commit)
        {
            if (fullImage == null) return;
            try
            {
                RectangleF region = new RectangleF(crop.X, crop.Y, crop.Width, crop.Height);

                Bitmap square = IconFile.Crop(fullImage, region, 160);
                if (sourceImage != null) sourceImage.Dispose();
                sourceImage = square;

                Bitmap shown = ApplyShape(square);
                if (previewBox.Image != null)
                {
                    Image old = previewBox.Image;
                    previewBox.Image = null;
                    old.Dispose();
                }
                previewBox.Image = shown;
                canvas.Invalidate();

                if (!commit)
                {
                    SetStatus("裁剪 " + ((int)Math.Round(crop.Width)) + " × " + ((int)Math.Round(crop.Height)) + "（拖动中，松开鼠标后生成 ICO）");
                    return;
                }

                Bitmap high = IconFile.Crop(fullImage, region, 512);
                Bitmap render = ApplyShape(high);
                try
                {
                    Store.Ensure();
                    string iconPath = Path.Combine(Store.IconsDir, "icon_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".ico");
                    IconFile.SaveIco(iconPath, render);
                    currentIcon = iconPath;
                    SetStatus("ICO 已生成（" + ShapeName() + "，裁剪 " + ((int)Math.Round(crop.Width)) + "×" + ((int)Math.Round(crop.Height)) + "）：" + Path.GetFileName(iconPath));
                }
                finally
                {
                    if (render != high) render.Dispose();
                    high.Dispose();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "生成 ICO 失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        // ===== 裁剪框结束 =====

        // ===== 新增：按当前形状（方形 / 圆形 / 圆角 / 智能抠图）套蒙版 =====
        private Bitmap ApplyShape(Bitmap square)
        {
            if (shapeMode == 1) return IconFile.Circle(square);
            if (shapeMode == 2) return IconFile.RoundedSquare(square, RoundCornerRatio);
            if (shapeMode == 3) return IconFile.Cutout(square);
            return (Bitmap)square.Clone();
        }

        private string ShapeName()
        {
            if (shapeMode == 1) return "圆形";
            if (shapeMode == 2) return "圆角";
            if (shapeMode == 3) return "智能抠图";
            return "方形";
        }

        private void rbShapeChanged(object sender, EventArgs e)
        {
            RadioButton radio = sender as RadioButton;
            if (radio == null || !radio.Checked) return;
            StyleChips();
            int mode = 0;
            if (radio == rbCircle) mode = 1;
            else if (radio == rbRound) mode = 2;
            else if (radio == rbCut) mode = 3;
            if (mode == shapeMode) return;
            shapeMode = mode;
            if (fullImage != null) UpdatePreview(true);
        }

        private void btnApplyClick(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(currentIcon))
            {
                MessageBox.Show(this, "请先选择一个图片。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (list.SelectedItems.Count == 0)
            {
                MessageBox.Show(this, "请先在列表里选中要修改的图标（可按住 Ctrl 或 Shift 多选）。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            List<UndoRecord> batch = new List<UndoRecord>();
            int done = 0;
            int failed = 0;
            foreach (ListViewItem row in list.SelectedItems)
            {
                IconEntry entry = (IconEntry)row.Tag;
                try
                {
                    UndoRecord record = CaptureState(entry);
                    RecordBackup(entry);
                    ApplyIconTo(entry.Path, entry.IsFolder, currentIcon);
                    batch.Add(record);
                    RefreshRow(entry);
                    done++;
                }
                catch
                {
                    failed++;
                }
            }

            if (batch.Count > 0)
            {
                undoStack.Add(batch);
                while (undoStack.Count > 20) undoStack.RemoveAt(0);
            }
            RefreshShell();
            SetStatus("已修改 " + done + " 个图标" + (failed > 0 ? "，失败 " + failed + " 个" : ""));
        }

        private void btnRestoreClick(object sender, EventArgs e)
        {
            if (list.SelectedItems.Count == 0)
            {
                MessageBox.Show(this, "请先在列表里选中要还原的图标。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Dictionary<string, string[]> backup = Store.Load();
            List<UndoRecord> batch = new List<UndoRecord>();
            int done = 0;
            int failed = 0;

            foreach (ListViewItem row in list.SelectedItems)
            {
                IconEntry entry = (IconEntry)row.Tag;
                try
                {
                    UndoRecord record = CaptureState(entry);
                    RevertToBackup(entry, backup);
                    batch.Add(record);
                    RefreshRow(entry);
                    done++;
                }
                catch
                {
                    failed++;
                }
            }

            if (batch.Count > 0)
            {
                undoStack.Add(batch);
                while (undoStack.Count > 20) undoStack.RemoveAt(0);
            }
            RefreshShell();
            SetStatus("已还原 " + done + " 个图标" + (failed > 0 ? "，失败 " + failed + " 个" : ""));
        }

        private void RevertToBackup(IconEntry entry, Dictionary<string, string[]> backup)
        {
            string[] record;
            if (backup.TryGetValue(entry.Path, out record))
            {
                if (entry.IsFolder)
                {
                    RestoreFolder(entry.Path, record);
                }
                else
                {
                    object shortcut = Shortcut.Open(entry.Path);
                    Shortcut.Set(shortcut, "IconLocation", record.Length > 2 ? record[2] : string.Empty);
                    Shortcut.Save(shortcut);
                }
            }
            else
            {
                if (entry.IsFolder)
                {
                    string ini = Path.Combine(entry.Path, "desktop.ini");
                    if (File.Exists(ini))
                    {
                        File.SetAttributes(ini, FileAttributes.Normal);
                        File.Delete(ini);
                    }
                    FileAttributes attributes = File.GetAttributes(entry.Path);
                    if ((attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                        File.SetAttributes(entry.Path, attributes & ~FileAttributes.ReadOnly);
                }
                else
                {
                    object shortcut = Shortcut.Open(entry.Path);
                    Shortcut.Set(shortcut, "IconLocation", string.Empty);
                    Shortcut.Save(shortcut);
                }
            }
            Native.SHChangeNotifyPath(Native.SHCNE_UPDATEITEM, Native.SHCNF_PATHW, entry.Path, IntPtr.Zero);
        }

        private static void RestoreFolder(string folder, string[] record)
        {
            string ini = Path.Combine(folder, "desktop.ini");
            string iniBackup = Path.Combine(Store.IniDir, Store.Hash(folder) + ".ini");
            try
            {
                if (File.Exists(iniBackup))
                {
                    if (File.Exists(ini)) File.SetAttributes(ini, FileAttributes.Normal);
                    File.Copy(iniBackup, ini, true);
                    File.SetAttributes(ini, FileAttributes.Hidden | FileAttributes.System);
                }
                else if (File.Exists(ini))
                {
                    File.SetAttributes(ini, FileAttributes.Normal);
                    File.Delete(ini);
                }
                FileAttributes attributes = File.GetAttributes(folder);
                if ((attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                    File.SetAttributes(folder, attributes & ~FileAttributes.ReadOnly);
            }
            catch
            {
            }
        }

        private void RecordBackup(IconEntry entry)
        {
            Dictionary<string, string[]> backup = Store.Load();
            if (backup.ContainsKey(entry.Path)) return;
            Store.Ensure();
            if (entry.IsFolder)
            {
                string iniBackup = Store.BackupIni(entry.Path);
                backup[entry.Path] = new string[] { "F", entry.Path, iniBackup, ((int)File.GetAttributes(entry.Path)).ToString() };
            }
            else
            {
                object shortcut = Shortcut.Open(entry.Path);
                backup[entry.Path] = new string[] { "L", entry.Path, Shortcut.Get(shortcut, "IconLocation") };
            }
            Store.Save(backup);
        }

        private void UndoLast()
        {
            if (undoStack.Count == 0)
            {
                SetStatus("没有可撤销的操作");
                return;
            }
            List<UndoRecord> batch = undoStack[undoStack.Count - 1];
            undoStack.RemoveAt(undoStack.Count - 1);
            int count = 0;
            for (int i = batch.Count - 1; i >= 0; i--)
            {
                try
                {
                    RevertRecord(batch[i]);
                    count++;
                }
                catch
                {
                }
            }
            foreach (IconEntry entry in entries) RefreshRow(entry);
            RefreshShell();
            SetStatus("已撤销 " + count + " 个图标的修改");
        }

        private void SetStatus(string text)
        {
            lblStatus.Text = text;
        }

        private void FormDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
            else e.Effect = DragDropEffects.None;
        }

        private void FormDragDrop(object sender, DragEventArgs e)
        {
            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0) return;
            string file = files[0];
            string extension = Path.GetExtension(file).ToLowerInvariant();
            if (extension == ".jpg" || extension == ".jpeg" || extension == ".png" || extension == ".bmp" || extension == ".gif")
                LoadImageFile(file);
            else
                SetStatus("不支持的图片格式：" + extension);
        }

        private void FormKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.Z)
            {
                UndoLast();
                e.Handled = true;
            }
        }
    }
}
