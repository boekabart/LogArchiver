using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using log4net.Config;

namespace OldFileArchiver
{
    internal class Program
    {
        private static readonly log4net.ILog Log = log4net.LogManager.GetLogger("ArchiveLogFiles");

        private static void LoadLogConfiguration()
        {
            try
            {
                XmlConfigurator.ConfigureAndWatch(new FileInfo("log4net.config"));
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Failed to Configure and Watch log4net config log4net.config: {0}", e);
            }
        }

        private static int Main(string[] args)
        {
            LoadLogConfiguration();

            var config = TryLoadConfig();
            if (config == null)
            {
                if (args.Length < 2)
                    return Error("Usage: OldFileArchiver [<dir> <days>]");
                int days;
                if (!Int32.TryParse(args[1], out days))
                    return Error("Usage: OldFileArchiver [<dir> <days>]");

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
            return DoArchive( entry.Directory, entry.ArchiveDirectory, entry.Days, entry.DeleteDays);
        }

        private static int DoArchive ( string  dir, string archivePath, int days, int deleteDays)
        {
            if (!Try(() =>
            {
                if (!Directory.Exists(archivePath))
                    Directory.CreateDirectory(archivePath);
            }))
            {
                return Error("Failed to create archive folder");
            }

            var archiveDirInfo = new DirectoryInfo(archivePath);
            var dirInfo = new DirectoryInfo(dir);
            var now = DateTime.UtcNow.Date;

            if (deleteDays > 0)
            {
                Log.InfoFormat("Deleting old files");
                var deleteDelay = TimeSpan.FromDays(deleteDays);
                var threshold = now - deleteDelay;
                var delFileInfos = dirInfo.EnumerateFiles("*.*").Where(fi=>fi.LastWriteTimeUtc < threshold);
                foreach (var fifi in delFileInfos)
                    TryDelete(fifi.FullName);

                foreach (var didi in archiveDirInfo.EnumerateDirectories().Where(di => IsDateDirAndOlderThan(di, threshold) ))
                    TryDeleteDirectoryFull(didi);
            }


            Log.InfoFormat("Setting compression attributes for archive folder {0} and subfolders", archivePath);
            TrySetCompressionOn(archiveDirInfo);
            foreach (var path in archiveDirInfo.EnumerateDirectories("*.*", SearchOption.AllDirectories).Where(fi => !fi.IsCompressed()))
                if (TrySetCompressionOn(path))
                    Log.InfoFormat("Compressed folder {0}", path.Name);

            Log.InfoFormat("Archiving old files");
            var delay = TimeSpan.FromDays(days);

            var fileInfos = dirInfo.EnumerateFiles();
            var oldFiles = fileInfos.Where(fi => (now - fi.LastWriteTimeUtc) > delay);
            foreach (var file in oldFiles)
                MoveFileToArchive(file);

            Log.InfoFormat("Setting compression attributes for archive folder contents");
            foreach (var path in archiveDirInfo.EnumerateFiles("*.*", SearchOption.AllDirectories).Where(fi => !fi.IsCompressed()))
                if (TrySetCompressionOn(path))
                       Log.InfoFormat("Compressed {0}", path.Name);
            return 0;
        }

        private static bool IsDateDirAndOlderThan(DirectoryInfo di, DateTime threshold)
        {
            DateTime parsed;
            if (!DateTime.TryParseExact(di.Name, "yyyy-MM-dd", null, DateTimeStyles.AssumeUniversal, out parsed))
                return false;
            return parsed < threshold;
        }

        private static void TryDeleteDirectoryFull(DirectoryInfo didi)
        {
            try
            {
                Log.InfoFormat("Deleting Directory {0}", didi.FullName);
                didi.Delete(true);
            }
            catch (Exception e)
            {
                Log.ErrorFormat("Exception: {0}", e.Message);
            }
        }

        private static void TryDelete(string fullName)
        {
            try
            {
                Log.InfoFormat("Deleting {0}", fullName);
                File.Delete(fullName);
            }
            catch (Exception e)
            {
                Log.ErrorFormat("Exception: {0}", e.Message);
            }
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
            if (String.IsNullOrWhiteSpace(ce.Directory)) return Error("No source directory set");
            if (!Directory.Exists(ce.Directory))
                return Error("Directory {0} does not exist", ce.Directory);
            if (ce.Days < 0) return Error("Negative # of days not supported");
            if (String.IsNullOrWhiteSpace(ce.ArchiveDirectory))
                ce.ArchiveDirectory = "Archive";
            if (!Path.IsPathRooted(ce.ArchiveDirectory))
                ce.ArchiveDirectory = Path.Combine(ce.Directory, ce.ArchiveDirectory);
            return 0;
        }

        private static void SaveConfigIfNotExists( Config config )
        {
            const string configFile = @"Example_OldFileArchiver.config";
            try
            {
                if (!File.Exists(configFile))
                    XmlHelper<Config>.ToFile(config, configFile);
            }
            catch (Exception exception)
            {
                Log.ErrorFormat("Error writing config file {0}: {1}", configFile, exception);
            }
        }

        private static Config TryLoadConfig()
        {
            const string configFile = @"OldFileArchiver.config";
            try
            {
                if (File.Exists(configFile))
                    return XmlHelper<Config>.FromFile(configFile);
            }
            catch (Exception exception)
            {
                Log.ErrorFormat("Error reading config file {0}: {1}", configFile, exception);
            }
            Log.ErrorFormat("No config file {0} - checking command line", configFile);
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
                Log.ErrorFormat("Exception: {0}", e.Message);
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
                Log.ErrorFormat("Failed to set compression on Archive folder: {0}", e.Message);
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
                Log.ErrorFormat("Failed to set compression on file: {0}", e.Message);
                return false;
            }
        }

        private static void MoveFileToArchive(FileInfo file)
        {
            var day = file.LastWriteTimeUtc.Date;
            var subDir = String.Format(@"Archive\{0:yyyy-MM-dd}", day);
            var fullDir = Path.Combine(file.DirectoryName, subDir);
            var fullPath = Path.Combine(fullDir, file.Name);
            try
            {
                if (!Directory.Exists(fullDir))
                    Directory.CreateDirectory(fullDir);
                File.Move(file.FullName, fullPath);
                Log.InfoFormat("Moved file {0} to {1}", file.Name, fullDir);
            }
            catch (Exception e)
            {
                Error("Error moving file {0} to {2}: '{1}'", file.Name, e.Message, fullDir);
            }
        }

        private static int Error(string format, params object[] args)
        {
            Console.Error.WriteLine(format, args);
            Log.ErrorFormat(format, args);
            return 1;
        }
    }
}
