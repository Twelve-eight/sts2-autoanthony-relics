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
