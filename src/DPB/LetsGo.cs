﻿using DPB.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Text.RegularExpressions;
using Senparc.CO2NET.Trace;
using Senparc.CO2NET.Helpers;
using Newtonsoft.Json;
using Senparc.CO2NET.Extensions;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;

namespace DPB
{
    public class LetsGo
    {
        public Manifest Manifest { get; set; }

        const string BEGIN_MARK_PERFIX = "DPBMARK ";
        const string END_MARK = "DPBMARK_END";
        const string FILE_MARK_PREFIX = "DPBMARK_FILE ";

        public List<string> Records { get; set; } = new List<string>();

        public int AllFilesCount { get; set; }
        private int finishedFilesCount;
        public int FinishedFilesCount
        {
            get => finishedFilesCount;
            set
            {
                finishedFilesCount++;
                FinishPercentAction?.Invoke(Task.Factory.StartNew(() => AllFilesCount == 0 ? 0 : ((finishedFilesCount / AllFilesCount)) * 100));
            }
        }


        /// <summary>
        /// Find nodes from xml by tag name and relpace with specified value
        /// </summary>
        /// <param name="parentNode"></param>
        /// <param name="xmlContent"></param>
        private void ReplaceXmlElements(XElement parentNode, XmlContent xmlContent)
        {
            if (parentNode == null)
            {
                return;
            }

            foreach (var item in parentNode.Elements())
            {
                if (item.Name == xmlContent.TagName)
                {
                    Record($"xml node <{xmlContent.TagName}> changed vale from [{item.Value}] to [{xmlContent.ReplaceContent}]");
                    item.Value = xmlContent.ReplaceContent;
                }
                else
                {
                    ReplaceXmlElements(item, xmlContent);
                }
            }
        }

        /// <summary>
        /// Find nodes from json by key name and relpace with specified value
        /// </summary>
        /// <param name="node"></param>
        /// <param name="jsonContent"></param>
        private void ReplaceJsonNodes(JToken node, JsonContent jsonContent)
        {
            if (node is JProperty)
            {
                var key = ((JProperty)node).Name;
                if (key == jsonContent.KeyName)
                {
                    Record($"json node <{jsonContent.KeyName}> changed vale from [{((JProperty)node).Value}] to [{jsonContent.ReplaceContent}]");
                    ((JProperty)node).Value = jsonContent.ReplaceContent;
                }

            }

            if (node.Children().Count() > 0)
            {
                for (int i = 0; i < node.Children().Count(); i++)
                {
                    ReplaceJsonNodes(node.Children().Skip(i).Take(1).First(), jsonContent);
                }
            }
        }


        private void CheckAndRemoveEmptyDirectory(string file)
        {
            //check empty folder
            var floderPath = Path.GetDirectoryName(file);
            if (Directory.GetFiles(floderPath).Count() == 0)
            {
                Directory.Delete(floderPath);
                Record($"removed empty directory: {floderPath}");
            }
        }

        /// <summary>
        /// Copy all files in the sourceDir to outputDir
        /// </summary>
        /// <param name="sourceDir"></param>
        /// <param name="outputDir"></param>
        public void CopyDirectory(string sourceDir, string outputDir)
        {
            try
            {
                DirectoryInfo dir = new DirectoryInfo(sourceDir);
                FileSystemInfo[] fileinfoArr = dir.GetFileSystemInfos();

                Parallel.ForEach(fileinfoArr, /*new ParallelOptions() { MaxDegreeOfParallelism = 30 },*/ fileInfo =>
                 {
                     try
                     {
                         if (fileInfo is DirectoryInfo)
                         {
                             if (!Directory.Exists(Path.Combine(outputDir, fileInfo.Name)))
                             {
                                 Directory.CreateDirectory(Path.Combine(outputDir, fileInfo.Name));
                             }
                             CopyDirectory(fileInfo.FullName, Path.Combine(outputDir, fileInfo.Name));
                         }
                         else
                         {
                             var newFile = Path.Combine(outputDir, fileInfo.Name);
                             File.Copy(fileInfo.FullName, newFile, true);
                             Record($"file copy from {fileInfo.FullName} to {newFile}");
                         }
                     }
                     catch (Exception)
                     {

                     }


                 });

                //foreach (FileSystemInfo fileInfo in fileinfoArr)
                //{

                //}
            }
            catch (Exception ex)
            {
                Record($"file copy error: {ex}");
            }
        }

