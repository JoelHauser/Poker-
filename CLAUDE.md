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

## The variant: no-limit Texas Hold'em against bots

**Decided, after two reversals.** Read the history before reopening it, because the
same argument has now been had three times.

The mod is a **Texas Hold'em cash game against AI opponents**. There is a pot, the
bots bet into it, and the player wins their chips or loses their own.

It was Ultimate Texas Hold'em for a while and a working UTH table was built and
tested before the decision changed -- see "Parked: the Ultimate Texas Hold'em
build". The thing that settled it: **UTH is house-banked and has no pot.** Every
seat there plays its own hand against the dealer, independently, and two players at
one table never take a chip from each other. That makes the seat-mates scenery, and
scenery was not what was wanted. Nobody bluffs anybody in UTH.

### The structure

- **No-limit**, with bots choosing from a **discrete menu of sizes** -- roughly a
  third of the pot, two thirds, pot, and all-in. This is how real poker AI is built
  and it is the difference between a bot that looks like a player and one that
  gives itself away instantly: naive no-limit sizing is the tell. The player is not
  restricted to the menu; it exists so the bots have a tractable decision.
- Small blind and big blind, a button that moves, and the four streets.
- Up to five seats including the player, as decided for UTH and unchanged.

### Where the money comes from, which is new and matters

**The bots' chips are notional -- a number on screen -- and the player's are real.**
So when the player drags a pot in, currency is created into their stash, and when
they lose one it is destroyed. The mod is a faucet and a sink.

This is a real departure and it has two consequences worth being deliberate about:

- **There is no fixed house edge any more.** UTH's economics were bounded by
  arithmetic at 2.185% of the ante. Here the rate at which currency appears is
  exactly the skill gap between the player and the bots. Beat weak bots and the mod
  prints roubles indefinitely. That is an accepted consequence of the decision, not
  an oversight -- but anything that makes the bots weaker also makes the mod a
  better faucet, which is a strange pressure to have on a difficulty dial.
- **Blackjack's "no chips, no buy-in" decision does not survive.** Hold'em cannot
  settle per hand: a stack is what a bet is sized against, what an all-in means, and
  what decides who is eligible for which side pot. The player **buys in** -- real
  currency debited, chips on the table -- and **cashes out** at the end.

  That makes escrow far more load-bearing than it was in Blackjack, and changes what
  it holds. `EscrowStore` recorded a *stake* until a hand settled. It must now record
  the player's **current stack**, updated as it changes, because a crash mid-session
  has to give back what they actually have rather than what they sat down with.

### What this buys, and what it costs

The bots become the product. They can take the player's money, which is the only way
a seat at a table ever feels like a person -- but it also means a flat bot ruins this
game far more thoroughly than it would have ruined UTH, where nobody was pretending
the other seats were players.

They do not have to be *good*, only **believable**, and that is a much lower bar than
it first appears. Rule-based play over a Monte Carlo equity estimate, with position
awareness and randomised aggression, reads as human. The expensive foundation for it
already exists: `HandEvaluator` is exhaustively verified and fast, and Monte Carlo
equity is built directly on it.

### What the decision changed in the code

- **`PotBuilder` is load-bearing again.** It was written first, then spent a day as
  dead code under UTH, and is now the settlement path. Already mutation-checked,
  side pots and uncalled-bet refunds included.
- **The payout-scale problem largely dissolves.** UTH's Blind paid 500:1 and forced
  ceilings down; a pot cannot pay more than the chips in it. See "The payout scale".
- The UTH table, its paytables and its strategy are parked, not deleted.

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
| `tests/Poker.Game.Tests` | 176 tests. The evaluator, pot builder, log, hold'em table and bots are live; the paytables, UTH table and strategy are parked. |

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
| `src/Blackjack.Server/Escrow.cs` | 146 | Records money taken but not settled, and refunds orphans on next contact. **Needs reworking, not just porting** -- it holds a stake, and hold'em needs it to hold the player's live stack. See "Open items". |
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
- `PotBuilder` -- splits what every seat committed into a main pot and however many
  side pots the all-ins require, and returns an uncalled bet rather than potting it.
  **The settlement path for hold'em.** Written first, spent a day as dead code under
  UTH, now load-bearing again. Mutation-checked -- see below.
- `StringEnumListConverter` -- ported from Blackjack, for a list of enums on the
  wire.
- `HoldemTable` / `HoldemSeat` / `HoldemRules` -- the game. Button, blinds, four
  streets, a full no-limit betting round, side pots and showdown. See "The betting
  round" below.
