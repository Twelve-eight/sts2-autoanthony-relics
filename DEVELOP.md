# AutoAnthonyRelics - 真正的东尼算法-遗物

## 2026-09-22 用户最新范围: 停止维护

用户裁定: 东尼遗物与怪异炼化遗物定位重叠, 舍弃东尼遗物. 本项目 `G:\omp works\Sts\sts2-autoanthony-relics` 自即日起停止维护, 不再进行功能开发, 代码审查, bug 修复, 构建, 部署或 Workshop 发布.

继续维护的是 `G:\omp works\Sts\AutoAnthonyRelics` 下的 `QuriousCraftingRelics` 怪异炼化遗物. 两个目录不能混淆.

停止维护不等于删除: 本仓库, Git 历史, 既有未提交变更和存档参考全部保留. 本轮没有移动源码, 卸载 mod, 修改 Steam 安装或共享 AppData 配置. 下方文档保留为历史设计与验证记录, 不构成继续执行的待办.

忠于原版 **Auto-Anthonyology** 机制的重写版: 把原版卡牌集离线拆成原子片段目录,
再按 `(角色, 种子)` 从**同一个池子**里重采样, 重新拼装出一整套新卡池。
条件(触发)与效果在数据层就是同一池子里的两类**独立**片段, 只在拼装末尾用运行期
`triggerIndex` 事后绑定 —— 所以任何条件都能挂到任何合法效果上。这就是"打乱条件"。

## 当前产品状态 (2026-09-12 夜, 垂直切片)

**本 mod 现在输出遗物** (用户裁定 D8/D9): 从引擎 300 个遗物类提取的 140 条候选原子,
经 `research/relic_atom_ledger.json` 账本 (deny-by-default, 25 supported / 26 rejected,
每条带证据) 过滤后拆成独立触发/效果片段池; 生成器每局种子确定性生成 60 件遗物
(定义 = 纯函数 (modId, 版本, seed, slot), 配置零参与); 宿主为 BaseLib CustomRelicModel
60 槽位类 + 11 事件钩子; RelicGrabBag 替换池, 与 Qurious 共存 (保留一切 CustomRelicModel)。

- 代码入口: `mod/Code/` (Data=账本加载, Generation=片段+生成+文本, Models=宿主,
  Patches=种子捕获+池替换)。卡牌主线的 Generation/Interpretation 代码保留为历史阶段, 未删除。
- 验证: `tools/relic-probe` 24/24 (构建/账本/确定性/文本/执行器漂移)。
- 未验: 实机双端联机; 实机获取→触发→存读档冒烟 (见 DEVLOG 2026-09-12 夜)。

---

## 数据来源与署名 (硬要求)

- 生成池数据 (`catalog_recipes.json` 481 条配方 / 931 个原子实例,
  `catalog_runtime_specs.json` 931 条 RuntimeSpec) 是 **Alriph** 的
  `Auto-Anthonyology` (mod id `AutoAnthony`, v0.3.81) 的离线产出, 本 mod 复用。
- 生成器 / 解释器 / 渲染 / 命名 是**本仓库独立重写**, 不移植反编译产物
  (交接文档 0.2 的"思想重写"要求)。
- `research/native_reference_cards.json` (2.4 MB) 与
  `research/native_reference_components.json` (0.55 MB) 是**引擎自身卡牌的转储**
  (`ReferenceOnly: true`, `GenerationEligible: false`), 放在 research/ 下只作逐条
  对照用, 运行期不需要。
- 本 mod 与 `AutoAnthony` 是独立 mod; 同装时数据重复但行为互不依赖。

署名必须在 manifest `description`、workshop 描述、本文件三处同时存在。

## 机制契约

`research/original-autoauthony-contract.md` (647 行) 是从反编译产物提炼的完整机制
契约, §9 给出 8 条可检验的验收条件。`research/step4-implementation-plan.md` 是
分阶段实施计划与实测数据。

## 关键设计约定

- **不碰 Godot 静态成员的数据层**: 池子以**嵌入资源**形式打进 dll, 加载器
  (`Data/AnthonyCatalog.cs`) 只用 `System.Text.Json` + `Assembly`, 不触碰任何
  Godot API。这样隔离探针能在引擎外完整验收数据层 —— Godot 的静态构造在引擎外是
  原生访问违例 (`0xC0000005`), try/catch 接不住。
- **枚举解析全函数**: 闭集枚举都有 `Unknown` 成员, 未知字符串降级而不抛异常。
  数据升级不得让 mod 初始化或某一局崩掉。
- **口径必须写清**: 例如 trigger 有 55 个 `Kind` 但 57 个完整 spec
  (= 57 个不同 `(Kind, Lifetime)` 对), 解释器要按完整 spec 绑定。这类"两个都对但
  含义不同"的数字必须在文档里标明, 否则会静默数错。
- **配置属性名必须是 Title_Snake**: BaseLib 用 `Slugify(属性名)` 拼 loc 键, 而该
  slugifier 对 ALL_CAPS_SNAKE 有损 (QuriousCraftingRelics 实测 152 个名字里 145 个
  失效)。单词属性名最安全。

## 构建

```bash
# 本 Bash 沙箱缺 APPDATA/PROGRAMDATA/ProgramFiles*, 必须用包装器
python "G:/omp works/Sts/AutoAnthonyRelics/.tmp/dotnet-env.py" \
       "G:/omp works/Sts/sts2-autoanthony-relics/mod" \
       build AutoAnthonyRelics.csproj -c Debug --nologo -v m
```

必须打印 `PCK packed`, 且 `0 警告 / 0 错误`。游戏占用 dll 时加
`-p:CopyToModsFolderOnBuild=false`。

## 隔离探针

```bash
python "G:/omp works/Sts/AutoAnthonyRelics/.tmp/dotnet-env.py" \
       "G:/omp works/.tmp/aar-step4-probe" \
       run --project Probe.csproj -c Debug --nologo -v q
```

探针直接引用构建产物 dll 并调用 `AnthonyCatalog`, 断言数据层与独立 Python 实现
逐项一致。**任何"编译并部署"都不等于"已验证"** —— 结论一律要有探针或实机证据。

## 执行上下文合法性 (WS-0916-05, 2026-09-16, SeedVersion v4)

生成器与执行器必须对"哪些 trigger x effect 组合能真的生效"持同一判断. 执行器
(`AnthonyRelicModel.ExecuteEffectsAsync`) 在两类情形会静默跳过 effect: 没有战斗上下文
(`PlayerCombatState is null`) 时跳过战斗域 opcode; `all_enemies` 目标在敌人全死后解析为
空列表. 生成器过去不检查这两点, 于是能产出"文字承诺了执行器不会跑的效果"的遗物.

`RelicGenerator.Excluded` 是生成期的一半, 在**每个采样点**生效(触发式抽取, 两次去重重抽,
以及被动回退):

