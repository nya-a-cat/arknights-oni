# PRTS 游戏资产与 ONI Mod 架构审计

审计时间：2026-07-14 至 2026-07-15

审计范围：PRTS 当前公开干员目录、torappu 资产存储、Spine 运行时兼容性，以及现有 ONI Mod 的渲染、构建、界面和清单文件。

文档状态：**调查基线。** 本文保存审计时已经观察或运行验证的事实，以及由这些事实形成的目标架构。站点目录、资源版本和网络表现属于时间敏感数据。

状态定义：

- **已验证**：有本地文件、浏览器观察、批量探针、解析器输出或游戏日志支撑。
- **架构决定**：第一阶段采用的行为合同，仍需要实现与验收证据。
- **待验证**：缺少当前实现或当前游戏运行证据。

第一阶段的实施和验收合同见 [第一阶段架构与验收规范](./phase1_architecture_and_acceptance.md)。

## 1. 业务结论

- **架构决定**：第一阶段采用本地单机、按选择下载方案，资源保存策略由玩家选择“按需缓存”或“永久保留已下载资源”。
- **架构决定**：Mod 继续使用 C#。资源层和渲染层需要重构，现有 Harmony 入口和替换复制人外观的业务目标可以保留。
- **已验证**：审计时 PRTS 目录共 449 个公开干员、954 组皮肤、2815 个 Spine 模型。该规模要求按选择获取资源。
- **已验证**：抽样和解析资源使用 Spine 3.8.99，官方 Spine 3.8 C# runtime 可以解析当前样本。
- **已验证**：审计时的仓库渲染器缺少 clipping、多图集页、槽位混合模式和双色着色支持，无法完整覆盖实际样本。
- **架构决定**：使用“C# 实时渲染 + 已安装 Chrome 按需烘焙”的混合路线，本机无需安装 Unity Editor。

## 2. 浏览器与站点证据

### 2.1 浏览器环境

- WSL 中已有 `playwright-cli 0.1.13`，直接启动时的原始阻塞为：

  ```text
  Chromium distribution 'chrome' is not found at /opt/google/chrome/chrome
  Run "npx playwright install chrome"
  ```

- 审计复用了 Windows 已安装的 Chrome `150.0.7871.114`，通过独立临时用户目录和 CDP 端口 9224 控制网页，没有安装额外浏览器。

### 2.2 PRTS 页面结构

- 阿米娅页面：<https://prts.wiki/w/%E9%98%BF%E7%B1%B3%E5%A8%85>
- 干员目录：<https://static.prts.wiki/charinfo/charId20260604.js>
- Spine Viewer 组件：<https://static.prts.wiki/widgets/production/SpineViewer.wPma3lCQ.js>
- 资产根地址：<https://torappu.prts.wiki/asset>
- 阿米娅页面的 Spine 根节点为 `#spine-root`，其 `data-id` 是 `char_002_amiya`。
- Viewer 读取 `meta.json`，随后加载 `.skel`、`.atlas` 和图集 PNG。
- 页面使用官方 Spine WebGL runtime，画布默认 1000×1000，PMA 默认开启。
- Viewer 会长期缓存已加载资源，组件内未观察到明确的资源释放流程。该行为不适合直接照搬到 ONI 长时间存档中。

## 3. 目录与元数据契约

### 3.1 公开目录

- `charId20260604.js` 当前包含 449 条记录和 449 个唯一 `char_id`。
- 本地验证页保存该 19,318 B 轻量目录快照，支持按中文名或 `char_id` 搜索，并将唯一匹配自动回填到稳定 `char_id` 字段；使用本地快照规避 Chromium ORB 跨域拦截，目录不可用时保留手动输入入口。
- 展示名不能充当稳定主键。目录中存在 Sharp、Stormeye 等重名，阿米娅形态名称也存在页面显示差异。
- 资源、存档和缓存统一使用 `char_id` 作为身份键。

### 3.2 全量 metadata 普查

