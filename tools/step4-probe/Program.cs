using System.Reflection;
using System.Text.Json;
using AutoAnthonyRelics.Data;
using AutoAnthonyRelics.Generation;
using AutoAnthonyRelics.Interpretation;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.ValueProps;

// Stage A probe: the data layer must be provably identical to the pool the
// plan was sized against. Every number below was measured independently with
// Python over the raw JSON (see .tmp/step4/inventory*.py), so this is a
// cross-check of the C# loader against an independent implementation, not a
// self-confirming assertion.

int failures = 0;
void Check(string label, object? actual, object? expected)
{
    bool ok = Equals(actual?.ToString(), expected?.ToString());
    if (!ok) failures++;
    Console.WriteLine($"  {(ok ? "OK  " : "FAIL")} {label}: {actual}{(ok ? "" : $"  (expected {expected})")}");
}

Console.WriteLine("=== embedded resources ===");
Assembly asm = typeof(AnthonyCatalog).Assembly;
foreach (string name in asm.GetManifestResourceNames().OrderBy(n => n, StringComparer.Ordinal))
{
    long size = 0;
    using (Stream? s = asm.GetManifestResourceStream(name)) { size = s?.Length ?? 0; }
    Console.WriteLine($"  {name}  ({size} bytes)");
}

Console.WriteLine();
Console.WriteLine("=== load + validate ===");
AnthonyCatalog catalog = AnthonyCatalog.Instance;
CatalogValidation validation = catalog.Validate();
Console.WriteLine($"  validation.Ok = {validation.Ok}");
foreach (string problem in validation.Problems) Console.WriteLine($"    problem: {problem}");

CatalogStats stats = catalog.Stats();

Console.WriteLine();
Console.WriteLine("=== totals ===");
Check("recipes", stats.RecipeCount, 481);
Check("fragments", stats.FragmentCount, 931);
Check("specs", stats.SpecCount, 931);

Console.WriteLine();
Console.WriteLine("=== recipes by character ===");
foreach (var expected in new (string Character, int Count)[]
         {
             ("Silent", 86), ("Defect", 86), ("Necrobinder", 86),
             ("Regent", 86), ("Ironclad", 85), ("Colorless", 52),
         })
{
    stats.RecipesByCharacter.TryGetValue(expected.Character, out int actual);
    Check(expected.Character, actual, expected.Count);
}

Console.WriteLine();
Console.WriteLine("=== fragments by scope ===");
foreach (var expected in new (FragmentScope Scope, int Count)[]
         {
             (FragmentScope.NonTargeted, 426),
             (FragmentScope.SingleEnemyOnly, 205),
             (FragmentScope.Independent, 84),
             (FragmentScope.AbilityTrigger, 77),
             (FragmentScope.Modifier, 60),
             (FragmentScope.ConditionalTrigger, 58),
             (FragmentScope.AbilityRule, 21),
         })
{
    stats.FragmentsByScope.TryGetValue(expected.Scope, out int actual);
    Check(expected.Scope.ToString(), actual, expected.Count);
}
Check("no Unknown scope", stats.FragmentsByScope.GetValueOrDefault(FragmentScope.Unknown), 0);

Console.WriteLine();
Console.WriteLine("=== fragments by opcode ===");
foreach (var expected in new (SpecOpcode Opcode, int Count)[]
         {
             (SpecOpcode.DealDamage, 174),
             (SpecOpcode.TemplateSelfAction, 130),
             (SpecOpcode.Trigger, 116),
             (SpecOpcode.GainBlock, 78),
             (SpecOpcode.ApplyPower, 76),
             (SpecOpcode.TemplateIndependentAction, 72),
             (SpecOpcode.DrawCards, 50),
             (SpecOpcode.TemplateModifier, 46),
             (SpecOpcode.TemplateTargetAction, 33),
             (SpecOpcode.GainEnergy, 30),
             (SpecOpcode.Condition, 23),
             (SpecOpcode.CombatRule, 21),
             (SpecOpcode.GainStars, 13),
             (SpecOpcode.LoseHp, 11),
             (SpecOpcode.ExhaustCard, 11),
             (SpecOpcode.DiscardCard, 8),
             (SpecOpcode.CreateCard, 7),
             (SpecOpcode.MoveCard, 5),
             (SpecOpcode.ModifyDamage, 4),
             (SpecOpcode.ModifyCost, 4),
             (SpecOpcode.CreateCopy, 3),
             (SpecOpcode.ModifyHits, 3),
             (SpecOpcode.ChooseGeneratedCard, 3),
             (SpecOpcode.UpgradeCard, 1),
             (SpecOpcode.ModifyBlock, 1),
             (SpecOpcode.GainMaxHp, 1),
             (SpecOpcode.Heal, 1),
             (SpecOpcode.ModifyOrbSlots, 1),
             (SpecOpcode.DrawAndDiscard, 1),
             (SpecOpcode.ModifyPower, 1),
             (SpecOpcode.ModifyX, 1),
             (SpecOpcode.EndTurn, 1),
             (SpecOpcode.RestrictBlockFromCards, 1),
         })
{
    stats.FragmentsByOpcode.TryGetValue(expected.Opcode, out int actual);
    Check(expected.Opcode.ToString(), actual, expected.Count);
}
Check("distinct opcodes present", stats.FragmentsByOpcode.Count, 33);
Check("no Unknown opcode", stats.FragmentsByOpcode.GetValueOrDefault(SpecOpcode.Unknown), 0);

Console.WriteLine();
Console.WriteLine("=== dimension cardinalities ===");
Check("distinct variants", stats.DistinctVariants, 291);
Check("distinct opcode/variant pairs", stats.DistinctOpcodeVariantPairs, 306);
Check("distinct flags", stats.DistinctFlags, 86);
// Two different measures on purpose. The pool's 116 trigger instances collapse to
// 55 distinct Kinds but 57 distinct full specs (= 57 distinct (Kind, Lifetime)
// pairs), so a Kind-only count is not the binding surface the interpreter needs.
Check("distinct condition kinds", stats.DistinctConditionKinds, 18);
Check("distinct condition specs", stats.DistinctConditionSpecs, 18);
Check("distinct trigger kinds", stats.DistinctTriggerKinds, 55);
Check("distinct trigger specs", stats.DistinctTriggerSpecs, 57);
Check("distinct trigger lifetimes", stats.DistinctTriggerLifetimes, 6);
Check("distinct targets", stats.DistinctTargets, 15);
Check("distinct source zones", stats.DistinctSourceZones, 7);
Check("distinct destination zones", stats.DistinctDestinationZones, 4);
Check("distinct card filters", stats.DistinctCardFilters, 5);

