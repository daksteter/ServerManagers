using System;
using System.Reflection;
using System.Xml;

namespace ServerManagerTool.Common.Utils
{
    public static class AppUtils
    {
        public static string GetDeployedVersion()
        {
            try
            {
                var assembly = Assembly.GetEntryAssembly();
                return GetDeployedVersion(assembly);
            }
            catch
            {
                return "Unknown";
            }
        }

        public static string GetDeployedVersion(Assembly assembly)
        {
            if (assembly == null)
                return "Unknown";

            try
            {
                // The .NET Framework build read this from the ClickOnce "<exe>.manifest" file,
                // which .NET builds don't produce; use the assembly's own version instead.
                return assembly.GetName().Version?.ToString() ?? "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }
    }
}