- 以并发 8 请求读取 449 个公开干员 metadata，449/449 成功，总耗时约 32.5 秒。
- 游戏内目录快照由 `tools/update_operator_appearance_catalog.py` 生成，限制单份 metadata 1 MiB、全批 16 MiB，并支持逐项暂存和有限重试。本轮 WSL 刷新曾因 TLS 握手限流剩余 2 项，续跑后 449/449 完整生成；正式 JSON 为 185,311 B，临时抓取缓存在生成后已清理。
- 共得到 954 组皮肤、2815 个模型。
- metadata 根字段为 `prefix`、`name`、`skin`；当前模型条目只观察到 `file` 字段，没有发现实际使用的模型级 `skin` 可选值。
- 模型类型统计：基建 924、正面 948、背面 937、战斗 6。
- 组合统计：907 组标准“基建+正面+背面”，30 组“正面+背面”，11 组“基建+正面”，6 组“战斗+基建”。共有 47 组非标准皮肤。
- “战斗”模型仅在当前空（Sora）与浊心斯卡蒂（Skadi the Corrupting Heart）相关资源中出现。
- 客户端必须枚举 metadata 中真实存在的模型，不能假设每个皮肤固定拥有三个模型。

已保存的阿米娅 metadata 展示了实际层级：

```json
{
  "prefix": "https://torappu.prts.wiki/assets/char_spine/char_002_amiya/",
  "name": "阿米娅",
  "skin": {
    "默认": {
      "正面": { "file": "defaultskin/front/char_002_amiya" },
      "背面": { "file": "defaultskin/back/char_002_amiya" },
      "基建": { "file": "defaultskin/build/build_char_002_amiya" }
    }
  }
}
```

直接资源地址由 `prefix + file + 扩展名` 组成。atlas 中的页名决定需要加载的 PNG 文件名，不能用 `.skel` 基名推断唯一贴图。

## 4. 原始资产存储

### 4.1 可用接口

- `/api/v1/version`
- `/api/v1/files/{encodedPath}`
- `/api/v1/files?path=...`
- `/api/v1/manifest/{version}/children`
- `/api/v1/version/{id}/files`

`https://torappu.prts.wiki/asset` 是资产浏览入口；metadata 的直接文件前缀使用 `https://torappu.prts.wiki/assets/...`。实现需要区分浏览入口和静态资源路径。

### 4.2 资产树现状

- `raw/char_spine` 当前有 888 个目录：453 个 `char`、67 个 `token`、368 个 `trap`。
- 其中 4 个 `char` 目录没有出现在公开 449 干员目录：`char_008_owl`、`char_279_knit3`、`char_4229_aphris`、`char_4234_pedro`。
- 第一版选择界面只展示公开 449 干员，避免把内部、未公开或非玩家角色混入 UI。
- 审计时最新资产版本为 352，客户端版本为 2.7.51，资源标识为 `26-07-10-13-49-06_a14b4a`。
- 重复加载时，CDN/API 单次请求曾耗时 45 至 180 秒。下载队列需要低并发、磁盘缓存、重试退避和 `.part` 原子落盘。

## 5. Spine 与图集工程结论

### 5.1 版本和图集

- 当前抽样资源的 Spine skeleton 版本均为 3.8.99。
- 对 449 个默认干员 atlas 的批量检查：448 个是单页图集，`char_1052_kalts2` 是双页图集，两页均为 528×528。
- 130 个图集使用非 2 次幂尺寸；共观察到 34 组尺寸。常见尺寸为 512×512，最大为 1024×1024。
- 10 个样本的动画数量为 5 至 20 个，动画名称并不保证包含 `action`。
- 阿米娅基建模型实际包含 `Default`、`Interact`、`Move`、`Relax`、`Sit`、`Sleep`，因此 ONI 默认外观优先选择基建模型。

### 5.2 C# runtime 验证

