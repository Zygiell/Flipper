﻿using System.Net.WebSockets;
using System.Text.Json;
using System.Text;

var client = new HttpClient();
Console.WriteLine("PoE Flipper: live search spammer by ZGL feat. BTN");

Console.WriteLine("Do you want to use Config from config file? 1 - YES, any other - NO");
var choice = Console.ReadLine();
if (choice == "1")
{
    if (!LoadConfigFromFile("config.json"))
    {
        Console.WriteLine("Failed to load config from file, please enter configuration manually.");
        await PromptForConfiguration();
    }
}
else
{
    await PromptForConfiguration();
}

await ConnectAndMonitorWebSocket(Config.LeagueName, Config.SearchSuffix, Config.Cookie);

static async Task PromptForConfiguration()
{
    Console.WriteLine("League name case sensitive e.g. : Necropolis");
    Config.LeagueName = Console.ReadLine();
    Console.WriteLine("Search suffix");
    Config.SearchSuffix = Console.ReadLine();
    Console.WriteLine("Cookie from poe trade");
    Config.Cookie = Console.ReadLine();
    Config.Initialized = true;
}

static bool LoadConfigFromFile(string filePath)
{
    if (!File.Exists(filePath))
    {
        Console.WriteLine($"Config file not found: {filePath}");
        return false;
    }

    try
    {
        string json = File.ReadAllText(filePath);
        var configData = JsonSerializer.Deserialize<ConfigData>(json);
        if (configData != null)
        {
            Config.LeagueName = configData.LeagueName;
            Config.SearchSuffix = configData.SearchSuffix;
            Config.Cookie = configData.Cookie;
            Config.Initialized = true;
            return true;
        }
        return false;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error reading config file: {ex.Message}");
        return false;
    }
}
async Task ConnectAndMonitorWebSocket(string leagueName, string searchSuffix, string cookie)
{
    var uri = $"wss://www.pathofexile.com/api/trade/live/{leagueName}/{searchSuffix}";
    using (var ws = new ClientWebSocket())
    {
        ws.Options.SetRequestHeader("Cookie", cookie);
        SetWebSocketHeaders(ws);
        await ws.ConnectAsync(new Uri(uri), CancellationToken.None);
        Console.WriteLine("Connected!");
        await ReceiveMessages(ws, cookie, searchSuffix, leagueName);
    }
}

void SetWebSocketHeaders(ClientWebSocket ws)
{
    ws.Options.SetRequestHeader("Origin", "https://www.pathofexile.com");
    ws.Options.SetRequestHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:124.0) Gecko/20100101 Firefox/124.0");
}

async Task ReceiveMessages(ClientWebSocket ws, string cookie, string searchSuffix, string leagueName)
{
    var buffer = new byte[4096];
    while (ws.State == WebSocketState.Open)
    {
        var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

        if (result.MessageType == WebSocketMessageType.Close)
        {
            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
        }
        else
        {
            var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
            Console.WriteLine($"Received: {message}");
            await ProcessMessage(message, cookie, searchSuffix, leagueName);
        }
    }
}

async Task ProcessMessage(string message, string cookie, string searchSuffix, string leagueName)
{
    using (var doc = JsonDocument.Parse(message))
    {
        if (doc.RootElement.TryGetProperty("new", out var newElement) && newElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var guid in newElement.EnumerateArray())
            {
                var guidValue = guid.GetString();
                Console.WriteLine($"Extracted GUID: {guidValue}");
                await FetchTradeData(guidValue, cookie, searchSuffix, leagueName);
            }
        }
    }
}

async Task FetchTradeData(string guid, string cookie, string searchSuffix, string leagueName)
{
    var requestUri = $"https://www.pathofexile.com/api/trade/fetch/{guid}?query={searchSuffix}";
    var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
    SetHttpRequestHeaders(request, cookie, $"https://www.pathofexile.com/trade/search/{leagueName}/{searchSuffix}/live");
    await ProcessHttpRequest(request, leagueName, searchSuffix, cookie);
}

static void SetHttpRequestHeaders(HttpRequestMessage request, string cookie, string referer)
{
    request.Headers.Add("Cookie", cookie);
    request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:124.0) Gecko/20100101 Firefox/124.0");
    request.Headers.Add("Referer", referer);
}

async Task ProcessHttpRequest(HttpRequestMessage request, string leagueName, string searchSuffix, string cookie)
{
    try
    {
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var responseBody = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"Trade Data: {responseBody}");
        await ParseAndSendWhisper(responseBody, cookie, searchSuffix, leagueName);
    }
    catch (HttpRequestException e)
    {
        Console.WriteLine($"Error fetching trade data: {e.Message}");
    }
}

async Task ParseAndSendWhisper(string responseBody, string cookie, string searchSuffix, string leagueName)
{
    using (var doc = JsonDocument.Parse(responseBody))
    {
        if (doc.RootElement.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Array)
        {
            if (result[0].GetProperty("listing").TryGetProperty("whisper_token", out var whisperTokenElement))
            {
                var whisperToken = whisperTokenElement.GetString();
                await SendWhisper(whisperToken, cookie, searchSuffix, leagueName);
            }
        }
    }
}

async Task SendWhisper(string whisperToken, string cookie, string searchSuffix, string leagueName)
{
    var requestUri = "https://www.pathofexile.com/api/trade/whisper";
    var jsonContent = JsonSerializer.Serialize(new { token = whisperToken });
    var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
    var request = new HttpRequestMessage(HttpMethod.Post, requestUri) { Content = content };
    SetHttpRequestHeaders(request, cookie, $"https://www.pathofexile.com/trade/search/{leagueName}/{searchSuffix}/live");
    request.Headers.Add("Origin", "https://www.pathofexile.com");
    request.Headers.Add("X-Requested-With", "XMLHttpRequest");

    try
    {
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var responseBody = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"Whisper Sent: {responseBody}");
    }
    catch (HttpRequestException e)
    {
        Console.WriteLine($"Error sending whisper: {e.Message}");
    }
}