# Poker -- working notes for Claude

A poker table for the SPT hideout. Server mod in C# (.NET 10) against SPT 4.1.3,
with a BepInEx client plugin. Players stake roubles, dollars, euros, GP coins,
bitcoin or Lega medals.

Sibling project to **Blackjack** (`../Blackjack`), which is shipped and working at
1.0.2. **The core of this mod comes from there.** Most of what is written here was
learned there, the expensive way. When something below says "port", it means there
is working, shipped code in that repo to copy rather than rediscover -- see "What
Blackjack gives this mod" for the file-by-file list.

This file is loaded automatically at the start of every session. Keep it to things
a fresh session would otherwise rediscover the hard way -- not a chronological
diary. **Update "Current state" when you finish a piece of work.**

---

## The variant: Ultimate Texas Hold'em, with AI seat-mates

**Decided.** This was the open question that shaped everything else and it is now
settled.

The constraint that drove it: poker needs opponents, and SPT is a single-player
offline server. UTH answers that by being **house-banked** -- every seat plays its
own hand against a dealer who follows a fixed qualifying rule, so there is no
opponent AI standing between the mod and a working game. It still reads as poker on
screen: hole cards, community cards, real decisions about when and how much to bet.

**The AI players are seat-mates, not opponents.** They sit at the other seats,
receive their own hole cards from the same deck, share the same community cards and
the same dealer hand, and play their own hands against the dealer. They can neither
win nor lose the player's money. They exist for pacing, reactions and the feeling of
a table.

This distinction is load-bearing and easy to lose. It was briefly decided the other
way -- bots contesting a shared pot -- and reversed the same hour, because that is
not Ultimate Texas Hold'em at all: it is multiway hold'em, the dealer stops being
the house, and the bot AI becomes the entire project with the SPT integration as a
footnote. **If a future session finds itself designing bet sizing, position play or
bluffing, it has drifted off this decision.**

Consequences worth stating plainly:

- **Per-hand settlement survives.** No chips, no buy-in, no session bankroll. The
  player stakes real currency, the hand resolves, the money comes back. This is
  Blackjack's model unchanged, which is exactly why the whole money path ports.
- **Bot money is notional.** It never touches a profile, never becomes an item, and
  is never persisted. Nothing in `Bank` should ever be called for a bot.
- **`PotBuilder` is not used.** Side pots exist only where seats contest a shared
  pot. See "What is done" for what to do with it.

### The rules, as this mod will implement them

Written down because settlement is where a variant is usually got subtly wrong, and
because half the tables online describe a different Blind paytable.

- **Ante and Blind are equal and mandatory.** **Trips** is an optional side bet.
- **Play is made exactly once**, and its size depends on when:

  | When | Play size |
  | --- | --- |
  | On the hole cards, before any community card | 3x or 4x Ante |
  | After the flop (3 community cards) | 2x Ante |
  | After the river (all 5) | 1x Ante, or fold |

  Checking is free at the first two points. At the river the choice is 1x or fold --
  there is no third check.
- **The dealer qualifies with a pair or better**, made from the dealer's two cards
  and the five community cards. `HandRank` already exposes the category, so this is
  a comparison and not a special case.
- **Settlement**, once the hands are compared:

  | Case | Ante | Play | Blind |
  | --- | --- | --- | --- |
  | Player folds | loses | never made | loses |
  | Player beats a qualified dealer | 1:1 | 1:1 | paytable |
  | Player beats a dealer who did not qualify | **push** | 1:1 | paytable |
  | Tie | push | push | push |
  | Player loses | loses | loses | loses |

  Trips resolves on the player's own hand and ignores the dealer entirely -- it pays
  even when the dealer wins.
- **The Blind pays only on a straight or better**, and pushes below that even on a
  winning hand. Standard paytable: royal flush 500:1, straight flush 50:1, quads
  10:1, full house 3:1, flush 3:2, straight 1:1. **See "The payout scale" -- the
  500:1 is not survivable on every wallet and is capped for valuables.**
