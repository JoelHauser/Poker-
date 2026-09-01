# Poker -- working notes for Claude

A poker table for the SPT hideout. Server mod in C# (.NET 10) against SPT 4.1.3,
with a BepInEx client plugin. Players stake roubles, dollars, euros, GP coins,
bitcoin or Lega medals.

Sibling project to **Blackjack** (`../Blackjack`), which is shipped and working at
1.0.2. Most of what is written here was learned there, the expensive way. When
something below says "already solved", it means there is working code in that repo
to port rather than rediscover.

This file is loaded automatically at the start of every session. Keep it to things
a fresh session would otherwise rediscover the hard way -- not a chronological
diary. **Update "Current state" when you finish a piece of work.**

---

## The variant is not decided yet

**This is the open question that shapes everything else, and it is not settled.**
Until it is, only variant-independent work should be done -- which so far is the
whole of `Poker.Game`.

The constraint that drives the decision: **poker needs opponents, and SPT is a
single-player offline server.** There is nobody else at the table. Blackjack did
not have this problem, because its dealer makes no decisions at all -- it draws to
17 and stops.

Three ways out, in ascending order of pain:

1. **Video poker (Jacks or Better)** -- no opponents, no dealer, player against a
   fixed paytable. Nearly a drop-in for the Blackjack architecture. Also barely
   poker.
2. **House-banked table poker -- Three Card Poker, Caribbean Stud, Ultimate Texas
   Hold'em.** Real hand rankings and real betting decisions, but the dealer plays a
   fixed qualifying rule, so there is no AI. This is the recommended band.
3. **Texas Hold'em against bots.** Needs believable opponents, pot management, side
   pots, position, blinds. The AI is the whole project and the SPT integration is a
   footnote.

Recommendation on record: **Ultimate Texas Hold'em** -- community cards and hole
cards so it reads as poker on screen, genuine player decisions (check, or bet
4x/2x/1x and when), dealer is a lookup table, house-banked so per-hand settlement
survives intact.

## The single most important fact

**SPT 4.x server mods are C#, not TypeScript.** The `mod.ts` / `package.json` /
tsyringe world ended at 3.x, and most guides online still describe it. Server mods
are .NET 10 class libraries referencing `SPTarkov.Server.Core`, with an
`IModMetadata` record in place of `package.json`.

`SptVersion` in that record is a **hard load gate**. It is `~4.1.3` (>=4.1.3
<4.2.0). A mod outside the range loads nothing and logs nothing.

## Layout

| Project | Owns |
| --- | --- |
| `src/Poker.Game` | Rules engine. No SPT reference, no I/O, no clock. |
| `tests/Poker.Game.Tests` | 44 tests over the evaluator. |

Still to come, mirroring Blackjack: `src/Poker.Server`, `src/Poker.Client`,
`tests/Poker.Server.Tests`, `tools/Poker.Console`, `scripts/smoke.ps1`.

The engine knows nothing about currency -- it takes an `int` and returns an `int`.
Everything that maps a `Wallet` to an item template belongs in `Wallets.cs` and
`Bank.cs` when they are ported. Keep it that way; it is what makes the rules
testable.

## What is done

**`Poker.Game` is variant-independent and finished for now.**

- `Card` / `Rank` / `Suit` -- **Ace is high at 14**, the opposite of the Blackjack
  engine where Ace was 1 and the hand applied the 11. Poker never adds ranks, only
  orders them. The two-character wire form (`AS`, `TH`, `2C`) is deliberately
  identical to Blackjack's so the client card art and parsing port unchanged.
- `Deck` -- single deck, freshly shuffled each hand. Simpler than Blackjack's shoe
  on purpose: a shoe exists so several decks can be dealt to a cut card, which
  matters only because blackjack is beatable by tracking what has gone.
  `Deck.Stacked("AS KS ...")` pins a deal for tests, same idea as `Shoe.Stacked`.
- `HandRank` -- category plus kickers packed into one int, so comparison is a
  single integer compare rather than a walk down two kicker lists. `Describe()`
  gives the table-side reading ("Full house, fours over nines").
- `HandEvaluator` -- ranks 5 to 7 cards. Best-of-seven is a brute-force walk of all
  21 combinations, deliberately: this runs dozens of times a hand, not millions, so
  the only thing worth optimising for is being correct on inspection.

### The evaluator is trustworthy, and here is why

