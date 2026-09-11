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
