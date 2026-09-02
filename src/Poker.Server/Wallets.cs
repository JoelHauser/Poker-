using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Enums;

namespace Poker.Server;

public enum Wallet
{
    Roubles,
    Dollars,
    Euros,
    GpCoins,
    Bitcoin,
    LegaMedals,
}

/// <summary>
/// What kind of thing is being staked. These are not interchangeable and the table
/// does not treat them alike.
/// </summary>
public enum WalletKind
{
    /// <summary>Spendable money, held in thousands and bought in with in thousands.</summary>
    Currency,

    /// <summary>
    /// Valuables: GP coins, bitcoin, Lega medals. Held in single figures, and the two
    /// that matter do not stack at all -- one item per unit, one grid cell each.
    /// </summary>
    Valuable,
}

/// <summary>
/// Per-wallet limits and presentation.
///
/// These live here rather than in the engine because the engine has no concept of a
/// currency -- it takes an int and returns an int. One pair of limits cannot serve
/// both roubles and bitcoin: a buy-in of 5,000 is unremarkable in one and impossible
/// in the other.
/// </summary>
public sealed record WalletInfo(
    Wallet Wallet,
    WalletKind Kind,
    MongoId Tpl,
    string Symbol,
    string Label,
    int MinBuyIn,
    int MaxBuyIn)
{
    /// <summary>
    /// The ceilings are set from what a session can hand back, not from what a hand
    /// can pay. A pot cannot exceed the chips at the table, so the most that can come
    /// off a table is roughly the buy-in times the number of seats -- and with five
    /// seats a 10-coin bitcoin buy-in is 50 coins, which is 50 free grid cells,
    /// because bitcoin does not stack.
    ///
    /// That is the binding constraint on valuables, and it is why theirs are counts
    /// rather than amounts.
    /// </summary>
    private static readonly Dictionary<Wallet, WalletInfo> Table = new()
    {
        [Wallet.Roubles] = new(Wallet.Roubles, WalletKind.Currency, Money.ROUBLES, "R", "Roubles", 100_000, 5_000_000),
        [Wallet.Dollars] = new(Wallet.Dollars, WalletKind.Currency, Money.DOLLARS, "$", "Dollars", 100, 5_000),
        [Wallet.Euros] = new(Wallet.Euros, WalletKind.Currency, Money.EUROS, "E", "Euros", 100, 5_000),

        [Wallet.GpCoins] = new(Wallet.GpCoins, WalletKind.Valuable, Money.GP, "GP", "GP coins", 5, 40),
        [Wallet.Bitcoin] = new(Wallet.Bitcoin, WalletKind.Valuable, ItemTpl.BARTER_PHYSICAL_BITCOIN, "B", "Bitcoin", 1, 4),
        [Wallet.LegaMedals] = new(Wallet.LegaMedals, WalletKind.Valuable, ItemTpl.BARTER_LEGA_MEDAL, "LEGA", "Lega medals", 1, 3),
    };

    public static WalletInfo For(Wallet wallet) => Table[wallet];

    public static IEnumerable<WalletInfo> All => Table.Values;
}
