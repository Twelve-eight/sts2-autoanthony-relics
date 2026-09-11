using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace AutoAnthonyRelics.Data;

/// <summary>
/// Loads and validates the generation pool.
///
/// DATA PROVENANCE (do not remove this notice):
///   catalog_recipes.json and catalog_runtime_specs.json are the offline output
///   of Alriph's Auto-Anthonyology (mod id "AutoAnthony", v0.3.81). This mod
///   reuses that data under an efficiency-first decision and reimplements the
///   generator, interpreter and rendering independently. Attribution is required
///   in the manifest description, the workshop description and DEVELOP.md.
///   The two native_reference_*.json files are dumps of the ENGINE's own cards
///   and are kept in research/ for our own cross-checking; they are not needed
///   at runtime.
///
/// NO GODOT API IS TOUCHED HERE. The pool is embedded in the dll rather than
/// shipped as a res:// resource precisely so that this layer can be exercised by
/// the isolated probe: touching Godot statics outside the engine is a native
/// access violation that try/catch cannot contain (see the QuriousCraftingRelics
/// config-migration incident in DEVLOG Session 45).
/// </summary>
public sealed class AnthonyCatalog
{
    private const string RecipeResourceSuffix = "catalog_recipes.json";
    private const string SpecResourceSuffix = "catalog_runtime_specs.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static AnthonyCatalog? _instance;
    private static readonly object Gate = new();

    private readonly List<AtomRecipe> _recipes;
    private readonly Dictionary<string, RuntimeSpec> _specs;
    private readonly List<JoinedFragment> _fragments;
    private readonly Dictionary<string, List<JoinedFragment>> _poolByCharacter;

    /// <summary>Loads once and caches. Throws only on a genuinely unreadable resource.</summary>
    public static AnthonyCatalog Instance
    {
        get
        {
            if (_instance != null)
            {
                return _instance;
            }
            lock (Gate)
            {
                return _instance ??= new AnthonyCatalog();
            }
        }
    }

    private AnthonyCatalog()
    {
        Assembly assembly = typeof(AnthonyCatalog).Assembly;
        _recipes = ReadJson<List<AtomRecipe>>(assembly, RecipeResourceSuffix);
        List<RuntimeSpecEntry> entries = ReadJson<List<RuntimeSpecEntry>>(assembly, SpecResourceSuffix);

        _specs = new Dictionary<string, RuntimeSpec>(entries.Count, StringComparer.Ordinal);
        foreach (RuntimeSpecEntry entry in entries)
        {
            _specs[entry.Id] = entry.Spec;
        }

        _fragments = new List<JoinedFragment>();
        _poolByCharacter = new Dictionary<string, List<JoinedFragment>>(StringComparer.Ordinal);
        foreach (AtomRecipe recipe in _recipes)
        {
            if (!_poolByCharacter.TryGetValue(recipe.Character, out List<JoinedFragment>? pool))
            {
                pool = new List<JoinedFragment>();
                _poolByCharacter[recipe.Character] = pool;
            }
            foreach (AtomFragment atom in recipe.Atoms)
            {
                _specs.TryGetValue(atom.SemanticId, out RuntimeSpec? spec);
                var joined = new JoinedFragment(recipe, atom, spec);
                _fragments.Add(joined);
                pool.Add(joined);
            }
        }
    }

    public IReadOnlyList<AtomRecipe> Recipes => _recipes;

    public IReadOnlyDictionary<string, RuntimeSpec> Specs => _specs;

    /// <summary>Every atom instance in the pool, with its runtime spec resolved (null if orphaned).</summary>
    public IReadOnlyList<JoinedFragment> Fragments => _fragments;

    /// <summary>
    /// The whole atomic pool of one character - the exact set the generator
    /// iterates per slot. "Colorless" is a real entry, not a fallback.
    /// </summary>
    public IReadOnlyList<JoinedFragment> PoolFor(string character) =>
        _poolByCharacter.TryGetValue(character, out List<JoinedFragment>? pool)
            ? pool
            : Array.Empty<JoinedFragment>();

    public IReadOnlyList<string> Characters =>
        _poolByCharacter.Keys.OrderBy(c => c, StringComparer.Ordinal).ToArray();