- House edge is about 2.2% of the Ante under correct play, which is the number to
  sanity-check a simulation against.

## The single most important fact

**SPT 4.x server mods are C#, not TypeScript.** The `mod.ts` / `package.json` /
tsyringe world ended at 3.x, and most guides online still describe it. Server mods
are .NET 10 class libraries referencing `SPTarkov.Server.Core`, with an
`IModMetadata` record in place of `package.json`.

`SptVersion` in that record is a **hard load gate**. It is `~4.1.3` (>=4.1.3
<4.2.0). A mod outside the range loads nothing and logs nothing. Silence at startup
means the gate, not a bug in the game code.

## Layout

| Project | Owns |
| --- | --- |
| `src/Poker.Game` | Rules engine. No SPT reference, no I/O, no clock. |
| `tests/Poker.Game.Tests` | 85 tests over the evaluator, the pot builder, the log and the paytables. |

Still to come, mirroring Blackjack: `src/Poker.Server`, `src/Poker.Client`,
`tests/Poker.Server.Tests`, `tools/Poker.Console`, `scripts/smoke.ps1`.

The engine knows nothing about currency -- it takes an `int` and returns an `int`.
Everything that maps a `Wallet` to an item template belongs in `Wallets.cs` and
`Bank.cs` when they are ported. Keep it that way; it is what makes the rules
testable.

## What Blackjack gives this mod

Roughly **1,400 lines of server plumbing that ports nearly unchanged** and **~800
lines of client card rendering**. It is shipped code that has moved real money on a
real profile. Read the original before rewriting anything here from scratch.

Line counts are from the working tree, as a sense of what each piece costs.

### Ports essentially as-is -- rename the namespace and go

| File | Lines | Notes |
| --- | --- | --- |
| `src/Blackjack.Server/Bank.cs` | 295 | The money. Stack walking, live `StackMaxSize`, balance checks either side of every move, shortfall-to-mail. Carries no blackjack rules at all. |
| `src/Blackjack.Server/ProfileGateway.cs` | 38 | `HasProfile` / `SaveAsync`. |
| `src/Blackjack.Server/Abstractions.cs` | 70 | `IBank`, `IProfileGateway`, `IStatsStore`, `IEscrowStore`. The seams that make the service testable with no server. |
| `src/Blackjack.Server/Wallets.cs` | 90 | Six wallets, templates, symbols, per-wallet limits. **Retune the limits** -- see "The payout scale". |
| `src/Blackjack.Server/Escrow.cs` | 146 | Records a stake until settlement, refunds orphans on next contact. `Hold` accumulates, which is what UTH's late Play bet needs. |
| `src/Blackjack.Server/BlackjackLog.cs` | 75 | Logger with a verbosity switch and the mod folder. See "Logging". |
| `src/Blackjack.Server/ModMetadata.cs` | 27 | New name and URL; the `~4.1.3` range is unchanged. The GUID is **`com.mybutthasarash.poker`** -- see "Releasing". |
| `src/Blackjack.Server/Startup.cs` | 50 | Boot banner. Retune the lines it prints. |
| `src/Blackjack.Game/EnumJson.cs` | 67 | `StringEnumListConverter`. Needed the moment a view carries a list of available actions. |
| `src/Blackjack.Client/Textures.cs` | 461 | Every sprite drawn in code -- rounded boxes, chips, felt. The mod ships no art. |
| `src/Blackjack.Client/CardView.cs` | 207 | Draws one card from its two-character code, with a drawn fallback when the art is absent. **`Card.Code` here is deliberately identical to Blackjack's, so this ports untouched.** |
| `src/Blackjack.Client/MenuButtonPatch.cs` | 379 | Menu entry, the end-of-frame clone trick that makes it survive menu mods, raid guard. |
| `src/Blackjack.Client/BlackjackClientPlugin.cs` | 86 | BepInEx entry point, config binding. |
| `src/Blackjack.Client/ProfileSync.cs` | 81 | Keeps the client's stash view in step after the table moves money. |
| `src/Blackjack.Client/BlackjackApi.cs` | 77 | The client's side of the transport. |
| `scripts/smoke.ps1` | 259 | Drives a real server with no game attached. The HTTPS, compression and cookie handling in it is the expensive part. |
| `tools/Blackjack.Console/Program.cs` | 100 | Terminal table. Worth more here than there: it can watch the bots play thousands of hands with no Unity. |