`HandDistributionTests` deals **all 2,598,960 distinct five-card hands** and checks
the category counts against the published figures (40 straight flushes, 624 quads,
3,744 full houses, 5,108 flushes, 10,200 straights, 54,912 trips, 123,552 two pair,
1,098,240 pairs, 1,302,540 high card, 4 royals). An evaluator that misreads one
hand in the deck lands off them.

Mutation-checked, per the rule below. Each of these was introduced and the suite
caught it: wheel reported ace-high (4 fail), straight flush not checked (8 fail),
two-pair kickers reversed (2 fail), full-house pair ignored (7 fail). Do this again
after touching the evaluator.

## Things that will bite you

Carried over from Blackjack. Each cost real time there. None are hypothetical, and
all of them still apply to this mod.

- **`new ItemEventRouterResponse()` is not a usable response.** Its constructor
  initialises nothing, and `RemoveItemByCount` reaches into
  `output.ProfileChanges[sessionId]`, so a hand-built one throws
  NullReferenceException -- *after* the items are already gone. Get one from
  `EventOutputHolder.GetOutput(sessionId)`.
- **A mod can change any item's stack limit.** Read `StackMaxSize` live rather than
  assuming the database value.
- **`PaymentService` cannot settle a bet.** Both entry points derive currency from a
  trader. Walk item stacks directly, as `Bank` does.
- **`AddItemToStash` can decline an item without throwing.** A full stash silently
  swallows a payout. Compare the balance either side of every move against what was
  intended and post the shortfall as mail rather than losing it.
- **A custom static route does not update the client's inventory.** Money lands in
  the profile but the stash view stays stale until reload, which reads to a player
  as the mod eating their winnings. Use item-event actions for the real client.
- **The table is in memory and the stake is not.** Record every stake in escrow
  until settlement and refund orphans, or a crash mid-round takes the money and
  leaves no hand.
- **State routes are called before any hand exists.** An empty table must describe
  itself rather than indexing into cards that are not there.
- **Naming a property `Path` shadows `System.IO.Path`** inside the same class and
  breaks every `Path.Combine`.
- **`OnLoadOrder` has no `PostDBModLoader`.** Values are `Watermark`, `Preload`,
  `GameCallbacks`, `TraderRegistration`, `Routers`, `HandbookCallbacks`,
  `SaveCallbacks`, `TraderCallbacks`, `PresetCallbacks`, `RagfairCallbacks`,
  `PostLoad`.
- **SPT's DI registers a class against every non-System interface it implements**
  (`DependencyInjectionHandler.InjectAll`), so `Bank : IBank` resolves for free.
- **Bash heredocs mangle backslashes.** Writing C# with `'\\'` through
  `cat <<'EOF'` produces broken escapes. Use the Write tool for those files.
- **`Compress-Archive` writes backslash zip entries**, which extract as one literal
  filename on Linux. Pack releases with `System.IO.Compression` instead.

## Talking to the server without a game client

All read out of 4.1.3 and confirmed against a running server, over in Blackjack.
`scripts/smoke.ps1` there is a working reference to port.

- **It serves HTTPS, not HTTP**, on the same port, with a self-signed certificate it
  generates into `user\certs\`. .NET rejects that by default and reports "the
  underlying connection was closed", which reads as the server being down.
- **Every request body is zlib-inflated and every response deflated.** Two headers
  opt out: `requestcompressed: 0` and `responsecompressed: 0`.
- **Request bodies are matched case-sensitively.** Send PascalCase, or every
  property silently takes its default.
- **Enums go over the wire as integers, not names** -- unless made strings
  deliberately. Blackjack shipped integers and regretted it. **Make the wire enums
  strings here from the start.**
- **The session id is a `PHPSESSID` cookie.** In PowerShell it cannot be passed via
  `-Headers` -- `Cookie` is restricted and dropped **silently**. Use a
  `WebRequestSession`.

## The payout scale is this mod's new problem

Blackjack's biggest payout was 1.5:1, which is why backing valuables down to even
money was enough. Poker paytables reach 50:1 and beyond -- some variants pay 500:1
on a royal.

Bitcoin and Lega medals have a **`StackMaxSize` of 1**. A 500:1 payout on a
10-bitcoin bet is 5,000 individual items needing 5,000 free grid cells. The
shortfall-to-mail path catches it rather than losing the money, but mail has
attachment limits too, and "you won, here are 40 letters" is not an outcome.

**Unresolved.** Options: much lower ceilings on valuables, a capped paytable for
valuables, or valuables restricted to the low-variance bets. Decide this before the
paytable is written, not after.

## Wallets, as verified on a real 4.1.3 install

| Wallet | Template | StackMaxSize |
| --- | --- | --- |
| Roubles | `5449016a4bdc2d6f028b456f` | 1,000,000 |
| Dollars | `5696686a4bdc2da3298b456a` | 50,000 |
| Euros | `569668774bdc2da2298b4568` | 50,000 |
| GP coins | `5d235b4d86f7742e017bc88a` | 100 |
| Bitcoin | `59faff1d86f7746c51718c9c` | **1** |
| Lega medal | `6656560053eaaa7a23349c86` | **1** |

The 4.1.3 namespaces, which are not what older docs say:
`Helpers.Profile.InventoryHelper`, `Helpers.Profile.ProfileHelper`,
`Helpers.Items.ItemHelper`, `Services.Commerce.MailSendService`,
`Servers.SaveServer`, `Common.Models.Logging.ISptLogger<T>`.

Building against NuGet 4.1.2 is safe on a 4.1.3 install -- verified for Blackjack
across 36 types and 63 members.

## Architecture, once the server exists

Server-authoritative. The client renders what it is handed and sends intents; it
never sees a hidden card, never draws, never decides an outcome. Mirror Blackjack:

```
PokerService              the whole game flow, on IBank / IProfileGateway /
                          IStatsStore / IEscrowStore. No SPT types but MongoId.
