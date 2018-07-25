using Amazon;
using Amazon.S3;
using Amazon.S3.Transfer;
using EOSNewYork.EOSCore;
using Newtonsoft.Json;
using NLog;
using PowerArgs;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Amazon.Runtime.CredentialManagement;
using Amazon.Runtime;
using System.Security.Cryptography;
using System.Threading;
using System.Diagnostics;

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
        public void Expand([ArgRequired]ExpandArguments arg)
        {

            logger.Info("Expanding contacts contained in file \"{0}\".", arg.FilePath);
            if (File.Exists(arg.FilePath))
            {
                FileInfo accountsFileInfo = new FileInfo(arg.FilePath);
                EOSInfoCollector collector = new EOSInfoCollector(accountsFileInfo, arg.OutputPath);
                if(arg.SkipDownload)
                {
                    logger.Warn("Skipping the download of data and will instead process the json files that exist in {0}", arg.OutputPath);
                }
                else
                {
                    var fetchfiles = collector.StartAsync(arg.Overwrite, arg.Apihost).Result;
                }               
                Console.WriteLine();
                if (collector.ValidateFileCount())
                {
                    logger.Info("Calculting the balance for each account");
                    collector.CalcTotalBalance();
                    collector.GenerateMD5();
                    collector.CompressOutput(arg.Zipoutput);
                    collector.UploadToS3(arg.Zipoutput,arg.S3bucket, arg.S3profile);
                    logger.Info("Done");

                } else
                {
                    logger.Error("The number of files in the staging are do not match the number of accounts provided", arg.FilePath);
                }
            }
            else
            {
                logger.Error("File \"{0}\" does not exist or is inaccessible", arg.FilePath);
                Environment.Exit(-1);
            }
        }
    }

    public class ExpandArguments
    {
        //[ArgDefaultValue("contacts.txt")]
        [ArgDescription("The path to the file containing the flat list of contacts you'd like to expand"), ArgPosition(1)]
        public String FilePath { get; set; }

        [ArgDefaultValue("output")]
        [ArgDescription("The path to the directory you'd like to output to"), ArgPosition(2)]
        public String OutputPath { get; set; }

        [ArgDefaultValue(false)]
        [ArgDescription("If the output directory path exists, delete and create start with an empty directory."), ArgPosition(3)]
        public bool Overwrite { get; set; }

        [ArgDefaultValue("http://pennstation.eosdocs.io:7001")]
        [ArgDescription("The url of the EOS API"), ArgPosition(4)]
        public Uri Apihost { get; set; }

        [ArgDefaultValue(false)]
        [ArgDescription("Process the files in the existing output directory. Do not re-download the data."), ArgPosition(5)]
        public bool SkipDownload { get; set; }

        [ArgDefaultValue("summary.zip")]
        [ArgDescription("The path to the ZIP file that will contain the raw data and summary file."), ArgPosition(6)]
        public String Zipoutput { get; set; }

        [ArgDefaultValue("eossnapshots-staticsitebucket-zp2kemxur4pw")]
        [ArgDescription("The name of the S3 bucket that the compressed file will be uploaded to."), ArgPosition(7)]
        public String S3bucket { get; set; }

        [ArgDefaultValue("publicwebsitefileupload")]
        [ArgDescription("The name of the S3 profile which contains the required credentials for upload."), ArgPosition(8)]
        public String S3profile { get; set; }

    }


    public class EOSInfoCollector
    {
        Logger logger = NLog.LogManager.GetCurrentClassLogger();
        List<string> contactList = new List<string>();
        String OutputDirectory { get; set; }
        String DownloadDirectory { get; set; }
        String BalanceFile { get; set; }
        int DownloadCounter = 0;
        Stopwatch StopWatch { get; set; } = new Stopwatch();

        public EOSInfoCollector(FileInfo file, String outputPath)
        {
            OutputDirectory = outputPath;
            DownloadDirectory = Path.Combine(outputPath, "rawdata");

            logger.Info("Loding all contacts contained in \"{0}\" into memory.", file.FullName);
            var logFile = File.ReadAllLines(file.FullName);
            contactList = new List<string>(logFile);
            logger.Info("{0} contacts loaded.", contactList.Count);

        }

        public async Task<bool> StartAsync(bool overwrite, Uri apihost)
        {
            if (Directory.Exists(OutputDirectory))
            {
                if (overwrite)
                {
                    logger.Warn("overwrite = true, Deleting existing output directory {0}", OutputDirectory);
                    Directory.Delete(OutputDirectory, true);                    
                } else
                {
                    //throw new Exception("Directory exists, but overwrite option set to fale.");
                    logger.Error("Directory exists, but overwrite option set to fale.");
                    Environment.Exit(-1);
                }
            }

            logger.Info("Creating output directory \"{0}\"", OutputDirectory);
            Directory.CreateDirectory(OutputDirectory);
            Directory.CreateDirectory(DownloadDirectory);

            logger.Info("Starting Async download of raw contact information", OutputDirectory);
            StopWatch.Start();
            Task[] requests = contactList.Select(accountName => new EOS_Object<EOSAccount_row>(apihost).getAllObjectRecordsAsync(new EOSAccount_row.postData() { account_name = accountName }))
                        .Select(r => HandleResponse(r,  contactList.Count))
                        .ToArray();
            await Task.WhenAll(requests);
            return true;
        }

        private async Task HandleResponse(Task<EOSAccount_row> accountTask,int count)
        {
            Interlocked.Increment(ref DownloadCounter);

            var eta = (StopWatch.Elapsed.TotalSeconds / DownloadCounter) * (contactList.Count - DownloadCounter);
            int etaSeconds = Convert.ToInt32(eta);
            TimeSpan remaining = new TimeSpan(0, 0, etaSeconds);

            Console.SetCursorPosition(0, Console.CursorTop);
            Console.Write(string.Format("{0}/{1} ({2}%) - ELAPSED: {3} - REMAINING(ETA) {4}",DownloadCounter, contactList.Count, (DownloadCounter/ contactList.Count)*100, StopWatch.Elapsed, remaining));

            var account = accountTask.Result;
            string json = JsonConvert.SerializeObject(account, Formatting.Indented);
            await File.WriteAllTextAsync(Path.Combine(DownloadDirectory, account.account_name + ".txt"), json);
        }

        public bool ValidateFileCount()
        {
            bool match = false;
            var fileCount = Directory.GetFiles(DownloadDirectory).Length;
            if (fileCount == contactList.Count)
                match = true;

            return match;
        }

        public void CalcTotalBalance()
        {
            BalanceFile = Path.Combine(OutputDirectory, "balances.csv");

            using (StreamWriter sw = File.CreateText(BalanceFile))
            {
                foreach (var file in Directory.GetFiles(DownloadDirectory))
                {
                    logger.Info("Process:  {0}", file);
                    var account = JsonConvert.DeserializeObject<EOSAccount_row>(File.ReadAllText(file));
                    var account_name = account.account_name;
                    string cpu_weight = "0.0000 EOS";
                    string net_weight = "0.0000 EOS"; 
                    if(account.self_delegated_bandwidth != null)
                    {
                        cpu_weight = account.self_delegated_bandwidth.cpu_weight;
                        net_weight = account.self_delegated_bandwidth.net_weight;
                    }
                    var core_liquid_balance = account.core_liquid_balance_decimal;
                    sw.WriteLine(string.Format("{0},{1},{2},{3}", account_name, cpu_weight, net_weight, core_liquid_balance));
                }
            }
        }

        public void GenerateMD5()
        {
            logger.Info("Calculate MD5 of {0}", BalanceFile);
            string md5HashString = string.Empty;
            using (var md5 = MD5.Create())
            {
                using (var stream = File.OpenRead(BalanceFile))
                {
                    var hash = md5.ComputeHash(stream);
                    md5HashString = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }
            logger.Info("MD5Hash = {0}", md5HashString);
            File.WriteAllText(Path.Combine(OutputDirectory, "MD5Hash.txt"), md5HashString);
        }

        public void CompressOutput(string zipOutputPath)
        {
            logger.Info("Compress contents of {0} -> {1}", OutputDirectory, zipOutputPath);
            if (File.Exists(zipOutputPath))
                File.Delete(zipOutputPath);
            ZipFile.CreateFromDirectory(OutputDirectory, zipOutputPath);
        }

        public void UploadToS3(String zipOutputPath, string s3Bucket, string s3Profile)
        {
            var dt = DateTime.Now;
            var bucket = s3Bucket + "/data/"+dt.ToString("yyyy-MM");
            var key = dt.ToString("yyyy-MM-dd") + ".zip";

            logger.Info("Uploading {0} to s3://{1}/{2}",zipOutputPath, bucket, key);
            var chain = new CredentialProfileStoreChain();
            if (chain.TryGetAWSCredentials(s3Profile, out AWSCredentials awsCredentials))
            {
                AmazonS3Client _s3Client = new AmazonS3Client(awsCredentials, RegionEndpoint.USEast1);
                TransferUtility fileTransferUtility = new TransferUtility(_s3Client);
                fileTransferUtility.Upload(zipOutputPath, bucket, key );
            }

        }


    }
}