1. 递归边界(v1): `gold_gained` 不配 `gain_gold`.
2. `gain_max_hp` 只在 `obtained` 下出现.
3. 被动槽只承载 `modify_hand_draw` -- 执行器的被动钩子只执行这个 opcode.
4. 战斗域 opcode(`apply_power`/`gain_block`/`gain_energy`/`draw_cards`/`deal_damage`)
   不配"可能在无战斗上下文时触发"的 trigger; `all_enemies` 效果不配"敌人已全死之后才触发"
   的 trigger.

判定依据(全部来自反编译与执行器源码, **无实机验证**):

- `PlayerCombatState` 在整个程序集里只在 `Player.ResetCombatState()` 被赋值一次, 之后再未
  置 null, 因此它为空恰好等价于"本局尚未开战". 池中只有 `obtained` 与 `gold_gained` 能在
  该窗口触发; `room_entered` 被执行器限定为 `CombatRoom`, 而 `CombatRoom` 的
  `SetUpCombat`(内含 `ResetCombatState`)先于 `Hook.AfterRoomEntered` 执行.
- `combat_end` / `combat_victory` 只由 `CombatManager.EndCombatInternal` 调用, 该路径要求
  `IsCombatEnding` 为真(无存活主敌), 且主敌死亡会连带清除残余敌人; 而 `all_enemies` 解析为
  `Enemies.Where(e => e.IsHittable)`, 死亡单位 `IsHittable` 为假 -- 即空列表.
- 执行器的被动钩子只执行 `modify_hand_draw`.

`CombatScopedOpcodes` 单一定义在 `EffectFragment` 上, 生成器与执行器共用, 避免两边漂移
(这正是本条目描述的缺陷).

规则覆盖(权威来源 = 探针 `tools/relic-eligibility-probe` 的实际输出, 复现命令 `dotnet run`):

```
trigger fragments (Kind|Condition) : 15
distinct trigger Kinds            : 12
triggered effect fragments        : 31
distinct effect opcodes           : 13
distinct effect shapes            : 21
passive fragments                 : 2
combat-scoped shapes              : 8
all_enemies shapes                : 2
Kinds x opcodes   : 12 x 13 = 156
Kinds x shapes    : 12 x 21 = 252
trigger keys x shapes : 15 x 21 = 315
rule (a) rejects : 2 no-combat triggers x 8 combat-scoped shapes = 16 (Kind x shape)
rule (b) rejects : 2 dead-enemy triggers x 2 all_enemies shapes = 4, of which 4 are not already rejected by (a)
context rejections (union, Kind x shape) : 20 of 252
all exclusions      (union, Kind x shape) : 71 of 252
all exclusions      (Kind x opcode)      : 26 of 156
```

opcode 粒度比规则粗 -- `apply_power` 的 `all_enemies` 变体在 dead-enemy trigger 下非法, 但其 self 变体
合法, 因此 Kind x opcode 计数无法精确表达该规则, **以 shape 计数为准**(探针原文亦如此说明).
2026-09-17 实机启动行 `15 triggers, 31 triggered effects, 2 passives` 与探针首段逐字一致.

权重,点数预算(负面 140 / 普通 100),候选顺序与 RNG 抽取序列均未改变; 规则只做配对排除.

## 先古限制类词条 (2026-09-17, SeedVersion v5)

### 一次已纠正的普查错误(先记录, 免得再犯)

首次普查用**钩子名正则**筛出 26 个"限制类钩子", 这是错的, 已废弃:

- 7 个只命中 `ModifyMaxEnergy`(`BlessedAntler` / `BloodSoakedRose` / `PaelsFlesh` /
  `PhilosophersStone` / `PumpkinCandle` / `SpikedGauntlets` / `WhisperingEarring`) -- 那是
  先古标准的 **+1 能量增益**, 不含任何限制;
- `PrismaticGem` / `StoneHumidifier` / `GoldenCompass` 无限制语义;
- 反而**漏掉**了 `PhilosophersStone.AfterCreatureAddedToCombat`(给敌人加力量)与
  `SpikedGauntlets.TryModifyEnergyCostInCombat`(能力牌费 +1)这两个真实负面。

正确做法: 先古 102 个遗物**全量**取 `public override` 实现体, 按**语义极性**判定, 不按钩子名。

### 真实限制类负面(逐条对照 `research/engine-dllsrc/.../Relics/*.cs`)

| 遗物 | 钩子 | 语义 | 实现体要点 | 引擎配对增益 |
|---|---|---|---|---|
| Ectoplasm | `ModifyGoldGained` | 金币获取归零 | `return 0m;` | `ModifyMaxEnergy` +1 |
| Sozu | `ShouldProcurePotion` | 无法获得药水 | `player != base.Owner` | `ModifyMaxEnergy` +1 |
| VelvetChoker | `ShouldPlay` | 每回合出牌上限 | `_cardsPlayedThisTurn >= CardsVar(6)` | `ModifyMaxEnergy` +1 |
| Fiddle | `ShouldDraw` | 卡牌效果无法抽牌 | 非 `fromHandDraw` 时 `false` | `ModifyHandDraw` +2 |
| SpikedGauntlets | `TryModifyEnergyCostInCombat` | 能力牌费 +1 | `CardType.Power` 时 `+1m` | `ModifyMaxEnergy` +1 |
| PhilosophersStone | `AfterCreatureAddedToCombat` | 敌人 +1 力量 | `PowerCmd.Apply<StrengthPower>` | `ModifyMaxEnergy` +1 |
| SilverCrucible | `ShouldGenerateTreasure` | 前 2 个宝箱房不生成 | `TreasureRoomsEntered > 1` | 升级 3 张牌 |

**`Fiddle.ShouldDraw` 不是开局必死**(实测反编译证据): `CombatManager.cs:924` 的回合起始抽牌
调用是 `CardPileCmd.Draw(..., fromHandDraw: true)`, 而 Fiddle 在 `fromHandDraw` 时**返回 true**,
所以正常起手仍然抽满, 它只否决**卡牌效果**的抽牌。`CardPileCmd.cs:1013` 的
`!Hook.ShouldDraw(..)` -> `return Array.Empty<CardModel>()` 只在非起手路径生效。

### 引擎的配对设计(决定了实现形状)

引擎**从不单独给出限制**: 上表每一行的限制都与一个增益同体出现。因此限制类词条必须与配对
增益**同体**进入遗物, 否则生成的是"只有代价、没有收益"的残废遗物 -- 与引擎自身的设计语言
相悖。纯增益(不含限制)的遗物才是"纯增益", 受 5% 门控。

### `extract.py` 策略反转(必须显式声明)

`extract.py` 模块 docstring 原文拒绝过这类钩子, 并**逐字点名** `potion procurement`:

> bodies we cannot reduce to a known command (custom card transforms, reward screens,
> potion procurement) - inventing an opcode for them would be inventing semantics.
> Refusing is the honest outcome.

