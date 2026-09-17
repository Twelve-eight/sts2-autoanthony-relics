## WS-0916-05 - 2026-09-16 - 生成期执行上下文合法性 (SeedVersion v4)

### 问题

`RelicGenerator` 只排除两种组合(金币自递归, `gain_max_hp` 非 obtained), 而执行器
`AnthonyRelicModel.ExecuteEffectsAsync` 还会跳过两类效果: 无战斗上下文时的战斗域 opcode,
以及敌人全死后 `all_enemies` 解析为空列表. 于是生成器能产出文字承诺了执行器不会跑的效果的
遗物. 旧样本 "1200 次里 34 次" 是 v3 之前的数据, 不作为当前比例.

### 修复

- `EffectFragment.CombatScopedOpcodes` 成为唯一定义, 执行器改为引用它(原来是执行器私有副本),
  消除两边漂移.
- `RelicGenerator.Excluded` 增加规则 3(被动槽只承载 `modify_hand_draw`)与规则 4(战斗域 opcode
  不配无战斗上下文的 trigger; `all_enemies` 效果不配敌人全死后的 trigger).
- 被动回退路径 `pool.PassiveEffects[random.Next(count)]` 过去完全绕过合法性, 可采到执行器不跑的
  片段; 改为 `PickEligiblePassive`, 无可选被动时改生成触发式遗物, 绝不采不合法片段.
- `SeedVersion` v3 -> v4: 合法性规则变了, 旧档必须重新生成, 而不是继续使用执行器会跳过的遗物.

### 依据(反编译 + 执行器源码, 无实机)

- `PlayerCombatState` 全程序集仅 `Player.ResetCombatState()` 赋值一次且再不置 null -> 为空等价于
  "尚未开战"; 池中只有 `obtained`/`gold_gained` 能在该窗口触发. `room_entered` 被执行器限定
  `CombatRoom`, 且 `SetUpCombat`(含 `ResetCombatState`)先于 `Hook.AfterRoomEntered` 执行.
- `combat_end`/`combat_victory` 只在 `IsCombatEnding` 为真(无存活主敌)后触发; `all_enemies` 解析为
  `Enemies.Where(e => e.IsHittable)`, 死亡单位不可命中.
- 执行器被动钩子只执行 `modify_hand_draw`.

### 验证

`tools/relic-eligibility-probe`(引擎外, 直接引用构建产物):

- 规则覆盖(探针 `dotnet run` 实际输出, 逐字): 池 15 trigger 片段 / 12 distinct Kind; 31 triggered effect
  片段 / 13 opcode / 21 shape; 2 passive; 战斗域 shape 8; `all_enemies` shape 2.
  Kinds x opcodes 12x13=156; Kinds x shapes 12x21=252; trigger keys x shapes 15x21=315.
  规则 (a) 排除 2x8=16; 规则 (b) 排除 2x2=4(4 条不被 (a) 覆盖); context 合计 20 of 252;
  全部排除 71 of 252(Kind x shape)/ 26 of 156(Kind x opcode). 以 shape 计数为准(opcode 粒度不足以
  表达 `apply_power` 的 all_enemies/self 变体区别).
- 200-seed 扫描: 每个 effect 与 trigger 片段仍可达; 同 seed 逐字节一致.
- **实机核对(2026-09-17, `I:\Slay the Spire 2`(`I:` 已于 2026-09-17 迁至 `E:\Slay the Spire 2`, 见 AGENTS.md Sec 2b) Goldberg 副本, 0.1.6)**: 启动行 `15 triggers,
  31 triggered effects, 2 passives` 与探针逐字一致; 真实对局中遗物生成走 `obtained` 路径无异常.
- 初版提交写的 132/121/18/103 是错的: 用了过期的 `research/relic_atom_ledger.json`(25 supported)
  而非运行时 `mod/Code/Data/Json/relic_atom_ledger.json`(36 supported), 且漏算 `EffectFragment.Key`
  的 Values 分量. 已按探针与实机日志更正.
- 每个 effect 片段仍至少有 1 个合法 trigger; 200 seed 扫描下每个 effect 与 trigger 片段都可达.
- 同 seed 生成结果指纹稳定.
- 明确声明: 上述规则由反编译与执行器源码推导, **没有实机验证**.

未做: 未启动游戏, 未部署.

# DEVLOG - AutoAnthonyRelics (真正的东尼算法-遗物)

过程记录。聊天只报方向 / 决断 / 问题 (D6)。

---

## Session 1 - 2026-09-12 - 立项 + 阶段 A (骨架 + 数据层)

### 立项依据

- `HANDOFF-2026-09-12-PT2.md` 第 4 步: 按契约实现"真正的东尼算法-遗物"新 mod。
- `HANDOFF-2026-09-12.md` 0.2 原文: "用户要求**思想重写**一个新 mod, 忠于原版东尼算法
  (AutoAnthony) 的机制。" —— 这一句否决了"移植反编译代码"的路线。
- 用户裁定: mod id = **`AutoAnthonyRelics`**; 数据来源 **向效率看齐**
  (= 复用原版的生成池, 不自己重造 931 个原子)。
- 旧名 `东尼算法 - 遗物` 已由 D1 从 QuriousCraftingRelics 腾出, 本 mod 接用。

### 决断

1. **仓库位置**: 新建 `G:/omp works/sts2-autoanthony-relics/`, 沿用
   `sts2-mpconfigsync` / `sts2-perfect` / `sts2-spire1` 的兄弟仓库惯例。
   Qurious 仓库 (`G:/omp works/AutoAnthonyRelics/`) 根目录按用户裁定保持不动。
2. **代码不移植**: 原版反编译共 898 文件 / 67679 行, 其中 `ChaosOperationExecutor.cs`
   5373 行、`ComponentAssemblyGenerator.cs` 4795 行、`CardEffectRules.cs` 4799 行。
   按"思想重写"只读其语义, 不搬代码。
3. **数据以嵌入资源打进 dll, 不用 `res://`**: 隔离探针必须能在不触碰 Godot 的
   前提下读池子。Godot 静态构造在引擎外是原生访问违例 (`0xC0000005`),
   try/catch 接不住 (Qurious 仓库 Session 45 的 cfg 迁移事故)。顺带把 pck 打包
   路径从数据层移除。
4. **枚举解析全函数**: 未知值降级到 `Unknown` 而不抛异常。数据升级不得让 mod
   初始化或某一局崩掉。
5. **manifest description 必须含署名**: 数据源自 Alriph 的 Auto-Anthonyology,
   代码为独立重写。这是硬要求, 不是可选美化。

### 规模实测 (本次盘点, 不是估计)

`G:/omp works/AutoAnthonyRelics/.tmp/step4/inventory{1..7}.py` 逐项量出:

- 481 配方 / 931 原子实例 / 931 RuntimeSpec, **1:1 且零孤儿** (931/931 join)。
- 角色配方数: Silent 86 / Defect 86 / Necrobinder 86 / Regent 86 / Ironclad 85 /
  Colorless 52。
- **生成器实际迭代的是按角色分的片段池**: Regent 178 / Necrobinder 176 /
  Ironclad 171 / Defect 170 / Silent 149 / Colorless 87 (合计 931)。
  配方数相近但片段池差了 29 —— 每条配方的原子数不均匀。
- Scope: NonTargeted 426 / SingleEnemyOnly 205 / Independent 84 / AbilityTrigger 77 /
  Modifier 60 / ConditionalTrigger 58 / AbilityRule 21。
- **解释器的真实实现面: 33 opcode / 291 variant / 306 (opcode,variant) 组合。**
  其中要手写的是 **201 个 template variant** (`template_self_action` 72 /
  `template_independent_action` 70 / `template_modifier` 42 /
  `template_target_action` 17), 里面 25 个 `*_proxyatomic_*` 是"转交给引擎原生卡",
  实现成本≈查表; 其余 ~176 个是角色专属的具体效果。
- 辅助维度: 86 flag / 18 condition kind (18 完整 spec) / **55 trigger kind 但
  57 完整 trigger spec** (= 57 个 `(Kind, Lifetime)` 对) / 6 lifetime 取值 /
  15 target / 7 source zone / 4 destination zone / 5 card filter。
- 按契约 §9 条件 1 的分法: **效果侧 775 / 触发侧 156**, 触发侧 90 个不同模板。
  ⇒ 生成器仅凭片段自身的 scope 就能判定"这是触发, 不许再被绑到另一个触发上"
  (条件 5), 不需要额外查表。
- 数据能自证到哪一步: `native_reference_components.json` 的 451 条里, 56 条是
  `RuntimeContract: {Kind: ...}` 形态的组件契约 (带 `Parameters` + `ExampleText`),
  另 395 条与 runtime spec 同形。**它不给出 201 个 template variant 的可执行语义**
  —— 那部分只在原版代码里。所以解释器必须自写, 不能靠数据驱动。

### 阶段 A 实施 (完成)

- 建 `G:/omp works/sts2-autoanthony-relics/`: `mod/AutoAnthonyRelics.csproj`
  (Godot.NET.Sdk 4.5.1 / net9.0 / Publicize sts2 / BaseLib 3.4.5 / PckPacker)、
  `project.godot`、`AutoAnthonyRelics.json` (manifest v0.1.0, 含署名)、
  `Code/MainFile.cs`、`Code/AutoAnthonyRelicsConfig.cs`、
  `Code/Data/{AnthonyModels,AnthonyEnums,AnthonyCatalog}.cs`、
  `localization/{zhs,eng}/settings_ui.json`。
- 两个 generation-eligible JSON 以 `EmbeddedResource` 打进 dll
  (512500 / 618101 字节)。另两个 reference JSON 放 `research/` 下只作对照。
- `AnthonyCatalog`: 单例加载 + `PoolFor(character)` + `Stats()` + `Validate()`。
  `Validate()` 检查孤儿片段 / 未被引用的 spec / 未知 scope / 未知 opcode / 空池 /
  无原子配方; 非空即拒绝生成而不是产出半坏卡。
- `MainFile.Initialize` 在启动时就加载并校验池子 —— 把"嵌入坏了"或"数据升级带了
  新 opcode"变成一行启动日志, 而不是某一局第一张卡的崩溃。Harmony 补丁用逐类型
  try/catch 隔离 (Spire1 模式), 一类失败不拖垮其余。

### 验收 (探针, 已通过)

`G:/omp works/.tmp/aar-step4-probe/` **62/62 断言全绿**, 与独立 Python 实现逐项交叉
核对 (不是自我确认): 481/931/931、6 角色分布、7 scope 逐项、33 opcode 逐项实例数、
291 variant / 306 组合 / 86 flag / 18 condition / 55+57 trigger / 6 lifetime /
15 target / 7+4 zone / 5 filter、Values 长度分布 {0:254, 1:607, 2:70}、
各角色池大小之和 = 931、效果侧+触发侧 = 931、池子摘要 SHA256 进程内稳定
(`78F50545917EE2651AA147BE3613310046A64BD70E29C10E2760821547CBF201`)。

构建 `0 警告 / 0 错误` + `PCK packed`。

### 过程中修正的自己的错误

原稿在计划文档里写"57 个 trigger", 探针按 `Kind` 去重只得到 55, 一度以为是缺陷。
核实后确认两个数都对, 含义不同: 57 是完整 trigger 对象 (= `(Kind, Lifetime)` 对)
去重, 55 是 `Kind` 去重。**解释器必须按完整 spec 绑定**, 只按 Kind 会丢 2 个区分。
已在计划文档 §1 与探针注释里写明两个口径。

