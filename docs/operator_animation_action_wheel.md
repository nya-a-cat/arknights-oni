# Operator Animation Profiles and Action Wheel

## 结论

PRTS 的基建模型和战斗模型需要共用语义动作映射，不能共用完整的原始动画名集合。

2026-07-15 的代表性样本对照如下：

| 干员与模型 | 基建动画 | 战斗/正面动画 | 共同名称 |
| --- | --- | --- | --- |
| 阿米娅 默认 | `Default / Interact / Move / Relax / Sit / Sleep` | `Attack / Attack_Begin / Attack_End / Default / Die / Idle / Skill / Skill_2 / Skill_2_Begin / Skill_2_End / Skill_Begin / Skill_End / Start / Stun` | `Default` |
| 德克萨斯 默认 | `Default / Interact / Move / Relax / Relax_Idle / Sit / Sleep` | `Attack_End / Attack_Loop / Attack_Start / Default / Die / Idle / Skill / Start` | `Default` |
| 凯尔希 默认 | `Default / Interact / Move / Relax / Sit / Sleep` | `Attack / Default / Die / Idle / Start` | `Default` |

因此动作适配以 `OperatorActionKind` 为稳定接口，模型只负责提供该语义动作的最佳可用动画。

## 语义动作合同

| ONI 场景 | 首选语义动作 | 基建优先级 | 战斗优先级 |
| --- | --- | --- | --- |
| 待机/休息 | `Idle` | `Relax -> Relax_Idle -> Idle -> Default` | `Idle -> Default` |
| 移动/爬梯/跳跃 | `Move` | `Move -> Idle -> Default` | `Idle -> Default` |
| 挖矿/建造/工作 | `Work` | `Interact -> Relax -> Idle -> Default` | `Attack -> Skill -> Idle -> Default` |
| 睡觉 | `Sleep` | `Sleep -> Sit -> Relax -> Idle -> Default` | `Idle -> Default` |
| 坐下/吃饭/如厕 | `Sit` | `Sit -> Relax -> Idle -> Default` | `Idle -> Default` |
| 战斗/攻击 | `Combat` | `Interact -> Attack -> Idle -> Default` | `Attack -> Skill -> Idle -> Default` |
| 压力/眩晕/生病 | `Stress` | `Relax -> Idle -> Default` | `Stun -> Idle -> Default` |
| 死亡 | `Death` | `Relax -> Idle -> Default` | `Die -> Stun -> Idle -> Default` |

Begin/Main/End 由语义动作计划处理。缺少某个阶段时跳过该阶段，缺少主体时降级到该模型的下一项。

## 手动表演转盘

手动表演和 ONI 自动状态分开。表演只改变视觉动画，不修改复制人的寻路、工作、生命、碰撞和模拟状态。

当前第一版已经实现。自动模型切换默认开启，也可在 Mod Options / `Ctrl+Shift+F8` 中关闭。

每个复制人可通过 `Ctrl+F8` 保存独立干员、皮肤和模型；`Ctrl+Shift+F8` 管理全局默认。动作转盘始终读取当前复制人的最终外观配置，切换干员时清除该复制人的手动表演并重新建立动作计划。

### 第一版入口

1. 玩家选中一个复制人。
2. 按 `Ctrl+F9` 打开或关闭转盘。
3. 八个外圈按钮对应 `待机`、`移动`、`工作/挖矿`、`攻击/技能`、`睡觉`、`坐下`、`眩晕`、`死亡`。
4. 点击动作后立即应用并关闭转盘，`Esc` 取消。
5. 中心按钮“恢复自动”立即交还给 ONI 当前状态；真实死亡与严重压力状态始终拥有最高优先级，即使导航器仍报告移动也不会被 `Move` 覆盖。

### 动作选择

- 转盘显示稳定的八种语义动作；当前模型缺少目标动画时按合同降级。
- 选择 `攻击/技能` 时优先使用 `Attack`、`Attack_Loop`、`Skill` 和编号技能相位。
- 选择 `睡觉`、`坐下` 时优先使用基建模型动作；当前模型没有对应动作时保留模型并使用 Idle 降级。
- 高级展开层可列出当前 skeleton 的原始动画名，方便测试新干员和新皮肤。
- 单次动作使用 Begin/Main/End 队列；循环动作持续到选择 `自动` 或复制人进入高优先级状态。

### 优先级

```text
Death / critical state
        ↓
Manual performance override
        ↓
ONI automatic state mapping
```

死亡、严重状态和资源失败路径由运行时状态机保护。表演结束后回到自动映射，避免留下永久视觉锁定。

## 分阶段实现

### 已完成 — 语义动作层

- 把当前 `OperatorAnimationMapper` 的优先级表提升为可测试的动作目录。
- 为每个复制人保存短生命周期的 `ManualActionOverride`，不写入存档。
- 增加 `Auto / Work / Combat / Sleep / Sit / Stress / Death` 的计划测试。

### 已完成 — 快捷键选择交互

- `Ctrl+F9` 对当前选中的复制人打开转盘。
- 转盘显示当前模型，动作选择按复制人独立保存到运行时。
- `Esc`、再次按快捷键或完成选择会关闭转盘。

### 已完成 — 自动模型协同

- 配置 schema v2 增加 `AutomaticModelSwitching`，默认开启。
- 基建/正面切换经过 0.35 秒稳定窗口，资源下载与解析继续复用现有缓存、租约和取消机制。
- 关闭自动切换后保持玩家手动选择的模型。

### 后续增强

- 在复制人详情面板增加可点击入口。
- 显示目标原始动画名、降级原因和下载状态。
- 增加快捷键自定义。

## 验收标准

- 阿米娅、德克萨斯、凯尔希各验证基建和战斗模型，动作名差异不导致空动画或错误死亡。
- 睡觉、挖矿/工作、攻击、眩晕、死亡在基建和战斗模型中都有可见的确定性降级结果。
- 手动转盘可让一个复制人播放循环和单次动作，其他复制人的自动状态不受影响。
- 真实死亡/压力状态抢占手动表演；卸载 Mod 或切换干员后清理手动覆盖；模型加载失败时保持当前可用模型并抑制同状态重试。
- 全流程只读取当前选择所需的资源，单项资源仍遵守本机开发不下载超过 100 MB 依赖的限制。

2026-07-15 实机验收使用同一存档中的德克萨斯、阿米娅、凯尔希和能天使：移动、挖矿/工作、攻击/技能、睡觉、眩晕与死亡入口均产生确定性日志映射，测试结束后四个复制人恢复自动状态。转盘属于视觉表演入口，不改变 ONI 的实际工作、压力、生命或死亡状态。
