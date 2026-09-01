using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace Poker.Server;

/// <summary>
/// Reads what the player has.
///
/// Ported from Blackjack's Bank, which walks item stacks directly rather than going
/// through PaymentService -- both of that service's entry points derive the currency
/// from a trader, so neither can settle anything denominated in dollars or euros.
///
/// **This build only reads.** The debit and credit halves, and the shortfall-to-mail
/// path that rescues a payout a full stash refused, come with the buy-in. Until then
/// the mod cannot move a rouble, which is what makes it safe to try on a real
/// profile.
/// </summary>
[Injectable]
public class Bank(ItemHelper itemHelper, ProfileHelper profileHelper, PokerLog log) : IBank
{
    /// <summary>
    /// Total of every stack of this currency the profile holds. Counts money in
    /// containers as well as loose in the stash, which is what a player would call
    /// their balance.
    /// </summary>
    public int GetBalance(MongoId sessionId, Wallet wallet)
    {
        var pmcData = profileHelper.GetPmcProfile(sessionId);

        if (pmcData is null)
        {
            log.Error($"GetBalance: no PMC profile for session '{sessionId}'.");
            return 0;
        }

        return StacksOf(pmcData, WalletInfo.For(wallet).Tpl).Sum(item => item.GetItemStackSize());
    }

    /// <summary>
    /// Clamped to at least one. A limit of zero -- which a careless item mod can
    /// produce -- would make a payout's splitting loop take zero each pass and never
    /// terminate, hanging a server thread rather than failing.
    /// </summary>
    public int MaxStackSize(Wallet wallet)
    {
        var declared = itemHelper.GetItem(WalletInfo.For(wallet).Tpl).Value?.Properties?.StackMaxSize;

        if (declared is null)
        {
            return int.MaxValue;
        }

        if (declared < 1)
        {
            log.Error(
                $"{wallet} reports a maximum stack of {declared}, which cannot be honoured. "
                + "Treating it as 1 -- an item mod has set something impossible.");
            return 1;
        }

        return (int)declared;
    }

    private static IEnumerable<Item> StacksOf(PmcData pmcData, MongoId tpl) =>
        pmcData.Inventory?.Items?.Where(item => item.Template == tpl) ?? [];
}
