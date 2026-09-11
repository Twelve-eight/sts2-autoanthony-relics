using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AutoAnthonyRelics.Data;

/// <summary>
/// Typed model of the two generation-eligible data files that drive the whole
/// mod:
///
///   catalog_recipes.json      481 card recipes -> 931 atom instances
///   catalog_runtime_specs.json 931 runtime specs, joined 1:1 by SemanticId
///
/// Provenance: both files are the offline output of Alriph's Auto-Anthonyology
/// (mod id AutoAnthony, v0.3.81). See AnthonyCatalog's doc for the attribution
/// rule and research/original-autoauthony-contract.md for the full mechanism
/// contract. The types here mirror the JSON field-for-field on purpose: any
/// rename would silently decouple us from the data.
///
/// Forward compatibility rule: every closed enum below has an Unknown member and
/// every parser degrades to it instead of throwing. A future data revision must
/// not be able to crash mod init.
/// </summary>
public sealed class AtomRecipe
{
    public string Character { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string ChineseTitle { get; set; } = string.Empty;
    public string EnglishTitle { get; set; } = string.Empty;
    public int Cost { get; set; }
    public int StarCost { get; set; }
    public bool HasStarCostX { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string OriginalRarity { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public List<AtomFragment> Atoms { get; set; } = new();
}

/// <summary>
/// One atomic fragment instance as it appears inside a recipe. This is the
/// "condition or effect" fragment the mechanism contract talks about: the
/// scope tells us which of the two it is, and SemanticId is the join key into
/// the runtime spec table.
/// </summary>
public sealed class AtomFragment
{
    public string SemanticId { get; set; } = string.Empty;

    /// <summary>Raw template id, e.g. "A:turnStart", "N:HP-", "T:poison".</summary>
    public string Template { get; set; } = string.Empty;

    public string Scope { get; set; } = string.Empty;
    public string ChineseText { get; set; } = string.Empty;
    public string EnglishText { get; set; } = string.Empty;
    public bool RequiresSingleTarget { get; set; }

    /// <summary>Engine card this fragment references, or "None".</summary>
    public string CardReference { get; set; } = "None";

    /// <summary>
    /// Which earlier fragment this one hangs off, per the ORIGINAL recipe.
    /// -1 means "standalone". The generator does NOT have to honour this: the
    /// binding is re-decided at generation time (see the contract's
    /// LinkedTriggerIndex table), which is exactly how conditions and effects
    /// get recombined.
    /// </summary>
    public int TriggerOwner { get; set; } = -1;

    [JsonIgnore] public FragmentScope ParsedScope => EnumParsing.ParseScope(Scope);
}

public sealed class RuntimeSpecEntry
{
    public string Id { get; set; } = string.Empty;
    public RuntimeSpec Spec { get; set; } = new();
}

/// <summary>
/// The structured semantics of one atom: opcode + target + zones + numeric
/// slots + condition + trigger. This is what makes the fragments machine
/// composable rather than text templates.
/// </summary>
public sealed class RuntimeSpec
{
    public int SchemaVersion { get; set; }
    public string Opcode { get; set; } = string.Empty;

    /// <summary>
    /// 291 distinct values across the pool. Deliberately a string, not an enum:
    /// the variant is resolved by the interpreter's handler registry, and new
    /// variants must degrade to "unhandled" rather than fail to deserialize.
    /// </summary>
    public string Variant { get; set; } = string.Empty;

    public string Target { get; set; } = string.Empty;
    public string SourceZone { get; set; } = string.Empty;
    public string DestinationZone { get; set; } = string.Empty;
    public string CardFilter { get; set; } = string.Empty;

    /// <summary>86 distinct values across the pool. See SpecFlags for the ones we act on.</summary>
    public List<string> Flags { get; set; } = new();

    public List<NumericSlot> Values { get; set; } = new();

    /// <summary>Only 23 of the 931 specs carry a condition.</summary>
    public ConditionSpec? Condition { get; set; }

    /// <summary>Only 116 of the 931 specs carry a trigger (all but one are "event").</summary>
    public TriggerSpec? Trigger { get; set; }

    [JsonIgnore] public SpecOpcode ParsedOpcode => EnumParsing.ParseOpcode(Opcode);
    [JsonIgnore] public SpecTarget ParsedTarget => EnumParsing.ParseTarget(Target);
    [JsonIgnore] public Zone ParsedSourceZone => EnumParsing.ParseZone(SourceZone);
    [JsonIgnore] public Zone ParsedDestinationZone => EnumParsing.ParseZone(DestinationZone);
    [JsonIgnore] public CardFilterKind ParsedCardFilter => EnumParsing.ParseCardFilter(CardFilter);
}

/// <summary>
/// A numeric slot on a fragment. 254 specs have none, 607 have one, 70 have two.
/// BaseValue is the value the ORIGINAL recipe used; the generator re-instantiates
/// it from the balance model rather than reusing it verbatim.
/// </summary>
public sealed class NumericSlot
{
    public string Id { get; set; } = string.Empty;
    public int BaseValue { get; set; }

    /// <summary>"fixed" is the only value observed in the pool.</summary>
    public string Source { get; set; } = string.Empty;

    public int Offset { get; set; }
    public bool Upgradable { get; set; }
    public bool Explicit { get; set; }
}

public sealed class ConditionSpec
{
    public string Kind { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;

    /// <summary>Name of the numeric slot that carries the threshold, or null.</summary>
    public string? ValueSlot { get; set; }
}

public sealed class TriggerSpec
{
    /// <summary>Name of the numeric slot carrying a duration, or null.</summary>
    public string? DurationSlot { get; set; }

    public string Kind { get; set; } = string.Empty;

    /// <summary>combat / next_turn / this_turn / immediate.</summary>
    public string Lifetime { get; set; } = string.Empty;

    /// <summary>Name of the numeric slot carrying a threshold, or null.</summary>
    public string? ThresholdSlot { get; set; }
}
