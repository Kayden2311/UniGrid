using System;
using System.Text.RegularExpressions;

namespace unigrid.Helpers
{
    public static class InputSanitizer
    {
        public static string SanitizeInput(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            // Strip out common SQL Injection payload patterns and dangerous characters
            string clean = input;
            
            // Remove SQL comments -- and /* */
            clean = Regex.Replace(clean, @"--", "", RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, @"/\*.*?\*/", "", RegexOptions.IgnoreCase);
            
            // Remove common SQL commands if they appear as standalone keywords
            string[] sqlKeywords = { "SELECT", "INSERT", "UPDATE", "DELETE", "DROP", "UNION", "ALTER", "EXEC", "EXECUTE", "XP_CMDSHELL" };
            foreach (var keyword in sqlKeywords)
            {
                clean = Regex.Replace(clean, $@"\b{keyword}\b", "", RegexOptions.IgnoreCase);
            }

            // Escape single quotes to prevent breaking SQL commands if a raw query is ever executed
            clean = clean.Replace("'", "''");

            return clean.Trim();
        }
    }
}
