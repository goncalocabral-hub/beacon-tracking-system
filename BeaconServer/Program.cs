using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// ✅ Serve wwwroot/index.html e ficheiros estáticos
app.UseDefaultFiles();
app.UseStaticFiles();

// Swagger
app.UseSwagger();
app.UseSwaggerUI();

app.MapHub<BeaconHub>("/hub");

// Estado em memória: último visto por beaconId
var state = new ConcurrentDictionary<string, BeaconState>(StringComparer.OrdinalIgnoreCase);

// Recebe eventos do scanner
app.MapPost("/ingest", async (IngestPayload payload, IHubContext<BeaconHub> hub) =>
{
    var now = DateTime.UtcNow;

    var s = state.AddOrUpdate(
        payload.BeaconId,
        _ => new BeaconState(payload.BeaconId, payload.Rssi, now, payload.ReceiverId),
        (_, old) => old with { LastRssi = payload.Rssi, LastSeenUtc = now, LastReceiverId = payload.ReceiverId }
    );

    // envia em tempo real para o dashboard
    await hub.Clients.All.SendAsync("beacon_seen", s);

    return Results.Ok(new { ok = true });
});

// Para veres o estado no browser
app.MapGet("/state", () => state.Values.OrderBy(x => x.BeaconId));

app.Run();

public record IngestPayload(string ReceiverId, string BeaconId, int Rssi);

public record BeaconState(string BeaconId, int LastRssi, DateTime LastSeenUtc, string LastReceiverId);

public class BeaconHub : Hub { }