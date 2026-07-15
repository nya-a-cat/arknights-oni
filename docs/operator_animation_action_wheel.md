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

### 第一版入口

1. 玩家选中一个复制人。
2. 按住可配置的表演快捷键打开转盘。
3. 八个扇区对应 `自动`、`待机`、`移动`、`工作/挖矿`、`攻击/技能`、`睡觉`、`坐下`、`眩晕/死亡`。
4. 松开快捷键确认，`Esc` 取消。
5. `自动` 立即交还给 ONI 当前状态；死亡状态始终拥有最高优先级。

### 动作选择

- 转盘只显示当前 skeleton 可解析的语义动作。
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

### P1 — 语义动作层

- 把当前 `OperatorAnimationMapper` 的优先级表提升为可测试的动作目录。
- 为每个复制人保存短生命周期的 `ManualActionOverride`，不写入存档。
- 增加 `Auto / Work / Combat / Sleep / Sit / Stress / Death` 的计划测试。

### P1 — 选择交互

- 在复制人详情交互中增加转盘入口，快捷键只作为备用入口。
- 转盘显示动作可用性、当前模型和降级结果。
- 转盘打开时暂停输入捕获，关闭后恢复 ONI 原有选择操作。

### P2 — 模型协同

- 为需要战斗姿态的表演增加可选的模型切换策略：保留当前模型、自动切换正面模型、使用固定模型。
- 模型切换完成后复用已下载资源租约，避免重复下载。
- 记录切换前后的资源键、动画计划和失败降级原因。

## 验收标准

- 阿米娅、德克萨斯、凯尔希各验证基建和战斗模型，动作名差异不导致空动画或错误死亡。
- 睡觉、挖矿/工作、攻击、眩晕、死亡在基建和战斗模型中都有可见的确定性降级结果。
- 手动转盘可让一个复制人播放循环和单次动作，其他复制人的自动状态不受影响。
- 复制人死亡、卸载 Mod、切换干员或资源失败后，手动覆盖均能清理。
- 全流程只读取当前选择所需的资源，单项资源仍遵守本机开发不下载超过 100 MB 依赖的限制。
