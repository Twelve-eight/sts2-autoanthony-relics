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
