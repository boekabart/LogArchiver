using System;
using System.CodeDom;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.Cryptography;
using System.Threading;
using System.Xml.Serialization;

namespace ArchiveOldFiles
{
    [Serializable]
    public class ConfigEntry
    {
        [XmlAttribute]
        public string Directory { get; set; }
        [XmlAttribute]
        public string ArchiveDirectory { get; set; }
        [XmlAttribute]
        public int Days { get; set; }
    }

    [Serializable]
    public class Config
    {
        [XmlElement("Entry")]
        public ConfigEntry[] Entries { get; set; }
    }

    internal class Program
    {
        private static int Main(string[] args)
        {
            var config = TryLoadConfig();
            if (config == null)
            {
                if (args.Length < 2)
                    return Error("Usage: ArchiveOldFiles <dir> <days>");
                int days;
                if (!int.TryParse(args[1], out days))
                    return Error("Usage: ArchiveOldFiles <dir> <days>");

                config = new Config
                {
                    Entries = new[]
                    {
                        new ConfigEntry
                        {
                            Directory = args[0],
                            Days = days
                        }
                    }
                };
                SaveConfigIfNotExists(config);
            }

            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;

            if (0 != CheckConfig(config))
                return Error("Invalid configuration");

            return config.Entries.Select(DoArchive).Sum();
        }

        private static int DoArchive(ConfigEntry entry)
        {
            return DoArchive( entry.Directory, entry.ArchiveDirectory, entry.Days);
        }

        private static int DoArchive ( string  dir, string archivePath, int days)
        {
            if (!Try(() =>
            {
                if (!Directory.Exists(archivePath))
                    Directory.CreateDirectory(archivePath);
            }))
            {
                return Error("Failed to create archive folder");
            }

            Console.WriteLine("Setting compression attributes for archive folder {0} and subfolders", archivePath);
            var di = new DirectoryInfo(archivePath);
            TrySetCompressionOn(di);
            foreach (var path in di.EnumerateDirectories("*.*", SearchOption.AllDirectories).Where(fi => !fi.IsCompressed()))
                if (TrySetCompressionOn(path))
                    Console.WriteLine("Compressed folder {0}", path.Name);

            Console.WriteLine("Archiving old files");
            var delay = TimeSpan.FromDays(days);
            var now = DateTime.UtcNow.Date;

            var dirInfo = new DirectoryInfo(dir);
            var fileInfos = dirInfo.EnumerateFiles();
            var oldFiles = fileInfos.Where(fi => (now - fi.LastWriteTimeUtc) > delay);
            foreach (var file in oldFiles)
                MoveFileToArchive(file);

            Console.WriteLine("Setting compression attributes for archive folder contents");
            foreach (var path in di.EnumerateFiles("*.*", SearchOption.AllDirectories).Where(fi => !fi.IsCompressed()))
                if (TrySetCompressionOn(path))
                       Console.WriteLine("Compressed {0}", path.Name);
            return 0;
        }

        private static int CheckConfig(Config config)
        {
            if (config == null) return Error("No Configuration");
            if (config.Entries == null) return Error("No Configuration Entries");
            if (!config.Entries.Any()) return Error("No Configuration Entries");
            return config.Entries.Select(CheckConfigEntry).Sum();
        }

        private static int CheckConfigEntry(ConfigEntry ce)
        {
            if (string.IsNullOrWhiteSpace(ce.Directory)) return Error("No source directory set");
            if (!Directory.Exists(ce.Directory))
                return Error("Directory {0} does not exist", ce.Directory);
            if (ce.Days < 0) return Error("Negative # of days not supported");
            if (string.IsNullOrWhiteSpace(ce.ArchiveDirectory))
                ce.ArchiveDirectory = "Archive";
            if (!Path.IsPathRooted(ce.ArchiveDirectory))
                ce.ArchiveDirectory = Path.Combine(ce.Directory, ce.ArchiveDirectory);
            return 0;
        }

        private static void SaveConfigIfNotExists( Config config )
        {
            const string configFile = @"ArchiveOldFiles.config";
            try
            {
                if (!File.Exists(configFile))
                    XmlHelper<Config>.ToFile(config, configFile);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("Error writing config file {0}: {1}", configFile, exception);
            }
        }

        private static Config TryLoadConfig()
        {
            const string configFile = @"ArchiveOldFiles.config";
            try
            {
                if (File.Exists(configFile))
                    return XmlHelper<Config>.FromFile(configFile);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("Error reading config file {0}: {1}", configFile, exception);
            }
            Console.Error.WriteLine("No config file {0} - checking command line");
            return null;
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

        private static bool TrySetCompressionOn(DirectoryInfo directoryInfo)
        {
            try
            {
                if ((directoryInfo.Attributes & FileAttributes.Compressed) == FileAttributes.Compressed)
                {
                    return true;
                }
                var destinationDirFixed = directoryInfo.FullName.Replace(@"\", "/").TrimEnd('/');
                var objPath = "Win32_Directory.Name=" + "\"" + destinationDirFixed + "\"";
                using (var dirManagementObject = new ManagementObject(objPath))
                {
                    var outParams = dirManagementObject.InvokeMethod("Compress", null, null);
                    var ret = (uint) (outParams.Properties["ReturnValue"].Value);
                    return ret == 0;
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Failed to set compression on Archive folder: {0}", e.Message);
                return false;
            }
        }

        private static bool TrySetCompressionOn(FileInfo fileInfo)
        {
            try
            {
                if (fileInfo.IsCompressed())
                {
                    return true;
                }
                var destinationDirFixed = fileInfo.FullName.Replace(@"\", "/").TrimEnd('/');
                var objPath = "CIM_DataFile.Name=" + "\"" + destinationDirFixed + "\"";
                using (var dataFileMgmtObject = new ManagementObject(objPath))
                {
                    var outParams = dataFileMgmtObject.InvokeMethod("Compress", null, null);
                    var ret = (uint) (outParams.Properties["ReturnValue"].Value);
                    return ret == 0;
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Failed to set compression on file: {0}", e.Message);
                return false;
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

        private static int Error(string format, params object[] args)
        {
            Console.Error.WriteLine(format, args);
            if (!Console.IsErrorRedirected)
                Console.ReadKey();
            return 1;
        }
    }

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