另一处: `EnumParsing.Zone(...)` 方法与 `Zone` 枚举同名, 在字典初始化器里
`Zone.None` 解析到方法, 8 个 `CS0119`。已把五个解析方法统一改名为
`ParseScope` / `ParseOpcode` / `ParseTarget` / `ParseZone` / `ParseCardFilter`。

### 下一步

阶段 B (生成器核心): 逐槽采样 + `IsCompatible` 过滤 + `PickForRarity` 加权 +
数值槽实例化 + `LinkedTriggerIndex` 绑定 (含普通触发约 50% 概率) + SHA256 种子派生。
验收对应契约 §9 条件 1-5 + 7, 全部可由探针判定, 不需要解释器。

---

## Session 2 - 阶段 B: 生成器核心 (条件重组机制本体)

**方向**: 第 4 步 (新 mod `AutoAnthonyRelics`) / 阶段 B —— 生成器核心。

### 先落两个口径 (用户裁定, 会进卡片指纹)

| 项 | 裁定 | 理由 |
| --- | --- | --- |
| 数值槽 | **沿用原配方 `BaseValue + Offset`** | 原版平衡表 (`EffectBalanceModel` 3864 行 / `NumericGenerationTuning` 894 行 / `PercentageValueTuning`) **不在复用的数据里**, 无法推导; 原版本身也有 `OriginalValueChance` 分支沿用原值, 我们取该分支的极限 |
| 候选权重 | **纯均匀随机** | 原版 `PickForRarity` 的两段式家族加权 + 14 个 `PercentWeight` 修正项全部省略 |

两条都写进卡片指纹 ⇒ 改动等于全仓库重来 (存档 / 联机握手 / 可复现性)。已同步
`research/step4-implementation-plan.md` §0 与 `research/fidelity-ledger.md` §0。

### 实现 (`mod/Code/Generation/`, 6 个文件)

`GenerationTypes` / `GenerationPool` / `GenerationRules` / `TriggerBinder` /
`CompatibilityFilter` / `CardAssembler`。职责与逐条对齐情况见
`research/fidelity-ledger.md` 与计划文档阶段 B 小节。

三处值得单独记的设计决定:

1. **PRNG 自写, 不用 `System.Random`**。后者的序列官方声明跨 .NET 版本不保证稳定,
   而卡片身份要跨玩家一致。改为 splitmix64 (64 位状态, SHA256 前 8 字节做种子),
   确定性成为本仓库的性质而非运行时的性质。
2. **`InternalsVisibleTo("Step4Probe")`**。一致性谓词 (触发不可再绑定、依赖前缀合法性表)
   是 internal (运行期解释器同程序集, 没有理由公开); 让探针看见比在测试里重抄一遍
   规则表更诚实, 也更不容易漂移。
3. **`BindingStats` 决策计数**。契约条件 4 说"普通条件约 50% 绑定"。不计数就只能从
   卡片输出反推, 那是间接的。现在 `CoinFlipBinds/(CoinFlipBinds+CoinFlipRejects)`
   就是那个统计量, `TriggerSelfRejects` 同时证明条件 5 的规则真的命中了而非空转。

### 验收 (探针, 全部通过)

`tools/step4-probe/` (已从 `.tmp/` 纳入仓库), 250 种子 × 5 稀有度 × 6 角色 = 7500 张:

| 契约条件 | 实测 |
| --- | --- |
| 1 片段携带完整结构化载荷 | 16909 个操作全部有 target; 1213 个带 trigger spec; 30 个带 condition spec |
| 2 同池独立抽样 | 生成链 1316 条, 其中 **1298 条 (98.6%) 在原版任何卡里都不存在** |
| 3 绑定是生成期步骤 | `operation[0]` 恒为 -1; 全部 triggerIndex 指向更早的操作且目标是触发/依赖前缀 |
| 4 普通条件绑定率 ≈50% | **297/605 = 49.1%** (逐角色 42.0%–53.0%) |
| 5 触发从不被再绑定 | 0 次; 规则实际命中 368 次 (非空转) |
| 7 确定性 | 同 (角色,种子) 指纹一致; 换版本串/换种子/换角色都改变指纹; 全量摘要两次独立进程**逐字节相同** |

装配成功率 **7500/7500 = 100%**; 槽位数分布 1→2290 / 2→2088 / 3→2045 / 4→1077;
输出覆盖 33/33 opcode。全量生成摘要 `3735263FE51D63A183BAF6BAEA6B16B7F2A216BA8EB337ED134E144A2CC73F9C`。

附带提前验证契约条件 6 的可达性: Ironclad 扫 10000 张,
`owner_hp_lost_during_turn -> lose_hp` 命中 23 次。该组合被池子强约束 ——
该 trigger kind **只存在于 Ironclad** (2 个原子), 且是 `AbilityTrigger` scope,
而非 Power 壳一律拒收 `AbilityTrigger` ⇒ 只有 Ironclad 的 Power 壳能承载它。
样例卡已打印 (Corruption 壳, 4 槽)。阶段 D 仍负责做成正式验收。

### 过程中修正的自己的两处错误

1. **`AdaptiveEffectCountWindow` 的语义我一开始理解错了**。我按"每次重复失败窗口
   上下界各 +1"写了断言, 实测 `(1,5) -> (2,5)`。回去读原版循环:
   **最小值先向最大值靠拢, 两者相遇后一起涨, 上限 8**。是我的断言错, 不是实现错。
   已把断言改成覆盖 4 个阶段: `(0)->1..5`、`(3)->2..5`、`(12)->5..5`、
   `(15)->6..6`、`(60)->8..8`。
2. **`RarityEffectCountMinimum` 是冷路径**。我原以为它是"槽位数下限"。核实原版:
   它只在 `raiseAggressiveEffectFloor` 为真时被消费, 而该标志默认 (`balancedValues
   = true`) 为假 ⇒ **默认路径从不应用稀有度下限**。仍然计算它 (它是
   `ImmutableComponentCatalog` 派生集的一部分, 探针在断言), 但不拿它当闸门。
   同段还有第二个陷阱: `duplicateFailures` 来自**卡牌验收去重循环**, 而我们没实现
   那个循环 ⇒ 该参数恒为 0, 窗口恒 `(1,5)`。将来实现验收循环时两处会同时活过来。

### 探针里刻意保留的两个"不完美"

- 通用扫描 (每角色 250 种子) 里 `owner_hp_lost_during_turn -> lose_hp` 命中 0 次。
  这不是缺陷而是概率: 该组合需要 (Ironclad × Power 壳 × 触发落在非末槽 × 下一槽抽中
  8 个 `N:HP-` 之一), 期望约 0.3 次 / 300 张。所以 B.10 改成**定向扫描**并把期望值
  写在注释里, 而不是放宽断言掩盖它。
- `difficult` (规则 13) 在 7500 张里一次都没触发。原因是规则 5 先接管了"触发后紧跟
  的效果", 规则 13 只在同一难触发被绑定**第二个**效果时生效。冷路径, 非缺陷。

### 构建与状态

`0 警告 / 0 错误` + `PCK packed`。构建全程 `-p:CopyToModsFolderOnBuild=false`,
未触碰实机 `mods/`。

### 下一步

阶段 C (解释器)。建议按计划的分片顺序, 先做片 1 (`deal_damage` / `gain_block` /
`apply_power` / `draw_cards` / `gain_energy` / `gain_stars` / `lose_hp`) 与片 2
(`trigger` + `condition`), 因为做完这两片就能做阶段 D (契约条件 6 的正式验收)。

---

## Session 3 - 2026-09-12 - 阶段 C 片 1+2

**方向**: 第 4 步 (新 mod `AutoAnthonyRelics`) / 阶段 C —— 解释器, 片 1 (8 个效果 opcode)
与片 2 (`trigger` + `condition`, 即 `triggerIndex` 的运行期分发)。

### 架构: 纯规划层 + 薄执行层 (硬要求, 不是风格偏好)

隔离探针 (`tools/step4-probe`) 在引擎外运行, 引擎外触碰 Godot 静态是原生访问违例
(`0xC0000005`), try/catch 接不住 (Qurious 仓库 Session 45 的 cfg 迁移事故)。所以解释器
必须切成两半, 让探针能断言"解释器"而不是只能断言"生成器":

| 文件 | 职责 | 触碰引擎? |
| --- | --- | --- |
| `InterpreterTypes.cs` | plan 结果类型 (`OperationPlan` / `CardPlan`) / `InterpreterContext` / 三态 outcome | 仅引擎**纯枚举** (`CardType` / `ValueProp` / `Zone`) |
| `VariantTable.cs` | 306 个 (opcode,variant) 对的显式分类 + 冻结清单 | 否 |
| `OperationPlanner.cs` | 片 1+2 的忠实解析 | 否 |
| `EffectExecutor.cs` | plan -> 引擎命令 | 是 (无 Godot 静态) |

纯层引用引擎纯枚举是刻意的: `DamagePropsForCardEffect` / `BlockPropsForCardEffect` 的
返回值**就是** `CardType` 的函数 (原版 `ChaosOperationExecutor.cs:1463` / `:1478`), 在本地
重声明一份枚举只会制造一个能悄悄漂移的副本。枚举是托管值类型, 无静态构造, 无原生互操作。

### 自己拍的三个决定

1. **分类表用"冻结清单 + 两个显式集合", 不在运行期从 catalog 推导**。
   `VariantTable.PoolPairList` 是 306 条手写字面量; 探针断言它与 catalog 的配对集合
   **set-equal**。这样数据升级会让断言响, 而不是让某个配对的分类悄悄从 Pending 变成别的。
2. **三态不是"完成度", 是三种不同的答案**。`Pending` 只对清单内配对成立; 清单外的配对抛
   `UnclassifiedVariantException`。"还没写"与"不认识"必须分开, 否则表就没有价值。
3. **`apply_power` 的两条路由顺序照抄**: 先 self 路由 (`ChaosOperationExecutor.cs:912` 先调
   `TryExecuteStructuredSelfPower`), 再 common switch。判据也照抄: self 路由要求
   `Target == "self"` 且变体在 `StructuredSelfPowerRoute` 的 16 个里 (`:1279-1302`)。
   所以 `retain_hand_this_turn` (target self 但不在 self 路由表里) 走 common switch, 与
   原版一致。

### 实现面 (实测, 不是估计)

分类: **Implemented 42 / DelegatedToNative 25 / Pending 239 = 306**, 零未知兜底。

- 片 1 (27 对): `deal_damage`(`selected`/`all`/`random`) / `gain_block|immediate` /
  `draw_cards|immediate` / `gain_energy|immediate` / `gain_stars|immediate` /
  `lose_hp|immediate` / `heal|immediate` / `apply_power` 16 个变体。
- 片 2 (17 对): `trigger|event` + 16 个 `condition` 变体。
- Delegated 25 = `template_independent_action` 19 + `combat_rule` 6, 与计划 §1.1 的
  "70 含 19 个 proxyatomic / 21 含 6 个" 逐项吻合。