- 使用官方 Spine 3.8 C# runtime 解析当前阿米娅默认/正面 3.8.99 资源：67 bones、53 slots、14 animations、1 atlas page。
- 当前 Mod 的真实资源集成测试解析阿米娅默认/基建和播种者/正面；默认/基建为 48 bones、35 slots、6 animations、1 atlas page，缓存索引总量 188,328 B，第二次解析复用缓存且磁盘用量不变。
- 同一解析器读取旧版阿米娅 3.5.51 时得到空 bones、slots 和 animations。缓存入口必须同时校验 skeleton 版本和非空结构。
- `char_1052_kalts2`：315 bones、202 slots、20 animations、2 atlas pages、197 Mesh、2 Path、4 Clipping。
- 该模型的槽位混合模式为 195 Normal、7 Additive；197 个槽位带第二颜色。
- 该模型包含 6013 个双色关键帧，其中 1007 个为非黑色第二颜色。普通单纹理材质无法准确还原。
- 10 个线上样本中，凯尔希异格、能天使、阿米娅近卫和艾露猫含 clipping；Lancet-2 含 path constraint。
- 除凯尔希异格外，样本骨骼边界宽度约 336 至 1084；凯尔希异格达到约 1290。渲染器需要按模型 bounds 自动缩放。

### 5.3 代表性样本

| 样本 | 已验证特征 | 工程用途 |
| --- | --- | --- |
| 当前阿米娅 3.8.99 | 67 bones、53 slots、14 animations、1 atlas page | 普通单页实时路径基线 |
| 旧版阿米娅 3.5.51 | 当前 3.8 解析器得到空 bones、slots、animations | 版本与非空结构双重校验 |
| `char_1052_kalts2` | 315 bones、202 slots、20 animations、2 pages、Mesh/Path/Clipping、Additive、非黑双色帧 | 最复杂的分类和烘焙降级样本 |
| 能天使 | 存在 Clipping attachment | clipping 视觉验收 |
| 阿米娅近卫、艾露猫 | 存在 Clipping attachment | clipping 补充样本 |
| Lancet-2 | 存在 Path attachment/constraint | path 动画验收 |

样本特征的“存在”已经验证，当前 Mod 对这些特征的正确渲染仍属于待验证项。

### 5.4 2026-07-15 浏览器复验

本地 `prts_animation_validator.html` 通过现有 Windows Headless Chrome 150 和 WSL `playwright-cli` CDP 路径读取当前 PRTS 资源。`prts_animation_validation_report.json` 记录 7/7 用例通过、0 失败；6 个正向用例均为 0 失败请求，另有 1 个缺失动画负例：

| 用例 | 实测结果 |
| --- | --- |
| 阿米娅默认/基建 `Move` | Spine 3.8.99，48 bones、35 slots、1 page，帧前进 |
| 阿米娅默认/基建 `Relax` | 同一模型，帧前进 |
| 阿米娅默认/基建 `Sleep` | 同一模型，帧前进 |
| 能天使“午夜邮差”/正面 `Idle` | 126 bones、81 slots、1 page、2 clipping，帧前进 |
| Lancet-2“海岸救援改装”/正面 `Idle` | 58 bones、47 slots、1 page、1 path attachment、1 path constraint，帧前进 |
| `char_1052_kalts2` 默认/正面 | 315 bones、202 slots、2 pages、4 clipping、2 path、6013 双色帧、1007 非黑双色帧，帧前进 |
| 阿米娅缺失动画负例 | `status=failed`、`error.code=missing_animation`，测试按预期失败语义通过 |

报告还验证了 Die 非循环/停末帧合同、Begin/Main/End 相位合同和 `Skill_Loop_2 -> Skill_2` 相位根归一。运行态实测 `Skill_2_Begin -> Skill_2_Idle(loop)`、退出 `Skill_2_End -> Idle`；Die 播放 1 秒后为 `running=false`、`held_last_frame=true`、`track_time=0.9833`。

第一轮联网验证遇到的原始错误为 `net::ERR_CONNECTION_CLOSED`。网页客户端增加 3 次有界重试后，最终 6 个正向用例的 `failed_requests` 均为 0。以上结果证明网页侧 PRTS WebGL 资源与诊断逻辑；ONI C# mesh 的视觉等价性仍需游戏内验证。

## 6. 本地代码与 ONI 环境

### 6.1 当前渲染器缺口

本节记录第一阶段开始前的源码基线；后续实现是否修复这些缺口需要以新测试和 diff 为准。

