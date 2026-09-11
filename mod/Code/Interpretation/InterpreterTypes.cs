using AutoAnthonyRelics.Data;
using AutoAnthonyRelics.Generation;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.ValueProps;

namespace AutoAnthonyRelics.Interpretation;

/// <summary>
/// Stage C, slices 1+2: the PURE planning half of the interpreter.
///
/// WHY THE SPLIT IS A HARD REQUIREMENT, NOT A STYLE CHOICE
/// ------------------------------------------------------
/// tools/step4-probe runs OUTSIDE Godot. Touching a Godot static there is a
/// native access violation (0xC0000005) that try/catch cannot contain - this
/// already happened once on this project (see DEVLOG Session 45 of
/// QuriousCraftingRelics). So the interpreter is cut in two:
///
///   (a) this file + OperationPlanner + VariantTable: no Godot static, no
///       engine command, no async. A total function from
///       (GeneratedCard, operationIndex, InterpreterContext) to a plan. The
///       probe can therefore assert on the interpreter's semantics without an
///       engine.
///   (b) EffectExecutor: turns a plan into engine commands. Engine-facing only.
///
/// What this file MAY reference is the engine's plain enums (CardType, ValueProp,
/// PileType). Those are managed value types with no static constructor and no
/// native interop; the two ValueProp derivations the original performs are
/// literally functions of CardType, so re-declaring the enum here would be a
/// copy that can silently drift. Everything stateful stays in EffectExecutor.
/// </summary>
public enum PlanOutcome
{
    /// <summary>We know what this operation means and resolved it in full.</summary>
    Resolved,

    /// <summary>
    /// A <c>*_proxyatomic_*</c> fragment: the original hands these off to the
    /// engine's own card model rather than re-implementing the effect. We do the
    /// same, so the plan is "let the native card do it" - a real answer, not a
    /// failure. See VariantTable.DelegatedToNative.
    /// </summary>
    DelegatedToNative,

    /// <summary>
    /// We recognise the pair but have not written its handler yet. This is
    /// deliberately distinct from "we do not recognise it": an unrecognised pair
    /// throws out of VariantTable.Classify instead of arriving here.
    /// </summary>
    Unsupported,
}

/// <summary>The resolved effect of one operation.</summary>
public enum PlannedActionKind
{
    None,

    // ---- slice 1: effects that do something -------------------------------
    DealDamage,
    GainBlock,
    /// <summary>apply_power through the original's non-self switch (weak / vulnerable / ...).</summary>
    ApplyPower,
    /// <summary>apply_power through the original's StructuredSelfPowerRoute (self-targeted).</summary>
    ApplySelfPower,
    DrawCards,
    GainEnergy,
    GainStars,
    LoseHp,
    Heal,

    // ---- slice 2: trigger / condition -------------------------------------
    /// <summary>A trigger host carried for the whole of its lifetime.</summary>
    TriggerEvent,
    /// <summary>A condition that gates its linked effects at its own slot.</summary>
    ConditionGate,
}

/// <summary>
/// Where an operation sits in the play sequence. This is the runtime half of the
/// mechanism contract: <c>triggerIndex</c> was decided at generation time, and
/// this is how the interpreter reads it back.
/// </summary>
public enum PlanDisposition
{
    /// <summary>Resolved and run in slot order when the card is played.</summary>
    OnPlay,

    /// <summary>
    /// <c>TriggerIndex &gt;= 0</c> and the host is an AbilityTrigger / lingering
    /// trigger: resolved HERE (so the numbers and target are frozen at plan
    /// time) but run by the host when its event fires - never on play.
    /// </summary>
    LinkedEffect,

    /// <summary>
    /// A trigger host with no link: armed so that its linked effects can fire.
    /// Mirrors the original's ChaosCompositePower arming branch
    /// (ChaosOperationExecutor.cs:199-223).
    /// </summary>
    CarriedHost,