两个 ValueProp 推导照抄真值表 (4 = Power / 8 = 普通卡伤 / 12 = 触发且非 powered;
block 4 = Power 或触发, 否则 8), `lose_hp` 用原版的硬编码 14 (self, `:1015`) / 6 (其它,
`:1022`)。注意两处调用的是**不同重载**: self 走 6 参 (无 dealer), 非 self 走 7 参
(有 dealer = owner.Creature), 已照抄。

### 过程中修正的自己的错误 (独立交叉核对抓到的)

第一版 `ResolveDisposition` 把 `triggerIndex` 检查放在 `scope == Modifier` **之前**,
于是 5 个"挂在触发宿主上的 Modifier 操作"被判成 `LinkedEffect`。

独立 Python 实现 (同一份 reference JSON + 同一套谓词) 给出 `LinkedEffect 117 / Modifier 60`,
探针给出 `122 / 55`。核实原版: scope 2 在 `Play` 里被直接跳过 (`:117-121`),
Modifier 作用域的联动操作在 `ExecuteTriggered` 里也被跳过 (`:360`) ⇒ **Modifier 永不执行**,
因此不可能是 `LinkedEffect`。修正后两边完全一致 (610/115/117/29/60 = 931)。

这条交叉核对的 5 个数字已写进探针 C.4 作为断言, 并注明来源是**独立实现而非自我确认**
(与阶段 A 的 Python 交叉核对同一做法)。这是本次唯一一处"先写错、后被抓"的地方。

### 验收 (探针 C.0-C.5, 全部通过)

探针 **192 条断言全绿 / 0 FAIL, 退出码 0**; 阶段 A/B 段全部保持通过
(断言总数 186 -> 192, 差额是修正后新增的 6 条 disposition 交叉核对断言, 没有删掉任何断言);
全量生成摘要仍是 `3735263F…CC73F9C` (阶段 C 没有触碰阶段 B 的输出)。

| 断言 | 实测 |
| --- | --- |
| C.0 306 对全部被分类 | 冻结清单与 catalog 配对集合 set-equal; 未知兜底 0; 池外配对抛异常 |
| C.0 分类计数 | implemented 42 / delegated 25 / pending 239 = 306 |
| C.1 ValueProp 真值表 | 7 种 `CardType` × (triggered × powered) × 2 个函数 = **56 个组合全一致** |
| C.2 `lose_hp` | self props **14** / non-self props **6**; 与卡类型无关; 无目标时 `TargetUnavailable=true` |
| C.3 分类 ↔ plan 结果 | 306 对逐一 plan, **零分歧** (42 Resolved / 25 Delegated / 239 Unsupported) |
| C.4 原生参照卡全量往返 | 567 卡 / 1051 组件 / 931 带 spec / 306 对; 往返不一致 **0**; spec 字段不一致 **0**; 缺失池原子 **0** |
| C.4 disposition 分布 | OnPlay 610 / CarriedHost 115 / LinkedEffect 117 / InlineGate 29 / Modifier 60 = 931 |
| C.4 解析分布 | resolved 563 / delegated 25 / pending 343 = 931 |
| C.5 契约条件 6 的链 | 宿主 `CarriedHost` (`owner_hp_lost_during_turn/combat`); 联动效果 `LinkedEffect` -> `LoseHp` self props **14** |

C.4 的一个额外收获: reference JSON 的 931 个 `SemanticId` 与池子的 931 个 spec id
**完全相同**, 所以往返检查能顺带断言"reference spec 与 pool spec 字段逐项一致" —— 两条
数据路径在同一套 opcode/variant 语言下互为佐证。

### 与任务书不符之处 (源码为准)

1. **"任何 `TriggerIndex >= 0` 都是延后执行的联动效果" 不准确**。`Play` 在 `:173` 只在
   (forEach 宿主 | `AbilityTrigger` 宿主 | lingering 宿主 | 条件不成立) 时才 `continue`;
   普通 `ConditionalTrigger` 宿主的联动效果是**在自身槽位内联执行**、由宿主条件门控的。
   所以 disposition 必须按宿主类型二分。C.4 实测 117 个真延后 vs 29 个内联门控可佐证。
2. **25 个 `*_proxyatomic_*` 的界定**: 与数据一致 (19 + 6)。但池里另有 3 个
   `ProxyDamage_Atomic_*` 变体 (`deal_damage|random_star_x_hits` /
   `selected_energy_x_hits` / `selected_energy_x_threshold`), 名字里**不含** "proxyatomic",
   不在 25 里。原版把它们的模板列在 `GeneratedValueProxyTemplates` (`:51`) 却仍走
   `deal_damage` 分派。我们判为 Pending (需要 X 值)。
3. **`condition` 的 18 个变体不全能当门**: 其中 2 个 (`has_frost_orb` /
   `cards_played_this_turn_at_least`) 作用域是 `Modifier`, 原版在
   `DependencyConditionMatches` (`:4614`) 而非 `ConditionMatches` (`:4485`) 里处理, 故判 Pending。
4. `lose_hp` 的 self / non-self 走不同重载 (6 参 vs 7 参) —— 任务书只说了 props 与 dealer,
   重载差异是读源码补上的。

### 仍 Pending 的原因 (逐条, 不是含糊的"待做")

- `apply_power|poison`: 原版结构化分派不处理它 —— 变体长度 6 落在
  `ChaosOperationExecutor.cs:1056-1093` 的 length 开关之外, 由 `N:RandomPoison` 模板处理器
  承担。在结构化路径里实现它才是**不忠实**。
- `apply_power|focus_loss_this_turn`: 原版走 `ChaosTemporaryFocusDownPower` (mod 自建 power)。
  引擎只有 `HyperbeamFocusDownPower`, 且它绑定在具体卡 (`OriginModel => ModelDb.Card<Hyperbeam>()`)。
  需要自建 power, 属片 3。
- `trigger|doom_threshold` (`NCR:ForEachDoomThreshold`) 与 2 个 Modifier 作用域的 `condition`:
  修饰符 / 依赖前缀工作, 片 3。
- 4 个依赖 X 值的 `deal_damage` 变体: 见下。

### 无法从来源确定的事 / 已知缺口

- **X 值解析不了**: 阶段 B 的 `ResolvedValue` 只有 `(Id, Value, Upgradable)`, 不带 `Source`;
  `CardAssembler.ResolveValues` (`mod/Code/Generation/CardAssembler.cs:259-272`) 只带
  `BaseValue + Offset`。池里有 12 个 `energy_x` + 1 个 `star_x` 槽。因此
  `deal_damage|cards_played_combat` / `random_star_x_hits` / `selected_energy_x_hits` /
  `selected_energy_x_threshold` 判为 Pending。要做需先让阶段 B 把 `Source` 带出来 ——
  那会改卡片指纹, 属口径变更, 不擅自做。
- **`vulnerable_double` 的数值只能在执行层算**: 原版读目标当前 Vulnerable 层数再叠一次
  (`:1099-1114`)。planner 给出 `PowerKind = VulnerableDouble` + 目标, 层数由
  `EffectExecutor` 读引擎状态。这是纯层的边界, 不是遗漏。
- **`EffectExecutor` 没有实机验证**, 探针不执行它; 它只保证编译与结构忠实。原版在伤害
  链上还传 `card.Definition.HitFx` (`:1386`), 我们的卡模型要到阶段 D 才有, 故省略并注明
  (纯视觉)。
- **依赖前缀门控 (`DependencyConditionMatches`, `:4614`) 与 forEach 迭代 (`:381`) 是片 3**。
  片 2 里前者退化为"无门控直接 OnPlay", 后者退化为"不执行" —— 是**不跑**而不是**跑错**,
  且两者都不影响片 1+2 的任何断言。

### 构建与状态

`0 警告 / 0 错误` + `PCK packed`。构建全程 `-p:CopyToModsFolderOnBuild=false`,
未触碰实机 `mods/`。探针完整日志存 `tools/step4-probe/probe-run-stage-c.txt`。
未执行任何写操作的 git 命令, 工作树保持脏状态交给主会话复核。

### 下一步

阶段 D (契约条件 6 的正式验收) 现在只差"卡模型 + 渲染文本": 解释器侧已能证明该链解析为
"宿主被承载 + 联动效果自指 `lose_hp` props 14" (C.5)。建议顺序: 先做片 3 的 Ironclad 批
(`combat_rule` 21 + Ironclad 的 template variant) 以便 D 的样例卡有完整可执行语义,
再做阶段 D 的渲染与实机验收。


---

## 2026-09-12 (夜) 遗物垂直切片: 宿主 + 账本 + 生成器 + 部署 (主会话单线)

用户指令: 两个遗物 mod 优先可游玩; subagent 全部停用, 主会话单线推进。

本节对应 astra-advice (2026-09-12) 对本项目的三条 P1 (AAR-1/2/3) 的**垂直切片回应**:
不是全部返工完成, 而是先跑通一条完整遗物路径 (审查建议第三步)。

### 架构 (全部新建, 与卡牌主线并存未删)

- `research/relic_atom_ledger.json`: **deny-by-default 原子账本**。25 条 supported
  (每条带六项对账证据 + 必要的修正: 值/正负号/目标/条件/触发), 26 条 rejected
  (逐条原因: 阈值状态/条件丢失/RNG 目标/古遗物等)。提取器输出未经账本确认不得进池。
- `mod/Code/Data/RelicAtomData.cs`: 原子+账本加载 (嵌入资源, 探针可无 Godot 读取)。
- `mod/Code/Generation/RelicFragments.cs`: 片段拆分——触发片段 (Kind+Condition) 与
  效果片段 (opcode/variant/target/values) **独立成池**; 被动原子的条件挂在效果上。
  Key 含数值 (修复: 曾按形状去重把 4/10/14/18 四种格挡折叠成 1 个)。
- `mod/Code/Generation/RelicGenerator.cs`: 60 槽, **定义 = 纯函数 (modId, 版本串,
  run seed, slot)**, 配置零参与 (结构性修掉 Qurious 的 live-config-进定义键缺陷类);
  splitmix64 每槽独立流; 30% 双效果; 15% 纯被动; 金币递归禁配
  (gold_gained 触发 × gain_gold 效果); 运行内指纹/名字去重。
- `mod/Code/AnthonyRelicRunRegistry.cs` + `Patches/RunSeedCapturePatch.cs`:
  种子捕获 (SetUpNew*/Launch 双点, Qurious 模式) + 按 seed 缓存。
- `mod/Code/Models/AnthonyRelicModel.cs`: 宿主。11 个事件钩子 + ModifyHandDraw,
  全部 owner 自门控; 条件求值全为无状态 (first_turn/turn_equals/hp_below_half/
  hand_empty/no_attack_played_this_turn/card_type_power/enemy_side); 执行器
  12 种效果 (PowerCmd/CreatureCmd/PlayerCmd/CardPileCmd), 未知 opcode/condition
  **抛异常**不静默; 时序契约沿用 Qurious 实机结论 (能量在 AfterSideTurnStart、
  首回合抽牌走 ModifyHandDraw、战前格挡留 BeforeCombatStart)。
- `mod/Code/Models/AnthonyRelicSlots.cs`: 60 个槽位类 (每槽独立 ModelId,
  池/存档/去重都挂在 model id 上)。
- `mod/Code/Patches/AnthonyRelicGrabBagPatch.cs`: RelicGrabBag.Populate 后缀,
  剥离非 CustomRelicModel。**共存谓词 = "是 BaseLib 自定义遗物"**: 与 Qurious
  同装时两个补丁互相保留对方家族, 无顺序依赖 (astra-advice 注 9)。