Console.WriteLine();
Console.WriteLine("=== specs by numeric-slot count ===");
foreach (var expected in new (int Slots, int Count)[] { (0, 254), (1, 607), (2, 70) })
{
    stats.SpecsByValueSlotCount.TryGetValue(expected.Slots, out int actual);
    Check($"{expected.Slots} slot(s)", actual, expected.Count);
}

Console.WriteLine();
Console.WriteLine("=== per-character pool sizes ===");
foreach (string character in catalog.Characters)
{
    Console.WriteLine($"  {character,-14} pool={catalog.PoolFor(character).Count}");
}
int poolSum = catalog.Characters.Sum(c => catalog.PoolFor(c).Count);
Check("sum of character pools == fragment count", poolSum, stats.FragmentCount);

Console.WriteLine();
Console.WriteLine("=== the mechanism contract's premise: conditions and effects are separate fragments ===");
// Contract condition 1: the data layer carries two independent fragment classes.
int effectScopes = catalog.Fragments.Count(f =>
    f.ParsedScope is FragmentScope.SingleEnemyOnly or FragmentScope.NonTargeted
        or FragmentScope.Modifier or FragmentScope.Independent);
int triggerScopes = catalog.Fragments.Count(f =>
    f.ParsedScope is FragmentScope.AbilityTrigger or FragmentScope.ConditionalTrigger
        or FragmentScope.AbilityRule);
Console.WriteLine($"  effect-side fragments:  {effectScopes}");
Console.WriteLine($"  trigger-side fragments: {triggerScopes}");
Check("effect + trigger == total", effectScopes + triggerScopes, stats.FragmentCount);

// Contract condition 5's premise: a trigger fragment is identifiable purely from
// its own scope, so the generator can refuse to re-bind it.
var triggerTemplates = catalog.Fragments
    .Where(f => f.ParsedScope is FragmentScope.AbilityTrigger or FragmentScope.ConditionalTrigger or FragmentScope.AbilityRule)
    .Select(f => f.Template)
    .Distinct(StringComparer.Ordinal)
    .OrderBy(t => t, StringComparer.Ordinal)
    .ToArray();
Console.WriteLine($"  distinct trigger-side templates: {triggerTemplates.Length}");
Console.WriteLine($"  sample: {string.Join(", ", triggerTemplates.Take(12))}");

Console.WriteLine();
Console.WriteLine("=== determinism of the data layer itself ===");
// The loader must be a pure function of the embedded bytes: loading twice in the
// same process must yield identical digests.
string Digest()
{
    var payload = new System.Text.StringBuilder();
    foreach (JoinedFragment f in catalog.Fragments)
    {
        payload.Append(f.SemanticId).Append('|')
               .Append(f.Template).Append('|')
               .Append(f.Spec?.Opcode).Append('|')
               .Append(f.Spec?.Variant).Append('|')
               .Append(f.Spec?.Condition?.Kind).Append('|')
               .Append(f.Spec?.Trigger?.Kind).Append('\n');
    }
    byte[] hash = System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(payload.ToString()));
    return Convert.ToHexString(hash);
}
string first = Digest();
string second = Digest();
Check("digest stable within a process", first == second, true);
Console.WriteLine($"  pool digest: {first}");

Console.WriteLine();
Console.WriteLine("=== a sample recipe, to confirm the join is real ===");
AtomRecipe sample = catalog.Recipes.First(r => r.Character == "Ironclad" && r.Id == "Aggression");
Console.WriteLine($"  {sample.Character}/{sample.Id} cost={sample.Cost} type={sample.Type} rarity={sample.OriginalRarity}");
foreach (AtomFragment atom in sample.Atoms)
{
    RuntimeSpec? spec = catalog.Specs.GetValueOrDefault(atom.SemanticId);
    Console.WriteLine($"    {atom.Template,-16} scope={atom.ParsedScope,-18} opcode={spec?.Opcode,-30} variant={spec?.Variant}");
}

Console.WriteLine();
Console.WriteLine("=== STAGE B: generator core ===");
// Contract conditions 1-5 and 7. These are all decidable without an interpreter,
// which is what makes Stage B the earliest point where "conditions and effects
// really are recombined" can be PROVEN rather than asserted.

const string ModId = "AutoAnthonyRelics";
const int SeedsPerCharacter = 250;
GeneratedRarity[] AllRarities =
[
    GeneratedRarity.Basic, GeneratedRarity.Common, GeneratedRarity.Uncommon,
    GeneratedRarity.Rare, GeneratedRarity.Ancient,
];

Console.WriteLine();
Console.WriteLine("--- B.0 derived pool statistics (must match the measured pool) ---");
foreach ((string character, string counts, int rarityFloor) in new (string, string, int)[]
         {
             ("Ironclad", "1,2,3,4", 1),
             ("Silent", "1,2,3", 1),
             ("Defect", "1,2,3,4", 1),
             ("Necrobinder", "1,2,3,4", 1),
             ("Regent", "1,2,3,4", 1),
             ("Colorless", "1,2,3", 1),
         })
{
    GenerationPool pool = GenerationPool.For(character);
    Check($"{character} ComponentCounts", string.Join(",", pool.ComponentCounts), counts);
    Check($"{character} ComponentCountCounts sum == recipes", pool.ComponentCountCounts.Values.Sum(), pool.Recipes.Count);
    Check($"{character} RarityEffectCountMinimum(Common)", pool.RarityEffectCountMinimum(GeneratedRarity.Common), rarityFloor);
    // The window's real semantics (measured against the original's loop): the
    // minimum climbs toward the maximum first, and only once they meet do BOTH
    // climb - capped at 8. It is not "widen by one" on the first step.
    int floor = pool.ComponentCounts[0];
    int ceiling = Math.Min(8, Math.Max(5, pool.ComponentCounts[^1]));
    (int minimum, int maximum) = pool.AdaptiveEffectCountWindow(0);
    Check($"{character} adaptive window(0)", $"{minimum}..{maximum}", $"{floor}..{ceiling}");
    (int m1, int x1) = pool.AdaptiveEffectCountWindow(3);
    Check($"{character} adaptive window(3)", $"{m1}..{x1}", $"{floor + 1}..{ceiling}");
    (int m4, int x4) = pool.AdaptiveEffectCountWindow(12);
    Check($"{character} adaptive window(12) converges to the ceiling", $"{m4}..{x4}", $"{ceiling}..{ceiling}");
    (int m5, int x5) = pool.AdaptiveEffectCountWindow(15);
    Check($"{character} adaptive window(15) pushes both past it", $"{m5}..{x5}", $"{ceiling + 1}..{ceiling + 1}");
    (int m8, int x8) = pool.AdaptiveEffectCountWindow(60);
    Check($"{character} adaptive window is capped at 8", $"{m8}..{x8}", "8..8");
}
// The original computes RarityEffectCountMinimum but only CONSUMES it on the
// aggressive-mode path (raiseAggressiveEffectFloor). Recorded here so nobody
// later mistakes it for a floor the default path applies.
Console.WriteLine("  note: RarityEffectCountMinimum is computed but only consumed when");
Console.WriteLine("        raiseAggressiveEffectFloor is set; the default path never applies it.");

