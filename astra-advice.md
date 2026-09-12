# Astra advice - AutoAnthonyRelics 返工方向与语义验收

日期: 2026-09-12. 主会话单线评估. 本文是建议, 未实现返工, 未改产品代码, 未发布.

先读 [总索引](../astra-advice.md). 本轮读取了 HANDOFF-2026-09-12-PT3.md, 实际运行 relic-atom-extractor, 并对照引擎遗物原始控制流. PT3 记录的用户裁决应保留; PT3 中的技术推论不能免审.

## 产品契约: 机制移植, 不是卡牌产品复制

用户要的是把原版 AutoAnthony 的条件/触发与效果分离采样, 生成期绑定, 种子确定性等机制用于遗物. PT3 D8/D9 明确: 输出是遗物; 数据来自引擎原生遗物, 不复用 Qurious 的固定模板池.

以下三种东西不可混为一谈:

1. 原版算法机制: 独立原子, 兼容性约束, 条件随机绑定, 确定性, 宿主触发生命周期.
2. 输入内容: 原生遗物的完整语义, 包括正面, 负面, 状态依赖, 一次性效果.
3. 运行载体: 持有即监听的 RelicModel, 而不是要抽到并打出的 CardModel.

正确的机制并不要求照搬原版卡牌费用/X资源/卡牌稀有度/OnPlay 的规则. 反过来, 只把一组固定 "触发+效果" 模板随机拼起来也没有复现条件重组.

## P1 AAR-1: 140 条是候选提取, 不能作为已正确的生成池

证据: `tools/relic-atom-extractor/extract.py`, `research/relic_atoms.json`, 本轮输出 `../astra-advice-evidence/2026-09-12/atom-audit.json`.

实际复跑:

- 扫描 300 个类, 130 个类贡献, 140 条结果.
- 19 opcode, 31 opcode/variant, 22 trigger, 4 condition.
- 37 条 Values 为空.
- 115 条把 Trigger 仍附在效果里; 独立 Opcode=trigger 的片段为 0.
- 复跑 JSON 与磁盘 JSON 完全一致. 这证明提取可重复, 不证明语义正确.

已直接反证的样本:

### BagOfMarbles

- 引擎 `...Models.Relics/BagOfMarbles.cs:23-28`: 自己首回合开始, 向 combatState.HittableEnemies 施加 1 Vulnerable.
- JSON: Target=self, Values=[], first_turn.
- 错误不只是少一个字段: 受益/受害方颠倒, 数值也丢失.

### ArtOfWar

- 引擎 `ArtOfWar.cs:55-105`: 记录本回合是否打攻击, 回合结束搬到 lastTurn; 第 2 回合起, 只有上回合未打攻击才 +1 能量.
- JSON: 每次 turn_start +1 Energy, 唯一条件 owner_turn.
- 它没有 SavedProperty 但有必需的战斗内状态. "无 SavedProperty" 不能推导出 "无状态".

### LetterOpener

- 引擎 `LetterOpener.cs:39-42,109-118`: 每回合每 3 张自己的技能牌, 对全部敌人造成 5 Unpowered 伤害.
- JSON: 每次 card_played, lose_hp, amount=3, condition=null.
- 数量 3 是阈值, 不是伤害. 提取器从整个方法读取首个 DynamicVar, 把阈值当效果量, 同时丢掉技能牌/拥有者/计数条件.

### Tingsha

- 引擎 `Tingsha.cs:18-27`: 自己的回合里弃自己的牌, 通过 CombatTargets RNG 选择一个敌人, 造成 3 伤害.
- JSON: all_enemies, condition=null. 方法内出现 HittableEnemies 不代表命令目标就是全体; 它在此只是随机候选集.

### BigMushroom

- 引擎 `BigMushroom.cs:36-46`: 自己第 1 回合, `cardsToDraw - 2`.
- JSON: modify_hand_draw, passive, amount=2, owner_turn.
- 修饰符的减号和首回合条件仍没被保存. PT3 只修正了 "别取成 20", 不代表剩余语义完整.

### FragrantMushroom / Brimstone

- FragrantMushroom 的获得效果打自己, `Unblockable | Unpowered`, JSON 却是 selected_enemy. 获得遗物时经常根本不在战斗中, 没有可选敌人.
- Brimstone 两次命令分别给自己 +2 Strength, 给活敌人 +1 Strength. JSON 两条都 self 且 Values 为空. 两个 named PowerVar 和每次调用的实参都未被解析.

## 根因, 不要逐样本打补丁

`extract.py:158-193` 从整个方法找第一个数字/第一个 DynamicVar. `:281` 对同一方法每个命令都重复使用这个数值.

`:282-286` 默认 self, 仅对统一命名为 lose_hp 的 CreatureCmd.Damage 猜目标, 且把方法里出现 HittableEnemies 当成全体目标.