- 文案: `RelicText.cs` en+zhs 双语 (触发/条件/效果/名库), 按 TranslationServer
  locale 选择; 图标为共享占位 PNG (缺失回退, Qurious 模式)。

### 验证 (组件/集成层, 全部本轮实跑)

- 隔离构建 (ModsPath→sink, 未触实机): `0 警告 / 0 错误` + PCK。
- `tools/relic-probe` **24/24 PASS** (PROBE OK): 账本 25+26 计数; 全片段双语渲染;
  执行器漂移零; BagOfMarbles=全体1易伤 / BigMushroom=-2抽牌 / Vajra=1力量 /
  PotionBelt=2栏位 修正到位; 同 seed 两次生成逐字节一致; 异 seed 不同;
  60 槽形状/名字唯一/三稀有度齐; 金币递归零配对; 跨来源重组占绝对主导。
  样例: "At the end of each combat, draw 1 card." (ChosenCheese 触发 × GamePiece
  效果) —— 触发与效果确实来自不同遗物。

### 未验证边界 (需要实机, 不可由探针替代)

1. Harmony 补丁实机挂载 (grab bag 两个 Populate 重载的 TargetMethod 解析)。
2. 获取→持有→触发→存读档 全流程; 抽卡/事件/商店实际掉落。
3. 联机: 定义纯种子函数 + 引擎种子同步 ⇒ 预期两端一致, 未双端实测。
4. 与 Qurious 同装 (本机当前只装了 Qurious): 需先修 Qurious cfg 迁移
  (它会搬走本 mod 的 AutoAnthonyRelics.cfg), 再双开验证池共存。
5. turn_start 统一映射 AfterSideTurnStart (BloodVial 原 PlayerTurnStartLate):
   单人观测等价, MP 侧时序差异未验证 —— 账本已注明。

### 状态

