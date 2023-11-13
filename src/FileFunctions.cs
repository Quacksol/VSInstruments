using System.Collections.Generic; // Lists
using System.Diagnostics; // debug todo remove
using System.IO; // Open files
using System.Text; // UTF8 stuff


namespace instruments
{
    public class RecursiveFileProcessor
    {
        // Process all files in the directory passed in, recurse on any directories
        // that are found, and process the files they contain.
        // nicked straight off microsoft docs
        public static bool DirectoryExists(string path)
        {
            Debug.WriteLine(Directory.GetCurrentDirectory()); // This returns the location in program files (or wherever the game exe is), not the mods folder :'(
            return Directory.Exists(path);
        }
        public static void ProcessDirectory(string targetDirectory, string baseDirectory, ref List<string> abcFiles)
        {
            // Process the list of files found in the directory.
            string[] fileEntries = Directory.GetFiles(targetDirectory);
            foreach (string fileName in fileEntries)
                ProcessFile(fileName, baseDirectory, ref abcFiles);

            // Recurse into subdirectories of this directory.
            string[] subdirectoryEntries = Directory.GetDirectories(targetDirectory);
            foreach (string subdirectory in subdirectoryEntries)
                ProcessDirectory(subdirectory, baseDirectory, ref abcFiles);
        }

        // Insert logic for processing found files here.
        public static void ProcessFile(string path, string baseDirectory, ref List<string> abcFiles)
        {
            if (path.Substring(path.Length - 4) == ".abc")
                abcFiles.Add(path.Replace(baseDirectory, ""));
        }
        public static bool ReadFile(string path, ref string fileData)
        {
            System.IO.FileStream stream;
            if (File.Exists(path))
            {
                stream = File.OpenRead(path);
                byte[] b = new byte[stream.Length];
                UTF8Encoding temp = new UTF8Encoding(true);
                while (stream.Read(b, 0, b.Length) > 0)
                {
                    fileData = temp.GetString(b);
                }
            }
            else
                return false;
            return true;
        }
    }
}