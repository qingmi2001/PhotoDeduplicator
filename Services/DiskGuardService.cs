using System.IO;

namespace PhotoDeduplicatorGUI.Services
{
    public class DiskSpaceInfo
    {
        public string DriveName { get; set; } = string.Empty;
        public long AvailableFreeBytes { get; set; }
        public long TotalSizeBytes { get; set; }
        public bool IsValid { get; set; }
    }

    /// <summary>
    /// 磁盘容量检测服务（防爆仓保护）
    /// </summary>
    public static class DiskGuardService
    {
        public static DiskSpaceInfo GetDriveSpace(string targetPath)
        {
            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(targetPath)) ?? string.Empty;
                if (!string.IsNullOrEmpty(root))
                {
                    var drive = new DriveInfo(root);
                    return new DiskSpaceInfo
                    {
                        DriveName = drive.Name,
                        AvailableFreeBytes = drive.AvailableFreeSpace,
                        TotalSizeBytes = drive.TotalSize,
                        IsValid = true
                    };
                }
            }
            catch { }
            return new DiskSpaceInfo { IsValid = false };
        }
    }
}
