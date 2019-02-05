using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security;
using System.Security.Cryptography;

namespace Exchange.Net
{
    public abstract class ExchangeApiCore
    {
        private SecureString apiKey;
        private SecureString apiSecret;

        protected SecureString ApiKey
        {
            get { return this.apiKey; }
        }

        protected SecureString ApiSecret
        {
            get { return this.apiSecret; }
        }

        protected HashAlgorithm Encryptor { get; set; }

        public bool IsSigned
        {
            get { return ApiKey != null && ApiSecret != null; }
        }

        protected long nonce { get => DateTime.UtcNow.Ticks; }

        protected bool LoadApiKeys(string filepath)
        {
            if (File.Exists(filepath))
            {
                var plain = File.ReadAllText(filepath);
                var parts = plain.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 3)
                {
                    var entropy = Convert.FromBase64String(parts[0]);
                    var apiKeyProtected = Convert.FromBase64String(parts[1]);
                    var apiSecretProtected = Convert.FromBase64String(parts[2]);
                    var apiKeyBytes = ProtectedData.Unprotect(apiKeyProtected, entropy, DataProtectionScope.CurrentUser);
                    var apiSecretBytes = ProtectedData.Unprotect(apiSecretProtected, entropy, DataProtectionScope.CurrentUser);
                    apiKey = new SecureString();
                    foreach (byte ch in apiKeyBytes)
                    {
                        apiKey.AppendChar((char)ch);
                    }
                    apiSecret = new SecureString();
                    foreach (byte ch in apiSecretBytes)
                    {
                        apiSecret.AppendChar((char)ch);
                    }
                    return true;
                }
            }
            return false;
        }

        public static void SaveApiKeys(string filepath, SecureString apiKey, SecureString apiSecret)
        {
            var entropy = new byte[20];
            using (var rng = new RNGCryptoServiceProvider())
            {
                rng.GetBytes(entropy);
                var apiKeyProtected = ProtectedData.Protect(apiKey.ToByteArray(), entropy, DataProtectionScope.CurrentUser);
                var secretKeyProtected = ProtectedData.Protect(apiSecret.ToByteArray(), entropy, DataProtectionScope.CurrentUser);
                var plain = String.Join(Environment.NewLine, Convert.ToBase64String(entropy), Convert.ToBase64String(apiKeyProtected), Convert.ToBase64String(secretKeyProtected));
                System.IO.File.WriteAllText(filepath, plain);
            }
        }

        protected byte[] SignString(string value)
        {
            return Encryptor.ComputeHash(Encoding.UTF8.GetBytes(value));
        }

        protected long ToUnixTimestamp(DateTime time)
        {
            return (long)(time - new DateTime(1970, 1, 1)).TotalMilliseconds;
        }

        protected static string ByteArrayToHexString(byte[] value)
        {
            return value.Select(x => x.ToString("X2")).Aggregate((x1, x2) => x1 + x2);
        }
    }

    public class ApiResult<T>
    {
        public TimeSpan Elapsed { get => elapsed; }
        public T Result { get => result; }

        public ApiResult(TimeSpan elapsed, T result)
        {
            this.elapsed = elapsed;
            this.result = result;
        }

        private TimeSpan elapsed;
        private T result;
    }
}
