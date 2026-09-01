using SPTarkov.Server.Core.Models.Common;

namespace Poker.Server;

/// <summary>
/// Reading the player's money.
///
/// An interface because SPT's helpers are concrete classes with non-virtual methods,
/// and depending on them directly makes the calling code impossible to test without a
/// running server. SPT's DI registers a class against every interface it implements,
/// so <see cref="Bank"/> resolves for this with no extra wiring.
///
/// **Nothing here moves money yet, deliberately.** Debit and credit arrive with the
/// buy-in, and the reason they are not here already is that a mod which cannot move
/// money cannot lose any -- which makes this build safe to point at a real profile.
/// </summary>
public interface IBank
{
    int GetBalance(MongoId sessionId, Wallet wallet);

    /// <summary>
    /// The running server's stack limit for a wallet, which item mods change.
    ///
    /// Read live, never assumed: the base database says roubles stack to 1,000,000
    /// and that bitcoin does not stack at all, while BarterItemsStacks raises both.
    /// Both are correct on different servers.
    /// </summary>
    int MaxStackSize(Wallet wallet);
}

public interface IProfileGateway
{
    bool HasProfile(MongoId sessionId);
}