### Ports as a shape, with different contents

| File | Lines | What changes |
| --- | --- | --- |
| `src/Blackjack.Server/BlackjackService.cs` | 295 | The flow -- validate, let the engine decide, move money to match, save -- is the model to copy exactly. The requests differ: an Ante/Blind/Trips bet, then Play or Check, then Play or Fold. |
| `src/Blackjack.Server/Contracts.cs` | 146 | Same job, different verbs. Keep `PingResponse` almost verbatim: it answers "did the mod load, did the session resolve, can the money be read" and is the first thing worth having. |
| `src/Blackjack.Server/BlackjackCallbacks.cs` | 148 | Static routes, for curl. |
| `src/Blackjack.Server/BlackjackItemEventCallbacks.cs` | 87 | Item events, for the real client. |
| `src/Blackjack.Server/BlackjackRouter.cs` + `BlackjackItemEventRouter.cs` | 96 | Route registration for both transports. |
| `src/Blackjack.Server/TableStore.cs` | 70 | Live tables keyed by session, in memory on purpose. Now holds seat-mates too. |
| `src/Blackjack.Server/Stats.cs` + `StatsStore.cs` | 244 | The persistence ports; the recorded fields do not. Blackjack outcomes do not map onto UTH -- hands played, Play bets made at each size, Blind hits by category. |
| `tests/Blackjack.Server.Tests/Fakes.cs` | 135 | Fake bank, profile, stats and escrow. The reason the money tests need no server. |
| `tests/Blackjack.Server.Tests/MoneyInvariantTests.cs` | 78 | Plays 400 random rounds and checks the money moved equals the profit the engine reported. **Port this before writing settlement, not after.** |

### Does not port

- `src/Blackjack.Client/BlackjackPanel.cs` (1,696 lines) -- the layout is blackjack
  shaped: one hand, one dealer, a hit/stand strip. A UTH table is several seats,
  five community cards and three bet spots. The *techniques* carry (nine-sliced
  sprites, the felt, cursor handling, the settled-round strip); the layout does not.
- `src/Blackjack.Game/*` -- already replaced. `Card`, `Deck`, `HandRank` and
  `HandEvaluator` exist here and are better suited to poker than the blackjack
  originals.

### Fork, do not share

Two SPT mods each shipping their own build of a same-named DLL into one process is a
load conflict waiting to happen. The shared code is ~800 lines. Copy it.

## Logging

**Everything in this project logs, and the logging is part of how it is tested.**
Two mechanisms, because the engine and the mod have different constraints.

### In the engine: `IGameLog`

`src/Poker.Game/GameLog.cs`. The engine has no SPT reference and no I/O, so it
cannot take a logger -- it takes a *sink*.

- **Off by default.** Every class defaults to `GameLog.Null`, so nothing allocates
  and nothing prints unless a caller asks for it.
- **Guard every call site with `_log.Enabled`.** This is not ceremony. The
  distribution test evaluates all 2,598,960 five-card hands; building a log string
  per hand that is then discarded turns a 1-second test into minutes.
- **`ListGameLog` is the test seam.** It captures lines in memory, so a test can
  assert on the engine's *reasoning* -- that a refund was decided, that a layer
  collapsed -- and not merely on its output. `GameLogTests` is the pattern.
- `DelegateGameLog` adapts it to anything else: `Console.WriteLine` in the console
  tool, `PokerLog` on the server.

Log the decision, not the arithmetic. A line saying which branch was taken and why
is worth ten lines reciting numbers the caller already has.

### On the server: `PokerLog`

