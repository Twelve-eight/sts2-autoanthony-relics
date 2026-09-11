using AutoAnthonyRelics.Data;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace AutoAnthonyRelics.Interpretation;

/// <summary>
/// The engine-facing half of the interpreter: plan -&gt; engine commands.
///
/// This file is the ONLY place in the Interpretation namespace that touches the
/// engine (and it touches no Godot static at all). It is deliberately not
/// exercised by tools/step4-probe - the probe asserts on OperationPlanner, which
/// is pure. What this file owes the project is structural faithfulness: same
/// commands, same argument order, same branches as
/// G:/omp works/.tmp/aa-decompile/src/AutoAnthony/ChaosOperationExecutor.cs.
///
/// Everything stateful that the plan cannot know lives in <see cref="EffectContext"/>:
/// the owner, the card being played, the resolved event creature, and the two
/// pieces of combat history the condition table reads (LastDrawnCards,
/// LastAttackKilled). Nothing else is consulted, so the plan remains the single
/// description of what an operation does.
/// </summary>
public sealed record EffectContext(
    PlayerChoiceContext ChoiceContext,
    Player Owner,
    CardModel Card,
    CardPlay? CardPlay,
    ICombatState? CombatState,
    Creature? EventTarget = null,
    bool IsTriggered = false,
    bool LastAttackKilled = false,
    IReadOnlyList<CardModel>? LastDrawnCards = null)
{
    public IReadOnlyList<CardModel> DrawnCards => LastDrawnCards ?? Array.Empty<CardModel>();
}

internal static class EffectExecutor
{
    /// <summary>
    /// Walk a card's plan the way ChaosOperationExecutor.Play walks the operation
    /// list (:109-179):
    ///   * CarriedHost / InlineGate / Modifier / LinkedEffect operations do NOT
    ///     run here;
    ///   * an OnPlay operation that hangs off an InlineGate host is gated by that
    ///     host's condition, evaluated once per host (:173);
    ///   * everything else runs in slot order.
    /// </summary>
    public static async Task PlayAsync(CardPlan plan, EffectContext context)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(context);

