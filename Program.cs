using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace sdeploy
{
    public class ReplaceRule
    {
        public string Find { get; set; }
        public string ReplaceWith { get; set; }
    }

    public class CopyRule
    {
        public string Source { get; set; }
        public string Destination { get; set; }
    }

    public class Config
    {
        public string ReplaceFilter = "*";
        public List<ReplaceRule> Replace = new List<ReplaceRule>();
        public List<CopyRule> Copy = new List<CopyRule>();
        public string BackupFolder = "";
    }
    class Program
    {
        public static void CopyFilesRecursively(DirectoryInfo source, DirectoryInfo target)
        {
            foreach (DirectoryInfo dir in source.GetDirectories())
                CopyFilesRecursively(dir, target.CreateSubdirectory(dir.Name));
            foreach (FileInfo file in source.GetFiles())
                file.CopyTo(Path.Combine(target.FullName, file.Name));
        }

        void CreateSample()
        {
            var config = new Config()
            {
                ReplaceFilter = "txt,json,xaml",
                BackupFolder = "d:/backup"
            };

            config.Copy.Add(new CopyRule()
            {
                Source = "[source folder, absolute or relative to Config file]",
                Destination = "[where to copy to]"
            });

            config.Replace.Add(new ReplaceRule()
            {
                Find = "[Text to find]",
                ReplaceWith = "[Replacement text]"
            });



            string output = JsonConvert.SerializeObject(config, Formatting.Indented);
            File.WriteAllText("sample.json", output);
            Console.WriteLine("Usage: sdeploy [config file]");
            Console.WriteLine("Sample config:" + Path.GetFullPath("sample.json"));
        }


        string Home = "";

        string ToAbsolutePath(string filePath)
        {
            if (!Path.IsPathRooted(filePath))
            {
                filePath = Path.Combine(Home, filePath);
            }
            return Path.GetFullPath(filePath);
        }


        DirectoryInfo GetDir(string dirPath, bool autoCreate = false)
        {
            dirPath = ToAbsolutePath(dirPath);
            if (!Directory.Exists(dirPath))
            {
                if (autoCreate) Directory.CreateDirectory(dirPath);
                else throw new Exception("can't find " + dirPath);
            }
            return new DirectoryInfo(dirPath);
        }

        void EmptyDir(string dirPath)
        {
            DirectoryInfo di = new DirectoryInfo(dirPath);
            foreach (FileInfo file in di.GetFiles())
            {
                file.Delete();
            }
            foreach (DirectoryInfo dir in di.GetDirectories())
            {
                dir.Delete(true);
            }
        }

        void Deploy(string configPath)
        {
            try
            {
                var watch = new System.Diagnostics.Stopwatch();
                watch.Start();

                Console.WriteLine("Deploying " + configPath);
                if (!File.Exists(configPath)) throw new Exception("Can't find " + configPath);
                var config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(configPath).Replace(@"\", "/"));
                Home = new FileInfo(configPath).Directory.FullName;
                var tempDir = Path.Combine(Home, "temp");
                config.Copy.ForEach(rule =>
                {
                    Console.WriteLine(String.Format("{0} =>> {1}", rule.Source, rule.Destination));
                    //create a temp 
                    if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
                    Directory.CreateDirectory(tempDir);
                    //grab from source 
                    var sourcePath = ToAbsolutePath(rule.Source);
                    if (Directory.Exists(sourcePath))
                    {
                        CopyFilesRecursively(GetDir(sourcePath), new DirectoryInfo(tempDir));
                    }
                    else if (File.Exists(sourcePath) && Path.GetExtension(sourcePath) == ".zip")
                    {
                        Console.WriteLine("  Unzip " + sourcePath);
                        ZipFile.ExtractToDirectory(sourcePath, tempDir);
                        string[] filePaths = Directory.GetDirectories(tempDir);
                        if (Directory.GetFiles(tempDir).Length == 0
                            && Directory.GetDirectories(tempDir).Length == 1)
                        { 
                            var folder = Directory.GetDirectories(tempDir)[0];
                            var zipTemp = Path.Combine(Home, "temp_zip");
                            Directory.Move(folder, zipTemp);
                            Directory.Delete(tempDir, true);
                            Directory.Move(zipTemp, tempDir);
                        }
                    }
                    else
                    {
                        throw new Exception(String.Format("can't find {0} ({1})",
                            rule.Source, GetDir(rule.Source)));
                    }
                    //replacing
                    var fileExtensions = config.ReplaceFilter.Split(',').Select(ext => "." + ext);

                    Console.WriteLine(String.Format("  Patching {0}", config.ReplaceFilter));

                    var allFiles = Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories);

                    config.Replace.ForEach(replaceRule =>
                    {
                        Console.WriteLine(String.Format("    {0} -> {1}", replaceRule.Find, replaceRule.ReplaceWith));

                        foreach (var file in allFiles)
                        {
                            if (config.ReplaceFilter=="*" || fileExtensions.Contains(Path.GetExtension(file)))
                            {
                                string text = File.ReadAllText(file);
                                text = text.Replace(replaceRule.Find, replaceRule.ReplaceWith);
                                File.WriteAllText(file, text);
                            }
                        }
                    });
                    //
                    var destPath = ToAbsolutePath(rule.Destination);
                    if (Directory.Exists(destPath))
                    {
                        if (Directory.Exists(config.BackupFolder))
                        {
                            var zipPath = Path.GetFileName(destPath) + "_" + DateTime.Now.ToString("yyyy_MM_dd_HHmmss") + ".zip";
                            zipPath = Path.Combine(config.BackupFolder, zipPath);
                            ZipFile.CreateFromDirectory(destPath, zipPath);
                            Console.WriteLine("  Backup saved as " + Path.GetFullPath(zipPath));
                        }
                        EmptyDir(destPath);
                    }
                    //copy to Destination  
                    Console.WriteLine(String.Format("  Coping {0} files to {1} ", allFiles.Length, destPath));
                    CopyFilesRecursively(new DirectoryInfo(tempDir), GetDir(rule.Destination, true));
                    Directory.Delete(tempDir, true);
                });
                Console.WriteLine($"Deployed {configPath} in { Math.Round(1 + watch.Elapsed.TotalSeconds)}s");
            }
            catch (Exception e)
            {
                Console.WriteLine("failed:" + e.Message);
            } 
        } 
        static void Main(string[] args)
        {
            var Program = new Program();
            if (args.Length == 0) Program.CreateSample();
            if (args.Length == 1) Program.Deploy(args[0]);
        }
    }
}
