using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using PhotoDeduplicatorGUI.Models;

namespace PhotoDeduplicatorGUI.Services
{
    /// <summary>
    /// 核心去重与归档引擎（完全与 UI 解耦，通过回调报告日志与进度）
    /// </summary>
    public class DeduplicationEngine
    {
        public event Action<string>? OnLog;
        public event Action<string, int>? OnProgress;

        /// <summary>
        /// 磁盘空间不足时的确认回调：(总需求字节, 剩余字节, 盘符) -> 是否强行继续
        /// </summary>
        public Func<long, long, string, bool>? OnDiskSpaceWarning { get; set; }

        public DeduplicationResult Execute(ProcessOptions options, CancellationToken ct = default)
        {
            var result = new DeduplicationResult();
            var watch = Stopwatch.StartNew();

            Log("===== 开始全文件去重与整理任务 (V2.0 哈希精确引擎) =====");
            Log($"源文件夹数: {options.SourceDirs.Count} 个");
            Log($"目标文件夹: {options.DestDir}");

            if (options.AllowAllFiles)
                Log("文件过滤范围: 全部文件 (*.*)");
            else if (options.AllowedExtensions != null)
                Log($"文件过滤范围: 共 {options.AllowedExtensions.Count} 种格式 ({string.Join(", ", options.AllowedExtensions.Take(8))}...)");

            // 1. 深度扫描
            var allFiles = ScanFiles(options, ct);
            result.TotalFound = allFiles.Count;
            result.TotalSourceBytes = allFiles.Sum(f => f.Size);

            double totalGb = result.TotalSourceBytes / (1024.0 * 1024.0 * 1024.0);
            Log($"扫描完成！共获取到 {result.TotalFound} 个符合条件的文件，总大小约为: {totalGb:F2} GB。");

            if (result.TotalFound == 0 || ct.IsCancellationRequested)
            {
                result.Cancelled = ct.IsCancellationRequested;
                Progress("状态：未找到符合条件的文件。", 0);
                return result;
            }

            // 2. 防爆仓检测
            var space = DiskGuardService.GetDriveSpace(options.DestDir);
            if (space.IsValid)
            {
                double freeGb = space.AvailableFreeBytes / (1024.0 * 1024.0 * 1024.0);
                Log($"[磁盘检测] 目标盘 ({space.DriveName}) 可用剩余: {freeGb:F2} GB | 待处理总大小: {totalGb:F2} GB");

                if (space.AvailableFreeBytes < result.TotalSourceBytes)
                {
                    bool forceContinue = OnDiskSpaceWarning?.Invoke(result.TotalSourceBytes, space.AvailableFreeBytes, space.DriveName) ?? false;
                    if (!forceContinue)
                    {
                        Log("🛑 用户已取消整理：已成功防止磁盘爆仓。");
                        Progress("状态：由于目标磁盘空间不足，任务已取消。", 0);
                        result.Cancelled = true;
                        return result;
                    }
                    Log("⚠️ 用户选择忽略空间预警强行继续。");
                }
                else
                {
                    Log("✅ 磁盘空间充裕，安全检测通过！");
                }
            }

            // 3. 哈希精确去重阶段（按大小分组，仅对同大小文件进行首尾抽样 + SHA-256 复核）
            Progress("状态：正在对同大小文件进行 SHA-256 哈希精确复核...", 10);
            var activePool = new List<FileItem>();
            var sameNameSizeDiffContentGroups = new List<List<FileItem>>();

            var sizeGroups = allFiles.GroupBy(f => f.Size).ToList();
            int hashedCount = 0;

            foreach (var sg in sizeGroups)
            {
                ct.ThrowIfCancellationRequested();
                var items = sg.ToList();

                if (items.Count == 1)
                {
                    activePool.Add(items[0]);
                    continue;
                }

                // 多个文件大小相同 -> 计算快速哈希
                foreach (var item in items)
                {
                    item.QuickHash = HashService.ComputeQuickHash(item.FullPath);
                    hashedCount++;
                }

                // 按快速哈希二次分组
                foreach (var qg in items.GroupBy(x => x.QuickHash))
                {
                    var qList = qg.ToList();
                    if (qList.Count == 1)
                    {
                        activePool.Add(qList[0]);
                        continue;
                    }

                    // 快速哈希也碰撞 -> 计算完整 SHA-256 100% 确认内容一致
                    foreach (var item in qList)
                    {
                        item.FullHash = HashService.ComputeFullSha256(item.FullPath);
                    }

                    foreach (var fg in qList.GroupBy(x => x.FullHash))
                    {
                        var identicalFiles = fg.ToList();
                        // 取第一个作为母本进入有效池
                        var master = identicalFiles[0];
                        activePool.Add(master);

                        if (identicalFiles.Count > 1)
                        {
                            for (int i = 1; i < identicalFiles.Count; i++)
                            {
                                var dup = identicalFiles[i];
                                result.SavedBytes += dup.Size;
                                if (string.Equals(dup.FileName, master.FileName, StringComparison.OrdinalIgnoreCase))
                                    result.ExactDedupCount++;
                                else
                                    result.RenamedDedupCount++;
                            }
                        }
                    }
                }
            }

            Log($"[哈希引擎] 已对 {hashedCount} 个同大小嫌疑文件完成指纹比对：" +
                $"同名完全重复 {result.ExactDedupCount} 个，改名但内容完全一致 {result.RenamedDedupCount} 个（已自动去重）。");

            // 4. 针对去重后的有效文件池，识别剩余冲突（同名不同大小 / 同大小但哈希不同 / 同名同大小但哈希不同）
            var nameGroups = activePool.GroupBy(f => f.FileName, StringComparer.OrdinalIgnoreCase)
                                       .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            var remainingSizeGroups = activePool.GroupBy(f => f.Size)
                                                .ToDictionary(g => g.Key, g => g.ToList());

            string dirDiffSizeSameName = Path.Combine(options.DestDir, "人工筛选_同名不同大小");
            string dirDiffNameSameSize = Path.Combine(options.DestDir, "人工筛选_同大小不同名");
            string dirSameNameSizeDiffContent = Path.Combine(options.DestDir, "人工筛选_同名同大小但内容不同");

            var conflictGroups = new List<ConflictGroup>();
            var recordedSameName = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var recordedSameSize = new HashSet<long>();
            var recordedSameNameSize = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Log("正在进行文件复制与智能年月归档...");

            for (int i = 0; i < activePool.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var f = activePool[i];

                var sameNameList = nameGroups[f.FileName];
                var sameSizeList = remainingSizeGroups[f.Size];

                // 情况 A：同名同大小但哈希不同（极端碰撞保护）
                var exactNameSizeDiffContent = sameNameList.Where(x => x.Size == f.Size).ToList();
                bool isSameNameSizeDiffContent = exactNameSizeDiffContent.Count > 1;
                bool isSameNameDiffSize = sameNameList.Any(x => x.Size != f.Size);
                bool isSameSizeDiffName = sameSizeList.Count > 1 && !isSameNameSizeDiffContent;

                if (isSameNameSizeDiffContent)
                {
                    string subDir = Path.Combine(dirSameNameSizeDiffContent, MetadataService.SanitizeDirName(f.FileName));
                    Directory.CreateDirectory(subDir);
                    string shortHash = string.IsNullOrEmpty(f.FullHash) ? Guid.NewGuid().ToString("N")[..6] : f.FullHash[..6];
                    string targetPath = MetadataService.GetUniqueTargetPath(subDir, $"[{shortHash}]_{f.FileName}");
                    CopyFileSafe(f.FullPath, targetPath);
                    result.SameNameSizeDiffContentCount++;

                    string key = $"{f.FileName}_{f.Size}";
                    if (!recordedSameNameSize.Contains(key))
                    {
                        recordedSameNameSize.Add(key);
                        conflictGroups.Add(new ConflictGroup
                        {
                            ConflictType = "同名同大小但内容不同",
                            Title = $"同名同体积碰撞: {f.FileName} ({f.Size:N0} 字节)"
                        });
                    }
                    conflictGroups.Last(c => c.ConflictType == "同名同大小但内容不同" && c.Title.Contains(f.FileName)).FilePaths.Add(targetPath);
                }
                else if (isSameNameDiffSize)
                {
                    string subDir = Path.Combine(dirDiffSizeSameName, MetadataService.SanitizeDirName(f.FileName));
                    Directory.CreateDirectory(subDir);
                    string targetPath = MetadataService.GetUniqueTargetPath(subDir, $"[{f.Size}字节]_{f.FileName}");
                    CopyFileSafe(f.FullPath, targetPath);
                    result.SameNameDiffSizeCount++;

                    if (!recordedSameName.Contains(f.FileName))
                    {
                        recordedSameName.Add(f.FileName);
                        conflictGroups.Add(new ConflictGroup
                        {
                            ConflictType = "同名不同大小",
                            Title = $"同名文件: {f.FileName}"
                        });
                    }
                    conflictGroups.Last(c => c.ConflictType == "同名不同大小" && c.Title == $"同名文件: {f.FileName}").FilePaths.Add(targetPath);
                }
                else if (isSameSizeDiffName)
                {
                    string subDir = Path.Combine(dirDiffNameSameSize, $"大小_{f.Size}字节");
                    Directory.CreateDirectory(subDir);
                    string targetPath = MetadataService.GetUniqueTargetPath(subDir, f.FileName);
                    CopyFileSafe(f.FullPath, targetPath);
                    result.SameSizeDiffNameCount++;

                    if (!recordedSameSize.Contains(f.Size))
                    {
                        recordedSameSize.Add(f.Size);
                        conflictGroups.Add(new ConflictGroup
                        {
                            ConflictType = "同大小不同名(内容不同)",
                            Title = $"相同字节大小: {f.Size:N0} 字节"
                        });
                    }
                    conflictGroups.Last(c => c.ConflictType == "同大小不同名(内容不同)" && c.Title == $"相同字节大小: {f.Size:N0} 字节").FilePaths.Add(targetPath);
                }
                else
                {
                    // 正常归档至 整理归档/YYYY年/M月/
                    string targetDir = MetadataService.GetArchiveTargetDirectory(options.DestDir, f.Date);
                    Directory.CreateDirectory(targetDir);
                    string targetPath = MetadataService.GetUniqueTargetPath(targetDir, f.FileName);
                    CopyFileSafe(f.FullPath, targetPath);
                    result.ArchivedCount++;
                }

                int percent = 15 + (int)((i + 1) * 85.0 / activePool.Count);
                if (i % 15 == 0 || i == activePool.Count - 1)
                {
                    Progress($"状态：正在复制与归档 ({i + 1}/{activePool.Count})...", percent);
                }
            }

            watch.Stop();
            result.ElapsedSeconds = watch.Elapsed.TotalSeconds;
            result.Conflicts = conflictGroups.Where(g => g.FilePaths.Count >= 2).ToList();
            result.ReportPath = WriteReport(options.DestDir, result);

            Log("===== 整理完成！ =====");
            Log($"按年月归档保存: {result.ArchivedCount} 个");
            Log($"同名完全重复去重: {result.ExactDedupCount} 个");
            Log($"改名同内容哈希去重: {result.RenamedDedupCount} 个");
            Log($"累计节省空间: {result.SavedBytes / (1024.0 * 1024.0):F2} MB");
            Log($"待审查冲突组数: {result.Conflicts.Count} 组");
            Log($"统计报告已生成: {result.ReportPath}");

            Progress("状态：全部处理完成！", 100);
            return result;
        }