Port `BlackjackLog` (75 lines). It wraps `ISptLogger<T>`, knows the mod folder, and
has a **verbose switch in `config.json`** so `log.Detail(...)` can be left in place
around every rouble and turned off once things work. Blackjack's `Bank` is the model
for how much to log on the money path: every debit and credit says what it intended,
what it did, and shouts when those disagree.

## What is done

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
- `GameLog` -- the engine's logging seam. See "Logging".
- `PotBuilder` -- correct, tested, mutation-checked, and **not used by UTH**. Side
  pots need a shared pot and UTH has none. It is kept because it costs nothing and a
  multiway mode may still be wanted one day, but **no settlement code should grow a
  dependency on it**. If a session finds itself wiring it into a UTH hand, that is
  the signal something has gone wrong.

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

### The pot builder is trustworthy too, with one caveat worth keeping

Mutation-checked the same way. Each was introduced and the suite caught it:
uncalled bet never refunded (2 fail), folded seats ignored when finding the matched
level (5 fail), unwinnable layers not collapsed (4 fail), folded seats left eligible
(3 fail), layer ceiling not advanced (8 fail).

The two-fail case is the one to remember. `EveryChipCommittedIsEitherPottedOrRefunded`
does **not** catch a missing refund -- the chips simply stay in the pot, so the books
still balance. Conservation is necessary and nowhere near sufficient: money can be
conserved and still settle to the wrong seat. **The same trap is waiting in UTH
settlement**, where three bets resolve on different rules and a total can come out
right with the Blind and the Play swapped.

### The paytables are data, and the reasons are in the code

`Paytable.cs` holds `Payout` (odds, not a multiplier, so 3:2 stays exact) and the
three standard tables. `Rules.cs` chooses between them.

Three decisions in there that a reader will otherwise undo:

- **A push and a loss are both `Payout` values**, not states the caller tracks. The
  Blind pushes beneath a straight and Trips loses beneath trips, and settlement code
  that treats those alike takes money the player was never owed.
- **The royal row is `RoyalOnly`** and sits above the straight-flush row, because a
  royal is not a category -- it is an ace-high straight flush. Ignore that flag and
  every straight flush pays 500:1.
- **`BlindForValuables` exists for divisibility, not only for size.** Every row pays
  a whole number on a single unit; the standard table's 3:2 does not, and half a
  bitcoin does not exist.

Mutation-checked. Each was introduced and the suite caught it: royal row claims
every straight flush (1 fail), 3:2 rounds half down (1), the Blind loses beneath a
straight (1), Trips pushes instead of losing (2), a win returns winnings without the
stake (4), overflow wraps instead of throwing (1).

`NoTableEverPaysLessForABetterHand` is the one to keep when the tables are retuned.
A mistyped row is invisible to every single-hand test around it and shows up only as
one hand paying worse than a hand it beats.

## What UTH needs that does not exist yet

The engine is variant-independent so far. Everything below is new work.

- **`UltimateHoldemTable`** -- the state machine. Phases: AwaitingBet, PreFlop
  (hole cards dealt, 4x/3x/Check), Flop (2x/Check), River (1x/Fold), Showdown,
  Settled. It owns the deal order, the dealer's hand and the community cards.
- **A fixed, documented deal order.** Adding or removing a seat must not silently
  change which cards the player gets from a stacked deck, or every pinned test
  drifts at once. Decide it, write it in a comment, and test it.
- **Settlement across three bets.** Ante, Blind and Play resolve on different rules
  and the dealer's qualification only touches the Ante. This is the most likely
  place for a quiet error -- see the pot builder caveat above.
- **Two paytables** (Blind, Trips) as data, not code, so they can be capped per
  wallet without a second code path.
- **The bot seat-mates**, below.
- **A strategy oracle**, eventually: UTH has a known correct strategy, and the same
  table that drives the bots can drive an optional hint for the player.

## The AI seat-mates

Cheap by construction, and that is the point of choosing UTH.

