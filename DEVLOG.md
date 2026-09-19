## WS-0919-05 - 2026-09-19 - 全量代码审查 (用户指令"审查所有代码")

### 方法

先派 3 个 reviewer 子代理(生成层/执行器/生命周期)并行审查, 但它们**静默 7-8 分钟**
(AGENTS.md Sec 11 阈值), 已取消并**收回主会话自做**. 主会话的审查比子代理更有效:
可直接跑暴力验证与跨进程实验.

### 修掉一个真实缺陷(潜在非确定性)

`RelicFragments.Build` 直接迭代 `Dictionary<string,int> fix.Values`.
.NET **字符串哈希按进程随机化** -> 字典枚举顺序**不保证跨进程稳定** -> 进入 `values` 顺序
-> `ValuesKey` -> 片段 Key -> 遗物 fingerprint -> **生成输出**.
实测当前账本每个 fix 只有 1 个 value key(max=1, 无多键条目), 所以尚未触发;
但加一个双键 fix 就会让同一 seed 在不同进程产出不同遗物, 且极难归因.
已改为 Ordinal 排序(对现有数据是 no-op).

### 暴力验证(而非抽样)

`PickBand` 的整数算术对 **68,921** 组三权重组合(0..400 步长 10)全部验证通过:
- `Math.Clamp(value, min, max)` 在 `min > max` 时**抛异常**, 而我传的 min 是
  `benefitThreshold + 1` -- 这是真实崩溃风险. 全组合无抛出.
- 阈值恒在 [0,100] 且严格递增; 非零权重恒保留其档可达.

### 新增断言

"关闭负面"配置下的**良构性**: 默认 soak 只跑出厂设置, 看不到该配置, 而该配置会从触发池
移除片段 -- 这是"有触发但零效果"遗物的唯一产生途径. 已加 120 seeds 断言(通过).

### 核实为正确的项(已记录, 不再重复检查)

- 生成层全仓库无 `GetHashCode`; 池组装 `OrderBy(Ordinal)`; `Union` 用 `SortedSet`;
  `RestrictionOffsets` 仅 `ContainsKey`/`TryGetValue`(顺序无关).
- 跨进程确定性: 3 次独立进程, fingerprint 与断言一致.
- `GenerationSettings.Key` 打包 1+1+4x10 = 42 bit(余 63 bit), 各字段 clamp 到 10 bit -> 单射无碰撞.
- `retain_hand` 在两种状态下都可达; 额外池负面 `ethereal_hand_card` 确实被覆盖
  (断言其出现在默认集合里, 而非假设).
- `CombatCardSelection` 是引擎对"战斗中随机选牌"的既有约定(TrueGrit/Cinder/Thrash/
  MummifiedHand/JeweledMask), 通道存在且语义相符.

## WS-0919-04 - 2026-09-19 - "关闭所有负面效果"选项 (用户指令)

### 指令

"为东尼遗物添加一个关闭所有负面效果的选项.(回合结束不自动丢弃手牌不是负面效果)"

### 交付

新配置 `DisableNegativeEffects`, **默认关**. 生成期谓词门控(与额外池开关同构):
池恒定构建, 选项只作用于 `RelicGenerator.Excluded` 规则 8, 并作为
`GenerationSettings.Key` 的分量进入定义缓存键.

### "负面"的范围 (逐条读引擎实现体确认)

`IsNegative = IsDownside || IsRestriction`:
- **触发类伤害**: 失去生命 / 生命上限 / 金币, 获得诅咒, 手牌虚无.
- **先古限制类**: 金币/药水/出牌数/抽牌限制, 能力牌费用 +1, 敌人获得力量.
  限制类**算**负面: 补偿(`RestrictionOffsets`)让这对词条公平, 但不让限制本身不再是代价.

**明确排除 `retain_hand`**(用户点名): 它是 RunicPyramid 的 `ShouldFlush` 返回 false,
即"回合结束不弃手牌". 它和限制类一样是**否决型**钩子, 一眼像负面, 但只会保留玩家本会失去的牌.
已在 `EffectFragment.IsNegative` 的文档里写明理由.

### 一个刻意的"不做"

**没有 bump SeedVersion**. 它参与 RNG 流字符串(`:506`), bump 会重掷**所有**局 --
包括选项为关(行为与旧版完全相同)的局, 对每个既有存档都是无谓的 60 件遗物重掷.
选项不需要它: 它已是缓存键的分量, 开关切换会正确地重新生成. 已在 `SeedVersion` 处写明理由.

### 审查发现的遗漏: 符号负值 (已修)

`IsNegative = IsDownside || IsRestriction` **漏掉**了按**符号**判定的负值:
`BigMushroom#ModifyHandDraw` 的账本 fix 把 amount 覆盖为 **-2**(渲染"少抽2张牌"),
这是真实代价, 却不在任何 opcode 黑名单里 -- 开启"关闭负面"后它仍会出现.
根因: 极性在这个 opcode 上是**按值**而非按 opcode 的(`modify_hand_draw` 两个符号都有:
BagOfPreparation +2 是增益, BigMushroom -2 是代价), 生成器自己的 `RestrictionOffsets`
注释早就把它称作 "the -2 downside".

已修: `IsNegative` 增加 `|| (Opcode == "modify_hand_draw" && Amount < 0)`.
同时把探针从"只断言 `IsNegative`"改为**也断言没有任何效果的值为负** --
只断言 `IsNegative` 是循环论证(属性写错也会通过).
判别力验证: 去掉符号子句 -> `no drawn effect has a negative VALUE` FAIL,
报告 `modify_hand_draw|passive|self|True|amount:-2 (amount=-2)`.

### 已知代价 (实测, 刻意接受)

关掉 6 个限制类后被动档只剩 1 个可用普通被动, 多样性下降.
40 局实测(2026-09-19 去掉兜底重发牌之后): 被动档 **40 槽位 / 1 种**遗物(未开启时 58 槽位 / 3 种). 旧记录 「231 槽位 / 6 种」 属**重发牌时代**, 两者都不算缺陷: 前者是重复, 后者是内容量不足.
**没有**塌缩成单一件, 但确实变单调. 接受并记录: 玩家显式要求关掉负面,
用多样性换掉负面是本意; 要恢复多样性应**扩充非负面被动池**, 而非让限制类漏回来.

### 验证

- `tools/relic-probe`: PROBE OK, 5 项新断言(默认会抽到负面 / 开启后一件都没有 /
  `retain_hand` 两种状态都可达 / 选项确实改变生成).
  判别力验证: 把 benefit opcode 也算作负面(即误判 `retain_hand`)
  -> `retain_hand survives the option` FAIL (`off=1 on=0`).
- `tools/relic-eligibility-probe`: PROBE OK, oracle 加了规则 8.
- 实机: `disableNegatives=True` 出现在初始化日志 -> 配置 -> BaseLib -> `ConfigSource`
  -> `GenerationSettings` 全链路打通; 无 `cfg migrated`; AAR 零异常.
- **未覆盖**: 真实战斗中不出现负面遗物(需玩家开局), MP 一致性.

## WS-0919-03 - 2026-09-19 - 权重语义定稿 + 阈值截断缺陷 (接 WS-0919-02)

### 权重语义 (这是本特性的设计定稿)

权重表示**各档在遗物池中的占比**, 不是"池内片段的权重". 理由: 被动档与增益档内部
**权重同质**, 对它们做加权抽取在算术上等同均匀抽取(WS-0919-02 的根因).

实现: `RelicGenerator.PickBand` 把三个基础概率按各自权重缩放后转成阈值,
**始终只消耗一次 `random.Next(100)`**. 因此:
- 出厂 100/100/100/100 -> 阈值恰为 5 / 20, 即**权重引入前的逐字节同一实现**
  (探针有 "same seed -> byte-identical" 与跨种子多样性断言把关);
- 只有四项之间的**比例**有意义(同倍缩放不改变阈值);
- 某一项为 0 -> 该档阈值归零, 该档关闭.

