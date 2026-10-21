using System.Collections.Concurrent;
using BulgariaConquest.Core.Bots;
using BulgariaConquest.Core.Game;
using BulgariaConquest.Core.Rules;
using BulgariaConquest.Server.Hubs;
using BulgariaConquest.Server.Storage;
using Microsoft.AspNetCore.SignalR;

namespace BulgariaConquest.Server.Games;

/// <summary>
/// Owns the live games: serialises access per game, runs the engine, saves to the repository
/// and pushes the new state to everyone in the game's SignalR group.
/// </summary>
public sealed class GameManager(
    GameEngine engine,
    BotDirector bots,
    IGameRepository repository,
    IHubContext<GameHub, IGameClient> hub,
    GameSettings settings,
    TimeProvider time,
    ILogger<GameManager> logger)
{
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private sealed class Session(GameState state)
    {
        public GameState State { get; } = state;
        public SemaphoreSlim Lock { get; } = new(1, 1);
    }

    private readonly ConcurrentDictionary<string, Session> _sessions = new();

    public async Task<JoinResult> CreateAsync(string playerName, GameDifficulty difficulty)
    {
        var state = engine.CreateGame(NewGameId(), settings, difficulty);
        var player = engine.AddPlayer(state, playerName);
        await AddSessionAsync(state);
        return new JoinResult(state.Id, player.Id, player.Token);
    }

    /// <summary>Single-player game: the human plus bots fill the remaining seats; starts immediately.</summary>
    public async Task<JoinResult> CreateBotGameAsync(string playerName, GameDifficulty difficulty)
    {
        var state = engine.CreateGame(NewGameId(), settings, difficulty);
        var player = engine.AddPlayer(state, playerName);
        while (state.Players.Count < settings.MaxPlayers)
            engine.AddPlayer(state, FreeBotName(state), BotLevelFor(state));
        engine.Start(state, player.Id);

        await AddSessionAsync(state);
        return new JoinResult(state.Id, player.Id, player.Token);
    }

    /// <summary>The host fills an empty lobby seat with a bot of the game's difficulty.</summary>
    public Task AddBotAsync(string gameId, string token) =>
        ActAsync(gameId, token, (game, hostId) => engine.AddBot(game, hostId, FreeBotName(game), BotLevelFor(game)));

    private static BotLevel BotLevelFor(GameState game) => game.Difficulty switch
    {
        GameDifficulty.Easy => BotLevel.Easy,
        GameDifficulty.Hard => BotLevel.Hard,
        _ => BotLevel.Normal,
    };

    private static string FreeBotName(GameState game) =>
        BotDirector.Names
            .Where(n => !game.Players.Any(p => string.Equals(p.Name, n, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(_ => Random.Shared.Next())
            .FirstOrDefault() ?? throw new GameRuleException("Няма свободни имена за ботове.");

    private string NewGameId()
    {
        string id;
        do id = string.Concat(Enumerable.Range(0, 5).Select(_ => CodeAlphabet[Random.Shared.Next(CodeAlphabet.Length)]));
        while (_sessions.ContainsKey(id) || repository.Exists(id));
        return id;
    }

    private async Task AddSessionAsync(GameState state)
    {
        var session = new Session(state);
        _sessions[state.Id] = session;
        await CommitAsync(session);
    }

    public Task<JoinResult> JoinAsync(string gameId, string playerName) =>
        MutateAsync(gameId, game =>
        {
            var player = engine.AddPlayer(game, playerName);
            return new JoinResult(game.Id, player.Id, player.Token);
        });

    public async Task<JoinResult> ResumeAsync(string gameId, string token)
    {
        var session = await GetSessionAsync(gameId);
        var player = session.State.Players.FirstOrDefault(p => p.Token == token)
                     ?? throw new GameRuleException("Не си участник в тази игра.");
        return new JoinResult(session.State.Id, player.Id, player.Token);
    }

    /// <summary>Runs a player action. The token identifies the player; the engine checks the rules.</summary>
    public Task ActAsync(string gameId, string token, Action<GameState, string> action) =>
        ActAsync(gameId, token, (game, playerId) =>
        {
            action(game, playerId);
            return true;
        });

    /// <summary>Runs a player action that returns something only the caller should see (e.g. a lifeline result).</summary>
    public Task<T> ActAsync<T>(string gameId, string token, Func<GameState, string, T> action) =>
        MutateAsync(gameId, game => action(game, PlayerIdFor(game, token)));

    /// <summary>Reads player-private data without changing or broadcasting the game.</summary>
    public async Task<T> QueryAsync<T>(string gameId, string token, Func<GameState, string, T> query)
    {
        var session = await GetSessionAsync(gameId);
        await session.Lock.WaitAsync();
        try { return query(session.State, PlayerIdFor(session.State, token)); }
        finally { session.Lock.Release(); }
    }

    private static string PlayerIdFor(GameState game, string token) =>
        game.Players.FirstOrDefault(p => p.Token == token)?.Id
        ?? throw new GameRuleException("Не си участник в тази игра.");

    public async Task<GameView> GetViewAsync(string gameId)
    {
        var session = await GetSessionAsync(gameId);
        await session.Lock.WaitAsync();
        try { return GameView.From(session.State, engine, time.GetUtcNow()); }
        finally { session.Lock.Release(); }
    }

    /// <summary>Advances every running game whose deadline has passed.</summary>
    public async Task TickAsync()
    {
        foreach (var session in _sessions.Values)
        {
            if (session.State.Phase is GamePhase.Lobby or GamePhase.Finished) continue;
            if (!await session.Lock.WaitAsync(0)) continue;
            try
            {
                var changed = engine.Tick(session.State);
                changed |= bots.Act(session.State);
                if (changed) await CommitAsync(session);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Tick failed for game {GameId}", session.State.Id);
            }
            finally
            {
                session.Lock.Release();
            }
        }
    }

    private async Task<T> MutateAsync<T>(string gameId, Func<GameState, T> action)
    {
        var session = await GetSessionAsync(gameId);
        await session.Lock.WaitAsync();
        try
        {
            var result = action(session.State);
            await CommitAsync(session);
            return result;
        }
        finally
        {
            session.Lock.Release();
        }
    }

    private async Task CommitAsync(Session session)
    {
        var state = session.State;
        state.Version++;
        state.UpdatedAt = time.GetUtcNow();
        await repository.SaveAsync(state);
        await hub.Clients.Group(state.Id).GameUpdated(GameView.From(state, engine, time.GetUtcNow()));
    }

    private async Task<Session> GetSessionAsync(string gameId)
    {
        var id = (gameId ?? "").Trim().ToUpperInvariant();
        if (_sessions.TryGetValue(id, out var session)) return session;

        var state = await repository.LoadAsync(id) ?? throw new GameRuleException("Няма игра с такъв код.");
        return _sessions.GetOrAdd(id, _ => new Session(state));
    }
}

/// <summary>Drives game timers (answer deadlines, turn timeouts, reveal pauses).</summary>
public sealed class GameTicker(GameManager games) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await games.TickAsync();
    }
}
