using EOSNewYork.EOSCore;
using Newtonsoft.Json;
using NLog;
using PowerArgs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace EOSAccountAnalyzer
{
    class Program
    {
        static void Main(string[] args)
        {
            Logger logger = NLog.LogManager.GetCurrentClassLogger();
            Args.InvokeAction<GetProgram>(args);
        }
    }


    [ArgExceptionBehavior(ArgExceptionPolicy.StandardExceptionHandling)]
    public class GetProgram
    {
        static Logger logger = NLog.LogManager.GetCurrentClassLogger();

        [HelpHook, ArgShortcut("-?"), ArgDescription("Shows this help")]
        public bool Help { get; set; }

        [ArgActionMethod, ArgDescription("Retrieve data from one of the well known EOS tables")]
        public void expand([ArgRequired]expandArguments arg)
        {

            logger.Info("Expanding contacts contained in file \"{0}\".", arg.filePath);
            if (File.Exists(arg.filePath))
            {
                FileInfo accountsFileInfo = new FileInfo(arg.filePath);
                EOSInfoCollector collector = new EOSInfoCollector(accountsFileInfo);
                var fetchfiles = collector.startAsync(arg.outputPath, arg.overwrite, arg.apihost).Result;
                if (collector.validateFileCount(arg.outputPath))
                {
                    logger.Info("Calculate the balance for each account");
                } else
                {
                    logger.Error("The number of files in the staging are do not match the number of accounts provided", arg.filePath);
                }
            }
            else
            {
                logger.Error("File \"{0}\" does not exist or is inaccessible", arg.filePath);
            }
        }
    }

    public class expandArguments
    {
        [ArgDefaultValue("contacts.txt")]
        [ArgDescription("The path to the file containing the flat list of contacts you'd like to expand"), ArgPosition(4)]
        public String filePath { get; set; }

        [ArgDefaultValue("output")]
        [ArgDescription("The path to the directory you'd like to output to"), ArgPosition(4)]
        public String outputPath { get; set; }

        [ArgDefaultValue(false)]
        [ArgDescription("If the output directory path exists, delete and create start with an empty directory."), ArgPosition(4)]
        public bool overwrite { get; set; }

        [ArgDefaultValue("http://pennstation.eosdocs.io:7001")]
        [ArgDescription("The url of the EOS API"), ArgPosition(4)]
        public Uri apihost { get; set; }

    }


    public class EOSInfoCollector
    {
        Logger logger = NLog.LogManager.GetCurrentClassLogger();
        List<string> contactList = new List<string>();

        public EOSInfoCollector(FileInfo file)
        {
            logger.Info("Loding all contacts contained in \"{0}\" into memory.", file.FullName);
            var logFile = File.ReadAllLines(file.FullName);
            contactList = new List<string>(logFile);
            logger.Info("{0} contacts loaded.", contactList.Count);

        }

        public async Task<bool> startAsync(String outputDirectory, bool overwrite, Uri apihost)
        {


            if (Directory.Exists(outputDirectory))
            {
                if (overwrite)
                {
                    logger.Warn("overwrite = true, Deleting existing output directory {0}", outputDirectory);
                    Directory.Delete(outputDirectory, true);                    
                } else
                {
                    //throw new Exception("Directory exists, but overwrite option set to fale.");
                    logger.Error("Directory exists, but overwrite option set to fale.");
                    Environment.Exit(-1);
                }
            }

            logger.Info("Creating output directory {0}", outputDirectory);
            Directory.CreateDirectory(outputDirectory);

            Task[] requests = contactList.Select(l => new EOS_Object<EOSAccount_row>(apihost).getAllObjectRecordsAsync(new EOSAccount_row.postData() { account_name = l }))
                        .Select(r => HandleResponse(r))
                        .ToArray();

            await Task.WhenAll(requests);
            return true;
        }

        private async Task HandleResponse(Task<EOSAccount_row> accountTask)
        {
            var account = accountTask.Result;
            string json = JsonConvert.SerializeObject(account);
            await File.WriteAllTextAsync(Path.Combine("output", account.account_name + ".txt"), json);
        }

        public bool validateFileCount(String outputDirectory)
        {
            bool match = false;
            var fileCount = Directory.GetFiles(outputDirectory).Length;
            if (fileCount == contactList.Count)
                match = true;

            return match;
        }

    }
}