`WeightExtra` 是例外: `pool.TriggeredEffects` 真正混了核心与额外片段, 所以它在
触发档**内部**也起作用.

### 修掉的两个真实缺陷

1. **非零权重会静默删除该档**(整数截断). 阈值算的是比例, 而 `WeightBenefitCore=10`
   (滑块最小非零步长) 配 400/400 得 `100*50/38050 = 0` -> 增益档被删除, 尽管玩家设的是
   **非零**值.已给阈值加下限: 缩放权重 > 0 时阈值至少为 1, 且累积阈值严格递增
   (否则非零被动权重会被增益阈值吞掉).
   判别力验证: 去掉下限 -> 探针
   `a minimal NONZERO 'benefit' weight keeps its band reachable` FAIL (`0 slots`).

2. **loc 表去重键漏了设置分量**. `AnthonyRelicLocUpdater` 的键是
   `seed + Fingerprint`, 而定义缓存键是四元组(含 settings).设置现在会改定义而不改种子,
   正是该键存在的意义, 却是它唯一遗漏的输入 -> 注入的遗物文案可能落后于玩家实际持有的
   定义.已补 `GenerationSettings.Current.Key`, 与其注释和 registry 键一致.

### 明确"不是缺陷"的项 (已实测反驳)

- **"触发档权重 0 不会移除该档"**: 实测 `WeightTriggeredCore=0` -> 档位构成
  `1780/3020/0`(benefit/passive/triggered), 触发档确实为 0.原因是 `PickPassives` 返回空时
  走 `PickEligiblePassive` 重发被动, 而不是无条件落到触发路径.探针断言成立, 未削弱.
- **"PickBand 文档声称逐字节一致是假的"**: 当前实现确实 `Next(100)` + 阈值, 出厂阈值
  恰为 5/20, 故与权重引入前同一实现; 注释已相应改写为准确表述.
- **`totalWeight == 0` 分支**: 已于 WS-0919-02 改为均匀抽取(不再返回 `candidates[^1]`),
  实测零权重下不再产生同一片段.

### 验证

`tools/relic-probe` PROBE OK, 权重相关断言共 10 项: 倾斜 profile 改变组合、
三个权重各自置 0 改变组合且清空本档、两个"非零最小权重仍可达"、全零回退到出厂基础概率.
关键项均做了故意破坏判别力验证.

## WS-0919-02 - 2026-09-19 - 权重接线修正 + 审计发现的崩溃 (接 WS-0919-01)

对 WS-0919-01 做了一轮对抗性审计, 查出**四个真实缺陷**, 其中两个是我自己引入的:

### 1. 两个滑块是死的 (设计缺陷)

初版把权重接在 `WeightOf` -> `PickWeightedUniquely`. 但 `BenefitEffects` 全带
`WeightBenefitCore`, `PassiveEffects` 全带 `WeightPassiveCore` -- 档内**权重同质**,
加权抽取在算术上等同均匀抽取. 只有 `pool.TriggeredEffects` 真正混了核心与额外片段,
所以四个滑块里只有两个有实际作用, 而"设置页面可调每种词条池的生成权重"是用户原话.

修正: 权重改为缩放**档位抽取**(新增 `RelicGenerator.PickBand`). 档内选取与 restriction
配对保持均匀(有意为之 -- 旋钮是"池贡献多少", 不是"池内谁胜出"). 设置页 hover 文案同步改写,
原先的文案承诺了代码做不到的事.

**探针原先抓不到**: 倾斜 profile 里 `weightTriggered=400` 一项就足以改变组合,
所以断言在 `WeightPassiveCore`/`WeightBenefitCore` 被整个删掉时仍然通过. 已改为
**逐个权重单独置 0** 验证: 必须同时"改变档位构成"且"该档归零".

### 2. 滑块拉到 0 会让游戏崩溃 (我自己引入的, 探针抓到)

`PickWeightedUniquely` 的 `totalWeight == 0` 分支取 `pool[candidates[^1]]`, **不消耗 RNG**
且每次返回同一个片段. 触发档权重置 0 时所有触发槽位拿到同一片段 -> provenance 相同 ->
`PickName` 名字空间抽干 -> 抛 `InvalidOperationException`. 滑块范围是 `[0, 400]`,
所以玩家拉到 0 就能在定义查询路径上触发未捕获异常.

修复: 该分支改为**均匀抽取**(同时恢复 RNG 消耗). 另把 `PickName` 的空间耗尽从 throw
改为返回最后一个合法组合 -- 名字重复只是观感, 抛异常会中断生成并破坏整局.

判别力验证: 把退化分支改回旧写法 -> 探针 6 项 FAIL, 含
`downside share ~= uniform  observed 1.000 vs uniform 0.300`(所有触发槽位同一片段).

### 3. 存档读入路径没有冻结设置 (遗漏)

`RunSeedLaunchTrackPatch.Postfix` 只设 `CurrentRunSeed`, 没有 `Freeze()`. 它是
**读档漏斗**, 所以读档后的局处于未冻结状态, 局中改权重会重掷已持有的遗物 --
正是冻结要防的事. 已补, 并把 `Freeze()` 改为**幂等**(首次冻结胜出), 使两个捕获点
不会互相覆盖.

### 4. 手牌抽取用了错误的 RNG 通道 (我自己引入的)

`PickHand` 用了 `RunState.Rng.UpFront` -- 那是**局生成**通道("你会遇到哪些怪物/事件/遗物",
`RunRngSet.cs:29-33`), 在每个回合开始消耗它会平移之后所有章节/地图/遗物 roll.
引擎对"战斗中随机选牌"的既有约定是 `CombatCardSelection`(`RunRngSet.cs:62`,
TrueGrit/Cinder/Thrash/MummifiedHand/JeweledMask 都用它). 已改用.
`EnchantDeck` 保留 `UpFront`: 它在 `obtained` 时于局外跑, 与 Qurious 的拾取附魔一致.

### 其他修正

- `EnchantCard` 硬编码 1 级, 而 `enchant_deck` 的文案把 `amount` 渲染成**层数** ->
  一旦账本 `Values` 被改, 文案与行为就会脱节. 已让牌组路径透传 `effect.Amount`
  (手牌路径的每张 1 级是对的, 那里的 `amount` 是**张数**).
- 启动日志 `(0 extra-pool)` 结构性恒为 0(9 个额外片段全是 triggered), 会被误读成
  "额外池没生效". 已改为同时统计两个列表.
- 两处注释把 Qurious 的属性误写成 `QuriousCraftingRelics.EnableExtraEffectPool`(不存在),
  改为其真实名 `EnableExtraPool`.
- `migration-probe` 的场景 2 **抓不到回归**: 它的 fixture 没有任何碰撞键, 旧规则也会跳过.
  已新增**真实事故 fixture**(一个碰撞键 `EnableExtraPool` + 外来键). 判别力验证:
  恢复旧规则 -> 该断言 FAIL 并复现"cfg migrated: 4 legacy keys".

### 验证 (实测)

- `tools/relic-probe`: PROBE OK.含逐个权重置 0 的档位验证.
- `tools/relic-eligibility-probe`: PROBE OK.
- `tools/migration-probe`: PROBE OK(含新的事故 fixture).
- 实机(开关置 True): `extra-pool: 9 triggered + 0 passive`,
  `extraPool=True`, `seedVersion=relics-v8`, 无 `cfg migrated`, AAR 零异常.
- **未覆盖**: 手牌效果在真实战斗中的生效(需玩家开局), MP 一致性.

## WS-0919-01 - 2026-09-19 - 额外词条池 + 每池生成权重 (SeedVersion v8)

### 用户指令

"扩遗物账本, 从怪异炼化遗物那里搬一点过来, 当作额外池, 做它的开关. 设置页面可调每种词条池的生成权重."

### 交付

1. **扩账本**: 从姊妹 mod QuriousCraftingRelics 的 extra 池搬 9 项(3 手牌关键词 + 6 附魔),
   写入账本新段 `extraSupported`; 原子 156 -> **165**。
