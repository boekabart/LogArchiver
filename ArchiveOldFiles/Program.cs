using System;
using System.IO;
using System.Linq;
using System.Management;

namespace ArchiveOldFiles
{
    class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length < 2)
                return Error("Usage: ArchiveOldFiles <dir> <days>");
            var dir = args[0];
            if (!Directory.Exists(dir))
                return Error("Directory {0} does not exist", dir);

            int days;
            if (!int.TryParse(args[1], out days))
                return Error("Usage: ArchiveOldFiles <dir> <days>");

            var archivePath = Path.Combine(dir, "Archive");

            if (!Try(() =>
            {
                if (!Directory.Exists(archivePath))
                    Directory.CreateDirectory(archivePath);
            }))
            {
                return Error("Failed to create archive folder");
            }

            TrySetCompression(archivePath);

            var delay = TimeSpan.FromDays(days);
            var now = DateTime.UtcNow.Date;

            var dirInfo = new DirectoryInfo(dir);
            var fileInfos = dirInfo.EnumerateFiles();
            var oldFiles = fileInfos.Where(fi => (now - fi.LastWriteTimeUtc) > delay);
            foreach (var file in oldFiles)
                MoveFileToArchive(file);

            return 0;
        }

        private static bool Try(Action action)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Exception: {0}", e.Message);
                return false;
            }
        }

        private static void TrySetCompression(string destinationDir)
        {
            try
            {
                var directoryInfo = new DirectoryInfo(destinationDir);
                if ((directoryInfo.Attributes & FileAttributes.Compressed) == FileAttributes.Compressed)
                {
                    Console.WriteLine("Archive folder already compressed");
                    return;
                }
                var destinationDirFixed = destinationDir.Replace(@"\", "/").TrimEnd('/');
                var objPath = "Win32_Directory.Name=" + "\"" + destinationDirFixed + "\"";
                using (var dirManagementObject = new ManagementObject(objPath))
                {
                    var outParams = dirManagementObject.InvokeMethod("Compress", null, null);
                    var ret = (uint) (outParams.Properties["ReturnValue"].Value);
                    Console.WriteLine("Tried to set compression on Archive folder, result code {0:X}", ret);
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Failed to set compression on Archive folder: {0}", e.Message);
            }
        }

        private static void MoveFileToArchive(FileInfo file)
        {
            var day = file.LastWriteTimeUtc.Date;
            var subDir = string.Format(@"Archive\{0:yyyy-MM-dd}", day);
            var fullDir = Path.Combine(file.DirectoryName, subDir);
            var fullPath = Path.Combine(fullDir, file.Name);
            try
            {
                if (!Directory.Exists(fullDir))
                    Directory.CreateDirectory(fullDir);
                File.Move(file.FullName, fullPath);
                Console.WriteLine("Moved file {0} to {1}", file.Name, fullDir);
            }
            catch (Exception e)
            {
                Error("Error moving file {0} to {2}: '{1}'", file.Name, e.Message, fullDir);
            }
        }

        static int Error(string format, params object[] args)
        {
            Console.Error.WriteLine(format, args);
            return 1;
        }
    }
}