        private List<FileItem> ScanFiles(ProcessOptions options, CancellationToken ct)
        {
            var allFiles = new List<FileItem>();
            var scannedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var sDir in options.SourceDirs)
            {
                if (!Directory.Exists(sDir)) continue;
                Log($"正在扫描: {sDir} ...");

                try
                {
                    foreach (var file in Directory.EnumerateFiles(sDir, "*.*", SearchOption.AllDirectories))
                    {
                        ct.ThrowIfCancellationRequested();
                        if (!scannedPaths.Add(file)) continue;

                        string ext = Path.GetExtension(file);
                        if (!options.AllowAllFiles && (options.AllowedExtensions == null || !options.AllowedExtensions.Contains(ext)))
                            continue;

                        var fi = new FileInfo(file);
                        allFiles.Add(new FileItem
                        {
                            FullPath = file,
                            FileName = fi.Name,
                            Size = fi.Length,
                            Date = MetadataService.ExtractFileDate(file, fi)
                        });
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Log($"⚠️ 扫描目录异常 ({sDir}): {ex.Message}");
                }
            }

            return allFiles;
        }

        private static void CopyFileSafe(string src, string dst)
        {
            try { File.Copy(src, dst, true); } catch { }
        }

        private static string WriteReport(string destDir, DeduplicationResult r)
        {
            string reportPath = Path.Combine(destDir, "去重与归档统计报告.txt");
            double totalGb = r.TotalSourceBytes / (1024.0 * 1024.0 * 1024.0);
            double savedMb = r.SavedBytes / (1024.0 * 1024.0);

            var sb = new StringBuilder();
            sb.AppendLine("================ 全文件去重与归档统计报告 (V2.0) ================");
            sb.AppendLine($"生成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"处理耗时: {r.ElapsedSeconds:F2} 秒");
            sb.AppendLine();
            sb.AppendLine($"1. 扫描总文件数:               {r.TotalFound} (总体积约 {totalGb:F2} GB)");
            sb.AppendLine($"2. 成功按年月归档文件数:       {r.ArchivedCount} (存放于: 整理归档/)");
            sb.AppendLine($"3. 同名同内容已去重文件数:     {r.ExactDedupCount}");
            sb.AppendLine($"4. 改名但同内容(哈希)去重数:   {r.RenamedDedupCount}");
            sb.AppendLine($"   -> 合计节省磁盘空间:        {savedMb:F2} MB");
            sb.AppendLine($"5. 同名不同大小(待核对)数:     {r.SameNameDiffSizeCount}");
            sb.AppendLine($"6. 同大小不同名(内容不同)数:   {r.SameSizeDiffNameCount}");
            sb.AppendLine($"7. 同名同大小内容碰撞数:       {r.SameNameSizeDiffContentCount}");
            sb.AppendLine($"   -> 需人工并排审查冲突组:    {r.Conflicts.Count} 组");
            sb.AppendLine("==================================================================");
            File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);
            return reportPath;
        }

        private void Log(string msg) => OnLog?.Invoke(msg);
        private void Progress(string status, int percent) => OnProgress?.Invoke(status, percent);
    }
}
