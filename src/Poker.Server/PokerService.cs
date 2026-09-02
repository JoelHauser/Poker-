using Poker.Game;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;

namespace Poker.Server;

/// <summary>
/// The whole server-side game flow: validate what was asked for, let the table
/// decide, hand back a view.
///
/// Depends only on <see cref="IBank"/>, <see cref="IProfileGateway"/> and
/// <see cref="TableStore"/>, so it runs -- and can be tested -- with no SPT server
/// present. HTTP and logging live in <see cref="PokerCallbacks"/>.
///
/// **The chips are not currency in this build.** Sitting down costs nothing and
/// cashing out pays nothing; the stacks are numbers in memory. That is a deliberate
/// stopping point rather than an oversight: it makes the mod safe to point at a real
/// profile while the parts that load, route and play are proven, and it keeps the
/// money path -- the part that cost Blackjack the most -- for a build where the rest
/// is known to work.
/// </summary>
[Injectable]
public class PokerService(
    IBank bank,
    IProfileGateway profiles,
    TableStore tables,
    INameSource names,
    PokerLog log)
{
    /// <summary>Cheap health check. Touches nothing and starts no game.</summary>
    public PingResponse Ping(MongoId sessionId)
    {
        var known = profiles.HasProfile(sessionId);

        return new PingResponse
        {
            ModVersion = new ModMetadata().Version.ToString(),
            SessionId = sessionId.ToString(),
            HasProfile = known,
            Balances = known
                ? Enum.GetValues<Wallet>().ToDictionary(w => w.ToString(), w => bank.GetBalance(sessionId, w))
                : [],

            // Not gated on the profile: the limits belong to the table rather than to
            // the player, and a client that cannot read them has no way to offer a
            // legal buy-in before sending one.
            Limits = WalletInfo.All.ToDictionary(
                w => w.Wallet.ToString(),
                w => new BuyInLimits
                {
                    Min = w.MinBuyIn,
                    Max = w.MaxBuyIn,
                    StackLimit = known ? bank.MaxStackSize(w.Wallet) : 0,
                }),
        };
    }

    public PokerResponse Sit(SitRequest request, MongoId sessionId)
    {
        if (!profiles.HasProfile(sessionId))
        {
            return PokerResponse.Failed("No PMC profile for this session.");
        }

        if (request.Seats is < 2 or > 5)
        {
            return PokerResponse.Failed("A table seats 2 to 5, the player included.");
        }

        if (request.BigBlind < 2)
        {
            return PokerResponse.Failed("The big blind has to be at least 2, so the small blind is a whole chip.");
        }

        if (request.BuyIn < request.BigBlind * 10)
        {
            return PokerResponse.Failed(
                $"A buy-in of {request.BuyIn} is under ten big blinds. There would be nothing to play with.");
        }

        var rules = new HoldemRules
        {
            SmallBlind = request.BigBlind / 2,
            BigBlind = request.BigBlind,
            BuyIn = request.BuyIn,
        };

        var seed = request.Seed ?? Environment.TickCount;
        var rng = new Random(seed);
        var engineLog = log.ForEngine();

        // Improvised rather than picked off the list, so no two tables are alike.
        var characters = Enumerable.Range(0, request.Seats - 1)
            .Select(_ => PokerPersonality.Improvise(rng))
            .ToList();

        var agents = characters
            .Select((character, index) => new BotAgent(character, new Random(seed + index + 1), engineLog))
            .ToList();

        // One name per bot, from the game's own PMC list. Fewer than asked for is
        // fine -- the table numbers whatever it does not get.
        var seatNames = names.Take(request.Seats - 1, rng);

        var table = new HoldemTable(
            rules,
            request.Seats,
            rng,
            engineLog,
            agents.Cast<IPokerAgent>().ToList(),
            seatNames);

        tables.Set(sessionId, new PlayerSession
        {
            Table = table,
            Characters = characters,
            Agents = agents,
            BuyIn = request.BuyIn,
        });

        log.Info(
            $"seat taken [{sessionId}] -- {request.Seats} seats, blinds {rules.SmallBlind}/{rules.BigBlind}, "
            + $"{request.BuyIn} chips each, seed {seed}");

        foreach (var character in characters)
        {
            log.Detail($"  {character}");
        }

        return Success(sessionId);
    }

    public PokerResponse Deal(MongoId sessionId)
    {
        var session = tables.Get(sessionId);

        if (session is null)
        {
            return PokerResponse.Failed("You are not at a table. Sit down first.");
        }

        if (session.Table.Street is not (HoldemStreet.Idle or HoldemStreet.Showdown))
        {
            return PokerResponse.Failed("A hand is already in progress.");
        }

        // A busted seat is bought back in by somebody new, which is the difference
        // between a table and a treadmill. The player's own seat is topped up too --
        // free, while the chips are notional.
        Reseat(session);

        try
        {
            session.Table.StartHand();
        }
        catch (InvalidOperationException ex)
        {
            return PokerResponse.Failed(ex.Message);
        }

        return Success(sessionId);
    }

    public PokerResponse Act(ActRequest request, MongoId sessionId)
    {
        var session = tables.Get(sessionId);

        if (session is null)
        {
            return PokerResponse.Failed("You are not at a table.");
        }

        if (!session.Table.AwaitingPlayer)
        {
            return PokerResponse.Failed("It is not your turn.");
        }

        if (!Enum.TryParse<HoldemMove>(request.Move, ignoreCase: true, out var move))
        {
            return PokerResponse.Failed($"Unknown move '{request.Move}'. Fold, Check, Call or Raise.");
        }

        try
        {
            session.Table.Act(new HoldemDecision(move, request.To));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException)
        {
            // The engine is the authority on legality. A refusal means the client's
            // view drifted, so hand it the real one back rather than a bare error.
            return Success(sessionId) with { Ok = false, Error = ex.Message };
        }

        return Success(sessionId);
    }

    public PokerResponse State(MongoId sessionId) =>
        tables.Get(sessionId) is null
            ? PokerResponse.Failed("You are not at a table.")
            : Success(sessionId);

    public PokerResponse Leave(MongoId sessionId)
    {
        tables.Clear(sessionId);
        log.Info($"left the table [{sessionId}]");

        return new PokerResponse();
    }

    private void Reseat(PlayerSession session)
    {
        foreach (var seat in session.Table.Seats.Where(seat => seat.Stack <= 0).ToList())
        {
            if (seat.IsPlayer)
            {
                session.Table.Reseat(seat.Index, session.BuyIn);
                log.Info("you went broke and were topped back up -- the chips are not real yet.");
                continue;
            }

            var newcomer = PokerPersonality.Improvise(new Random(Environment.TickCount + seat.Index));
            var agent = new BotAgent(newcomer, new Random(Environment.TickCount + seat.Index + 7), log.ForEngine());

            session.Agents[seat.Index - 1] = agent;
            session.Table.Reseat(seat.Index, session.BuyIn, agent);

            log.Info($"{seat.Name} went broke; {newcomer.Name} sits down.");
        }
    }

    private PokerResponse Success(MongoId sessionId)
    {
        var session = tables.Get(sessionId);

        return new PokerResponse
        {
            Table = session is null ? null : HoldemView.Of(session.Table),
            Characters = session?.Characters.Select(character => character.Name).ToList() ?? [],
        };
    }
}
