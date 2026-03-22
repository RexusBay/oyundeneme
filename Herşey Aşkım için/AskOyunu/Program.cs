using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseWebSockets();

var rooms = new ConcurrentDictionary<string, RoomState>(StringComparer.OrdinalIgnoreCase);

app.MapGet("/api/health", () => Results.Ok(new { ok = true }));

app.Map("/ws", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    var roomCode = SanitizeRoomCode(context.Request.Query["room"]);
    var playerName = SanitizeName(context.Request.Query["name"]);

    if (string.IsNullOrWhiteSpace(roomCode) || string.IsNullOrWhiteSpace(playerName))
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("Room and name are required.");
        return;
    }

    var room = rooms.GetOrAdd(roomCode, code => new RoomState(code));

    PlayerConnection? player;
    lock (room.SyncRoot)
    {
        if (room.Players.Count >= 2)
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            return;
        }

        var symbol = room.Players.Count == 0 ? 'A' : 'B';
        player = new PlayerConnection(playerName, symbol);
        room.Players.Add(player);

        if (room.Players.Count == 2)
        {
            room.Status = $"{room.Players[0].Name} basladi. Sirada {room.GetCurrentPlayerName()} var.";
        }
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    player.Socket = socket;

    await BroadcastRoomStateAsync(room);

    try
    {
        while (socket.State == WebSocketState.Open)
        {
            var message = await ReceiveMessageAsync(socket, context.RequestAborted);
            if (message is null)
            {
                break;
            }

            using var document = JsonDocument.Parse(message);
            var type = document.RootElement.GetProperty("type").GetString();

            switch (type)
            {
                case "move":
                    if (!document.RootElement.TryGetProperty("cell", out var cellElement))
                    {
                        break;
                    }

                    var cellIndex = cellElement.GetInt32();
                    lock (room.SyncRoot)
                    {
                        if (room.Winner is not null || room.Players.Count < 2)
                        {
                            break;
                        }

                        if (room.CurrentTurn != player.Symbol)
                        {
                            break;
                        }

                        if (cellIndex < 0 || cellIndex >= room.Board.Length || room.Board[cellIndex] != '.')
                        {
                            break;
                        }

                        room.Board[cellIndex] = player.Symbol;
                        room.LastMoveAt = DateTimeOffset.UtcNow;

                        if (HasLine(room.Board, player.Symbol))
                        {
                            room.Winner = player.Symbol;
                            room.Status = $"{player.Name} bu raundu kazandi.";
                        }
                        else if (room.Board.All(cell => cell != '.'))
                        {
                            room.Winner = 'D';
                            room.Status = "Berabere bitti. Yeni raund baslatabilirsiniz.";
                        }
                        else
                        {
                            room.CurrentTurn = player.Symbol == 'A' ? 'B' : 'A';
                            room.Status = $"Sirada {room.GetCurrentPlayerName()} var.";
                        }
                    }
                    break;

                case "restart":
                    lock (room.SyncRoot)
                    {
                        room.ResetBoard();
                        room.Status = $"Yeni raund basladi. Sirada {room.GetCurrentPlayerName()} var.";
                    }
                    break;
            }

            await BroadcastRoomStateAsync(room);
        }
    }
    finally
    {
        lock (room.SyncRoot)
        {
            room.Players.Remove(player);

            if (room.Players.Count == 0)
            {
                rooms.TryRemove(room.Code, out _);
            }
            else
            {
                room.ResetBoard();
                room.Status = $"{player.Name} ayrildi. Yeni oyuncu bekleniyor.";
            }
        }

        await BroadcastRoomStateAsync(room);
    }
});

var portValue = Environment.GetEnvironmentVariable("PORT");
if (int.TryParse(portValue, out var port))
{
    app.Run($"http://0.0.0.0:{port}");
}
else
{
    app.Run();
}