- `AmiyaDuplicantOverlay.cs` 当前约 547 行，只处理 Region 和 Mesh attachment。
- 当前实现使用单一 `FirstMaterial`，会跳过 clipping，并忽略槽位 blend、双色着色和多图集页。
- 固定比例缩放与现有大尺寸 spritesheet 回退会放大内存占用，难以扩展到完整目录。

### 6.2 构建和游戏环境

- 当前 ONI build 为 722606，Unity 为 6000.3.5f2，目标框架为 .NET Standard 2.1。
- Windows 已安装 .NET SDK 8.0.411；WSL 当前可使用 Mono C# compiler 6.14.1.0，`build.sh` 已实测成功。
- 官方 Spine 3.8 C# runtime 固定到提交 `8b4844bd4b193ba9e54487ed397a777993cbad56`。仓库只保存 42 个必需 C# 文件（496,886 B）、许可证和 README；对 `System.Action` 做限定并将旧 `FlipX` 调用改为 `ScaleX` 后，可针对当前 ONI 引用无警告编译。
- ONI 数据中能找到 `Klei/Unlit`、`Sprites/Default`、`Unlit/Transparent`；没有找到 `Spine/Skeleton Tint Black`。

### 6.3 施工前游戏日志基线

- `mods.json` 中 Mod 当前被禁用，`mod_load_in_progress=true`。
- `mod_info.yaml` 中的 `staticID`、`title`、`description` 被当前加载器报告为未知字段。
- 现有日志没有形成存档内 overlay 成功运行的证据。
- 游戏随后在基础游戏文件 `ocean_poi_ladder_tunnel_rotated.yaml` 第 217 行出现致命解析错误。该错误独立于本 Mod，需要在正式游戏冒烟测试前处理。
- 清单应按当前 Klei 约定拆分：`mod.yaml` 保存 `title`、`description`、`staticID`；`mod_info.yaml` 保存最低 build、版本和 API2 信息。

### 6.4 第一阶段施工后的源码状态

截至 2026-07-15，以下代码已经落地并通过当前 ONI 引用的全量 DLL 编译：

- PLib Options 注册“按需缓存（512 MiB）/永久保留已下载资源”。
- 随 Mod 保存 185,311 B 外观目录快照，覆盖 449 个干员、954 组皮肤和 2815 个模型；Options 支持中文名/`char_id` 搜索及干员、皮肤、模型联动。
- 配置、原子索引、串行下载、64 MiB 单文件限制、SHA-256、512 MiB LRU、离线回退和资源 key 租约。
- `OperatorAssetResolver` 按 metadata 下载 atlas、skel 和 atlas 中实际枚举的所有 PNG 页。
- overlay 接入远端资源解析，并保留轻量内置 Spine、帧缓存和原始复制人的分级失败保护。
- 存档内 `Ctrl+F8` 打开同一 Options；保存外观后现有 overlay 取消旧请求，在新骨骼完整解析后替换资源，失败时保留当前外观。
- C# mesh 已加入 `SkeletonClipping`、多 atlas page、Normal/Additive/Multiply/Screen 子网格材质和 bounds 自动缩放。
- 动画映射、Die 非循环、Begin/Main/End 相位、`Skill_Loop_2 -> Skill_2` 与实际 `Skill_2_Idle` 归一通过 26 项纯逻辑断言；工作、清扫、照料、下落和落地词根已覆盖。
- 缓存索引通过 8 项回归断言，覆盖 LRU、保护集合、同 key 版本路径替换和目录逃逸删除保护。

游戏内可玩主路径已于 2026-07-15 验证：Mod `0.3.1` 从 Steam 正常冷启动并加载，Options 五行布局、中文搜索、皮肤/模型联动和 `Ctrl+F8` 均可用；Cycle 9 中四个复制人能显示阿米娅播种者/正面，并在约 0.57 秒内切换到能天使午夜邮差/正面。稳定参考动画校准修复了视觉高度和脚底基线，解除暂停后的移动、姿态变化和爬梯通过实机观察。C# mesh 尚未实现 Spine 双色 shader；非黑双色模型的 Chrome 按需烘焙并加载 sheet 已列为后续复杂视觉增强，不计入本轮第一阶段代码完成状态。

