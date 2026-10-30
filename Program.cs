using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PhotoDeduplicatorGUI
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
    }

    public class PhotoItem
    {
        public string FullPath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long Size { get; set; }
        public DateTime Date { get; set; }
    }

    public class MainForm : Form
    {
        private ListBox lstSources = null!;
        private Button btnAddSource = null!;
        private Button btnRemoveSource = null!;
        private Button btnClearSources = null!;
        private TextBox txtDest = null!;
        private Button btnSelectDest = null!;
        private Label lblDestFreeSpace = null!;
        private Label lblDestTip = null!;
        private Button btnStart = null!;
        private ProgressBar progressBar = null!;
        private Label lblStatus = null!;
        private TextBox txtLog = null!;

        private static readonly HashSet<string> ValidExts = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".heic", ".bmp", ".webp",
            ".tif", ".tiff", ".raw", ".cr2", ".nef", ".arw",
            ".mp4", ".mov"
        };

        private static readonly Regex ExifDateRegex = new(
            @"(\d{4}):(\d{2}):(\d{2})\s+(\d{2}):(\d{2}):(\d{2})",
            RegexOptions.Compiled);

        public MainForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "Windows 照片深度去重与智能年月归档工具 (GUI版)";
            Size = new Size(840, 760);
            MinimumSize = new Size(780, 680);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            BackColor = Color.FromArgb(248, 249, 250);

            // 1. 源文件夹区域
            var grpSource = new GroupBox
            {
                Text = "1. 源照片文件夹 (支持添加多个，支持直接将文件夹拖拽至下方列表)",
                Location = new Point(16, 12),
                Size = new Size(790, 175),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            lstSources = new ListBox
            {
                Location = new Point(16, 26),
                Size = new Size(640, 135),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                AllowDrop = true
            };
            lstSources.DragEnter += LstSources_DragEnter;
            lstSources.DragDrop += LstSources_DragDrop;

            btnAddSource = new Button
            {
                Text = "➕ 添加文件夹",
                Location = new Point(668, 26),
                Size = new Size(110, 36),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                BackColor = Color.FromArgb(235, 243, 250)
            };
            btnAddSource.Click += BtnAddSource_Click;

            btnRemoveSource = new Button
            {
                Text = "➖ 移除选中",
                Location = new Point(668, 72),
                Size = new Size(110, 36),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnRemoveSource.Click += (s, e) =>
            {
                if (lstSources.SelectedIndex >= 0)
                {
                    lstSources.Items.RemoveAt(lstSources.SelectedIndex);
                }
            };

            btnClearSources = new Button
            {
                Text = "🗑️ 清空列表",
                Location = new Point(668, 118),
                Size = new Size(110, 36),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnClearSources.Click += (s, e) => lstSources.Items.Clear();

            grpSource.Controls.AddRange(new Control[] { lstSources, btnAddSource, btnRemoveSource, btnClearSources });

            // 2. 目标文件夹区域
            var grpDest = new GroupBox
            {
                Text = "2. 整理后保存的目标文件夹 (单一归档目录，自动防爆仓检测)",
                Location = new Point(16, 195),
                Size = new Size(790, 110),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            txtDest = new TextBox
            {
                Location = new Point(16, 26),
                Size = new Size(640, 27),
                ReadOnly = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                PlaceholderText = "请点击右侧按钮选择保存整理结果的文件夹..."
            };

            btnSelectDest = new Button
            {
                Text = "📁 选择目标...",
                Location = new Point(668, 22),
                Size = new Size(110, 34),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                BackColor = Color.FromArgb(235, 243, 250)
            };
            btnSelectDest.Click += BtnSelectDest_Click;

            lblDestFreeSpace = new Label
            {
                Text = "目标盘剩余空间：尚未选择目标文件夹",
                Location = new Point(16, 58),
                Size = new Size(760, 20),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(73, 80, 87)
            };

            lblDestTip = new Label
            {
                Text = "💡 提示：目标盘剩余空间需大于所选照片总大小。程序在开始复制前会自动精准核验，防止磁盘爆仓。",
                Location = new Point(16, 80),
                Size = new Size(760, 20),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                ForeColor = Color.FromArgb(108, 117, 125)
            };

            grpDest.Controls.AddRange(new Control[] { txtDest, btnSelectDest, lblDestFreeSpace, lblDestTip });

            // 3. 执行与进度区域
            var grpAction = new GroupBox
            {
                Text = "3. 处理状态与实时日志",
                Location = new Point(16, 315),
                Size = new Size(790, 390),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };

            btnStart = new Button
            {
                Text = "🚀 开始智能去重与归档",
                Location = new Point(16, 24),
                Size = new Size(762, 42),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.FromArgb(13, 110, 253),
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat
            };
            btnStart.Click += BtnStart_Click;

            progressBar = new ProgressBar
            {
                Location = new Point(16, 74),
                Size = new Size(762, 18),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Minimum = 0,
                Maximum = 100,
                Value = 0
            };

            lblStatus = new Label
            {
                Text = "状态：准备就绪。请添加源文件夹并设置目标保存文件夹。",
                Location = new Point(16, 98),
                Size = new Size(762, 22),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                ForeColor = Color.FromArgb(108, 117, 125)
            };

            txtLog = new TextBox
            {
                Location = new Point(16, 124),
                Size = new Size(762, 250),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(245, 245, 245),
                Font = new Font("Consolas", 9F)
            };

            grpAction.Controls.AddRange(new Control[] { btnStart, progressBar, lblStatus, txtLog });

            Controls.AddRange(new Control[] { grpSource, grpDest, grpAction });
        }

        private void LstSources_DragEnter(object? sender, DragEventArgs e)
        {
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy;
            }
        }

        private void LstSources_DragDrop(object? sender, DragEventArgs e)
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files)
            {
                foreach (var path in files)
                {
                    if (Directory.Exists(path) && !lstSources.Items.Contains(path))
                    {
                        lstSources.Items.Add(path);
                    }
                }
            }
        }

        private void BtnAddSource_Click(object? sender, EventArgs e)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "选择照片源文件夹 (可多次添加不同目录)"
            };
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                string path = dialog.SelectedPath;
                if (!lstSources.Items.Contains(path))
                {
                    lstSources.Items.Add(path);
                }
            }
        }

        private void BtnSelectDest_Click(object? sender, EventArgs e)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "选择整理后保存的目标文件夹"
            };
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                txtDest.Text = dialog.SelectedPath;
                UpdateDestDriveDisplay(dialog.SelectedPath);
            }
        }

        private void UpdateDestDriveDisplay(string path)
        {
            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(path)) ?? string.Empty;
                if (!string.IsNullOrEmpty(root))
                {
                    var drive = new DriveInfo(root);
                    double freeGb = drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                    double totalGb = drive.TotalSize / (1024.0 * 1024.0 * 1024.0);
                    lblDestFreeSpace.Text = $"目标盘 [{drive.Name}] 当前可用剩余: {freeGb:F2} GB / 总容量: {totalGb:F2} GB";
                    lblDestFreeSpace.ForeColor = freeGb < 5.0 ? Color.Crimson : Color.FromArgb(40, 167, 69);
                    return;
                }
            }
            catch { }
            lblDestFreeSpace.Text = "目标盘剩余空间：已选择有效文件夹";
            lblDestFreeSpace.ForeColor = Color.FromArgb(73, 80, 87);
        }

        private async void BtnStart_Click(object? sender, EventArgs e)
        {
            if (lstSources.Items.Count == 0)
            {
                MessageBox.Show("请至少添加一个源照片文件夹！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string destDir = txtDest.Text.Trim();
            if (string.IsNullOrEmpty(destDir))
            {
                MessageBox.Show("请选择目标保存文件夹！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var sourceDirs = lstSources.Items.Cast<string>().ToList();

            SetUIEnabled(false);
            txtLog.Clear();
            progressBar.Value = 0;
            lblStatus.Text = "状态：正在扫描所选文件夹及深度计算照片总容量...";

            try
            {
                await Task.Run(() => ProcessDeduplication(sourceDirs, destDir));
            }
            catch (Exception ex)
            {
                AppendLog($"❌ 运行异常: {ex.Message}");
                MessageBox.Show($"处理过程中发生错误: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetUIEnabled(true);
            }
        }

        private void SetUIEnabled(bool enabled)
        {
            btnAddSource.Enabled = enabled;
            btnRemoveSource.Enabled = enabled;
            btnClearSources.Enabled = enabled;
            btnSelectDest.Enabled = enabled;
            btnStart.Enabled = enabled;
            lstSources.Enabled = enabled;
        }

        private void AppendLog(string message)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => AppendLog(message)));
                return;
            }
            txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\r\n");
        }

        private void UpdateStatus(string status, int progressPercent)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => UpdateStatus(status, progressPercent)));
                return;
            }
            lblStatus.Text = status;
            progressBar.Value = Math.Clamp(progressPercent, 0, 100);
        }

        private void ProcessDeduplication(List<string> sourceDirs, string destDir)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            AppendLog("===== 开始照片去重与整理任务 =====");
            AppendLog($"源文件夹数: {sourceDirs.Count} 个");
            AppendLog($"目标文件夹: {destDir}");

            // 1. 扫描所有文件
            var allFiles = new List<PhotoItem>();
            var scannedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long totalSourceBytes = 0;

            foreach (var sDir in sourceDirs)
            {
                if (!Directory.Exists(sDir)) continue;
                AppendLog($"正在扫描: {sDir} ...");

                try
                {
                    var files = Directory.EnumerateFiles(sDir, "*.*", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        if (scannedPaths.Contains(file)) continue;
                        scannedPaths.Add(file);

                        string ext = Path.GetExtension(file);
                        if (!ValidExts.Contains(ext)) continue;

                        var fi = new FileInfo(file);
                        totalSourceBytes += fi.Length;
                        DateTime shootDate = ExtractShootDate(file, fi.LastWriteTime);

                        allFiles.Add(new PhotoItem
                        {
                            FullPath = file,
                            FileName = fi.Name,
                            Size = fi.Length,
                            Date = shootDate
                        });
                    }
                }
                catch (Exception ex)
                {
                    AppendLog($"⚠️ 扫描目录异常 ({sDir}): {ex.Message}");
                }
            }

            int totalFound = allFiles.Count;
            double totalGb = totalSourceBytes / (1024.0 * 1024.0 * 1024.0);
            AppendLog($"扫描完成！共获取到 {totalFound} 个媒体文件，总大小约为: {totalGb:F2} GB。");

            if (totalFound == 0)
            {
                UpdateStatus("状态：未找到任何支持的照片文件。", 0);
                return;
            }

            // 2. 核心防爆仓检测 (零耗时内核检测)
            string root = Path.GetPathRoot(Path.GetFullPath(destDir)) ?? string.Empty;
            if (!string.IsNullOrEmpty(root))
            {
                try
                {
                    var drive = new DriveInfo(root);
                    long freeBytes = drive.AvailableFreeSpace;
                    double freeGb = freeBytes / (1024.0 * 1024.0 * 1024.0);

                    AppendLog($"[磁盘检测] 目标盘 ({drive.Name}) 可用剩余: {freeGb:F2} GB | 待处理照片总大小: {totalGb:F2} GB");

                    // 如果目标剩余磁盘空间小于源照片总大小
                    if (freeBytes < totalSourceBytes)
                    {
                        bool userContinue = false;
                        Invoke(new Action(() =>
                        {
                            var dr = MessageBox.Show(
                                $"【⚠️ 目标磁盘空间不足警报 (防爆仓)】\n\n" +
                                $"• 待整理照片总大小: {totalGb:F2} GB\n" +
                                $"• 目标磁盘 ({drive.Name}) 剩余空间: {freeGb:F2} GB\n\n" +
                                $"注意：目标磁盘剩余空间小于照片总容量，继续复制将极可能导致【磁盘爆满/系统卡顿】！\n\n" +
                                $"建议操作：点击【否】中止操作，清理磁盘或更换为容量更大的硬盘。\n\n" +
                                $"是否仍然强行继续？",
                                "磁盘空间不足预警",
                                MessageBoxButtons.YesNo,
                                MessageBoxIcon.Warning,
                                MessageBoxDefaultButton.Button2);
                            userContinue = (dr == DialogResult.Yes);
                        }));

                        if (!userContinue)
                        {
                            AppendLog("🛑 用户已取消整理：已成功防止磁盘爆仓。");
                            UpdateStatus("状态：由于目标磁盘剩余空间不足，任务已取消。", 0);
                            return;
                        }
                        AppendLog("⚠️ 用户选择忽略警告强行继续执行。");
                    }
                    else
                    {
                        AppendLog("✅ 磁盘空间充裕，安全检测通过！");
                    }
                }
                catch (Exception ex)
                {
                    AppendLog($"⚠️ 磁盘空间读取失败: {ex.Message}");
                }
            }

            // 3. 识别完全重复（第一类：同名同大小）
            var nameSizeMap = new Dictionary<string, List<PhotoItem>>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in allFiles)
            {
                string key = $"{f.FileName}_{f.Size}";
                if (!nameSizeMap.TryGetValue(key, out var list))
                {
                    list = new List<PhotoItem>();
                    nameSizeMap[key] = list;
                }
                list.Add(f);
            }

            var activePool = new List<PhotoItem>();
            int skippedCount = 0;
            long savedBytes = 0;

            foreach (var kvp in nameSizeMap)
            {
                activePool.Add(kvp.Value[0]); // 代表文件
                if (kvp.Value.Count > 1)
                {
                    for (int i = 1; i < kvp.Value.Count; i++)
                    {
                        skippedCount++;
                        savedBytes += kvp.Value[i].Size;
                    }
                }
            }

            // 4. 构建第二类与第三类筛选索引
            var nameMap = new Dictionary<string, List<PhotoItem>>(StringComparer.OrdinalIgnoreCase);
            var sizeMap = new Dictionary<long, List<PhotoItem>>();

            foreach (var f in activePool)
            {
                if (!nameMap.TryGetValue(f.FileName, out var nList))
                {
                    nList = new List<PhotoItem>();
                    nameMap[f.FileName] = nList;
                }
                nList.Add(f);

                if (!sizeMap.TryGetValue(f.Size, out var sList))
                {
                    sList = new List<PhotoItem>();
                    sizeMap[f.Size] = sList;
                }
                sList.Add(f);
            }

            // 5. 建立分类输出路径
            string dirArchived = Path.Combine(destDir, "整理归档");
            string dirDiffSizeSameName = Path.Combine(destDir, "人工筛选_同名不同大小");
            string dirDiffNameSameSize = Path.Combine(destDir, "人工筛选_同大小不同名");

            int countArchived = 0;
            int countSameNameDiffSize = 0;
            int countSameSizeDiffName = 0;

            AppendLog("正在进行文件复制与分类归档...");

            for (int i = 0; i < activePool.Count; i++)
            {
                var f = activePool[i];
                bool isSameNameDiffSize = nameMap[f.FileName].Count > 1;
                bool isSameSizeDiffName = sizeMap[f.Size].Count > 1;

                if (isSameNameDiffSize)
                {
                    // 第二类：同名不同大小
                    string subDir = Path.Combine(dirDiffSizeSameName, SanitizeDirName(f.FileName));
                    Directory.CreateDirectory(subDir);
                    string targetName = $"[{f.Size}字节]_{f.FileName}";
                    string targetPath = Path.Combine(subDir, targetName);
                    CopyFileSafe(f.FullPath, targetPath);
                    countSameNameDiffSize++;
                }
                else if (isSameSizeDiffName)
                {
                    // 第三类：同大小不同名
                    string subDir = Path.Combine(dirDiffNameSameSize, $"大小_{f.Size}字节");
                    Directory.CreateDirectory(subDir);
                    string targetPath = Path.Combine(subDir, f.FileName);
                    CopyFileSafe(f.FullPath, targetPath);
                    countSameSizeDiffName++;
                }
                else
                {
                    // 第一类母本及无冲突独立文件：按“年/月”建立文件夹
                    string yearFolder = $"{f.Date.Year}年";
                    string monthFolder = $"{f.Date.Month}月";
                    string targetDir = Path.Combine(dirArchived, yearFolder, monthFolder);
                    Directory.CreateDirectory(targetDir);

                    string targetPath = GetUniqueTargetPath(targetDir, f.FileName);
                    CopyFileSafe(f.FullPath, targetPath);
                    countArchived++;
                }

                int percent = (int)((i + 1) * 100.0 / activePool.Count);
                if (i % 20 == 0 || i == activePool.Count - 1)
                {
                    UpdateStatus($"状态：正在复制与归档 ({i + 1}/{activePool.Count})...", percent);
                }
            }

            watch.Stop();

            // 6. 写入统计报告
            string reportPath = Path.Combine(destDir, "去重与归档统计报告.txt");
            var sb = new StringBuilder();
            sb.AppendLine("================ 照片去重与归档统计报告 ================");
            sb.AppendLine($"生成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"处理耗时: {watch.Elapsed.TotalSeconds:F2} 秒");
            sb.AppendLine();
            sb.AppendLine($"1. 扫描总文件数:             {totalFound} (总体积约 {totalGb:F2} GB)");
            sb.AppendLine($"2. 成功按年月归档文件数:     {countArchived} (存放于: 整理归档/)");
            sb.AppendLine($"3. 完全重复已去重文件数:     {skippedCount} (节省空间约 {savedBytes / (1024.0 * 1024.0):F2} MB)");
            sb.AppendLine($"4. 同名不同大小(待核对)数:   {countSameNameDiffSize} (存放于: 人工筛选_同名不同大小/)");
            sb.AppendLine($"5. 同大小不同名(待核对)数:   {countSameSizeDiffName} (存放于: 人工筛选_同大小不同名/)");
            sb.AppendLine("========================================================");
            File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);

            AppendLog("===== 整理完成！ =====");
            AppendLog($"按年月归档保存: {countArchived} 张");
            AppendLog($"完全重复已去重: {skippedCount} 张 (节省空间: {savedBytes / (1024.0 * 1024.0):F2} MB)");
            AppendLog($"同名不同大小:   {countSameNameDiffSize} 张");
            AppendLog($"同大小不同名:   {countSameSizeDiffName} 张");
            AppendLog($"统计报告已生成: {reportPath}");

            UpdateStatus("状态：全部处理完成！", 100);

            Invoke(new Action(() =>
            {
                MessageBox.Show(
                    $"照片整理已全部完成！\n\n" +
                    $"• 扫描总大小: {totalGb:F2} GB\n" +
                    $"• 归档保存: {countArchived} 张\n" +
                    $"• 完全重复去重: {skippedCount} 张 (省空间 {savedBytes / (1024.0 * 1024.0):F2} MB)\n" +
                    $"• 人工筛选(同名不同大小): {countSameNameDiffSize} 张\n" +
                    $"• 人工筛选(同大小不同名): {countSameSizeDiffName} 张\n\n" +
                    $"报告已保存至目标文件夹。",
                    "处理完成",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }));
        }

        private static DateTime ExtractShootDate(string filePath, DateTime fallback)
        {
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                byte[] buffer = new byte[64 * 1024];
                int bytesRead = fs.Read(buffer, 0, buffer.Length);
                if (bytesRead > 0)
                {
                    string headerStr = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                    var match = ExifDateRegex.Match(headerStr);
                    if (match.Success)
                    {
                        int y = int.Parse(match.Groups[1].Value);
                        int m = int.Parse(match.Groups[2].Value);
                        int d = int.Parse(match.Groups[3].Value);
                        int h = int.Parse(match.Groups[4].Value);
                        int min = int.Parse(match.Groups[5].Value);
                        int s = int.Parse(match.Groups[6].Value);
                        if (y >= 1970 && y <= 2100 && m >= 1 && m <= 12 && d >= 1 && d <= 31)
                        {
                            return new DateTime(y, m, d, h, min, s);
                        }
                    }
                }
            }
            catch { }
            return fallback;
        }

        private static void CopyFileSafe(string src, string dst)
        {
            try
            {
                File.Copy(src, dst, true);
            }
            catch { }
        }

        private static string GetUniqueTargetPath(string dir, string fileName)
        {
            string ext = Path.GetExtension(fileName);
            string baseName = Path.GetFileNameWithoutExtension(fileName);
            string target = Path.Combine(dir, fileName);
            int counter = 1;
            while (File.Exists(target))
            {
                target = Path.Combine(dir, $"{baseName}_{counter}{ext}");
                counter++;
            }
            return target;
        }

        private static string SanitizeDirName(string name)
        {
            string baseName = Path.GetFileNameWithoutExtension(name);
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                baseName = baseName.Replace(c, '_');
            }
            return baseName.Trim();
        }
    }
}
