using AutoAnthonyRelics.Data;

namespace AutoAnthonyRelics.Generation;

/// <summary>
/// The per-character derived view of the pool that the generator samples from.
///
/// Everything here is DERIVED from catalog_recipes.json / catalog_runtime_specs.json
/// - there is no second data file and no hand-maintained table. The original
/// builds the same three quantities in ImmutableComponentCatalog's constructor:
///
///   ComponentCounts            distinct recipe atom counts, ordered
///   ComponentCountCounts       recipes grouped by atom count
///   MinimumEffectCountsByRarity recipes of a rarity -> min atom count
///
/// Measured from the pool (see research/step4-implementation-plan.md §1):
///   Ironclad / Defect / Necrobinder / Regent  ComponentCounts = [1,2,3,4]
///   Silent / Colorless                        ComponentCounts = [1,2,3]
///   RarityEffectCountMinimum is 1 everywhere except Ironclad/Regent/Silent
///   Ancient, where it is 2.
/// </summary>
public sealed class GenerationPool
{
    private static readonly Dictionary<string, GenerationPool> Cache = new(StringComparer.Ordinal);
    private static readonly object Gate = new();

    private readonly List<JoinedFragment> _fragments;
    private readonly List<int> _componentCounts;
    private readonly Dictionary<int, int> _componentCountCounts;
    private readonly Dictionary<GeneratedRarity, int> _minimumEffectCountsByRarity;
    private readonly Dictionary<GeneratedRarity, List<AtomRecipe>> _recipesByRarity;

    public string Character { get; }

    /// <summary>This character's recipes, in pool order.</summary>
    public IReadOnlyList<AtomRecipe> Recipes { get; }

    /// <summary>
    /// This character's atoms WITH a runtime spec. Orphans are excluded rather
    /// than carried as nulls: AnthonyCatalog.Validate reports zero orphans today,
    /// and if that ever changes the generator must refuse to build cards out of
    /// fragments it cannot interpret.
    /// </summary>
    public IReadOnlyList<JoinedFragment> Fragments => _fragments;

    /// <summary>Distinct recipe atom counts, ascending. The candidate slot counts.</summary>
    public IReadOnlyList<int> ComponentCounts => _componentCounts;

    public IReadOnlyDictionary<int, int> ComponentCountCounts => _componentCountCounts;

    public IReadOnlyDictionary<GeneratedRarity, int> MinimumEffectCountsByRarity => _minimumEffectCountsByRarity;

    private GenerationPool(string character)
    {
        Character = character;
        AnthonyCatalog catalog = AnthonyCatalog.Instance;

        Recipes = catalog.Recipes.Where(r => r.Character == character).ToArray();
        if (Recipes.Count == 0)
        {
            throw new InvalidOperationException($"no recipes for character '{character}'");
        }

        _fragments = catalog.PoolFor(character).Where(f => f.Spec != null).ToList();

        _componentCounts = Recipes.Select(r => r.Atoms.Count).Distinct().OrderBy(c => c).ToList();
        _componentCountCounts = Recipes
            .GroupBy(r => r.Atoms.Count)
            .ToDictionary(g => g.Key, g => g.Count());

        _minimumEffectCountsByRarity = Recipes
            .GroupBy(r => GenerationEnumParsing.ParseRarity(r.OriginalRarity))
            .ToDictionary(g => g.Key, g => g.Min(r => r.Atoms.Count));

        _recipesByRarity = Recipes
            .GroupBy(r => GenerationEnumParsing.ParseRarity(r.OriginalRarity))
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    public static GenerationPool For(string character)
    {
        lock (Gate)
        {
            if (!Cache.TryGetValue(character, out GenerationPool? pool))
            {
                pool = new GenerationPool(character);
                Cache[character] = pool;
            }
            return pool;
        }
    }

    /// <summary>
    /// The original's AdaptiveEffectCountWindow. The window widens every three
    /// consecutive duplicate rejections so a stubborn shell eventually gets more
    /// room; it never exceeds 8 and never shrinks below the pool's own minimum
    /// atom count.
    ///
    /// NOTE: the caller's duplicate-rejection loop is not implemented yet
    /// (Phase B has no acceptance filter), so duplicateFailures is always 0 and
    /// the window is the pool's static [min, max(5, max)] clamped to 8.
    /// </summary>
    public (int Minimum, int Maximum) AdaptiveEffectCountWindow(int duplicateFailures)
    {
        int minimum = _componentCounts[0];
        int maximum = Math.Max(5, _componentCounts[^1]);
        int steps = Math.Max(0, duplicateFailures) / 3;
        for (int i = 0; i < steps; i++)
        {
            if (minimum >= 8)
            {
                break;
            }
            if (minimum < maximum)
            {
                minimum++;
                continue;
            }
            minimum++;
            maximum++;
        }
        return (minimum, Math.Max(minimum, Math.Min(maximum, 8)));
    }

    /// <summary>
    /// The original's RarityEffectCountMinimum: the smallest atom count among
    /// recipes of that rarity, falling back to the pool minimum when the
    /// character has no recipe of that rarity at all (Colorless has neither
    /// Basic nor Common nor Ancient recipes).
    /// </summary>
    public int RarityEffectCountMinimum(GeneratedRarity rarity) =>
        _minimumEffectCountsByRarity.TryGetValue(rarity, out int minimum) ? minimum : _componentCounts[0];

    /// <summary>Shell candidates of a rarity, with a documented fallback to the whole recipe set.</summary>
    public IReadOnlyList<AtomRecipe> ShellsOf(GeneratedRarity rarity) =>
        _recipesByRarity.TryGetValue(rarity, out List<AtomRecipe>? shells) && shells.Count > 0
            ? shells
            : Recipes;

    /// <summary>
    /// Slot count. The original weights this heavily by rarity, cost and type
    /// (PickComponentCount). Per the user's "pure uniform" decision we draw
    /// uniformly from the pool's own ComponentCounts; the adaptive window and the
    /// rarity minimum are still applied by the caller, so the mechanism is intact
    /// and only the distribution is flat. Restoring the weighted version is a
    /// single-function change - the recipe-derived weights it needs
    /// (Recipes.Count(rarity && count), ComponentCountCounts[count]) are already
    /// computed above.
    /// </summary>
    public int PickComponentCount(DeterministicRandom random) => random.PickUniform(_componentCounts);
}
