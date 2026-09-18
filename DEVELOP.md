# AutoAnthonyRelics - 真正的东尼算法-遗物

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