2. **额外池 + 开关**: 新配置 `EnableExtraEffectPool`, **默认关**。
3. **每池权重**: 4 个滑块(触发/被动/增益/额外), 默认 100/100/100/100。

`SeedVersion` v7 -> **v8**(新增 9 个 name morpheme 会拓宽 `RelicText.AllSources`,
从而改变**每个种子**的遗物名字; 权重与开关本身也是生成输入)。

### 关键设计

- **开关用"谓词门控", 不重建池**: `MainFile` 永远以 `includeExtraPool: true` 建池,
  开关只作用于 `RelicGenerator.Excluded` 规则 6。重建池需要**在局中**发生, 而重建正是
  定义缓存键要避免的事。
- **设置进缓存键**: `GenerationSettings.Key` 把 5 个值(1 bool + 4 权重 x 10 bit)完美打包进
  `long`, 作为 `AnthonyRelicRunRegistry` 键元组的第 4 个分量。用打包而非哈希, 因为哈希会存在
  两组设置撞键、互相取到对方遗物的风险。**开局冻结**(seed 捕获点 `Freeze()`,
  `ResetForRunEnd` 里 `Unfreeze()`), 防止局中改设置改变已持有遗物的含义。
- **执行时机(初稿判断错误, 已修正)**: `combat_start`(`CombatManager.cs:594`)在 `StartTurn`(`:610`)
  之前 -> 手牌为空, 手牌效果会遍历 0 张牌静默失效; 而 `turn_start`(`:783`)在 `:778` `await`
  抽牌任务(`:924`)**之后** -> 手牌已存在。所以 `turn_start` 内联执行, 只有 `combat_start` 走
  `AfterPlayerTurnStartLate` 延迟通道(以 `turn <= 1` 保持每场战斗一次语义)。
  `enchant_deck` 作用于主牌组(`PileType.Deck => player.Deck`), 局外存在, 故在 `obtained` 内联。
- 生成侧规则 7: 手牌效果只配 `turn_start`/`combat_start`, 牌组效果只配 `obtained`。
- 延迟通道加**重复调用守卫**(RitsuLib 等框架会重发钩子, 否则开局手牌被附魔多次)。

### 修掉的两个真实缺陷

1. **跨 mod 配置键碰撞(我引入的, 已修 + 已恢复用户数据)**: 新属性最初叫 `EnableExtraPool`,
   与 Qurious `KnownLegacyScalarKeys` 里的旧键同名; 又因两 mod 的 cfg **文件名相同**
   (BaseLib 用根命名空间推导, Qurious 由 `AutoAnthonyRelics` 改名而来), Qurious 的迁移
   (当时判据为"任一键匹配即认领")把 **AAR 的配置整个偷走**。两侧修复: AAR 改名
   `EnableExtraEffectPool`; Qurious 判据收紧为 all-or-nothing。用户配置已还原, 实机确认
   `cfg migrated` 不再出现。
2. **`GenerationSettings` 的 `Nullable<self>` 布局环**: `private static GenerationSettings? _frozen`
   让加载器陷入循环布局依赖, 任何访问都 `TypeLoadException`。改为普通字段 + 显式 flag。

### 顺带修掉的既有错误断言

`tools/relic-eligibility-probe` 的"被动带 15% +- 3"断言**在 HEAD(f1c34cc)上就已失败**
(实测 10.97%, 本次 11.23%)。根因: 8 个被动词条里 6 个是 restriction, 只剩 2 个普通被动,
一局约 9 个被动带槽位消耗完后落到触发路径, restriction 分支仅 30% 触发 -> 有效值结构性
低于标称值.该断言已改为"不超过标称 15%" + "仍被填充(>= 8%)", 并在文档写明这是内容量限制.

**(后续更正, 见 WS-0919-08)**: 该下限当时取 8%; 去掉兜底重发牌后被动档实测降至 **6.04%**,
下限已改为 **5%**, 判据是"该档不会变空"而非钉住名义 15%.

### 验证(实测输出)

- `tools/relic-probe`: PROBE OK。含额外池**双向**可达性(关闭时从不被抽到; 开启时 200 seed 全可达)、
  权重改变抽中组合、同 (seed, settings) 可复现、时机规则无违规配对。
  **判别力验证**(故意破坏后确认会失败): `WeightOf` 退回常量 -> 2 项 FAIL;
  开关谓词短路 -> 1 项 FAIL; 规则 7 手牌子句短路 -> 1 项 FAIL。
- `tools/relic-eligibility-probe`: PROBE OK。规则 6/7 与独立 oracle 表逐对一致;
  短路规则 7 -> FAIL(并有非空泛性守卫)。
  为使该 oracle 继续可用, `GenerationSettings` 刻意不引用 BaseLib(配置经 `ConfigSource` 委托注入)。
- `tools/migration-probe`(Qurious 侧): PROBE OK, 含"外来 cfg 不被认领"。
- **实机**(`E:\Slay the Spire 2`, d3d12): 启动行 `relic pool: 165 extracted atoms,
  47 ledger-supported (+9 extra) ... seedVersion=relics-v8`; AAR 零异常, 9 个补丁类全挂载;
  5 个新配置项被 BaseLib 写入 cfg; 把开关置 True 后日志变为 `extraPool=True`, 证明
  配置 -> BaseLib -> `ConfigSource` -> `GenerationSettings` 全链路打通。
- **未覆盖**: 手牌效果在真实战斗中的实际生效(需玩家开局并打开额外池; 后台输入对 Godot 无效),
  MP 两端一致性。

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

> **后续更正 (2026-09-20, 见 WS-0920-01)**: 上面引用的 `check-vdf.py` 输出**当时就不可信** --
> 该检查器把 `\"` 当合法转义, 会对**导致 steamcmd 中止的文件**报 `balanced+paired: True`
> (已实证复现). 它已被按消费者语义重写, 不再硬编码版本前缀. 另外本节把"ASCII 引号"当作
> 唯一陷阱也不完整: **任何反斜杠**同样致命, 且超 **8000 字节**会让条目报 `Invalid Parameter`
> 并中止整个会话. 详见 WS-0920-01.

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

## 2026-09-18 缺陷: "遗物从来没有真正随机" -- 限制类词条每局固定 (SeedVersion v7)

### 用户报告 (Steam 版实测 + 工坊留言板多名用户)

- 用户: "东尼遗物从来没有被真正随机, 所有种子的遗物都是同样的."
- 工坊 `好咸的一条` (15 Sep): "同一个存档遗物永远是一批, 不管开多少吧, **遗物效果都没有重随**,
  而且全是负面的, 各种塞诅咒, 扣血上限, 失去所有金币."
- 工坊 `ikuseiso` (16 Sep): "我居然能看到**只有一个效果的遗物**: 每回合开始塞一个诅咒牌."

### 先证伪了错误的那一半主张

"所有种子的遗物完全相同" **不成立**. 决定性实验(离线 probe, 8 个真实种子):

- 同槽位**名字**跨种子重合率 **0.2%**; 每个 run 内 60 个名字**互不相同**(60/60).
- 8 个真实 run 合起来 480 个槽位里出现 **384 个不同定义**.
- 用户真实存档里的稀有度分布随种子变化(两档对比 33/60 槽位不同).
- 游戏日志里的真实种子 `1ZSDT8AZFNH9` 离线复现**逐字一致**(slot 4 提琴背包 / slot 24 鹿角颈圈).

所以生成器本身是种子相关的, 根因不在这里.

### 根因: 限制类词条池被每局抽干, 6 件固定遗物每局都出现

`PickPassives` 的 restriction 分支**只要还有未用过的 restriction 就必进**:

```csharp
if (pairs.Count > 0)          // v6 及以前
{
    var (restriction, offset) = pairs[random.Next(pairs.Count)];
```