本条目**反转**该决策: `ShouldProcurePotion` 这类钩子是引擎**具名钩子**, 实现体返回一个完全确定的
答案, 因此 opcode 是钩子自己的名字, 而不是"发明"出来的语义。仍在拒绝的是真正无法归约的
(自定义卡牌变换、依赖 RNG 的单敌选择器)。反转已写入 `extract.py` docstring 与本文件, 不是静默应用。

`QUERY_HOOKS` 按 **(遗物, 钩子)** 建键而非按钩子名: `TryModifyCardRewardOptionsLate` 在引擎里
有 9 个实现者(附魔 / 升级 / Glam 各不相同), 按钩子名建键会把 8 个错误标签写进"可复现"的提取产物。

## 遗物命名契约 (2026-09-17, SeedVersion v6)

### 契约

遗物名是**所选片段的纯函数**, 不是独立抽样:

```
name = Compose(morpheme(stemSource), morpheme(tailSource))
stemSource ∈ provenance(trigger ∪ effects)      -- 必为真实来源
tailSource ∈ allSources, 按 provenance 加权      -- 权重 64 : 1
```

- **词素表**: `RelicText.Morphemes`, 每个来源遗物一条, EN 与 ZHS 两个字段**都是该遗物官方标题的
  子串**(45/45 已逐条核对权威 loc 转储). 查不到词素的来源**抛异常**, 绝不回退到臆造文本.
- **provenance 是集合**: 片段按 effect shape 折叠, 折叠时 `SourceAtoms` 取**并集**
  (`RelicFragmentPool.Build`). 这正是"可见来源"能成立的前提.
- **确定性**: 命名使用**独立 RNG 流**(`.../slot/{n}/name`), 不消耗片段流的任何一次抽取; 候选顺序
  与权重均为片段集合的纯函数(来源集合 Ordinal 排序), 不依赖字典枚举顺序.
- **唯一性**: 同一 run 内 60 个名字互不相同(EN 与 ZHS 同时参与去重键); 词素对空间 45x44 足够.

### 为什么"stem 必为真实来源"是硬要求

用户要的是"读者能看出与来源遗物的联系". 若两次抽取都从全体词素里按权重取, 会有约 6% 的遗物
两个词素都落在非来源词素上(实测 84/1320), 名字看着像有出处、实际与原子无关 -- 那是最坏的形态.
因此 stem 从 provenance 集合内取, 保证**每一个**生成名都指向真实来源(实测 1320/1320, 0 例外).

### RNG 消耗形状

v5 -> v6 **确实改变了 RNG 消耗形状**, 不是预防性改动: 旧实现每个槽位从片段流里取 2 次
`Next(24)`, 且重试路径上这两次抽取与片段抽取交错; 新实现完全不碰片段流, 改在独立 `/name` 流上
抽取. 因此同 seed 在 v5 与 v6 下会产生不同的片段序列(v5 流少被抽 2 次), 旧档必须重新生成.

### 池指纹不受影响

`RelicFragmentPool.Fingerprint` 只由片段 **Key** 组成, provenance 不参与, 因此 v5/v6 之间
指纹**逐字相同**(实测 `64D5C820863AACB1`). 缓存键 `(seed, SeedVersion, fingerprint)` 中区分新旧
的是 `SeedVersion` 这一项.

## 进度

见 `DEVLOG.md`.当前: 阶段 A (骨架 + 数据层) 完成.

## 限制类词条的抽样门控 (2026-09-18, SeedVersion v7)

### 契约

`PickPassives` 的 restriction 配对分支**不是**"有就必取", 而是先过一道槽位级 roll:

```csharp
if (pairs.Count > 0 && random.Next(100) < RestrictionRelicChancePercent)   // = 30
```

`RestrictionRelicChancePercent = 30`. 门控在 `pairs.Count > 0` 之后求值, 所以
`random.Next(100)` **只在被动带有可用 restriction 时消耗** -- 池为空时不改变 RNG 流.

### 为什么必须门控 (实测数据, 不是推理)

restriction fragment **恰好 6 个**, 被动带是 15% x 60 ~ 9-15 个槽位. 无门控时被动带**每局都把 6 个
抽干**: 68 个种子里 **67 个** restriction 数量 = 6. 名字自 v6 起是片段的纯函数, 6 个 restriction
又全部带先古 provenance, 于是**每局出现同样的 6 个名字与 6 条描述** -- 即用户与工坊反馈的
"遗物从来没有真正随机 / 同一批遗物".

修复后 68 种子的 restriction 数量分布 `0->5 1->7 2->17 3->16 4->12 5->6 6->5`,
**跨全部种子都出现的描述由 7/60 降到 0/60**.

### 与 v6 的关系

v6 让名字成为片段的纯函数, 这**放大**了本缺陷: 修复前"同一批效果"还只是效果文本重复, v6 之后
连**名字**也一并固定. v7 因此必须 bump SeedVersion(RNG 消耗形状变了), 旧存档按新规则重生成.

### 保留的设计意图

- 限制类词条**仍然会出现**, 只是每局是随种子变化的子集, 不再每局全 6 个.
- 限制必须与引擎配对的增益同体出现(Excluded 规则 5)的约束不变.
- 每个 run 内名字仍 60/60 唯一(探针断言).

### 已知边界 (非本轮范围)

一个 run 内**描述**可以重复(60 个槽位 vs 44 个效果片段, 空间必然复用). 名字不同, 描述相同.
实测修复前 60 种子中 56 个有此现象, 属既有设计取舍, 未改动.

## 额外词条池 + 每池生成权重 (2026-09-18 用户指令)

### 用户指令

"扩遗物账本, 从怪异炼化遗物那里搬一点过来, 当作额外池, 做它的开关. 设置页面可调每种词条池的生成权重."

三个交付物:
1. **扩账本** -- 新增效果片段(来源: 姊妹 mod QuriousCraftingRelics 的 extra 池)
2. **额外池 + 开关** -- 默认关; 开启后才参与生成
3. **每池生成权重** -- 设置页可调, 每种池一个滑块

### 为什么必须动"配置不参与生成"的契约 (这是本次设计的核心)

现有契约(见 `AutoAnthonyRelicsConfig` 注释):**配置一律不参与生成**, 定义是
`(mod id, version, run seed, slot)` 的纯函数, 两端 MP 各自重生成即一致.

但用户要的"权重"**本身就是生成输入**. 强行让它不进缓存键会产生一个真缺陷:
改权重后 `DefinitionsFor` 命中旧缓存, 玩家改了设置却看不到任何变化.

**解法: 把生成相关配置纳入定义缓存键, 并在开局冻结.**
(实现后修正: 开关**不**改池内容.)

- **实现取"池恒定 + 谓词门控"**: `RelicFragmentPool.Build(.., includeExtraPool: true)`
  在 `MainFile` 里**永远**把额外原子建进池, 开关只作用于生成期的
  `RelicGenerator.Excluded` 规则 6(`effect.Pool == Extra && !IncludeExtraPool`).
  理由: 让开关改池内容需要**在局中重建池**, 而重建正是定义缓存键要避免的事;
  谓词门控让 fingerprint 保持稳定, 开关改走缓存键的 settings 分量.
