using System;

namespace QuickNote.Infrastructure.Cloud
{
    /// <summary>
    /// Manages Google OAuth client credentials safely.
    /// Credentials can be provided via a git-ignored GoogleDriveSecrets.local.cs,
    /// environment variables (QUICKNOTE_GOOGLE_CLIENT_ID, QUICKNOTE_GOOGLE_CLIENT_SECRET),
    /// or user settings in the app.
    /// </summary>
    public static partial class GoogleDriveSecrets
    {
        public static string ClientId => GetConfiguredClientId();
        public static string ClientSecret => GetConfiguredClientSecret();

        static partial void InitializeCustomSecrets(ref string? clientId, ref string? clientSecret);

        private static string GetConfiguredClientId()
        {
            string? id = null;
            string? secret = null;
            InitializeCustomSecrets(ref id, ref secret);
            if (!string.IsNullOrWhiteSpace(id)) return id;

            var env = Environment.GetEnvironmentVariable("QUICKNOTE_GOOGLE_CLIENT_ID");
            if (!string.IsNullOrWhiteSpace(env)) return env;

            return "614992283073-sc1hivoo37bjt5rap571kuo7j1kjjv86.apps.googleusercontent.com";
        }

        private static string GetConfiguredClientSecret()
        {
            string? id = null;
            string? secret = null;
            InitializeCustomSecrets(ref id, ref secret);
            if (!string.IsNullOrWhiteSpace(secret)) return secret;

            var env = Environment.GetEnvironmentVariable("QUICKNOTE_GOOGLE_CLIENT_SECRET");
            if (!string.IsNullOrWhiteSpace(env)) return env;

            return string.Empty;
        }
    }
}