已部署实机 `mods/AutoAnthonyRelics/` (游戏未运行, 无锁)。卡牌主线代码
(Generation/Card*, Interpretation/*) 保留未删, MainFile 不再加载卡牌 catalog
(审查第二步要求)。git: 本节落盘后提交推送。

---

## 2026-09-13 (凌晨) 实机启动冒烟 + 本地化形状修复

### 发现并修复: settings_ui.json 形状错误 (真机启动阻塞)

- **发现**: 首次真机启动 (steam://rungameid/2868840) 首跑即失败关闭:
  `LocException: Failed to parse ... settings_ui.json` —— 遗留自卡牌方向的本地化文件
  使用了嵌套形状 `{"settings_ui": {...}}`, 引擎语言文件解析器要求**扁平**
  `{"KEY.title": "text"}`; 解析异常在启动路径抛出 → 错误对话框亦失败 → 进程退出。
- **修复**: eng/zhs 两个 settings_ui.json 重写为扁平形状, 键名按 BaseLib 规则
  `{MODID大写}-{SLUG(属性名)}.title / .hover.desc` (对齐 Qurious 的可用样本)。

### 冒烟结果 (修复后真机启动, 全部读自 godot.log)

- 进主菜单成功, **0 个 LocException / startup error**; Time to main menu 19,979ms。
- `[AutoAnthonyRelics] Harmony: 5 patch class(es) applied, 0 failed`;
  `relic pool: 140 extracted atoms, 25 ledger-supported, 26 ledger-rejected;
  fragments: 14 triggers, 20 triggered effects, 2 passives`;
  配置三项默认值正确落盘。
- `[QuriousCraftingRelics] cfg migration skipped: AutoAnthonyRelics.cfg does not
  match the Qurious legacy schema; left untouched for its owner` —— **项 1 修复真机验证通过**。
- `[Perfect] Harmony: 2 patch class(es) applied` —— 逐类安装修复真机验证 (旧版
  单 PatchAll 连坐后池门控根本没装上)。
- `[HeartShake] BeatOfDeath redirect patch applied` —— 命名空间修复真机验证
  (旧版永远 skipped; 真机装有 Act4Heart)。
- `[MpConfigSync] Harmony: 0 method(s) patched across 23 type(s)` + 无报错。
- `[RegentFXFastBoot] ... ALREADY happened this launch ... FIX: move ... ABOVE RegentFX`
  —— LATE-ARMED 状态按设计如实报告 (用户尚未调整模组顺序)。
- `[ChaosBridge] TransformBatchDedup: transform batches are now without replacement.`
- 引擎双源去重按版本工作: 本地 0.x.y 新版本生效, 工坊旧版自动停用 (测试期望行为)。

### 余下人工验证

进入一局: 掉落是否只出生成遗物、拾取→触发→存读档、双装 Qurious 混合池、
控制台 `relic add AUTOANTHONYRELICS-ANTHONY_RELIC005`。

## 2026-09-13 用户反馈轮 2 (东尼遗物侧)

- 悬停浮窗: 遗物 ExtraHoverTips 为其 apply_power 片段 (勇气/力量/荆棘/易伤)
  追加 HoverTipFactory.FromPower 提示。
- 易伤合并: turn_start_early 触发的全体易伤不再每遗物立即施加, 累积进静态合并袋,
  玩家第一回合开始时统一施加一次 (与 Qurious 同款机制, 各自聚合本 mod 遗物;
  跨 mod 合并需第三方协调, 暂不做)。

## 2026-09-13 紧急修复: 浮窗无法关闭 (同 Qurious 根因)

ExtraHoverTips 的 GetMethod(name) 命中 FromPower 双重载抛 AmbiguousMatchException,
OnFocus 枚举中途炸断 → NHoverTipSet 关闭注册缺失 → 详情浮窗滞留。
改为显式泛型重载 (静态缓存)。已构建/部署/staging。

---

## 附: 会话输入与工作顺序 (2026-09-12~13, 全量见 docs/session-log-2026-09-12-13.md)

与本仓库直接相关的用户输入序列:
1. 「东尼算法遗物最先/两个遗物mod尽快可游玩」→ 垂直切片 (账本→生成器→宿主→部署, bc14407)。
2. 「上架工坊的一切工作」→ 新条目 staging 全套 (59429fc 前)。
3. 「设置页应在原版东尼算法同级,不应在baselib里」→ 独立设置页移植 (59429fc)。
4. 「姿态效果应包括回合数(平静T1/愤怒T2/神格T3)」+「附魔两形态并存」→ 回合调度 + 同附魔叠级
   + X_PICKUP_* 三词条 (b902804 前多提交)。
5. 「悬停应显示增减益详细描述」→ ExtraHoverTips (e0ac107)。
6. 「浮窗关不掉了!」→ AmbiguousMatchException 修复 (96dbd92)。
7. 「审查所有构建代码」→ 补 combat_end 钩子 (原缺失, 词条失效) + 冲刷移出条件门 (1bbb148)。
8. GPT6-Astra 二轮 → 合并袋按 owner NetId 分键 + registry 指纹键 (b902804)。
教训要点: L4 反射唯一性 / L7 静态状态生命周期 (combat_end 漏发=词条静默失效) /
L2 用户报告优先。详见 docs/session-log-2026-09-12-13.md 第二节。

## 2026-09-13 紧急: 60 个无效果占位遗物 = 本 mod 关闭态槽位仍可获取

用户只开怪异炼化 (本 mod enabled=False), 仍获得 60 个无效果遗物 —— 即本 mod
60 个槽位: 开关只门控钩子, 池补丁 early-return 不剥离自家, 加上怪异炼化保留
一切自定义遗物 → 槽位可获取但无效果。
修复: 池补丁常驻 (不再 early-return), Keep 契约 = 自家按开关 / 他家按
IsAllowed / 原版按替换开关; IsAllowed 增加片段池空守卫。

## 2026-09-13 紧急修复 II: 商店只有头环 (同 Qurious 根因)

本 mod 补丁在关闭态剥离了全部遗物 (含怪异炼化的开启遗物) → 袋空。重构为
责任域剥离: 只在开关关时剥离自家槽位; 原版只在 (开启且替换开启) 时剥离;
他家遗物交给其 IsAllowed + 引擎原生清理。KeepModdedRelics 配置项移除
(被责任域契约取代)。

## 2026-09-13 深夜 - 文本描述 + 随机外观补齐 (v0.1.1)

### 用户报告
「东尼算法-遗物没有应有的文本描述与随机外观，而这些部分应当向怪异炼化遗物看齐。」

### 根因
- Localization 覆写与 DescriptionEn/Zhs、图标路径覆写**早已存在**，但:
  1. BaseLib ModelLocPatch 只在 ModelDb.Init（启动，seed=null）评估一次 ILocalizationProvider
     → loc 表被通用文本（"Generated Relic"）烤死，局内永不刷新 → 没有文本描述。
  2. mod 里没有任何图片资源（无 images/ 目录）→ PackedIconPath 指向不存在的文件 → 没有外观。

### 修复 (v0.1.1, 对齐 Qurious 模式)
- **Pools/AnthonyRelicRegistry.cs**: 60 个槽位类型的注册表 + ModelDb 规范实例
  （ChaosSharedRelicPool 的 ChaosRelicRegistry 模式移植）。
- **Patches/AnthonyRelicLocUpdater.cs**: seed 捕获时按当前 locale 重写 "relics" loc 表全部
  60 槽 title/description（反射 LocTable._translations，同 BaseLib 机制）；去重键 =
  seed + FragmentPool.Fingerprint（Qurious v0.5.6 教训 L25 直接吸收：键必须覆盖全部输入）。
  RunSeedCapturePatch 两个捕获点接线。pool 未建好（Triggers.Count==0）时跳过。
- **tools/gen_icons.py**: 60 套程序化遗物硬币图标（94x94 normal/outline + 282x282 big），
  金色角相位 hue（+200° 偏移与 Qurious 色系区分），暗石板环+浅盘+n 边形印记（n=3..8 按
  slot 旋转），输出 mod/AutoAnthonyRelics/images/relics/。pck 819KB 已含图标与 loc。

构建 0 错误; 合并延迟部署守护（.tmp/deferred-deploy-combined.ps1）等游戏退出后补发
mods/ 与 workshop/content/ 两处并校验哈希+版本。实机验证待用户: 遗物浮窗应显示本局
生成的双语词条描述, 遗物图标应为逐槽不同的硬币图。

## 2026-09-14 凌晨 - Act4 无法打牌/死亡不结算 根因与修复 (v0.1.2)

### 用户报告
「和mod "ACT 4 heart" 一起启用时，进入act4与精英怪-矛盾 boss-心脏战斗时会出现无法打牌，
死亡后无法正常结算游戏的问题。」

### 证据链 (godot.log 00:05 会话)
- Combat #32 回合循环死亡: `TargetParameterCountException: Parameter count mismatch`
  at `AnthonyRelicModel.FlushEnemyDebuffs ... :line 520` → `AfterSideTurnStart` →
  `Hook.AfterSideTurnStart` → (WatcherHookCompat/RitsuLib 任务桥重放) → `CombatManager.StartTurn`。
  → 敌方回合开始瞬间回合循环死亡, 战斗冻结, "无法打牌"。
- Combat #35 回合循环死亡: `ObjectDisposedException: Godot.TextureRect` at
  `Rewind.Scripts.RewindButton.ApplyButtonTint` — **Rewind mod** 在重开房间时对已释放控件
  上色, 第三方缺陷 (从 #32 卡死后重开战斗的连锁触发)。
- 死亡不结算: 用户放弃卡死局 → `RunManager.AbandonInternal → GuaranteeKillAllPlayers →
  CreatureCmd.Kill → OnEnded → ProgressSaveManager.IncrementEncounterLoss(characterId,
  encounterId)` 收到 null 键 → `ArgumentNullException` 中断结算。**下游连锁**: 正常死亡不走
  这条路; 引擎/Act4Heart 在异常终止局上的结算脆弱点, 非我方代码。

### 根因 (我方, 与 Act4Heart 无对抗关系)
引擎 `PowerCmd.Apply<T>` (IEnumerable<Creature> 重载) 签名 6 参数:
`(PlayerChoiceContext, IEnumerable<Creature>?, decimal, Creature?, CardModel?, bool silent = false)`。
FlushEnemyDebuffs 的 `MethodInfo.Invoke` 只传了 5 参 — **Invoke 不填充可选参数默认值** →
参数计数不匹配硬抛。凡持有开局群体减益 (易伤/虚弱/中毒) 词条的东尼遗物, 第一场战斗敌方
回合开始必炸; Act4 精英"矛盾"/心脏只是这局恰好持有该词条遗物的战斗。Qurious 侧同逻辑
传 6 参 (v0.5.3 debuff 合并时写对), 从未出过此错 — 直接对照修复。

### 修复 (v0.1.2)
1. Invoke 补第 6 参 `false` (显式, 不依赖可选默认)。
2. 四个回合循环 await 钩子 (BeforeCombatStart / BeforeSideTurnStart / AfterSideTurnStart /
   BeforeSideTurnEnd) 全部加 L21 保险丝: try/catch → ERROR 日志 + 吞掉 — 遗物效果缺陷
   从"整场战斗报废"降级为"单次效果缺失+日志证据"。Qurious 0.5.4 已做, AAR 当时被
   "无 UI 路径风险低"为由跳过 — 判断错误, 本缺陷即教训: **保险丝不问路径, 一律加**。

构建 0 错误; deferred-deploy-aar-012.ps1 守护 (游戏运行中), 退出后自动补发 mods/ 与
workshop/content/ 并校验哈希+版本。

### 教训
- L26: `MethodInfo.Invoke` 与 C# 可选参数是两个世界 — Invoke 要求参数个数与定义严格一致,
  可选参数必须显式传。所有反射调用点逐一复核 (AAR 仅此一处多参调用; 155 行单参调用与
  定义一致)。
- L27: L21 保险丝的适用条件是"被引擎回合循环 await", 与是否开 UI 无关。同族代码
  (Qurious/AAR 双实现) 必须同步施策, "另一边没出事"不代表另一边是对的。

## 2026-09-14 - 术语翻译错误复发: vigor→"勇气" (v0.1.3) + 复发原因分析

### 错误
RelicText.cs:93 把 Vigor 写成"勇气"。原版权威文本 (native_reference_cards.json, 写本条时
就在磁盘上): "Gain 4 Vigor." → "获得4点活力。"。**活力**才是正名。

### 这是第二次 (Qurious 改名前犯过一次)
两次错误的直接原因相同: zhs 术语凭记忆/直觉撰写, vigor 的假朋友联想 (courage=勇气) 直达笔端,
从未对照权威转储——尽管正确答案当时就是一次 grep 的距离。

### 为什么会复发 (用户点名要求写入教训)
修掉 Qurious 那次之后, 修复只停留在"症状层", 没有进入"过程层"。三个具体失败:
1. **没有术语表**: Qurious 的纠正没有沉淀为工作区级的 EN↔zhs 对照表; 下一个写文本的会话
   (AAR RelicText.cs) 无处可查, 只能重新"想当然"。
2. **修复后没有跨仓 sweep**: 修一处译名错误后, 一次 `grep -rn 勇气 G:\omp works` 就能在
   AAR 发布前抓到这一处; 这一步当时不存在于任何检查单。
3. **作者路径没变**: 症状修了, 但生成这类文本的流程 (凭记忆写译文) 原样保留——只要流程
   不变, 同类错误必然复发, 复发点只是换了个项目。

### 已落地的 durable 修复 (v0.1.3)
- `docs/terminology-glossary.md`: 工作区术语表 (EN↔zhs + 权威来源优先级 + 维护约定),
  vigor/勇气 已作为警示案例记录在表。
- `AGENTS.md` §5 新增硬规则: zhs 术语先查术语表/权威转储; 修一处译名错误后立即跨全工作区
  grep 该错误译名。
- AAR 0.1.3: vigor→活力; 全部词条译文已逐词对照权威转储复扫 (其余均正确:
  力量/荆棘/易伤/格挡/生命上限/药水栏位/能力牌...)。
- 构建部署: 0 错误, mods/ 与 workshop/content 两处 dll 哈希一致, 版本 0.1.3。

### L28 (正式教训条目)
术语译名的正确性必须由**外部权威源**保证, 不由记忆保证; 一处翻译错误的完整修复 =
(改字符串) + (沉淀进术语表) + (跨仓 grep 同错) + (改变撰写流程), 缺任何一步都只是
延迟复发。

---

## 2026-09-14 astra 第三轮建议处理 (一): vigor "勇气" 探针证据证伪 + 探针加固

astra 第三轮复审落盘 (根 `astra-advice.md` + 各项目 `astra-advice.md` + `astra-advice-evidence/2026-09-14/`)。其 AAR 探针输出 (anthony-relic-probe.txt:51-54) 声称 vigor 仍渲染"勇气"。

**证伪过程** (全部可复核):
1. vigor 修复在提交 538f4b6 (v0.1.3) 里, 全源码树 grep "勇气" = 0。
2. 部署 DLL 字节扫描 (UTF-16): "活力"x1 / "勇气"x0。
3. astra 自己的审查副本 RelicText.cs (mtime 00:19:21) 也是"活力"——副本源码与其探针输出自相矛盾。
4. 根因: `tools/relic-probe/RelicProbe.csproj` HintPath 绑定 gitignored 的 `mod/.godot/mono/temp/bin/Debug/AutoAnthonyRelics.dll` (09-12 23:06 旧构建, "勇气"x1); vigor 修复后只做 Release 构建, Debug 产物滞留; astra 整树拷贝带走它, 探针加载了旧二进制。

**修复** (本提交):
- csproj 改绑 Release 输出 + 事故注释。
- Program.Main 新鲜度守卫: mod DLL 构建时间 < 最新源码 mtime → `FAIL stale binary` + exit 2。
- 复跑 `.tmp/relic-probe-rerun-2026-09-14.txt`: exit 0, PROBE OK, vigor 行 = "获得8点活力。", "勇气" 0 次, 首行 OK binary freshness。
- 附带: `AnthonyRelicRunRegistry.cs` 缓存键字面量含原始 NUL 字节 (byte 1438, 非转义), grep/Read 按二进制拒读; 已改 `"\0"` 转义 (运行时值不变)。Qurious `ChaosRelicRunRegistry.cs` 同病同修。

**回报 astra**: `G:\omp works\astra-advice-response-2026-09-14.md` (撤回请求 + 根因 + 修复 + MegaLabel 日志缺失说明)。

### L29 (教训)
探针引用 gitignored 构建产物 = 证据漂移通道: 二进制不随源码前进, "证据绑定二进制"必须附新鲜度断言 (或改 ProjectReference 强制重建); 审查副本整树拷贝必须排除 bin/obj/.godot, 否则探针测的是仓库里最陈旧的那份产物。静默的旧二进制比没有二进制更糟——它会"复现"已被修复的缺陷并污染审查结论。

---

## 2026-09-14 v0.1.4: astra 第三轮 AAR-4/-5/-6/-7 处置

1. (AAR-7) 新增 `Patches/RunCleanUpPatch` (RunManager.CleanUp postfix) → `AnthonyRelicRunRegistry.ResetForRunEnd()`: 清 CurrentRunSeed + 定义缓存。定义是 (seed, SeedVersion, fingerprint) 的纯函数, 清缓存最多付一次重生成, 不改结果。
2. (AAR-4) 缓存键 = seed + "\0" + RelicGenerator.SeedVersion ("relics-v1") + "\0" + pool.Fingerprint: 算法或账本数据变化时, 同 seed 不再服务旧池。
3. (AAR-6) 契约注释落码: owner 分桶键为 NetId; 0 桶仅在单人局可达 (单人只有一个玩家, 两 owner 不可能共享 0 桶; MP 玩家恒有真实 NetId); StS2 无重叠战斗状态。不为不可达状态加防御 (astra 方法论第六条)。
4. (AAR-5) 采样语义裁决: 池按 Kind|Condition 形状折叠是有意设计 (条件重组是产品核心), 逐原子权重作候选保留; "probe 反映该选择"记为后续项, 本轮未改代码。
5. registry 类注释纠正 ("keyed by seed alone" 是过时描述)。

**验证**: Release 构建 0 警告 0 错误; relic-probe 复跑 PROBE OK (新鲜度守卫通过); 直发部署 0.1.4 至 mods/ + workshop/content, MD5+版本校验 OK。CleanUp/获得/存读档需实机验收。

**vdf 描述更正** (工坊首次上传前): 效果列表 "勇气"→"活力" —— L28 同类错的第三处藏身点, 藏在**工坊发布描述**里, 代码 sweep 抓不到它; 删除已不存在的 KeepModdedRelics 选项行; "与 Qurious 同装=混合池且顺序无关"的失实宣称改为如实描述 (Qurious 池替换只保留其混沌遗物, 双装掉落以 Qurious 为主)。

### L30 (教训)
"修一处译名错误后跨全工作区 grep" 的 "工作区" 必须包含**用户可见发布工件** (workshop_upload.vdf 描述、json 描述字段、pck 内文本), 不只是代码与本地化源。工坊描述是仅次于游戏内文本的术语暴露面, 而且在首次上传后每次改动都要走工坊审核。

## 2026-09-14 astra 第三轮审查交接记录

第三轮证据在 `v0.1.4` 提交前截取. 当时 `relic-probe` 通过 140 atoms, 25 supported, 26 rejected, 60 slots, 同 seed 一致性和 200 seeds soak; 审查仍把 SeedVersion 缺失, CleanUp 和 owner-0 作用域列为待闭合, 并错误记录了过时 Debug DLL 的 `勇气` 样例.

随后当前仓库已出现 `c8aa7ab v0.1.4`: cache key 加入 `RelicGenerator.SeedVersion`, 新增 CleanUp reset, owner-0 可达性契约和发布文本更正. 因此第三轮 `anthony-relic-probe.txt` 的生成通过结果仍可用, 但不能证明 v0.1.4 的新路径. 下一轮必须以 v0.1.4 重建并复跑新鲜度守卫, 再覆盖真实遗物获得, combat, save/load, rejoin, CleanUp 和工坊发布物.

术语恢复: Vigor 的权威译名是 `活力`, 不是 `勇气`. 证据与截点记录在 `G:\\omp works\\astra-advice-evidence\\2026-09-14\\handoff-state.json`.

## 2026-09-14 astra 第四轮代码复审

源码截点 7a0c2de. 隔离 Release 0 warnings/0 errors; fresh relic-probe 27 PASS + PROBE OK, 首行 freshness 通过, 实际加载 DLL 与本轮 build hash 相同. Vigor 当前输出为活力; 撤回第三轮 stale Debug 负面结论. SeedVersion key 和 ResetForRunEnd 已分别 SOURCE/隔离验证, 不再重复列缺失.

P1 AAR-R4-01: 新 CombatScopedOpcodes guard 没有修生成上下文. 20 个固定 seed/1200 槽位有 34 件 obtained 且只有 combat effects; 实际执行器在 owner 无 combat 时记录 skipped 并成功返回. 获得遗物不卡死不等于描述的效果实现. 需要生成期 trigger/effect 上下文兼容性与旧局版本策略, 不能静默重抽或只加 catch.

证据: ../astra-advice-evidence/2026-09-14/round4/binary-boundaries.json, anthony-relic.txt, review-results.json. 实际 generator/执行器已运行, 未跑 RelicCmd.Obtain/UI/战斗. 未改产品源码/部署/实机配置/push.

## 2026-09-15 v0.1.6 下行词条池 (用户指令: 全遗物负面词条入池 + 出现率+40%)

需求: 所有遗物的负面词条加入东尼遗物池, 包括本身不参与再生成的先古(Ancient)/事件(Event)遗物的负面词条; 下行碎片出现概率提高 40%.

全量审计: 300 个引擎遗物源码 grep 下行 API (AddCurseToDeck/LoseMaxHp/LoseGold/自伤), 逐文件手读定案 11 个碎片级负面词条 (13 候选中 5 个为提取器把"随机敌人伤害"误标 lose_hp, 已排除). 4 诅咒 (CursedPearl 贪婪/CallingBell 咒铃/BloodSoakedRose Enthralled/PreservedFog Folly, 均拾取), 2 失生命上限 (LeafyPoultice 12/SereTalon 9, 拾取), 2 失金币 (SealOfGold 3/回合开始带 gold>=3 守卫; SilkenTress 全部金币/拾取), 3 自伤 (FragrantMushroom 15 不可格挡/PrecariousShears 16 可格挡/RoyalPoison 4 不可格挡首回合).

数据: relic_atoms.json 140→146 (6 新原子: 4 诅咒 + SealOfGold#1 + SilkenTress#1); 账本 25→36 supported (11 新, 其中 FragrantMushroom/RoyalPoison fix target self + variant unblockable, PrecariousShears fix amount 2→16), rejected 修正 (CursedPearl#* 收窄到 #0, 移除 FragrantMushroom#* 通配, 新增 SealOfGold#0 拒绝记录变量互换错). 提取器 Variant 默认 "immediate", unblockable 经 ledger fix 注入.

代码: SpecOpcode+LoseGold/AddCurse; EffectFragment.IsDownside (opcode 判定); RelicGenerator SeedVersion→relics-v2 + 整数加权采样 (下行 140 vs 普通 100, PickWeightedUniquely); RelicText EN/ZHS 新用例 (诅咒文本通用化 "add 1 Curse to your deck"/"将1张诅咒牌加入你的牌堆", 具体诅咒由牌自身本地化); AnthonyRelicModel 执行器 5 新 case + CurseTypes 反射泛型 AddCurseToDeck<T> + HasUponPickupEffect 动态覆写 (引擎契约: 带拾取效果=不可交易). 设计硬约束: 加权系数是常量不是配置项 — 生成定义不得受配置影响 (seed 纯函数契约).

验证: Release 0 警告 0 错误; relic-probe 37 PASS + PROBE OK (含新鲜度守卫; 新增 11 碎片组成断言 + 1.4 权重行为断言: 100 seeds 触发型抽取下行份额在 uniform×[1.15,1.65] 区间). 部署哈希与构建一致. 未实机验证: 真实拾取下行遗物 (诅咒入牌堆/失上限/失金币/自伤), 用户冒烟; 工坊推送待用户 2FA.

### v0.1.6 追加: 描述标点结构化重组 (用户报告语病)

问题: 碎片文本自带句号, 机械拼接产生 "gain 14 Block. gain 1 Strength." —— 并列同时触发的效果被句号切成两个不相关的句子; 纯被动遗物还以小写动词开头.

修复 (只动渲染层, 生成逻辑与 SeedVersion 不变, 同种子同遗物): RelicText.EffectEn/EffectZhs 全部 40 个用例改为列表项形式 (无尾部句号); GeneratedRelicDefinition.DescriptionEn/Zhs 按真逻辑重组 —— EN 单效果直拼、双效果 "A and B"、三个以上 "A, B and C", 首字母大写 (被动开头), 全句唯一句号; ZHS 以顿号","并列并以"。"收尾. 描述仍由 AnthonyRelicLocUpdater 在运行期刷新, 无需重开进程.

验证: 构建 0/0; 探针 38 项 PASS + PROBE OK, 新增标点断言 (50 seeds x 60 slots: 句中不得出现句中句号, >=2 效果必须含 " and " 或 ","); 样例确认 "Draw 2 additional cards on turn 1 of each combat." 大写开头. 部署哈希 a33d2723 与构建一致.

### v0.1.6 追加2: 生命上限提升策略收紧 (用户指令, 硬规则)

规则: 生成遗物只允许在"拾起时"(obtained)触发下提升生命值上限; 其余任何场景(战斗结束/获得金币/回合系/无触发被动)一律禁止. 受影响池内碎片: ChosenCheese(战斗结束+1)与 DragonFruit(获得金币+1)同形折叠的一条 gain_max_hp|amount:1 碎片 —— 数据与账本保留(忠实记录原版遗物行为), 仅生成采样排除. Mango/Strawberry/BigMushroom 的拾取系上限增益不受影响; lose_max_hp(下行)不受影响.

实现: RelicGenerator.Forbidden 扩展为 Excluded(递归边界 + max-HP 策略, 含被动路径防御), eligible 过滤两处 + 被动路径接入; SeedVersion relics-v2→v3(资格规则变更, 同种子将重新生成, 存档续读走版本隔离). 探针: 新增策略断言(100 seeds 无违规 + 拾取系上限遗物仍可生成 + 池内碎片保留), soak 可达性断言改为"仅 max-HP 策略碎片允许缺席"(原全量可达断言不再成立).

验证: Release 0/0; relic-probe 42 项 PASS + PROBE OK; content 已重同步(dll 43bbcedf), changenote 增补策略句, VDF 校验通过. 0.1.6 仍未推工坊, 本次与下行词条池/文本重组合并为一次发布.

## 2026-09-17 v0.1.7 (已提交 abf8cee, 已推送): 先古限制类词条入池 + 取消负面权重加成 + 纯增益 5% 门控

用户指令 (三条, 顺序到达):
1. 把先古(Ancient)遗物的限制类负面补进词条池 -- "全做", 要求把限制类钩子全部列出后**按极性分池**;
2. 取消负面词条的概率加成 (`DownsideWeight` 140 改回 100, 负面恢复均匀抽取);
3. 先古遗物中的**纯增益**出现概率调到 **5%**.

### 普查错误与纠正 (教训, 见 DEVELOP.md 同章)

首次用**钩子名正则**筛出 26 个"限制类钩子" -- 错. 7 个只命中 `ModifyMaxEnergy` 的是先古标准
+1 能量增益; 反而漏掉 `PhilosophersStone.AfterCreatureAddedToCombat` 与
`SpikedGauntlets.TryModifyEnergyCostInCombat`. 改为对 102 个先古遗物**全量**取实现体按**语义极性**
判定. 定案 6 个真实限制类负面, 全部与配对增益同体(引擎从不单独给出限制).

### 极性分池 (不是全塞负面池)

纯负面 6 个入负面池; 纯增益 5 个入**受 5% 门控的正面池**. 把增益当负面会产出标注错误的遗物.
`IsBenefit` 作用域**限定先古**: `modify_hand_draw` 故意不列入 `BenefitOpcodes`, 否则会把既有的
`BagOfPreparation`(+2, 普通品质)从 15% 被动带挪到 5% 带 -- 属未授权的既有池行为变更.

### 5% 是槽位级门控, 不是权重

被动抽取路径(`PickUniquely`)硬编码 `NormalWeight`, 权重无法表达"5% 的遗物". 故
`BenefitRelicChancePercent = 5` 与 `PassiveRelicChancePercent = 15` 用**同一次 roll** 划成互斥三段
(5% benefit / 15% passive / 80% triggered).

### 两处必须记的实现约束

- **offset 不消耗 `usedPassives`**: 所有 `modify_max_energy` 原子塌缩为同一 fragment(Key 不含
  condition), 消耗它会让整局最多只出 1 个 restriction.
- **offset 必须取正号**: `modify_hand_draw` 同时含正负(`BagOfPreparation` +2 / `BigMushroom` -2),
  用 `e.Amount > 0` 选正号, 否则会配出"禁抽牌 + 少抽2张".

### 数据

`relic_atoms.json` 146->156 (追加 10 条 query atom, **不覆盖** -- committed 文件含 6 条手工原子);
`relic_atom_ledger.json` 36->47 supported; `downsidePool` 字段改为反转声明.
`SeedVersion` relics-v4 -> relics-v5 (RNG 消耗形状变了).

### 提取器策略反转 (已显式落盘, 非静默)

`extract.py` 模块 docstring 原文逐字点名拒绝 `potion procurement`; 本条目**反转**该决策并写入
docstring: `ShouldProcurePotion` 这类是引擎**具名钩子**, 实现体返回完全确定的答案, 故 opcode 是
钩子自己的名字, 不是"发明"语义. 仍拒绝真正无法归约的(自定义卡牌变换/依赖 RNG 的单敌选择器).
`QUERY_HOOKS` 按 **(遗物, 钩子)** 建键: `TryModifyCardRewardOptionsLate` 有 9 个实现者, 按钩子名
建键会写进 8 个错误标签. 5 个带 `[SavedProperty]` 的钩子显式排除(`QUERY_HOOKS_STATEFUL`).

### 执行器

12 个新钩子覆写: `ModifyGoldGained` / `ShouldProcurePotion` / `ShouldPlay` / `ShouldDraw` /
`TryModifyEnergyCostInCombat` / `ModifyMaxEnergy` / `ShouldFlush` / `ShouldTakeExtraTurn` /
`ModifyCardRewardCreationOptions` / `TryModifyCardRewardOptionsLate` /
`AfterCreatureAddedToCombat` / `BeforeSideTurnEndEarly`, 加 `PassivesWith`/`HasPassive` 辅助.
`RelicText` EN+ZHS 补齐全部新 opcode (缺失会在渲染时抛异常); `Glam` 无权威 zhs 译名, 保留英文.

### 实机验证 (副本 A `I:\Slay the Spire 2`(`I:` 已于 2026-09-17 迁至 `E:\Slay the Spire 2`, 见 AGENTS.md Sec 2b), vulkan, 0.1.6 + build #2)

**已通过**:
- 启动日志池构成与探针逐字一致: 156 atoms / 47 supported / 26 rejected; 15 triggers / 31
  triggered effects / 8 passives / 5 benefits; restrictions 6.
- 同 seed 重启生成**完全一致** (确定性保持).
- 生成的遗物日志确认 6 个 restriction **全部出现且都带 offset**, 4 个纯 benefit 出现.
- **能量 5/5** = 基础 3 + benefit +1 + offset +1 -- 两条 +1 能量词条均生效.
- **金币 99 -> 99**: 连发两次 `gold 50` 均未增加 -- `ModifyGoldGained` 限制钩子生效
  (`PlayerCmd.GainGold` -> `Hook.ModifyGoldGained` 是唯一路径, 命令回显的 "'50' gold added."
  是**不检查钩子结果的固定文本**, 故只能看计数器).

**探针**: `PROBE OK`; 6 个 restriction 全部可达; 实测 benefit 5.45% / passive 13.62% /
triggered 80.93% (200 seeds x 60 slots).

### 两个已修 bug (修在 build #2, 已部署)

**BUG 1 `ApplyEnemyStrengthAsync(owner)` 是死代码**: `AfterRoomEntered` 里传入 `Resolve("room_entered")`
的返回值, 而 `Resolve` 在 `definition?.Trigger is null` 时返回 `(null,null,null)` --
`enemy_strength_gain` 是**被动**词条(无 trigger), 故 `owner` 恒为 null, 函数在
`owner?.Creature?.CombatState is null` 处直接 return. 加上 `AfterCreatureAddedToCombat` 只在
**战斗中新增生物**时触发, 该词条在正常战斗**永不生效**, 而其文本承诺"敌人进入战斗时获得力量".
修法: 改为传 `Owner` 并前移到 `Resolve` 之前.

**BUG 2 `_wasOwnerPartOfLastPlayerTurn` 惰性且注释错误**: (a) 赋值在 `effects is null` 提前返回
**之后**, 被动型 `extra_turn` 遗物根本到不了; (b) 该字段**只会被赋 true, 从不赋 false**, 故
`!_wasOwnerPartOfLastPlayerTurn` 永不为真; (c) 注释里"引擎禁止 turn-1 额外回合"的说法**是反的**.
对照引擎 `PaelsEye.cs` 的真实实现: `AfterSideTurnStart` 在 `side == Owner.Creature.Side` 且
`!UsedThisCombat` 时, 按 `participants.Contains(Owner.Creature)` 分别置 **true / false** --
即有 false 分支, 且**每个**玩家回合(含第 1 回合)都执行.
修法: 移到 trigger 提前返回**之前**, 并补上引擎的 false 分支
(`_wasOwnerPartOfLastPlayerTurn = participants.Contains(Owner.Creature)`).

`ApplyEnemyStrengthAsync` 的修复已逐行对照引擎 `PhilosophersStone.cs` 确认一致: 同一个
`AfterRoomEntered`(战斗开始名册) + `AfterCreatureAddedToCombat`(战斗中新增) 双钩子, 同样的
`GetOpponentsOf().Where(IsAlive)` 目标集与 `ThrowingPlayerChoiceContext`.

### BUG 3 (修在 9dd7a80): `BeforeSideTurnEndEarly` 少了引擎的第 4 个守卫

复审额外回合记账时发现: `BeforeSideTurnEndEarly` 只检查了 `PaelsEye` 四个守卫中的三个,
漏掉 `!WasOwnerPartOfLastPlayerTurn`, 而它的 doc 注释却写着"Guard set mirrors
PaelsEye.cs:110-121 exactly". 该断言在字段恒为 true 时无害, 但 **BUG 2 的修复让这个字段
真的可以为 false**(多人局中被强制跳过回合的玩家), 于是这个钩子会烧掉那个玩家的手牌, 而
`ShouldTakeExtraTurn` 会正确地拒绝给予额外回合 -- 纯亏损, 且与兄弟钩子自相矛盾.
修法: 补上该守卫, 注释与代码一致.

### 工坊文案 (已在 9dd7a80 改写, 不再是阻塞项)

v5 删除了 `DownsideWeight`, 而 `workshop_upload.vdf` 的描述与 changenote 仍写着
"出现率比均匀采样高 40%(权重 140 比 100)" / "weight 140 vs 100". 已改写:
描述改为"均匀抽取, 无概率加成", 并补上先古限制词条(必定与配对增益同体)与纯增益 5% 门控;
changenote 前置 v0.1.7 条目, v0.1.6 原文作为历史保留(其 weight-140 句子描述的是 v0.1.6, 正确).
版本 0.1.6 -> 0.1.7.

**改写时的坑(值得记住)**: 用 ASCII 双引号写 `"no gold"` / `"+1 Energy"` 会**提前终止 VDF 字符串**,
描述被静默截断(3563 -> 1056 字符)而编辑器里看不出问题. 中文段有同样缺陷. 两处改用全角引号后,
`check-vdf.py` 报 `balanced+paired: True`, 8 个键值对, description 4222 / changenote 3663 均正常解析.
检查器的 `changenote starts v0.1.6` 断言现在必然为 False(它钉的是旧前缀), 属预期, 不是故障.

### 未闭环 (不得含糊)

- **enemy_strength 词条已实机验证通过 (2026-09-17, 副本 A, 构建 `29bade36`)**:
  开局 -> `relic add RELIC028`(slot 28 "Unlikely Crown" = 敌人入场获得 1 力量 + 1 能量)
  -> 进 Act 1 BOSS 战(THE_KIN_BOSS). 三个敌人血条下方**都有红色剑图标 + 数值 1**(力量 1),
  同时能量读数为 **4**(基础 3 + 配对 offset +1). 即限制类词条与配对增益**两半都生效**.
  修复前该词条在正常战斗**永不生效**(owner 取自只对 trigger 生效的查找, 被动词条恒为 null).
- **extra_turn 词条端到端可用 -- 已实机验证 (2026-09-17, 副本 A, 构建 `29bade36`)**, 单人局:
  `relic add RELIC013`(slot 13 "Scrambled Medallion" = 打出 0 张牌则获得额外回合), 第 1 回合
  **不打任何牌**直接结束. 引擎日志 `Player 1 (IRONCLAD) is taking an extra turn`, 手牌被重新抽取.
  即该词条端到端可授予额外回合.

  **这次测试不能区分修复前后**: 单人局里 `_wasOwnerPartOfLastPlayerTurn` 修复前后**同为 true**
  (无强制跳过分支), BUG 3 补的第 4 个守卫在 flag=true 时**恒不触发**, BUG 4 的子句在没有
  WhisperingEarring 时**根本不参与**. 所以它证明的是"extra_turn 词条端到端可用", **不是**
  "BUG 2/3 已验证".

  **未证实的细节(不要当成结论)**: 我起初据"弃牌堆计数为 0"推断旧手牌被
  `BeforeSideTurnEndEarly` 烧毁, 该推断**已被自己的对照否证** -- 之后打出一张牌(敌人 27->17,
  能量 4->3, 手牌 4->3), 同一个计数器**仍读 0**, 说明它不是我以为的弃牌堆. 手牌去向(烧毁 vs
  弃置)**目前无可靠证据**, 且该细节不承重: BUG 3 的守卫在 flag=true 时不会触发, 烧毁与否只反映
  既有的 Pael's Eye 镜像行为, 与本轮 4 个修复无关.

- **BUG 4 的区分性证据 -- 已取得**: 同一局内做天然对照(同遗物、同 flag 状态, 唯一变量是
  WhisperingEarring 是否打牌):
  - 第 1 回合: WhisperingEarring 自动打牌(截图确认敌人已受 18 伤害), 结束回合后
    **没有**额外回合(日志 18433 `turn 1` -> 18451 `turn 2`, 中间无 `extra turn` 行).
  - 第 2 回合: **未打任何牌**(无自动打牌), 结束回合后**给出**额外回合(日志 18452
    `is taking an extra turn`).
  同一局的这个差异排除了"flag 恒 true 所以看不出差别": 若缺 `AnyCardsPlayedThisTurn` 的
  WhisperingEarring 子句, 第 1 回合的历史检查会因全是 auto-play 而返回 false, 从而**误给**额外
  回合; 实际第 1 回合不给、第 2 回合给, 正是子句生效的形状.

- **BUG 2 的 false 分支与 BUG 3 的守卫仍未验证**: 两者的差异都只在
  `_wasOwnerPartOfLastPlayerTurn = false` 时显现, 而该状态**只能由多人局的强制跳过产生**
  (单人局恒为 true). 需要双人局 -- 这是本轮唯一真正需要联机的项.
- 副本 A 与副本 B 均已部署 `29bade36`, 三处(构建/副本A/副本B)md5 一致.
- **自动化边界**: 用户占用前台时(如打 CS2)不抢焦点. 已验证 `PostMessage` 可在**不抢焦点**下
  驱动键盘(控制台开关/命令发送均成功), 且 `PrintWindow(.., 2)` 可后台截图; 但**鼠标按键对 Godot
  无效**(悬停生效, 按键被忽略, 伪造 WM_ACTIVATE/WM_SETFOCUS 也不行), 因此需点击的验证只能在
  用户空闲时做.

## 2026-09-17 遗物名由原子来源派生 (用户指令, SeedVersion v6)

### 问题

遗物名与效果**完全无关**: `RelicGenerator.PickName` 从 `RelicText` 的两张 24 词表
(`AdjectivesEn`/`NounsEn` 与对应 zhs)里各抽一个词, 于是 "Chaotic Orb"(混沌的宝珠)与它的
trigger/效果没有任何关系. 用户要求: 名字必须能让人**看出**它由哪些原子重组而来.

### 参照物: 原版 Auto-Anthonyology 的卡名机制(反编译 `ChaosCardGenerator.CardNameGenerator`)

原版**不是**从"幸存的那个原子"取名的. 机制三段:

1. `BuildSourcePool(catalog, card, ..)`: 对**每一张**源卡按"与生成卡的相似度"加权建池 --
   `DiceSimilarity(descriptionSchemas, recipeSchemas) * 1000 + DiceSimilarity(templates, recipeTemplates)`,
   再 `weight = 1 + tuple.Item1/50 + primaryEffectSimilarity*80 + sameTypeSameCost*30 + sameType*10`.
   关键性质: 池是**全体**源卡, 匹配者以最高约 2283:1 压过地板权重 1(DiceSimilarity 上界
   100 -> `1 + (100*1000+100)/50 + 3*80 + 30 + 10`), 因此池**永不枯竭**.
2. `WeightedNameSourcePool.Sample` 抽**两个** `NameParts`(累积权重 + `Array.BinarySearch`).
3. `ComposeChinese` / `ComposeEnglish` 把两个 `NameParts` 的词块拼起来;
   `ChineseSplitOverrides` 与 `ManualExternalNameParts` 是手工词块表.
   随后 `TryCreateUniqueName` 在 `max(512, poolSize*24)` 次内保证唯一.

即: **名字由来源的名字词块组成, 且来源池带地板权重**. AAR 的对应物: "与生成卡相似" -> "确实
贡献了原子"(精确 provenance), 地板权重保留下来只为给单一来源的遗物补第二个词素.

### 折叠陷阱(决定了实现形状)

`EffectFragment.SourceAtom` 原本只存**一个** `atom.Id`, 而片段按 `Key` 折叠且**后写覆盖**
(`bucket[effect.Key] = effect;`), `Key` 又不含来源. 于是折叠片段保留的 `SourceAtom` 是
**任意**的, 不是来源集合 -- `modify_max_energy` 有 7 个来源, `gain_energy`/`draw_cards`/
`gain_max_hp` 各有 2 个, `obtained` 折叠 15 个, `turn_start|first_turn` 折叠 4 个.

**选择: 方案 (b) -- 让折叠保留全部来源.** `EffectFragment`/`TriggerFragment` 的
`SourceAtom`(单值)改为 `SourceAtoms`(`IReadOnlyList<string>`), 折叠时取**并集**
(`RelicFragmentPool.Union`, Ordinal 排序, 与账本数组顺序无关). 理由: 用户要的正是"可见的真实
出处", 方案 (a)(从语义取名)只能表达机制、表达不了具体来源, 恰好丢掉这个特性的全部价值; 而
方案 (b) 让名字能指向真实来源. 代价是片段形状变了, 已逐项核对:

- **池指纹不变**: `Fingerprint` 只由片段 `Key` 组成, provenance 不参与 -> v5/v6 指纹逐字相同
  (实测两边均 `64D5C820863AACB1`). 缓存键里区分新旧的是 `SeedVersion` 这一项.
- 无任何序列化路径(`JsonSerializer`)涉及这些片段; 使用点只有执行器的两处诊断字符串(已改为
  打印来源集合)与探针.
- 折叠顺序本身**确定性**: 遍历 `ledger.Supported` 的 JSON 数组顺序, 且并集后排序, 因此与字典
  枚举顺序无关.

### 实现

- `RelicText.Morphemes`: 45 个来源遗物各一条 `NameMorpheme(Source, En, Zhs)`. 两个字段**都是
  该遗物官方标题的子串**(逐条对照权威 loc 转储 `f05-verify/{eng,zhs}-relics.json`, 45/45 通过),
  例如 `BagOfPreparation` -> `Preparation`/`背包`, `PrecariousShears` -> `Shears`/`羊毛剪`.
  查不到词素**抛异常**, 不回退到臆造文本(用户要求: 没有合适词素就报告, 不要发明).
- `RelicGenerator.PickName(random, trigger, effects, used)`:
  - `provenance` = trigger 与全部 effect 的 `SourceAtoms` 去重后的来源集合(Ordinal 排序);
  - **stem 必从 provenance 内取** -- 保证**每一个**名字都含真实来源词素;
  - tail 从全体来源按权重取(provenance 64 : 其他 1), 与原件的地板权重同形, 保证词素对空间
    足够(45x44), 单一来源遗物也能拿到第二个词素;
  - 去重键含 EN 与 ZHS 两者(避免中文玩家看到同名).
- 调用点在**片段选完之后**, 且用**独立 RNG 流** `{ModId}/{SeedVersion}/{seed}/slot/{n}/name`
  (重试路径同理 `.../retry/{r}/name`).
- 旧的两张形容词/名词表已**整段删除**(无死代码); `RelicText.NameEn/NameZhs` 一并移除.

### SeedVersion v5 -> v6: RNG 消耗形状确实变了(非预防性)

旧实现每个槽位从**片段流**取 2 次 `Next(24)`, 且重试路径上与片段抽取交错; 新实现完全不碰片段流.
因此 v5 与 v6 同 seed 的片段序列必然不同, 旧档必须重新生成, 与 v4->v5 同理.

**隔离实验证明命名代码本身不扰动片段抽样**: 把 v6 源码的 `SeedVersion` 临时改回 `relics-v5`,
对 22 个 seed / 1320 槽位输出片段投影, 与改动前的基线**逐字节相同**. 因此真实构建里观察到的
片段变化**全部**归因于 v5->v6 版本串, 与命名无关.

### 验证(全部实测, 无实机)

- `dotnet build AutoAnthonyRelics.csproj -c Release -p:CopyToModsFolderOnBuild=false`: **0 警告 0 错误**.
- `tools/relic-eligibility-probe`: **PROBE OK**, 含 `same seed -> byte-identical relic set` PASS.
- `tools/relic-probe`: **PROBE OK**(顺带修正 5 项**先于本次改动**就已过期的断言: 硬编码的
  146 atoms/36 supported 应为 156/47; `passiveSupported` 只有 `modify_hand_draw`, 而 v5 的 11 个
  被动 opcode 执行器**都已实现**; 金额检查漏掉 restriction/benefit 这类无数字旗标;
  `cross-source` 的"触发来源与效果来源不相交"在 provenance 变成集合后是**错判据** -- `obtained`
  折叠 15 个来源, 几乎必然与任何效果来源相交, 已改为"该遗物 provenance 至少跨 2 个来源"
  (实测 1056/1070 = 98.7%, 而相交判据只有 942/1070 = 88.0%)).
- 命名覆盖(22 seeds x 60 槽位 = 1320): 数字后缀兜底 **0**; stem 非真实来源 **0**;
  ZHS 未以真实来源词素开头 **0**; 同 run 内重名 **0**; 词素 45/45 均为官方标题子串.

### 未做 / 边界

- 未实机验证(需用户启动游戏看遗物名); 词素表覆盖的是账本 47 条 supported 原子涉及的 45 个来源,
  若账本将来纳入新来源, `RelicText.Morpheme` 会**抛异常**提示补表, 而不是静默降级.
- `Glam` 等无权威 zhs 译名者沿用英文, 与既有约定一致(见 v5 条目).

---

## 2026-09-17 "替换原版遗物"选项: 补上读档路径 + 让补丁可观测

### 背景
用户问"有没有禁止原版遗物生成的选项". **有**: `AutoAnthonyRelicsConfig.ReplaceVanillaRelics`
(设置里 "替换原版遗物", 默认开), Harmony Postfix 打在引擎遗物袋 `Populate` 上, 剥离所有非
`CustomRelicModel` 的条目(`_deques` 与 `_originalRelics` 都剥, 后者防止稀有度队列耗尽时
`RefreshRarity` 把原版塞回).

### 修的两个真问题

**(1) 读档进入的局不会被剥离.** `Populate` 只在开局跑一次; `LoadFromSerializable` 从
`RelicIdLists` **原样还原** `_deques`, 之后 `IsPopulated` 为 true, `PopulateIfNecessary` 直接
短路. 所以"开关关闭时创建的存档"(或装本 mod 之前开的档)读进来后袋里仍是原版遗物, 开关开着
也看不到效果. 实测该档: 87 个袋条目, **0 个**是生成的.
修法: 给 `LoadFromSerializable` 加同款 Postfix. 单机下玩家的袋**就是**共享袋(Player ctor 里
`relic-bag = shared relic-bag`), 所以这一个实例方法覆盖全部三个读档点 --
`RunState.FromSerializable`(共享袋), `Player.FromSerializable`(玩家袋),
`CombatStateSynchronizer`(MP 客户端重同步).
`_originalRelics` **有意不还原**: 它从不被序列化, 读档后为 null, 其唯一读者 `RefreshRarity` 在
读档袋上不可达(`FromSerializable` 用无参 ctor, `_refreshAllowed` 保持 false). `Apply()` 里读该
字段用的是 `is List<RelicModel>` 模式匹配, null 天然落到 else 分支, 已正确处理.

**(2) 这个功能原本不可观测.** Harmony 的 `CreateClassProcessor().Patch()` 在
`TargetMethod()` 返回 null 时**不报错** -- 它什么都不补, 却照样计入 "applied". 所以引擎一旦改
类型名, 本功能会静默退化成空操作, 而日志仍显示绿色的 "N patch class(es) applied". 另一条日志
(移除计数)只在 `Populate` Postfix 内部打印, 而只到主菜单的会话根本不会触发它 -- 这就是为什么
5 个历史日志里 `pool replacement` 都是 0 次, 那个 0 **不能**证明补丁坏了.
修法: 启动时显式解析袋类型, 打印每个 `Populate` 与 `LoadFromSerializable` 重载, 并在
`LoadFromSerializable` 缺失时**报错**.

### 实机验证
```
[AutoAnthonyRelics] Harmony: 9 patch class(es) applied, 0 failed      (原 8)
[AutoAnthonyRelics] vanilla-relic replacement armed: MegaCrit.Sts2.Core.Runs.relic-bag
   Populate overloads = [Player,Rng | IEnumerable`1,Rng],
   LoadFromSerializable = [Serializable relic-bag], replaceVanilla=True
```
0 崩溃. 启动期无 AAR 错误.

### 未验证(诚实标注)
**剥离动作本身未实机验证** -- 引擎没有任何控制台命令能开局(`win`/`room`/`travel`/`act` 都要求
局面已存在, `RunManager` 的开局入口是 `SetUpNewMultiplayer` 无 console 前置). 要看到
`pool replacement: removed N relics` 必须真实开一局.

### 排查中犯的错误(已撤回, 教训留档)
1. **误判"类型名不匹配"**: 比较 AAR 拼接的 `BagTypeName` 与引擎类名时, 两边都被
   `.omp/hooks/pre/strip-illegal.ts` 规则 7 重写(`relic-bag` -> `relic-bag`), 于是看起来不同.
   实际字符串**完全一致**(码点核对: 0x52 0x65 0x6c 0x69 0x63 0x47 0x72 0x61 0x62 0x42 0x61 0x67
   = `relic-bag`). **教训: 该仓库里含 `relic`+`grab` 的标识符在工具输出中一律被重写, 比较字符串
   必须用码点或绕开 hook 读取.**
2. **误判"读档是缺口所以补丁坏了"**: 机制分析对(读档确实绕过 Populate), 但我起初把它当成
   "补丁失效"的证据. 它只是**范围**问题, 不是功能不存在. 且存档里 87 个原版遗物是**旧档**的
   状态快照, 不能用来判断当前选项是否生效.
3. **误从"日志 0 次"推断补丁未触发**: 见上文 (2).

### 追加: MP 客户端不得剥离遗物袋(审查发现, 我引入的缺陷)

上面那个 `LoadFromSerializable` Postfix **同时被 MP 客户端重同步路径命中**:
`CombatStateSynchronizer.WaitForSync` 在 `Type != Host`(即客户端)时把 **host 的袋快照**
下发覆盖本地. 在那里剥离会让**客户端袋 != host 袋** -- 只要两端 `ReplaceVanillaRelics` 不同就
必然发生, 而这正是 `AutoAnthonyRelicsConfig` 注释里声称"结构上安全"的场景.

`Populate` 钩子没有这个问题: 两端从同一 seed 重建.

**修法**: `Apply()` 增加 `allowStrip`;`LoadFromSerializable` 的 Postfix 传
`allowStrip: !IsClientMirror()`,`IsClientMirror()` 读
`RunManager.Instance.NetService.Type == NetGameType.Client`(防御式, 任何异常都判为"非客户端",
即保持剥离 -- 对本地存档而言"少剥离"是更安全的方向). **两个剥离分支都受它管**:客户端镜像上
剥离我方占位模型(`Enabled=false` 分支)同样会让袋分歧. 跳过时日志标 `mirror-only`.

```
Singleplayer -> 剥离(本地存档)
Host         -> 剥离(host 拥有自己的袋)
Client       -> 不剥离(host 的镜像)
```

同时更正了 `AutoAnthonyRelicsConfig` 的 MP DETERMINISM 注释: "两端配置可不同"只对**生成**成立,
不适用于**改动被复制的状态**(遗物袋), 并写明新增此类开关必须带同样的门控.

**未验证**: MP 客户端路径本身未实机验证(需双端会话). 单机启动验证: 9 补丁类 0 失败, 自检报告
两个 `Populate` 重载与 `LoadFromSerializable` 均解析, 0 崩溃, 0 AAR 错误.
