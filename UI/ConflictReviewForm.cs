using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using PhotoDeduplicatorGUI.Models;
using PhotoDeduplicatorGUI.Services;

namespace PhotoDeduplicatorGUI.UI
{
    /// <summary>
    /// V2.0 冲突文件并排比对审查窗口：支持 3+ 多版本擂台筛选、键盘快捷键、胜出者自动归档
    /// </summary>
    public class ConflictReviewForm : Form
    {
        private readonly List<ConflictGroup> groups;
        private readonly string destDir;
        private int currentIndex = 0;
        private int archivedWinnersCount = 0;

        private Label lblHeader = null!;
        private Button btnPrev = null!;
        private Button btnNext = null!;
        private Label lblGroupProgress = null!;

        private PictureBox picA = null!;
        private Label lblInfoA = null!;
        private Button btnKeepA = null!;

        private PictureBox picB = null!;
        private Label lblInfoB = null!;
        private Button btnKeepB = null!;

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
            KeyPreview = true; // 启用全局快捷键监听
            InitializeComponent();
            LoadCurrentGroup();
        }

        private void InitializeComponent()
        {
            Text = "冲突文件并排比对审查工具 V2.0 (支持快捷键 & 自动归档)";
            Size = new Size(1040, 800);
            MinimumSize = new Size(920, 680);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            BackColor = Color.FromArgb(245, 247, 250);

            var panelTop = new Panel
            {
                Dock = DockStyle.Top,
                Height = 68,
                BackColor = Color.White,
                Padding = new Padding(16, 10, 16, 10)
            };

            lblHeader = new Label
            {
                Text = "冲突详情加载中...",
                Location = new Point(16, 10),
                Size = new Size(720, 24),
                Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
                ForeColor = Color.FromArgb(33, 37, 41)
            };

            lblGroupProgress = new Label
            {
                Text = "进度: 1 / 1",
                Location = new Point(16, 38),
                Size = new Size(720, 20),
                ForeColor = Color.FromArgb(108, 117, 125)
            };

            btnPrev = new Button
            {
                Text = "◀ 上一组 (←)",
                Location = new Point(790, 16),
                Size = new Size(105, 34),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnPrev.Click += (s, e) => Navigate(-1);

            btnNext = new Button
            {
                Text = "下一组 (→) ▶",
                Location = new Point(905, 16),
                Size = new Size(105, 34),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnNext.Click += (s, e) => Navigate(1);

            panelTop.Controls.AddRange(new Control[] { lblHeader, lblGroupProgress, btnPrev, btnNext });

            var panelBottom = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 60,
                BackColor = Color.White
            };

            btnKeepBoth = new Button
            {
                Text = "🤝 全部保留并跳过此组 (Space)",
                Location = new Point(300, 12),
                Size = new Size(230, 36),
                BackColor = Color.FromArgb(240, 242, 245)
            };
            btnKeepBoth.Click += (s, e) => Navigate(1);

            btnOpenFolder = new Button
            {
                Text = "📂 在资源管理器中查看",
                Location = new Point(545, 12),
                Size = new Size(180, 36)
            };
            btnOpenFolder.Click += BtnOpenFolder_Click;

            panelBottom.Controls.AddRange(new Control[] { btnKeepBoth, btnOpenFolder });

            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 510,
                SplitterWidth = 8,
                Padding = new Padding(12, 10, 12, 10)
            };

            // 左侧 A
            var panelA = new GroupBox
            {
                Text = "文件 A (按数字键 1 保留此项)",
                Dock = DockStyle.Fill,
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold)
            };
            picA = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(235, 238, 242) };
            lblInfoA = new Label { Dock = DockStyle.Bottom, Height = 92, BackColor = Color.White, Font = new Font("Microsoft YaHei UI", 9F), Padding = new Padding(8) };
            btnKeepA = new Button
            {
                Text = "✅ [1] 保留左侧 (淘汰右侧并自动归档胜出者)",
                Dock = DockStyle.Bottom,
                Height = 42,
                BackColor = Color.FromArgb(13, 110, 253),
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat
            };
            btnKeepA.Click += (s, e) => ResolvePair(keepA: true);

            panelA.Controls.Add(picA);
            panelA.Controls.Add(lblInfoA);
            panelA.Controls.Add(btnKeepA);
            split.Panel1.Controls.Add(panelA);

            // 右侧 B
            var panelB = new GroupBox
            {
                Text = "文件 B (按数字键 2 保留此项)",
                Dock = DockStyle.Fill,
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold)
            };
            picB = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(235, 238, 242) };
            lblInfoB = new Label { Dock = DockStyle.Bottom, Height = 92, BackColor = Color.White, Font = new Font("Microsoft YaHei UI", 9F), Padding = new Padding(8) };
            btnKeepB = new Button
            {
                Text = "✅ [2] 保留右侧 (淘汰左侧并自动归档胜出者)",
                Dock = DockStyle.Bottom,
                Height = 42,
                BackColor = Color.FromArgb(13, 110, 253),
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat
            };
            btnKeepB.Click += (s, e) => ResolvePair(keepA: false);

            panelB.Controls.Add(picB);
            panelB.Controls.Add(lblInfoB);
            panelB.Controls.Add(btnKeepB);
            split.Panel2.Controls.Add(panelB);

            Controls.Add(split);
            Controls.Add(panelBottom);
            Controls.Add(panelTop);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (groups.Count == 0) return base.ProcessCmdKey(ref msg, keyData);

            switch (keyData)
            {
                case Keys.Left:
                case Keys.A:
                    Navigate(-1);
                    return true;
                case Keys.Right:
                case Keys.D:
                    Navigate(1);
                    return true;
                case Keys.D1:
                case Keys.NumPad1:
                    ResolvePair(keepA: true);
                    return true;
                case Keys.D2:
                case Keys.NumPad2:
                    ResolvePair(keepA: false);
                    return true;
                case Keys.Space:
                    Navigate(1);
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void Navigate(int delta)
        {
            if (groups.Count == 0) return;
            currentIndex = (currentIndex + delta + groups.Count) % groups.Count;
            LoadCurrentGroup();
        }

        private void LoadCurrentGroup()
        {
            if (groups.Count == 0)
            {
                lblHeader.Text = "🎉 所有冲突文件已全部审查完毕！";
                lblGroupProgress.Text = $"本次共决断并自动归档胜出文件 {archivedWinnersCount} 个。";
                btnPrev.Enabled = btnNext.Enabled = btnKeepA.Enabled = btnKeepB.Enabled = btnKeepBoth.Enabled = false;
                ClearImage(picA);
                ClearImage(picB);
                lblInfoA.Text = lblInfoB.Text = "";
                return;
            }

            var g = groups[currentIndex];
            g.FilePaths = g.FilePaths.Where(File.Exists).ToList();
            if (g.FilePaths.Count < 2)
            {
                groups.RemoveAt(currentIndex);
                if (currentIndex >= groups.Count) currentIndex = 0;
                LoadCurrentGroup();
                return;
            }

            lblHeader.Text = $"[{g.ConflictType}] {g.Title}";
            lblGroupProgress.Text = $"第 {currentIndex + 1} / {groups.Count} 组冲突 | 本组共 {g.FilePaths.Count} 个版本待擂台筛选 | 快捷键: [1]留左 [2]留右 [←/→]翻页 [Space]跳过";

            LoadFileInfo(g.FilePaths[0], picA, lblInfoA);
            LoadFileInfo(g.FilePaths[1], picB, lblInfoB);
        }

        private void LoadFileInfo(string path, PictureBox pic, Label lbl)
        {
            ClearImage(pic);
            var fi = new FileInfo(path);
            string resolutionStr = "非图像格式";

            if (ImageExts.Contains(fi.Extension))
            {
                try
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var origBmp = new Bitmap(stream);
                    resolutionStr = $"{origBmp.Width} x {origBmp.Height} 像素";
                    pic.Image = new Bitmap(origBmp);
                }
                catch { resolutionStr = "图像不可预览"; }
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

        /// <summary>
        /// 擂台式淘汰：保留胜出者、将淘汰者移入弃用副本目录；若组内只剩 1 个最终胜出者，则自动归档至 整理归档/年/月/
        /// </summary>
        private void ResolvePair(bool keepA)
        {
            if (groups.Count == 0) return;
            var g = groups[currentIndex];
            if (g.FilePaths.Count < 2) return;

            string winnerPath = keepA ? g.FilePaths[0] : g.FilePaths[1];
            string loserPath = keepA ? g.FilePaths[1] : g.FilePaths[0];

            try
            {
                ClearImage(picA);
                ClearImage(picB);

                // 1. 淘汰文件安全移入弃用目录
                string excludedFolder = Path.Combine(destDir, "人工筛选_已排除的弃用副本");
                Directory.CreateDirectory(excludedFolder);
                string excludedTarget = MetadataService.GetUniqueTargetPath(excludedFolder, Path.GetFileName(loserPath));
                if (File.Exists(loserPath)) File.Move(loserPath, excludedTarget);

                g.FilePaths.Remove(loserPath);

                // 2. 若本组所有候选者已淘汰完毕，将唯一胜出者自动归档到 整理归档/YYYY年/M月/
                if (g.FilePaths.Count == 1 && File.Exists(winnerPath))
                {
                    DateTime fileDate = MetadataService.ExtractFileDate(winnerPath);
                    string archiveDir = MetadataService.GetArchiveTargetDirectory(destDir, fileDate);
                    Directory.CreateDirectory(archiveDir);

                    // 还原干净文件名（去掉同名冲突时附加的 [1234字节]_ 前缀）
                    string cleanName = Path.GetFileName(winnerPath);
                    if (g.ConflictType == "同名不同大小" && cleanName.StartsWith("[") && cleanName.Contains("字节]_"))
                    {
                        int idx = cleanName.IndexOf("字节]_", StringComparison.Ordinal);
                        if (idx > 0) cleanName = cleanName[(idx + 4)..];
                    }

                    string archiveTarget = MetadataService.GetUniqueTargetPath(archiveDir, cleanName);
                    File.Move(winnerPath, archiveTarget);
                    archivedWinnersCount++;

                    groups.RemoveAt(currentIndex);
                    if (currentIndex >= groups.Count) currentIndex = 0;
                }

                LoadCurrentGroup();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"处理文件移动时发生异常: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                LoadCurrentGroup();
            }
        }

        private void BtnOpenFolder_Click(object? sender, EventArgs e)
        {
            if (groups.Count == 0) return;
            var first = groups[currentIndex].FilePaths.FirstOrDefault(File.Exists);
            if (first != null)
            {
                string dir = Path.GetDirectoryName(first) ?? destDir;
                try { Process.Start("explorer.exe", dir); } catch { }
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