而 restriction fragment **恰好只有 6 个**, 被动带却是 15% x 60 ≈ 9-15 个槽位. 实测 **68 个种子里
67 个把 6 个 restriction 全部抽完**. 又因为 v6 起名字是片段的纯函数, 且 6 个 restriction 全部
带先古 provenance, 于是**每局都出现同样 6 个名字 + 同样 6 条描述**:

```
= 8 个真实种子里都出现的描述 (v6) =
  Enemies gain 1 Strength when they enter combat and gain 1 additional Energy.
  Power cards cost 1 more and gain 1 additional Energy.
  You can no longer gain Gold and gain 1 additional Energy.
  You can no longer obtain potions and gain 1 additional Energy.
  You cannot play more than 6 cards each turn and gain 1 additional Energy.
  Card effects no longer draw cards for you and draw 2 additional cards on turn 1 of each combat.
```

名字层同样固定: "Antler Choker" / "Fiddle Preparation" 各自出现在 **8 个真实种子中的 4 个**.
这就是"遗物没有重随 / 永远是同一批"的直接来源 -- 玩家每局都会看到这几件.

### 修复: 给 restriction 分支加槽位级门控 (SeedVersion v7)

```csharp
if (pairs.Count > 0 && random.Next(100) < RestrictionRelicChancePercent)   // 30
```

`RestrictionRelicChancePercent = 30` -- 与被动的 15% 带同量级, 保留"限制类词条会出现"的设计,
但把**每局固定 6 个**改成**随种子变化的子集**.

### 验证 (全部实测)

- 探针 `PROBE OK`; `soak: every trigger fragment reachable` PASS;
  `soak: only max-HP-policy fragments unreachable` PASS.
- 68 个种子的 restriction 数量直方图: 修复前 `6->67 3->1`(67/68 都是满 6 个),
  修复后 `0->5 1->7 2->17 3->16 4->12 5->6 6->5`.
- **跨全部 68 个种子都出现的描述: 7/60 -> 0/60**.
- 两两平均共享描述 16.4%(修复前同类指标含那 7 条恒定项).
- 每个 run 内名字仍然 60/60 唯一; 指纹重复 0.
- 实机启动(副本 A `E:\Slay the Spire 2`, d3d12): 9 补丁类 0 失败, 池构成 156/47/26 与
  15 triggers/31 triggered/8 passives/5 benefits 不变, 0 崩溃, 0 AAR 错误.

### 顺带查清的两个非缺陷 (不得再误判)

1. **"重载后遗物相同"是正确行为**. 用户日志里 8 次 `continue_reload` / 4 次 `hard_reload`,
   整份日志只有 **1 次全新开局**(`Embarking on a singleplayer IRONCLAD run ... Seed: 1ZSDT8AZFNH9`).
   读档保留同一 seed 是设计契约("同种子 = 同一批遗物"), 不是 bug.
2. **同一 run 内描述重复是**既有现象, 非本次引入. 把门控置 100(等价修复前行为)复测:
   60 个种子里 56 个存在重复描述; 置 30 后 60/60. 因为 fragment 空间只有 59 个, 60 个槽位必然
   有复用(60 件遗物 > 44 个效果片段). 名字不同、描述相同 -- 属设计取舍, 未在本轮改动范围.

### 未闭环 (诚实标注)

- **实机开局验证未做**: 后台点击/键盘对 Godot 无效, 且游戏无任何控制台命令能开局, 需要用户在场.
  本次只验证到"启动 + 池构建 + 补丁挂载"; 每局限制类词条子集变化由 68 种子离线测量覆盖.
- 工坊反馈里"很多遗物没有正常生效"在本轮全部日志里**没有对应异常**(`skipped: no combat context` /
  `failed; continuing` / `unsupported ...` 计数均为 0), 未定位到具体案例; 若复现需玩家提供种子.
- "全是负面"未改动: 负面占比由用户 2026-09-17 指令(取消权重加成 + 均匀抽取)确定, 本轮不擅自变更.

## 2026-09-18 追加: advisory 逐条验证 + 补上"多样性"回归断言

### 先说结论: 有一条 advisory 的关键事实主张是**错的**

某条 advisory 称"新 godot.log 里已有一局由你刚部署的 DLL 生成,种子 `7J31K8TQGN83`,
6 个 restriction -- 这是 v7 门控生效的唯一实机证据". **该日志不是我的部署**:

- 该 `godot.log`(13:03,985KB)只出现 `G:\steam` 路径(166 次),**0 次** `E:\Slay the Spire 2`;
  加载的是 **Steam 版 0.1.7**(`Steam version (v0.1.7) is greater than local version (v0.1.6)`),
  即 **v6**,日志中 `relics-v7` 出现 **0 次**.
- 我的 E: 部署那次是 `godot2026-09-18T12.24.28.log`(E: 路径 136 次,`G:\steam` 0 次).
- **但这条 advisory 给了一个更好的测试**: 用离线 probe 在 v6 下复现该种子.结果**逐槽位完全一致**:

```
离线 v6 复现 seed 7J31K8TQGN83 (与 Steam 日志逐字相同):
  slot 14 [Uncommon] "Gauntlet Antler"  / "手甲鹿角"
  slot 16 [Common]   "Antler Basin"     / "鹿角水盆"
  slot 19 [Rare]     "Choker Antler"    / "颈圈鹿角"
  slot 30 [Uncommon] "Antler Coin"      / "鹿角钱币"
  slot 35 [Common]   "Ectoplasm Antler" / "外质鹿角"
  slot 40 [Common]   "Preparation Coin" / "背包钱币"
```

这**独立证实了整条链路**(种子捕获 -> 生成 -> 本地化注入)在 Steam 版上就是 v6 的行为,
且"6 个 restriction 全出现"正是 v6 缺陷的现场.**重要运营事实**: 该日志同时证明**用户当前仍在 Steam 版 0.1.7(修复前)游玩** --
所以在 0.1.8 推送到工坊之前, 用户后续的实机反馈**仍会复现该缺陷**, 这不是"修复无效".
0.1.7 的变更说明里那句"读档保留同批遗物"在跨版本时也不成立(见下文 (5)).

附带教训: 我因此把 `SeedVersion` 加进了
MainFile 的初始化日志(`seedVersion=relics-v7`),**日志从此可归因到具体构建**.

### 三条**成立**的 advisory (已处置)

**(1) 探针缺"跨种子多样性"断言 -- 这是缺陷漏网的直接原因.已补.**

原有断言全部通过,却放过了缺陷: 确定性("同种子逐字节一致")、唯一性("每局 60 个名字互不相同")、
**可达性**("200 种子里每个片段都可达") -- 这三者对一个"每局输出固定集合"的生成器**全部满足**.
可达性是**相反**的性质.新补 4 条断言(用**用户 8 个真实种子**,不是合成种子):

- `variety: no relic description is present in every seed`
- `variety: no relic name is present in every seed`
- `variety: most seeds draw only a subset of the restriction pool (not all of it)`
- `variety: no two seeds share most of their relic descriptions`

**并已实测它们在修复前的行为**(把代码临时回退到 v6): **只有 2 条真正判别本缺陷**, 另 2 条在 v6 下也 PASS --

```
FAIL  variety: no relic description is present in every seed  7 constant: Power cards cost 1 more...
FAIL  variety: most seeds draw only a subset of the restriction pool   1/72 (1%) below the pool size 6
PROBE FAILED: 2 check(s)
```

| 断言 | v6(修复前) | 判别力 |
|---|---|---|
| `no relic description is present in every seed` | **FAIL** (7 条恒定) | **判别** |
| `most seeds draw only a subset of the restriction pool` | **FAIL** (1/72) | **判别** |
| `no relic name is present in every seed` | PASS | 不判别 |
| `no two seeds share most of their relic descriptions` | PASS | 不判别 |

后两条在 v6 下也 PASS, 原因: 名字的 tail 词素本来就随种子变化(v6 的名字层只有**部分**被固化,
固化的是 6 条 restriction 文本对应的那几件), 而两局共享描述在 v6 下平均 13.6/60(最差 18/60, 实测 8 真实种子), 远低于 35 的阈值.
**必须写明这一点**: 若后人以为"名字断言"能拦住这类缺陷, 就会把一张拦不住的网当成回归护栏
(AGENTS.md Sec 9 的诚实要求). 修复后 4 条全 PASS.

