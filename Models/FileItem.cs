using System;

namespace PhotoDeduplicatorGUI.Models
{
    public class FileItem
    {
        public string FullPath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long Size { get; set; }
        public DateTime Date { get; set; }
        public string QuickHash { get; set; } = string.Empty;
        public string FullHash { get; set; } = string.Empty;
    }
}
