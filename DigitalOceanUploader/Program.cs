using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using System.Linq;

using HeyRed.Mime;
using Amazon.S3;
using Amazon.Runtime;
using DigitalOceanUploader.Shared;

namespace DigitalOceanSpacesManager
{
    class Program
    {
        public static void Main(string[] args)
        {
            MainAsync(args).GetAwaiter().GetResult();
        }

        public async static Task MainAsync(string[] args)
        {
            //Welcome user
            Console.BackgroundColor = ConsoleColor.Blue;
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("Digital Ocean Spaces Manager");
            Console.ResetColor();

            #region Keys
            Console.Write("Enter DO Access Key: ");
            ConsoleKeyInfo key;
            KeyManager keyManager = new KeyManager();
            do
            {
                key = Console.ReadKey(true);

                if (key.Key != ConsoleKey.Backspace && key.Key != ConsoleKey.Enter)
                {
                    keyManager.AccessKey.AppendChar(key.KeyChar);
                    Console.Write("*");
                }
                else
                {
                    if (key.Key == ConsoleKey.Backspace && keyManager.AccessKey.Length > 0)
                    {
                        keyManager.AccessKey.RemoveAt(keyManager.AccessKey.Length - 1);
                        Console.Write("\b \b");
                    }
                }
            } while (key.Key != ConsoleKey.Enter);
            Console.Write("\n");
            Console.Write("Enter DO Secrey Key: ");
            keyManager.SecretKey.Clear();
            do
            {
                key = Console.ReadKey(true);

                if (key.Key != ConsoleKey.Backspace && key.Key != ConsoleKey.Enter)
                {
                    keyManager.SecretKey.AppendChar(key.KeyChar);
                    Console.Write("*");
                }
                else
                {
                    if (key.Key == ConsoleKey.Backspace && keyManager.SecretKey.Length > 0)
                    {
                        keyManager.SecretKey.RemoveAt(keyManager.SecretKey.Length - 1);
                        Console.Write("\b \b");
                    }
                }
            } while (key.Key != ConsoleKey.Enter);
            Console.Write("\n");
            #endregion

            string filePath, uploadName, spaceName, contentType = string.Empty;
            Console.Write("Enter Space name to use: ");
            spaceName = Console.ReadLine();

            // Can now setup manager
            DigitalOceanUploadManager digitalOceanUploadManager = new DigitalOceanUploadManager(keyManager, spaceName);

            Console.WriteLine("Do you wish to upload or download a file? (U - upload file, UD - Upload Directory, D - download): ");
            var upDown = Console.ReadLine();

            if (upDown == "U")
            {
                bool fileExists = false;
                do
                {
                    Console.Write("Enter file location: ");
                    filePath = Console.ReadLine();
                    if (File.Exists(filePath))
                    {
                        contentType = MimeGuesser.GuessFileType(filePath).MimeType;
                        fileExists = true;
                    }
                    else
                    {
                        fileExists = false;
                        Console.WriteLine("File does not exist.  Please enter again.");
                    }
                } while (!fileExists);
                Console.Write("Enter name to use when uploaded: ");
                uploadName = Console.ReadLine();
                Console.Write("Wipe away previous attempts? (Y/n): ");
                var wipeAway = Console.ReadLine();
                if (wipeAway == "Y")
                {
                    await digitalOceanUploadManager.CleanUpPreviousAttempts();
                }

                digitalOceanUploadManager.UploadStatusEvent += DigitalOceanUploadManager_UploadStatusEvent;
                digitalOceanUploadManager.UploadExceptionEvent += DigitalOceanUploadManager_UploadExceptionEvent;
                var uploadId = await digitalOceanUploadManager.UploadFile(filePath, uploadName);
                Console.WriteLine("File upload complete");
                digitalOceanUploadManager.UploadStatusEvent -= DigitalOceanUploadManager_UploadStatusEvent;
                digitalOceanUploadManager.UploadExceptionEvent -= DigitalOceanUploadManager_UploadExceptionEvent;
            }
            else if (upDown == "D")
            {
                Console.Write("Enter name used to upload file: ");
                var uploadFileName = Console.ReadLine();
                Console.Write("Enter location to save file: ");
                var downloadLocation = Console.ReadLine();
                var file = await digitalOceanUploadManager.DownloadFile(uploadFileName);
                using (var fs = File.Create(downloadLocation))
                {
                    fs.Write(file, 0, file.Length);
                }
                Console.WriteLine($"File downloaded to {downloadLocation} ({file.Length})");
            }
            else if (upDown == "UD")
            {
                Console.Write("Enter file location: ");
                string folderPath = Console.ReadLine();
                bool folderExists = Directory.Exists(folderPath);
                if (!folderExists)
                {
                    Console.WriteLine("Folder does not exist");
                }
                else
                {
                    //Console.Write("Wipe away previous attempts? (Y/n): ");
                    //var wipeAway = Console.ReadLine();
                    //if (wipeAway == "Y")
                    //{
                    //    await digitalOceanUploadManager.CleanUpPreviousAttempts();
                    //}
                    //var files = Directory.GetFiles(folderPath);


                    //foreach (var file in Directory.EnumerateFiles(folderPath))
                    //{
                    //    uploadName = Path.GetFileName(file);
                    //    var uploadId = await digitalOceanUploadManager.UploadFile(file, uploadName);
                    //    Console.WriteLine($"{uploadName} upload complete");
                    //}
                    //var ids = Directory.EnumerateFiles(folderPath).Select(x => UploadFile(x, digitalOceanUploadManager));
                    int total = 0;
                    IEnumerable<IEnumerable<string>> sublists = Directory.EnumerateFiles(folderPath).Chunk(5);
                    foreach (var group in sublists)
                    {
                        digitalOceanUploadManager.UploadStatusEvent += DigitalOceanUploadManager_UploadStatusEvent;
                        digitalOceanUploadManager.UploadExceptionEvent += DigitalOceanUploadManager_UploadExceptionEvent;

                        var ids = group.Select(x => digitalOceanUploadManager.UploadFile(x, Path.GetFileName(x)));
                        var res = await Task.WhenAll(ids);
                        Console.WriteLine($"Uploaded {res.Length} files");

                        digitalOceanUploadManager.UploadStatusEvent -= DigitalOceanUploadManager_UploadStatusEvent;
                        digitalOceanUploadManager.UploadExceptionEvent -= DigitalOceanUploadManager_UploadExceptionEvent;
                        total += 5;
                    }
                    Console.WriteLine($"Total upload = {total}");
                }

            }
            else
                Console.WriteLine("No idea what you want.  Try again");

            digitalOceanUploadManager?.Dispose();
            digitalOceanUploadManager = null;

            keyManager?.Dispose();
            keyManager = null;

            Console.WriteLine("Press enter to exit");
            Console.ReadLine();
        }

        static void DigitalOceanUploadManager_UploadStatusEvent(object sender, UploadStatus e)
        {
            Console.WriteLine($"File Upload Status: Part {e.PartNumber}/{e.EstimatedParts} ({e.BytesUploaded}/{e.TotalBytes})");
        }

        static void DigitalOceanUploadManager_UploadExceptionEvent(object sender, UploadException e)
        {
            Console.WriteLine(e.Message);
        }

        static async Task<string> UploadFile(string filePath, DigitalOceanUploadManager digitalOceanUploadManager)
        {
            string uploadName = Path.GetFileName(filePath);

            digitalOceanUploadManager.UploadStatusEvent += DigitalOceanUploadManager_UploadStatusEvent;
            digitalOceanUploadManager.UploadExceptionEvent += DigitalOceanUploadManager_UploadExceptionEvent;
            var uploadId = await digitalOceanUploadManager.UploadFile(filePath, uploadName);
            Console.WriteLine($"{uploadName} upload complete");
            digitalOceanUploadManager.UploadStatusEvent -= DigitalOceanUploadManager_UploadStatusEvent;
            digitalOceanUploadManager.UploadExceptionEvent -= DigitalOceanUploadManager_UploadExceptionEvent;
            return uploadId;
        }
    }
}