- **权重**不改池内容, 只改抽样分布, 必须**显式**进键. 做法:
  `GenerationSettings.Key` 把 5 个值(1 bool + 4 权重, 各 10 bit)**完美打包**进一个
  `long`(非哈希 -- 哈希会存在两组设置撞键, 进而互相取到对方遗物的风险),
  作为第 4 个分量加进 `AnthonyRelicRunRegistry` 的键元组.
- **开局冻结**: 权重必须在 seed 捕获时快照, 与 Qurious 的
  `QuriousGenerationSnapshot` 同一模式. 否则**局中改权重会让已持有的遗物改变含义**
  (定义被重算, 同一槽位的遗物换掉) -- 这是 Qurious 已用实测复现过的缺陷类.
  AAR 侧对应点: `AnthonyRelicRunRegistry.ResetForRunEnd` / `CurrentRunSeed` 的生命周期.

MP 一致性: 两端配置不同 -> 键不同 -> 定义不同 -> **分歧**. 所以权重与开关都是
**Tier-1 MP 确定性键**, 必须在设置页注明"联机两端需一致"(与 Qurious 的
`EnableExtraPool` 注释同一处理).

### 额外池的搬运范围 (已逐个核对引擎 API)

Qurious extra 池 13 项, 按 AAR 执行器能否承接分三类:

**(A) 可直接搬 -- 引擎 API 已核实存在**
| Qurious 模板 | 引擎调用 | AAR 需要的实现 |
|---|---|---|
| `X_HAND_RETAIN` | `CardModel.GiveSingleTurnRetain()` (`CardModel.cs:1348`) | 新 opcode `retain_hand_card` + 回合开始钩子 |
| `X_HAND_SLY` | `CardModel.GiveSingleTurnSly()` (`:1357`) | 新 opcode `sly_hand_card` |
| `X_HAND_ETHEREAL` | `CardModel.AddKeyword(CardKeyword.Ethereal)` (`:1330`) | 新 opcode `ethereal_hand_card` (负面) |

`CardKeyword` 枚举确认含 `Retain`/`Sly`/`Ethereal`(`CardKeyword.cs`).

**(B) 需要附魔 API -- 已核实, 但要选牌逻辑**
`CardCmd.Enchant<T>(card, amount)` (`CardCmd.cs:520`)、`Enchant(enchantment, card, amount)` (`:534`)、
`ClearEnchantment` (`:567`). 覆盖 `X_ENCHANT_SHARP/NIMBLE/IMBUED` 与
`X_PICKUP_SHARP/NIMBLE/IMBUED`(拾起时给**牌组**牌附魔, 与战斗开始给**手牌**附魔是两个不同钩子).

**(C) 不可搬 -- 依赖外部 mod**
`X_STANCE_WRATH/CALM/DIVINITY` 依赖 Watcher mod (`EnterWrath` 在引擎源码中 **0 处匹配**,
Qurious 也是反射调用并在 mod 缺失时跳过). AAR 不应引入这种可选依赖.

**结论: 搬运 (A) 3 项 + (B) 6 项 = 9 项, 全部落在一个新池 `ExtraEffects`.**
`X_RETAIN_ENERGY_DISCOUNT` / `X_RETAIN_ATTACK_BUFF` 依赖"保留牌"的跨回合计数状态,
AAR 现有架构无该状态 -> **本轮不搬**, 与账本 reject 的既有理由一致.

### 权重模型

每池一个整数权重(整数, 因为 `DeterministicRandom` 只有整数 API, 且跨平台一致).
权重表示**各档在遗物池中的占比**, 因此只有相互比例有意义(四项同倍缩放不改变结果):

| 配置键 | 默认 | 作用(实现后修正: 是"档占比", 不是"池内片段权重") |
|---|---|---|
| `WeightTriggeredCore` | 100 | 触发档的占比(默认 80) |
| `WeightPassiveCore` | 100 | 被动档的占比(默认 15) |
| `WeightBenefitCore` | 100 | 增益档的占比(默认 5) |
| `WeightExtra` | 100 | 额外池相对核心池的贡献; **同时**作用于触发档内部 |

接入点: `RelicGenerator.PickBand(random, settings)` 把三个基础概率
(`BenefitRelicChancePercent` / `PassiveRelicChancePercent` / 余量)按各自权重缩放后
转成阈值. 触发档的基础值是"余量"而非固定的 80, 所以其权重缩放的是余量.

`RelicGenerator.WeightOf` 仍然存在, 但**只**对触发档有意义: `pool.TriggeredEffects`
真正混了核心与额外片段, 所以 `WeightExtra` 在那里起作用. 被动档与增益档内部**权重同质**
(`BenefitEffects` 全带 `WeightBenefitCore`, `PassiveEffects` 全带 `WeightPassiveCore`),
对它们做加权抽取在算术上等同均匀抽取 -- 这正是初版两个滑块失效的原因.

关键约束(**实现后修正**): `PickWeightedUniquely` 每次**抽取**无论权重如何都恰好消耗
一次 `random.Next(..)`, 所以权重不改单次抽取的成本. 但它**会**改变总抽取次数 --
被选中的片段会左右控制流(抽到 restriction 会带出配对的 offset 抽取; benefit 走另一条带).
最初本文档写的是"权重不得改变 RNG 消耗形状", 探针一测即证伪(2026-09-19, 同一 seed 下
per-seed 效果数 `74,78,76,79,73` vs `74,77,76,79,74`), 该断言已删除.
真正必须成立的是: **同一 (seed, settings) 组合逐字节可复现** -- 这正是缓存键里
settings 分量所保证的.

### 权重作用在哪一层 (实现后修正: 原设计有两个死滑块)

初版把权重接在 `WeightOf` -> `PickWeightedUniquely` 上, 但**被动档与增益档内部是权重同质的**
(`BenefitEffects` 全带 `WeightBenefitCore`, `PassiveEffects` 全带 `WeightPassiveCore`),
所以对这两档做加权抽取在算术上等同于均匀抽取 -- 两个滑块是死的. 探针原先的"倾斜 profile"
断言抓不到这点, 因为单靠触发档的加权就足以改变组合.

修正: 权重改为缩放**档位抽取**(`RelicGenerator.PickBand`), 即"这个池贡献多少".
档内选取与 restriction 配对保持均匀, 这是有意为之: 用户要的旋钮是"每种池的权重",
不是"池内谁胜出". 设置页 hover 文案已按此改写.
`WeightExtra` 是例外, 它同时作用于触发档内部(核心与额外片段在那里一起抽).

