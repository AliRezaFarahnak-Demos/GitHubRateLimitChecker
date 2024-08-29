using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GitHubRateLimitChecker;

class Program
{
    private static readonly string RedirectUri = "http://localhost:5000/callback"; // Redirect URI for your web server  
    private static readonly string Scope = "read:user";
    private static string AuthorizationCode;

    static async Task Main(string[] args)
    {
        var credentialsJson = File.ReadAllText("credentials.json");
        var clientCredentialsList = JsonConvert.DeserializeObject<CredentialsContainer>(credentialsJson).ClientCredentialsList;

        if (clientCredentialsList == null || clientCredentialsList.Count == 0)
        {
            Console.WriteLine("No credentials found.");
            return;
        }

        var allLogs = new List<RateLimitLog>();
        var builder = WebApplication.CreateBuilder(args);
        var app = builder.Build();

        app.MapGet("/", async context =>
        {
            var authorizationUrl = GetAuthorizationUrl(clientCredentialsList[0].ClientId);
            await context.Response.WriteAsync($"Please visit the following URL to authorize the application: {authorizationUrl}");
        });

        app.MapGet("/callback", async context =>
        {
            AuthorizationCode = context.Request.Query["code"];
            await context.Response.WriteAsync("Authorization successful! You can close this window.");
        });

        var serverTask = app.RunAsync("http://localhost:5000");

        foreach (var credentials in clientCredentialsList)
        {
            try
            {
                Console.WriteLine($"Please visit the following URL to authorize the application: {GetAuthorizationUrl(credentials.ClientId)}");
                // Wait for the authorization code to be set by the callback  
                while (string.IsNullOrEmpty(AuthorizationCode))
                {
                    await Task.Delay(1000);
                }

                // Step 3: Exchange authorization code for access token  
                var token = await ExchangeAuthorizationCodeForToken(credentials.ClientId, credentials.ClientSecret, AuthorizationCode);
                Console.WriteLine($"Making API requests for {credentials.AppName}...");

                // Make 10 API requests to a common endpoint  
                for (int i = 0; i < 10; i++)
                {
                    await MakeApiRequest(token);
                }

                Console.WriteLine($"Fetching rate limit for {credentials.AppName}...");
                var rateLimit = await GetRateLimit(token);
                var logs = LogRateLimit(credentials.AppName, rateLimit);
                allLogs.AddRange(logs);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error fetching data for {credentials.AppName}: {e.Message}");
                Console.WriteLine(e.StackTrace); // Print the stack trace for debugging  
            }

            // Reset the authorization code for the next iteration  
            AuthorizationCode = null;
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

    private static string GetAuthorizationUrl(string clientId)
    {
        return $"https://github.com/login/oauth/authorize?client_id={clientId}&redirect_uri={RedirectUri}&scope={Scope}";
    }

    private static async Task<string> ExchangeAuthorizationCodeForToken(string clientId, string clientSecret, string authorizationCode)
    {
        using var client = new HttpClient();
        var requestBody = new Dictionary<string, string>
        {
            { "client_id", clientId },
            { "client_secret", clientSecret },
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

public record struct CredentialsContainer(List<ClientCredentials> ClientCredentialsList);
public record struct ClientCredentials(string ClientId, string ClientSecret, string AppName);
public record struct RateLimitLog(DateTime Timestamp, string App, string Resource, int Limit, int Remaining, DateTime Reset);
