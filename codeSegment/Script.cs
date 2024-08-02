#nullable disable
using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class Script : ScriptBase
{
    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        // Log the start of the action with the current UTC time
        Context.Logger.LogInformation($"Action started: {DateTime.UtcNow}");

        // Initialize sessionId as an empty string
        string sessionId = "";
        // Define the API version to be used in the request
        string version = "v24.1";

        // Check if the current operation is one of the specified types
        if ((new[] { "VqlQuery", "ListItemsAtPath", "DownloadItemContent" }).Contains(Context.OperationId))
        {
            // Modify the request URI to include the API version in the path
            Context.Request.RequestUri = new UriBuilder(Context.Request.RequestUri)
            {
                Path = Uri.UnescapeDataString(Context.Request.RequestUri.AbsolutePath)
                    .Replace("//", "/") // Ensure no double slashes in the path
                    .Replace("/api/", $"/api/{version}/") // Insert the API version
            }.Uri;

            // Attempt to retrieve the session ID for the current version
            HttpResponseMessage sessionIdResponse = await GetSessionId(version).ConfigureAwait(false);

            // Check if the session ID was not successfully retrieved
            if (sessionIdResponse.StatusCode == HttpStatusCode.OK)
            {
                // Extract the session ID from the response content
                sessionId = await sessionIdResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                // Log the successful retrieval of the session ID
                Context.Logger.LogInformation($"Got sessionId: {sessionId}");
            }
            else
            {
                // Return a BadRequest response indicating failure to get the session ID
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.BadRequest,
                    Content = new StringContent("ERROR: Failed to get sessionId. Check the logs for details.")
                };
            }
        }

        // Check if the operation ID is "VqlQuery"
        if (Context.OperationId == "VqlQuery")
        {
            // Handle the VqlQuery operation and return its response
            return await HandleQueryOperation(sessionId).ConfigureAwait(false);
        }

        // Check if the operation ID is "ListItemsAtPath"
        if (Context.OperationId == "ListItemsAtPath")
        {
            // Handle the ListItemsAtPath operation and return its response
            return await HandleListItemsOperation(sessionId).ConfigureAwait(false);
        }

        // Check if the operation ID is "DownloadItemContent"
        if (Context.OperationId == "DownloadItemContent")
        {
            // Handle the DownloadItemContent operation and return its response
            return await HandleDownloadItemContentOperation(sessionId).ConfigureAwait(false);
        }

        // If the operation ID does not match any known operations, return a BadRequest response
        return new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.BadRequest,
            Content = new StringContent($"Unknown operation ID: {Context.OperationId}")
        };
    }
    // Define an asynchronous method to retrieve a session ID for authentication
    private async Task<HttpResponseMessage> GetSessionId(string version)
    {
        // Log the attempt to get a session ID
        Context.Logger.LogInformation($"Getting sessionId");
        try
        {
            // Extract the host domain from the request URI
            var hostDomain = Context.Request.RequestUri.Host;
            // Log the host domain
            Context.Logger.LogInformation($"host: {hostDomain}");
            // Construct the authentication request URL
            string requestUrl = $"https://{hostDomain}/api/{version}/auth";
            // Create the HTTP POST request for authentication
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
            // Set the Accept header to expect JSON responses
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            // Attempt to retrieve the username from the request headers
            Context.Request.Headers.TryGetValues("un", out var unValue);
            // Check if the usernme is present in the headers, then remove it
            if (unValue.Any()) Context.Request.Headers.Remove("un");
            // Attempt to retrieve the password from the request headers
            Context.Request.Headers.TryGetValues("pw", out var pwValue);
            // Check if the password is present in the headers, then remove it
            if (pwValue.Any()) Context.Request.Headers.Remove("pw");
            // Add the username and password to the request body
            var body = new List<KeyValuePair<string, string>>
            {
                new("username", unValue.FirstOrDefault()),
                new("password", pwValue.FirstOrDefault())
            };
            // Set the request content
            request.Content = new FormUrlEncodedContent(body);
            // Send the request and await the response
            HttpResponseMessage response = await Context.SendAsync(request, CancellationToken);
            // Ensure the response status code indicates success
            response.EnsureSuccessStatusCode();
            // Read the response content as a string
            var responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            // Parse the response to extract the session ID
            //return JObject.Parse(responseString)["sessionId"]?.ToString() ?? "";
            var responseStatus = JObject.Parse(responseString)["responseStatus"]?.ToString() ?? "";
            Context.Logger.LogInformation($"GetSessionId responseStatus: {responseStatus}");
            if (responseStatus == "SUCCESS" || responseStatus == "WARNING")
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JObject.Parse(responseString)["sessionId"]?.ToString() ?? "")
                };
            else
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent(responseString ?? "")
                };
        }
        catch (Exception ex) // Catch any exceptions that occur during the process
        {
            // Log the error
            Context.Logger.LogError($"ERROR: {ex.ToString()}");
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(ex.ToString())
            };
        }
    }

    // Define an asynchronous method to handle the "Query Operation"
    private async Task<HttpResponseMessage> HandleQueryOperation(string sessionId)
    {
        // Log the execution of the Query Operation
        Context.Logger.LogInformation($"Executing Query Operation");
        try
        {
            // Log the request URI for debugging
            Context.Logger.LogInformation($"requestUri: {JsonConvert.SerializeObject(Context.Request.RequestUri)}");
            // Add the session ID as an Authorization header to the request
            Context.Request.Headers.TryAddWithoutValidation("Authorization", sessionId);
            // Read the request content as a string asynchronously
            string contentString = await (Context.Request.Content?.ReadAsStringAsync() ?? Task.FromResult<string>(null!)).ConfigureAwait(false);
            // Deserialize the request content to a JSON object
            var contentJson = JsonConvert.DeserializeObject<JObject>(contentString);
            // Extract the query from the JSON object
            string query = contentJson?["q"]?.ToString() ?? string.Empty;
            // Prepare the query for the request body
            var body = new List<KeyValuePair<string, string>> { new("q", query) };
            // Set the request content with the prepared query
            Context.Request.Content = new FormUrlEncodedContent(body);

            // Send the request and await the response
            HttpResponseMessage response = await Context.SendAsync(Context.Request, CancellationToken);
            // Ensure the response status code indicates success
            response.EnsureSuccessStatusCode();
            // Read the response content as a string asynchronously
            var responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            // Deserialize the response content to a JSON object
            var responseJson = JObject.Parse(responseString);
            var responseStatus = responseJson["responseStatus"]?.ToString() ?? "";
            Context.Logger.LogInformation($"Query responseStatus: {responseStatus}");
            if (responseStatus != "SUCCESS" && responseStatus != "WARNING")
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent(responseString ?? "")
                };

            // Extract the "data" part of the response
            var data = responseJson["data"];
            // Initialize a variable to track the next page URL
            var next = responseJson["responseDetails"]?["next_page"]?.ToString() ?? "";
            // Loop through pagination if there's a next page
            while (next != "")
            {
                // Log the next page URL for debugging
                Context.Logger.LogInformation($"next: {next}");
                // Prepare the request for the next page
                var requestUri = new Uri($"https://{Context.Request.RequestUri.Host}{next}");
                var nextRequest = new HttpRequestMessage(Context.Request.Method, requestUri);
                // Add the session ID as an Authorization header to the next request
                nextRequest.Headers.TryAddWithoutValidation("Authorization", sessionId);
                // Send the next request and await the response
                response = await Context.SendAsync(nextRequest, CancellationToken);
                // Ensure the response status code indicates success
                response.EnsureSuccessStatusCode();
                // Read the next response content as a string asynchronously
                responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                // Deserialize the next response content to a JSON object
                responseJson = JObject.Parse(responseString);
                responseStatus = responseJson["responseStatus"]?.ToString() ?? "";
                Context.Logger.LogInformation($"Query Next responseStatus: {responseStatus}");
                if (responseStatus != "SUCCESS" && responseStatus != "WARNING")
                    return new HttpResponseMessage(HttpStatusCode.BadRequest)
                    {
                        Content = new StringContent(responseString ?? "")
                    };
                // Extract the "data" part of the next response
                var nextData = responseJson["data"] ?? Enumerable.Empty<JToken>();
                // Concatenate the current data with the next data
                data = JToken.FromObject(data?.Concat(nextData) ?? Enumerable.Empty<JToken>());
                // Update the next page URL
                next = responseJson["responseDetails"]?["next_page"]?.ToString() ?? "";
            }
            // Update the "data" part of the original response JSON with the concatenated data
            responseJson["data"] = data;
            responseJson["request-host-domain"] = Context.Request.RequestUri.Host;
            // Set the response content with the updated JSON
            response.Content = CreateJsonContent(responseJson.ToString());
            // Return the modified response
            return response;
        }
        catch (Exception ex) // Catch any exceptions that occur during the process
        {
            // Log the error
            Context.Logger.LogError($"ERROR: {ex.ToString()}");
            // Return a BadRequest response with the error message
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(ex.ToString())
            };
        }
    }

    // Define an asynchronous method to handle the "List Items Operation"
    private async Task<HttpResponseMessage> HandleListItemsOperation(string sessionId)
    {
        // Log the execution of the List Items Operation
        Context.Logger.LogInformation($"Executing List Items Operation");
        try
        {
            // Add the session ID as an Authorization header to the request
            Context.Request.Headers.TryAddWithoutValidation("Authorization", sessionId);
            // Modify the request URI to remove duplicate slashes
            Context.Request.RequestUri = new UriBuilder(Context.Request.RequestUri)
            {
                Path = Uri.UnescapeDataString(Context.Request.RequestUri.AbsolutePath).Replace("//", "/")
            }.Uri;
            // Send the request and await the response
            HttpResponseMessage response = await Context.SendAsync(Context.Request, CancellationToken);
            // Ensure the response status code indicates success
            response.EnsureSuccessStatusCode();
            // Read the response content as a string asynchronously
            var responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            // Deserialize the response content to a JSON object
            var responseJson = JObject.Parse(responseString);
            var responseStatus = responseJson["responseStatus"]?.ToString() ?? "";
            Context.Logger.LogInformation($"List Items responseStatus: {responseStatus}");
            if (responseStatus != "SUCCESS" && responseStatus != "WARNING")
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent(responseString ?? "")
                };

            // Extract the "data" part of the response
            var data = responseJson["data"];
            // Initialize a variable to track the next page URL
            var next = responseJson["responseDetails"]?["next_page"]?.ToString() ?? "";
            // Loop through pagination if there's a next page
            while (next != "")
            {
                // Log the next page URL for debugging
                Context.Logger.LogInformation($"next: {next}");
                // Prepare the request for the next page
                var requestUri = new Uri($"https://{Context.Request.RequestUri?.Host}{next}");
                var request = new HttpRequestMessage(Context.Request.Method, requestUri);
                // Add the session ID as an Authorization header to the next request
                request.Headers.TryAddWithoutValidation("Authorization", sessionId);
                // Send the next request and await the response
                response = await Context.SendAsync(request, CancellationToken);
                // Ensure the response status code indicates success
                response.EnsureSuccessStatusCode();
                // Read the next response content as a string asynchronously
                responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                // Deserialize the next response content to a JSON object
                responseJson = JObject.Parse(responseString);
                responseStatus = responseJson["responseStatus"]?.ToString() ?? "";
                Context.Logger.LogInformation($"List Items Next responseStatus: {responseStatus}");
                if (responseStatus != "SUCCESS" && responseStatus != "WARNING")
                    return new HttpResponseMessage(HttpStatusCode.BadRequest)
                    {
                        Content = new StringContent(responseString ?? "")
                    };

                // Extract the "data" part of the next response
                var newdata = responseJson["data"] ?? Enumerable.Empty<JToken>();
                // Concatenate the current data with the next data
                data = JToken.FromObject(data?.Concat(newdata) ?? Enumerable.Empty<JToken>());
                // Update the next page URL
                next = responseJson["responseDetails"]?["next_page"]?.ToString() ?? "";
            }
            // Update the "data" part of the original response JSON with the concatenated data
            responseJson["data"] = data;
            responseJson["request-host-domain"] = Context.Request.RequestUri.Host;
            // Set the response content with the updated JSON
            response.Content = CreateJsonContent(responseJson.ToString());
            // Return the modified response
            return response;
        }
        catch (Exception ex) // Catch any exceptions that occur during the process
        {
            // Log the error
            Context.Logger.LogError($"ERROR: {ex.ToString()}");
            // Return a BadRequest response with the error message
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(ex.ToString())
            };
        }
    }

    // Define an asynchronous method to handle the "Download Item Content Operation"
    private async Task<HttpResponseMessage> HandleDownloadItemContentOperation(string sessionId)
    {
        // Log the execution of the Download Item Content Operation
        Context.Logger.LogInformation($"Executing Download Item Content Operation");
        try
        {
            // Add the session ID as an Authorization header to the request
            Context.Request.Headers.TryAddWithoutValidation("Authorization", sessionId);
            // Modify the request URI to remove duplicate slashes and replace URL-encoded characters
            Context.Request.RequestUri = new UriBuilder(Context.Request.RequestUri)
            {
                Path = Uri.UnescapeDataString(Context.Request.RequestUri.AbsolutePath)
                    .Replace("//", "/")
                    .Replace("%3A", ":")
            }.Uri;
            // Send the request and await the response
            HttpResponseMessage response = await Context.SendAsync(Context.Request, CancellationToken);
            // Ensure the response status code indicates success
            response.EnsureSuccessStatusCode();
            var responseType = response.Headers.GetValues("responseType").FirstOrDefault();
            Context.Logger.LogInformation($"Download Item Content response type: {responseType}");
            // var responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            // var responseJson = JObject.Parse(responseString);
            // var responseStatus = responseJson["responseStatus"]?.ToString() ?? "";
            // Context.Logger.LogInformation($"Get Item Content responseStatus: {responseStatus}");
            // if (responseStatus != "SUCCESS" && responseStatus != "WARNING")
            //     return new HttpResponseMessage(HttpStatusCode.BadRequest)
            //     {
            //         Content = new StringContent(responseString ?? "")
            //     };

            response.Headers.Add("sessionId", sessionId);
            // Return the response
            return response;
        }
        catch (Exception ex) // Catch any exceptions that occur during the process
        {
            // Log the error
            Context.Logger.LogError($"ERROR: {ex.ToString()}");
            // Return a BadRequest response with the error message
            return new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.BadRequest,
                Content = new StringContent(ex.ToString())
            };
        }
    }
}