- `IPokerAgent` -- where a bot's decision comes from, **one instance per seat**. Its
  `PokerContext` carries that seat's own cards, the board, the stacks, the pot and
  what is legal -- never another seat's cards and never the deck.
- The whole Ultimate Texas Hold'em game -- **parked, not on the path**. See "Parked:
  the Ultimate Texas Hold'em build".

- `BotAgent` / `PokerPersonality` / `HandEquity` -- the opponents. Monte Carlo
  equity, pot odds, position, stack depth and eight characters over one decision
  procedure. See "How they actually decide".

## The betting round

The bug-dense part of hold'em, and where the tests are aimed. Settlement is
comparatively easy because `PotBuilder` already does it.

Rules that are each one line of code and each cost a real bug when missed:

- **Heads-up reverses the blinds.** The button posts the small blind and acts
  **first** before the flop, then last on every street after it. With three or more
  the button is last pre-flop and the seat after the big blind opens. This is the one
  everybody gets wrong.
- **After the flop the small blind opens**, which heads-up means the big blind. Two
  different orders for the two halves of a hand; using one for both is invisible for
  as long as every bot only checks.
- **Posting a blind is not acting.** That distinction, and nothing else, is what
  leaves the big blind its option to raise when the table has only called round to
  it.
- **A raise must be at least the size of the last one.** Otherwise a player can grind
  a round out in single chips and never let it close.
- **An all-in too small to be a full raise does not reopen the betting.** Seats that
  have already acted owe the difference and may call or fold, but may not raise
  again. Miss it and a short all-in becomes an unlimited raising war between two
  other players.
- **An uncalled bet comes back** rather than being counted as a pot that was won.
  `PotBuilder` already does this; the table only has to return it to the stack.
- **The odd chip on a split goes to the first winner left of the button.** Any rule
  will do; having none quietly destroys a chip a hand, and a table whose books drift
  is a bug nobody sees until the numbers are far apart.

### Chips are conserved, and that is the invariant to build against

Every hand starts with a known number of chips at the table and must end with the
same number. `ChipsAreNeitherCreatedNorDestroyed` fuzzes two to five seats through
three hundred hands of random aggression and checks it after every one. The ways to
break it all live in the betting round -- an uncalled bet kept, a side pot paid
twice, an odd chip dropped -- and none of them are settlement bugs.

Mutation-checked, nine faults, each caught: heads-up blinds reversed, no minimum
raise, a short all-in reopening the betting, posting a blind counting as acting, the
odd chip dropped, uncalled bets not returned, a round closing before everyone has
matched, the flop using the pre-flop order, and a seat betting more than it has.

**Three of those nine survived the first pass**, and all three were holes in the
tests rather than in the code:

- The big-blind-option test asserted on the *first* thing that seat was offered, and
  a table that closed the pre-flop round early simply offered it the same shape on
  the flop -- nothing to call, a raise available. **Assert the street.**
- The post-flop order tests only ever used bots that checked, so every order looked
  alike. Order is only visible when somebody bets: make the seat that should act
  first bet, then check that the next seat has something to call.
- The clamp stopping a seat betting more than it has is currently unreachable,
  because the options already cap every caller. Unreachable defensive code needs a
  direct test or it silently stops being true.

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

## Parked: the Ultimate Texas Hold'em build

A complete, tested UTH game is in the tree and is **not** on the path any more. It is
kept rather than deleted because it works and because the decision has moved twice
already. Do not build on it, and do not let it accrete: nothing new should call into
it.

`Paytable.cs`, `Rules.cs`, `UltimateHoldemTable.cs`, `Seat.cs`, `TableView.cs`,
`UthStrategy.cs`, `SeatMateAgent.cs`, plus their tests. Green, mutation-checked, and
worth reading before writing the hold'em equivalents -- several of its lessons are
about card games rather than about UTH.

The parts of it that are **not** UTH-specific and should carry across:

- **A fixed, documented deal order, pinned by a test.** Adding a seat changes which
  cards every later position receives, so a stacked-deck test is pinned to a seat
  count as well. If the order moves, every pinned deal breaks at once and the
  failures read as rules bugs. This is just as true in hold'em.
- **Hidden cards are absent from the view, not blanked.** Anything sent to the client
  is knowable by the client.
- **Conservation is necessary and nowhere near sufficient.** Money can be conserved
  and still settle to the wrong seat -- see the pot builder note above.