    /// <summary>
    /// A ConditionalTrigger / <c>condition</c> opcode host. The original does
    /// not execute these; it evaluates the condition once at the host's own slot
    /// and lets the linked effects run inline when it holds
    /// (ChaosOperationExecutor.cs:152, :173).
    /// </summary>
    InlineGate,

    /// <summary>
    /// Scope Modifier. The original never executes these directly
    /// (ChaosOperationExecutor.cs:117-121 skips scope 2 and 3); they only change
    /// how sibling effects resolve.
    /// </summary>
    Modifier,
}

/// <summary>Which creature(s) the resolved effect acts on.</summary>
public enum PlannedTargetSelector
{
    None,
    Self,
    SelectedEnemy,
    AllEnemies,
    RandomEnemy,
    /// <summary>The creature supplied by the triggering event (original: state.Target in a triggered context).</summary>
    EventEnemy,
    Other,
}

/// <summary>
/// The resolved power, already mapped past the original's two apply_power
/// routes. The executor maps this onto an engine PowerModel type; keeping it an
/// enum here is what lets the probe assert the route without an engine.
/// </summary>
public enum PlannedPowerKind
{
    None,
    Strength,
    StrengthGain,
    StrengthLoss,
    StrengthThisTurn,
    StrengthLossThisTurn,
    StrengthPerTargetVulnerable,
    DexterityGain,
    DexterityLoss,
    Vulnerable,
    /// <summary>Re-applies the target's current Vulnerable amount (ChaosOperationExecutor.cs:1099-1114).</summary>
    VulnerableDouble,
    Weak,
    Doom,
    FocusLoss,
    Plating,
    Vigor,
    RetainHandThisTurn,
}

/// <summary>
/// What the context can supply as a target. The original's equivalent is
/// <c>state.Target ?? cardPlay.Target</c> plus the event creature, so this is the
/// pure stand-in for "is there a creature to point at".
/// </summary>
public enum TargetAvailability
{
    None,
    SelectedEnemy,
    EventEnemy,
}

/// <summary>
/// Everything the plan needs to know about the moment of execution. This is the
/// pure projection of the original's <c>ChaosExecutionState</c> - only the
/// fields slices 1+2 consume.
/// </summary>
public sealed record InterpreterContext(
    CardType CardType,
    bool IsTriggered = false,
    bool UsePoweredCardDamage = true,
    TargetAvailability Target = TargetAvailability.None,
    string OwnerId = "",
    bool SourceInCombatPile = true)
{
    /// <summary>
    /// A card being played normally: not triggered, so the powered-attack flag is
    /// irrelevant (the original only consults it when IsTriggered).
    /// </summary>
    public static InterpreterContext OnPlay(
        CardType cardType,
        TargetAvailability target = TargetAvailability.None,
        string ownerId = "") =>
        new(cardType, IsTriggered: false, UsePoweredCardDamage: true, Target: target, OwnerId: ownerId);

    /// <summary>
    /// A linked effect being run by its trigger host. UsePoweredCardDamage is
    /// derived exactly as ChaosOperationExecutor.cs:345 does it, from whether the
    /// source card is in a combat pile and is not a Power.
    /// </summary>
    public static InterpreterContext Triggered(
        CardType cardType,
        bool sourceInCombatPile,
        TargetAvailability target = TargetAvailability.None,
        string ownerId = "") =>
        new(
            cardType,
            IsTriggered: true,
            UsePoweredCardDamage: OperationPlanner.TriggeredDamageUsesPoweredAttack(sourceInCombatPile, cardType),
            Target: target,
            OwnerId: ownerId,
            SourceInCombatPile: sourceInCombatPile);
}

/// <summary>
/// One operation, fully resolved. The probe asserts on this; EffectExecutor
/// consumes it.
///
/// Opcode / Variant / DeclaredTarget are echoed verbatim from the runtime spec.
/// That is not redundant: the native-reference round trip checks that the
/// planner never silently reinterprets a spec, so the plan has to carry what the
/// spec actually said next to what we decided it means.
/// </summary>
public sealed record OperationPlan
{
    public int OperationIndex { get; init; }
    public PlanOutcome Outcome { get; init; } = PlanOutcome.Unsupported;
    public PlannedActionKind Action { get; init; } = PlannedActionKind.None;
    public PlanDisposition Disposition { get; init; } = PlanDisposition.OnPlay;