`PickEligiblePassive` 这条**不查权重**的路径只在**下落目标档被关闭**时才走(`WeightTriggeredCore <= 0`, 或增益档的 `WeightPassiveCore <= 0 && WeightTriggeredCore <= 0`): 此时没有合法下落目标, 重发一件已用片段胜过从被关闭的档位产出. 其余情况池抽干后一律下落.

零权重的语义: 设为 0 **彻底关闭该档**(不是"很少出现"). 这需要两处配套修复, 都是真实缺陷:
1. `PickWeightedUniquely` 的 `totalWeight == 0` 分支原先取 `pool[candidates[^1]]`,
   **不消耗 RNG** 且每次返回同一个片段 -> 触发档权重置 0 时所有触发槽位拿到同一片段,
   provenance 相同, 名字空间被抽干并抛异常(**玩家把滑块拉到 0 会崩**). 已改为均匀抽取.
2. `PickName` 的空间耗尽从 `throw` 改为返回最后一个合法组合: 名字重复只是观感问题,
   抛异常会中断生成并破坏整局. 出厂权重下不可能触发(探针有 60 个名字唯一的断言).

实测被动带**达不到标称 15%**(f1c34cc 上 10.97%, 2026-09-19 去掉重发牌后为 6.04%), 原因经池构成核实:
8 个被动词条里 6 个是 restriction, 只剩 **2 个**普通被动; 一局约 9 个被动带槽位,
消耗完这 2 个后槽位落到触发路径, 而 restriction 分支只有 30% 触发. 所以被动带
**上界**是标称值, **有效值**结构性地低于标称值 -- 属内容量限制, 不是缺陷.
探针里原先"15% +- 3"的断言是错的(在 HEAD 上就已失败, 与本次改动无关), 已改为
"不超过标称 15%" + "仍被填充". 下限在 2026-09-19 由 8% 调为 **5%**: 去掉重发牌后
实测 6.04%, 下限的作用是"该档不会变空", 不是钉住名义 15%.

### 池归属的判定

片段属于哪个池, 由**账本来源**决定(而非 opcode 猜测): 额外池的原子写在账本新增的
`extraSupported` 段, `RelicFragmentPool.Build` 据此打标 `EffectFragment.Pool`。
这样"扩账本"就是纯数据工作, 判定不散落在代码里。

### 执行器时机 (实现中查证并修正了原设计)

搬运的 9 项里, 手牌类效果的**执行时机**是唯一真正棘手处, 且本文档初稿判断错了:

- `combat_start` = `Hook.BeforeCombatStart`(`CombatManager.cs:594`), 在 `StartTurn`(`:610`)
  之前 -> **抽牌尚未发生, 手牌为空**. 在此执行手牌效果会遍历 0 张牌而静默失效.
  Qurious 正是撞上这点才把手牌附魔挪到 `AfterPlayerTurnStartLate`
  (`ChaosRelicModel.cs:519` 原话: the hand is empty and every enchant loop iterated
  zero cards).
- `turn_start` = `Hook.AfterSideTurnStart`(`:783`), 而抽牌在 `SetupPlayerTurn`(`:924`)里,
  且 `:778` 已 `await` 该任务 -> **`AfterSideTurnStart` 时手牌已经存在**.
  初稿误以为 `:783` 早于 `:924` 就把两者都推迟了, 属于对异步顺序的误读.
- 修正后的实现: `turn_start` 的手牌效果**内联**执行(`ExecuteEffectsAsync` 默认
  `handAvailable: true`); 只有 `combat_start` 传 `handAvailable: false`, 由
  `AfterPlayerTurnStartLate` 的延迟通道补执行, 并以 `turn <= 1` 保持"每场战斗一次"语义.
- `enchant_deck` 目标是**主牌组**(`PileType.Deck => player.Deck`, 非战斗牌堆),
  局外也存在, 故在 `obtained` 内联执行, 无需延迟.

生成侧对应加了规则 7(`RelicGenerator.Excluded`): 手牌类效果只允许配
`turn_start` / `combat_start`(`EffectFragment.HandEffectTriggers`), 牌组类只允许配
`obtained`(`DeckEffectTriggers`). 否则会生成"文案承诺了某个效果, 但没有任何钩子会跑"的遗物.

另一处实现细节: 延迟通道加了**重复调用守卫**(按 combat state + turn 记忆).
引擎每回合每模型只广播一次该钩子, 但本环境装了 RitsuLib 这类**会重发钩子**的框架,
无守卫时开局手牌会被附魔多次. Qurious 对同一钩子有同样的守卫(`ChaosRelicModel.cs:510`).

### 跨 mod 配置键碰撞 (真实事故, 2026-09-19)

**现象**: 启动日志出现 `[QuriousCraftingRelics] cfg migrated: 7 legacy keys, 6 carried over;
old file kept as AutoAnthonyRelics.cfg.v0.5.1.bak` -> AAR 自己的配置被 Qurious 的迁移**偷走**,
AAR 设置全部重置, 且 6 个 AAR 键被灌进 Qurious 的 cfg.

**根因(两个条件同时成立)**:
1. 两个 mod 的配置文件名**都是** `AutoAnthonyRelics.cfg` -- BaseLib 用**根命名空间**推导文件名,
   而 Qurious 正是从 `AutoAnthonyRelics` 改名而来(见 Qurious `ConfigMigration.cs` 头部注释).
2. 本次给 AAR 新增的属性叫 `EnableExtraPool`, 而它**恰好**是 Qurious
   `KnownLegacyScalarKeys` 里的一个旧键名. Qurious 的归属判据当时是
   **"任一键匹配即认领"** -> 一个同名键就让它认领了别人的整个文件.

**修复(两侧都改)**:
- AAR 侧: 属性改名 `EnableExtraPool` -> `EnableExtraEffectPool`, 并在
  `AutoAnthonyRelicsConfig` 里写明"此名字是承重的, 不得改回; 也不得使用 Qurious 的
  旧键名或 `Cost_/Refund_/Min_/Max_` 前缀".
- Qurious 侧: 归属判据从"任一键匹配"收紧为**"每个键都必须属于 Qurious"**
  (all-or-nothing). 真正的旧版 Qurious 配置不含任何外来键, 所以不损失任何合法迁移,
  而任何带外来键的文件都会留给它的主人. 该侧有独立探针
  (`tools/migration-probe`)覆盖"外来文件不被认领".

**用户数据恢复**: AAR 配置已从 `.v0.5.1.bak` 还原为 `AutoAnthonyRelics.cfg`
(并把旧键名迁移为 `EnableExtraEffectPool`); Qurious cfg 里被注入的 6 个 AAR 键已移除.
两侧实机确认: 启动日志不再出现 `cfg migrated`, 且两个 mod 各自读到自己的设置.

### 验证

- `tools/relic-probe`(离线, 绑定 mod 构建产物): 池计数(165 原子 / 47+9 supported),
  额外池开关的**双向**可达性(关闭时 9 个额外片段从不被抽到; 开启时 200 seed 内全部可达),
  权重确实改变抽中组合, 同一 (seed, settings) 可复现, 时机规则无违规配对.
  并对**故意破坏**做过判别力验证: 把 `WeightOf` 退回常量 -> 2 项失败;
  把开关谓词短路 -> 1 项失败; 把规则 7 手牌子句短路 -> 1 项失败.