- **Mutation-check anything that ranks a hand or moves money.** Every settlement
  rule in the UTH table was introduced as a deliberate fault and caught: the Ante
  pushing only on a won hand (1 fail), the Blind paytable consulted on a tie (1),
  folding taking the Trips bet (1), a winning bet returning winnings without its
  stake (2), the dealer needing better than a pair (3), a third check at the river
  (1), the dealer dealt before the seats (5), hole cards visible from the deal (1).

The UTH-specific knowledge, compressed, in case it is ever picked up again:

- Settlement is three bets on three rules. The Ante pushes whenever the dealer fails
  to open, **including on a hand the seat lost** -- the natural misreading looks
  right in every winning hand anyone would test. The Blind pays its table on a win,
  pushes on a tie, and pushes rather than paying beneath a straight. Trips ignores
  the dealer and survives a fold.
- Paytables are data, not code, so the capped valuables table is a different table
  rather than a second path through settlement. A push and a loss are both `Payout`
  values for the same reason.
- **The river is computed, not looked up, and that was worth six points of house
  edge.** A rule of thumb -- bet a hidden pair or better, else fold -- folded 26% of
  hands where the real game folds about 19%, and each of those folds threw away two
  antes. Measured edge 8.4%. Walking all 990 possible dealer holdings instead gives
  the exact value of betting, at about four milliseconds a decision.
- **Folding is the expensive decision and every plausible heuristic forgets it.** A
  royal on the board cannot be beaten, so every dealer holding ties and betting is
  worth exactly zero -- and every rule of thumb folds it, which is the worst answer
  available.
- The edge still measured 5.4% against a published 2.185%, and the evidence pointed
  at which hands the pre-flop and flop lookups selected rather than at settlement.
  Unresolved, and only matters if UTH is revived.
- **Do not try to confirm a house edge by simulation.** At a standard deviation near
  4.9 antes a hand, a tenth of a point needs about nine million hands. Measure
  decision frequencies instead -- they are proportions, they converge in thousands
  rather than millions, and they are what caught the river bug.
## The bots

They are opponents now, not scenery, and they are the product. A flat bot ruins this
game in a way it never could have ruined UTH, where nobody was pretending the other
seats were players.

- **They have to be believable, not good.** Strong poker AI is a research problem;
  a bot that reads as a person is not. Rule-based play over a Monte Carlo equity
  estimate, with position awareness and randomised aggression, gets there -- and the
  expensive part already exists, because `HandEvaluator` is what equity is estimated
  with.
- **Bet sizing is the tell.** Naive no-limit bots give themselves away instantly by
  betting odd amounts. Bots choose from a discrete menu -- about a third of the pot,
  two thirds, pot, all-in -- which is both how real poker AI works and what makes
  their bets look considered.
- **A bot sees only what a player at that seat could see**: its own cards, the board,
  the betting so far, the stacks. Never another seat's cards and never the deck.
  Structural, not a promise -- a bot cannot cheat with what it was never handed.
- **A bot must never call `IBank`.** Their chips are notional. Worth an explicit
  test.
- **Their RNG must be injectable**, exactly as `Deck`'s is, or their behaviour cannot
  be pinned in a test.
- **Every decision is logged** with its reason, through `IGameLog`. A table of seats
  that silently do things is untestable and unwatchable; the console tool has to be
  able to print why seat 3 shoved.
- **Hole cards are absent from the view until showdown**, and mucked hands stay
  absent. Anything sent to the client is knowable by the client.

### How they actually decide

`BotAgent` runs one procedure for every character, weighted by five dials in
`PokerPersonality`. One procedure and eight sets of dials, never eight procedures:
a seat that decides by its own logic cannot be debugged, and when two of them
disagree about a hand there is no way to say which is wrong.

What goes into a decision, which is what a person weighs too:

- **How often the hand wins**, from `HandEquity` -- a Monte Carlo rollout over the
  unseen cards. It handles any street and any number of opponents in the same code,
  and it already accounts for the crowd: aces against one player and aces against
  four are different hands, which is the thing no chart can tell a bot.
- **The price**, as pot odds. Equity above the price is a call that makes money and
  below it is one that does not; everything else is an adjustment to one side.
- **Position**, weighted by the `Positional` dial. Weak players ignore it, which is
  the most reliable way to spot one.
- **What is already in**, because chips in the pot change what folding costs.
- **Stack depth** -- under about ten big blinds a seat starts shoving, and the
  `Risk` dial decides how early.
- **How many opponents are still live**, which throttles bluffing hard.

The dials are `Tightness`, `Aggression`, `Bluff`, `Risk` and `Positional`, each 0 to
1. Measured over sixty hands apiece, facing a bet:

