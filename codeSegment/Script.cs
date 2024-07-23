public class Script : ScriptBase
{
    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        Context.Logger.LogInformation($"started {DateTime.UtcNow}");
        // get parameters from connector
        string host = Context.Request.RequestUri.Host;
        string contentString = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var contentJson = JObject.Parse(contentString);
        var username = (string)contentJson["username"];
        var password = (string)contentJson["password"];
        var query = (string)contentJson["q"];

        // auth request
        var request = new HttpRequestMessage(HttpMethod.Post, $"https://{host}/api/v23.1/auth");
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        var body = new List<KeyValuePair<string, string>>();
        body.Add(new("username", (string)contentJson["username"]));
        body.Add(new("password", (string)contentJson["password"]));
        request.Content = new FormUrlEncodedContent(body);
        var response = await this.Context.SendAsync(request, this.CancellationToken);
        response.EnsureSuccessStatusCode();

        //extract session ID
        var responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var responseJson = JObject.Parse(responseString);
        var sessionId = (string)responseJson["sessionId"];

        // run query
        request = new HttpRequestMessage(HttpMethod.Post, $"https://{host}/api/v23.1/query");
        request.Headers.Add("Accept", "application/json");
        request.Headers.Add("Authorization", sessionId);
        body = new List<KeyValuePair<string, string>>();
        body.Add(new("q", (string)contentJson["q"]));
        request.Content = new FormUrlEncodedContent(body);
        response = await this.Context.SendAsync(request, this.CancellationToken);
        try {
            response.EnsureSuccessStatusCode();
   
            responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            responseJson = JObject.Parse(responseString);

            // determine if more data is available
            var data = responseJson["data"];
            var next = (string)responseJson["responseDetails"]["next_page"] ?? "";
            while (next != "") {
                request = new HttpRequestMessage(HttpMethod.Post, $"https://{host}{next}");
                request.Headers.Add("Accept", "application/json");
                request.Headers.Add("Authorization", sessionId);
                //request.Content = new FormUrlEncodedContent(body);
                response = await this.Context.SendAsync(request, this.CancellationToken);
                response.EnsureSuccessStatusCode();
                responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                responseJson = JObject.Parse(responseString);

                var newdata = responseJson["data"];
                data = JToken.FromObject(data.Concat(newdata));
                next = (string)responseJson["responseDetails"]["next_page"] ?? "";
            }
            responseJson["data"] = data;
            response.Content = CreateJsonContent(responseJson.ToString());
            return response;
        }
        catch {
            return new HttpResponseMessage{
                StatusCode = HttpStatusCode.BadRequest,
                RequestMessage = request,
                Content = response.Content
            };
        }
    }
}