- `tools/relic-eligibility-probe`(独立 oracle, 直接编译 mod 的纯源码层):
  `Excluded` 的规则 6/7 与 oracle 表逐对比较. 为让它继续可用,
  `GenerationSettings` 刻意**不引用 BaseLib**(配置读取经 `ConfigSource` 委托注入),
  否则该工程只引用 `sts2.dll` 会编译失败.
  同样做了判别力验证: 短路规则 7 -> 该断言失败(且非空泛性守卫证明它非空转).
- 实机: 启动日志 `relics-v8`, 165 原子, `+9 extra`, 配置项齐全, AAR 零异常;
  把 `EnableExtraEffectPool` 置 True 后日志变为 `extraPool=True`, 证明
  配置 -> BaseLib -> `ConfigSource` -> `GenerationSettings` 全链路打通.
- **未覆盖**: 手牌效果在真实战斗中的实际生效(需要玩家开局并打开额外池;
  后台输入对 Godot 无效), 以及 MP 两端一致性.

### 关闭所有负面效果 (2026-09-19 用户指令)

新配置 `DisableNegativeEffects`, **默认关**(关闭它等于删除内容, 默认保持完整池).

**"负面"的定义** = `EffectFragment.IsNegative` = `IsDownside || IsRestriction`, 两个不相交来源:

| 来源 | 内容 |
|---|---|
| `DownsideOpcodes` | 触发类伤害:失去生命/生命上限/金币, 获得诅咒, 手牌虚无 |
| `RestrictionOpcodes` | 先古限制类词条:金币/药水/出牌数/抽牌限制, 能力牌费用 +1, 敌人获得力量 |

限制类**算负面**, 尽管生成器总是让它与其补偿一起出现(`RestrictionOffsets`). 补偿让这对
词条**公平**, 但不让限制本身不再是代价 -- 玩家说"不要负面效果"是指**根本不要**收到
"金币获取被否决"这件遗物, 而不是"要它但附赠补偿".

**明确不算负面**:
- `retain_hand`(RunicPyramid 的 `ShouldFlush` 返回 false, 即"回合结束不弃手牌").
  它和限制类一样走**否决型**引擎钩子, 一眼看去像负面, 但它只会**保留**玩家本会失去的牌.
  用户特别点名了这一点. 它在 `BenefitOpcodes` 里.
- `modify_card_cost` / `enemy_strength_gain` **算**负面(已读引擎实现体确认:
  SpikedGauntlets 让能力牌费用 +1, PhilosophersStone 让敌人获得力量, 各带 +1 能量补偿).

**接入方式**: 与额外池开关同构 -- 池**恒定构建**, 选项只作用于生成期的
`RelicGenerator.Excluded` 规则 8. 选项是 `GenerationSettings.Key` 的分量, 因而进入定义
缓存键, 开启/关闭各自生成不同定义, 但不需要重建池.

**为什么 SeedVersion 不 bump**: `SeedVersion` 是 RNG **流字符串**的一部分
(`RelicGenerator.cs:506`), 所以 bump 会**重掷所有局**, 包括选项为关(即行为与旧版完全相同)
的局 -- 对每个既有存档都是无谓的 60 件遗物重掷. 本选项不需要它: 它已经是
`GenerationSettings.Key` 的分量, 而该 Key 已在缓存键里, 所以开关切换会正确地重新生成.
(与 v8 的权重同理, 那些权重也是经 Key 生效而没有单独 bump 版本.)

**已知代价(实测, 刻意接受)**: 关掉 6 个限制类后, 被动档只剩 BagOfPreparation(+2)
可用(BigMushroom -2 也被本选项关掉), 即**只剩 1 个**合格普通被动.

2026-09-19 去掉兜底重发牌后的实测(40 局, 被动档槽位/不同遗物):

| `DisableNegativeEffects` | 槽位 | 不同遗物 |
|---|---|---|
| 关(默认) | 58 | 3 |
| **开** | **40** | **1** |

读法: 开启后每局被动档恰好产出 1 件(40 局 40 槽位), 且**恒为同一件** -- 因为池里只有
1 个合格片段, 而每个片段每局最多用一次, 多出的槽位按新规则**下落为触发型遗物**
(不再重发已用片段, 见"被动/增益档耗尽不再重发牌"一节).

**这与本文档此前的数字不同**: 旧记录是"231 槽位 / 6 种", 那是**重发牌时代**的测量
(兜底反复重发, 于是每局约 5.8 个被动槽位, 并在少数 shape 间轮转). 去掉重发后该档
如实降到每局 1 件. 两者都不是缺陷: 前者是重复, 后者是内容量不足.

选择: 接受并记录, 不改架构 -- 玩家显式要求"不要负面效果", 用数量换掉负面是本意;
若要保留多样性, 正确做法是**扩充非负面被动池**, 而不是让限制类漏回来.

**验证**: `tools/relic-probe` 5 项断言(默认确实会抽到负面; 开启后**一件都没有**;
**没有任何效果带负值**(符号断言, 钉住 `modify_hand_draw` -2 这一类);
`retain_hand` 在两种状态下都可达; 选项确实改变生成而非空转).
符号断言是必要的: 只断言 `IsNegative` 是循环论证(属性本身写错也会通过).
判别力验证: 把 `IsNegative` 改成"所有 benefit opcode 也算负面"(即把 `retain_hand`
误判为负面)-> `retain_hand survives the option` FAIL (`off=1 on=0`).
`tools/relic-eligibility-probe` 的 oracle 同步加了规则 8(直接读 `IsNegative`, 不重列 opcode,
以免与实现漂移).

### 代码审查 (2026-09-19, 全量)

**修掉一个潜在的非确定性缺陷(我自己引入的类)**:
`RelicFragments.Build` 里 `foreach (KeyValuePair<...> injection in fix.Values)` 直接迭代
`Dictionary<string,int>`. .NET 的**字符串哈希按进程随机化**, 所以字典枚举顺序**不保证跨进程稳定**;
该顺序会进入 `values` 顺序 -> `ValuesKey` -> 片段 Key -> 遗物 fingerprint, 即**生成输出**.
当前账本每个 fix 只有 **1 个** value key(实测:max=1, 无多键条目), 所以暂时触发不了,
但一旦有人加一个双键 fix, 同一 seed 在不同进程就会产出不同遗物 -- 极难归因.
已改为 `OrderBy(kv => kv.Key, StringComparer.Ordinal)`(对现有单键数据是 no-op).

**逐项核实为"正确"的(不再重复检查)**:
- `PickBand` 算术: 对 UI 全范围(0..400 步长 10, 共 **68,921** 组三权重组合)暴力验证:
  `Math.Clamp` 从不抛(`min > max` 会抛, 这是真实风险), 阈值恒在 [0,100] 且严格递增,
  非零权重恒保留其档.
