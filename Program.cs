using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GitHubRateLimitChecker
{
    class Program
    {
        private static readonly string ClientId = ""; // Replace with your actual Client ID  
        private static readonly string ClientSecret = ""; // Replace with your actual Client Secret  
        private static readonly string RedirectUri = "http://localhost:5000/callback"; // Redirect URI for your web server  
        private static readonly string AppName = "rate-limit-app";
        private static readonly string Scope = "read:user";
        private static string AuthorizationCode;

        static async Task Main(string[] args)
        {
            var allLogs = new List<RateLimitLog>();

            var builder = WebApplication.CreateBuilder(args);
            var app = builder.Build();

            app.MapGet("/", async context =>
            {
                var authorizationUrl = GetAuthorizationUrl();
                await context.Response.WriteAsync($"Please visit the following URL to authorize the application: {authorizationUrl}");
            });

            app.MapGet("/callback", async context =>
            {
                AuthorizationCode = context.Request.Query["code"];
                await context.Response.WriteAsync("Authorization successful! You can close this window.");
            });

            var serverTask = app.RunAsync("http://localhost:5000");

            try
            {
                Console.WriteLine($"Please visit the following URL to authorize the application: {GetAuthorizationUrl()}");

                // Wait for the authorization code to be set by the callback  
                while (string.IsNullOrEmpty(AuthorizationCode))
                {
                    await Task.Delay(1000);
                }

                // Step 3: Exchange authorization code for access token  
                var token = await ExchangeAuthorizationCodeForToken(AuthorizationCode);

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

            await serverTask;
        }

        private static string GetAuthorizationUrl()
        {
            return $"https://github.com/login/oauth/authorize?client_id={ClientId}&redirect_uri={RedirectUri}&scope={Scope}";
        }

        private static async Task<string> ExchangeAuthorizationCodeForToken(string authorizationCode)
        {
            using var client = new HttpClient();
            var requestBody = new Dictionary<string, string>
            {
                { "client_id", ClientId },
                { "client_secret", ClientSecret },
                { "code", authorizationCode },
                { "redirect_uri", RedirectUri }
            };

            var response = await client.PostAsync("https://github.com/login/oauth/access_token", new FormUrlEncodedContent(requestBody));
            response.EnsureSuccessStatusCode();
            var responseBody = await response.Content.ReadAsStringAsync();
            var parsedResponse = System.Web.HttpUtility.ParseQueryString(responseBody);
            return parsedResponse["access_token"];
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