- **They decide, they do not compete.** A bot needs one function: given its hole
  cards, whatever community cards are showing and the current street, return Check
  or a Play bet. Nothing about the player's hand or money enters into it.
- **UTH strategy is a lookup, not a search.** The published optimal strategy is a
  set of rules over hole-card classes and board texture. An approximation is fine
  and arguably better: a bot that always plays perfectly is a bot with no character.
- **Personality is a dial on that table**, not a different brain -- how loose the 4x
  range is, whether it takes marginal 2x spots, how often it folds the river. One
  strategy, a few dials, named seats.
- **Their RNG must be injectable**, exactly as `Deck`'s is, or bot behaviour cannot
  be pinned in a test.
- **A bot must never call `IBank`.** Notional money only. Worth an explicit test.
- **Every bot decision is logged** through `IGameLog`, with the reason. A table of
  seats that silently do things is untestable and unwatchable; the console tool
  should be able to print why seat 3 took 4x.
- **Their hole cards are hidden until showdown**, on the same rule as the dealer's:
  omitted from the payload entirely rather than blanked. See "Things that will bite
  you" -- Blackjack learned this on the hole card.

## The payout scale, and the ceilings it forces

This is the mod's new problem. Blackjack's biggest payout was 1.5:1, so backing
valuables down to even money was enough. UTH's Blind pays **500:1** on a royal.

Worst case per hand, as a multiple of the Ante, since the Play bet can be 4x:

| Bet | Stake | Returns at most |
| --- | --- | --- |
| Ante | A | 2A |
| Play | 4A | 8A |
| Blind (500:1) | A | 501A |
| **Total** | 6A | **511A** |

That number has to fit in a stash, in items, at the wallet's `StackMaxSize`.

**Decided: valuables get a capped Blind paytable.** Everything above a flush pays
3:1, so the top of the table stops at 4A rather than 501A and the worst case falls
to **14A**. The rule is printed on the table rather than hidden in a rounding
decision, which is the same principle as Blackjack paying naturals at even money in
valuables. Trips is capped the same way or offered in currency only -- it pays 50:1
at the top and has the identical problem.

**The ceilings in `Wallets.cs` must come down regardless.** Blackjack's limits were
written against a 1.5:1 payout and do not survive here:

- **Bitcoin and Lega medals have a `StackMaxSize` of 1** -- one item per unit, one
  grid cell each. Blackjack's bitcoin ceiling of 10 still gives 140 items under the
  capped table. An Ante ceiling of 1 or 2 is the honest answer.
- **Roubles are not automatically safe.** Blackjack's 500,000 maximum against the
  real 500:1 table is 255 million roubles, which is 255 stacks at a 1,000,000 stack
  limit -- past what a stash will take, and straight into mail. An Ante ceiling
  around 50,000 keeps the worst case near 26 stacks.
- Whatever is chosen, `Bank.Credit`'s shortfall-to-mail path is the backstop, not
  the plan. Mail has attachment limits too, and "you won, here are 40 letters" is
  not an outcome.

## Things that will bite you

Carried over from Blackjack. Each cost real time there. None are hypothetical, and
all of them still apply to this mod.

- **`new ItemEventRouterResponse()` is not a usable response.** Its constructor
  initialises nothing, and `RemoveItemByCount` reaches into
  `output.ProfileChanges[sessionId]`, so a hand-built one throws
  NullReferenceException -- *after* the items are already gone. That failure reported
  itself as "not enough roubles" while the stake had left the stash. Get one from
  `EventOutputHolder.GetOutput(sessionId)`.
- **A mod can change any item's stack limit.** Roubles cap at 1,000,000 in the base
  database and at 20,000,000 on a server running BarterItemsStacks. Read
  `StackMaxSize` live. Clamp it to at least 1: a limit of zero, which a careless
  item mod can produce, makes the splitting loops take zero each pass and hang a
  server thread rather than fail.
- **Stack limits cannot be reported at startup.** `PostLoad + 1` is not last --
  BarterItemsStacks rewrites them about half a second later. Report them on first
  contact instead, which is the earliest the answer is trustworthy.
