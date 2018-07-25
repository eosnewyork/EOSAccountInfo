using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using EOSNewYork.EOSCore;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using NLog;
using PowerArgs;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Amazon.Extensions.NETCore.Setup;
using Amazon.Runtime.CredentialManagement;
using Amazon.Runtime;

namespace EOSAccountAnalyzer
{
    class Program
    {
        static void Main(string[] args)
        {
            Logger logger = NLog.LogManager.GetCurrentClassLogger();
            Args.InvokeAction<GetProgram>(args);
            Console.ReadLine();
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
                if(arg.skipDownload)
                {
                    logger.Warn("Skipping the download of data and will instead process the json files that exist in {0}", arg.outputPath);
                }
                else
                {
                    var fetchfiles = collector.startAsync(arg.outputPath, arg.overwrite, arg.apihost).Result;
                }               
                Console.WriteLine();
                if (collector.validateFileCount(arg.outputPath))
                {
                    logger.Info("Calculate the balance for each account");
                    collector.calcTotalBalance(arg.outputPath);
                    collector.compressOutput(arg.outputPath,arg.zipoutput);
                    collector.uploadToS3(arg.zipoutput,arg.s3bucket, arg.s3profile);
                    logger.Info("Done");

                } else
                {
                    logger.Error("The number of files in the staging are do not match the number of accounts provided", arg.filePath);
                }
            }
            else
            {
                logger.Error("File \"{0}\" does not exist or is inaccessible", arg.filePath);
                Environment.Exit(-1);
            }
        }
    }

    public class expandArguments
    {
        [ArgDefaultValue("contacts.txt")]
        [ArgDescription("The path to the file containing the flat list of contacts you'd like to expand"), ArgPosition(1)]
        public String filePath { get; set; }

        [ArgDefaultValue("output")]
        [ArgDescription("The path to the directory you'd like to output to"), ArgPosition(2)]
        public String outputPath { get; set; }

        [ArgDefaultValue(false)]
        [ArgDescription("If the output directory path exists, delete and create start with an empty directory."), ArgPosition(3)]
        public bool overwrite { get; set; }

        [ArgDefaultValue("http://pennstation.eosdocs.io:7001")]
        [ArgDescription("The url of the EOS API"), ArgPosition(4)]
        public Uri apihost { get; set; }

        [ArgDefaultValue(false)]
        [ArgDescription("Process the files in the existing output directory. Do not re-download the data."), ArgPosition(5)]
        public bool skipDownload { get; set; }

        [ArgDefaultValue("summary.zip")]
        [ArgDescription("The path to the ZIP file that will contain the raw data and summary file."), ArgPosition(6)]
        public String zipoutput { get; set; }

        [ArgDefaultValue("eossnapshots-staticsitebucket-zp2kemxur4pw")]
        [ArgDescription("The name of the S3 bucket that the compressed file will be uploaded to."), ArgPosition(7)]
        public String s3bucket { get; set; }

        [ArgDefaultValue("publicwebsitefileupload")]
        [ArgDescription("The name of the S3 profile which contains the required credentials for upload."), ArgPosition(8)]
        public String s3profile { get; set; }


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
                        .Select(r => HandleResponse(r, outputDirectory))
                        .ToArray();

            await Task.WhenAll(requests);
            return true;
        }

        private async Task HandleResponse(Task<EOSAccount_row> accountTask, string outputdir)
        {
            Console.Write('.');
            var account = accountTask.Result;
            string json = JsonConvert.SerializeObject(account, Formatting.Indented);
            await File.WriteAllTextAsync(Path.Combine(outputdir, account.account_name + ".txt"), json);
        }

        public bool validateFileCount(String outputDirectory)
        {
            bool match = false;
            var fileCount = Directory.GetFiles(outputDirectory).Length;
            if (fileCount == contactList.Count)
                match = true;

            return match;
        }

        public void calcTotalBalance(String outputDirectory)
        {
            var summaryDirectory = Path.Combine(outputDirectory, "summary");
            if (Directory.Exists(summaryDirectory))
                Directory.Delete(summaryDirectory, true);

            logger.Info("Creating summary directory {0}", summaryDirectory);
            Directory.CreateDirectory(summaryDirectory);
            var balanceFile = Path.Combine(summaryDirectory, "balances.csv");

            using (StreamWriter sw = File.CreateText(balanceFile))
            {
                foreach (var file in Directory.GetFiles(outputDirectory))
                {
                    var account = JsonConvert.DeserializeObject<EOSAccount_row>(File.ReadAllText(file));
                    sw.WriteLine(string.Format("{0},{1},{2},{3}",account.account_name, account.self_delegated_bandwidth.cpu_weight, account.self_delegated_bandwidth.net_weight,account.core_liquid_balance_decimal));
                }
            }
        }


        public void compressOutput(String outputDirectory, string zipOutputPath)
        {
            logger.Info("Compress contents of {0} -> {1}", outputDirectory, zipOutputPath);
            if (File.Exists(zipOutputPath))
                File.Delete(zipOutputPath);
            ZipFile.CreateFromDirectory(outputDirectory, zipOutputPath);
        }

        public void uploadToS3(String zipOutputPath, string s3Bucket, string s3Profile)
        {
            var dt = DateTime.Now;
            var bucket = s3Bucket + "/data/"+dt.ToString("yyyy-MM");
            var key = dt.ToString("yyyy-MM-dd") + ".zip";

            logger.Info("Upload {0} to S3 as {1}/{2}",zipOutputPath, bucket, key);
            var chain = new CredentialProfileStoreChain();
            AWSCredentials awsCredentials;
            if (chain.TryGetAWSCredentials(s3Profile, out awsCredentials))
            {
                AmazonS3Client _s3Client = new AmazonS3Client(awsCredentials, RegionEndpoint.USEast1);
                TransferUtility fileTransferUtility = new TransferUtility(_s3Client);
                fileTransferUtility.Upload(zipOutputPath, bucket, key);
            }

        }


    }
}