    /// <summary>Spec opcode, echoed.</summary>
    public string Opcode { get; init; } = string.Empty;

    /// <summary>Spec variant, echoed.</summary>
    public string Variant { get; init; } = string.Empty;

    /// <summary>Spec target string, echoed.</summary>
    public string DeclaredTarget { get; init; } = string.Empty;

    public PlannedTargetSelector Target { get; init; } = PlannedTargetSelector.None;

    /// <summary>Index of the trigger host this operation hangs off, or -1.</summary>
    public int HostIndex { get; init; } = -1;

    /// <summary>
    /// Primary resolved amount. Mirrors the original's OperationAmount: the first
    /// UPGRADABLE value slot, falling back to the first slot. Never re-derived -
    /// it comes straight out of the generator's ResolvedValue.
    /// </summary>
    public int Amount { get; init; }

    /// <summary>Resolved hit count for damage; 1 when the spec carries no hits slot.</summary>
    public int Hits { get; init; } = 1;

    /// <summary>
    /// The ValueProp handed to the engine. For DealDamage this is
    /// DamagePropsForCardEffect; for GainBlock BlockPropsForCardEffect; for LoseHp
    /// the original's hardcoded 14 (self) / 6 (other).
    /// </summary>
    public ValueProp Props { get; init; }

    public PlannedPowerKind PowerKind { get; init; } = PlannedPowerKind.None;

    public Zone SourceZone { get; init; } = Zone.None;
    public Zone DestinationZone { get; init; } = Zone.None;

    public string? ConditionKind { get; init; }
    public string? TriggerKind { get; init; }
    public string? TriggerLifetime { get; init; }

    /// <summary>Resolved threshold slot (conditions / triggers), 0 when absent.</summary>
    public int Threshold { get; init; }

    /// <summary>
    /// The generator's resolved slots, passed through untouched. Kept whole so a
    /// later slice can read slots this slice does not name.
    /// </summary>
    public IReadOnlyList<ResolvedValue> Values { get; init; } = Array.Empty<ResolvedValue>();

    /// <summary>
    /// Set when Outcome is DelegatedToNative: the template whose native card the
    /// original hands this fragment to.
    /// </summary>
    public string? DelegatedTemplate { get; init; }

    /// <summary>Set when Outcome is Unsupported: why the pair is still pending.</summary>
    public string? PendingReason { get; init; }

    /// <summary>
    /// True when the effect is real but must be skipped because the creature it
    /// points at is absent - the original's <c>if (target == null) return true;</c>
    /// (ChaosOperationExecutor.cs:1166-1169, :1368-1372).
    /// </summary>
    public bool TargetUnavailable { get; init; }

    /// <summary>
    /// True when damage bypasses the powered-attack pipeline and goes straight
    /// through CreatureCmd.Damage. Mirrors the original's local <c>flag</c>
    /// (ChaosOperationExecutor.cs:1361).
    /// </summary>
    public bool DirectDamage { get; init; }

    /// <summary>
    /// True when this operation drives its linked effects once per iterated card
    /// instead of once per play (the original's for-each family,
    /// ChaosOperationExecutor.cs:381-407). Iteration itself is slice 3.
    /// </summary>
    public bool IteratesLinkedEffects { get; init; }

    public bool RunsOnPlay => Disposition == PlanDisposition.OnPlay;
    public bool IsCarried => Disposition == PlanDisposition.CarriedHost;
    public bool IsLinkedEffect => Disposition == PlanDisposition.LinkedEffect;

    public int ValueOf(string id, int fallback = 0)
    {
        foreach (ResolvedValue value in Values)
        {
            if (string.Equals(value.Id, id, StringComparison.Ordinal))
            {
                return value.Value;
            }
        }
        return fallback;
    }
}

