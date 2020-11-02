using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Linq;
using MySql.Data.MySqlClient;
using System.Threading.Channels;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.ComponentModel.DataAnnotations;
using Ubiety.Dns.Core.Records;
using Microsoft.Data.Sqlite;

namespace TranscriptInjector
{
    class Transcriber
    {
        public struct Options
        {
            public IEnumerable<string> items;
            public bool stopifError;
            /// <summary>
            /// minimal size of word
            /// </summary>
            public int wordSize;
            /// <summary>
            /// for Srt file
            /// </summary>
            public bool removeTimeStamp;
        }

        public void Start(Options options)
        {
            Console.WriteLine("founds items: " + options.items.Count());
            int s = 1;
            foreach (string item in options.items)
            {
                try
                {
                    Console.WriteLine(s + "." + item.Substring(item.LastIndexOf("\\") + 1));
                    TranscriptInject(item, options.wordSize, options.removeTimeStamp);
                    s++;
                }
                catch (Exception e)
                {
                    if (options.stopifError)
                    {
                        Console.WriteLine(e.Message);
                        Console.WriteLine("Proccess terminated");
                        return;
                    }
                    else 
                    {
                        Console.WriteLine(e.Message);
                    }
                }
            }
            Console.WriteLine("Proccess is over");
        }

        private void TranscriptInject(string path, int wordSize = 3, bool removeTimeStamp = false)
        {
            string[] lines = null;
            string fulltext;
            try
            {
                lines = File.ReadAllLines(path);
                List<string> list = new List<string>();
                StringBuilder builder = new StringBuilder();
                foreach (string line in lines)
                {
                    var col = Regex.Matches(line, @"\b[^\W\d]+?\b").Select(x => x.Value).Where(c => c.Length > 3).Distinct();
                    list.AddRange(col);
                    builder.Append(line + Environment.NewLine);
                }
                fulltext = builder.ToString();
                if (removeTimeStamp)
                {
                    fulltext = RemoveTimeStampInSrt(fulltext);
                }
                list = list.Where(x => x.Length > wordSize).Select(p => p.ToLower()).Where(x => Regex.Match(x, @"^\w").Success == true).Distinct().OrderBy(x => x).ToList();
                Dictionary<string, string> dic = Fetch(list);
                foreach (var pair in dic)
                {
                    if (pair.Key.Contains("+"))
                    {
                        Console.WriteLine(1);
                    }
                    string pattern = @"\b(?i)" + pair.Key + @"\b";
                    fulltext = Regex.Replace(fulltext, pattern, new MatchEvaluator(m => m.ToString() + " [" + pair.Value + "]"));
                }
                int dot = path.LastIndexOf('.');
                string p = path.Substring(0, dot) + ".phonetic" + path.Substring(dot);
                File.WriteAllText(p, fulltext);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message + " path:" + path);
                throw new Exception(e.Message);
            }
        }
        /// <summary>
        /// get list of file 
        /// </summary>
        /// <param name="path"></param>
        /// <param name="extensions">default values: "txt", "srt", "md"</param>
        /// <returns></returns>
        public IEnumerable<string> GetFiles(string folder, string[] extensions = null)
        {
            extensions ??= new string[] { "txt", "srt", "md" };
            string pattern = @"\.(?i)(" + string.Join('|', extensions) + ")$";
            var items = Directory.GetFiles(folder).Where(p => Regex.Match(p, pattern).Success == true);
            string pattern1 = @"^(?!.*phonetic).*"; // not contains 'phonetic';
            items = items.Where(p => Regex.Match(p, pattern1).Success == true);
            RemoveReadonlyAttribute(items);
            return items;
        }

        private string RemoveTimeStampInSrt(string sample)
        {
            sample = Regex.Replace(sample, @"(?i).*-->.*(\r\n|\r)", "");
            sample = Regex.Replace(sample, @"(?m)^\d{1,}(\r\n|\r)", "");
            while (Regex.Match(sample, @"(?m).*\r\n\s*?\r\n").Success)
            {
                sample = Regex.Replace(sample, @"(?m)\r\n\s*?\r\n", "\r\n");
            }
            return sample;
        }

        private void RemoveReadonlyAttribute(IEnumerable<string> items)
        {
            foreach (var item in items)
            {
                new FileInfo(item)
                {
                    IsReadOnly = false
                };
            }
        }
        /// <summary>
        /// MySQL fetcher
        /// </summary>
        /// <param name="list"></param>
        /// <returns></returns>
        private Dictionary<string, string> Fetch(List<string> list)
        {
            var dic = new Dictionary<string, string>();
            try
            {
                using MySqlConnection conn = new MySqlConnection("server = localhost; userid = root; pwd = rootpw; port = 3306; database = transcript;");
                conn.Open();
                using MySqlCommand myCommand = new MySqlCommand()
                {
                    Connection = conn
                };
                string str = String.Join(',', list.Select(x => "\"" + x + "\"").ToArray());
                myCommand.CommandText = "Select WORD, BrE2 FROM  words Where Word in (" + str + ")";
                var builder = new StringBuilder();
                using (var reader = myCommand.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (!dic.ContainsKey(reader[0].ToString()))
                        {
                            dic.Add(reader[0].ToString(), reader[1].ToString());
                        }
                        else
                        {
                            builder.AppendLine($"{reader[0]} {dic[reader[0].ToString()]} {reader[1]}");
                        }
                    }
                }
                Console.WriteLine("There are not transcription for the following words: \n" + String.Join('\n', list.Where(x => !dic.ContainsKey(x)).ToArray()));
                if (builder.Length > 0)
                {
                    Console.Write($"Check the database. There are repetitions:\n {builder}");
                }


            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
            return dic;
        }
        /// <summary>
        /// SQlLite fetcher
        /// </summary>
        /// <param name="list"></param>
        /// <returns></returns>
        private Dictionary<string, string> LiteFetcher(List<string> list)
        {
            var dic = new Dictionary<string, string>();
            string db = Directory.GetParent(Environment.CurrentDirectory).Parent.Parent.FullName + @"\PhoTransEditDB.sqlite";
            try
            {
                using var connection = new SqliteConnection("Data Source=" + db);
                connection.Open();
                var command = connection.CreateCommand();
                string str = String.Join(',', list.Select(x => "\"" + x + "\"").ToArray());
                command.CommandText = "Select WORD, BrE2 FROM  words Where Word in (" + str + ")";

                var builder = new StringBuilder();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (!dic.ContainsKey(reader[0].ToString()))
                        {
                            dic.Add(reader[0].ToString(), reader[1].ToString());
                        }
                        else
                        {
                            builder.AppendLine($"{reader[0]} {dic[reader[0].ToString()]} {reader[1]}");
                        }
                    }
                }
                Console.WriteLine("There are not transcription for the following words: \n" + String.Join('\n', list.Where(x => !dic.ContainsKey(x)).ToArray()));
                if (builder.Length > 0)
                {
                    Console.Write($"Check the database. Repetitions exist:\n {builder}");
                }
            }

            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
            return dic;
        }
    }
}