PokerCallbacks            static routes  -- curl testing
PokerItemEventCallbacks   item events -- the game client
Bank / ProfileGateway     the only classes that touch SPT services
```

Two transports, one service. Do not put game logic in either adapter. The interface
seams exist because `InventoryHelper`, `ProfileHelper` and `SaveServer` are concrete
classes with non-virtual methods.

Note `EscrowStore.Hold` **accumulates** rather than replaces -- written for
blackjack's double and split, and exactly the semantics multi-street poker betting
needs.

## Decisions inherited from Blackjack

These were settled there against the real client and apply unchanged.

- **Not a new hideout area.** `EFT.EAreaType` ends at `CircleOfCultists = 27` and
  each area has a baked prefab. A new value has no model.
- **The entry point is a button on `EFT.UI.MenuScreen`**, cloned from an existing
  `DefaultUIButton` field. `Awake` and `Show` are the patch points.
- **Guarding against play-in-raid is the mod's job.** Nothing enforces it.
- **The panel floats over a dimmed hideout**, so freeing the cursor and swallowing
  player input is a hard requirement.
- **No hotkey.** A key would be reachable from anywhere, including a raid.
- **Per-hand settlement, straight to the stash.** No session, no chips, no buy-in.
  Mail only when the stash cannot take the winnings.

## Conventions

- **Comments explain why, not what** -- ideally naming the failure the code
  prevents. The codebase is deliberately heavy on rationale.
- Prose in comments uses `--`, not em dashes.
- Tests are named as the rule they pin, not the method they call.
- Every tunable a player might argue about lives in `Rules` or `WalletInfo`.

## Verifying

```
dotnet test    # 44 tests, no SPT needed
```

**Distrust a suite that passes first time.** Mutation-check anything that ranks a
hand or moves money -- see the evaluator note above for the pattern, and Blackjack's
`MoneyInvariantTests` for the money equivalent once there is money to move.

---

## Current state

**Update this section as work completes.**

- Repo scaffolded, `Poker.Game` complete and green at 44 tests, mutation-checked.
- **Blocked on the variant decision** above. The evaluator, deck and card model are
  needed by every variant, so they were built first; the table state machine cannot
  be until the variant is chosen.
- Nothing SPT-facing exists yet -- no server project, no client plugin, no routes.

### Open items

- **Choose the variant.** Everything else waits on it.
- **Decide the valuables payout policy** before writing a paytable.
- **Make the wire enums strings** from the start, unlike Blackjack.
- Port `Bank`, `ProfileGateway`, `EscrowStore`, `StatsStore` from Blackjack largely
  as-is -- they are currency plumbing and carry no blackjack rules.
- Port `CardView` / `Textures` from `Blackjack.Client` (~670 lines of card
  rendering) and `MenuButtonPatch` (menu entry, raid guard, `PluginValidator`).
- Fork rather than share a common assembly: two SPT mods each shipping their own
  build of a same-named DLL into one process is a load conflict waiting to happen,
  and the shared code is only ~800 lines.
