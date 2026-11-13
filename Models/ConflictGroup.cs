using System.Collections.Generic;

namespace PhotoDeduplicatorGUI.Models
{
    public class ConflictGroup
    {
        public string ConflictType { get; set; } = string.Empty; // "同名不同大小" | "同大小不同名(内容不同)" | "同名同大小但内容不同"
        public string Title { get; set; } = string.Empty;
        public List<string> FilePaths { get; set; } = new();
    }
}