    /// <summary>Cheap structural counts. The probe asserts every one of these.</summary>
    public CatalogStats Stats()
    {
        var byScope = new Dictionary<FragmentScope, int>();
        var byOpcode = new Dictionary<SpecOpcode, int>();
        var byCharacter = new Dictionary<string, int>(StringComparer.Ordinal);
        var variantPairs = new HashSet<string>(StringComparer.Ordinal);
        var variants = new HashSet<string>(StringComparer.Ordinal);
        var flags = new HashSet<string>(StringComparer.Ordinal);
        var conditions = new HashSet<string>(StringComparer.Ordinal);
        var conditionSpecs = new HashSet<string>(StringComparer.Ordinal);
        var triggers = new HashSet<string>(StringComparer.Ordinal);
        var triggerSpecs = new HashSet<string>(StringComparer.Ordinal);
        var triggerLifetimes = new HashSet<string>(StringComparer.Ordinal);
        var targets = new HashSet<string>(StringComparer.Ordinal);
        var sourceZones = new HashSet<string>(StringComparer.Ordinal);
        var destinationZones = new HashSet<string>(StringComparer.Ordinal);
        var cardFilters = new HashSet<string>(StringComparer.Ordinal);
        var valueCounts = new Dictionary<int, int>();

        foreach (AtomRecipe recipe in _recipes)
        {
            byCharacter.TryGetValue(recipe.Character, out int n);
            byCharacter[recipe.Character] = n + 1;
        }

        foreach (JoinedFragment fragment in _fragments)
        {
            Increment(byScope, fragment.ParsedScope);

            if (fragment.Spec is not { } spec)
            {
                continue;
            }

            Increment(byOpcode, spec.ParsedOpcode);
            variants.Add(spec.Variant);
            variantPairs.Add(spec.Opcode + "|" + spec.Variant);
            foreach (string flag in spec.Flags)
            {
                flags.Add(flag);
            }
            if (spec.Condition is { } condition)
            {
                conditions.Add(condition.Kind);
                // Kind alone is NOT the right identity for a condition: the
                // value slot matters too. Both measures are reported because the
                // pool's 23 condition instances collapse to 18 kinds AND 18
                // full specs, so they happen to agree here - which is exactly
                // the kind of coincidence that should not be relied on silently.
                conditionSpecs.Add($"{condition.Kind}|{condition.Subject}|{condition.ValueSlot}");
            }
            if (spec.Trigger is { } trigger)
            {
                triggers.Add(trigger.Kind);
                triggerSpecs.Add($"{trigger.Kind}|{trigger.Lifetime}|{trigger.DurationSlot}|{trigger.ThresholdSlot}");
                triggerLifetimes.Add(trigger.Lifetime);
            }
            targets.Add(spec.Target);
            sourceZones.Add(spec.SourceZone);
            destinationZones.Add(spec.DestinationZone);
            cardFilters.Add(spec.CardFilter);
            valueCounts.TryGetValue(spec.Values.Count, out int vc);
            valueCounts[spec.Values.Count] = vc + 1;
        }

        return new CatalogStats(
            RecipeCount: _recipes.Count,
            FragmentCount: _fragments.Count,
            SpecCount: _specs.Count,
            RecipesByCharacter: byCharacter,
            FragmentsByScope: byScope,
            FragmentsByOpcode: byOpcode,
            DistinctVariants: variants.Count,
            DistinctOpcodeVariantPairs: variantPairs.Count,
            DistinctFlags: flags.Count,
            DistinctConditionKinds: conditions.Count,
            DistinctConditionSpecs: conditionSpecs.Count,
            DistinctTriggerKinds: triggers.Count,
            DistinctTriggerSpecs: triggerSpecs.Count,
            DistinctTriggerLifetimes: triggerLifetimes.Count,
            DistinctTargets: targets.Count,
            DistinctSourceZones: sourceZones.Count,
            DistinctDestinationZones: destinationZones.Count,
            DistinctCardFilters: cardFilters.Count,
            SpecsByValueSlotCount: valueCounts);
    }

