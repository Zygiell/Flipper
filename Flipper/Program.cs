﻿using System.Media;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Flipper;
using YamlDotNet.Serialization;

var client = new HttpClient();
Console.WriteLine("PoE Flipper: live search spammer by ZGL feat. BTN");

Console.WriteLine("Do you want to use Config from config file? 1 - YES, any other - NO");
var choice = Console.ReadLine();
if (choice == "1")
{
    if (!LoadConfigFromFile("config.yaml"))
    {
        PrintWithColor("Failed to load config from file, please enter configuration manually.", ConsoleColor.Red);
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
    Console.WriteLine("Enter search suffixes, separated by commas WITHOUT WHITE SPACES!!!");
    Config.SearchSuffix = new List<string>(Console.ReadLine().Split(','));
    Console.WriteLine("Cookie from poe trade");
    Config.Cookie = Console.ReadLine();
    Console.WriteLine("Do you want to hear notification sound on sent whisper? 1 - yes Any other NO");
    Config.PlayNotificationSoundOnWhisper = Console.ReadLine() == "1" ? true : false;
    Config.Initialized = true;
}

bool LoadConfigFromFile(string filePath)
{
    if (!File.Exists(filePath))
    {
        PrintWithColor($"Config file not found: {filePath}", ConsoleColor.Red);
        return false;
    }

    try
    {
        string yaml = File.ReadAllText(filePath);
        var deserializer = new DeserializerBuilder()
            .Build();
        var configData = deserializer.Deserialize<ConfigData>(yaml);

        if (configData != null)
        {
            Config.LeagueName = configData.LeagueName;
            Config.SearchSuffix = configData.SearchSuffix;
            Config.Cookie = configData.Cookie;
            Config.Initialized = true;
            Config.PlayNotificationSoundOnWhisper = configData.PlayNotificationSoundOnWhisper;
            return true;
        }
        return false;
    }
    catch (Exception ex)
    {
        PrintWithColor($"Error reading config file: {ex.Message}", ConsoleColor.Red);
        return false;
    }
}

async Task ConnectAndMonitorWebSocket(string leagueName, List<string> searchSuffixes, string cookie)
{
    foreach (var searchSuffix in searchSuffixes)
    {
        if (String.IsNullOrEmpty(searchSuffix))
            return;
        _ = Task.Run(async () =>
        {
            var uri = $"wss://www.pathofexile.com/api/trade/live/{leagueName}/{searchSuffix}";
            using (var ws = new ClientWebSocket())
            {
                ws.Options.SetRequestHeader("Cookie", cookie);
                SetWebSocketHeaders(ws);
                await ws.ConnectAsync(new Uri(uri), CancellationToken.None);
                PrintWithColor($"Connected to {searchSuffix}!", ConsoleColor.Green);
                await ReceiveMessages(ws, cookie, searchSuffix, leagueName);
            }
        });
    }

    PrintWithColor("Attempting to connect, press any key to terminate app anytime!", ConsoleColor.Yellow);
    Console.ReadKey();
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
            PrintWithColor($"Received: {message}", ConsoleColor.Green);
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

        var tradeData = ParseTradeData(responseBody);
        if (tradeData != null)
        {
            string itemNamePart = !string.IsNullOrEmpty(tradeData.Name) ? $"Item name: {tradeData.Name}, " : "";
            PrintWithColor($"Player: @{tradeData.Seller} || {itemNamePart}{tradeData.TypeLine} || Price: {tradeData.Amount} {tradeData.Currency} || Quantity: {tradeData.Quantity} || Full stack price: {tradeData.FullStackPrice} ||", ConsoleColor.Blue);
        }
        else
        {
            PrintWithColor("Player offline or no data available.", ConsoleColor.Red);
        }

        await ParseAndSendWhisper(responseBody, cookie, searchSuffix, leagueName);
    }
    catch (Exception ex)
    {
        PrintWithColor($"An error occurred: {ex.Message}", ConsoleColor.Red);
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
            else
            {
                PrintWithColor("Player offline", ConsoleColor.Red);
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
        PrintWithColor($"Whisper Sent: {responseBody}", ConsoleColor.Green);
        if (Config.PlayNotificationSoundOnWhisper)
            PlaySound();
    }
    catch (HttpRequestException e)
    {
        PrintWithColor($"Error sending whisper: {e.Message}", ConsoleColor.Red);
    }
}

void PlaySound()
{
    try
    {
        using (var player = new SoundPlayer(@"pulse.wav"))
        {
            player.Load();
            player.Play();
        }
    }
    catch (Exception ex)
    {
        PrintWithColor($"Cant play sound: {ex.Message}", ConsoleColor.Red);
    }
}

TradeData ParseTradeData(string responseBody)
{
    using (var doc = JsonDocument.Parse(responseBody))
    {
        if (doc.RootElement.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Array && result.GetArrayLength() > 0)
        {
            var firstResult = result[0];
            if (firstResult.GetProperty("listing").TryGetProperty("price", out var price))
            {
                var seller = firstResult.GetProperty("listing").GetProperty("account").GetProperty("lastCharacterName").GetString();
                var amount = price.GetProperty("amount").GetDecimal();
                var currency = price.GetProperty("currency").GetString();
                if (firstResult.TryGetProperty("item", out var item))
                {
                    var name = item.GetProperty("name").GetString();
                    var typeLine = item.GetProperty("typeLine").GetString();

                    var quantity = 1;
                    if (item.TryGetProperty("stackSize", out var quantityJsEl) && quantityJsEl.TryGetInt32(out var tempQuantity))
                        quantity = tempQuantity;

                    var fullStackPrice = quantity * amount;

                    return new TradeData
                    {
                        Amount = amount,
                        Currency = currency,
                        Name = name,
                        TypeLine = typeLine,
                        Quantity = quantity,
                        FullStackPrice = fullStackPrice,
                        Seller = seller
                    };
                }
            }
        }
    }
    return null;
}

static void PrintWithColor(string message, ConsoleColor color)
{
    Console.ForegroundColor = color;
    Console.WriteLine(message);
    Console.ResetColor();
}