- **`PaymentService` cannot settle a bet.** Both entry points derive currency from a
  trader. Walk item stacks directly, as `Bank` does.
- **`AddItemToStash` can decline an item without throwing.** A full stash silently
  swallows a payout. Compare the balance either side of every move against what was
  intended and post the shortfall as mail rather than losing it.
- **An item-event reply carries `ProfileChanges` and nothing else.** The round rides
  in the response's `ExtensionData`, or the client needs a second request for it.
- **A custom static route does not update the client's inventory.** Money lands in
  the profile but the stash view stays stale until reload, which reads to a player
  as the mod eating their winnings. Use item-event actions for the real client.
- **`[JsonConverter]` on the enum type is not enough.** System.Text.Json resolves
  converters property attribute first, then `options.Converters`, then the type
  attribute -- and SPT registers `EftEnumConverterFactory` into `options.Converters`,
  which outranks anything declared on the enum. Blackjack's enums kept serialising
  as integers until the attributes moved onto the **properties** of the view record.
  Do it that way from the start.
- **The table is in memory and the stake is not.** Record every stake in escrow
  until settlement and refund orphans, or a crash mid-round takes the money and
  leaves no hand. In UTH the Play bet is collected later than the Ante, which is
  exactly why `EscrowStore.Hold` accumulates rather than replaces.
- **State routes are called before any hand exists.** An empty table must describe
  itself rather than indexing into cards that are not there. Blackjack's
  `DealerView` threw on a fresh table and would have failed every visit to the panel.
- **Naming a property `Path` shadows `System.IO.Path`** inside the same class and
  breaks every `Path.Combine`.
- **`OnLoadOrder` has no `PostDBModLoader`.** Values are `Watermark`, `Preload`,
  `GameCallbacks`, `TraderRegistration`, `Routers`, `HandbookCallbacks`,
  `SaveCallbacks`, `TraderCallbacks`, `PresetCallbacks`, `RagfairCallbacks`,
  `PostLoad`.
- **SPT's DI registers a class against every non-System interface it implements**
  (`DependencyInjectionHandler.InjectAll`), so `Bank : IBank` resolves for free.
- **The client plugin must be built against the install it runs on.** 4.1.3's
  `PluginValidator` reads a plugin's references to `spt-*` and requires a
  major.minor match. It targets `net472`, not .NET 10, because it runs inside the
  game's mono runtime.
- **Bash heredocs mangle backslashes**, and a long one containing quotes will fail
  to parse outright. Use the Write tool for C# and for any large file.
- **`Compress-Archive` writes backslash zip entries**, which extract as one literal
  filename on Linux. Pack releases with `System.IO.Compression` instead.

## Talking to the server without a game client

All read out of 4.1.3 and confirmed against a running server, over in Blackjack.
`scripts/smoke.ps1` there is a working reference to port.

