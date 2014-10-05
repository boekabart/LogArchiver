using System.IO;

namespace OldFileArchiver
{
    public static class FileInfoExtensions
    {
        public static bool IsCompressed(this FileInfo fileInfo)
        {
            return (fileInfo.Attributes & FileAttributes.Compressed) == FileAttributes.Compressed;
        }
        public static bool IsCompressed(this DirectoryInfo dirInfo)
        {
            return (dirInfo.Attributes & FileAttributes.Compressed) == FileAttributes.Compressed;
        }
    }
}