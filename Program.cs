using System;

using System.Threading;
using MySql.Data.MySqlClient;
using System.Threading.Tasks;
using Org.BouncyCastle.Utilities.IO;
using System.Diagnostics;
using Ubiety.Dns.Core.Records;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections.Generic;

namespace TranscriptInjector
{
    class Program
    {

        static void Main()
        {
            


            string path = @"D:\Temp";

            Transcriber transcriber = new Transcriber();
            Transcriber.Options options;
            options.wordSize = 3;
            options.stopifError = true;
            options.removeTimeStamp = true;
            options.items = transcriber.GetFiles(path);
            transcriber.Start(options);
            
          

            

            }


    }
}