static async Task BroadcastRoomStateAsync(RoomState room)
{
    RoomSnapshot snapshot;
    List<PlayerConnection> players;

    lock (room.SyncRoot)
    {
        players = room.Players.ToList();
        snapshot = room.ToSnapshot();
    }

    var payload = JsonSerializer.Serialize(new { type = "state", state = snapshot });

    foreach (var player in players)
    {
        if (player.Socket?.State == WebSocketState.Open)
        {
            await SendMessageAsync(player.Socket, payload);
        }
    }
}

static async Task<string?> ReceiveMessageAsync(WebSocket socket, CancellationToken cancellationToken)
{
    var buffer = new byte[4096];
    using var stream = new MemoryStream();

    while (true)
    {
        var result = await socket.ReceiveAsync(buffer, cancellationToken);

        if (result.MessageType == WebSocketMessageType.Close)
        {
            return null;
        }

        stream.Write(buffer, 0, result.Count);

        if (result.EndOfMessage)
        {
            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }
}

static Task SendMessageAsync(WebSocket socket, string payload)
{
    var bytes = Encoding.UTF8.GetBytes(payload);
    return socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
}

static string SanitizeRoomCode(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return string.Empty;
    }

    return new string(value.Trim().ToUpperInvariant().Where(char.IsLetterOrDigit).Take(6).ToArray());
}

static string SanitizeName(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return string.Empty;
    }

    return new string(value.Trim().Take(20).Where(ch => char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch)).ToArray());
}

static bool HasLine(char[] board, char symbol)
{
    const int size = 5;
    const int target = 4;
    var directions = new (int row, int column)[]
    {
        (0, 1),
        (1, 0),
        (1, 1),
        (1, -1)
    };

    for (var row = 0; row < size; row++)
    {
        for (var column = 0; column < size; column++)
        {
            if (board[(row * size) + column] != symbol)
            {
                continue;
            }

            foreach (var (dr, dc) in directions)
            {
                var matched = true;

                for (var step = 1; step < target; step++)
                {
                    var nextRow = row + (dr * step);
                    var nextColumn = column + (dc * step);

                    if (nextRow < 0 || nextRow >= size || nextColumn < 0 || nextColumn >= size)
                    {
                        matched = false;
                        break;
                    }

                    if (board[(nextRow * size) + nextColumn] != symbol)
                    {
                        matched = false;
                        break;
                    }
                }

                if (matched)
                {
                    return true;
                }
            }
        }
    }

    return false;
}

sealed class RoomState
{
    public RoomState(string code)
    {
        Code = code;
        Board = Enumerable.Repeat('.', 25).ToArray();
        Status = "Oyuncu bekleniyor.";
        LastMoveAt = DateTimeOffset.UtcNow;
    }

    public string Code { get; }
    public object SyncRoot { get; } = new();
    public List<PlayerConnection> Players { get; } = [];
    public char[] Board { get; }
    public char CurrentTurn { get; set; } = 'A';
    public char? Winner { get; set; }
    public string Status { get; set; }
    public DateTimeOffset LastMoveAt { get; set; }

    public void ResetBoard()
    {
        for (var index = 0; index < Board.Length; index++)
        {
            Board[index] = '.';
        }

        Winner = null;
        CurrentTurn = 'A';
        LastMoveAt = DateTimeOffset.UtcNow;
    }

    public string GetCurrentPlayerName()
    {
        return Players.FirstOrDefault(player => player.Symbol == CurrentTurn)?.Name ?? "ilk oyuncu";
    }

    public RoomSnapshot ToSnapshot()
    {
        return new RoomSnapshot(
            Code,
            new string(Board),
            Players.Select(player => new PlayerSnapshot(player.Name, player.Symbol.ToString())).ToArray(),
            CurrentTurn.ToString(),
            Winner?.ToString(),
            Status);
    }
}

sealed class PlayerConnection
{
    public PlayerConnection(string name, char symbol)
    {
        Name = name;
        Symbol = symbol;
    }

    public string Name { get; }
    public char Symbol { get; }
    public WebSocket? Socket { get; set; }
}

record PlayerSnapshot(string Name, string Symbol);
record RoomSnapshot(string Code, string Board, PlayerSnapshot[] Players, string CurrentTurn, string? Winner, string Status);
