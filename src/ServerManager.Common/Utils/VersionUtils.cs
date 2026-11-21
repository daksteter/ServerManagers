using System;
using System.IO;
using System.Runtime.InteropServices;

namespace ServerManagerTool.Common.Utils
{
    public static class VersionUtils
    {
        public static Version GetVersionFromFile(string versionFile)
        {
            if (!string.IsNullOrWhiteSpace(versionFile) && File.Exists(versionFile))
            {
                return ParseVersion(File.ReadAllText(versionFile));
            }

            return new Version(0, 0);
        }

        /// <summary>
        /// Parses "98.9" or "358" (treated as "358.0"); returns 0.0 when the text isn't a version.
        /// </summary>
        public static Version ParseVersion(string versionString)
        {
            versionString = versionString?.Trim();
            if (!string.IsNullOrWhiteSpace(versionString))
            {
                if (versionString.IndexOf('.') == -1)
                    versionString += ".0";

                if (Version.TryParse(versionString, out Version version))
                    return version;
            }

            return new Version(0, 0);
        }

        /// <summary>
        /// Reads a string from a file's version resource, e.g. the "ArkVersion" entry of the ASA server exe.
        /// FileVersionInfo only exposes the standard entries (and returns nothing at all for that exe), so this
        /// asks Windows directly, using the resource's own language and code page.
        /// </summary>
        /// <returns>The value, or null if the file or entry doesn't exist.</returns>
        public static string GetVersionResourceString(string file, string key)
        {
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
                return null;

            var size = NativeMethods.GetFileVersionInfoSize(file, out _);
            if (size <= 0)
                return null;

            var data = new byte[size];
            if (!NativeMethods.GetFileVersionInfo(file, 0, size, data))
                return null;

            if (!NativeMethods.VerQueryValue(data, @"\VarFileInfo\Translation", out IntPtr translation, out int length) || length < 4)
                return null;

            var language = (ushort)Marshal.ReadInt16(translation);
            var codePage = (ushort)Marshal.ReadInt16(translation, 2);

            if (!NativeMethods.VerQueryValue(data, $@"\StringFileInfo\{language:X4}{codePage:X4}\{key}", out IntPtr value, out length) || length == 0)
                return null;

            return Marshal.PtrToStringUni(value);
        }

        private static class NativeMethods
        {
            [DllImport("version.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            internal static extern int GetFileVersionInfoSize(string filename, out int handle);

            [DllImport("version.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool GetFileVersionInfo(string filename, int handle, int length, byte[] data);

            [DllImport("version.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool VerQueryValue(byte[] block, string subBlock, out IntPtr buffer, out int length);
        }
    }
}
