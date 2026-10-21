using BulgariaConquest.Core.Game;
using BulgariaConquest.Server.Games;
using Microsoft.AspNetCore.SignalR;

namespace BulgariaConquest.Server.Hubs;

/// <summary>Messages the server pushes to browsers.</summary>
public interface IGameClient
{
    Task GameUpdated(GameView game);
}

/// <summary>
/// Real-time endpoint (/hubs/game). Every action carries the game code and the player's secret token;
/// the resulting state is broadcast to the whole game via <see cref="IGameClient.GameUpdated"/>.
/// </summary>
public sealed class GameHub(GameManager games, GameEngine engine) : Hub<IGameClient>
{
    /// <summary>Game with friends. <paramref name="difficulty"/>: "easy" or "hard".</summary>
    public async Task<JoinResult> CreateGame(string playerName, string? difficulty) =>
        await Subscribe(await games.CreateAsync(playerName, ParseDifficulty(difficulty)));

    /// <summary>Single-player game against bots. <paramref name="difficulty"/>: "easy" or "hard".</summary>
    public async Task<JoinResult> CreateBotGame(string playerName, string? difficulty) =>
        await Subscribe(await games.CreateBotGameAsync(playerName, ParseDifficulty(difficulty)));

    /// <summary>Lobby host only: adds a bot to an empty seat.</summary>
    public Task AddBot(string gameId, string token) => games.AddBotAsync(gameId, token);

    /// <summary>Lobby host only: removes a player or bot.</summary>
    public Task KickPlayer(string gameId, string token, string playerId) =>
        games.ActAsync(gameId, token, (g, p) => engine.KickPlayer(g, p, playerId));

    private static GameDifficulty ParseDifficulty(string? value) =>
        Enum.TryParse<GameDifficulty>(value, ignoreCase: true, out var parsed) ? parsed : GameDifficulty.Easy;

    public async Task<JoinResult> JoinGame(string gameId, string playerName) =>
        await Subscribe(await games.JoinAsync(gameId, playerName));

    public async Task<JoinResult> ResumeGame(string gameId, string token) =>
        await Subscribe(await games.ResumeAsync(gameId, token));

    public Task StartGame(string gameId, string token) =>
        games.ActAsync(gameId, token, (g, p) => engine.Start(g, p));

    public Task ChooseCastle(string gameId, string token, string provinceId) =>
        games.ActAsync(gameId, token, (g, p) => engine.ChooseCastle(g, p, provinceId));

    public Task ClaimProvince(string gameId, string token, string provinceId) =>
        games.ActAsync(gameId, token, (g, p) => engine.ClaimProvince(g, p, provinceId));

    public Task Attack(string gameId, string token, string provinceId) =>
        games.ActAsync(gameId, token, (g, p) => engine.Attack(g, p, provinceId));

    public Task AnswerNumeric(string gameId, string token, double value) =>
        games.ActAsync(gameId, token, (g, p) => engine.AnswerNumeric(g, p, value));

    public Task AnswerChoice(string gameId, string token, int optionIndex) =>
        games.ActAsync(gameId, token, (g, p) => engine.AnswerChoice(g, p, optionIndex));

    /// <summary>Spends a lifeline. kind: "fiftyFifty", "range" or "friend". The result goes only to the caller.</summary>
    public Task<LifelineResult> UseLifeline(string gameId, string token, string kind) =>
        games.ActAsync(gameId, token, (g, p) => engine.UseLifeline(g, p,
            Enum.TryParse<LifelineKind>(kind, ignoreCase: true, out var parsed) ? parsed : throw new HubException("Няма такъв жокер.")));

    /// <summary>The "Тема" lifeline: before attacking, picks the category of the battle question.</summary>
    public Task<LifelineResult> UseCategoryLifeline(string gameId, string token, string category) =>
        games.ActAsync(gameId, token, (g, p) => engine.UseLifeline(g, p, LifelineKind.Category, category));

    /// <summary>Lifeline results for the current question (to restore them after a page reload).</summary>
    public Task<IReadOnlyList<LifelineResult>> MyLifelines(string gameId, string token) =>
        games.QueryAsync(gameId, token, (g, p) => engine.LifelinesOf(g.Question, p));

    public Task SendChat(string gameId, string token, string text) =>
        games.ActAsync(gameId, token, (g, p) => engine.SendChat(g, p, text));

    private async Task<JoinResult> Subscribe(JoinResult result)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, result.GameId);
        await Clients.Caller.GameUpdated(await games.GetViewAsync(result.GameId));
        return result;
    }
}

/// <summary>Turns rule violations into friendly errors for the client instead of generic hub failures.</summary>
public sealed class GameRuleExceptionFilter : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext context, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        try
        {
            return await next(context);
        }
        catch (GameRuleException ex)
        {
            throw new HubException(ex.Message);
        }
    }
}
