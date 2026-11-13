using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PhotoDeduplicatorGUI.Models;
using PhotoDeduplicatorGUI.Services;

namespace PhotoDeduplicatorGUI.UI
{
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

        public MainForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "Windows 全文件与照片深度去重归档工具 V2.0";
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
            lstSources.DragEnter += (s, e) =>
            {
                if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
                    e.Effect = DragDropEffects.Copy;
            };
            lstSources.DragDrop += (s, e) =>
            {
                if (e.Data?.GetData(DataFormats.FileDrop) is string[] files)
                {
                    foreach (var path in files)
                        if (Directory.Exists(path) && !lstSources.Items.Contains(path))
                            lstSources.Items.Add(path);
                }
            };

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
                    lstSources.Items.RemoveAt(lstSources.SelectedIndex);
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
                Size = new Size(165, 24),
                Checked = true
            };

            rbAllFiles = new RadioButton
            {
                Text = "🌐 全部文件 (*.*)",
                Location = new Point(190, 28),
                Size = new Size(135, 24)
            };

            rbCustom = new RadioButton
            {
                Text = "⚙️ 指定后缀:",
                Location = new Point(335, 28),
                Size = new Size(95, 24)
            };

            txtCustomExt = new TextBox
            {
                Location = new Point(435, 27),
                Size = new Size(380, 26),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Enabled = false,
                Text = ".pdf, .docx, .xlsx, .zip, .rar"
            };

            rbCustom.CheckedChanged += (s, e) =>
            {
                txtCustomExt.Enabled = rbCustom.Checked;
                if (rbCustom.Checked) txtCustomExt.Focus();
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
                Text = "💡 提示：已启用 SHA-256 哈希精确复核，改名重复文件将自动去重；冲突项可在审查窗口中一键归档。",
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
                Maximum = 100
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

        private void BtnAddSource_Click(object? sender, EventArgs e)
        {
            using var dialog = new FolderBrowserDialog { Description = "选择源文件夹 (可多次添加不同目录)" };
            if (dialog.ShowDialog() == DialogResult.OK && !lstSources.Items.Contains(dialog.SelectedPath))
            {
                lstSources.Items.Add(dialog.SelectedPath);
            }
        }

        private void BtnSelectDest_Click(object? sender, EventArgs e)
        {
            using var dialog = new FolderBrowserDialog { Description = "选择整理后保存的目标文件夹" };
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                txtDest.Text = dialog.SelectedPath;
                var space = DiskGuardService.GetDriveSpace(dialog.SelectedPath);
                if (space.IsValid)
                {
                    double freeGb = space.AvailableFreeBytes / (1024.0 * 1024.0 * 1024.0);
                    double totalGb = space.TotalSizeBytes / (1024.0 * 1024.0 * 1024.0);
                    lblDestFreeSpace.Text = $"目标盘 [{space.DriveName}] 当前可用剩余: {freeGb:F2} GB / 总容量: {totalGb:F2} GB";
                    lblDestFreeSpace.ForeColor = freeGb < 5.0 ? Color.Crimson : Color.FromArgb(40, 167, 69);
                }
            }
        }

        private ProcessOptions BuildOptions()
        {
            var opts = new ProcessOptions
            {
                SourceDirs = lstSources.Items.Cast<string>().ToList(),
                DestDir = txtDest.Text.Trim()
            };

            if (rbAllFiles.Checked)
            {
                opts.AllowAllFiles = true;
            }
            else if (rbPresetPhotoVideo.Checked)
            {
                opts.AllowedExtensions = MetadataService.PhotoVideoExts;
            }
            else
            {
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in txtCustomExt.Text.Split(new[] { ',', ';', ' ', '，', '、', '|' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string ext = p.Trim();
                    if (!ext.StartsWith('.')) ext = "." + ext;
                    set.Add(ext);
                }
                if (set.Count == 0) opts.AllowAllFiles = true;
                else opts.AllowedExtensions = set;
            }

            return opts;
        }

        private async void BtnStart_Click(object? sender, EventArgs e)
        {
            if (lstSources.Items.Count == 0)
            {
                MessageBox.Show("请至少添加一个源文件夹！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(txtDest.Text))
            {
                MessageBox.Show("请选择目标保存文件夹！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var options = BuildOptions();
            currentDestDir = options.DestDir;
            currentConflicts.Clear();
            btnOpenReview.Enabled = false;
            SetUIEnabled(false);
            txtLog.Clear();
            progressBar.Value = 0;

            var engine = new DeduplicationEngine();
            engine.OnLog += AppendLog;
            engine.OnProgress += UpdateStatus;
            engine.OnDiskSpaceWarning = (needBytes, freeBytes, driveName) =>
            {
                bool proceed = false;
                Invoke(new Action(() =>
                {
                    double needGb = needBytes / (1024.0 * 1024.0 * 1024.0);
                    double freeGb = freeBytes / (1024.0 * 1024.0 * 1024.0);
                    var dr = MessageBox.Show(
                        $"【⚠️ 目标磁盘空间不足警报 (防爆仓)】\n\n" +
                        $"• 待整理文件总大小: {needGb:F2} GB\n" +
                        $"• 目标磁盘 ({driveName}) 剩余空间: {freeGb:F2} GB\n\n" +
                        $"建议点击【否】中止操作，更换大容量硬盘。是否强行继续？",
                        "磁盘空间不足预警",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button2);
                    proceed = (dr == DialogResult.Yes);
                }));
                return proceed;
            };

            try
            {
                var result = await Task.Run(() => engine.Execute(options));
                if (!result.Cancelled && result.TotalFound > 0)
                {
                    currentConflicts = result.Conflicts;
                    ShowCompletionDialog(result);
                }
            }
            catch (Exception ex)
            {
                AppendLog($"❌ 运行异常: {ex.Message}");
                MessageBox.Show($"处理过程中发生错误: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetUIEnabled(true);
                btnOpenReview.Enabled = currentConflicts.Count > 0;
            }
        }

        private void ShowCompletionDialog(DeduplicationResult r)
        {
            double savedMb = r.SavedBytes / (1024.0 * 1024.0);
            if (r.Conflicts.Count > 0)
            {
                var ask = MessageBox.Show(
                    $"整理已全部完成！\n\n" +
                    $"• 归档保存: {r.ArchivedCount} 个\n" +
                    $"• 同名重复去重: {r.ExactDedupCount} 个\n" +
                    $"• 改名同内容哈希去重: {r.RenamedDedupCount} 个\n" +
                    $"• 合计节省空间: {savedMb:F2} MB\n" +
                    $"• 发现待审查冲突: {r.Conflicts.Count} 组\n\n" +
                    $"是否立即打开【并排对比审查工具】？(审查胜出文件将自动归档)",
                    "整理完成 - 建议审查冲突",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (ask == DialogResult.Yes) OpenConflictReview();
            }
            else
            {
                MessageBox.Show(
                    $"整理已全部完成！\n\n" +
                    $"• 归档保存: {r.ArchivedCount} 个\n" +
                    $"• 同名重复去重: {r.ExactDedupCount} 个\n" +
                    $"• 改名同内容哈希去重: {r.RenamedDedupCount} 个\n" +
                    $"• 合计节省空间: {savedMb:F2} MB\n" +
                    $"• 无任何冲突项！",
                    "处理完成",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
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
            btnOpenReview.Enabled = currentConflicts.Any(g => g.FilePaths.Count(File.Exists) >= 2);
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
            if (InvokeRequired) { Invoke(new Action(() => AppendLog(message))); return; }
            txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\r\n");
        }

        private void UpdateStatus(string status, int progressPercent)
        {
            if (InvokeRequired) { Invoke(new Action(() => UpdateStatus(status, progressPercent))); return; }
            lblStatus.Text = status;
            progressBar.Value = Math.Clamp(progressPercent, 0, 100);
        }
    }
}
