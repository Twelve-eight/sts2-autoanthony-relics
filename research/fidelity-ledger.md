# 保真台账 - 生成器核心 (阶段 B)

本文件回答一个问题: **本 mod 的生成器与原版 `AutoAnthony` 的生成器, 逐条差在哪。**

每一行都注明是"照实复刻"、"近似替代"还是"省略", 以及代价与恢复成本。
代码里对应的注释标记: `exact` / `APPROXIMATED` / `OMITTED`。

参考源: `G:/omp works/.tmp/aa-decompile/src/ChaosCardGenerator/`
(`ComponentAssemblyGenerator.cs` / `CardEffectRules.cs` / `EffectSelectionTuning.cs` /
`ImmutableComponentCatalog.cs` / `EffectBalanceModel.cs` / `NumericGenerationTuning.cs` /
`NativeComponentFrequencyTracker.cs` / `ResourceEconomyModel.cs`)。

---

## 0. 两条已裁定的口径 (用户决定, 不是我的取舍)

| 项 | 裁定 | 影响 |
| --- | --- | --- |
| 数值槽 | **沿用原配方 `BaseValue + Offset`** | 不重写平衡表; 数值等于原卡数值 |
| 候选权重 | **纯均匀随机** | 不重写口味权重表; 分布与原版不同 |

这两条决定了下面"省略"区的大部分内容。**它们同时写进卡片指纹**, 所以改口径 =
全仓库重来 (存档 / 联机握手 / 可复现性), 不是可以随手调的参数。

---

## 1. 照实复刻 (exact)

判定顺序与集合内容逐条对齐, 探针可判定。

### 1.1 绑定判定 `TriggerBinder.LinkedTriggerIndex`

原版 `ComponentAssemblyGenerator.cs:2112-2188`, 14 条按序判定, 全部实现:

| # | 判定 | 说明 |
| --- | --- | --- |
| 0 | `operations.Count == 0` | 无历史 |
| 1 | `Template == "D:IncreaseThisCardCost"` | 永不绑定 |
| 2 | `IsRestrictedEffect(atom)` 且 `LinkedTrigger` 是 repeated | 拒绝 |
| 3 | `IsStandaloneEventDependencyPrefix(atom)` | 永不绑定 |
| 4 | `last.Template == "D:ShuffleAllUnexhaustedIntoDraw"` | 继承 last 的 link |
| 5 | `TriggerNeedsLinkedEffect(last)` | **必然绑定** (除玩家选择冲突) |
| 6 | `IsDependencyPrefix(last)` | 传播 last 的 link |
| 7 | `atom.Scope ∈ {3,4,5}` | **永不绑定** ← 契约条件 5 |
| 8 | `last.TriggerIndex < 0` | 无 link 可挂 |
| 9 | 同 triggerIndex 已被 ≥2 个操作占用 | 拒绝 |
| 10 | `IsDoubleTargetVulnerable && IsFatalCondition(trigger)` | 拒绝 |
| 11 | `N:RetaliateDamage` 且 trigger.Kind ≠ `attack_received` | 拒绝 |
| 12 | `RequiresPlayerChoice(atom)` 且 trigger 不支持 | 拒绝 |
| 13 | `DifficultConditionTier(trigger) >= 2` | **必然绑定** |
| 14 | `atom.Template != "I:UpgradeThatCard"` 且 `_random.Next(2) != 0` | **约 50%** ← 契约条件 4 |

`EffectBalanceModel.LinkedTrigger` (一跳解析) 照实实现。

### 1.2 谓词与模板集合

`GenerationRules.cs` 中照实复刻的集合 (内容逐条对齐):

- `DependencyPayoffs` 24 条 / `GenericDependencyPrefixes` 19 条 /
  `MultiplicativeDependencyPrefixes` (19 减去 3 条非乘法) / `DependencyOnlyPayoffs` 23 条 /
  `StandaloneEventDependencyPrefixes` 7 条
- `TriggerSupportsChoiceContext` 的 10 个模板 + 5 个 Trigger.Kind 黑名单
- `RequiresPlayerChoice` 的 24 模板白名单 + 6 个 `ProxyAtomic_*` + CardReference 判定
- `IsSelfManagedStateEffect` 6 条 / `IsSelfCostReduction` 7 条 + 2 个 trigger kind /
  `IsExtremeLifecycleDownside` 3 条 / `IsRestrictedEffect` 9 条模板