| | folds | calls | raises |
| --- | --- | --- | --- |
| Rock | 78% | 18% | 4% |
| Owl | 73% | 20% | 7% |
| Grinder | 67% | 20% | 13% |
| Shark | 55% | 26% | 19% |
| Tourist | 47% | 45% | 8% |
| Station | 42% | 57% | 2% |
| Gambler | 35% | 36% | 29% |
| Maniac | 27% | 33% | 40% |

**The spans on the dials matter more than their midpoints**, and both had to be
widened after measurement. At the first attempt a rock folded 15% and a calling
station 11%, and a merely ordinary player raised as rarely as a station -- that is
not eight characters, it is one character with eight names. If a dial is retuned,
re-measure this table rather than trusting that it still separates.

### Three things about testing bots that cost time here

- **Measure decisions where money was actually asked for.** Most of what a seat does
  is check into pots nobody bet, and averaging over all of it drowns every
  difference. "How often it folds when asked" is both the number that separates the
  characters and the one a person at the table would notice.
- **Top the stacks up between hands when measuring style.** The maniac busted a
  third of the way through its sample and took the sample with it. True to life,
  useless as a measurement.
- **A five-seat table is not a five-handed pot.** Bluffing frequency is conditioned
  on *live* opponents, and by the time anyone checks after the flop most of the table
  has folded -- so a test that varies the seat count measures almost nothing (48%
  against 46%). Condition on what the rule actually reads.

### They have to feel like real people. This is a stated requirement, not polish.

The table is meant to feel alive and the seats are meant to read as players rather
than as a lookup table with names on it.

**The honest constraint first**, because it shapes every answer below: in UTH the
seat-mates cannot take the player's money. There is no pot -- every seat plays its
own hand against the dealer. So they can never be made to feel real by being
*dangerous*. They are made to feel real by having a life of their own that the
player watches happen: their own money, their own runs of luck, their own mistakes,
and their own reactions to all three.

What that needs, roughly in order of how much it buys:

1. **Persistence between hands.** The same named characters, still there next hand,
   with a history the player can notice. A cast that is re-rolled every deal is
   scenery.
2. **A bankroll with consequences.** Each seat has notional chips that rise and
   fall, and a seat that busts **leaves and is replaced**. A player who can go broke
   is the single strongest signal that a seat is a person, and it costs almost
   nothing -- `Seat.Net` already produces the number.
3. **Mood that moves.** Dials that drift during a session rather than sitting where
   they were set: a seat that has lost four in a row chases, a seat that just won
   big gets careless. A fixed personality is still a lookup table, just a biased
   one. This is the thing that most makes a bot stop feeling mechanical.
4. **Reactions tied to real events**, emitted by the engine rather than invented by
   the client -- hitting the Blind paytable, a bad beat, a third fold running, a
   royal. The client must never make up a fact about a hand; it renders what the
   engine says happened.
5. **Timing.** Real players do not act instantly or uniformly. The engine should hand
   the client a thinking time per decision, and it already knows the right one: the
   river calculation produces the exact value of betting against folding, so **a seat
   can take longer precisely when the decision is genuinely close.** That single
   detail does more than any amount of random delay.
6. **Visible mistakes.** The dials already produce them and the log already records
   them; surfacing one as a tell is nearly free.

**Known flaw blocking this: the table takes one `ISeatAgent` for every seat**, so
today all four seat-mates are the same person with different labels. Distinct
characters need an agent per seat -- a list, or a factory keyed on the seat. Fix
that before building anything above it.

None of this belongs in the client. The engine owns behaviour and emits events; the
client owns rendering and animation. A bot whose personality lives in the UI cannot
be tested and will not survive the first refactor.

## The payout scale, which hold'em mostly solves

This was the mod's hardest open problem under UTH, where the Blind paid **500:1** on
a royal and the worst case reached 511 antes -- a payout that has to arrive as items,
in a stash, at the wallet's `StackMaxSize`.

**A pot cannot pay more than the chips in it.** The most the player can win in a hand
is the sum of what everyone else put in, which is bounded by the stacks at the table
and therefore by the buy-in. No paytable, no multiplier, no tail. That removes the
entire class of problem, and with it the capped valuables paytable that existed to
work around it.

What is left is smaller and still real:

- **The buy-in is now the number that sets the ceiling**, not a bet limit. A player
  who buys in for X can at most cash out X times the number of seats, so the maximum
  a session can hand back is roughly `buy-in x seats`. That is the figure to size
  wallet limits against.
