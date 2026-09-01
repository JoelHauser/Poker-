namespace Poker.Game;

/// <summary>
/// What kind of player a seat is.
///
/// Five dials, and between them they cover the range real players actually occupy.
/// The classic taxonomy is two of these crossed -- tight or loose, passive or
/// aggressive -- which gives the rock, the grinder, the calling station and the
/// maniac. The other three add the texture that makes two players of the same type
/// still feel different: whether they bluff, whether they will put a stack in, and
/// whether they notice where they are sitting.
///
/// Every dial runs 0 to 1 and every one of them is *applied to the same decision
/// procedure*. That is deliberate. A seat that decides by its own logic is a seat
/// nobody can debug, and when two of them disagree about a hand there is no way to
/// say which one is wrong.
/// </summary>
/// <param name="Name">Shown at the seat.</param>
/// <param name="Tightness">
/// How much better than the pot price a hand must be before this seat will put money
/// in. A rock wants a wide margin; a calling station will take almost any price.
/// </param>
/// <param name="Aggression">
/// Whether it raises or merely calls, and how large it bets when it does. This is the
/// dial that decides whether a hand is *played* or *paid off*.
/// </param>
/// <param name="Bluff">How often it bets a hand that cannot win if it is called.</param>
/// <param name="Risk">
/// Willingness to get a whole stack in. High risk shoves short stacks and jams draws;
/// low risk keeps pots small and folds rather than gamble.
/// </param>
/// <param name="Positional">
/// How much acting last is worth to it. Good players lean on position heavily; weak
/// ones play the same hand the same way wherever they are sitting, which is the most
/// reliable way to spot one.
/// </param>
public sealed record PokerPersonality(
    string Name,
    double Tightness,
    double Aggression,
    double Bluff,
    double Risk,
    double Positional)
{
    /// <summary>Nothing exaggerated. A reference point rather than a character.</summary>
    public static PokerPersonality Balanced { get; } = new("Balanced", 0.50, 0.50, 0.20, 0.50, 0.50);

    /// <summary>
    /// The table. Deliberately spread wide: the point is that the seats do not play
    /// alike, and a player should be able to tell them apart after a few orbits
    /// without ever being told which is which.
    /// </summary>
    public static IReadOnlyList<PokerPersonality> Cast { get; } =
    [
        // Waits all night for a hand and then bets it like an apology. Easy to play
        // against once you notice, which is the point of having one at the table.
        new("Rock", Tightness: 0.95, Aggression: 0.15, Bluff: 0.02, Risk: 0.15, Positional: 0.30),

        // The competent regular: few hands, played hard, and very aware of where it
        // is sitting.
        new("Grinder", Tightness: 0.75, Aggression: 0.70, Bluff: 0.25, Risk: 0.50, Positional: 0.85),

        // Calls everything, raises nothing, bluffs never. Impossible to bluff and
        // impossible to lose much to.
        new("Station", Tightness: 0.18, Aggression: 0.08, Bluff: 0.02, Risk: 0.35, Positional: 0.10),

        // Plays every hand and bets every street. Will hand over a stack and take one
        // back twenty minutes later.
        new("Maniac", Tightness: 0.10, Aggression: 0.95, Bluff: 0.62, Risk: 0.85, Positional: 0.40),

        // The best seat at the table: tight enough, aggressive, bluffs credibly, and
        // punishes position.
        new("Shark", Tightness: 0.68, Aggression: 0.82, Bluff: 0.42, Risk: 0.60, Positional: 0.95),

        // Here for the night out. Average everything and no idea where the button is.
        new("Tourist", Tightness: 0.42, Aggression: 0.48, Bluff: 0.15, Risk: 0.45, Positional: 0.12),

        // Would rather find out now. Loves a shove and does not much mind the price.
        new("Gambler", Tightness: 0.28, Aggression: 0.78, Bluff: 0.35, Risk: 0.98, Positional: 0.25),

        // Tight and passive until it has something, then suddenly enormous. The
        // opposite tell to the maniac.
        new("Owl", Tightness: 0.82, Aggression: 0.30, Bluff: 0.06, Risk: 0.70, Positional: 0.55),
    ];

    /// <summary>
    /// Picks distinct characters for a table, so no two seats are the same person.
    /// That was the parked UTH table's mistake and it is the one thing that most
    /// stops a table reading as a room full of players.
    /// </summary>
    public static IReadOnlyList<PokerPersonality> Deal(int count, Random rng)
    {
        if (count > Cast.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count), count, $"There are only {Cast.Count} characters to go round.");
        }

        return [.. Cast.OrderBy(_ => rng.Next()).Take(count)];
    }

    public override string ToString() => Name;
}