`:125-146` 用第一个能识别的条件返回结果, 没有逻辑表达式, 合取, 分支支配, 参数角色或局部变量的数据流.

`:35-80` 把 Before/After 以及不同阶段合并为 turn_start/card_played/turn_end. 回合能量重置和抽牌的相对顺序恰好是遗物语义的核心, 不能压平.

`:224,358-362` 只看 SavedProperty 排除状态, 不分析普通字段, 属性, helper 调用, closure, 计数器重置.

未知数值仍输出 Values=[], 未知条件变成 null, 但没有 GenerationEligible=false/unsupported reason. 这与注释 "拒绝错误原子" 相反.

建议: 此脚本只作为候选发现工具. 不扩充正则直到看起来涵盖大多数; 采用 Roslyn/IL 的可解析调用与数据流分析, 或对候选逐条人工形成结构化事实. 无法证明的候选留在拒绝清单, 不进入抽样集合. 不要求一步实现通用反编译器.

## P1 AAR-2: 数据结构尚未实现独立条件与效果

当前 `relic_atoms.json` 把 Trigger/Condition 嵌入 Spec. 作为来源配方记录可以, 作为唯一生成原子集合不够.

需要显式区分:

- 来源配方: 原版哪个遗物的哪个钩子, 原有绑定关系, 只用于忠实重建与审核.
- 独立触发/条件片段: 事件名, Before/After 阶段, owner 过滤, 可用事件参数, 状态依赖, 生命周期.
- 独立效果片段: 目标解析, 数值表达式, ValueProp/伤害类别, 修改还是追加, 上下文需求.
- 生成定义: 抽样得到的片段及新绑定边. 来源配方搭档不得偷偷作为默认绑定.

不要用 "new GeneratedRelic(old GeneratedCard)" 或 "不再叫 Card" 证明完成. 验收要从生成数据中观察到来源不同的触发与效果, 并在游戏中由持有遗物实际执行.

## P1 AAR-3: PT3 的复用判断过于乐观

- `TriggerBinder.cs:138-179`: IncreaseThisCardCost, ShuffleAllUnexhaustedIntoDraw 等卡牌特判仍在. 14 条不是通用公理.
- `CompatibilityFilter.cs:6-10,49-149`: ShellContext 依赖 CardType, Card Cost, HasStarCostX, Power 壳约束.
- `GenerationTypes.cs:18-64`: 卡牌类型与单体敌人壳; RelicRarity 的 Starter/Event/Shop 与卡牌 Basic/Ancient 不能机械等同.
- `EffectExecutor.cs:30-39`: EffectContext 强制 CardModel Card + CardPlay; 获取遗物, 进房, 每回合被动没有这张正在打出的卡.
- `EffectExecutor.PlayAsync` 是 OnPlay 的遍历器, 不等于常驻遗物事件调度器.
- Modifier 需要返回变换后的数值, 不能当成 async 副作用命令. 正负号/加乘顺序/提前返回必须有结构化表示.

可复用: 经证明的确定性 RNG, 索引绑定形式, 纯计划与引擎执行分离, 被逐条验证的命令适配器.

应删除或重新推导: 卡牌壳/费用/X门控, 卡牌原子白名单, 为卡牌特有生命周期准备的 14 条细则, 卡牌牌库/描述/出牌注册链.

不要为了少改文件保留一个虚构 CardModel 来承载遗物效果; 这会污染 cardSource, 伤害修饰, 抽牌历史, Kill attribution, UI 和联机序列化.

## 必须先锁定的遗物语义契约

以下是设计建议, 不是虚构的用户新裁决:

1. **owner/event source/target 分离**. 事件来自队友, 宠物, 怪物还是自身? 伤害是攻击/无来源/HP loss? 不能全部 default self.
2. **阶段保真**. BeforeSideTurnStart, AfterEnergyReset, AfterHandDraw 等先保留原事件, 不急于压成一个 turn_start.
3. **状态生命周期显式**. 每事件/每回合/每战斗/每局; 首次获得一次性状态与读档恢复分开. 无 SavedProperty 不代表可丢掉状态.
4. **持有即生效**. 遗物的重复触发与卡牌的有限打出频率不同. 原始数值可作为来源值, 不能据此宣称随机组合平衡.
5. **递归触发是核心边界**. 原契约样例 "失去生命 -> 失去生命" 会形成事件反馈. 先研究原版真实 limiter 的作用域/重置规则. 不要自行捏造阈值, 也不要用 catch 或全局禁递归把用户要求的组合悄悄删掉.
6. **不可变本局定义**. 包含生成版本, 数据版本, 有效配置, 精确操作与绑定; 存读档/重连恢复它, 不是重新读 live 偏好生成. 参考 Qurious 文档中的反例, 不复制其缺陷.
7. **纯查询**. Modifier/描述不得消耗 RNG/修改计数. 主线程成本按多件遗物叠加考虑.
8. **Unsupported 不上生产池**. Unknown 可作为健壮解析结果, 但不能自动转成可抽样的空效果. 未知存档不能被悄悄替换为另一件遗物.
9. **两个遗物模组共存**. Qurious 会删除非 Qurious 的整个随机遗物袋. 新项目若也做替换, 双方顺序可能把池清空. 范围/互斥/共存策略必须明确, 不能默认双开安全.
10. **配置名被复用**. 新 AutoAnthonyRelics.cfg 当前会被 Qurious 的迁移器误搬走; 必须先修 Qurious 迁移边界再双装测试.