Console.WriteLine();
Console.WriteLine("--- B.1/B.2 generate, then measure ---");
var allCards = new List<GeneratedCard>();
var overall = new BindingStats();
var rejectReasons = new Dictionary<string, int>(StringComparer.Ordinal);
var slotCounts = new Dictionary<int, int>();
var generatedOpcodes = new Dictionary<string, int>(StringComparer.Ordinal);
int assembled = 0;
int failed = 0;

foreach (string character in catalog.Characters)
{
    GenerationPool pool = GenerationPool.For(character);
    var perCharacter = new BindingStats();
    int characterOk = 0;
    int characterFail = 0;

    for (int seed = 0; seed < SeedsPerCharacter; seed++)
    {
        var assembler = new CardAssembler(pool, GenerationSeed.For(ModId, character, seed));
        foreach (GeneratedRarity rarity in AllRarities)
        {
            GeneratedCard? card = assembler.Assemble(rarity, out AssemblyReport report);
            if (card == null)
            {
                characterFail++;
                string key = $"{character}: {report.LastRejectReason}";
                rejectReasons.TryGetValue(key, out int n);
                rejectReasons[key] = n + 1;
                continue;
            }
            characterOk++;
            allCards.Add(card);
            slotCounts.TryGetValue(card.Operations.Count, out int sc);
            slotCounts[card.Operations.Count] = sc + 1;
            foreach (GeneratedOperation operation in card.Operations)
            {
                string key = operation.Spec.Opcode;
                generatedOpcodes.TryGetValue(key, out int oc);
                generatedOpcodes[key] = oc + 1;
            }
        }
        perCharacter.Merge(assembler.BindingStats);
    }

    overall.Merge(perCharacter);
    assembled += characterOk;
    failed += characterFail;
    Console.WriteLine($"  {character,-12} ok={characterOk,4} fail={characterFail,4}  {perCharacter}");
}

Check("assemblies produced", assembled > 0, true);
double successRate = (double)assembled / (assembled + failed);
Check("assembly success rate > 0.5", successRate > 0.5, true);
Console.WriteLine($"  success rate: {assembled}/{assembled + failed} = {successRate:P1}");
Console.WriteLine($"  slot-count distribution: {string.Join(", ", slotCounts.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}->{kv.Value}"))}");
Console.WriteLine($"  distinct opcodes in output: {generatedOpcodes.Count}");
if (rejectReasons.Count > 0)
{
    Console.WriteLine("  rejection reasons:");
    foreach (var entry in rejectReasons.OrderByDescending(kv => kv.Value).Take(10))
    {
        Console.WriteLine($"    {entry.Value,5}  {entry.Key}");
    }
}

Console.WriteLine();
Console.WriteLine("--- B.3 contract condition 1: fragments carry the full structured payload ---");
int withTriggerSpec = allCards.SelectMany(c => c.Operations).Count(o => o.Spec.Trigger != null);
int withConditionSpec = allCards.SelectMany(c => c.Operations).Count(o => o.Spec.Condition != null);
int withValues = allCards.SelectMany(c => c.Operations).Count(o => o.Values.Count > 0);
int withTarget = allCards.SelectMany(c => c.Operations).Count(o => !string.IsNullOrEmpty(o.Spec.Target));
Console.WriteLine($"  operations total           : {allCards.Sum(c => c.Operations.Count)}");
Console.WriteLine($"  with a Trigger spec        : {withTriggerSpec}");
Console.WriteLine($"  with a Condition spec      : {withConditionSpec}");
Console.WriteLine($"  with numeric slots         : {withValues}");
Console.WriteLine($"  with an explicit target    : {withTarget}");
Check("every operation carries a target", withTarget, allCards.Sum(c => c.Operations.Count));
Check("some operations carry a trigger spec", withTriggerSpec > 0, true);

Console.WriteLine();
Console.WriteLine("--- B.4 contract condition 2: the same pool is sampled for both classes ---");
// The decisive test. If the generator were carrying "the original partner" along
// with each fragment, every generated (trigger -> payoff) link would already
// exist in some vanilla recipe. Measure how many do not.
var vanillaPairs = new HashSet<string>(StringComparer.Ordinal);
foreach (AtomRecipe recipe in catalog.Recipes)
{
    for (int i = 0; i < recipe.Atoms.Count; i++)
    {
        int owner = recipe.Atoms[i].TriggerOwner;
        if (owner >= 0 && owner < recipe.Atoms.Count && owner != i)
        {
            vanillaPairs.Add(recipe.Atoms[owner].SemanticId + "->" + recipe.Atoms[i].SemanticId);
        }
    }
}
var generatedPairs = new HashSet<string>(StringComparer.Ordinal);
foreach (GeneratedCard card in allCards)
{
    foreach (GeneratedOperation operation in card.Operations)
    {
        if (operation.TriggerIndex >= 0)
        {
            generatedPairs.Add(card.Operations[operation.TriggerIndex].SemanticId + "->" + operation.SemanticId);
        }
    }
}
int novelPairs = generatedPairs.Count(p => !vanillaPairs.Contains(p));
Console.WriteLine($"  vanilla (owner -> atom) pairs : {vanillaPairs.Count}");
Console.WriteLine($"  generated link pairs          : {generatedPairs.Count}");
Console.WriteLine($"  NOT present in any vanilla card: {novelPairs} ({100.0 * novelPairs / Math.Max(1, generatedPairs.Count):F1}%)");
Check("links were generated at all", generatedPairs.Count > 0, true);
Check("links exist that no vanilla card has", novelPairs > 0, true);

