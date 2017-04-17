using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CommandLine;
using System.Data.SqlClient;

namespace SqlRun
{
    class Program
    {
		static SqlProvider SqlProvider; 
        
        static Options options;
        private static bool isStop = false;

        static void Main(string[] args)
        {
            bool IsDirectory = true;
            bool IsFromFile = false;
            try
            {
                options = new Options();
                if(args.Length == 0) { 
                    Console.WriteLine(options.GetUsage());
                    Console.Write("Run sql with param to default? (y/n): ");
                    string res = Console.ReadLine();
                    if(res != "y")
                        return;
                }

                if (Parser.Default.ParseArguments(args, options))
                {
                    options.Verbose = true;
                }
                if (!string.IsNullOrEmpty(options.Path))
                {
                    options.Path = options.Path.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    //char lastChar = options.Path[options.Path.Length - 1];
                    if (File.Exists(options.Path))
                    {
                        IsDirectory = false;
                    }
                    else
                    {
                        if (Directory.Exists(options.Path))
                        {
                            options.Patern = "*.sql";
                        }
                        else
                        {
                            int index = options.Path.LastIndexOf(Path.AltDirectorySeparatorChar);
                            if (index != -1) {
                                options.Patern = options.Path.Substring(index);
                                options.Path = options.Path.Substring(0, index);
                            }
                            else {
                                options.Patern = "*";
                            }
                        }
                    }
                }
                else if (!string.IsNullOrEmpty(options.File))
                {
                    IsFromFile = true;
                }
                else
                {
                    options.Path = Environment.CurrentDirectory;
                    options.Patern = "*.sql";
                }

                //test(args); return;

				SqlProvider = new SqlProvider(options);
				SqlProvider.InitConection ();

                if (IsFromFile)
                {
                    foreach (var file in File.ReadAllLines(options.File))
                    {
                        string path = file.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        if (File.Exists(path))
                        {
                            Console.ResetColor();
                            ActionFile(path);                           
                        }
                        else
                        {
                            string Patern = "";
                            if (Directory.Exists(path))
                            {
                                Patern = "*.sql";
                            }
                            else
                            {
                                int index = path.LastIndexOf(Path.AltDirectorySeparatorChar);
                                if (index != -1) { 
                                    Patern = path.Substring(index);
                                    path = CleanFileName(path.Substring(0, index));
                                }
                                if (!Directory.Exists(path))
                                {
                                    Console.ForegroundColor = ConsoleColor.Red;
                                    Console.WriteLine("File {0} not exists", path);
                                    isStop = true;
                                    continue;
                                }
                            }
                            foreach (FileInfo fileInfo in (new DirectoryInfo(path)).GetFiles(Patern))
                            {
                                ActionFile(fileInfo.FullName);
                            }
                        }
                    }
                }
                else if (IsDirectory)
                {
                    options.Path = CleanFileName(options.Path);
                    var d = new DirectoryInfo(options.Path);
                    var files = d.GetFiles(options.Patern);

                    if (files.Length == 0)
                    {
                        var newPath = Path.Combine(options.Path, "sql");
                        if (Directory.Exists(newPath))
                        {
                            d = new DirectoryInfo(newPath);
                            files = d.GetFiles(options.Patern);
                        }
                    }

                    foreach (var file in files.OrderBy(c =>
                     {
                         int i = int.MaxValue;
                         if (Regex.IsMatch(c.Name, "^\\d+"))
                             i = int.Parse(Regex.Match(c.Name, "^\\d+").Value);
                         return i;
                     }))
                    {
                        ActionFile(file.FullName);
                    }
                }
                else
                {
                    ActionFile(options.Path);
                }
            }
            catch (Exception e)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(e.Message);
                Console.WriteLine(e.StackTrace);
                Console.ResetColor();
                isStop = true;                
            }
            Console.WriteLine("end");
            if(isStop) Console.Read();
        }

        private static string CleanFileName(string fileName)
        {
            return fileName.Replace("\"", "");
        }

        private static void test(string[] args)
        {
            Console.WriteLine("-----------------------------");
            Console.WriteLine("args:");
            foreach (var item in args)
            {
                Console.WriteLine("\t{0}", item);
            }

            Console.WriteLine("DataBase - {0}", options.DataBase);
            Console.WriteLine("File - {0}", options.File);
            Console.WriteLine("Patern - {0}", options.Patern);
            Console.WriteLine("Path - {0}", options.Path);
            Console.WriteLine("Source - {0}", options.Source);
            Console.WriteLine("Verbose - {0}", options.Verbose);

            Console.WriteLine("-----------------------------");
        }        

        private static void ActionFile(string file)
        {
            try
            {
                Console.WriteLine("file - {0}", Path.GetFileNameWithoutExtension(file));
                SqlProvider.ExecuteSqlCommand(File.ReadAllText(file));
            }
            catch (InvalidCastException ex) { throw new Exception($"InvalidCastException:{ex.Message} {file} "); }
            catch (SqlException ex) { throw new Exception($"SqlException:{ex.Message} {file} LineNumber:{ex.LineNumber}"); }
            catch (IOException ex) { throw new Exception($"IOException:{ex.Message} {file} "); }
            catch (InvalidOperationException ex) { throw new Exception($"InvalidOperationException:{ex.Message} {file} "); }
            catch (Exception ex) { throw new Exception($"Exception:{ex.Message} {file} "); }
            Console.ResetColor();
        }       
    }
}
