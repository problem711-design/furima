using System;
using System.IO;
using System.Reflection;
using System.Text;

public static class DashboardHtml
{
    private const string ResourceName = "FurimaDashboard.html";

    private static string EscapeJsSingleQuoted(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value
            .Replace("\\", "\\\\")
            .Replace("'", "\\'")
            .Replace("\r", "")
            .Replace("\n", "");
    }

    public static string GetHtml(string authToken = "")
    {
        Assembly asm = Assembly.GetExecutingAssembly();
        using (Stream stream = asm.GetManifestResourceStream(ResourceName))
        {
            if (stream == null)
            {
                throw new InvalidOperationException("Embedded resource not found: " + ResourceName);
            }
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
            {
                string template = reader.ReadToEnd();
                return template.Replace("__AUTH_TOKEN__", EscapeJsSingleQuoted(authToken));
            }
        }
    }
}
