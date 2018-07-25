# EOS Account Analyzer

This project acts as part of a data pipeline that feeds https://www.eossnapshots.io

The application takes as input a flat list of account names. It then:  
1. Fetches account information for each account
2. Creates a CSV of the accounts and the respective balances. 

### usage

Simply running EOSAccountAnalyzer will output help that looks something like this:

```
Usage - cscleos <action> -options

Expand - Retrieve data from one of the well known EOS tables

Usage - EOSAccountAnalyzer Expand [<FilePath>] [<OutputPath>] [<Overwrite>] [<Apihost>] [<SkipDownload>] [<Zipoutput>] [<S3bucket>] [<S3profile>]

Expand Options
Option              Description
FilePath (-F)       The path to the file containing the flat list of contacts you'd like to expand [Default='contacts.txt']
OutputPath (-O)     The path to the directory you'd like to output to [Default='output']
Overwrite (-Ov)     If the output directory path exists, delete and create start with an empty directory. [Default='False']
Apihost (-A)        The url of the EOS API [Default='http://pennstation.eosdocs.io:7001']
SkipDownload (-S)   Process the files in the existing output directory. Do not re-download the data. [Default='False']
Zipoutput (-Z)      The path to the ZIP file that will contain the raw data and summary file. [Default='summary.zip']
S3bucket (-S3)      The name of the S3 bucket that the compressed file will be uploaded to. [Default='eossnapshots-staticsitebucket-zp2kemxur4pw']
S3profile (-S3p)    The name of the S3 profile which contains the required credentials for upload. [Default='publicwebsitefileupload']```

```

Example usage:

```
dotnet EOSAccountAnalyzer.dll expand -f contacts.txt -ov true -s false -A http://api.eosnewyork.io

```

## Installation


1. Install .NET core on your OS (Yes, this really works on Linux, and macOS) - easy to follow instructions can be found here
https://www.microsoft.com/net/learn/get-started

2. Clone the repo
```
git clone https://github.com/eosnewyork/EOSAccountInfo.git
cd EOSAccountInfo/EOSAccountAnalyzer
```

## S3 Authentication

Note that the file upload to S3 requires that the machine have a configured S3 authenticaiton profile, this is outside the scope of this document. 
