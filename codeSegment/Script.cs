#nullable disable
using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        Context.Logger.LogInformation($"Action started: {DateTime.UtcNow}");
        if (Context.OperationId == "vvQuery")
        {
            string sessionId = await GetSessionId().ConfigureAwait(false);
            return await HandleQueryOperation(sessionId).ConfigureAwait(false);
        }

        if (Context.OperationId == "vvListItemsAtPath")
        {
            string sessionId = await GetSessionId().ConfigureAwait(false);
            return await HandleListItemsOperation(sessionId).ConfigureAwait(false);
        }

        if (Context.OperationId == "vvDownloadItemContent")
        {
            string sessionId = await GetSessionId().ConfigureAwait(false);
            return await HandleDownloadItemContentOperation(sessionId).ConfigureAwait(false);
        }

        // Handle an invalid operation ID
        HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.BadRequest);
        response.Content = CreateJsonContent($"Unknown operation ID: {Context.OperationId}");
        return response;
    }

    private async Task<string> GetSessionId()
    {
        Context.Logger.LogInformation($"Getting sessionId");
        try
        {
            //dev
            //string username = "harmony.integration@sb-takeda.com";
            //string password = "Mediv@2030";
            //prod
            string username = "harmony.integration@takeda.com";
            string password = "Mediv@2024";
            string requestUrl = $"https://{Context.Request.RequestUri?.Host}/api/v23.1/auth";
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            var body = new List<KeyValuePair<string, string>>
            {
                new("username", username),
                new("password", password)
            };
            request.Content = new FormUrlEncodedContent(body);
            HttpResponseMessage response = await Context.SendAsync(request, CancellationToken);
            response.EnsureSuccessStatusCode();
            var responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JObject.Parse(responseString)["sessionId"]?.ToString() ?? "";
        }
        catch (Exception ex)
        {
            Context.Logger.LogError($"ERROR: {ex.ToString()}");
            return "";
        }
    }

    private async Task<HttpResponseMessage> HandleQueryOperation(string sessionId)
    {
        Context.Logger.LogInformation($"Executing Query Operation");
        try
        {
            Context.Request.Headers.TryAddWithoutValidation("Authorization", sessionId);
            string contentString = await (Context.Request.Content?.ReadAsStringAsync() ?? Task.FromResult<string>(null!)).ConfigureAwait(false);
            var contentJson = JsonConvert.DeserializeObject<JObject>(contentString);
            string query = contentJson?["q"]?.ToString() ?? string.Empty;
            //Context.Logger.LogInformation($"query: {query}");
            var body = new List<KeyValuePair<string, string>> { new("q", query) };
            Context.Request.Content = new FormUrlEncodedContent(body);

            HttpResponseMessage response = await Context.SendAsync(Context.Request, CancellationToken);
            response.EnsureSuccessStatusCode();
            var responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var responseJson = JObject.Parse(responseString);

            var data = responseJson["data"];
            var next = responseJson["responseDetails"]?["next_page"]?.ToString() ?? "";
            while (next != "")
            {
                Context.Logger.LogInformation($"next: {next}");
                var requestUri = new Uri($"https://{Context.Request.RequestUri.Host}{next}");
                var nextRequest = new HttpRequestMessage(Context.Request.Method, requestUri);
                nextRequest.Headers.TryAddWithoutValidation("Authorization", sessionId);
                response = await Context.SendAsync(nextRequest, CancellationToken);
                response.EnsureSuccessStatusCode();
                responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                responseJson = JObject.Parse(responseString);
                var nextData = responseJson["data"] ?? Enumerable.Empty<JToken>();
                data = JToken.FromObject(data?.Concat(nextData) ?? Enumerable.Empty<JToken>());
                next = responseJson["responseDetails"]?["next_page"]?.ToString() ?? "";
            }
            responseJson["data"] = data;
            //Context.Logger.LogInformation($"data: {JsonConvert.SerializeObject(data)}");
            response.Content = CreateJsonContent(responseJson.ToString());
            return response;
        }
        catch (Exception ex)
        {
           Context.Logger.LogError($"ERROR: {ex.ToString()}");
            return new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.BadRequest,
                Content = new StringContent(ex.ToString())
            };
        }
    }

    private async Task<HttpResponseMessage> HandleListItemsOperation(string sessionId)
    {
        Context.Logger.LogInformation($"Executing List Items Operation");
        try
        {
            Context.Request.Headers.TryAddWithoutValidation("Authorization", sessionId);
            Context.Request.RequestUri = new UriBuilder(Context.Request.RequestUri)
            {
                Path = Uri.UnescapeDataString(Context.Request.RequestUri.AbsolutePath).Replace("//", "/")
            }.Uri;
            HttpResponseMessage response = await Context.SendAsync(Context.Request, CancellationToken);
            response.EnsureSuccessStatusCode();
            var responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var responseJson = JObject.Parse(responseString);

            var data = responseJson["data"];
            var next = responseJson["responseDetails"]?["next_page"]?.ToString() ?? "";
            while (next != "")
            {
                Context.Logger.LogInformation($"next: {next}");
                var requestUri = new Uri($"https://{Context.Request.RequestUri?.Host}{next}");
                var request = new HttpRequestMessage(Context.Request.Method, requestUri);
                request.Headers.TryAddWithoutValidation("Authorization", sessionId);
                response = await Context.SendAsync(request, CancellationToken);
                response.EnsureSuccessStatusCode();
                responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                responseJson = JObject.Parse(responseString);

                var newdata = responseJson["data"] ?? Enumerable.Empty<JToken>();
                data = JToken.FromObject(data?.Concat(newdata) ?? Enumerable.Empty<JToken>());
                next = responseJson["responseDetails"]?["next_page"]?.ToString() ?? "";
            }
            responseJson["data"] = data;
            //Context.Logger.LogInformation($"data: {JsonConvert.SerializeObject(data)}");
            response.Content = CreateJsonContent(responseJson.ToString());
            return response;
        }
        catch (Exception ex)
        {
           Context.Logger.LogError($"ERROR: {ex.ToString()}");
           return new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.BadRequest,
                Content = new StringContent(ex.ToString())
            };
        }
    }

    private async Task<HttpResponseMessage> HandleDownloadItemContentOperation(string sessionId)
    {
        Context.Logger.LogInformation($"Executing Download Item Content Operation");
        try
        {
            Context.Request.Headers.TryAddWithoutValidation("Authorization", sessionId);
            Context.Request.RequestUri = new UriBuilder(Context.Request.RequestUri)
            {
                Path = Uri.UnescapeDataString(Context.Request.RequestUri.AbsolutePath)
                    .Replace("//", "/")
                    .Replace("%3A", ":")
            }.Uri;
            //Context.Logger.LogInformation($"RequestUri: {Context.Request.RequestUri}");
            HttpResponseMessage response = await Context.SendAsync(Context.Request, CancellationToken);
            response.EnsureSuccessStatusCode();
            return response;
        }
        catch (Exception ex)
        {
           Context.Logger.LogError($"ERROR: {ex.ToString()}");
           return new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.BadRequest,
                Content = new StringContent(ex.ToString())
            };
        }
    }
}