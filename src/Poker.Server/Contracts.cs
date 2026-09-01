using Poker.Game;
using SPTarkov.Server.Core.Models.Utils;

namespace Poker.Server;

// Request bodies are matched case-sensitively, so these must be sent PascalCase or
// every property silently takes its default. Blackjack lost an afternoon to a wager
// of 10,000 arriving as 0.

public record PingRequest : IRequestData;

public record StateRequest : IRequestData;

public record LeaveRequest : IRequestData;

/// <summary>Sit down at a table.</summary>
public record SitRequest : IRequestData
{
    /// <summary>Seats including the player. Two to five.</summary>
    public int Seats { get; set; } = 4;

    /// <summary>Chips each seat starts with. Not currency -- see the note on the service.</summary>
    public int BuyIn { get; set; } = 2_000_000;

    public int BigBlind { get; set; } = 20_000;

    /// <summary>Fixes the shuffle and the characters, so a hand can be got back.</summary>
    public int? Seed { get; set; }
}

/// <summary>Deal the next hand at a table already sat at.</summary>
public record DealRequest : IRequestData;

/// <summary>Fold, Check, Call or Raise. Parsed case-insensitively.</summary>
public record ActRequest : IRequestData
{
    public string Move { get; set; } = string.Empty;

    /// <summary>
    /// For a raise: the **total to be in for on this street**, not the extra being
    /// added. Poker is spoken that way, and reading it the other way is the easiest
    /// route to a betting round that takes the wrong number of chips.
    /// </summary>
    public int To { get; set; }
}

/// <summary>
/// Answers the questions that must be true before anything else is worth trying: did
/// the mod load, is the route reachable, did the session resolve to a real profile,
/// and can its money be read at all.
/// </summary>
public record PingResponse
{
    public bool Ok { get; init; } = true;

    public string ModVersion { get; init; } = string.Empty;

    /// <summary>Empty here means the session cookie did not resolve.</summary>
    public string SessionId { get; init; } = string.Empty;

    public bool HasProfile { get; init; }

    /// <summary>Read only. This build cannot move any of it.</summary>
    public Dictionary<string, int> Balances { get; init; } = [];

    /// <summary>What each wallet would take as a buy-in, once buy-ins exist.</summary>
    public Dictionary<string, BuyInLimits> Limits { get; init; } = [];

    /// <summary>
    /// True while the table plays for chips that are not currency. Sent so the client
    /// can say so plainly rather than implying a stash is at stake.
    /// </summary>
    public bool ChipsAreNotional { get; init; } = true;
}

public record BuyInLimits
{
    public int Min { get; init; }

    public int Max { get; init; }

    /// <summary>What one unit occupies. A limit of 1 means one item per unit.</summary>
    public int StackLimit { get; init; }
}

/// <summary>
/// What every game route returns. <see cref="Ok"/> false means the request was
/// refused before anything changed -- the client should show <see cref="Error"/> and
/// keep displaying the table it already had.
/// </summary>
public record PokerResponse
{
    public bool Ok { get; init; } = true;

    public string? Error { get; init; }

    public HoldemView? Table { get; init; }

    /// <summary>Who is sitting at the table, in seat order. Empty until sat down.</summary>
    public IReadOnlyList<string> Characters { get; init; } = [];

    public static PokerResponse Failed(string error) => new() { Ok = false, Error = error };
}
