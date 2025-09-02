using Azure;
using System;
using System.Threading.Tasks;

namespace CLDV6212_GROUP_04.Service
{
    public static class RetryPolicyExtensions
    {
        public static async Task<T> WithRetryAsync<T>(
            this Task<T> operation,
            int maxRetries = 3,
            TimeSpan? baseDelay = null)
        {
            var delay = baseDelay ?? TimeSpan.FromSeconds(1);
            var attempts = 0;

            while (attempts < maxRetries)
            {
                try
                {
                    return await operation;
                }
                catch (RequestFailedException ex) when (IsTransientError(ex) && attempts < maxRetries - 1)
                {
                    attempts++;
                    var currentDelay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * Math.Pow(2, attempts - 1));
                    await Task.Delay(currentDelay);
                }
                catch
                {
                    throw; // Re-throw non-transient exceptions or final attempt
                }
            }

            // This should never be reached due to the throw above, but satisfies compiler
            return await operation;
        }

        public static async Task WithRetryAsync(
            this Task operation,
            int maxRetries = 3,
            TimeSpan? baseDelay = null)
        {
            var delay = baseDelay ?? TimeSpan.FromSeconds(1);
            var attempts = 0;

            while (attempts < maxRetries)
            {
                try
                {
                    await operation;
                    return;
                }
                catch (RequestFailedException ex) when (IsTransientError(ex) && attempts < maxRetries - 1)
                {
                    attempts++;
                    var currentDelay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * Math.Pow(2, attempts - 1));
                    await Task.Delay(currentDelay);
                }
                catch
                {
                    throw; // Re-throw non-transient exceptions or final attempt
                }
            }
        }

        private static bool IsTransientError(RequestFailedException ex)
        {
            // Common transient error status codes
            return ex.Status == 500 || // Internal Server Error
                   ex.Status == 502 || // Bad Gateway
                   ex.Status == 503 || // Service Unavailable
                   ex.Status == 504 || // Gateway Timeout
                   ex.Status == 429;   // Too Many Requests
        }
    }
}