**其中一条断言我自己先写错了, 已修正(记下来)**: 初版写的是 `restrictionCounts.Max() < 6`,
即"不允许任何种子拿到 6 个 restriction". 这是**假不变式** -- 6 是 v7 的**合法**结果
(68 种子直方图里 `6->5`), 它当时通过纯粹是因为那 8 个真实种子恰好 max=5. 一旦扩池/改账本
使这 8 个种子的流发生位移, 它就会**假报警**, 把人指向门控而实际什么都没坏.
改成"**多数种子不取满整个 restriction 池**"(`belowPoolShare >= 0.5`), 在更宽的 72 个种子上度量:
v6 = 1/72 (1%) -> FAIL, v7 = 63/68 (93%) -> PASS. 这才是能区分修复与未修复的判据.
教训: 断言必须写在**分布**上, 且不得把某个**合法取值**排除掉.

**(2) 效果词表跨种子高度复用 -- 成立,但**不是**本轮缺陷,且不是代码能修的.**

实测(9 个真实种子): 每局效果并集 **29-34/44**(66-77%),两局之间平均共享 **26.9/44 = 61.2%**;
trigger-kind 并集 **12/12**(每局都覆盖全部触发类型).所以"遗物效果感觉都见过"是**真实感受**,
根因是**片段池只有 44 个效果片段**而每局要抽 ~78 次.
- 减少 `SlotCount` **无用**(advisory 已自行更正): 20 件 x 1.3 ~ 26 次仍覆盖 ~43%.
- 真正的杠杆只有两个: **扩账本**(数据工作量大)或**每局片段子集化**(改代码).
- **本轮不做**: 属设计取舍,且改动会触及用户 2026-09-17 两条指令(负面均匀抽取 / 5% 槽位级门控).
  已记入未闭环,留给用户决策.

**(3) 收藏册/无 seed 场景下 60 件遗物全部显示同一条兜底文本 -- 机制成立,属家族级既有现象.**

`NRelicCollectionCategory.LoadRelics`(`engine-dllsrc/.../NRelicCollectionCategory.cs:154`)按
`ModelDb.AllRelics.Where(r => r.Rarity == relicRarity)` 过滤;局外 `Definition` 为 null,
`Rarity` 退化为 `Common`、`Localization` 返回 "Generated Relic / A relic generated by the Anthony
algorithm." -- 60 件全同.
**但 Qurious(同一作者、更成熟的姊妹 mod)是同一套写法**(`ChaosRelicModel.cs:87` 同样的
`Definition?.Rarity ?? Common`,`:106` 同样的 `"Chaos Relic " + (Slot + 1)` 兜底),
故这是**家族级既有设计**,不是本轮引入.已记入未闭环.

**(4) `AnthonyRelicLocUpdater._lastKey` 在 run end 未复位 -- 成立,已修.**

`LocManager.SetLanguage` 整体替换表集合,注入条目会丢,而 `_lastKey` 仍匹配 -> 同 seed 再次捕获
会短路.`RunCleanUpPatch` 现在调用 `AnthonyRelicLocUpdater.OnSeedCaptured(null)` 一并清掉
(`OnSeedCaptured(null)` 本身就把 `_lastKey` 置 null).

**(5) 变更说明必须写明 SeedVersion 变更会导致存档重生成 -- 成立,已改.**

原文只写"读档保留同批遗物",跨版本是**错的**: `SeedVersion` 是注册表缓存键的一部分,
0.1.7 的存档在 0.1.8 首次加载时 **60 件定义会重新生成**(新名字+新效果).
v0.1.8 changenote 已显式写明这一点,并说明版本内读档才保持同批.

### 不成立 / 无需处置的 advisory

- **"日志证明 v7 已生效"**: 错,那是 Steam 版 v6(见上).
- **"图标按槽位固定,所以玩家看到 60 张相同图"**: 属实(60 个 `anthony_relicNNN.png` 确实各不相同,
  但跨局是同一套 60 张).属视觉表现,本轮不改;若要改需按 provenance 派生图标.
- **"降低 SlotCount 可修"**: 提出者已自行更正,无需处置.
- **"可能反转了用户 2026-09-17 的指令"**: 本轮**没有**动负面权重(仍是均匀)也没动
  `BenefitRelicChancePercent = 5`(仍是槽位级门控);新增的 `RestrictionRelicChancePercent`
  是**新的**槽位级门控,方向与既有两条一致.

### 本轮补充的验证(全部实测)

- 探针: `PROBE OK`,含 4 条新多样性断言;并已证明其修复前 FAIL.
- 8 个真实种子的修复后数字(与修复前 apples-to-apples):
  - 每个种子都出现的描述: **7/60 -> 0/60**
  - 每个种子都出现的**名字**: **0/60**(名字层也已打散)
  - restriction 数量: `[6,6,6,6,6,6,6,6]` -> `[0,3,5,1,2,5,3,0]`
  - 两两平均共享描述: **7.7/60 = 12.9%**
  - 每局 distinctDesc 55-58/60(60 槽位 vs 44 效果片段的必然复用,非缺陷)
- 实机启动(副本 A, d3d12): 9 补丁类 0 失败, 池构成不变,
  **`initialized: enabled=True, replaceVanilla=True, seedVersion=relics-v7`** -- 日志可归因.

## 2026-09-18 v0.1.8 已推送工坊 (缺陷闭环)

### 结果

```
[13:53:37] Upload starting for workshop item 3801304033
[13:53:53] Uploaded new content ( ManifestID 7221203532627958162 ) for item 3801304033
[13:53:54] Upload finished for workshop item 3801304033 : OK
```

工坊 changelog 页已出现 `v0.1.8`(此前为 `v0.1.7`),变更说明含
"the descriptions present in EVERY run went from 7 of 60 to 0" 与跨版本重生成提示.

推送前的三次前置门禁全过:
```
REFRESH RESULT: OK (0 refreshed, 8 already current)
Release-content guard: no held-back layer in any staged payload.
VDF metadata guard: no unescaped ASCII quote in any text field.
```

推送用的二进制已核验为 **v7**(三个副本 UTF-16 计数一致, 哈希 `902241e4d00e71ab`):
```
build    902241e4d00e71ab  v7=2  v6=0  seedVersion=1
payload  902241e4d00e71ab  v7=2  v6=0  seedVersion=1
E: copy  902241e4d00e71ab  v7=2  v6=0  seedVersion=1
```
时间线亦自洽: 源码最后一次 v7 恢复 13:50:02, 重建 13:50:12, 推送 13:53:37.

### 推送过程中发现并修掉的两个**既有**脚本缺陷

**(1) 单元素 `Where-Object` 没有 `.Count`, 导致单推必然误报失败.** `workshop-push-all.ps1`:

```powershell
$okCount = ($results | Where-Object { $_.Ok }).Count     # 单元素时返回标量
```

实测: `($w).Count` 为 `$null`, 而 `$null -lt 1` 为 **true** -> 一次**成功**的单推会打印
`/1 items verified.`(`{0}` 为空)并 `exit 5`. 8 项全推时 `Where-Object` 返回数组, 所以这个
bug 一直没暴露. 已改为 `@(...).Count`. **这是既有缺陷, 非本轮引入.**

**(2) `-Only` 新增过滤器需要防歧义.** 本轮为它加了 `-Only <substring>`(只推匹配项, 其余 7 项
在单 mod 轮次里纯属浪费 2FA 码与暴露窗口). 已加固为**必须恰好命中 1 项**:
命中 0 项时 `$pushItems.Count` 为 0, 旧写法下 `$okCount -lt $pushItems.Count` 即 `0 -lt 0` 为假,
会打印 "0/0 items verified." 并 `exit 0` -- **看着像成功, 实际什么都没推, 白烧一个码**.
现为 `-ne 1` 则 `FATAL` + `exit 2` 并列出全部行名.
三道前置门禁**仍覆盖全部 8 项**, 过滤器只收窄上传列表; 校验循环 / 通过计数 / 退出码均跟随过滤后的列表.

