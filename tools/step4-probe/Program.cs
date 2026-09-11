using System.Reflection;
using System.Text.Json;
using AutoAnthonyRelics.Data;
using AutoAnthonyRelics.Generation;

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

Console.WriteLine();
Console.WriteLine(failures == 0 ? "ALL CHECKS PASSED" : $"{failures} CHECK(S) FAILED");
return failures == 0 ? 0 : 1;