/// <summary>
/// The whole card, planned. Exists so the executor and the probe both read the
/// same thing, and so the trigger dispatch (which is inherently card-level: it
/// scans forward for operations whose TriggerIndex equals the host's) lives in
/// one place.
/// </summary>
public sealed record CardPlan(
    string Character,
    string ShellId,
    GeneratedCardType CardType,
    GeneratedRarity Rarity,
    IReadOnlyList<OperationPlan> Operations,
    bool ArmsCarriedHosts = false)
{
    public OperationPlan this[int index] => Operations[index];

    public int ImplementedCount => Operations.Count(o => o.Outcome == PlanOutcome.Resolved);
    public int DelegatedCount => Operations.Count(o => o.Outcome == PlanOutcome.DelegatedToNative);
    public int PendingCount => Operations.Count(o => o.Outcome == PlanOutcome.Unsupported);

    public bool FullyImplemented => PendingCount == 0 && DelegatedCount == 0;

    /// <summary>
    /// The original's forward scan: every operation AFTER the host whose
    /// triggerIndex equals the host's index
    /// (ChaosOperationExecutor.cs:291-293).
    /// </summary>
    public IReadOnlyList<int> LinkedEffectIndices(int hostIndex)
    {
        var result = new List<int>();
        for (int i = hostIndex + 1; i < Operations.Count; i++)
        {
            if (Operations[i].HostIndex == hostIndex)
            {
                result.Add(i);
            }
        }
        return result;
    }

    public string Describe()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"{Character}/{ShellId} [{Rarity} {CardType}] ops={Operations.Count} ")
          .Append($"resolved={ImplementedCount} delegated={DelegatedCount} pending={PendingCount}");
        foreach (OperationPlan operation in Operations)
        {
            sb.Append("\n    [").Append(operation.OperationIndex).Append("] ")
              .Append(operation.Opcode.PadRight(30))
              .Append(' ').Append(operation.Variant.PadRight(34))
              .Append(" target=").Append(operation.Target.ToString().PadRight(14))
              .Append(" action=").Append(operation.Action.ToString().PadRight(16))
              .Append(" disp=").Append(operation.Disposition.ToString().PadRight(13))
              .Append(" host=").Append(operation.HostIndex)
              .Append(" outcome=").Append(operation.Outcome);
            if (operation.Action is PlannedActionKind.DealDamage or PlannedActionKind.LoseHp or PlannedActionKind.GainBlock)
            {
                sb.Append(" amount=").Append(operation.Amount)
                  .Append(" hits=").Append(operation.Hits)
                  .Append(" props=").Append((int)operation.Props);
            }
            if (operation.PowerKind != PlannedPowerKind.None)
            {
                sb.Append(" power=").Append(operation.PowerKind);
            }
            if (operation.TriggerKind != null)
            {
                sb.Append(" trigger=").Append(operation.TriggerKind)
                  .Append('/').Append(operation.TriggerLifetime);
            }
            if (operation.ConditionKind != null)
            {
                sb.Append(" condition=").Append(operation.ConditionKind);
            }
        }
        return sb.ToString();
    }
}

/// <summary>
/// Thrown when a (opcode, variant) pair is not in the frozen pool manifest.
/// Deliberately an exception rather than a silent Pending: "we have not written
/// this handler yet" and "we do not recognise this at all" are different facts,
/// and collapsing them is what makes a variant table worthless.
/// </summary>
public sealed class UnclassifiedVariantException : Exception
{
    public string Opcode { get; }
    public string Variant { get; }

    public UnclassifiedVariantException(string opcode, string variant)
        : base($"unclassified (opcode, variant) pair: '{opcode}' + '{variant}'. " +
               "It is not in VariantTable.PoolPairs, so it is not part of the pool this " +
               "interpreter was built against - a data revision would have to be classified " +
               "explicitly rather than falling through to Pending.")
    {
        Opcode = opcode;
        Variant = variant;
    }
}