Console.WriteLine();
Console.WriteLine("--- B.5 contract condition 3: binding is a generation-time step ---");
bool firstIsStandalone = true;
bool linksPointBackwards = true;
bool linksPointAtTriggers = true;
foreach (GeneratedCard card in allCards)
{
    for (int i = 0; i < card.Operations.Count; i++)
    {
        GeneratedOperation operation = card.Operations[i];
        if (i == 0 && operation.TriggerIndex != -1)
        {
            firstIsStandalone = false;
        }
        if (operation.TriggerIndex < 0)
        {
            continue;
        }
        if (operation.TriggerIndex >= i)
        {
            linksPointBackwards = false;
            continue;
        }
        GeneratedOperation target = card.Operations[operation.TriggerIndex];
        if (!target.IsTriggerScope && !GenerationRules.IsDependencyPrefix(target))
        {
            linksPointAtTriggers = false;
        }
    }
}
Check("operation[0] is always standalone", firstIsStandalone, true);
Check("every triggerIndex points at an EARLIER operation", linksPointBackwards, true);
Check("every triggerIndex targets a trigger or dependency prefix", linksPointAtTriggers, true);

Console.WriteLine();
Console.WriteLine("--- B.6 contract condition 4: plain conditions bind ~50% of the time ---");
Console.WriteLine($"  {overall}");
Check("coin-flip trials are a meaningful sample", overall.CoinFlipTrials >= 200, true);
Check("coin-flip bind rate within 40%-60%", overall.CoinFlipBindRate is > 0.40 and < 0.60, true);
Console.WriteLine($"  by design: rule 5 (unfulfilled trigger) binds {overall.NeedsPayoffBinds} times and");
Console.WriteLine($"             rule 13 (difficult condition) binds {overall.DifficultConditionBinds} times;");
Console.WriteLine($"             only the remaining {overall.CoinFlipTrials} decisions are the coin flip.");

Console.WriteLine();
Console.WriteLine("--- B.7 contract condition 5: a trigger is never re-bound ---");
int reboundTriggers = allCards.SelectMany(c => c.Operations)
    .Count(o => o.IsTriggerScope && o.TriggerIndex >= 0);
Check("no trigger-scope operation carries a link", reboundTriggers, 0);
Check("the rule actually fired (not vacuous)", overall.TriggerSelfRejects > 0, true);
Console.WriteLine($"  rule 7 refused a link {overall.TriggerSelfRejects} times");

Console.WriteLine();
Console.WriteLine("--- B.8 contract condition 7: determinism ---");
GeneratedCard? CardFor(string material, string character, GeneratedRarity rarity) =>
    new CardAssembler(GenerationPool.For(character), new DeterministicRandom(material))
        .Assemble(rarity, out _);

string material = GenerationSeed.Material(ModId, "Ironclad", 7);
Check("same material, same fingerprint (fresh instance)",
    CardFor(material, "Ironclad", GeneratedRarity.Rare)?.Fingerprint,
    CardFor(material, "Ironclad", GeneratedRarity.Rare)?.Fingerprint);
Check("material contains the version segment", material.Contains($"/{GenerationSeed.Version}/", StringComparison.Ordinal), true);

string bumped = material.Replace($"/{GenerationSeed.Version}/", "/v999/", StringComparison.Ordinal);
Check("version bump changes the fingerprint",
    CardFor(bumped, "Ironclad", GeneratedRarity.Rare)?.Fingerprint == CardFor(material, "Ironclad", GeneratedRarity.Rare)?.Fingerprint,
    false);
Check("different seed changes the fingerprint",
    CardFor(GenerationSeed.Material(ModId, "Ironclad", 8), "Ironclad", GeneratedRarity.Rare)?.Fingerprint
        == CardFor(material, "Ironclad", GeneratedRarity.Rare)?.Fingerprint,
    false);
Check("different character changes the fingerprint",
    CardFor(GenerationSeed.Material(ModId, "Silent", 7), "Silent", GeneratedRarity.Rare)?.Fingerprint
        == CardFor(material, "Ironclad", GeneratedRarity.Rare)?.Fingerprint,
    false);

Console.WriteLine();
Console.WriteLine("--- B.9 whole-run generation digest (compare across processes) ---");
var digestPayload = new System.Text.StringBuilder();
foreach (string character in catalog.Characters)
{
    GenerationPool pool = GenerationPool.For(character);
    for (int seed = 0; seed < SeedsPerCharacter; seed++)
    {
        var assembler = new CardAssembler(pool, GenerationSeed.For(ModId, character, seed));
        foreach (GeneratedRarity rarity in AllRarities)
        {
            GeneratedCard? card = assembler.Assemble(rarity, out _);
            digestPayload.Append(character).Append('/').Append(seed).Append('/').Append(rarity)
                         .Append('=').Append(card?.Fingerprint ?? "NULL").Append('\n');
        }
    }
}
string generationDigest = Convert.ToHexString(
    System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(digestPayload.ToString())));
Console.WriteLine($"  generation digest: {generationDigest}");
Check("generation digest is stable within the process",
    Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(digestPayload.ToString()))),
    generationDigest);

Console.WriteLine();
Console.WriteLine("--- B.10 contract condition 6 preview: Rupture x Inferno, targeted ---");
// Stage D owns the acceptance gate, but reachability is cheap to prove here.
// The pool constrains this hard, which is why a generic sweep finds nothing:
//   * Trigger.Kind == "owner_hp_lost_during_turn" exists on Ironclad ONLY
//     (ironclad/inferno/2 and ironclad/rupture/0), and it is scope AbilityTrigger;
//   * an AbilityTrigger atom is rejected outright on a non-Power shell
//     (CompatibilityFilter "trigger-scope-on-non-power"), so only Ironclad
//     POWER shells can carry it - about a fifth of Ironclad shells;
//   * the payoff has to be one of Ironclad's 8 self-"N:HP-" atoms and has to be
//     drawn in the slot right after the trigger.
// So this is a targeted scan, and the number it reports is a probability
// estimate rather than a floor.
int ruptureInferno = 0;
int ironcladScanned = 0;
GeneratedCard? sampleCard = null;
GenerationPool ironcladPool = GenerationPool.For("Ironclad");
for (int seed = 0; seed < 2000; seed++)
{
    var scanner = new CardAssembler(ironcladPool, GenerationSeed.For(ModId, "Ironclad", seed));
    foreach (GeneratedRarity rarity in AllRarities)
    {
        GeneratedCard? card = scanner.Assemble(rarity, out _);
        if (card == null)
        {
            continue;
        }
        ironcladScanned++;
        for (int i = 0; i < card.Operations.Count; i++)
        {
            GeneratedOperation operation = card.Operations[i];
            if (operation.TriggerIndex < 0)
            {
                continue;
            }
            GeneratedOperation trigger = card.Operations[operation.TriggerIndex];
            if (trigger.Spec.Trigger?.Kind == "owner_hp_lost_during_turn"
                && operation.Spec.ParsedOpcode == SpecOpcode.LoseHp)
            {
                ruptureInferno++;
                sampleCard ??= card;
            }
        }
    }
}
Console.WriteLine($"  Ironclad cards scanned            : {ironcladScanned}");
Console.WriteLine($"  'owner_hp_lost_during_turn' -> 'lose_hp' links: {ruptureInferno}");
if (sampleCard != null)
{
    Console.WriteLine("  sample card:");
    Console.WriteLine("    " + sampleCard.Describe().Replace("\n", "\n    "));
}
Check("the contract's named combination is reachable", ruptureInferno > 0, true);