        var conditionCache = new Dictionary<int, bool>();
        for (int i = 0; i < plan.Operations.Count; i++)
        {
            OperationPlan operation = plan.Operations[i];
            if (!operation.RunsOnPlay)
            {
                continue;
            }
            if (operation.HostIndex >= 0)
            {
                OperationPlan host = plan.Operations[operation.HostIndex];
                if (host.Disposition == PlanDisposition.InlineGate
                    && !EvaluateConditionOnce(conditionCache, operation.HostIndex, host, context))
                {
                    continue;
                }
            }
            await ExecuteAsync(operation, context);
        }
    }

    /// <summary>
    /// Run the linked effects of one host, mirroring
    /// ChaosOperationExecutor.ExecuteTriggered (:286-379): every operation after
    /// the host whose triggerIndex equals the host's, skipping Modifier-scope ones
    /// (:360).
    /// </summary>
    public static async Task ExecuteTriggeredAsync(CardPlan plan, int hostIndex, EffectContext context)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(context);

        foreach (int index in plan.LinkedEffectIndices(hostIndex))
        {
            OperationPlan effect = plan.Operations[index];
            if (effect.Disposition != PlanDisposition.LinkedEffect)
            {
                continue;
            }
            await ExecuteAsync(effect, context);
        }
    }

    /// <summary>
    /// Execute one resolved operation. Returns false when the plan says this
    /// operation is not ours to run (delegated, pending, or a non-effect slot).
    /// </summary>
    public static async Task<bool> ExecuteAsync(OperationPlan plan, EffectContext context)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(context);

        if (plan.Outcome != PlanOutcome.Resolved)
        {
            return false;
        }

        switch (plan.Action)
        {
            case PlannedActionKind.DealDamage:
                return await ExecuteDamageAsync(plan, context);
            case PlannedActionKind.GainBlock:
                // ChaosOperationExecutor.cs:944-947. The original passes null for
                // the CardPlay when triggered, and the block ValueProp already
                // encodes "triggered".
                await CreatureCmd.GainBlock(
                    context.Owner.Creature,
                    plan.Amount,
                    plan.Props,
                    context.IsTriggered ? null : context.CardPlay,
                    false);
                return true;
            case PlannedActionKind.DrawCards:
                // ChaosOperationExecutor.cs:965-967.
                await CardPileCmd.Draw(context.ChoiceContext, plan.Amount, context.Owner, false);
                return true;
            case PlannedActionKind.GainEnergy:
                await PlayerCmd.GainEnergy(plan.Amount, context.Owner);
                return true;
            case PlannedActionKind.GainStars:
                await PlayerCmd.GainStars(plan.Amount, context.Owner);
                return true;
            case PlannedActionKind.LoseHp:
                return await ExecuteLoseHpAsync(plan, context);
            case PlannedActionKind.Heal:
                await CreatureCmd.Heal(context.Owner.Creature, plan.Amount, true);
                return true;
            case PlannedActionKind.ApplyPower:
            case PlannedActionKind.ApplySelfPower:
                return await ExecutePowerAsync(plan, context);
            case PlannedActionKind.TriggerEvent:
            case PlannedActionKind.ConditionGate:
                // Nothing to do at execution time: arming the host and evaluating
                // the gate both happen at the card level (PlayAsync /
                // ExecuteTriggeredAsync). Recognised, not ignored.
                return true;
            default:
                return false;
        }
    }

    private static async Task<bool> ExecuteDamageAsync(OperationPlan plan, EffectContext context)
    {
        if (plan.Hits == 0)
        {
            return true;
        }

        switch (plan.Target)
        {
            case PlannedTargetSelector.SelectedEnemy:
            case PlannedTargetSelector.EventEnemy:
            {
                Creature? target = ResolveCreatureTarget(plan, context);
                if (target == null)
                {
                    return true;
                }
                if (plan.DirectDamage)
                {
                    for (int hit = 0; hit < plan.Hits; hit++)
                    {
                        await CreatureCmd.Damage(
                            context.ChoiceContext, target, plan.Amount, plan.Props,
                            context.Owner.Creature, context.Card, context.CardPlay);
                    }
                    return true;
                }
                // NOTE: the original also chains .WithHitFx(card.Definition.HitFx,
                // null, null) here; our card model does not exist until stage D,
                // and hit VFX is purely cosmetic, so it is omitted rather than
                // hardcoded.
                await DamageCmd.Attack(plan.Amount)
                    .WithHitCount(plan.Hits)
                    .FromCard(context.Card, context.CardPlay)
                    .Targeting(target)
                    .Execute(context.ChoiceContext);
                return true;
            }
            case PlannedTargetSelector.AllEnemies:
            {
                if (context.CombatState is not { } combatState)
                {
                    return true;
                }
                if (plan.DirectDamage)
                {
                    for (int hit = 0; hit < plan.Hits; hit++)
                    {
                        await CreatureCmd.Damage(
                            context.ChoiceContext, combatState.HittableEnemies, plan.Amount, plan.Props,
                            context.Owner.Creature, context.Card, context.CardPlay);
                    }
                    return true;
                }
                await DamageCmd.Attack(plan.Amount)
                    .WithHitCount(plan.Hits)
                    .FromCard(context.Card, context.CardPlay)
                    .TargetingAllOpponents(combatState)
                    .Execute(context.ChoiceContext);
                return true;
            }
            case PlannedTargetSelector.RandomEnemy:
            {
                if (context.CombatState is not { } combatState)
                {
                    return true;
                }
                if (plan.DirectDamage)
                {
                    for (int hit = 0; hit < plan.Hits; hit++)
                    {
                        Creature? target = context.Owner.RunState.Rng.CombatTargets
                            .NextItem(combatState.HittableEnemies);
                        if (target == null)
                        {
                            continue;
                        }
                        await CreatureCmd.Damage(
                            context.ChoiceContext, target, plan.Amount, plan.Props,
                            context.Owner.Creature, context.Card, context.CardPlay);
                    }
                    return true;
                }
                await DamageCmd.Attack(plan.Amount)
                    .WithHitCount(plan.Hits)
                    .FromCard(context.Card, context.CardPlay)
                    .TargetingRandomOpponents(combatState, true)
                    .Execute(context.ChoiceContext);
                return true;
            }
            default:
                return false;
        }
    }

    private static async Task<bool> ExecuteLoseHpAsync(OperationPlan plan, EffectContext context)
    {
        // ChaosOperationExecutor.cs:1013-1024. Note the two overloads differ:
        // the self branch passes no dealer at all.
        if (plan.Target == PlannedTargetSelector.Self)
        {
            await CreatureCmd.Damage(
                context.ChoiceContext, context.Owner.Creature, plan.Amount, plan.Props,
                context.Card, context.CardPlay);
            return true;
        }

        Creature? target = ResolveCreatureTarget(plan, context);
        if (target == null)
        {
            return true;
        }
        await CreatureCmd.Damage(
            context.ChoiceContext, target, plan.Amount, plan.Props,
            context.Owner.Creature, context.Card, context.CardPlay);
        return true;
    }

    private static async Task<bool> ExecutePowerAsync(OperationPlan plan, EffectContext context)
    {
        Player owner = context.Owner;
        Creature applier = owner.Creature;
        decimal amount = plan.Amount;

        // ---- ChaosOperationExecutor.cs:1196-1270: the self route -------------
        if (plan.Action == PlannedActionKind.ApplySelfPower)
        {
            switch (plan.PowerKind)
            {
                case PlannedPowerKind.DexterityGain:
                    await PowerCmd.Apply<DexterityPower>(context.ChoiceContext, applier, amount, applier, context.Card, false);
                    return true;
                case PlannedPowerKind.DexterityLoss:
                    await PowerCmd.Apply<DexterityPower>(context.ChoiceContext, applier, -amount, applier, context.Card, false);
                    return true;
                case PlannedPowerKind.Doom:
                    await PowerCmd.Apply<DoomPower>(context.ChoiceContext, applier, amount, applier, context.Card, false);
                    return true;
                case PlannedPowerKind.FocusLoss:
                    await PowerCmd.Apply<FocusPower>(context.ChoiceContext, applier, -amount, applier, context.Card, false);
                    return true;
                case PlannedPowerKind.Plating:
                    await PowerCmd.Apply<PlatingPower>(context.ChoiceContext, applier, amount, applier, context.Card, false);
                    return true;
                case PlannedPowerKind.Vigor:
                    await PowerCmd.Apply<VigorPower>(context.ChoiceContext, applier, amount, applier, context.Card, false);
                    return true;
                case PlannedPowerKind.Strength:
                    await PowerCmd.Apply<StrengthPower>(context.ChoiceContext, applier, amount, applier, context.Card, false);
                    return true;
                case PlannedPowerKind.StrengthThisTurn:
                    await PowerCmd.Apply<SetupStrikePower>(context.ChoiceContext, applier, amount, applier, context.Card, false);
                    return true;
                case PlannedPowerKind.StrengthLoss:
                    await PowerCmd.Apply<StrengthPower>(context.ChoiceContext, applier, -amount, applier, context.Card, false);
                    return true;
                case PlannedPowerKind.StrengthLossThisTurn:
                    await PowerCmd.Apply<ManglePower>(context.ChoiceContext, applier, amount, applier, context.Card, false);
                    return true;
                case PlannedPowerKind.StrengthPerTargetVulnerable:
                {
                    Creature? target = ResolveCreatureTarget(plan, context);
                    int vulnerable = target?.GetPowerAmount<VulnerablePower>() ?? 0;
                    int strength = vulnerable * Math.Max(1, plan.Amount);
                    await PowerCmd.Apply<StrengthPower>(context.ChoiceContext, applier, strength, applier, context.Card, false);
                    return true;
                }
                default:
                    return false;
            }
        }

        // ---- ChaosOperationExecutor.cs:1055-1191: the common switch ----------
        switch (plan.PowerKind)
        {
            case PlannedPowerKind.RetainHandThisTurn:
                // The original applies exactly 1 regardless of the numeric slot (:1122).
                await PowerCmd.Apply<RetainHandPower>(context.ChoiceContext, applier, 1m, applier, context.Card, false);
                return true;
            case PlannedPowerKind.VulnerableDouble:
            {
                Creature? target = ResolveCreatureTarget(plan, context);
                if (target == null)
                {
                    return true;
                }
                int existing = target.GetPowerAmount<VulnerablePower>();
                if (existing > 0)
                {
                    await PowerCmd.Apply<VulnerablePower>(context.ChoiceContext, target, existing, applier, context.Card, false);
                }
                return true;
            }
        }

        if (plan.Target == PlannedTargetSelector.AllEnemies)
        {
            if (context.CombatState is not { } combatState)
            {
                return true;
            }
            IReadOnlyList<Creature> enemies = combatState.HittableEnemies;
            switch (plan.PowerKind)
            {
                case PlannedPowerKind.Vulnerable:
                    await PowerCmd.Apply<VulnerablePower>(context.ChoiceContext, enemies, amount, applier, context.Card, false);
                    return true;
                case PlannedPowerKind.Weak:
                    await PowerCmd.Apply<WeakPower>(context.ChoiceContext, enemies, amount, applier, context.Card, false);
                    return true;
                case PlannedPowerKind.StrengthLossThisTurn:
                    foreach (Creature enemy in enemies)
                    {
                        await PowerCmd.Apply<ManglePower>(context.ChoiceContext, enemy, amount, applier, context.Card, false);
                    }
                    return true;
                case PlannedPowerKind.StrengthLoss:
                    foreach (Creature enemy in enemies)
                    {
                        await PowerCmd.Apply<StrengthPower>(context.ChoiceContext, enemy, -amount, applier, context.Card, false);
                    }
                    return true;
                default:
                    // The original's all_enemies else branch is Strength gain (:1158-1161).
                    foreach (Creature enemy in enemies)
                    {
                        await PowerCmd.Apply<StrengthPower>(context.ChoiceContext, enemy, amount, applier, context.Card, false);
                    }
                    return true;
            }
        }

        Creature? single = ResolveCreatureTarget(plan, context);
        if (single == null)
        {
            return true;
        }
        switch (plan.PowerKind)
        {
            case PlannedPowerKind.Vulnerable:
                await PowerCmd.Apply<VulnerablePower>(context.ChoiceContext, single, amount, applier, context.Card, false);
                return true;
            case PlannedPowerKind.Weak:
                await PowerCmd.Apply<WeakPower>(context.ChoiceContext, single, amount, applier, context.Card, false);
                return true;
            case PlannedPowerKind.StrengthLossThisTurn:
                await PowerCmd.Apply<ManglePower>(context.ChoiceContext, single, amount, applier, context.Card, false);
                return true;
            case PlannedPowerKind.StrengthLoss:
                await PowerCmd.Apply<StrengthPower>(context.ChoiceContext, single, -amount, applier, context.Card, false);
                return true;
            default:
                // Includes strength_gain: the original's else branch is Strength gain (:1184).
                await PowerCmd.Apply<StrengthPower>(context.ChoiceContext, single, amount, applier, context.Card, false);
                return true;
        }
    }

    /// <summary>
    /// The original's <c>state.Target ?? cardPlay.Target</c>, plus the random
    /// re-roll ExecuteWithResolvedTarget performs for explicitly random targets
    /// (:492-504).
    /// </summary>
    private static Creature? ResolveCreatureTarget(OperationPlan plan, EffectContext context)
    {
        switch (plan.Target)
        {
            case PlannedTargetSelector.EventEnemy:
            case PlannedTargetSelector.SelectedEnemy:
                return context.EventTarget ?? context.CardPlay?.Target;
            case PlannedTargetSelector.RandomEnemy:
                return context.CombatState is { } combatState
                    ? context.Owner.RunState.Rng.CombatTargets.NextItem(combatState.HittableEnemies)
                    : null;
            default:
                return null;
        }
    }

    /// <summary>
    /// ChaosOperationExecutor.cs:4577-4584: a host's condition is evaluated at
    /// most once per play, and every linked effect reads the same answer.
    /// </summary>
    private static bool EvaluateConditionOnce(
        Dictionary<int, bool> cache,
        int hostIndex,
        OperationPlan host,
        EffectContext context)
    {
        if (cache.TryGetValue(hostIndex, out bool cached))
        {
            return cached;
        }
        bool value = ConditionMatches(host, context);
        cache[hostIndex] = value;
        return value;
    }

    /// <summary>
    /// ChaosOperationExecutor.cs:4485-4575. Only the kinds VariantTable classifies
    /// as Implemented are handled; an unknown kind returns true, which is the
    /// original's own default branch.
    /// </summary>
    private static bool ConditionMatches(OperationPlan host, EffectContext context)
    {
        string? kind = host.ConditionKind;
        if (kind == null)
        {
            return true;
        }
        ICombatState? combatState = context.CombatState;
        Player owner = context.Owner;
        int threshold = Math.Max(1, host.Threshold);

        switch (kind)
        {
            case "fatal":
                return context.LastAttackKilled;
            case "exhaust_pile_minimum":
                return PileType.Exhaust.GetPile(owner).Cards.Count >= threshold;
            case "card_exhausted_this_turn":
                return combatState != null
                    && CombatManager.Instance.History.Entries.OfType<CardExhaustedEntry>()
                        .Any(e => e.Actor == owner.Creature && e.HappenedThisTurn(combatState));
            case "owner_lost_hp_this_turn":
                return combatState != null
                    && CombatManager.Instance.History.Entries.OfType<DamageReceivedEntry>()
                        .Any(e => e.Receiver == owner.Creature
                                  && e.Result.UnblockedDamage > 0
                                  && e.HappenedThisTurn(combatState));
            case "target_has_vulnerable":
            {
                Creature? target = ResolveCreatureTarget(host, context);
                return (target?.GetPowerAmount<VulnerablePower>() ?? 0) > 0;
            }
            case "target_has_poison":
            {
                Creature? target = ResolveCreatureTarget(host, context);
                return (target?.GetPowerAmount<PoisonPower>() ?? 0) > 0;
            }
            case "draw_pile_empty":
                return PileType.Draw.GetPile(owner).Cards.Count == 0;
            case "last_drawn_card_is_skill":
                return context.DrawnCards.Count == 1 && context.DrawnCards[0].Type == CardType.Skill;
            case "cards_played_this_turn_below":
                return combatState != null
                    && CombatManager.Instance.History.CardPlaysFinished
                        .Count(e => e.CardPlay.Player == owner && e.HappenedThisTurn(combatState)) < threshold;
            case "enemy_intends_attack":
            {
                Creature? target = ResolveCreatureTarget(host, context);
                return target?.Monster?.IntendsToAttack == true;
            }
            case "osty_alive":
                return owner.IsOstyAlive;
            case "doom_applied_this_turn":
                return combatState != null
                    && CombatManager.Instance.History.Entries.OfType<PowerReceivedEntry>()
                        .Any(e => e.HappenedThisTurn(combatState)
                                  && e.Power is DoomPower
                                  && e.Applier == owner.Creature);
            case "first_play_of_this_card_this_turn":
                return combatState != null
                    && !CombatManager.Instance.History.CardPlaysFinished
                        .Any(e => e.CardPlay.Card == context.Card && e.HappenedThisTurn(combatState));
            case "osty_attacked_this_turn":
                return combatState != null
                    && CombatManager.Instance.History.Entries.OfType<CreatureAttackedEntry>()
                        .Any(e => e.Actor == owner.Osty && e.HappenedThisTurn(combatState));
            case "no_attacks_in_hand":
                return PileType.Hand.GetPile(owner).Cards.All(c => c.Type != CardType.Attack);
            case "hand_empty":
                return PileType.Hand.GetPile(owner).Cards.Count == 0;
            default:
                return true;
        }
    }
}
