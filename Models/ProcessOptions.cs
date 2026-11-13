using System.Collections.Generic;

namespace PhotoDeduplicatorGUI.Models
{
    public class ProcessOptions
    {
        public List<string> SourceDirs { get; set; } = new();
        public string DestDir { get; set; } = string.Empty;
        public bool AllowAllFiles { get; set; }
        public HashSet<string>? AllowedExtensions { get; set; }
    }

    public class DeduplicationResult
    {
        public bool Cancelled { get; set; }
        public int TotalFound { get; set; }
        public long TotalSourceBytes { get; set; }
        public int ArchivedCount { get; set; }
        public int ExactDedupCount { get; set; }
        public int RenamedDedupCount { get; set; }
        public long SavedBytes { get; set; }
        public int SameNameDiffSizeCount { get; set; }
        public int SameSizeDiffNameCount { get; set; }
        public int SameNameSizeDiffContentCount { get; set; }
        public List<ConflictGroup> Conflicts { get; set; } = new();
        public string ReportPath { get; set; } = string.Empty;
        public double ElapsedSeconds { get; set; }
    }
}