- **Bitcoin and Lega medals still have a `StackMaxSize` of 1** -- one item per unit,
  one grid cell each. A five-handed table with a 10-bitcoin buy-in can hand back 50
  coins, which is 50 free grid cells. Tighter than roubles by a long way, and the
  reason valuables want their own buy-in ceiling.
- **Chips need a denomination.** Blackjack never had one because it never had chips.
  A stack of a million roubles cannot be one chip per rouble, so the table needs a
  chip size -- a big blind, effectively -- and the buy-in has to be a whole number of
  them. Rounding here is where money goes missing.
- `Bank.Credit`'s shortfall-to-mail path is still the backstop, not the plan. Mail
  has attachment limits too, and "you won, here are 40 letters" is not an outcome.

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
dotnet test    # 176 tests, no SPT needed. About 8s.
```

**Distrust a suite that passes first time.** Mutation-check anything that ranks a
hand or moves money -- see the evaluator and pot builder notes above for the
pattern, and port Blackjack's `MoneyInvariantTests` before writing settlement
rather than after.

**Chips are conserved. That is the invariant hold'em has and UTH did not.** Every
hand starts with a known number of chips at the table and must end with the same
number: what leaves the stacks equals what the pots pay out plus what is refunded.
`PotBuilder` already carries the pot half of that -- and its own caveat applies here
too, that conservation is necessary and nowhere near sufficient, because chips can
balance and still reach the wrong seat.

Fuzz the betting round rather than the payouts. The bug-dense part of hold'em is not
settlement, it is **who acts next and when a round closes**: min-raises, an all-in
that is too small to reopen the action, a blind that is already all-in, everyone
folding to the big blind. Those are cheap to generate randomly and expensive to
enumerate by hand.

**Do not try to confirm a house edge by simulation**, if a session ever reaches for
it -- see the note under "Parked" for why. Measure decision frequencies instead;
they are proportions and converge in thousands rather than millions.

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

- Working branch **`uth`** (named before the variant changed), off `main` at
  `9c4b9e9`. Pushed to `origin/uth`.
- `Poker.Game` green at **176 tests** in about 8 seconds, mutation-checked
  throughout.
- **The variant is no-limit Texas Hold'em against bots**, decided after two
  reversals. See the top of this file, and read it before reopening the question.
- **The game is playable end to end in the engine.** The table deals, bets and
  settles; eight distinct characters fill the other seats and a five-handed table
  runs itself. What is missing is everything outside `Poker.Game`: no server, no
  client, no console.
- A complete UTH game is in the tree and **parked**. It is green and does no harm;
  nothing new should call into it.
- Nothing SPT-facing exists yet -- no server project, no client plugin, no routes.

### Open items

**The game**

- **Give the bots a life between hands** -- the dynamism requirement below. They
  play well enough now; what they do not have is memory, a bankroll that can bust,
  or a mood that moves.
- **A chip denomination**, so a buy-in in roubles becomes a whole number of chips.
  Rounding at that boundary is where money goes missing.
- **Busting and re-seating.** `StartHand` refuses to deal to a seat with no chips,
  deliberately -- who leaves and who sits down is table management and does not
  belong in the middle of a deal.
- **Then the dynamism** under "They have to feel like real people": persistence
  between hands, notional stacks that can bust and be replaced, mood that drifts,
  engine-emitted reactions, and thinking time drawn from how close the decision was.
- Decide UTH's fate -- delete, ship as a second table, or leave parked. Undecided on
  purpose; it costs nothing where it is.

**The money**

- **Buy-in and cash-out replace per-hand settlement**, which Blackjack did not have
  to do. `EscrowStore` must hold the player's **current stack**, updated as it
  changes, not the amount they sat down with -- a crash mid-session has to return
  what they actually have.
- **Set the wallet ceilings from the buy-in**, not from a paytable. The most a
  session can return is roughly `buy-in x seats`; bitcoin and Lega medals, which do
  not stack, are the binding constraint.
- Port `Bank`, `ProfileGateway`, `EscrowStore`, `StatsStore`, `BlackjackLog` and
  `Fakes` from Blackjack largely as-is; they are currency plumbing and carry no
  blackjack rules.

**The client**

- Port `CardView` / `Textures` / `MenuButtonPatch` from `Blackjack.Client`.
- The wire enums are strings already, with property-level attributes. Keep it that
  way when the server's own contracts are written.
- `tools/Poker.Console` is worth building early: it makes the game playable with no
  SPT install and is the only practical way to watch bots play thousands of hands.
