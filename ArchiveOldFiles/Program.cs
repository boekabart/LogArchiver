using System;
using System.IO;
using System.Linq;

namespace ArchiveOldFiles
{
    class Program
    {
        static int Main(string[] args)
        {
            if (args.Length<2)
                return Error("Usage: ArchiveOldFiles <dir> <days>");
            var dir = args[0];
            if (!Directory.Exists(dir))
                return Error("Directory {0} does not exist", dir);

            int days;
            if (!int.TryParse(args[1], out days))
                return Error("Usage: ArchiveOldFiles <dir> <days>");

            var delay = TimeSpan.FromDays(days);
            var now = DateTime.UtcNow.Date;

            var dirInfo = new DirectoryInfo(dir);
            var fileInfos = dirInfo.EnumerateFiles();
            var oldFiles = fileInfos.Where(fi => (now - fi.LastWriteTimeUtc) > delay);
            foreach (var file in oldFiles)
            {
                MoveFileToArchive(file);
            }
            return 0;
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
