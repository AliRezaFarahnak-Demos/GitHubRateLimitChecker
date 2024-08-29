using System.Web; // Add this for HttpUtility  
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

namespace GitHubRateLimitChecker
{
    class Program
    {
        private static readonly string ClientId = "clientid"; // Replace with your actual Client ID  
        private static readonly string AppName = "name";
        private static readonly string Scope = "read:user"; // Define your scope  

        static async Task Main(string[] args)
        {
            var allLogs = new List<RateLimitLog>();

            try
            {
                // Step 1: Request device and user verification codes  
                var deviceAuthResponse = await RequestDeviceAuthorization();
                Console.WriteLine($"Please visit {deviceAuthResponse.VerificationUri} and enter the code: {deviceAuthResponse.UserCode}");

                // Step 2: Poll for user authorization  
                var token = await PollForUserAuthorization(deviceAuthResponse.DeviceCode, deviceAuthResponse.Interval);
                Console.WriteLine($"Making API requests...");

                // Make 10 API requests to a common endpoint  
                for (int i = 0; i < 10; i++)
                {
                    await MakeApiRequest(token);
                }

                Console.WriteLine($"Fetching rate limit...");
                var rateLimit = await GetRateLimit(token);
                var logs = LogRateLimit(AppName, rateLimit);
                allLogs.AddRange(logs);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error fetching data: {e.Message}");
                Console.WriteLine(e.StackTrace); // Print the stack trace for debugging  
            }

            if (allLogs.Count == 0)
            {
                Console.WriteLine("No data to display.");
                return;
            }

            // Save the logs to a JSON file  
            var json = JsonConvert.SerializeObject(allLogs, Formatting.Indented);
            File.WriteAllText("rate_limit_logs.json", json);
            Console.WriteLine("Rate limit data saved to rate_limit_logs.json");
        }

        private static async Task<DeviceAuthResponse> RequestDeviceAuthorization()
        {
            using var client = new HttpClient();
            var requestBody = new Dictionary<string, string>
            {
                { "client_id", ClientId },
                { "scope", Scope }
            };

            var response = await client.PostAsync("https://github.com/login/device/code", new FormUrlEncodedContent(requestBody));
            response.EnsureSuccessStatusCode();
            var responseBody = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"Response from Device Authorization: {responseBody}"); // Log the raw response  

            var parsedResponse = HttpUtility.ParseQueryString(responseBody);
            return new DeviceAuthResponse
            {
                DeviceCode = parsedResponse["device_code"],
                UserCode = parsedResponse["user_code"],
                VerificationUri = parsedResponse["verification_uri"],
                ExpiresIn = int.Parse(parsedResponse["expires_in"]),
                Interval = int.Parse(parsedResponse["interval"])
            };
        }

        private static async Task<string> PollForUserAuthorization(string deviceCode, int interval)
        {
            using var client = new HttpClient();
            string token = null;
            var expiresIn = 900; // Expiration time in seconds  
            var startTime = DateTime.UtcNow;
            var endTime = startTime.AddSeconds(expiresIn);

            Console.WriteLine($"Polling started at: {startTime}");
            Console.WriteLine($"Polling will end at: {endTime}");

            // Initial wait time before starting the polling  
            await Task.Delay(5000); // Wait 5 seconds before starting to poll  

            while (DateTime.UtcNow < endTime)
            {
                await Task.Delay(interval * 1000); // Wait for the specified interval  

                var requestBody = new Dictionary<string, string>
                {
                    { "client_id", ClientId },
                    { "device_code", deviceCode },
                    { "grant_type", "urn:ietf:params:oauth:grant-type:device_code" }
                };

                var response = await client.PostAsync("https://github.com/login/oauth/access_token", new FormUrlEncodedContent(requestBody));
                var responseBody = await response.Content.ReadAsStringAsync();

                // Check if the response is not in JSON format  
                if (response.Content.Headers.ContentType.MediaType == "application/x-www-form-urlencoded")
                {
                    var parsedResponse = HttpUtility.ParseQueryString(responseBody);

                    if (parsedResponse["access_token"] != null)
                    {
                        token = parsedResponse["access_token"];
                        break;
                    }

                    var error = parsedResponse["error"];
                    Console.WriteLine($"Error while polling: {error}"); // Log any errors  

                    if (error == "authorization_pending")
                    {
                        continue; // User hasn't entered the code yet  
                    }
                    else if (error == "slow_down")
                    {
                        interval += 5; // Increase the interval as per the error response  
                        Console.WriteLine($"Slowing down, new interval: {interval} seconds");
                    }
                    else
                    {
                        throw new Exception($"Error during polling: {error}");
                    }
                }
                else
                {
                    throw new Exception("Unexpected response format.");
                }
            }

            if (token == null)
            {
                throw new Exception("User did not authorize in time.");
            }

            return token;
        }

        private static async Task MakeApiRequest(string token)
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"token {token}");
            client.DefaultRequestHeaders.Add("User-Agent", "GitHubRateLimitChecker");
            var response = await client.GetAsync("https://api.github.com/user");
            response.EnsureSuccessStatusCode();
            var responseBody = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"API Response: {responseBody}");
        }

        private static async Task<JObject> GetRateLimit(string token)
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"token {token}");
            client.DefaultRequestHeaders.Add("User-Agent", "GitHubRateLimitChecker");
            var response = await client.GetAsync("https://api.github.com/rate_limit");
            response.EnsureSuccessStatusCode();
            var responseBody = await response.Content.ReadAsStringAsync();
            return JObject.Parse(responseBody);
        }

        private static List<RateLimitLog> LogRateLimit(string appName, JObject rateLimit)
        {
            var rateLimitData = rateLimit["resources"];
            var timestamp = DateTime.Now;
            var logData = new List<RateLimitLog>();

            foreach (var resource in rateLimitData)
            {
                var data = resource.First;
                logData.Add(new RateLimitLog
                {
                    Timestamp = timestamp,
                    App = appName,
                    Resource = resource.Path,
                    Limit = (int)data["limit"],
                    Remaining = (int)data["remaining"],
                    Reset = DateTimeOffset.FromUnixTimeSeconds((long)data["reset"]).DateTime
                });
            }

            return logData;
        }
    }

    public class DeviceAuthResponse
    {
        public string DeviceCode { get; set; }
        public string UserCode { get; set; }
        public string VerificationUri { get; set; }
        public int ExpiresIn { get; set; }
        public int Interval { get; set; }
    }

    public class RateLimitLog
    {
        public DateTime Timestamp { get; set; }
        public string App { get; set; }
        public string Resource { get; set; }
        public int Limit { get; set; }
        public int Remaining { get; set; }
        public DateTime Reset { get; set; }
    }
}