- `CardUniqueEffectKey` 的 21 条模板映射 + 8 个 `combat_rule` variant
- `SuppliesReferencedCardTriggerKinds` 27 条 / `TriggerSuppliesEnemyTarget` 7 条 +
  `SuppliesEventAttackTriggerKinds` 6 条
- `IsRepeatableDependencyModifier` 的 opcode/variant 分支 + 9 条模板
- `DifficultConditionTier` 的完整分支表 (**分支顺序也一致**: 高费阈值特判 → tier4 →
  tier3 模板 → tier3 flag → tier2 模板 → tier2 flag → tier1 → 0)
- `CanSupplySpecificTriggerPayload` 的 6 个显式 case + 4 个 opcode/variant 分支
- `IsFailableOneShotCondition` 17 条模板

### 1.3 派生统计 `GenerationPool`

原版 `ImmutableComponentCatalog` 构造 (:32-66) 的三个量, 全部从我们的配方直接派生:
`ComponentCounts` / `ComponentCountCounts` / `MinimumEffectCountsByRarity`。
`AdaptiveEffectCountWindow` 的循环语义照实 (最小值先向最大值靠拢, 相遇后两者一起涨, 上限 8)。

### 1.4 每槽过滤 `CompatibilityFilter`

`IsCompatible` 的 30 条判定 + `CanCompleteFinalPlannedSlot` 的形状意图。
原版是嵌套 if/goto, 但每条都是"拒绝", 所以扁平化为合取不改变结果。
每条规则有名字, 探针可报告是哪条命中。

**关键形状保证**: 两个触发不连排 (`trigger-stacks-on-trigger`)、
AbilityRule 不接在触发后 (`rule-stacks-on-trigger`)、
非 Power 壳不收 AbilityTrigger/AbilityRule (`trigger-scope-on-non-power`)。

### 1.5 确定性

- 种子材料: `{ModId}/{Version}/{character}/{seed}` (`GenerationSeed`)
- PRNG: **自写 splitmix64**, 不用 `System.Random`
  —— 后者的序列官方声明"跨 .NET 版本不保证稳定", 而卡片身份要跨玩家一致。
  自写后确定性是本仓库的性质, 不依赖运行时。
- 拒绝采样取模 (无偏), 池子规模变化不会静默改变分布。

---

## 2. 近似替代 (APPROXIMATED)

输入数据不在复用的两个 JSON 里, 用可用信号替代。**全部偏保守**: 可能误拒合法卡,
不会放过非法卡 (唯二例外在 2.4 标注)。

| 谓词 | 原版依据 | 我们的替代 | 影响 |
| --- | --- | --- | --- |
| `IsNegativeEffect` | `ComponentValuationApi.IsNegative` + `IsIntrinsicNegative` + `DerivativeSlotCatalog` | opcode ∈ {lose_hp, discard_card, end_turn, restrict_block_from_cards} / 模板名单 / 槽位值为负 | 只会少判"负面" |
| `IsBeneficialEffect` | `ComponentValuationApi` 收益表 | `!IsNegativeEffect` | 只会少判"增益" |
| `IsEnemyDamage` | `api_enemy_damage` flag | 模板前缀 (`T:D*`/`N:AllD*`/…) + opcode | 池中该 flag 只出现 1 次 |
| `HasAttackClassifyingDamage` | 同上 + 伤害类型抑制条件 | `IsEnemyDamage` | 近似 |
| `HasSameXEffectKind` | 编译期 X 效果键 | X 需求类别 + opcode | 偶尔误拒第二个 X 效果 |
| `IsRepeatedTriggerOrCondition` | `EffectBalanceModel.HasRepeatedOrMultiplicativePayoff` | 池中 `repeated_or_multiplicative` flag (85 条) | 同一 spec 同一结果 |
| `FieldOccurrenceCount` | `StructuralFieldKey` = `Family(Template)+KeyShape+SlotShape` | `Family(Template)+opcode+variant+target+zones+filter+槽位 id` | 结构等价, 实现不同 |
| `CanCompleteFinalPlannedSlot` 的"收益"项 | `IsBeneficialEffect` | `!IsNegativeEffect` | 近似 |