// ---------------------------------------------------------------------------
// STAGE C - the interpreter (slices 1+2).
//
// The interpreter is split into a pure planning layer (OperationPlanner,
// VariantTable) and an engine-facing executor (EffectExecutor). The probe only
// ever touches the pure half: the executor is never called here, because
// touching a Godot static outside the engine is a native access violation that
// try/catch cannot contain. See InterpreterTypes.cs.
// ---------------------------------------------------------------------------

Console.WriteLine();
Console.WriteLine("=== STAGE C: interpreter (slices 1+2) ===");

var fragmentBySemanticId = catalog.Fragments
    .GroupBy(f => f.SemanticId, StringComparer.Ordinal)
    .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

var catalogPairs = new HashSet<string>(StringComparer.Ordinal);
foreach (JoinedFragment fragment in catalog.Fragments)
{
    if (fragment.Spec is { } fragmentSpec)
    {
        catalogPairs.Add(VariantTable.Key(fragmentSpec.Opcode, fragmentSpec.Variant));
    }
}

JoinedFragment Frag(string template, string variant, string target) =>
    catalog.Fragments.First(f =>
        f.Template == template && f.Spec!.Variant == variant && f.Spec.Target == target);

GeneratedOperation Op(JoinedFragment fragment, int triggerIndex = -1) =>
    new(fragment, triggerIndex, fragment.Spec!.Values
        .Select(v => new ResolvedValue(v.Id, v.BaseValue + v.Offset, v.Upgradable))
        .ToArray());

GeneratedCard MakeCard(GeneratedCardType type, params GeneratedOperation[] operations) =>
    new("Ironclad", GeneratedRarity.Common, "ProbeShell", 1, -1, false, type,
        GeneratedTargetMode.Other, Array.Empty<string>(), operations);

Console.WriteLine();
Console.WriteLine("--- C.0 variant table: every pool pair is classified, none falls through ---");
VariantCounts variantCounts = VariantTable.Counts();
Console.WriteLine($"  {variantCounts}");
Check("catalog (opcode, variant) pairs", catalogPairs.Count, 306);
Check("frozen manifest size", VariantTable.PoolPairs.Count, 306);
Check("frozen manifest has no duplicate entries",
    VariantTable.PoolPairs.Distinct(StringComparer.Ordinal).Count(), VariantTable.PoolPairs.Count);
Check("frozen manifest == catalog pair set",
    VariantTable.PoolPairs.ToHashSet(StringComparer.Ordinal).SetEquals(catalogPairs), true);
Check("classified total == pool total", variantCounts.Total, 306);
Check("Implemented", variantCounts.Implemented, 42);
Check("DelegatedToNative", variantCounts.DelegatedToNative, 25);
Check("Pending", variantCounts.Pending, 239);

// Exhaustiveness: classify every catalog pair and count any that is unknown.
// A pair outside the pool must throw rather than being silently called Pending -
// that is the whole difference between "not written yet" and "not recognised".
int unclassified = 0;
foreach (string pairKey in catalogPairs)
{
    int separator = pairKey.IndexOf('|', StringComparison.Ordinal);
    try
    {
        VariantTable.Classify(pairKey[..separator], pairKey[(separator + 1)..]);
    }
    catch (UnclassifiedVariantException)
    {
        unclassified++;
    }
}
Check("pairs falling through to an unknown default", unclassified, 0);
bool threwForOutsider = false;
try
{
    VariantTable.Classify("no_such_opcode", "no_such_variant");
}
catch (UnclassifiedVariantException)
{
    threwForOutsider = true;
}
Check("a pair outside the pool throws instead of defaulting", threwForOutsider, true);

Console.WriteLine();
Console.WriteLine("--- C.1 ValueProp derivations vs the original's truth table ---");
// The expected values are written out independently here, straight from the
// original's control flow (ChaosOperationExecutor.cs:1463-1487), so this is a
// transcription check and not the implementation confirming itself.
CardType[] allCardTypes =
[
    CardType.None, CardType.Attack, CardType.Skill, CardType.Power,
    CardType.Status, CardType.Curse, CardType.Quest,
];
int propMismatches = 0;
int propCases = 0;
foreach (CardType type in allCardTypes)
{
    foreach (bool triggered in new[] { false, true })
    {
        foreach (bool powered in new[] { false, true })
        {
            int expectedDamage = type == CardType.Power ? 4 : (triggered && !powered ? 12 : 8);
            int actualDamage = (int)OperationPlanner.DamagePropsForCardEffect(type, triggered, powered);
            if (actualDamage != expectedDamage)
            {
                propMismatches++;
                Console.WriteLine($"    damage mismatch: type={type} triggered={triggered} powered={powered} got={actualDamage} want={expectedDamage}");
            }
            propCases++;

            int expectedBlock = type == CardType.Power || triggered ? 4 : 8;
            int actualBlock = (int)OperationPlanner.BlockPropsForCardEffect(type, triggered);
            if (actualBlock != expectedBlock)
            {
                propMismatches++;
                Console.WriteLine($"    block mismatch: type={type} triggered={triggered} got={actualBlock} want={expectedBlock}");
            }
            propCases++;
        }
    }
}
Check($"{propCases} damage/block prop cases match the original", propMismatches, 0);
Check("8 == Move (powered attack)", (int)ValueProp.Move, 8);
Check("12 == Move|Unpowered (unpowered attack)", (int)(ValueProp.Move | ValueProp.Unpowered), 12);
Check("4 == Unpowered (power / triggered block)", (int)ValueProp.Unpowered, 4);

