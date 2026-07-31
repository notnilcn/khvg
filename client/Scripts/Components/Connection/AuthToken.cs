#nullable enable
using System;
using System.IO;

namespace SpacetimeDB
{
    /// <summary>
    /// Persists the SpacetimeDB auth token to a local file so reconnects reuse the
    /// same identity instead of minting a new anonymous one each run.
    /// </summary>
    public static class AuthToken
    {
        private static string _filePath = "";

        public static string? Token { get; private set; }

        public static void Init(string filePath)
        {
            _filePath = filePath;
            Token = File.Exists(_filePath) ? File.ReadAllText(_filePath).Trim() : null;
            if (string.IsNullOrEmpty(Token))
                Token = null;
        }

        public static void SaveToken(string token)
        {
            Token = token;
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(_filePath, token);
        }
    }
}