### 2.1 池中不存在的 flag (0 次出现, 已核实)

`api_restricted` / `api_negative` / `standalone_keyword` / `api_power_foundation` /
`referenced_non_attack_exhaust` —— 全部 **0 次出现**。
所以依赖它们的判定退化为纯模板/opcode 判定:

- `IsRestrictedEffect` → 9 条模板 + Heal/GainMaxHp opcode。**够用**: 池里的
  `N:Heal` / `I:GainMaxHp` 都被模板名单覆盖。
- `IsStandaloneKeywordOperation` → **恒 false** (规则保留但不会触发)。
- `IsPersistentPowerFoundation` → 退化为 `scope ∈ {AbilityTrigger, AbilityRule}`。
  原版还 OR 上 `IsPersistentStat` (N:Dex/N:Thorns/N:Intangible…) 与
  `IsCopyThisCardToDiscard`。**后果**: Power 壳的首槽必须是触发/规则片段。
  这是原版的主流情形, 不会产出不连贯的卡, 只是排除了几个我们识别不出的
  power-foundation 原子。

### 2.2 玩家选择判定

`RequiresPlayerChoice` 三个来源里, `CardReference ∈ {HandCard, HandAttack}` 与
`requires_player_choice` flag (41 条) 都在数据里, 所以这一条**接近照实**。

### 2.3 未实现的一致性规则

原版 `HasValidOperationAssembly` 是 33 项合取。其中 28 项已被每槽规则覆盖 (无需重复),
5 项依赖平衡模型未实现:
`HasNoRepeatedTriggeredCombatDamageGrowth` / `HasNoRepeatedTriggeredNextTurnAttackDouble` /
`HasNoTurnEndDrawOrResourcePayoffs` / `HasNoTurnEndTurnLocalPayoffs` /
`HasValidRepeatDamageAssembly` / `HasValidNextAttackGrantAssembly` /
`HasValidShuffleThenDrawAssembly` / `HasValidPreventDrawOrdering` /
`HasValidExhaustAllHandOrdering` / `HasValidGrandFinaleAssembly`。
已实现的是能在"每槽都通过之后仍然失败"的那一项:
`HasValidFailableConditionAssembly`。

### 2.4 已知会"放过"而非"误拒"的两处

- `IsNegativeEffect` 漏判时, `FinalSlotRejectReason` 的
  `paid-shell-without-benefit` 可能放过一张全负面但付费的卡。
- `IsBeneficialEffect` 用 `!IsNegativeEffect` 代理时同理。

两处都只在"全部操作都是负面"时才可能发生, 概率低但非零。

---

## 3. 省略 (OMITTED)

### 3.1 权重 (用户裁定: 纯均匀)

原版 `PickForRarity` 是两段式: 先按家族 (`NumericTextSchema.Family`) 加权抽家族,
再在家族内按变体加权抽。权重来源:

- `NativeComponentFrequencyTracker.SourcePriorWeight/VariantWeight/SelectionWeight`
- `EffectSelectionTuning.ApplyAtomAdjustments` (含 14 个 `PercentWeight` 修正项)
- `ResourceEconomyModel.NegativeFamilyWeight`
- `FinaleVariantWeight` (万倍权重)
- `AggressiveModeTuning.EnergyGainSelectionWeight`

**全部省略**, 改为在兼容候选上均匀抽样。

`PickComponentCount` 同理: 原版按 `(稀有度, 费用, 类型, 槽位数)` 加权,
我们改为在池子的 `ComponentCounts` 上均匀。

**恢复成本低**: `PickComponentCount` 需要的权重项
(`Recipes.Count(rarity && count)` ×16 + `ComponentCountCounts[count]`) 已经在
`GenerationPool` 里算好了, 且 4 个 rank 权重函数 (`ComponentCountRarityWeight` /
`ComponentCountCostWeight` / `ComponentCountTopRarityNonPowerWeight` /
`ComponentCountPowerWeight`) 是纯常量表, 可以直接抄。约 1 个函数的工作量。

### 3.2 数值平衡 (用户裁定: 沿用)