int poweredAttackMismatches = 0;
foreach (CardType type in allCardTypes)
{
    foreach (bool inCombatPile in new[] { false, true })
    {
        bool expected = type != CardType.Power && inCombatPile;
        if (OperationPlanner.TriggeredDamageUsesPoweredAttack(inCombatPile, type) != expected)
        {
            poweredAttackMismatches++;
        }
    }
}
Check("TriggeredDamageUsesPoweredAttack matches the original", poweredAttackMismatches, 0);

Console.WriteLine();
Console.WriteLine("--- C.2 lose_hp: self vs non-self target and props ---");
JoinedFragment hpSelf = Frag("N:HP-", "immediate", "self");
JoinedFragment hpOther = Frag("T:LoseHp", "immediate", "selected_enemy");
int hpSelfExpected = hpSelf.Spec!.Values.First(v => v.Id == "hp_loss").BaseValue
    + hpSelf.Spec.Values.First(v => v.Id == "hp_loss").Offset;
int hpOtherExpected = hpOther.Spec!.Values.First(v => v.Id == "amount").BaseValue
    + hpOther.Spec.Values.First(v => v.Id == "amount").Offset;

GeneratedCard hpSelfCard = MakeCard(GeneratedCardType.Skill, Op(hpSelf));
GeneratedCard hpOtherCard = MakeCard(GeneratedCardType.Attack, Op(hpOther));

OperationPlan selfPlan = OperationPlanner.Plan(
    hpSelfCard, 0, InterpreterContext.OnPlay(CardType.Skill, TargetAvailability.SelectedEnemy));
Check("lose_hp self action", selfPlan.Action, PlannedActionKind.LoseHp);
Check("lose_hp self target", selfPlan.Target, PlannedTargetSelector.Self);
Check("lose_hp self props", (int)selfPlan.Props, 14);
Check("lose_hp self amount == spec value", selfPlan.Amount, hpSelfExpected);
Check("lose_hp self outcome", selfPlan.Outcome, PlanOutcome.Resolved);
Console.WriteLine($"    self: {hpSelf.Template} -> amount={selfPlan.Amount} props={(int)selfPlan.Props} target={selfPlan.Target}");

OperationPlan otherPlan = OperationPlanner.Plan(
    hpOtherCard, 0, InterpreterContext.OnPlay(CardType.Attack, TargetAvailability.SelectedEnemy));
Check("lose_hp non-self target", otherPlan.Target, PlannedTargetSelector.SelectedEnemy);
Check("lose_hp non-self props", (int)otherPlan.Props, 6);
Check("lose_hp non-self amount == spec value", otherPlan.Amount, hpOtherExpected);
Console.WriteLine($"    other: {hpOther.Template} -> amount={otherPlan.Amount} props={(int)otherPlan.Props} target={otherPlan.Target}");

// lose_hp ignores the source card's type: the original hardcodes 14 / 6.
OperationPlan selfAsPower = OperationPlanner.Plan(
    hpSelfCard, 0, InterpreterContext.OnPlay(CardType.Power, TargetAvailability.SelectedEnemy));
Check("lose_hp self props do not depend on card type", (int)selfAsPower.Props, 14);

// With no creature available the original returns early: `if (target == null) return true;`
OperationPlan otherNoTarget = OperationPlanner.Plan(hpOtherCard, 0, InterpreterContext.OnPlay(CardType.Attack));
Check("lose_hp non-self with no target is flagged unavailable", otherNoTarget.TargetUnavailable, true);
Check("lose_hp self is never flagged unavailable", selfPlan.TargetUnavailable, false);

Console.WriteLine();
Console.WriteLine("--- C.3 classification and plan outcome agree over the whole pool ---");
// For every one of the 306 pairs, take a representative pool fragment, plan it,
// and check the plan's outcome is exactly what the table promised. This is the
// "zero fallthrough" assertion in its strongest form: it is not enough that the
// table has an answer, the planner has to act on that answer.
int implementedResolved = 0;
int delegatedOk = 0;
int pendingOk = 0;
int outcomeDisagreements = 0;
var implementedSamples = new Dictionary<string, string>(StringComparer.Ordinal);
foreach (string pairKey in catalogPairs.OrderBy(k => k, StringComparer.Ordinal))
{
    int separator = pairKey.IndexOf('|', StringComparison.Ordinal);
    string opcode = pairKey[..separator];
    string variant = pairKey[(separator + 1)..];
    JoinedFragment representative = catalog.Fragments.First(f =>
        f.Spec is { } s && s.Opcode == opcode && s.Variant == variant);

    GeneratedCard probeCard = MakeCard(GeneratedCardType.Skill, Op(representative));
    OperationPlan probePlan = OperationPlanner.Plan(
        probeCard, 0, InterpreterContext.OnPlay(CardType.Skill, TargetAvailability.SelectedEnemy));

    switch (VariantTable.Classify(opcode, variant))
    {
        case VariantClassification.Implemented:
            if (probePlan.Outcome == PlanOutcome.Resolved)
            {
                implementedResolved++;
                implementedSamples[pairKey] = probePlan.Action.ToString();
            }
            else
            {
                outcomeDisagreements++;
                Console.WriteLine($"    Implemented but plan says {probePlan.Outcome}: {pairKey}");
            }
            break;
        case VariantClassification.DelegatedToNative:
            if (probePlan.Outcome == PlanOutcome.DelegatedToNative)
            {
                delegatedOk++;
            }
            else
            {
                outcomeDisagreements++;
                Console.WriteLine($"    DelegatedToNative but plan says {probePlan.Outcome}: {pairKey}");
            }
            break;
        default:
            if (probePlan.Outcome == PlanOutcome.Unsupported)
            {
                pendingOk++;
            }
            else
            {
                outcomeDisagreements++;
                Console.WriteLine($"    Pending but plan says {probePlan.Outcome}: {pairKey}");
            }
            break;
    }
}
Check("Implemented pairs that resolve", implementedResolved, 42);
Check("DelegatedToNative pairs that delegate", delegatedOk, 25);
Check("Pending pairs that report Unsupported", pendingOk, 239);
Check("classification and plan outcome never disagree", outcomeDisagreements, 0);
Console.WriteLine("  implemented pairs, by resolved action:");
foreach (var group in implementedSamples.GroupBy(kv => kv.Value).OrderBy(g => g.Key, StringComparer.Ordinal))
{
    Console.WriteLine($"    {group.Key,-16} {group.Count()}");
}

