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
python "G:/omp works/AutoAnthonyRelics/.tmp/dotnet-env.py" \
       "G:/omp works/sts2-autoanthony-relics/mod" \
       build AutoAnthonyRelics.csproj -c Debug --nologo -v m
```

必须打印 `PCK packed`, 且 `0 警告 / 0 错误`。游戏占用 dll 时加
`-p:CopyToModsFolderOnBuild=false`。

## 隔离探针

```bash
python "G:/omp works/AutoAnthonyRelics/.tmp/dotnet-env.py" \
       "G:/omp works/.tmp/aar-step4-probe" \
       run --project Probe.csproj -c Debug --nologo -v q
```

探针直接引用构建产物 dll 并调用 `AnthonyCatalog`, 断言数据层与独立 Python 实现
逐项一致。**任何"编译并部署"都不等于"已验证"** —— 结论一律要有探针或实机证据。

## 进度

见 `DEVLOG.md`。当前: 阶段 A (骨架 + 数据层) 完成。