## 7. 无 Unity 的目标架构

本节全部属于**架构决定**。代码完成状态和验收状态统一记录在 [第一阶段架构与验收规范](./phase1_architecture_and_acceptance.md)及对应测试结果中。

### 7.1 资源层

1. 目录客户端只拉取 449 个公开角色的轻量索引。
2. 玩家选定角色、皮肤和模型后，再下载对应 `meta.json`、`.skel`、`.atlas` 和实际引用的 PNG。
3. 同一时间只运行一个不同资源的网络任务；相同资源键和版本的并发请求共享同一个在途任务。资源请求建议超时 120 秒，最多重试 3 次并指数退避。
4. 文件先写入 `.part`，校验完成后原子重命名，避免中断后把残缺文件当作有效缓存。
5. 缓存键包含资源版本、角色 ID、皮肤、模型和动画，避免 PRTS 更新后复用旧结构。

### 7.2 实时 C# 路径

- 使用 Spine 3.8 C# runtime 负责骨骼动画、Region、Mesh、Path 和 `SkeletonClipping`。
- 按 atlas page 和 blend mode 建立子网格/材质，普通模型使用 ONI 已有 `Klei/Unlit`。
- 加载后扫描 `TwoColorTimeline`。没有非黑双色关键帧的模型直接走实时渲染。
- 相同资源由所有复制人共享 geometry、texture 和 animation data，并用引用计数在最后一个使用者释放时卸载。

### 7.3 Chrome 按需烘焙路径

- 检测到非黑双色关键帧，或实时路径视觉验收失败时，调用本机现有 Chrome 中的 PRTS/Spine WebGL 渲染逻辑生成当前动画缓存。
- 建议帧尺寸 512×512、约 12 fps，只生成当前选择的角色、皮肤、模型和动画。
- 单张 sheet 上限建议 4096 像素，超过上限时分片。
- ONI 只加载当前使用的 sheet，并让多个复制人共享同一份纹理。
- 该路径保留 PRTS 的双色 shader 表现，也避免安装 Unity Editor 和自制 Spine shader bundle。

### 7.4 外观与动画映射

- 默认皮肤优先选 metadata 中的“默认”；模型优先级为“基建、正面、战斗、第一项”。
- 移动优先匹配 `Move`。
- 工作优先匹配 `Interact`、`Attack`、`Skill`。
- 睡眠与坐下优先匹配 `Sleep`、`Sit`。
- 待机优先匹配 `Relax`、`Idle`、`Default`。
- 压力与倒地动作根据实际存在的 `Stun`、`Die`、`Idle` 选择。
- 动画匹配只基于实际动画列表，缺失时降级到模型的第一个安全循环动画。

### 7.5 失败保护

- 新外观完整解析并创建材质前，继续显示 ONI 原始复制人外观。
- 网络、解析、Chrome 烘焙或纹理创建失败时保留原始外观，并在详情面板展示简短错误和重试入口。
- 现有 130 张、约 168 MB 的全动画大图缓存不继续扩展。其全部解码后的内存理论上可能接近 5 GB。

## 8. 权利边界

- PRTS 页脚说明游戏图片、文本原文等版权归鹰角网络或其关联公司所有；站点其他内容通常使用 CC BY-NC-SA。
- 仓库保存的 `SPINE-RUNTIME-LICENSE.txt` 是 2019-05-01 的 Spine Runtimes License Agreement，要求再分发保留许可与版权声明，并包含与 Spine Editor 许可相关的使用条件。
- Spine 3.8 runtime 固定到官方仓库提交 `8b4844bd4b193ba9e54487ed397a777993cbad56`；来源、许可文本和本地兼容补丁分别记录在 `lib/SPINE-RUNTIME-SOURCE.txt`、`SPINE-RUNTIME-LICENSE.txt` 和 `lib/SPINE-RUNTIME-README.md`。
- GitHub 公开范围只包含原创源码、测试、开发文档、轻量目录元数据和带原始许可文本的第三方代码；游戏图片、导出动画帧、Spine 角色资源和复制的 PRTS 网页构建产物全部排除。
- 原创代码当前未授予额外开源许可证。Spine runtime 继续适用 Esoteric Software 的独立许可与 Spine Editor 条件；仓库可见性不改变用户或再分发者的许可责任。