- **It serves HTTPS, not HTTP**, on the same port, with a self-signed certificate it
  generates into `user\certs\`. .NET rejects that by default and reports "the
  underlying connection was closed", which reads as the server being down.
- **Every request body is zlib-inflated and every response deflated.** Two headers
  opt out: `requestcompressed: 0` and `responsecompressed: 0`. Without them a plain
  JSON body dies inside `Inflater` complaining about an unsupported compression
  method.
- **Request bodies are matched case-sensitively.** Send PascalCase, or every
  property silently takes its default -- which made a 10,000 bet arrive as 0 while a
  field with a sensible default looked like it had bound correctly.
- **Enums go over the wire as integers, not names** unless made strings
  deliberately. Blackjack shipped integers and regretted it. **Make the wire enums
  strings here from the start**, with the property-level attributes noted above.
- **The session id is a `PHPSESSID` cookie.** In PowerShell it cannot be passed via
  `-Headers` -- `Cookie` is restricted and dropped **silently**, and the server then
  says "session id provided was empty". Use a `WebRequestSession`.

## Whether there is an SPT install depends on the machine

| Machine | Installs |
| --- | --- |
| Joel's Windows box | `H:\SPT4.1.X` (4.1.3) and `H:\SPT2026` (4.0.13) |

With an install present, item templates live at
`SPT_Runtime/SPT_Data/database/templates/items.json`, the server assemblies at
`SPT_Runtime/SPTarkov.*.dll`, and `EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll`
is what the client plugin needs. **Reflecting over the installed assemblies beats
reflecting over the NuGet package**, which tops out at 4.1.2. Mono.Cecil ships with
the game at `BepInEx/core/Mono.Cecil.dll` and reads them without loading them.

Building against NuGet 4.1.2 is safe on a 4.1.3 install -- verified for Blackjack
across 36 types and 63 members.

Without an install, .NET 10 file-based apps make the package a one-liner:

```csharp
// probe.cs, run with: dotnet run probe.cs
#:package SPTarkov.Server.Core@4.1.2
var asm = typeof(SPTarkov.Server.Core.Models.Eft.Profile.SptProfile).Assembly;
var t = asm.GetTypes().First(x => x.Name == "MailSendService");
foreach (var m in t.GetMethods()) Console.WriteLine(m);
```

Source lives at `github.com/sp-tarkov/server-csharp` under
`Libraries/SPTarkov.Server.Core/`.

### The test profiles on Joel's box

Profile `6a8cd3a7e0b8272790f41285` ("test", level 69) is the sandbox -- roughly
499M roubles, 500M dollars, 500M euros, 5,000 GP coins. The other profile,
`6a7501c247d2e12a3892aaee` ("SCOOP", level 16), is the real one; leave it alone.

**Bitcoin and Lega medals are both at zero there**, so the two wallets with a
`StackMaxSize` of 1 -- the riskiest payout path -- cannot be exercised by betting
until some are added.

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

## Architecture, once the server exists

Server-authoritative. The client renders what it is handed and sends intents; it
never sees a hidden card, never draws, never decides an outcome. Mirror Blackjack:

```
PokerService              the whole game flow, on IBank / IProfileGateway /
                          IStatsStore / IEscrowStore. No SPT types but MongoId.
