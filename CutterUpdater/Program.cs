using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;

namespace CutterUpdater
{
    class Program
    {
        class SemVer
        {
            public int Major { get; set; }
            public int Minor { get; set; }
            public int Patch { get; set; }
            public string Label { get; set; }
            public (int, int, int, string) Tuple { get { return (Major, Minor, Patch, Label); } }

            public SemVer()
            {
            }

            public SemVer(int major, int minor, int patch)
            {
                Major = major;
                Minor = minor;
                Patch = patch;
            }

            public static SemVer Parse(string version)
            {
                if (version.StartsWith("v"))
                    version = version.Substring(1);
                var labelSplit = version.Split('-');
                var versionSplit = labelSplit[0].Split('.');
                return new SemVer
                {
                    Major = int.Parse(versionSplit[0]),
                    Minor = int.Parse(versionSplit[1]),
                    Patch = int.Parse(versionSplit[2]),
                    Label = labelSplit.Length > 1 ? labelSplit[1] : "",
                };
            }

            public static bool operator <(SemVer a, SemVer b)
            {
                // Label is "preX" or "rcX", compared to no label means an earlier
                if (!string.IsNullOrEmpty(a.Label) && string.IsNullOrEmpty(b.Label))
                    return true;
                return a.Tuple.CompareTo(b.Tuple) < 0;
            }

            public static bool operator >(SemVer a, SemVer b)
            {
                throw new NotImplementedException();
            }

            public override string ToString()
            {
                return $"{Major}.{Minor}.{Patch}{(Label.Length > 0 ? "-" + Label : "")}";
            }
        }

        static readonly HttpClientWithProgress client = new HttpClientWithProgress();

        // BASED ON: https://stackoverflow.com/a/56903270/1806760
        public static void ExtractZipFileToDirectory(string sourceZipFilePath, string destinationDirectoryName, bool overwrite)
        {
            using (var archive = ZipFile.Open(sourceZipFilePath, ZipArchiveMode.Read))
            {
                if (!overwrite)
                {
                    archive.ExtractToDirectory(destinationDirectoryName);
                    return;
                }

                DirectoryInfo di = Directory.CreateDirectory(destinationDirectoryName);
                string destinationDirectoryFullPath = di.FullName;

                foreach (ZipArchiveEntry file in archive.Entries)
                {
                    string completeFileName = Path.GetFullPath(Path.Combine(destinationDirectoryFullPath, file.FullName));

                    if (!completeFileName.StartsWith(destinationDirectoryFullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new IOException("Trying to extract file outside of destination directory. See this link for more info: https://snyk.io/research/zip-slip-vulnerability");
                    }

                    // ASSUMING EMPTY FOR DIRECTORY
                    if (file.Name == "")
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(completeFileName));
                        continue;
                    }

                    // CREATE ANY MISSING DIRECTORIES
                    var dir = Path.GetDirectoryName(completeFileName);
                    if (!Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    file.ExtractToFile(completeFileName, true);
                    var utcDateTime = file.LastWriteTime.UtcDateTime;
                    File.SetCreationTimeUtc(completeFileName, utcDateTime);
                    File.SetLastAccessTimeUtc(completeFileName, utcDateTime);
                    File.SetLastWriteTimeUtc(completeFileName, utcDateTime);
                }
            }
        }

        static int Main(string[] args)
        {
            Console.Title = "CutterUpdater";
            var processes = Process.GetProcessesByName("cutter");
            
            if (processes.Length > 0)
            {
                Console.WriteLine("Running instance(s) of cutter:");
                foreach (var process in processes)
                    Console.WriteLine($"{process.Id}: {process.ProcessName}");
                Console.WriteLine();
                Console.WriteLine("Close cutter before updating...");
                Console.ReadKey();
                return 1;
            }
            
            var versionInfo = FileVersionInfo.GetVersionInfo("cutter.exe");
            var fileVersion = SemVer.Parse(versionInfo.FileVersion);
            var fileDate    = new FileInfo("cutter.exe").CreationTimeUtc.Date;
            Console.WriteLine($"Detected Cutter v{fileVersion} ({fileDate:yyyy-MM-dd})");
          
            client.DefaultRequestHeaders.Add("User-Agent", "CutterUpdater");
            
            var json          = client.GetStringAsync("https://api.github.com/repos/radareorg/cutter/releases/latest").Result;
            var release       = Newtonsoft.Json.JsonConvert.DeserializeObject<Release>(json);
            var latestVersion = SemVer.Parse(release.TagName);
            var latestDate    = release.CreatedAt.Date;
            
            Console.WriteLine($"Latest release v{latestVersion} ({latestDate:yyyy-MM-dd})");
            var shouldUpdate = fileVersion < latestVersion;
            if (fileVersion.ToString() == $"{latestVersion.Major}.{latestVersion.Minor}.{latestVersion.Patch}" && (latestDate - fileDate).TotalDays > 2)
            {
                Console.WriteLine("Detected probable prerelease on disk, suggesting update");
                shouldUpdate = true;
            }
            if (shouldUpdate)
            {
                Console.Write("New version detected, do you want to update? [Y/n] ");
                var response = Console.ReadKey();
                Console.WriteLine();
                if (response.KeyChar == 'Y' || response.KeyChar == 'y' || response.Key == ConsoleKey.Enter)
                {
                    var asset = release.Assets.Where(a => a.Name.Contains("Windows")).FirstOrDefault();
                    Console.WriteLine($"Downloading {asset.BrowserDownloadUrl}");
                    
                    var destinationFile     = Path.GetFileName(asset.BrowserDownloadUrl.LocalPath);
                    
                    client.ProgressChanged += Client_ProgressChanged;
                    client.StartDownload(asset.BrowserDownloadUrl.ToString(), destinationFile).Wait();
                    
                    Console.Title = "CutterUpdater";
                    
                    ExtractZipFileToDirectory(destinationFile, ".", true);
                    File.Delete(destinationFile);
                    
                    Console.WriteLine($"Cutter updated to v{latestVersion}!");
                }
            }
            else
            {
                Console.WriteLine("No update required!");
            }
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
            
            return 0;
        }

        private static void Client_ProgressChanged(long? totalFileSize, long totalBytesDownloaded, double? progressPercentage)
        {
            Console.Title = $"CutterUpdater - {progressPercentage}%";
        }
    }
}
