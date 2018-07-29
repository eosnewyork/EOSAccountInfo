# EOS Account Analyzer

This project acts as part of a data pipeline that feeds https://www.eossnapshots.io

The application takes as input a flat list of account names. It then:  
1. Fetches account information for each account
2. Creates a CSV of the accounts and the respective balances. 

## Installation


1. Install .NET core on your OS (Yes, this really works on Linux, and macOS) - easy to follow instructions can be found here
https://www.microsoft.com/net/learn/get-started

2. Clone the supporting EOSDotNet prohject, this is used by the application
```
git clone https://github.com/eosnewyork/EOSDotNet.git
cd /EOSDotNet
./build
```

3. Clone the application repo
```
git clone https://github.com/eosnewyork/EOSAccountInfo.git
cd EOSAccountInfo
./build
```

TODO: A note to reader .. it's planned to make EOSDotNet a submodule of EOSAccountInfo so that you don't need to check out and compile two different projects. 

### usage

Simply running ./EOSAccountAnalyzer.sh will output help.

Currently two commands are supported Expand and Compare. Only the "Expand" command is suggested for use at this time. 

```
./EOSAccountAnalyzer.sh Expand -h
```

```
Usage - EOSAccountAnalyzer Expand [<FilePath>] [<OutputPath>] [<Overwrite>] [<Apihost>] [<SkipDownload>] [<Zipoutput>] [<S3bucket>] [<S3profile>] [<UploadtoS3>] [<Resume>] [<ExcludeAccountsCreatedAfterMidnightUTC>]

Expand Options
Option                                        Description
FilePath (-F)                                 The path to the file containing the flat list of contacts you'd like to expand
OutputPath (-O)                               The path to the directory you'd like to output to
Overwrite (-Ov)                               If the output directory path exists, delete and create start with an empty directory. [Default='False']
Apihost (-A)                                  The url of the EOS API [Default='http://pennstation.eosdocs.io:7001']
SkipDownload (-S)                             Process the files in the existing output directory. Do not re-download the data. [Default='False']
Zipoutput (-Z)                                The path to the ZIP file that will contain the raw data and summary file. [Default='summary.zip']
S3bucket (-S3)                                The name of the S3 bucket that the compressed file will be uploaded to. [Default='eossnapshots-staticsitebucket-zp2kemxur4pw']
S3profile (-S3p)                              The name of the S3 profile which contains the required credentials for upload. [Default='publicwebsitefileupload']
UploadtoS3 (-U)                               Disable the upload to S3. [Default='False']
Resume (-R)                                   Resume downloads from where you left off (if you have SkipDownload = false). [Default='False']
ExcludeAccountsCreatedAfterMidnightUTC (-E)   Exclude accounts that were created after midnight UTC. All accounts will be processed but final output will exclude those created after midnight UTC [Default='True']

```

Example usage:

```
./EOSAccountAnalyzer.sh Expand -f ~/accountdata/acct_snapshot_uniq.txt -O ~/accountdata/output -A "http://localhost" -Z ~/accountdata/summary.zip
```

The above command will produce a CSV file in  ~/accountdata/output/balances.csv

example output
```
creation_time,account_name,total_eos
2018-06-10 01:04:13,111111111111,0.1003
2018-06-12 05:41:00.500000,111111111112,0.0000
2018-06-12 02:06:48,111111111113,0.0000
2018-06-12 05:54:12,111111111114,10.4640
2018-06-12 02:07:13.500000,111111111115,0.0000
2018-06-11 01:00:48.500000,11111111111a,0.2000
2018-06-11 01:22:09.500000,11111111111b,0.2000
2018-06-12 06:32:13.500000,11111111111c,0.0000
2018-06-12 06:33:40.500000,11111111111d,0.0000
```

## S3 Authentication

Note that the file upload to S3 requires that the machine have a configured S3 authenticaiton profile.

For more information on S3 profiles see https://docs.aws.amazon.com/cli/latest/userguide/cli-multiple-profiles.html 