## 建议执行顺序与完成定义

### 第一步: 停止把计数当质量门

保留 140 条为审计候选; 创建逐原子来源账本, 标明 supported/rejected/requires-state. 先审上述反例以及 Anchor, Lantern, BagOfPreparation, BloodVial, Brimstone 等不同形态. 每条要解释目标, 数值表达式, 条件, 生命周期, 原子化后丢失了什么. 数值不明不生成默认值.

### 第二步: 固化遗物 IR, 再迁数据

只建立承载实际需求的最小结构. 不要先机械全局改名. 数据层输入切到新 schema 后, 旧卡牌 JSON 不再是运行期嵌入资源, MainFile 不再把卡牌 catalog loaded 当遗物初始化成功.

### 第三步: 先做一个完整垂直路径

从可信原子生成一件真正遗物 -> RelicCmd.Obtain -> 事件/纯 modifier 执行 -> 悬停描述 -> 保存加载 -> 两件独立计数. 这不是把范围缩水成 MVP, 而是先证明承载模型, 再沿同一规则补全其余原子. 中间状态不得发布为完成品.

### 第四步: 扩展组合与同步

覆盖所有支持的原子, 拒绝不兼容目标/上下文, 独立采样绑定, 递归触发, seed 与输入版本确定性. 同 seed 同快照一致; 改本地配置不改变已获得遗物; 新进程读档不变; 重连恢复同一份定义.

### 第五步: 清理错误方向, 完整交付

仅在替换链成立后移除不再服务产品的卡牌生成路径, 文案/manifest/测试/嵌入资源一起切换. 不能保留大量 "已完成 Stage A-D" 给接手者误读. DEVELOP/DEVLOG 当前仍描述旧卡牌方向, PT3 又在技术上过度许诺; 用当前契约替换状态摘要, 历史留在历史章节.

## 高价值验收场景

- BagOfMarbles 忠实重建: 对所有敌人 1 易伤, 不伤自己, 自己第 1 回合一次.
- Tingsha: 弃牌只打一个随机敌人, RNG 不被预览消费, 对端候选顺序一致.
- LetterOpener: 第 1/2 张技能不触发, 第 3 张 5 伤, 攻击牌与队友牌不推进计数, 下回合重置.
- BigMushroom: 获得 +20 MaxHp; 首回合少抽 2, 非首回合不减少. 正负号不能被 uint/abs/默认正数吞掉.
- ArtOfWar: 第 1 回合无奖励; 打攻击后的下回合无奖励; 未打攻击后的下回合 +1.
- 获得时掉血/最大生命: 读档不再次扣除; 重复查询描述不扣血; 改偏好不重新抽掉负面.
- 来源不同的触发+效果确实组合, 不是从原配方复制后改文本.
- 修改器与事件效果执行次数独立; 重复挂载/重新进房/重新加载不会多订阅一次.
- 隐式未知条件/数值/目标全部阻止进生成池, 而不是让 probe 只检查 count=140.

## 不要重复的思考错误

- "opcode 一样, 所以可复用" 忽略目标, ValueProp, 时序和上下文.
- "所有信息来自原版, 所以忠实" 忽略提取过程会丢信息.
- "不能提取的条件就省略" 会把有条件收益变成无条件收益.
- "自己扫描输出与自己 JSON 一致" 只是同一错误的两份副本.
- "多做原子能提高覆盖率" 在来源错误时只会扩大污染.
- "用户没说就维持卡牌设计" 与明确遗物目标相反.

## 证据和边界

证据包: `../astra-advice-evidence/2026-09-12/atom-audit.json`, `atom-extractor.log`; 完整隔离数据在 `G:/omp works/.tmp/sts-workspace-audit-1789171823600/relic-atoms-rerun.json`.

本轮未把卡牌构建/既有 CardPlan 探针称作遗物验收. 也未判定 "只做 140 条就够了". PT3 记录的其他待用户决策, 例如版本与发布, 保持未决.

工作区快照后来 HEAD 已不等于 PT3 所写 ce01c54, 且 AnthonyEnums.cs 有未提交修改. 本轮没有回滚/清理它们. 接手时先读当前实际文件, 但不要因为目录有更新就跳过本文已复现的提取问题.
