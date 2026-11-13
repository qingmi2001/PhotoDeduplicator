using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace PhotoDeduplicatorGUI.Services
{
    /// <summary>
    /// 元数据与时间提取服务：照片优先读取 Exif 拍摄时间，普通文件使用修改时间
    /// </summary>
    public static class MetadataService
    {
        public static readonly HashSet<string> PhotoVideoExts = new(StringComparer.OrdinalIgnoreCase)
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

        public static DateTime ExtractFileDate(string filePath, FileInfo? fi = null)
        {
            fi ??= new FileInfo(filePath);
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
            if (date.Year < 1970 || date.Year > 2100) date = fi.CreationTime;
            if (date.Year < 1970 || date.Year > 2100) date = DateTime.Now;
            return date;
        }

        public static string GetArchiveTargetDirectory(string destRootDir, DateTime date)
        {
            return Path.Combine(destRootDir, "整理归档", $"{date.Year}年", $"{date.Month}月");
        }

        public static string GetUniqueTargetPath(string dir, string fileName)
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

        public static string SanitizeDirName(string name)
        {
            string baseName = Path.GetFileNameWithoutExtension(name);
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                baseName = baseName.Replace(c, '_');
            }
            return string.IsNullOrWhiteSpace(baseName) ? "未命名" : baseName.Trim();
        }
    }
}
