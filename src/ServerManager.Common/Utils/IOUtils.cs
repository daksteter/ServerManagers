using System;
using System.IO;
using System.Runtime.InteropServices;

namespace ServerManagerTool.Common.Utils
{
    public static class IOUtils
    {
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteFile(string name);

        public static bool IsUnc(string path) => new Uri(path).IsUnc;

        /// <summary>
        /// Whether the folder can't be there: neither it nor its drive (or network share) exists, as after a move to
        /// another PC, a removed drive or a changed drive letter. A missing folder on an existing drive could be created.
        /// </summary>
        public static bool IsOnMissingDrive(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
                return false;

            try
            {
                var fullPath = Path.GetFullPath(folder);
                return !Directory.Exists(fullPath) && !Directory.Exists(Path.GetPathRoot(fullPath));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                // not a usable path at all
                return true;
            }
        }

        public static string NormalizeFolder(string path)
        {
            var newPath = path.TrimEnd('\\') + "\\";

            if (IsUnc(newPath))
            {
                return newPath.TrimEnd('\\');
            }

            var root = Path.GetPathRoot(newPath);
            if (!root.EndsWith("\\"))
                root += "\\";

            if (!string.Equals(root, newPath, StringComparison.OrdinalIgnoreCase))
            {
                newPath = newPath.TrimEnd('\\');
            }

            return newPath;
        }

        public static string NormalizePath(string path) => 
            Path.GetFullPath(new Uri(path).LocalPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .ToLowerInvariant();

        public static bool Unblock(string fileName)
        {
            return DeleteFile(fileName + ":Zone.Identifier");
        }
    }
}