        /// <summary>
        /// Record logs
        /// </summary>
        /// <param name="message"></param>
        private void Record(string message)
        {
            Records.Add($"{DateTime.Now.ToString()}\t{message}");
        }

        /// <summary>
        /// LetsGo
        /// </summary>
        /// <param name="manifest">manifest entity</param>
        public LetsGo(Manifest manifest)
        {
            Manifest = manifest;
        }

        /// <summary>
        /// LetsGo
        /// </summary>
        /// <param name="manifestJson">manifest json file</param>
        public LetsGo(string manifestJson)
        {
            Manifest = SerializerHelper.GetObject<Manifest>(manifestJson);
        }

        /// <summary>
        /// How much percent finished
        /// </summary>
        public Action<Task<int>> FinishPercentAction = null;


        /// <summary>
        /// Build a new project from source
        /// </summary>
        /// <param name="cleanOutputDir"></param>
        public void Build(bool cleanOutputDir = true)
        {
            var startTime = DateTime.Now;
            try
            {

                Records.Clear();
                Record($"---- DBP Build begin at {startTime.ToString()}  ----");

                //var fullSourceRoot = Path.Combine(Directory.GetCurrentDirectory(), Manifest.SourceDir);
                //var fullOutputRoot = Path.Combine(Directory.GetCurrentDirectory(), Manifest.OutputDir);

                if (cleanOutputDir)
                {
                    try
                    {
                        Directory.Delete(Manifest.AbsoluteOutputDir, true);
                    }
                    catch (Exception ex)
                    {
                        SenparcTrace.BaseExceptionLog(ex);
                    }
                }

                if (!Directory.Exists(Manifest.AbsoluteOutputDir))
                {
                    Directory.CreateDirectory(Manifest.AbsoluteOutputDir);
                }

                //Copy all files
                Record($"===== start copy all files  =====");
                CopyDirectory(Manifest.AbsoluteSourceDir, Manifest.AbsoluteOutputDir);
                Record($"---- end copy all files  ----");
                Record($"---- {(DateTime.Now - startTime).TotalSeconds} seconds  ----");


                int groupIndex = 0;

                //var filesGroup = new List<List<string>>();
                //foreach (var configGroup in Manifest.ConfigGroup)
                //{
                //    var omitFiles = configGroup.OmitFiles.SelectMany(f => Directory.GetFiles(Manifest.AbsoluteOutputDir, f, SearchOption.AllDirectories)).ToList();
                //    var files = configGroup.Files.SelectMany(f => Directory.GetFiles(Manifest.AbsoluteOutputDir, f, SearchOption.AllDirectories))
                //                    .Where(f => !omitFiles.Contains(f)).ToList();
                //    filesGroup.Add(files);
                //    AllFilesCount += files.Count;
                //}

                AllFilesCount = Manifest.ConfigGroup.Sum(z => z.Files.Count());

                foreach (var configGroup in Manifest.ConfigGroup)
                {
                    groupIndex++;

                    Record($"config group: {groupIndex}");

                    var omitFiles = configGroup.OmitFiles.SelectMany(f => Directory.GetFiles(Manifest.AbsoluteOutputDir, f, SearchOption.AllDirectories)).ToList();
                    var files = configGroup.Files.SelectMany(f => Directory.GetFiles(Manifest.AbsoluteOutputDir, f, SearchOption.AllDirectories))
                                    .Where(f => !omitFiles.Contains(f)).ToList();

                    #region Remove Files

                    if (configGroup.RemoveFiles)
                    {
                        Record($"remove files:");
                        foreach (var file in files)
                        {
                            Record($"try to remove file: {file}");
                            if (File.Exists(file))
                            {
                                File.Delete(file);
                                Record($"removed file: {file}");
                            }
                            else if (Directory.Exists(file))
                            {
                                //it's a directory like .git
                                Directory.Delete(file, true);
                                Record($"removed directory: {file}");
                            }
                            CheckAndRemoveEmptyDirectory(file);
                            FinishedFilesCount++;
                        }
                        continue;
                    }

                    #endregion

                    #region Remove Directories

                    foreach (var dir in configGroup.RemoveDictionaries)
                    {
                        var dirPath = Path.Combine(Manifest.AbsoluteOutputDir, dir);
                        Record($"tobe remove directory: {dirPath}");
                        if (Directory.Exists(dirPath))
                        {
                            Record($"remove directory: {dirPath}");
                            Directory.Delete(dirPath, true);
                        }
                    }

                    #endregion

                    Parallel.ForEach(files, /*new ParallelOptions() { MaxDegreeOfParallelism = 30 },*/ file =>
                        {
                            try
                            {

                                Record($"dynamic file: {file}");

                                var newContent = new StringBuilder();
                                string fileContent = null;
                                using (var fs = new FileStream(file, FileMode.Open))
                                {
                                    using (var sr = new StreamReader(fs, Encoding.UTF8))
                                    {
                                        fileContent = sr.ReadToEnd();
                                    }
                                }

                                #region File Mark

                                if (fileContent.Contains(FILE_MARK_PREFIX))
                                {
                                    //judgement whether this file can keep
                                    var regex = new Regex($@"{FILE_MARK_PREFIX}(?<kw>[^\r\n \*,]*)");
                                    var match = regex.Match(fileContent);
                                    if (match.Success && !configGroup.KeepFileConiditions.Any(z => z == match.Groups["kw"].Value))
                                    {
                                        //remove this file
                                        Record($"remove this file");
                                        try
                                        {
                                            File.Delete(file);
                                            Record($"removed file: {file}");
                                            CheckAndRemoveEmptyDirectory(file);
                                        }
                                        catch (Exception ex)
                                        {
                                            Record($"delete file error:{ex}");
                                        }
                                        return;
                                    }
                                }

                                #endregion

                                #region ReplaceContents

                                XDocument xml = null;
                                dynamic json = null;
                                foreach (var replaceContent in configGroup.ReplaceContents)
                                {
                                    #region String

                                    if (replaceContent.StringContent != null)
                                    {
                                        Record($"Replace String \"{replaceContent.StringContent.String}\" by \"{replaceContent.StringContent.ReplaceContent}\"");
                                        fileContent = fileContent.Replace(replaceContent.StringContent.String, replaceContent.StringContent.ReplaceContent);
                                    }

                                    #endregion

                                    #region Regex

                                    else if (replaceContent.RegexContent != null)
                                    {
                                        Record($"Regex Replace String \"{replaceContent.RegexContent.Pattern}\" by \"{replaceContent.RegexContent.ReplaceContent}\"");
                                        fileContent = Regex.Replace(fileContent, replaceContent.RegexContent.Pattern, replaceContent.RegexContent.ReplaceContent, replaceContent.RegexContent.RegexOptions);
                                    }

                                    #endregion

                                    #region Xml

                                    else if (replaceContent.XmlContent != null)
                                    {
                                        try
                                        {
                                            xml = xml ?? XDocument.Parse(fileContent);
                                            ReplaceXmlElements(xml.Root, replaceContent.XmlContent);
                                        }
                                        catch (Exception ex)
                                        {
                                            Record($"Xml file format wrong");
                                            SenparcTrace.SendCustomLog("Xml file format wrong", ex.Message);
                                        }
                                    }
                                    #endregion

                                    #region Json
                                    else if (replaceContent.JsonContent != null)
                                    {
                                        try
                                        {
                                            //var serializer = new JavaScriptSerializer();
                                            json = fileContent.GetObject<dynamic>(); //serializer.Deserialize(fileContent, typeof(object));
                                            ReplaceJsonNodes(json, replaceContent.JsonContent);
                                        }
                                        catch (Exception ex)
                                        {
                                            Record($"Json file format wrong");
                                            SenparcTrace.SendCustomLog("Json file format wrong", ex.Message);
                                        }
                                    }

                                    #endregion
                                }

                                if (xml != null)
                                {
                                    fileContent = xml.ToString();
                                    Record($"Xml file changed.");
                                }
                                else if (json != null)
                                {
                                    fileContent = JsonConvert.SerializeObject(json, Formatting.Indented);
                                    Record($"Json file changed.");
                                }


                                #endregion

                                #region Custom Functions

                                if (configGroup.CustomFunc != null)
                                {
                                    fileContent = configGroup.CustomFunc(file, fileContent);
                                    Record($"Custom Function");
                                }


                                #region Content Mark

                                if (configGroup.KeepContentConiditions.Count > 0 && fileContent.Contains(BEGIN_MARK_PERFIX))
                                {
                                    var lines = fileContent.Split(new string[] { Environment.NewLine, "\r", "\n" }, StringSplitOptions.None);
                                    var keep = true;
                                    var removeBlockCount = 0;
                                    var i = 0;
                                    foreach (var line in lines)
                                    {
                                        i++;
                                        if (keep)
                                        {
                                            if (line.Contains(BEGIN_MARK_PERFIX))
                                            {
                                                //begin to check Conditions
                                                if (!configGroup.KeepContentConiditions.Any(z => line.Contains(z)))
                                                {
                                                    //drop content
                                                    removeBlockCount++;

                                                    if (line.Contains(END_MARK))
                                                    {
                                                        //just remove this line
                                                        keep = true;
                                                    }
                                                    else
                                                    {
                                                        keep = false;
                                                    }

                                                    continue;
                                                }
                                            }

                                            //keep
                                            newContent.Append(line);
                                            if (i != lines.Count())
                                            {
                                                newContent.Append(Environment.NewLine);   //not last Item
                                            }
                                        }
                                        else
                                        {
                                            //not keep, waiting the end mark
                                            if (line.Contains(END_MARK))
                                            {
                                                keep = true;
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    newContent.Append(fileContent);
                                }

                                Record("File Size:" + newContent.Length);

                                #endregion


                                #endregion

                                #region save new file

                                //save the file to OutputDir
                                using (var fs = new FileStream(file, FileMode.Truncate))
                                {
                                    var sw = new StreamWriter(fs, Encoding.UTF8);
                                    sw.Write(newContent.ToString());
                                    sw.Flush();
                                    fs.Flush(true);
                                    Record($"modified and saved a new file: {file}");
                                }

                                #endregion

                                FinishedFilesCount++;
                            }
                            catch (Exception ex)
                            {
                                Record($@"error[{file}]: {ex.Message}
{ex.StackTrace}");
                            }
                        });
                }


            }
            catch (Exception)
            {

                throw;
            }
            finally
            {
                var manifestFileName = Path.Combine(Manifest.AbsoluteOutputDir, "manifest.config");
                using (var logFs = new FileStream(manifestFileName, FileMode.Create))
                {
                    var sw = new StreamWriter(logFs, Encoding.UTF8);
                    sw.Write(Manifest.ToJson());
                    sw.Flush();
                    logFs.Flush(true);
                }
                Record($"saved manifest file: {manifestFileName}");


                var logFileName = Path.Combine(Manifest.AbsoluteOutputDir, "DPB.log");
                Record($"saved log file: {logFileName}");
                Record($"===== DPB Build Finished =====");
                Record($"---- Total time: {(DateTime.Now - startTime).TotalSeconds} seconds ----");

                using (var logFs = new FileStream(logFileName, FileMode.Create))
                {
                    var sw = new StreamWriter(logFs, Encoding.UTF8);
                    var logs = new StringBuilder();
                    Records.ForEach(z => logs.AppendLine(z));
                    sw.Write(logs.ToString());
                    sw.Flush();
                    logFs.Flush(true);
                }
            }
        }


        ///// <summary>
        ///// Build a new project from source
        ///// </summary>
        ///// <param name="cleanOutputDir"></param>
        //public async Task BuildAsync(bool cleanOutputDir = true)
        //{
        //    var startTime = DateTime.Now;
        //    Records.Clear();
        //    Record($"---- DBP Build begin at {startTime.ToString()}  ----");

        //    //var fullSourceRoot = Path.Combine(Directory.GetCurrentDirectory(), Manifest.SourceDir);
        //    //var fullOutputRoot = Path.Combine(Directory.GetCurrentDirectory(), Manifest.OutputDir);

        //    if (cleanOutputDir)
        //    {
        //        try
        //        {
        //            Directory.Delete(Manifest.AbsoluteOutputDir, true);
        //        }
        //        catch (Exception ex)
        //        {
        //            SenparcTrace.BaseExceptionLog(ex);
        //        }
        //    }

        //    if (!Directory.Exists(Manifest.AbsoluteOutputDir))
        //    {
        //        Directory.CreateDirectory(Manifest.AbsoluteOutputDir);
        //    }

        //    //Copy all files
        //    Record($"===== start copy all files  =====");
        //    CopyDirectory(Manifest.AbsoluteSourceDir, Manifest.AbsoluteOutputDir);
        //    Record($"---- end copy all files  ----");
        //    Record($"---- {(DateTime.Now - startTime).TotalSeconds} seconds  ----");


        //    int groupIndex = 0;

        //    var filesGroup = new List<List<string>>();
        //    foreach (var configGroup in Manifest.ConfigGroup)
        //    {
        //        var omitFiles = configGroup.OmitFiles.SelectMany(f => Directory.GetFiles(Manifest.AbsoluteOutputDir, f, SearchOption.AllDirectories)).ToList();
        //        var files = configGroup.Files.SelectMany(f => Directory.GetFiles(Manifest.AbsoluteOutputDir, f, SearchOption.AllDirectories))
        //                        .Where(f => !omitFiles.Contains(f)).ToList();
        //        filesGroup.Add(files);
        //        AllFilesCount += files.Count;
        //    }

        //    foreach (var configGroup in Manifest.ConfigGroup)
        //    {
        //        groupIndex++;

        //        Record($"config group: {groupIndex}");

        //        var omitFiles = configGroup.OmitFiles.SelectMany(f => Directory.GetFiles(Manifest.AbsoluteOutputDir, f, SearchOption.AllDirectories)).ToList();
        //        var files = filesGroup[groupIndex - 1];

        //        #region Remove Files

        //        if (configGroup.RemoveFiles)
        //        {
        //            Record($"remove files:");
        //            foreach (var file in files)
        //            {
        //                File.Delete(file);
        //                Record($"removed file: {file}");
        //                CheckAndRemoveEmptyDirectory(file);
        //                FinishedFilesCount++;
        //            }
        //            continue;
        //        }

        //        #endregion

        //        #region Remove Directories

        //        foreach (var dir in configGroup.RemoveDictionaries)
        //        {
        //            var dirPath = Path.Combine(Manifest.AbsoluteOutputDir, dir);
        //            Record($"tobe remove directory: {dirPath}");
        //            if (Directory.Exists(dirPath))
        //            {
        //                Record($"remove directory: {dirPath}");
        //                Directory.Delete(dirPath, true);
        //            }
        //        }

        //        #endregion

        //        foreach (var file in files)
        //        {
        //            Record($"dynamic file: {file}");

        //            var newContent = new StringBuilder();
        //            string fileContent = null;
        //            using (var fs = new FileStream(file, FileMode.Open))
        //            {
        //                using (var sr = new StreamReader(fs, Encoding.UTF8))
        //                {
        //                    fileContent = await sr.ReadToEndAsync();
        //                }
        //            }

        //            #region File Mark

        //            if (fileContent.Contains(FILE_MARK_PREFIX))
        //            {
        //                //judgement whether this file can keep
        //                var regex = new Regex($@"{FILE_MARK_PREFIX}(?<kw>[^\r\n \*,]*)");
        //                var match = regex.Match(fileContent);
        //                if (match.Success && !configGroup.KeepFileConiditions.Any(z => z == match.Groups["kw"].Value))
        //                {
        //                    //remove this file
        //                    Record($"remove this file");
        //                    try
        //                    {
        //                        File.Delete(file);
        //                        Record($"removed file: {file}");
        //                        CheckAndRemoveEmptyDirectory(file);
        //                    }
        //                    catch (Exception ex)
        //                    {
        //                        Record($"delete file error:{ex}");
        //                    }
        //                    continue;
        //                }
        //            }

        //            #endregion

        //            #region ReplaceContents

        //            XDocument xml = null;
        //            dynamic json = null;
        //            foreach (var replaceContent in configGroup.ReplaceContents)
        //            {
        //                #region Xml

        //                if (replaceContent.XmlContent != null)
        //                {
        //                    try
        //                    {
        //                        xml = xml ?? XDocument.Parse(fileContent);
        //                        ReplaceXmlElements(xml.Root, replaceContent.XmlContent);
        //                    }
        //                    catch (Exception ex)
        //                    {
        //                        Record($"Xml file format wrong");
        //                        SenparcTrace.SendCustomLog("Xml file format wrong", ex.Message);
        //                    }
        //                }
        //                #endregion

        //                #region Json
        //                else if (replaceContent.JsonContent != null)
        //                {
        //                    try
        //                    {
        //                        //var serializer = new JavaScriptSerializer();
        //                        json = serializer.Deserialize(fileContent, typeof(object));
        //                        ReplaceJsonNodes(json, replaceContent.JsonContent);
        //                    }
        //                    catch (Exception ex)
        //                    {
        //                        Record($"Json file format wrong");
        //                        SenparcTrace.SendCustomLog("Json file format wrong", ex.Message);
        //                    }
        //                }

        //                #endregion
        //            }

        //            if (xml != null)
        //            {
        //                fileContent = xml.ToString();
        //                Record($"Xml file changed.");
        //            }
        //            else if (json != null)
        //            {
        //                fileContent = JsonConvert.SerializeObject(json);
        //                Record($"Json file changed.");
        //            }


        //            #endregion

        //            #region Content Mark

        //            if (fileContent.Contains(BEGIN_MARK_PERFIX))
        //            {
        //                var lines = fileContent.Split(new string[] { Environment.NewLine }, StringSplitOptions.None);
        //                var keep = true;
        //                var removeBlockCount = 0;
        //                var i = 0;
        //                foreach (var line in lines)
        //                {
        //                    i++;
        //                    if (keep)
        //                    {
        //                        if (line.Contains(BEGIN_MARK_PERFIX))
        //                        {
        //                            //begin to check Conditions
        //                            if (!configGroup.KeepContentConiditions.Any(z => line.Contains(z)))
        //                            {
        //                                //drop content
        //                                keep = false;
        //                                removeBlockCount++;
        //                                continue;
        //                            }
        //                        }

        //                        //keep
        //                        newContent.Append(line);
        //                        if (i != lines.Count())
        //                        {
        //                            newContent.Append(Environment.NewLine);   //not last Item
        //                        }
        //                    }
        //                    else
        //                    {
        //                        //not keep, waiting the end mark
        //                        if (line.Contains(END_MARK))
        //                        {
        //                            keep = true;
        //                        }
        //                    }
        //                }
        //            }
        //            else
        //            {
        //                newContent.Append(fileContent);
        //            }

        //            #endregion

        //            #region save new file

        //            //save the file to OutputDir
        //            using (var fs = new FileStream(file, FileMode.Truncate))
        //            {
        //                var sw = new StreamWriter(fs, Encoding.UTF8);
        //                await sw.WriteAsync(newContent.ToString());
        //                await sw.FlushAsync();
        //                fs.Flush(true);
        //                Record($"modified and saved a new file: {file}");
        //            }

        //            #endregion

        //            FinishedFilesCount++;
        //        }
        //    }

        //    var manifestFileName = Path.Combine(Manifest.AbsoluteOutputDir, "manifest.config");
        //    using (var logFs = new FileStream(manifestFileName, FileMode.Create))
        //    {
        //        var sw = new StreamWriter(logFs, Encoding.UTF8);
        //        await sw.WriteAsync(Manifest.ToJson());
        //        await sw.FlushAsync();
        //        logFs.Flush(true);
        //    }
        //    Record($"saved manifest file: {manifestFileName}");


        //    var logFileName = Path.Combine(Manifest.AbsoluteOutputDir, "DPB.log");
        //    Record($"saved log file: {logFileName}");
        //    Record($"===== DPB Build Finished =====");
        //    Record($"---- Total time: {(DateTime.Now - startTime).TotalSeconds} seconds ----");

        //    using (var logFs = new FileStream(logFileName, FileMode.Create))
        //    {
        //        var sw = new StreamWriter(logFs, Encoding.UTF8);
        //        var logs = new StringBuilder();
        //        Records.ForEach(z => logs.AppendLine(z));
        //        await sw.WriteAsync(logs.ToString());
        //        await sw.FlushAsync();
        //        logFs.Flush(true);
        //    }
        //}
    }
}