    /// <summary>
    /// Structural integrity report. Any non-empty problem list means the pool
    /// cannot be sampled faithfully, so the caller should refuse to generate
    /// rather than produce half-broken cards.
    /// </summary>
    public CatalogValidation Validate()
    {
        var problems = new List<string>();

        var orphanFragments = _fragments.Where(f => f.Spec is null).Select(f => f.Atom.SemanticId).ToList();
        if (orphanFragments.Count > 0)
        {
            problems.Add($"{orphanFragments.Count} atom(s) have no runtime spec, e.g. {string.Join(", ", orphanFragments.Take(5))}");
        }

        var referenced = new HashSet<string>(_fragments.Select(f => f.Atom.SemanticId), StringComparer.Ordinal);
        var unreferenced = _specs.Keys.Where(id => !referenced.Contains(id)).ToList();
        if (unreferenced.Count > 0)
        {
            problems.Add($"{unreferenced.Count} runtime spec(s) are referenced by no atom, e.g. {string.Join(", ", unreferenced.Take(5))}");
        }

        var unknownScopes = _fragments.Where(f => f.ParsedScope == FragmentScope.Unknown)
            .Select(f => f.Atom.Scope).Distinct(StringComparer.Ordinal).ToList();
        if (unknownScopes.Count > 0)
        {
            problems.Add($"unrecognised scope value(s): {string.Join(", ", unknownScopes)}");
        }

        var unknownOpcodes = _fragments.Where(f => f.Spec is { ParsedOpcode: SpecOpcode.Unknown })
            .Select(f => f.Spec!.Opcode).Distinct(StringComparer.Ordinal).ToList();
        if (unknownOpcodes.Count > 0)
        {
            problems.Add($"unrecognised opcode value(s): {string.Join(", ", unknownOpcodes)}");
        }

        var emptyPools = _poolByCharacter.Where(kv => kv.Value.Count == 0).Select(kv => kv.Key).ToList();
        if (emptyPools.Count > 0)
        {
            problems.Add($"character(s) with an empty pool: {string.Join(", ", emptyPools)}");
        }

        var badRecipes = _recipes.Where(r => r.Atoms.Count == 0).Select(r => $"{r.Character}/{r.Id}").ToList();
        if (badRecipes.Count > 0)
        {
            problems.Add($"{badRecipes.Count} recipe(s) have no atoms, e.g. {string.Join(", ", badRecipes.Take(5))}");
        }

        return new CatalogValidation(problems);
    }

    private static void Increment<TKey>(Dictionary<TKey, int> counts, TKey key) where TKey : notnull
    {
        counts.TryGetValue(key, out int n);
        counts[key] = n + 1;
    }

    private static T ReadJson<T>(Assembly assembly, string resourceSuffix) where T : new()
    {
        string? name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(resourceSuffix, StringComparison.Ordinal));
        if (name == null)
        {
            throw new InvalidOperationException(
                $"Embedded resource ending in '{resourceSuffix}' not found. " +
                $"Available: {string.Join(", ", assembly.GetManifestResourceNames())}");
        }

        using Stream stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded resource '{name}' could not be opened.");

        // detectEncodingFromByteOrderMarks strips the UTF-8 BOM the data files
        // carry; JsonSerializer would otherwise choke on the leading U+FEFF.
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        string json = reader.ReadToEnd();

        return JsonSerializer.Deserialize<T>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Embedded resource '{name}' deserialized to null.");
    }
}

/// <summary>An atom instance plus its recipe context and resolved runtime spec.</summary>
public sealed record JoinedFragment(AtomRecipe Recipe, AtomFragment Atom, RuntimeSpec? Spec)
{
    public string SemanticId => Atom.SemanticId;
    public string Template => Atom.Template;
    public FragmentScope ParsedScope => Atom.ParsedScope;
}

public sealed record CatalogStats(
    int RecipeCount,
    int FragmentCount,
    int SpecCount,
    IReadOnlyDictionary<string, int> RecipesByCharacter,
    IReadOnlyDictionary<FragmentScope, int> FragmentsByScope,
    IReadOnlyDictionary<SpecOpcode, int> FragmentsByOpcode,
    int DistinctVariants,
    int DistinctOpcodeVariantPairs,
    int DistinctFlags,
    int DistinctConditionKinds,
    int DistinctConditionSpecs,
    int DistinctTriggerKinds,
    int DistinctTriggerSpecs,
    int DistinctTriggerLifetimes,
    int DistinctTargets,
    int DistinctSourceZones,
    int DistinctDestinationZones,
    int DistinctCardFilters,
    IReadOnlyDictionary<int, int> SpecsByValueSlotCount);

public sealed record CatalogValidation(IReadOnlyList<string> Problems)
{
    public bool Ok => Problems.Count == 0;
}
