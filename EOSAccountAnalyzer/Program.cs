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
using Amazon.Util;

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
                EOSInfoCollector collector = new EOSInfoCollector(accountsFileInfo, arg.OutputPath, arg.Resume);
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
                    collector.CalcTotalBalance(arg.ExcludeAccountsCreatedAfterMidnightUTC);
                    collector.GenerateMD5();
                    collector.CompressOutput(arg.Zipoutput);
                    if(arg.UploadtoS3)
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

        //[ArgDefaultValue("output")]
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

        [ArgDefaultValue(false)]
        [ArgDescription("Disable the upload to S3."), ArgPosition(9)]
        public bool UploadtoS3 { get; set; }

        [ArgDefaultValue(false)]
        [ArgDescription("Resume downloads from where you left off (if you have SkipDownload = false)."), ArgPosition(10)]
        public bool Resume { get; set; }

        [ArgDefaultValue(true)]
        [ArgDescription("Exclude accounts that were created after midnight UTC. All accounts will be processed but final output will exclude those created after midnight UTC"), ArgPosition(10)]
        public bool ExcludeAccountsCreatedAfterMidnightUTC { get; set; }

    }


    public class EOSInfoCollector
    {
        Logger logger = NLog.LogManager.GetCurrentClassLogger();
        List<string> contactList = new List<string>();
        List<string> resumeContactList = new List<string>();
        String OutputDirectory { get; set; }
        String DownloadDirectory { get; set; }
        String BalanceFile { get; set; }
        int DownloadCounter = 0;
        Stopwatch StopWatch { get; set; } = new Stopwatch();
        bool Resume { get => resume; set => resume = value; }

        bool resume = false;

        public EOSInfoCollector(FileInfo file, String outputPath, bool resume)
        {
            OutputDirectory = outputPath;
            DownloadDirectory = Path.Combine(outputPath, "rawdata");
            Resume = resume;

            logger.Info("Loding all contacts contained in \"{0}\" into memory.", file.FullName);
            var logFile = File.ReadAllLines(file.FullName);
            contactList = new List<string>(logFile);
            logger.Info("{0} contacts loaded.", contactList.Count);

            if (resume)
            {
                logger.Info("Resume = true. Filter download list to contacts not already downloaded.");
                int counter = 0;
                foreach (var contact in contactList)
                {
                    counter++;
                    var filePath = Path.Combine(DownloadDirectory, contact + ".txt");
                    if (!File.Exists(filePath))
                    {
                        //Console.SetCursorPosition(0, Console.CursorTop);
                        //Console.Write(string.Format("Add {0} to resume list", contact));
                        logger.Info("Add {0} to resume list", contact);
                        resumeContactList.Add(contact);
                    } else
                    {
                        Console.SetCursorPosition(0, Console.CursorTop);
                        Console.Write(string.Format("{0} existing record(s) found", counter));
                    }
                }
                Console.WriteLine();
                logger.Info("Resume download of {0} contacts", resumeContactList.Count);
            } 

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
                    if (resume)
                    {
                        logger.Info("Leave directory in tact becasue resume = true.");
                    }
                    else
                    {
                        //throw new Exception("Directory exists, but overwrite option set to fale.");
                        logger.Error("Directory exists, but overwrite option set to fale.");
                        Environment.Exit(-1);
                    }
                }
            }

            if(!Directory.Exists(OutputDirectory))
            {
                logger.Info("Creating output directory \"{0}\"", OutputDirectory);
                Directory.CreateDirectory(OutputDirectory);
            }
           

            if(!Directory.Exists(DownloadDirectory))
            {
                logger.Info("Creating downloads directory \"{0}\"", DownloadDirectory);
                Directory.CreateDirectory(DownloadDirectory);
            }


            logger.Info("Starting Async download of raw contact information", OutputDirectory);
            StopWatch.Start();
            if(Resume)
            {
                Task[] requests = resumeContactList.Select(accountName => new EOS_Object<EOSAccount_row>(apihost).getAllObjectRecordsAsync(new EOSAccount_row.postData() { account_name = accountName }))
                        .Select(r => HandleResponse(r, contactList.Count))
                        .ToArray();
                await Task.WhenAll(requests);
            }
            else
            {
                Task[] requests = contactList.Select(accountName => new EOS_Object<EOSAccount_row>(apihost).getAllObjectRecordsAsync(new EOSAccount_row.postData() { account_name = accountName }))
                        .Select(r => HandleResponse(r, contactList.Count))
                        .ToArray();
                await Task.WhenAll(requests);
            }
            

            return true;
        }

        private async Task HandleResponse(Task<EOSAccount_row> accountTask,int count)
        {
            try
            {
                Interlocked.Increment(ref DownloadCounter);

                var countactDownloadCount = 0;
                if (resume)
                    countactDownloadCount = resumeContactList.Count;
                else
                    countactDownloadCount = contactList.Count;

                var percentage = ((float)DownloadCounter / (float)countactDownloadCount) * 100.00;


                Console.SetCursorPosition(0, Console.CursorTop);
                Console.Write(string.Format("{0:n0}/{1:n0} ({2:n0}%) - ELAPSED: {3} ", DownloadCounter, countactDownloadCount, percentage,StopWatch.Elapsed));

                var account = accountTask.Result;
                string json = JsonConvert.SerializeObject(account, Formatting.Indented);

                var fileOutputPath = Path.Combine(DownloadDirectory, account.account_name + ".txt");
                logger.Debug("Write: {0}", fileOutputPath);
                await File.WriteAllTextAsync(fileOutputPath, json);
            }
            catch (Exception ex)
            {
                logger.Error("Error while fetching account \"{0}\"",accountTask.Result.account_name);
                logger.Error(ex.Message);
                Environment.Exit(-1);
            }

        }

        public bool ValidateFileCount()
        {
            bool match = false;
            var fileCount = Directory.GetFiles(DownloadDirectory).Length;
            if (fileCount == contactList.Count)
                match = true;

            return match;
        }

        public void CalcTotalBalance(bool excludeAfterMidnight)
        {

            var UTCToday = DateTime.Today.ToUniversalTime();
            var UTCMidnight = UTCToday.AddHours(-UTCToday.Hour);
            decimal totalEOS = 0;

            logger.Info("Accounts created after midnight UTC ({0}) will be removed: {1}", UTCMidnight.ToString("yyyy-MM-dd HH:mm:ss \"GMT\"zzz") ,excludeAfterMidnight);
            BalanceFile = Path.Combine(OutputDirectory, "balances.csv");

            using (StreamWriter sw = File.CreateText(BalanceFile))
            {
                StopWatch.Restart();
                int counter = 0;
                sw.WriteLine(string.Format("{0},{1}", "account_name", "total_eos"));
            
                foreach (var contact in contactList.OrderBy(x => x).ToList())
                {
                    var file = Path.Combine(DownloadDirectory, contact + ".txt");

                    counter++;

                    Console.SetCursorPosition(0, Console.CursorTop);
                    var percentage = ((float)counter / (float)contactList.Count) * 100.00;
                    Console.Write(string.Format("{0:n0}/{1:n0} ({2:n0}%) - ELAPSED: {3}", counter, contactList.Count, percentage, StopWatch.Elapsed));

                    //Console.Write(string.Format("{0}/{1} ({2}%) - ELAPSED: {3}", counter, file.Count, "x", StopWatch.Elapsed));


                    //logger.Info("Process:  {0}", file);
                    var account = JsonConvert.DeserializeObject<EOSAccount_row>(File.ReadAllText(file));
                    var account_name = account.account_name;
                    if (account.created_datetime > UTCMidnight)
                    {
                        logger.Info("Account {0} will be excluded as it was created after midnight at {1}", account_name, account.created_datetime);
                        continue;
                    }
  
                    decimal cpu_weight_decimal = 0;
                    decimal net_weight_decimal = 0;
                    if (account.self_delegated_bandwidth != null)
                    {
                        cpu_weight_decimal = account.self_delegated_bandwidth.cpu_weight_decimal;
                        net_weight_decimal = account.self_delegated_bandwidth.net_weight_decimal;
                    }

                    var balance = account.core_liquid_balance_ulong + cpu_weight_decimal + net_weight_decimal;
                    sw.WriteLine(string.Format("{0},{1}", account_name, balance));
                    totalEOS = totalEOS + balance;
                }
                logger.Info("Total EOS = {0}", totalEOS);
                Console.WriteLine();
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

            /*
            logger.Info("The following AWS profiles were found:");
            var profiles = ProfileManager.ListProfileNames();
            foreach (var profile in profiles)
            {
                logger.Info("Profile: {0}", profile);
            }
            */

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