PokerCallbacks            static routes  -- curl testing
PokerItemEventCallbacks   item events -- the game client
Bank / ProfileGateway     the only classes that touch SPT services
PokerLog                  the one place that knows how to write a line
```

Two transports, one service. Do not put game logic in either adapter. The interface
seams exist because `InventoryHelper`, `ProfileHelper` and `SaveServer` are concrete
classes with non-virtual methods.

The bots live **inside the engine**, not in the service. They are part of what the
table does when a street advances, and they must be exercisable from
`tools/Poker.Console` with no server present.

## Decisions inherited from Blackjack

These were settled there against the real client and apply unchanged.

- **Not a new hideout area.** `EFT.EAreaType` ends at `CircleOfCultists = 27` and
  each area has a baked prefab. A new value has no model.
- **Not the Rest Space either.** It has a whole game-disc system in it that would
  have solved the camera and cursor problems for free, but the disc player needs
  Rest Space 2, a generator and burning fuel, which locks a new profile out of the
  mod entirely. It stays available as an optional second entrance later.
- **The entry point is a button on `EFT.UI.MenuScreen`**, cloned from an existing
  `DefaultUIButton` field. `Awake` and `Show` are the patch points, and the clone
  happens at the **end of the frame** so it inherits whatever other menu mods did to
  the button it copies.
- **Guarding against play-in-raid is the mod's job.** Nothing enforces it.
- **The panel floats over a dimmed hideout**, so freeing the cursor and swallowing
  player input is a hard requirement.
- **No hotkey.** A key would be reachable from anywhere, including a raid.
- **Valuables are staked through EFT's own grid component**, dragged into a
  container. One item type per bet: a mixed stake has no coherent payout.
- **Per-hand settlement, straight to the stash.** No session, no chips, no buy-in.
  Mail only when the stash cannot take the winnings.
- **Settings a player might want to change live in the F12 BepInEx menu**, not in a
  server config file that needs a restart. It is single player; the person sending
  the request owns the server it is sent to.

## Conventions

- **Comments explain why, not what** -- ideally naming the failure the code
  prevents. The codebase is deliberately heavy on rationale.
- Prose in comments uses `--`, not em dashes.
- Tests are named as the rule they pin, not the method they call.
- Every tunable a player might argue about lives in `Rules` or `WalletInfo`.
- **Everything logs.** See "Logging" -- through `IGameLog` in the engine, through
  `PokerLog` on the server, off by default, and never by building a string that is
  then thrown away.

## Verifying

```
dotnet test    # 85 tests, no SPT needed
```

**Distrust a suite that passes first time.** Mutation-check anything that ranks a
hand or moves money -- see the evaluator and pot builder notes above for the
pattern, and port Blackjack's `MoneyInvariantTests` before writing settlement
rather than after.

A UTH settlement suite has one check the others do not: **simulate a few hundred
thousand hands under correct play and confirm the house edge lands near 2.2% of the
Ante.** A settlement bug large enough to matter moves that number, and no single
pinned hand would catch it.

On a machine with SPT, `scripts\smoke.ps1 -SessionId <id> -PingOnly` first. It
touches no money and proves the mod loaded, the route is reachable, the session
resolved and the profile can be read.

## Releasing

Mirror Blackjack: `releases/Poker-<ver>.zip`, laid out as `user/mods/Poker/` so it
extracts into an SPT install. The version lives in **two** places and they must
agree: the server csproj `<Version>` and `ModMetadata.Version`. SPT's own assemblies
are not bundled -- the server provides them.

**The mod GUID is `com.mybutthasarash.poker`**, and **both halves declare it
unchanged** -- `ModMetadata.ModGuid` on the server and `[BepInPlugin]` on the client
plugin, with no `.client` suffix on either. The Forge checks that the two halves
agree with the GUID the mod is registered under and rejects an upload where they
differ. There is nothing to collide with: BepInEx keeps its own plugin registry and
SPT's mod GUID lives in the server metadata, so the two identifiers never meet.
Blackjack ships as `com.mybutthasarash.blackjack` on the same rule.

---

## Current state

**Update this section as work completes.**

- Working branch **`uth`**, off `main` at `9c4b9e9`.
- `Poker.Game` green at **85 tests**, mutation-checked -- evaluator (44),
  `PotBuilder` (17), the log seam (8) and the paytables (16).
- **The variant is decided** -- Ultimate Texas Hold'em with AI seat-mates.
- **The paytables are written** -- Blind, Blind-for-valuables and Trips, as data,
  with `Rules` selecting between them. The table state machine and the bots are
  still to come.
- Nothing SPT-facing exists yet -- no server project, no client plugin, no routes.

### Open items

- **Write `UltimateHoldemTable`.** The deal order, the three decision points and
  the three-bet settlement. This is the next real piece of work.
- **Set the wallet ceilings** from `Rules.WorstCaseReturnPerAnte` -- 511 antes on
  the standard table, 14 on the capped one -- once `Wallets.cs` is ported. The
  ceiling is a question about free grid cells, not about what the house can afford.
- **Decide how many seats the table has**, and **fix the deal order**, before any
  test pins a stacked deck. Changing either later invalidates every pinned deal at
  once.
- **Build the bots** once the table exists -- strategy table, personality dials,
  injectable RNG, a log line per decision, and a test that they never touch `IBank`.
- Port `Bank`, `ProfileGateway`, `EscrowStore`, `StatsStore`, `BlackjackLog` and
  `Fakes` from Blackjack largely as-is; they are currency plumbing and carry no
  blackjack rules.
- Port `CardView` / `Textures` / `MenuButtonPatch` from `Blackjack.Client`.
- **Make the wire enums strings** from the start, with property-level attributes.
- Decide whether `PotBuilder` stays. It is correct and free to keep, but it is dead
  code under UTH and dead code invites someone to use it.