- 生成层无 `GetHashCode` 调用(全仓库 grep 为空); 池组装按 `OrderBy(Ordinal)` 排序;
  `Union` 用 `SortedSet`; `RestrictionOffsets` 只用 `ContainsKey`/`TryGetValue`(与顺序无关).
- 跨进程确定性: 连续 3 次独立进程运行, fingerprint 与全部断言一致.
- 良构性: 200 seeds 默认设置 + 120 seeds 开启负面开关(后者是**新增**断言 --
  默认 soak 看不到该配置), 每个槽位 `Effects.Count > 0`, 不存在"有触发但无效果"的遗物.
- `fix.Values` 之外无其它 Dictionary 迭代进入生成路径.


## 避免无效效果 (2026-09-19 用户指令, SeedVersion v8)

### 用户指令

> 增加一个选项, 使遗物效果不会无效(如战斗结束给敌方减益, 伤害)或者非战斗中给予能量, 增益, 减益.

### 先测, 再实现: 这是真实缺陷而非臆测

用户举的两个例子**已经**被既有规则覆盖, 但普查发现真正的洞在别处.
对 200 个种子逐遗物逐效果枚举 (触发时机 x 效果 opcode) 配对, 用"该效果在此触发时机
是否可能产生任何可观测结果"判定:

| 判据 | 覆盖它的规则 | 实测配对数 |
|---|---|---|
| 非战斗状态下需要战斗的效果 | 规则 4 (无条件) | 已覆盖 |
| 战斗结束时对**敌方**造成伤害/减益(敌人已全死) | 规则 4 (无条件) | 已覆盖 |
| **战斗已结束的触发 x 自身战斗类效果** | **无 -- 本轮新增规则 9** | **638 对 / 200 种子** |

