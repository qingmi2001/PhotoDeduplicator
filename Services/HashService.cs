using System;
using System.IO;
using System.Security.Cryptography;

namespace PhotoDeduplicatorGUI.Services
{
    /// <summary>
    /// 高性能文件哈希服务：首尾 4KB 快速抽样 + 全量 SHA-256 精确校验
    /// </summary>
    public static class HashService
    {
        private const int SampleChunkSize = 4096;

        /// <summary>
        /// 计算首尾抽样快速哈希（O(1) 耗时，大文件也能毫秒级完成）
        /// </summary>
        public static string ComputeQuickHash(string filePath)
        {
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                long length = fs.Length;

                if (length <= SampleChunkSize * 2)
                {
                    using var sha = SHA256.Create();
                    return Convert.ToHexString(sha.ComputeHash(fs));
                }

                byte[] buffer = new byte[SampleChunkSize * 2 + 8];
                fs.ReadExactly(buffer, 0, SampleChunkSize);

                fs.Seek(-SampleChunkSize, SeekOrigin.End);
                fs.ReadExactly(buffer, SampleChunkSize, SampleChunkSize);

                BitConverter.GetBytes(length).CopyTo(buffer, SampleChunkSize * 2);

                byte[] hash = SHA256.HashData(buffer);
                return Convert.ToHexString(hash);
            }
            catch
            {
                return Guid.NewGuid().ToString("N");
            }
        }

        /// <summary>
        /// 计算完整文件 SHA-256（仅在大小与快速哈希均相同时触发，确保 100% 字节级一致）
        /// </summary>
        public static string ComputeFullSha256(string filePath)
        {
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 81920);
                using var sha = SHA256.Create();
                byte[] hash = sha.ComputeHash(fs);
                return Convert.ToHexString(hash);
            }
            catch
            {
                return Guid.NewGuid().ToString("N");
            }
        }
    }
}