Console.WriteLine();
Console.WriteLine("--- C.4 native reference round trip (whole reference set) ---");
// research/native_reference_cards.json is the engine's own cards decomposed in
// the same opcode/variant language. It is ReferenceOnly / GenerationEligible:
// false, so it is not a generation source - it is the golden reference for
// "does our planner read a spec the same way the spec says it should be read".
string? referencePath = null;
for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
{
    string candidate = Path.Combine(dir.FullName, "research", "native_reference_cards.json");
    if (File.Exists(candidate))
    {
        referencePath = candidate;
        break;
    }
}
Check("reference file located", referencePath != null, true);
if (referencePath == null)
{
    Console.WriteLine("  (reference file missing; skipping C.4 - this is a probe setup failure, not a planner result)");
}
else
{
    ReferenceRoot reference = JsonSerializer.Deserialize<ReferenceRoot>(
        File.ReadAllText(referencePath),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    Console.WriteLine($"  {reference.Cards.Count} reference cards at {referencePath}");

    int referenceComponents = 0;
    int referenceWithSpec = 0;
    int missingPoolAtom = 0;
    int specFieldMismatches = 0;
    int roundTripMismatches = 0;
    int referenceResolved = 0;
    int referenceDelegated = 0;
    int referencePending = 0;
    var dispositionCounts = new Dictionary<PlanDisposition, int>();
    var referencePairs = new HashSet<string>(StringComparer.Ordinal);

    foreach (ReferenceCard referenceCard in reference.Cards)
    {
        var operations = new List<GeneratedOperation>();
        var operationComponentIndex = new List<int>();
        var componentToOperation = new int[referenceCard.Components.Count];
        for (int ci = 0; ci < referenceCard.Components.Count; ci++)
        {
            componentToOperation[ci] = -1;
        }

        for (int ci = 0; ci < referenceCard.Components.Count; ci++)
        {
            ReferenceComponent component = referenceCard.Components[ci];
            referenceComponents++;
            if (component.RuntimeSpec is not { } referenceSpec || component.SemanticId is null)
            {
                continue;
            }
            referenceWithSpec++;
            referencePairs.Add(VariantTable.Key(referenceSpec.Opcode, referenceSpec.Variant));

            if (!fragmentBySemanticId.TryGetValue(component.SemanticId, out JoinedFragment? source))
            {
                missingPoolAtom++;
                continue;
            }

            // The reference spec and the pool spec must be the same object
            // language; if they drift, everything downstream is meaningless.
            RuntimeSpec poolSpec = source.Spec!;
            if (poolSpec.Opcode != referenceSpec.Opcode
                || poolSpec.Variant != referenceSpec.Variant
                || poolSpec.Target != referenceSpec.Target
                || poolSpec.SourceZone != referenceSpec.SourceZone
                || poolSpec.DestinationZone != referenceSpec.DestinationZone
                || poolSpec.CardFilter != referenceSpec.CardFilter)
            {
                specFieldMismatches++;
            }

            componentToOperation[ci] = operations.Count;
            operationComponentIndex.Add(ci);
            operations.Add(new GeneratedOperation(
                source,
                -1,
                referenceSpec.Values
                    .Select(v => new ResolvedValue(v.Id, v.BaseValue + v.Offset, v.Upgradable))
                    .ToArray()));
        }

        // The reference's TriggerOwner indexes the FULL component list (including
        // the selector components that carry no RuntimeSpec), so it has to be
        // translated into operation indices rather than used directly.
        for (int i = 0; i < operations.Count; i++)
        {
            int owner = referenceCard.Components[operationComponentIndex[i]].TriggerOwner;
            int mapped = owner >= 0 && owner < componentToOperation.Length ? componentToOperation[owner] : -1;
            if (mapped >= i)
            {
                mapped = -1;
            }
            operations[i] = operations[i] with { TriggerIndex = mapped };
        }

        var planned = new GeneratedCard(
            "native", GeneratedRarity.Common, referenceCard.CatalogId, 0, -1, false,
            GeneratedCardType.Skill, GeneratedTargetMode.Other, Array.Empty<string>(), operations);
        InterpreterContext referenceContext =
            InterpreterContext.OnPlay(CardType.Skill, TargetAvailability.SelectedEnemy);

        for (int i = 0; i < operations.Count; i++)
        {
            OperationPlan plan = OperationPlanner.Plan(planned, i, referenceContext);
            ReferenceSpec expected = referenceCard.Components[operationComponentIndex[i]].RuntimeSpec!;
            if (plan.Opcode != expected.Opcode
                || plan.Variant != expected.Variant
                || plan.DeclaredTarget != expected.Target)
            {
                roundTripMismatches++;
                if (roundTripMismatches <= 5)
                {
                    Console.WriteLine(
                        $"    round-trip mismatch at {referenceCard.CatalogId}[{i}]: " +
                        $"plan={plan.Opcode}/{plan.Variant}/{plan.DeclaredTarget} " +
                        $"spec={expected.Opcode}/{expected.Variant}/{expected.Target}");
                }
            }

            switch (plan.Outcome)
            {
                case PlanOutcome.Resolved:
                    referenceResolved++;
                    break;
                case PlanOutcome.DelegatedToNative:
                    referenceDelegated++;
                    break;
                default:
                    referencePending++;
                    break;
            }
            dispositionCounts.TryGetValue(plan.Disposition, out int seen);
            dispositionCounts[plan.Disposition] = seen + 1;
        }
    }
    Console.WriteLine($"  components={referenceComponents} with RuntimeSpec={referenceWithSpec}");
    Console.WriteLine($"  distinct pairs in reference={referencePairs.Count}");
    Console.WriteLine($"  resolved={referenceResolved} delegated={referenceDelegated} pending={referencePending}");
    Console.WriteLine("  dispositions: " + string.Join(", ",
        dispositionCounts.OrderBy(kv => kv.Key.ToString(), StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}={kv.Value}")));
    Check("reference pairs are all in the pool", referencePairs.IsSubsetOf(catalogPairs), true);
    Check("reference spec ids all resolve to a pool atom", missingPoolAtom, 0);
    Check("reference spec fields match the pool spec", specFieldMismatches, 0);
    Check("planner echoes the declared opcode/variant/target for every reference operation", roundTripMismatches, 0);
    Check("reference resolution split adds up", referenceResolved + referenceDelegated + referencePending, referenceWithSpec);

    // Independent cross-check, not self-confirmation: these five numbers were
    // computed by a separate Python implementation over the same reference JSON
    // plus the same two embedded pool files (scope/template from the recipes,
    // classification from the runtime specs), using the original's own
    // RequiresCompositePower / IsLingeringTrigger / for-each sets. The first run
    // of this probe disagreed (LinkedEffect 122 vs 117, Modifier 55 vs 60) and
    // that is what caught a real ordering bug in OperationPlanner.ResolveDisposition:
    // scope Modifier must be skipped BEFORE the trigger link is consulted,
    // because the original never executes a Modifier (ChaosOperationExecutor.cs:118, :360).
    Check("disposition OnPlay", dispositionCounts.GetValueOrDefault(PlanDisposition.OnPlay), 610);
    Check("disposition CarriedHost", dispositionCounts.GetValueOrDefault(PlanDisposition.CarriedHost), 115);
    Check("disposition LinkedEffect", dispositionCounts.GetValueOrDefault(PlanDisposition.LinkedEffect), 117);
    Check("disposition InlineGate", dispositionCounts.GetValueOrDefault(PlanDisposition.InlineGate), 29);
    Check("disposition Modifier", dispositionCounts.GetValueOrDefault(PlanDisposition.Modifier), 60);
    Check("dispositions cover every reference operation", dispositionCounts.Values.Sum(), referenceWithSpec);
}

Console.WriteLine();
Console.WriteLine("--- C.5 contract condition 6: owner_hp_lost_during_turn -> lose_hp chain ---");
// Stage D owns the formal acceptance gate, but the interpreter half of it is
// decidable here: when the generator produces that chain, the host must be
// CARRIED and the linked effect must resolve to a self-targeted lose_hp with
// props 14. The scan is targeted for the reason documented in B.10.
int chainHits = 0;
GeneratedCard? chainCard = null;
int chainHostIndex = -1;
int chainEffectIndex = -1;
for (int seed = 0; seed < 4000 && chainCard == null; seed++)
{
    var scanner = new CardAssembler(ironcladPool, GenerationSeed.For(ModId, "Ironclad", seed));
    foreach (GeneratedRarity rarity in AllRarities)
    {
        GeneratedCard? candidate = scanner.Assemble(rarity, out _);
        if (candidate == null)
        {
            continue;
        }
        for (int i = 0; i < candidate.Operations.Count; i++)
        {
            GeneratedOperation operation = candidate.Operations[i];
            if (operation.TriggerIndex < 0)
            {
                continue;
            }
            GeneratedOperation host = candidate.Operations[operation.TriggerIndex];
            if (host.Spec.Trigger?.Kind == "owner_hp_lost_during_turn"
                && operation.Spec.ParsedOpcode == SpecOpcode.LoseHp)
            {
                chainHits++;
                if (chainCard == null)
                {
                    chainCard = candidate;
                    chainHostIndex = operation.TriggerIndex;
                    chainEffectIndex = i;
                }
            }
        }
    }
}
Check("the chain was found", chainCard != null, true);
if (chainCard != null)
{
    CardPlan chainPlan = OperationPlanner.PlanCard(
        chainCard, InterpreterContext.OnPlay(CardType.Power, TargetAvailability.SelectedEnemy));
    OperationPlan hostPlan = chainPlan[chainHostIndex];
    OperationPlan effectPlan = chainPlan[chainEffectIndex];

    Console.WriteLine($"  host   [{chainHostIndex}] {hostPlan.Opcode}/{hostPlan.Variant} " +
                      $"disposition={hostPlan.Disposition} trigger={hostPlan.TriggerKind}/{hostPlan.TriggerLifetime}");
    Console.WriteLine($"  effect [{chainEffectIndex}] {effectPlan.Opcode}/{effectPlan.Variant} " +
                      $"disposition={effectPlan.Disposition} target={effectPlan.Target} " +
                      $"action={effectPlan.Action} amount={effectPlan.Amount} props={(int)effectPlan.Props}");

    Check("the trigger host is carried", hostPlan.Disposition, PlanDisposition.CarriedHost);
    Check("the host resolves as a trigger event", hostPlan.Action, PlannedActionKind.TriggerEvent);
    Check("the host carries the contract's trigger kind", hostPlan.TriggerKind, "owner_hp_lost_during_turn");
    Check("the host lifetime is combat", hostPlan.TriggerLifetime, "combat");
    Check("the linked effect is deferred to its host", effectPlan.Disposition, PlanDisposition.LinkedEffect);
    Check("the linked effect points back at the host", effectPlan.HostIndex, chainHostIndex);
    Check("the linked effect is lose_hp", effectPlan.Action, PlannedActionKind.LoseHp);
    Check("the linked effect is self-targeted", effectPlan.Target, PlannedTargetSelector.Self);
    Check("the linked effect uses props 14", (int)effectPlan.Props, 14);
    Check("the card arms a carried host", chainPlan.ArmsCarriedHosts, true);
    Check("the card's forward scan finds exactly this linked effect",
        chainPlan.LinkedEffectIndices(chainHostIndex).Contains(chainEffectIndex), true);
    Console.WriteLine($"  scanned {chainHits} such chains; sample card:");
    Console.WriteLine("    " + chainPlan.Describe().Replace("\n", "\n    "));
}

Console.WriteLine();
Console.WriteLine(failures == 0 ? "ALL CHECKS PASSED" : $"{failures} CHECK(S) FAILED");
return failures == 0 ? 0 : 1;

// Minimal shape of research/native_reference_cards.json - only the fields this
// probe reads. The file is ReferenceOnly / GenerationEligible:false.
internal sealed class ReferenceRoot
{
    public List<ReferenceCard> Cards { get; set; } = new();
}

internal sealed class ReferenceCard
{
    public string CatalogId { get; set; } = string.Empty;
    public List<ReferenceComponent> Components { get; set; } = new();
}

internal sealed class ReferenceComponent
{
    public string? SemanticId { get; set; }
    public int TriggerOwner { get; set; } = -1;
    public ReferenceSpec? RuntimeSpec { get; set; }
}

internal sealed class ReferenceSpec
{
    public string Opcode { get; set; } = string.Empty;
    public string Variant { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string SourceZone { get; set; } = string.Empty;
    public string DestinationZone { get; set; } = string.Empty;
    public string CardFilter { get; set; } = string.Empty;
    public List<ReferenceValue> Values { get; set; } = new();
}

internal sealed class ReferenceValue
{
    public string Id { get; set; } = string.Empty;
    public int BaseValue { get; set; }
    public int Offset { get; set; }
    public bool Upgradable { get; set; }
}