原版 `InstantiateNumericSlotsStructured` (:3579) 从
`EffectBalanceModel` (3864 行) / `NumericGenerationTuning` (894 行) /
`PercentageValueTuning` 重新推导数值。**这三张表不在数据里**。
我们取原版自己的 `OriginalValueChance` 分支的极限: 永远沿用原值。

原版本身也有一条"按稀有度/费用/槽位数调整数值"的路径
(`RaiseEmergencyFallbackToRarityFloor`, `ApplyOrbBudgetCompensation`,
`NegativeEffectCompensationPercent`) —— 全部省略。

**恢复成本高**: 需要从零建立平衡表, 或反向从 481 张原卡的数值拟合。
不建议恢复; 若要做, 应该是"另写一张我们自己的缩放表", 而不是复原原版。

### 3.3 卡牌验收与去重循环

原版 `TryFinalizeUniqueCard` 会拒绝与已生成卡重复的卡 (名字 / 效果签名 / 池唯一组件),
并把 `duplicateFailures` 回灌给 `AdaptiveEffectCountWindow`。
**未实现**: 因此 `duplicateFailures` 恒为 0, 自适应窗口恒为 `(1, 5)`。

### 3.4 其余

- `CardSlotContext` 的卡牌引用槽 (`N_SELECT_HAND_CARD` 注入) —— 未实现。
  数据里 `CardReference` 只有 `None`(918) / `ThisCard`(5) / `HandCard`(8),
  影响面小。
- `CardNameGenerator` (44 KB, 自有 SHA256 流) —— 未实现, 卡名由壳决定。
- `SampleTags` 的标签重采样 —— 未实现, 标签直接继承壳。
- `SampleCost` 的费用重采样 —— 未实现, 费用直接继承壳。
- 悔恨式额外池 / 诅咒衍生槽 —— 阶段 F/G。
- 卡牌文本渲染 —— 阶段 C 之后。

---

## 4. 实测后果 (阶段 B 探针, 250 种子 × 5 稀有度 × 6 角色 = 7500 张)

| 指标 | 值 |
| --- | --- |
| 装配成功率 | **7500/7500 = 100%** |
| 槽位数分布 | 1→2290 / 2→2088 / 3→2045 / 4→1077 |
| 输出覆盖 opcode | 33 / 33 |
| 生成的 (trigger→payoff) 链 | 1316 条, 其中 **1298 条 (98.6%) 在原版任何卡里都不存在** |
| 硬币绑定率 | 297/605 = **49.1%** (逐角色 42.0%–53.0%) |
| 触发被再绑定次数 | **0** (规则实际命中 368 次, 非空转) |
| 全量生成摘要 | `3735263FE51D63A183BAF6BAEA6B16B7F2A216BA8EB337ED134E144A2CC73F9C` |
| 跨进程一致性 | 两次独立进程输出逐字节相同 |

"98.6% 的链在原版不存在"是契约条件 2 的直接证据: 生成器没有把原搭档效果带过来。

`difficult` 计数为 0 —— 规则 13 (难条件必定绑定) 在 7500 张里一次都没触发。
原因是规则 5 先一步接管了"触发后紧跟的效果"这一情形, 规则 13 只在**同一个难触发
被绑定第二个效果**时才生效。不是缺陷, 但说明这条规则目前是冷路径。

---

## 5. 一处容易误读的原版语义 (记录以免将来踩坑)

`RarityEffectCountMinimum` (该稀有度配方的原子数最小值) 看起来很像是"生成卡的
槽位数下限"。**核实后不是**: 原版只在 `raiseAggressiveEffectFloor` 为真时消费它
(`ComponentAssemblyGenerator.cs:231-235`), 而该标志来自 `AggressiveModeTuning`,
默认 `balancedValues = true` 时为假 —— 也就是说默认路径**从不应用稀有度下限**。

我们仍然计算它 (它是 `ImmutableComponentCatalog` 派生集的一部分, 探针在断言),
但生成器不拿它当闸门。

同一段还有第二个陷阱: `AdaptiveEffectCountWindow` 的 `duplicateFailures` 来自
**卡牌验收去重循环**, 而我们没实现那个循环 (§3.3), 所以该参数恒为 0,
窗口恒为 `(1, 5)`。如果将来实现验收循环, 这两处会同时活过来。