638 对约合**每局 3 件死遗物**, 实例: `combat_end x gain_block`("每场战斗结束时获得
14 点格挡" -- 战斗已结束, 格挡随之清除), `combat_victory x gain_block`,
`combat_end x apply_power/strength/self`, `combat_victory x apply_power/thorns/self`.

**结论: 用户描述的是同一个缺陷的两个侧面, 规则 4 只补了一半.**

### 为什么规则 9 只判"自身"方向

敌人目标的效果挂在 `combat_end`/`combat_victory` 上时**必然**无效(此时敌人已全部死亡),
规则 4 已无条件拒绝, 规则 9 不需要重复实现. 同理"非战斗状态下需要战斗"也是规则 4.
规则 9 只补剩下的那一半: **触发在战斗结束后发生 x 效果作用域是本场战斗** --
格挡随战斗清除, 能量已无回合可花, 加给自己的 Power 随战斗状态丢弃, 抽到的牌无处可去.

### 规则 9 可关, 规则 4 不可关 (设计决定)

规则 4 拒绝的是**纯死文本**(结构性破坏: 一个指向已死敌人的效果, 描述本身就是错的),
所以它**没有开关**. 规则 9 拒绝的是引擎层面的 no-op(效果文本没错, 只是在该时机无事可做),
属于"不想要但不破坏结构", 所以**给开关**. 这也是为什么 `DisableIneffectiveEffects`
默认**开**(见下).

### `DisableIneffectiveEffects` 默认开 (与其它"删内容"选项相反)

`EnableExtraEffectPool` 默认关, `DisableNegativeEffects` 默认关 -- 它们删的是**内容**
(玩家可能就想要那些). 死效果不是内容, 是**缺陷**, 所以默认保证开启, 除非玩家主动关掉.

它是 **Tier-1 MP 确定性键**(进 `GenerationSettings.Key` 第 3 bit), 联机两端必须一致.

### 命名检查 (跨 mod 配置键碰撞的教训)

新增配置属性前必须查 `QuriousCraftingRelics` 的 `KnownLegacyScalarKeys` 与
`Cost_/Refund_/Min_/Max_` 前缀. `DisableIneffectiveEffects` 已确认无碰撞; 实机日志
`cfg migration skipped: AutoAnthonyRelics.cfg does not match the Qurious legacy schema`
再次确认.

### 一个必须记住的坑: `GenerationSettings.Default` 必须与 config 默认一致

`Default` 是**离线探针**在未绑定 `ConfigSource` 时读到的值(也是 `MainFile` 绑定前的兜底).
本轮最初把它写成 `disableIneffectiveEffects: false`, 而 config 属性初值是 `true` --
探针于是测的是**玩家永远拿不到的配置**, 规则 9 在探针里等于没测. 已修正为 `true`,
并在 `Default` 上写明"MUST track AutoAnthonyRelicsConfig's initializers".
`tools/relic-probe` 的 `GenerationSettingsTest.Set` 同理: `disableIneffective` 缺省值为
**`true`**(随出厂默认), 而不是 `false`.

### 不 bump `SeedVersion`

`SeedVersion` 参与 RNG 流字符串(`RelicGenerator.cs:506`), bump 会重掷**所有**局,
包括选项为关(行为与旧版完全一致)的局. 选项本身已通过 `GenerationSettings.Key`
进入定义缓存键与 loc 去重键, 切换时会正确重新生成, 不需要 bump.
`SeedVersion` 保持 `relics-v8`.

### 验证

- `tools/relic-probe` 新增 3 条断言, 全 PASS:
  1. 选项**关**时死配对**必须存在**(非空性 -- 否则"规则把一切都排除了"也会通过)
  2. 选项**开**时死配对**为 0**(200 种子 x 全部遗物 x 全部效果)
  3. `combat_end`/`combat_victory` **触发本身仍可达**(规则剪的是配对, 不是触发;
     若某触发被剪到无合法效果, 遗物会变成"有触发无效果"的畸形)
- **判别力验证**: 把规则 9 的判据替换为 `&& false` 后重建, 断言 2 立即 FAIL 并报出
  `combat_end x gain_block/immediate/self | combat_victory x gain_block/immediate/self |
  combat_end x apply_power/strength/self | combat_victory x apply_power/thorns/self`.
  证明断言真的在测这条规则.
- `tools/relic-eligibility-probe`(独立 oracle, 直接编译纯源码层)的 `PolicyExcluded`
  加入规则 9, 读的是**同一批集合**(`EffectFragment.CombatScopedOpcodes` +
  触发名字面量)而非重新抄一份 opcode 列表, 避免 oracle 与实现漂移. PROBE OK,
  含"每个触发词条仍 >= 1 合法触发"的可达性断言.
- **实机**: 见 DEVLOG 条目.


## 金币与药水栏位仅在拾起时 (2026-09-19 用户指令, 规则 10)

### 用户指令

> 获得金币和药水栏位只能在拾起时, 否则强度会极高.

### 先测再写: 又是真实缺陷

写规则前先枚举 200 个种子 x 全部遗物, 找"触发不是 `obtained` 但效果给金币/药水栏位"的配对:

```
block_cleared      x gain_max_potion/immediate
combat_start       x gain_gold/immediate
combat_end         x gain_max_potion/immediate
turn_start_early   x gain_max_potion/immediate
turn_start         x gain_max_potion/immediate
combat_victory     x gain_max_potion/immediate
```

`turn_start x gain_max_potion` = **每回合一个药水栏位**; `block_cleared x gain_max_potion`
= 每次格挡被清空就给一个. 这类效果远强于本体任何遗物.

### 为什么必须限 `obtained`

金币与药水栏位是**永久货币**, 不是每场战斗的资源. 挂在 `obtained` 上只结算一次,
这正是引擎自己的做法: `OldCoin`(金币), `PotionBelt` / `PhialHolster`(药水栏位)
三个原子的源触发**全部**是 `obtained` (已查 `relic_atoms.json` 确认). 挂在会重复的触发上
则每次触发都结算, 复利式膨胀.

### 规则 10 无条件, 不给开关

与规则 2(`gain_max_hp` 仅 `obtained`)同形同理: 这是**平衡保证**, 不是偏好,
所以没有开关 -- 它和规则 2 一样属于"生成策略", 不属于规则 6/8/9 那种"玩家可选的选项".

**规则 1 被严格包含**: 规则 1 是 `gold_gained x gain_gold`(递归边界: 给金币会再次进入
`gold_gained` 钩子). `gold_gained != obtained`, 所以规则 10 已覆盖它. 规则 1 **保留**,
因为它陈述的是**另一条不变式**(递归边界), 若将来规则 10 被收窄, 规则 1 必须仍然成立.

### 验证

- `relic-probe` 新增 2 条断言:
  1. 200 种子内**没有**非 `obtained` 触发携带 `gain_gold`/`gain_max_potion`
  2. **非空性**: `obtained` **仍然**能拿到这两类效果(证明规则门的是触发而非删片段)
- **判别力验证**: 规则 10 判据插入 `&& false` 后重建, 断言 1 立即 FAIL 并报出
  `block_cleared x gain_max_potion/immediate | combat_start x gain_gold/immediate |
  turn_start x gain_max_potion/immediate | ...`. 已还原.
- `relic-eligibility-probe`: 规则 10 进 `LegacyExcluded`(与规则 2 同段, 因为二者都是
  无条件的策略而非开关驱动的策略). PROBE OK, 含可达性断言.


## 被动/增益档耗尽不再重发牌 (2026-09-19, v9 顺带修复)

### 怎么发现的

规则 10 需要 bump `SeedVersion`(v8 -> v9),因为它是**无条件**规则,改变默认产出.
bump 后 `relic-probe` 的跨种子多样性断言失败:

```
FAIL  variety: no relic description is present in every seed
      2 constant: Take an extra turn if you play no cards. | Draw 2 fewer cards on turn 1 of each combat.
```

### 先分离因果,再改代码

三个对照实验:

1. **只把 `SeedVersion` 改回 v8**(保留规则 10)-> PASS
2. **保持 v9,规则 10 短路为 `false`** -> **仍然 FAIL**,同样两条

实验 2 证明**与规则 10 无关**.但"v8 绿 / v9 红"本身不能证明有缺陷 -- 也可能是
**那条断言不稳健**.所以没有直接改代码,而是先做**大规模测量**:

| 配置 | 200 种子下最高频描述 | `>= 95%` 的描述数 |
|---|---|---|
| 原样(带回退) | **196/200 = 98%** | **2 条** |
| 去掉回退 | 121/200 = 60.5% | **0 条** |

这才是决定性证据:回退让两条描述**近乎普及**,去掉后没有任何描述超过 60.5%.

### 根因

两个档位在池耗尽时都有 `?? PickEligiblePassive(..)` 兜底,而 `PickEligiblePassive`
**忽略去重集合**,直接重发一个已用过的片段:

| 档位 | 片段数 | 构成 |
|---|---|---|
| 被动档 `PassiveEffects` | 8 | 6 个限制类 + **2 个普通**(均为 `modify_hand_draw/passive`) |
| 增益档 `BenefitEffects` | 5 | `extra_turn` / `modify_max_energy` / `retain_hand` / `enchant_reward` / `expand_card_pool` |

被"钉住"的两条正是**两个档位各一条**:`Draw 2 fewer cards on turn 1`(被动档普通片段)
与 `Take an extra turn if you play no cards`(增益档).两个档位的片段总数都远小于每局
进入该档的次数,回退于是反复重发同一批片段,把它们推到近乎每局必现.

### 修法

去掉两处回退,池耗尽时档位**下落**(增益 -> 被动 -> 触发),与既有的档位递补结构一致.
**例外(零权重契约)**:若下落的目标档位被玩家关掉(`WeightXxx <= 0`),则仍然重发 --
因为"权重设为 0 关闭该档"是**对玩家的承诺**(工坊说明与探针都这么写),此时重发一件
重复遗物是两者中较轻的那个恶.被动档的守卫判 `WeightTriggeredCore <= 0`,增益档的守卫判
`WeightPassiveCore <= 0 && WeightTriggeredCore <= 0`(两个下落目标都不可用).

### 顺带修正的两处

1. **断言本身换成统计稳健的判据**:原断言取 **8 个种子**的交集并要求为空.片段池很小,
   每条描述在约 55-60% 的局中出现,单条命中 8/8 的概率约 `0.57^8 = 0.9%`,60 条里期望
   约 0.5 条 -- 这条断言**靠运气**.改为在 **200 个种子**上判"无描述达到 95% 普及率",
   并打印峰值占比,判据与实测(60.5% vs 95% 阈值)留有充分余量.
2. **`relic-eligibility-probe` 的被动档下限 8% -> 5%**:修复后被动档实测 **6.04%**,
   受内容容量限制(只有 2 个普通被动,每局每片段最多一次).下限的作用是"该档不会变空",
   不是钉住名义 15%.

### 零权重组合的新测试

原探针只做**一档一关**的测试,覆盖不到"某档被关,其槽位又落进另一个被关的档".
补了 3 个**成对**用例(`passive+benefit` / `triggered+passive` / `triggered+benefit`),
并要求两个被关档位都保持 0 槽位.

**判别力验证**:把增益档的守卫短路为 `false` 后重建,`triggered+passive` 用例立即 FAIL
并报出 `benefit/passive/triggered = 400/0/4400` -- 被动档被关后仍产出 4400 个触发型档位,
正是该守卫要挡的漏洞.已还原.

### 工坊说明的过时数字

`workshop_upload.vdf` 原写"100/100/100/100 即默认的 80/15/5 分布".实测(200 种子,
12000 槽位)为 **89.17 / 6.04 / 4.78**.已改为:80/15/5 是**名义**比例,并给出**实测**
比例与原因(被动/增益档片段少,耗尽后落档).
