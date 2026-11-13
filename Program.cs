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

    public class FileItem
    {
        public string FullPath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long Size { get; set; }
        public DateTime Date { get; set; }
    }

    public class ConflictGroup
    {
        public string ConflictType { get; set; } = string.Empty; // "同名不同大小" 或 "同大小不同名"
        public string Title { get; set; } = string.Empty;
        public List<string> FilePaths { get; set; } = new();
    }

    public class MainForm : Form
    {
        private ListBox lstSources = null!;
        private Button btnAddSource = null!;
        private Button btnRemoveSource = null!;
        private Button btnClearSources = null!;

        private RadioButton rbPresetPhotoVideo = null!;
        private RadioButton rbAllFiles = null!;
        private RadioButton rbCustom = null!;
        private TextBox txtCustomExt = null!;

        private TextBox txtDest = null!;
        private Button btnSelectDest = null!;
        private Label lblDestFreeSpace = null!;
        private Label lblDestTip = null!;

        private Button btnStart = null!;
        private Button btnOpenReview = null!;
        private ProgressBar progressBar = null!;
        private Label lblStatus = null!;
        private TextBox txtLog = null!;

        private List<ConflictGroup> currentConflicts = new();
        private string currentDestDir = string.Empty;

        private static readonly HashSet<string> PhotoVideoExts = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".heic", ".bmp", ".webp",
            ".tif", ".tiff", ".raw", ".cr2", ".nef", ".arw",
            ".mp4", ".mov", ".avi", ".mkv", ".flv", ".wmv"
        };

        private static readonly HashSet<string> ExifSupportedExts = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".heic", ".tif", ".tiff", ".raw", ".cr2", ".nef", ".arw"
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
            Text = "Windows 全文件与照片深度去重归档工具 (专业双核版)";
            Size = new Size(880, 850);
            MinimumSize = new Size(820, 760);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            BackColor = Color.FromArgb(248, 249, 250);

            // 1. 源文件夹区域
            var grpSource = new GroupBox
            {
                Text = "1. 源文件夹 (支持添加多个不同目录，支持直接拖拽文件夹进下方列表)",
                Location = new Point(16, 12),
                Size = new Size(830, 160),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            lstSources = new ListBox
            {
                Location = new Point(16, 26),
                Size = new Size(680, 120),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                AllowDrop = true
            };
            lstSources.DragEnter += LstSources_DragEnter;
            lstSources.DragDrop += LstSources_DragDrop;

            btnAddSource = new Button
            {
                Text = "➕ 添加文件夹",
                Location = new Point(708, 26),
                Size = new Size(110, 32),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                BackColor = Color.FromArgb(235, 243, 250)
            };
            btnAddSource.Click += BtnAddSource_Click;

            btnRemoveSource = new Button
            {
                Text = "➖ 移除选中",
                Location = new Point(708, 66),
                Size = new Size(110, 32),
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
                Location = new Point(708, 106),
                Size = new Size(110, 32),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnClearSources.Click += (s, e) => lstSources.Items.Clear();

            grpSource.Controls.AddRange(new Control[] { lstSources, btnAddSource, btnRemoveSource, btnClearSources });

            // 2. 文件格式过滤区域
            var grpFilter = new GroupBox
            {
                Text = "2. 处理文件类型范围",
                Location = new Point(16, 180),
                Size = new Size(830, 70),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            rbPresetPhotoVideo = new RadioButton
            {
                Text = "📷 仅照片与视频 (默认)",
                Location = new Point(16, 28),
                Size = new Size(160, 24),
                Checked = true
            };

            rbAllFiles = new RadioButton
            {
                Text = "🌐 全部文件 (*.*)",
                Location = new Point(186, 28),
                Size = new Size(130, 24)
            };

            rbCustom = new RadioButton
            {
                Text = "⚙️ 指定后缀:",
                Location = new Point(326, 28),
                Size = new Size(95, 24)
            };

            txtCustomExt = new TextBox
            {
                Location = new Point(425, 27),
                Size = new Size(390, 26),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Enabled = false,
                Text = ".pdf, .docx, .xlsx, .zip, .rar"
            };

            rbCustom.CheckedChanged += (s, e) =>
            {
                txtCustomExt.Enabled = rbCustom.Checked;
                if (rbCustom.Checked)
                {
                    txtCustomExt.Focus();
                }
            };

            grpFilter.Controls.AddRange(new Control[] { rbPresetPhotoVideo, rbAllFiles, rbCustom, txtCustomExt });

            // 3. 目标文件夹区域
            var grpDest = new GroupBox
            {
                Text = "3. 整理后保存的目标文件夹 (单一归档目录，防爆仓检测)",
                Location = new Point(16, 258),
                Size = new Size(830, 110),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            txtDest = new TextBox
            {
                Location = new Point(16, 26),
                Size = new Size(680, 27),
                ReadOnly = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                PlaceholderText = "请点击右侧按钮选择保存整理结果的文件夹..."
            };

            btnSelectDest = new Button
            {
                Text = "📁 选择目标...",
                Location = new Point(708, 22),
                Size = new Size(110, 34),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                BackColor = Color.FromArgb(235, 243, 250)
            };
            btnSelectDest.Click += BtnSelectDest_Click;

            lblDestFreeSpace = new Label
            {
                Text = "目标盘剩余空间：尚未选择目标文件夹",
                Location = new Point(16, 58),
                Size = new Size(800, 20),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(73, 80, 87)
            };

            lblDestTip = new Label
            {
                Text = "💡 提示：目标盘剩余空间需大于所选文件总大小。程序在开始复制前会自动精准核验，防止磁盘爆仓。",
                Location = new Point(16, 80),
                Size = new Size(800, 20),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                ForeColor = Color.FromArgb(108, 117, 125)
            };

            grpDest.Controls.AddRange(new Control[] { txtDest, btnSelectDest, lblDestFreeSpace, lblDestTip });

            // 4. 执行与进度区域
            var grpAction = new GroupBox
            {
                Text = "4. 处理状态与实时日志",
                Location = new Point(16, 376),
                Size = new Size(830, 415),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };

            btnStart = new Button
            {
                Text = "🚀 开始智能去重与归档",
                Location = new Point(16, 24),
                Size = new Size(540, 42),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.FromArgb(13, 110, 253),
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat
            };
            btnStart.Click += BtnStart_Click;

            btnOpenReview = new Button
            {
                Text = "🔍 冲突并排对比审查",
                Location = new Point(566, 24),
                Size = new Size(250, 42),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                BackColor = Color.FromArgb(25, 135, 84),
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Enabled = false
            };
            btnOpenReview.Click += (s, e) => OpenConflictReview();

            progressBar = new ProgressBar
            {
                Location = new Point(16, 74),
                Size = new Size(800, 18),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Minimum = 0,
                Maximum = 100,
                Value = 0
            };

            lblStatus = new Label
            {
                Text = "状态：准备就绪。请添加源文件夹、选择格式并设置目标保存文件夹。",
                Location = new Point(16, 98),
                Size = new Size(800, 22),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                ForeColor = Color.FromArgb(108, 117, 125)
            };

            txtLog = new TextBox
            {
                Location = new Point(16, 124),
                Size = new Size(800, 275),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(245, 245, 245),
                Font = new Font("Consolas", 9F)
            };

            grpAction.Controls.AddRange(new Control[] { btnStart, btnOpenReview, progressBar, lblStatus, txtLog });

            Controls.AddRange(new Control[] { grpSource, grpFilter, grpDest, grpAction });
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
                Description = "选择源文件夹 (可多次添加不同目录)"
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

        private HashSet<string>? GetAllowedExtensions(out bool allowAll)
        {
            allowAll = false;
            if (rbAllFiles.Checked)
            {
                allowAll = true;
                return null;
            }
            if (rbPresetPhotoVideo.Checked)
            {
                return PhotoVideoExts;
            }

            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string raw = txtCustomExt.Text.Trim();
            var parts = raw.Split(new[] { ',', ';', ' ', '，', '、', '|' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts)
            {
                string ext = p.Trim();
                if (!ext.StartsWith('.'))
                {
                    ext = "." + ext;
                }
                set.Add(ext);
            }
            if (set.Count == 0)
            {
                allowAll = true;
                return null;
            }
            return set;
        }

        private async void BtnStart_Click(object? sender, EventArgs e)
        {
            if (lstSources.Items.Count == 0)
            {
                MessageBox.Show("请至少添加一个源文件夹！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string destDir = txtDest.Text.Trim();
            if (string.IsNullOrEmpty(destDir))
            {
                MessageBox.Show("请选择目标保存文件夹！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            currentDestDir = destDir;
            currentConflicts.Clear();
            btnOpenReview.Enabled = false;

            var allowedExts = GetAllowedExtensions(out bool allowAll);
            var sourceDirs = lstSources.Items.Cast<string>().ToList();

            SetUIEnabled(false);
            txtLog.Clear();
            progressBar.Value = 0;
            lblStatus.Text = "状态：正在扫描所选文件夹及深度计算文件总容量...";

            try
            {
                await Task.Run(() => ProcessDeduplication(sourceDirs, destDir, allowedExts, allowAll));
            }
            catch (Exception ex)
            {
                AppendLog($"❌ 运行异常: {ex.Message}");
                MessageBox.Show($"处理过程中发生错误: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetUIEnabled(true);
                if (currentConflicts.Count > 0)
                {
                    btnOpenReview.Enabled = true;
                }
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
            rbPresetPhotoVideo.Enabled = enabled;
            rbAllFiles.Enabled = enabled;
            rbCustom.Enabled = enabled;
            txtCustomExt.Enabled = enabled && rbCustom.Checked;
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

        private void ProcessDeduplication(List<string> sourceDirs, string destDir, HashSet<string>? allowedExts, bool allowAll)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            AppendLog("===== 开始全文件去重与整理任务 =====");
            AppendLog($"源文件夹数: {sourceDirs.Count} 个");
            AppendLog($"目标文件夹: {destDir}");

            // 1. 扫描所有文件
            var allFiles = new List<FileItem>();
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
                        if (!allowAll && (allowedExts == null || !allowedExts.Contains(ext)))
                        {
                            continue;
                        }

                        var fi = new FileInfo(file);
                        totalSourceBytes += fi.Length;
                        DateTime fileDate = ExtractFileDate(file, fi);

                        allFiles.Add(new FileItem
                        {
                            FullPath = file,
                            FileName = fi.Name,
                            Size = fi.Length,
                            Date = fileDate
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
            AppendLog($"扫描完成！共获取到 {totalFound} 个符合条件的文件，总大小约为: {totalGb:F2} GB。");

            if (totalFound == 0)
            {
                UpdateStatus("状态：未找到任何符合过滤条件的文件。", 0);
                return;
            }

            // 2. 防爆仓检测
            string root = Path.GetPathRoot(Path.GetFullPath(destDir)) ?? string.Empty;
            if (!string.IsNullOrEmpty(root))
            {
                try
                {
                    var drive = new DriveInfo(root);
                    long freeBytes = drive.AvailableFreeSpace;
                    double freeGb = freeBytes / (1024.0 * 1024.0 * 1024.0);

                    AppendLog($"[磁盘检测] 目标盘 ({drive.Name}) 可用剩余: {freeGb:F2} GB | 待处理文件总大小: {totalGb:F2} GB");

                    if (freeBytes < totalSourceBytes)
                    {
                        bool userContinue = false;
                        Invoke(new Action(() =>
                        {
                            var dr = MessageBox.Show(
                                $"【⚠️ 目标磁盘空间不足警报 (防爆仓)】\n\n" +
                                $"• 待整理文件总大小: {totalGb:F2} GB\n" +
                                $"• 目标磁盘 ({drive.Name}) 剩余空间: {freeGb:F2} GB\n\n" +
                                $"注意：目标磁盘剩余空间小于文件总容量，继续复制将极可能导致【磁盘爆满/系统卡顿】！\n\n" +
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
            var nameSizeMap = new Dictionary<string, List<FileItem>>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in allFiles)
            {
                string key = $"{f.FileName}_{f.Size}";
                if (!nameSizeMap.TryGetValue(key, out var list))
                {
                    list = new List<FileItem>();
                    nameSizeMap[key] = list;
                }
                list.Add(f);
            }

            var activePool = new List<FileItem>();
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
            var nameMap = new Dictionary<string, List<FileItem>>(StringComparer.OrdinalIgnoreCase);
            var sizeMap = new Dictionary<long, List<FileItem>>();

            foreach (var f in activePool)
            {
                if (!nameMap.TryGetValue(f.FileName, out var nList))
                {
                    nList = new List<FileItem>();
                    nameMap[f.FileName] = nList;
                }
                nList.Add(f);

                if (!sizeMap.TryGetValue(f.Size, out var sList))
                {
                    sList = new List<FileItem>();
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

            var conflictGroups = new List<ConflictGroup>();
            var sameNameRecorded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sameSizeRecorded = new HashSet<long>();

            AppendLog("正在进行文件复制与年月分类归档...");

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

                    if (!sameNameRecorded.Contains(f.FileName))
                    {
                        sameNameRecorded.Add(f.FileName);
                        var g = new ConflictGroup
                        {
                            ConflictType = "同名不同大小",
                            Title = $"同名文件: {f.FileName}"
                        };
                        foreach (var item in nameMap[f.FileName])
                        {
                            string itemTarget = Path.Combine(subDir, $"[{item.Size}字节]_{item.FileName}");
                            g.FilePaths.Add(itemTarget);
                        }
                        conflictGroups.Add(g);
                    }
                }
                else if (isSameSizeDiffName)
                {
                    // 第三类：同大小不同名
                    string subDir = Path.Combine(dirDiffNameSameSize, $"大小_{f.Size}字节");
                    Directory.CreateDirectory(subDir);
                    string targetPath = Path.Combine(subDir, f.FileName);
                    CopyFileSafe(f.FullPath, targetPath);
                    countSameSizeDiffName++;

                    if (!sameSizeRecorded.Contains(f.Size))
                    {
                        sameSizeRecorded.Add(f.Size);
                        var g = new ConflictGroup
                        {
                            ConflictType = "同大小不同名",
                            Title = $"相同字节大小: {f.Size} 字节"
                        };
                        foreach (var item in sizeMap[f.Size])
                        {
                            string itemTarget = Path.Combine(subDir, item.FileName);
                            g.FilePaths.Add(itemTarget);
                        }
                        conflictGroups.Add(g);
                    }
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
            currentConflicts = conflictGroups;

            // 6. 写入统计报告
            string reportPath = Path.Combine(destDir, "去重与归档统计报告.txt");
            var sb = new StringBuilder();
            sb.AppendLine("================ 全文件去重与归档统计报告 ================");
            sb.AppendLine($"生成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"处理耗时: {watch.Elapsed.TotalSeconds:F2} 秒");
            sb.AppendLine();
            sb.AppendLine($"1. 扫描总文件数:             {totalFound} (总体积约 {totalGb:F2} GB)");
            sb.AppendLine($"2. 成功按年月归档文件数:     {countArchived} (存放于: 整理归档/)");
            sb.AppendLine($"3. 完全重复已去重文件数:     {skippedCount} (节省空间约 {savedBytes / (1024.0 * 1024.0):F2} MB)");
            sb.AppendLine($"4. 同名不同大小(待核对)数:   {countSameNameDiffSize} (共 {sameNameRecorded.Count} 组冲突)");
            sb.AppendLine($"5. 同大小不同名(待核对)数:   {countSameSizeDiffName} (共 {sameSizeRecorded.Count} 组冲突)");
            sb.AppendLine("==========================================================");
            File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);

            AppendLog("===== 整理完成！ =====");
            AppendLog($"按年月归档保存: {countArchived} 个");
            AppendLog($"完全重复已去重: {skippedCount} 个 (节省空间: {savedBytes / (1024.0 * 1024.0):F2} MB)");
            AppendLog($"待对比冲突总组数: {conflictGroups.Count} 组");
            AppendLog($"统计报告已生成: {reportPath}");

            UpdateStatus("状态：全部处理完成！", 100);

            Invoke(new Action(() =>
            {
                if (conflictGroups.Count > 0)
                {
                    var askReview = MessageBox.Show(
                        $"照片与文件整理已全部完成！\n\n" +
                        $"• 归档保存: {countArchived} 个\n" +
                        $"• 完全重复去重: {skippedCount} 个 (节省空间 {savedBytes / (1024.0 * 1024.0):F2} MB)\n" +
                        $"• 发现冲突组: {conflictGroups.Count} 组需人工比对\n\n" +
                        $"是否现在立即打开【并排对比审查工具】进行极速筛选？",
                        "整理完成 - 建议进行并排对比",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);

                    if (askReview == DialogResult.Yes)
                    {
                        OpenConflictReview();
                    }
                }
                else
                {
                    MessageBox.Show(
                        $"照片与文件整理已全部完成！\n\n" +
                        $"• 扫描总大小: {totalGb:F2} GB\n" +
                        $"• 归档保存: {countArchived} 个\n" +
                        $"• 完全重复去重: {skippedCount} 个 (省空间 {savedBytes / (1024.0 * 1024.0):F2} MB)\n" +
                        $"• 无任何冲突项！\n\n" +
                        $"详细报告已保存至目标文件夹。",
                        "处理完成",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            }));
        }

        private void OpenConflictReview()
        {
            if (currentConflicts.Count == 0)
            {
                MessageBox.Show("当前没有需要比对的冲突文件组！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using var reviewForm = new ConflictReviewForm(currentConflicts, currentDestDir);
            reviewForm.ShowDialog(this);
        }

        private static DateTime ExtractFileDate(string filePath, FileInfo fi)
        {
            string ext = fi.Extension;
            if (ExifSupportedExts.Contains(ext))
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
            }

            DateTime date = fi.LastWriteTime;
            if (date.Year < 1970 || date.Year > 2100)
            {
                date = fi.CreationTime;
            }
            if (date.Year < 1970 || date.Year > 2100)
            {
                date = DateTime.Now;
            }
            return date;
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

    /// <summary>
    /// 专业级冲突文件并排对比与人工决断窗口
    /// </summary>
    public class ConflictReviewForm : Form
    {
        private readonly List<ConflictGroup> groups;
        private readonly string destDir;
        private int currentIndex = 0;

        private Label lblHeader = null!;
        private Button btnPrev = null!;
        private Button btnNext = null!;
        private Label lblGroupProgress = null!;

        // 左侧文件 A 控件
        private PictureBox picA = null!;
        private Label lblInfoA = null!;
        private Button btnKeepA = null!;

        // 右侧文件 B 控件
        private PictureBox picB = null!;
        private Label lblInfoB = null!;
        private Button btnKeepB = null!;

        // 底部全局控件
        private Button btnKeepBoth = null!;
        private Button btnOpenFolder = null!;

        private static readonly HashSet<string> ImageExts = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".webp", ".tif", ".tiff"
        };

        public ConflictReviewForm(List<ConflictGroup> groups, string destDir)
        {
            this.groups = groups.Where(g => g.FilePaths.Count(File.Exists) >= 2).ToList();
            this.destDir = destDir;
            InitializeComponent();
            LoadCurrentGroup();
        }

        private void InitializeComponent()
        {
            Text = "冲突文件并排比对与审查工具 (专业双核版)";
            Size = new Size(1020, 780);
            MinimumSize = new Size(920, 680);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            BackColor = Color.FromArgb(245, 247, 250);

            // 顶部导航栏
            var panelTop = new Panel
            {
                Dock = DockStyle.Top,
                Height = 65,
                BackColor = Color.White,
                Padding = new Padding(16, 10, 16, 10)
            };

            lblHeader = new Label
            {
                Text = "冲突详情加载中...",
                Location = new Point(16, 12),
                Size = new Size(620, 24),
                Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
                ForeColor = Color.FromArgb(33, 37, 41)
            };

            lblGroupProgress = new Label
            {
                Text = "进度: 1 / 1",
                Location = new Point(16, 38),
                Size = new Size(300, 20),
                ForeColor = Color.FromArgb(108, 117, 125)
            };

            btnPrev = new Button
            {
                Text = "◀ 上一组",
                Location = new Point(780, 16),
                Size = new Size(95, 34),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnPrev.Click += (s, e) => Navigate(-1);

            btnNext = new Button
            {
                Text = "下一组 ▶",
                Location = new Point(885, 16),
                Size = new Size(95, 34),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnNext.Click += (s, e) => Navigate(1);

            panelTop.Controls.AddRange(new Control[] { lblHeader, lblGroupProgress, btnPrev, btnNext });

            // 底部操作栏
            var panelBottom = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 60,
                BackColor = Color.White
            };

            btnKeepBoth = new Button
            {
                Text = "🤝 两者均保留 (跳过此组)",
                Location = new Point(340, 12),
                Size = new Size(180, 36),
                Anchor = AnchorStyles.None,
                BackColor = Color.FromArgb(240, 242, 245)
            };
            btnKeepBoth.Click += (s, e) => Navigate(1);

            btnOpenFolder = new Button
            {
                Text = "📂 在资源管理器中查看",
                Location = new Point(530, 12),
                Size = new Size(170, 36),
                Anchor = AnchorStyles.None
            };
            btnOpenFolder.Click += BtnOpenFolder_Click;

            panelBottom.Controls.AddRange(new Control[] { btnKeepBoth, btnOpenFolder });

            // 中间并排比对区
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 500,
                SplitterWidth = 8,
                Padding = new Padding(12, 10, 12, 10)
            };

            // 左侧 A 容器
            var panelA = new GroupBox
            {
                Text = "文件 A (母本 / 候选 1)",
                Dock = DockStyle.Fill,
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold)
            };
            picA = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(235, 238, 242)
            };
            lblInfoA = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 90,
                BackColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular),
                Padding = new Padding(8)
            };
            btnKeepA = new Button
            {
                Text = "✅ 保留此文件 (将右侧移入排除目录)",
                Dock = DockStyle.Bottom,
                Height = 42,
                BackColor = Color.FromArgb(13, 110, 253),
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat
            };
            btnKeepA.Click += (s, e) => KeepSingleFile(keepA: true);

            panelA.Controls.Add(picA);
            panelA.Controls.Add(lblInfoA);
            panelA.Controls.Add(btnKeepA);
            split.Panel1.Controls.Add(panelA);

            // 右侧 B 容器
            var panelB = new GroupBox
            {
                Text = "文件 B (冲突对比项 / 候选 2)",
                Dock = DockStyle.Fill,
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold)
            };
            picB = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(235, 238, 242)
            };
            lblInfoB = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 90,
                BackColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular),
                Padding = new Padding(8)
            };
            btnKeepB = new Button
            {
                Text = "✅ 保留此文件 (将左侧移入排除目录)",
                Dock = DockStyle.Bottom,
                Height = 42,
                BackColor = Color.FromArgb(13, 110, 253),
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat
            };
            btnKeepB.Click += (s, e) => KeepSingleFile(keepA: false);

            panelB.Controls.Add(picB);
            panelB.Controls.Add(lblInfoB);
            panelB.Controls.Add(btnKeepB);
            split.Panel2.Controls.Add(panelB);

            Controls.Add(split);
            Controls.Add(panelBottom);
            Controls.Add(panelTop);
        }

        private void Navigate(int delta)
        {
            if (groups.Count == 0) return;
            currentIndex += delta;
            if (currentIndex < 0) currentIndex = groups.Count - 1;
            if (currentIndex >= groups.Count) currentIndex = 0;
            LoadCurrentGroup();
        }

        private void LoadCurrentGroup()
        {
            if (groups.Count == 0)
            {
                lblHeader.Text = "🎉 所有冲突文件已全部处理完毕！";
                lblGroupProgress.Text = "无待审项";
                btnPrev.Enabled = false;
                btnNext.Enabled = false;
                btnKeepA.Enabled = false;
                btnKeepB.Enabled = false;
                btnKeepBoth.Enabled = false;
                ClearImage(picA);
                ClearImage(picB);
                lblInfoA.Text = "";
                lblInfoB.Text = "";
                return;
            }

            var g = groups[currentIndex];
            // 确保组内文件都还存在
            var validFiles = g.FilePaths.Where(File.Exists).ToList();
            if (validFiles.Count < 2)
            {
                groups.RemoveAt(currentIndex);
                if (currentIndex >= groups.Count) currentIndex = 0;
                LoadCurrentGroup();
                return;
            }

            lblHeader.Text = $"[{g.ConflictType}] {g.Title}";
            lblGroupProgress.Text = $"第 {currentIndex + 1} / {groups.Count} 组冲突 (可用键盘左右键快捷翻页)";

            string fileA = validFiles[0];
            string fileB = validFiles[1];

            LoadFileInfo(fileA, picA, lblInfoA);
            LoadFileInfo(fileB, picB, lblInfoB);
        }

        private void LoadFileInfo(string path, PictureBox pic, Label lbl)
        {
            ClearImage(pic);
            var fi = new FileInfo(path);
            string ext = fi.Extension;

            string resolutionStr = "非图像格式";
            if (ImageExts.Contains(ext))
            {
                try
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var origBmp = new Bitmap(stream);
                    resolutionStr = $"{origBmp.Width} x {origBmp.Height} 像素";
                    pic.Image = new Bitmap(origBmp); // 深拷贝释放文件占用
                }
                catch
                {
                    resolutionStr = "图像损坏或不可预览";
                }
            }

            double mb = fi.Length / (1024.0 * 1024.0);
            lbl.Text =
                $"文件名: {fi.Name}\n" +
                $"文件大小: {mb:F2} MB ({fi.Length:N0} 字节)\n" +
                $"尺寸规格: {resolutionStr}\n" +
                $"修改时间: {fi.LastWriteTime:yyyy-MM-dd HH:mm:ss}";
        }

        private void ClearImage(PictureBox pic)
        {
            if (pic.Image != null)
            {
                var old = pic.Image;
                pic.Image = null;
                old.Dispose();
            }
        }

        private void KeepSingleFile(bool keepA)
        {
            if (groups.Count == 0) return;
            var g = groups[currentIndex];
            var validFiles = g.FilePaths.Where(File.Exists).ToList();
            if (validFiles.Count < 2) return;

            string targetToExclude = keepA ? validFiles[1] : validFiles[0];
            string excludedFolder = Path.Combine(destDir, "人工筛选_已排除的弃用副本");
            Directory.CreateDirectory(excludedFolder);

            try
            {
                // 安全释放 PictureBox 句柄以防文件占用
                ClearImage(picA);
                ClearImage(picB);

                string fileName = Path.GetFileName(targetToExclude);
                string destFile = Path.Combine(excludedFolder, fileName);
                int count = 1;
                while (File.Exists(destFile))
                {
                    destFile = Path.Combine(excludedFolder, $"{Path.GetFileNameWithoutExtension(fileName)}_{count}{Path.GetExtension(fileName)}");
                    count++;
                }

                File.Move(targetToExclude, destFile);

                // 移除已处理的组
                groups.RemoveAt(currentIndex);
                if (currentIndex >= groups.Count) currentIndex = 0;
                LoadCurrentGroup();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"移动文件发生异常: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                LoadCurrentGroup();
            }
        }

        private void BtnOpenFolder_Click(object? sender, EventArgs e)
        {
            if (groups.Count == 0) return;
            var g = groups[currentIndex];
            var first = g.FilePaths.FirstOrDefault(File.Exists);
            if (first != null)
            {
                string dir = Path.GetDirectoryName(first) ?? destDir;
                try
                {
                    System.Diagnostics.Process.Start("explorer.exe", dir);
                }
                catch { }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            ClearImage(picA);
            ClearImage(picB);
            base.OnFormClosing(e);
        }
    }
}