## 9. 剩余验证

- 已完成：ONI 模组页 Options、`Ctrl+F8` 入口、中文搜索、皮肤/模型联动和四个现有复制人的实时切换。
- 已完成：阿米娅播种者/正面与能天使午夜邮差/正面的普通 C# 实时路径、配置持久化和缓存内往返切换。
- 待完成：用指定 clipping 样本和 Lancet-2 对照网页结果验证 path constraint 的视觉一致性。
- 在 ONI 中用 `char_1052_kalts2` 验证双图集页、Additive blend 和 Chrome 双色烘焙降级。
- 已完成：当前 ONI build 的 Cycle 9 存档内冒烟测试；旧 YAML 启动阻塞本轮未复现。
- 已完成：基建模型脚底对齐、移动、姿态变化和爬梯实机复验；Steam 冷启动没有进入 Mod Safe Mode。
- 已完成：共享下载中单调用者取消隔离、索引内同长度缓存损坏的 SHA-256 恢复。
- 验证全局选择跨重启持久化、资源租约释放和后续每个复制人独立选择方案；当前版本按设计使用全局选择。
- 实测 20 个复制人共享一个资源和使用多个不同资源时的显存、托管内存及卸载行为。

当前路线不要求安装 Unity，也不要求下载 PRTS 全量资产。

## 10. 证据索引与边界

| 结论 | 状态 | 本地或运行证据 |
| --- | --- | --- |
| 阿米娅 metadata 层级 | 已验证 | `site_probe/char_002_amiya_meta.json` |
| 阿米娅 4 皮肤、每皮肤实际模型与远端路径 | 已验证 | `assets/spine/amiya/prts_spine_index.json` |
| PRTS Viewer 页面结构和导出能力 | 已验证 | `site_probe/amiya.html`、`site_probe/SpineViewer.Di5NOLW8.js`、`preview/prts_frame_exporter.html` |
| 4 个角色、6 个实时动画/特征用例和 1 个缺失动画负例 | 已验证 | `preview/prts_animation_validation_report.json`，Playwright 7/7 通过 |
| 干员中文名搜索与 `char_id` 回填 | 已验证 | PRTS 轻量目录不少于 400 条；阿米娅和能天使映射由 Playwright 断言 |
| 130 个阿米娅动画 sheet、8 fps、1000×1000 帧 | 已验证的旧原型 | `assets/frames/amiya/library/index.json`；该方案不继续扩展 |
| 当前 overlay 的 Region/Mesh、单材质实现 | 已验证的旧源码基线 | `src/AmiyaDuplicantOverlay.cs` |
| Spine runtime 许可文本 | 已验证的仓库文件 | `SPINE-RUNTIME-LICENSE.txt` |
| 449/954/2815 和 atlas/样本统计 | 已验证的 2026-07-14 至 2026-07-15 审计输出 | 本文保存的批量探针结果；站点变化后需要重新普查 |
| 两种资源策略、缓存索引和混合渲染 | 架构决定 | `phase1_architecture_and_acceptance.md` |
| 当前 ONI 游戏内成功运行 | 已验证 | Mod `0.3.1` Steam 冷启动日志、Cycle 9 四复制人截图、阿米娅与能天使切换及移动/爬梯日志 |
| 浏览器侧代表性资源和动画诊断 | 已验证 | Playwright 报告覆盖帧前进、clipping、path、多页、双色和相位合同 |
| ONI 内代表性动画视觉正确 | 部分验证 | 阿米娅播种者与能天使午夜邮差已通过；clipping/path/多页/非黑双色专项仍待验证 |

审计数字描述审计时快照。以后再次抓取 PRTS 时，应保留新的目录日期、资源版本、成功/失败数和请求耗时，避免用旧快照覆盖历史证据。
