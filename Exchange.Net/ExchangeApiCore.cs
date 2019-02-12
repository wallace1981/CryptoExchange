using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

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

        [ThreadStatic]
        private HashAlgorithm encryptor;
        protected long nonce { get => DateTime.UtcNow.Ticks; }

        protected log4net.ILog Log { get; private set; }
        protected virtual string LogName => "ApiClient";

        public bool IsSigned
        {
            get { return ApiKey != null && ApiSecret != null; }
        }

        public ExchangeApiCore(string hashFilePath = null, Type hashType = null)
        {
            encryptor = CreateHash(hashFilePath, hashType);
            InitializeLog();
        }

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
                    try
                    {
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
                    catch (CryptographicException ex)
                    {
                        Debug.Print(ex.ToString());
                    }
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
                File.WriteAllText(filepath, plain);
            }
        }

        protected byte[] SignString(string value)
        {
            return encryptor.ComputeHash(Encoding.UTF8.GetBytes(value));
        }

        protected static string ByteArrayToHexString(byte[] value)
        {
            return value.Select(x => x.ToString("X2")).Aggregate((x1, x2) => x1 + x2);
        }

        protected void DumpJson(string contentPath, string content)
        {
#if DEBUG
            if (string.IsNullOrWhiteSpace(contentPath))
                return;
            try
            {
                var path = Path.Combine(LogName, Path.ChangeExtension(contentPath, ".json"));
                if (!Directory.Exists(LogName))
                    Directory.CreateDirectory(LogName);
               File.WriteAllText(path, content);
            }
            catch (Exception ex)
            {
                Debug.Print(ex.ToString());
            }
#endif
        }

        protected string LoadJson(string contentPath)
        {
            if (string.IsNullOrWhiteSpace(contentPath))
                return null;
            try
            {
                var path = Path.Combine(LogName, Path.ChangeExtension(contentPath, ".json"));
                if (!File.Exists(path))
                    return null;
                return File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                Debug.Print(ex.ToString());
                return null;
            }
        }

        internal void InitializeLog()
        {
            if (Log == null)
            {
                log4net.Config.XmlConfigurator.Configure();
                Log = log4net.LogManager.GetLogger(LogName);
            }
        }

        internal HashAlgorithm CreateHash(string hashFilePath, Type hashType)
        {
            if (hashFilePath != null && hashType != null && LoadApiKeys(hashFilePath))
            {
                return Activator.CreateInstance(hashType, ApiSecret.ToByteArray()) as HashAlgorithm;
            }
            return null;
        }

        protected void UpdateWeight(int w)
        {
            if (DateTime.Now >= weightDueTime)
            {
                Interlocked.Exchange(ref weightLastMinute, w);
                weightDueTime = DateTime.Now.AddMinutes(1);
            }
            else
            {
                Interlocked.Add(ref weightLastMinute, w);
            }
        }

        public int Weight => weightLastMinute;
        public TimeSpan WeightReset => weightDueTime - DateTime.Now;
        private int weightLastMinute;
        private DateTime weightDueTime;
    }

    public class ApiResult<T>
    {
        /// <summary>
        /// The data returned by the call
        /// </summary>
        public T Data { get; internal set; }
        /// <summary>
        /// An error if the call didn't succeed
        /// </summary>
        public ApiError Error { get; internal set; }
        /// <summary>
        /// Whether the call was successful
        /// </summary>
        public bool Success => Error == null;
        /// <summary>
        /// How many time was used by request
        /// </summary>
        public double ElapsedMilliseconds { get; }

        public ApiResult(T data, ApiError error, double elapsed = 0)
        {
            Data = data;
            Error = error;
            ElapsedMilliseconds = elapsed;
        }
    }

    public class ApiError
    {
        public int Code { get; set; }
        public string Msg { get; set; }

        public ApiError(int code, string message)
        {
            Code = code;
            Msg = message;
        }

        public ApiError()
        { }

        public override string ToString()
        {
            return $"{Code}: {Msg}";
        }
    }

    public static class DateTimeHelpers
    {
        public static long ToUnixTimestamp(this DateTime time)
        {
            return (long)(time - new DateTime(1970, 1, 1)).TotalMilliseconds;
        }

        public static DateTime FromUnixTimestamp(this long timestamp, bool convertToLocalTime = true)
        {
            var result = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).DateTime;
            return convertToLocalTime ? result.ToLocalTime() : result;
        }

        public static DateTime FromUnixSeconds(this long timestamp, bool convertToLocalTime = true)
        {
            var result = DateTimeOffset.FromUnixTimeSeconds(timestamp).DateTime;
            return convertToLocalTime ? result.ToLocalTime() : result;
        }

        public static long ToUnixSeconds(this DateTime time)
        {
            return (long)(time - new DateTime(1970, 1, 1)).TotalSeconds;
        }
    }
}