实测选择逻辑: `-Only AAR` / `3801304033` / `0.1` 各命中 1 项; `-Only relics` / `NOPE` 命中 0 项 -> FATAL.

### 关于 2FA 的两条事实(已实测)

- **steamcmd 无缓存会话**: `+login <acct> <pass> +quit` 输出
  `This account is protected by a Steam Guard mobile authenticator. Waiting for confirmation..` 后
  `exit 5`. 该账号**必须**每次提供新的 Steam Guard 码.
- 第一次带码推送(`VT2HT`)因我方回合超时**在登录阶段被杀**, 未上传任何内容
  (`workshop_log.txt` 无新增 Upload 行) -- 此后改为后台任务运行, 第二次(`V2RG3`)成功.


## WS-0919-06 避免无效效果 (规则 9)

**用户指令**: "增加一个选项, 使遗物效果不会无效(如战斗结束给敌方减益, 伤害)或者非战斗中给予能量, 增益, 减益."

### 先测再写: 638 对死配对

写代码前先枚举 200 个种子 x 全部遗物 x 全部效果, 判定 (触发时机, 效果 opcode) 是否可能
产生可观测结果. 用户举的两个例子**已被既有规则 4 覆盖**(规则 4 无条件拒绝"非战斗状态下
需要战斗的效果"与"战斗结束时对敌方的效果" -- 那时敌人已全死). 真正的洞是第三类:

**触发在战斗结束后 x 效果作用域是本场战斗, 且目标是自身** -> **638 对 / 200 种子**,
约每局 3 件死遗物. 实例 `combat_end x gain_block`(战斗已结束, 格挡已清),
`combat_victory x apply_power/thorns/self`(Power 随战斗状态丢弃).

### 实现

- `GenerationSettings.DisableIneffectiveEffects` -- 字段 + `Key` 第 3 bit + `FreezeExplicit`
  参数 + `Default`.
- `AutoAnthonyRelicsConfig.DisableIneffectiveEffects { get; set; } = **true**`(默认**开**).
  与其它"删内容"选项相反的理由: 死效果是**缺陷**不是内容, 玩家不会"就想要"一个什么都不做的
  遗物, 所以默认保证; `DisableNegativeEffects` 删的是真内容, 默认关.
- `RelicGenerator.Excluded` **规则 9**:
  `DisableIneffectiveEffects && trigger != null
   && TriggersAfterEnemiesAreDead.Contains(trigger.Kind)
   && EffectFragment.CombatScopedOpcodes.Contains(effect.Opcode)`.
- `MainFile.cs` ConfigSource 传第 7 参; 日志加 `disableIneffective=`.
- loc 6 条(eng/zhs). zhs 用 `\u300c` 转义, 因为中文描述里的 `"` 会破坏 Python 字面量
  (本轮踩了一次, 见下).

### 规则 9 可关 / 规则 4 不可关

规则 4 拒绝的是**结构性死文本**(效果指向已死敌人, 描述本身就是错的), 无开关.
规则 9 拒绝的是引擎 no-op(文本没错, 只是该时机无事可做), 给开关.

### 不 bump `SeedVersion`

`SeedVersion` 参与 RNG 流字符串(`RelicGenerator.cs:506`). bump 会重掷**所有**局,
包括选项为关(行为与旧版逐字节相同)的局 -- 白白破坏玩家的种子记忆.
选项已通过 `GenerationSettings.Key` 进定义缓存键与 loc 去重键, 切换时正确重生成.
保持 `relics-v8`.

### 踩到的坑

1. **`GenerationSettings.Default` 与 config 初值不一致**: 最初写 `disableIneffectiveEffects: false`,
   而 config 属性初值 `true`. `Default` 是离线探针未绑定 `ConfigSource` 时读到的值 ->
   探针测的是玩家拿不到的配置, 规则 9 在探针里等于没测. 已改 `true` 并在 `Default` 上写明
   "MUST track AutoAnthonyRelicsConfig's initializers". `relic-probe` 的
   `GenerationSettingsTest.Set` 的 `disableIneffective` 缺省值同步改为 `true`.
2. **Python heredoc 里的中文引号**: zhs 描述含 `"..."`, 直接放进 Python 字符串字面量 ->
   `SyntaxError`. 改用 `\u300c`/`\u300d` 转义.
3. **非空性断言必须有**: 只断言"开着时死配对为 0"是不够的 -- 一个把一切都排除的回归也会通过.
   加了"关着时必须 > 0"与"combat_end/combat_victory 触发仍可达"两条.

### 验证 (全部实测)

- `relic-probe`: 3 条新断言 PASS, `PROBE OK`.
- **判别力**: 规则 9 判据替换为 `&& false` -> 重建 -> 断言 2 立即 FAIL 并报出
  `combat_end x gain_block/immediate/self | combat_victory x gain_block/immediate/self |
  combat_end x apply_power/strength/self | combat_victory x apply_power/thorns/self`. 已还原.
- `relic-eligibility-probe`: `PolicyExcluded` 加规则 9(读**同一批集合**而非重抄 opcode 列表),
  `PROBE OK`, 含可达性断言.
- **实机**(测试副本 A, `E:\Slay the Spire 2\`, d3d12 + VeryDebug):
  - `relic pool: 165 extracted atoms, 47 ledger-supported (+9 extra), 26 ledger-rejected;
    fragments: 15 triggers, 40 triggered effects, 8 passives, 5 benefits`
  - `initialized: enabled=True, replaceVanilla=True, extraPool=False, disableNegatives=False,
    **disableIneffective=True**, weights(triggered/passive/benefit/extra)=100/100/100/100`
    -> 选项**确实以出厂默认 True 抵达生成器**(配置键缺失时走属性初值, 实测有效).
  - `Harmony: 9 patch class(es) applied, 0 failed`; **AAR 自身 ERROR 数 = 0**.
  - `[QuriousCraftingRelics] cfg migration skipped: AutoAnthonyRelics.cfg does not match the
    Qurious legacy schema` -> 跨 mod 配置键碰撞的修复仍然有效.
  - 游戏退出后复查 cfg: 新键 `"DisableIneffectiveEffects": "True"` 已由 BaseLib 写回,
    其余仍为出厂默认(`False`/`False`, 权重全 `100`).
  - **日志里唯一的 `[ERROR]` 属于另一个 mod**: `[AutoAnthony] Startup initialization failed...
    Expected 65 complete v111 Colorless cards, found 77` 来自 `AutoAnthony.ChaosRunDefinitions`
    (混沌 mod), 与 AAR 无关. 已用 `grep 'ERROR.*AutoAnthonyRelics'` 计数确认 = 0.

### 未覆盖的验证缺口

- 真实战斗中"死遗物消失"的**肉眼确认**需用户开局(后台输入对 Godot 无效).
- **MP 两端一致性**未实测.


## WS-0919-07 金币与药水栏位仅在拾起时 (规则 10)

**用户指令**: "获得金币和药水栏位只能在拾起时, 否则强度会极高."

### 先测再写

枚举 200 种子 x 全部遗物, 找"触发 != `obtained` 但给金币/药水栏位"的配对, 实测存在:

```
block_cleared x gain_max_potion | combat_start x gain_gold | combat_end x gain_max_potion
turn_start_early x gain_max_potion | turn_start x gain_max_potion | combat_victory x gain_max_potion
```

`turn_start x gain_max_potion` 即每回合一个药水栏位. 确认是真实缺陷.

### 实现

`RelicGenerator.Excluded` **规则 10**:

```csharp
|| ((trigger is null || trigger.Kind != "obtained")
    && (effect.Opcode == "gain_gold" || effect.Opcode == "gain_max_potion"));
```

无条件(无开关), 与规则 2 同形. 依据: 金币/药水栏位是永久货币, 引擎自己的三个原子
(`OldCoin`, `PotionBelt`, `PhialHolster`)源触发全部是 `obtained` -- 已查
`mod/Code/Data/Json/relic_atoms.json` 确认.

**规则 1 被包含**: 规则 1 的 `gold_gained x gain_gold` 中 `gold_gained != obtained`,
已被规则 10 覆盖. 规则 1 保留(陈述的是递归边界这条不同不变式), 已在代码注释中写明.

### 验证

- `relic-probe` 2 条断言 PASS: 无非 `obtained` 的金币/药水配对; `obtained` 仍可拿到(非空性).
- **判别力**: 规则 10 插入 `&& false` -> 断言 1 FAIL 并报出 6 种具体配对. 已还原.
- `relic-eligibility-probe`: 规则 10 进 `LegacyExcluded`(与规则 2 同段). PROBE OK.
- **未实机验证**(游戏已退出, 且此规则是生成期过滤, 需新开一局才能看到 60 件中的差异).

### 顺带修正上一条报告的错误说法

上一条报告写"要恢复出厂默认就把两个开关都关掉" -- **错误**. 出厂默认是
`DisableNegativeEffects=false` 但 `DisableIneffectiveEffects=**true**`. 正确说法是:
负面效果开关关掉, 避免无效效果开关**保持开启**.


## WS-0919-08 被动/增益档耗尽不再重发牌 + SeedVersion v9 + 全 mod 标注测试中

### SeedVersion v8 -> v9

规则 10 是**无条件**的, 所以**改变默认产出**, 必须 bump. 判据在 v8 注释里已写明:
v8 不为选项 6/8/9 bump, 理由是那些选项在**默认配置下恒假**(bump 属无谓重掷);
规则 10 恒真, 该理由不成立. 先例: v3(`gain_max_hp` 钉在 `obtained`)与 v4 都是同形改动并 bump 了.

**顺带核实**: 生成的定义**不落盘** -- `AnthonyRelicRunRegistry` 是进程内 `Dictionary` +
`Queue`, 由 `ResetForRunEnd` 清空, 无序列化. 所以 bump 不是为了清磁盘旧数据, 而是因为
`SeedVersion` 参与 RNG 流字符串, 同种子的 v8 与 v9 是两次不同的抽取.

### v9 位移暴露的被动/增益档饱和 (真实缺陷)

bump 后跨种子多样性断言失败: 2 条描述在 8/8 种子中恒定(`extra_turn`, `modify_hand_draw -2`).

**因果分离**: (1) 改回 v8 -> PASS; (2) 保持 v9 但规则 10 短路 -> **仍 FAIL**.
故与规则 10 无关. 但"v8 绿 v9 红"不能证明有缺陷 -- 也可能是断言不稳健, 所以**先测量**:

| 配置 | 200 种子最高频 | >=95% 的描述 |
|---|---|---|
| 带回退 | **196/200 = 98%** | **2** |
| 去回退 | 121/200 = 60.5% | **0** |

决定性. 根因: 两档的 `?? PickEligiblePassive(..)` 兜底**忽略去重集合**, 重发已用片段.
被动档 8 片段(6 限制 + **2 普通**, 均为 `modify_hand_draw/passive`), 增益档 5 片段
(`extra_turn`/`modify_max_energy`/`retain_hand`/`enchant_reward`/`expand_card_pool`).
被钉住的两条正是**每档各一条**. 两档片段数都远小于每局进入该档的次数.

### 修法

去掉两处回退, 耗尽时**下落**(增益 -> 被动 -> 触发). **例外**: 若下落目标档被玩家关掉
(`WeightXxx <= 0`)则仍重发 -- "权重 0 关闭该档"是对玩家的承诺. 被动档守卫判
`WeightTriggeredCore <= 0`; 增益档守卫判 `WeightPassiveCore <= 0 && WeightTriggeredCore <= 0`.

**注意**: 初版只给被动档加了守卫, 增益档漏了 -> 存在"增益档开, 被动+触发都关"时
仍产出触发型遗物的漏洞(正是上一轮 advisory 指出的). 已补齐并加**成对**零权重测试.

### 顺带修正

1. 断言换成**统计稳健**判据: 原取 8 种子交集要求为空, 而单条命中 8/8 概率约
   `0.57^8 = 0.9%`, 60 条期望约 0.5 条 -- **靠运气**. 改为 200 种子 + 95% 普及率阈值,
   并打印峰值(实测 60.5%, 余量充分).
2. `relic-eligibility-probe` 被动档下限 8% -> 5%(修复后实测 6.04%, 受内容容量限制).
3. 工坊说明"100/100/100/100 即默认 80/15/5"过时 -> 实测 **89.17/6.04/4.78**(200 种子
   12000 槽位), 已改为"80/15/5 是名义比例 + 给出实测与原因".

### 判别力验证

增益档守卫短路为 `false` -> 重建 -> `triggered+passive` 用例 FAIL 并报出
`benefit/passive/triggered = 400/0/4400`(被动档关闭后仍产出 4400 个触发型档位). 已还原.

### 全 mod 标题与介绍标注"测试中"

依据各 mod manifest 的 `affects_gameplay`: AAR/Qurious/Spire1/MpConfigSync/Perfect/
HeartShake/ChaosBridge 共 7 个标注; FastBoot 为 `false`(纯启动加速)**不标注**.
标题追加 ` [测试中]`, 介绍在 `[h1]` 块后插入中英双语横幅.

**踩坑**: VDF 值里用**真实换行**(合法), 我第一版序列化器把它转义成 `\n`, 导致 7 个文件
round-trip 全部不一致; 改用最小侵入的单行正则替换(只动 title)与 `[h1]` 后插入
(只动 description), 未触碰其余字节.

### 补记 (2026-09-20 04:5x): 阈值与文案再修

1. **近普及阈值 95% -> 80%**: 95% 离它要抓的缺陷(实测 96% / 98%)只有 1 点余量, 而健康峰值
   是 60.5%. 更轻的回归或另一次版本位移落在 94% 就会**静默通过**. 改为按**实测健康分布**
   取 80%, 两侧各留约 20 点余量. **判别力复验**: 把两处守卫短路回旧行为 -> 立即 FAIL 并报出
   `192/200` 与 `196/200`. 已还原.
2. **changenote 仍有过时说法**: 描述行已改, 但 changenote 里"(3) ... 100/100/100/100 gives the
   default 80/15/5 split" 未改. 已改为"权重决定各档被**抽取**的比例(ROLL);80/15/5 是名义值,
   因被动/增益档片段少且耗尽后落档, **实测**约 89/6/5".
3. **确认 payload DLL 与构建一致**: 一度担心 staging 是"加守卫之前"的旧 DLL(该文件自守卫
   加入后已变更多次). 复核 md5: 构建产物与 staging 一致(`bc2b5316..`), 已随本次刷新更新.

## WS-0920-01 工坊推送：四个根因、一次限流事故、8/8 成功

**结果**: 8/8 全部 `True`(`[2026-09-20 06:01:26]`),含此前反复失败的 AAR(3801304033,
ManifestID 2259583100286860837)与 ChaosBridge(3801304105)。AAR 首次真正带上 v0.1.9 内容。

### 本条目存在的原因

本轮从 04:39 到 06:01 共 **14 次**推送尝试,只有最后一次成功。四个根因**互相掩盖**,
每次修掉一个才暴露下一个;而且**每个根因的代价都不对称** -- 有的静默截断、有的硬失败、
有的连累用户。这些无法从代码重新推导,必须记录。

---

### 根因 1(最严重):`\"` 会让 steamcmd **拒绝解析整个 VDF**

**症状**: AAR 报 `Invalid Parameter`,而其**前面 6 项全部 Success**。

**真相**(`console_log.txt`):
```
CKeyValuesSystem::AddStringToPool: key name too long (1165 chars)
RecursiveLoadFromBuffer: got } in key in file workshopitem [offset: 14009]
ERROR! Failed to parse build config file "..workshop_upload.vdf"
```

**steamcmd 的 KeyValues 解析器不解析反斜杠转义**。`\` 是普通字符,其后的 `"` 仍然
**终止字符串**。于是 `\"拾起时\"` 被解析成"值在此结束",剩下的 `拾起时\` 变成**键名**。

**谁引入的**: 我在更早一轮把描述里的 ASCII 引号"规范化"成 `\"`,理由是"VDF 转义约定" --
**这是错的**。正确做法是 CJK 引号 `U+201C/U+201D`,无需转义。

**为什么两个"验证"都给了假通过**:
- 推送脚本的引号守卫注释明写 `\" escapes; those are fine`,且 `Get-Content` 逐行匹配
  **只读多行值的第一行**;
- `workshop/check-vdf.py` 同样把 `\"` 当合法转义,对**会导致中止的文件**报
  `balanced+paired: True`(已实证复现)。

**不对称的代价**:
| 情形 | 后果 |
|---|---|
| 截断后"键名"较短 | **静默截断**,steamcmd 照样报 OK |
| 截断后"键名"过长 | **硬失败**,整个会话中止,后续条目全不推 |

**线上可见损坏**: Spire1(3799031900)的已发布变更记录**确实被截断**在
`(2) 术语更正:Frail=脆弱(原误作\` -- 本地同一句后面还有整段.所以"6/8 成功"
**不等于**"6/8 正确发布".

> **后续更正 (2026-09-20 已修复, 见 Spire1 的 WS-0920-01)**: 该截断**已修复**.
> Spire1 先后发布 1.2.1(修复截断, 重新发布完整 v1.2.0 原文)与 1.2.2(修正 1.2.1 误用的
> Markdown 星号). 抓**原始 HTML** 核实: v1.2.2 条目 `<b>` 标签 3 个, 字面 `**` 0 个.
> 两点教训: 变更记录**追加式且不可编辑**, 1.2.0 的历史条目**无法修复**;
> 且修复必须 **bump 版本号**触发内容变更, 否则只会得到 `No content change detected`.

**修复**: 8 个 VDF 的 `\"` 全改 CJK 引号;守卫改为**任何反斜杠即 FATAL**;
`check-vdf.py` 按消费者语义重写(双向验证:致命文件 FAIL,真实文件 OK)。

---

### 根因 2:描述超过 Steam 的 **8000 字节**上限 -> `Invalid Parameter`

Steamworks `isteamremotestorage.h`:
```
k_cchPublishedDocumentTitleMax            = 128 + 1
k_cchPublishedDocumentDescriptionMax      = 8000
k_cchPublishedDocumentChangeDescriptionMax = 8000
```

AAR 描述在规则 9/10 与权重文档那几轮里膨胀到 **11151 字节**(上次成功时 6576)。
**超限不是截断,而是该条目失败并中止会话**,所以排在 AAR 后面的 ChaosBridge 也没轮到。

**关键**: 上限是**字节**不是字符,而这些字符串多为中文(UTF-8 3 字节/字符)。
已重写为 7946 字节,并加**字节上限守卫**(按 `Encoding.UTF8.GetByteCount`)。

---

### 根因 3:登录限流是 **IP 级**,我因此锁住了用户

我在 ~25 分钟内重试 **9 次**,错误从 `ERROR (Timeout)` 升级为
`ERROR (Rate Limit Exceeded)`,导致**用户自己的 Steam 客户端都登不上、无法玩游戏**。
用户实测确认:**换 IP 即可恢复**,所以限流是 **IP 级**(我早先写"account-wide"是错的)。

**我犯的错**: 确认窗口没赶上时**立刻重试**而不是等待;被限流后**又推了一次**,加剧问题。

**结构性防护(三层)**:
1. **事前冷却守卫**: 判据取 `console_log.txt` 里最后一条 `Logging in user` 时间戳
   (**不是**只靠新写的标记文件 -- 否则守卫在文件存在前**形同虚设**,而"验证守卫"
   最自然的做法就是跑脚本,那次会真的发出登录)。`< 30 分钟` 直接 `exit 8` 且
   **不启动 steamcmd**;`-Force` 可绕过。
2. **事中识别**: 遇 `Rate Limit Exceeded` 立即 `exit 7` 并提示等待 15-30 分钟。
3. **`-GuardsOnly`**: 跑完全部预检后退出,**不读凭据、不启 steamcmd、不消耗冷却**。
   之前守卫只能在登录之后才可达,所以我三次用 Python 临时重实现守卫逻辑 --
   重复逻辑本身会漂移,正是本轮关掉的那类问题。

**注意**: 冷却守卫测的是**本机历史**,不是 Steam 的真实状态。用户**换 IP 后**新 IP
并未被限流,但守卫仍会拒绝满 30 分钟;此时 `-Force` 才是正确的绕过。

---

### 根因 4:2FA 码**必须在启动时给出**

脚本**没有交互式 2FA 提示**:码只从 `--2FACode` 读取并拼进 `+login` 行。
所以"先启动、再报码"**必然失败** -- 会发出无码的 `+login`,退化成手机确认赛跑。

**正确顺序**: 用户先读出 5 位码 -> 报给操作者 -> 操作者在 ~30 秒有效期内
`--2FACode <码>` 启动。

**同时证伪**: 脚本头部原写"首次成功登录后缓存 sentry,后续无需码"。实测
`find` 全盘搜 `ssfn*` **一个都没有**;每次成功登录走的都是手机确认或新码。已改写。

---

### 本轮同时修掉的其它缺陷

- **描述写错替换范围**: 曾写"替换含先古遗物"。实际 `relic-bagPatch.cs` 明写
  `Starter / Event / Neow / Ancient pools stay vanilla by design` -- **先古遗物完全不动**,
  只有其**效果**被采集进词条池。
- **描述漏掉两个总开关** `Enabled` 与 `ReplaceVanillaRelics`,玩家无法得知可关闭本 mod
  或与原版混合掉落。已补回,并补回稀有度分布/片段清单/30% 双效果/数据忠实度示例。
- **两个凭记忆写错的译名**: "水银玉猫袋"/"弯刀" -> **弹珠袋**/**金刚杵**(本 mod
  `RelicText.cs` 与权威转储一致,且错名全工作区仅此两处)。
- **稀有度数字写错**: 描述写 20/21/19,而生成器按 `Next(100)` 的 45/80 掷点。**实测**
  (200 种子 / 12000 槽位)为 **26.7/21.0/12.3** -- 稀有是最**小**档而非第二大。
  已改为 27/21/12,并加**常驻断言**(占比落在 45/35/20 ±3;且 Rare 必须最小),
  判别力已验证(阈值改 34/69 立即 FAIL)。
- **"逐字节一致"原先无证据**: `Fingerprint` 只覆盖稀有度/触发/效果/数值,**不含**
  name 与 description 文本。已补断言逐槽比较四个可见字符串。
- **`description.txt` 与 VDF 漂移**: txt 是同一文本的第二份拷贝且**无人读取**,
  只改 txt 会让守卫全 PASS 而发出旧文本。已加**一致性守卫**(不匹配 `exit 9`)。
- **`-GuardsOnly` 未满足自身契约**: 凭据块排在退出之前,无环境变量时会 `Read-Host`
  挂起。已用 `if (-not $guardsOnly)` 包住,并在**清空凭据**下实测 4.6 秒返回。

### 教训(下次直接照做)

1. **改动 VDF 文本后立刻跑 `-GuardsOnly`**,不要靠肉眼或临时脚本。
2. **失败后不要立刻重试**。先读 `console_log.txt` 定位真实错误,再决定是否重试。
3. **限流期间不要再推**,并优先确认用户自己的客户端是否可用。
4. **2FA 码先要后启**,顺序不能颠倒。
5. **描述有字节预算**: 上限 8000 字节,中文按 3 字节/字符估算。
