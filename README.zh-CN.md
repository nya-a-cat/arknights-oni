<div align="center">

# arknights-oni

**把明日方舟干员带进《缺氧》。**

当前版本已经支持干员外观，后续计划加入语音、基建家具、敌人和特效。明日方舟会作为未来通用 ONI 内容框架的第一套参考实现。

[English](./README.md) · [简体中文](./README.zh-CN.md) · [路线图](#当前进度与-roadmap) · [安装](#安装)

[![版本](https://img.shields.io/badge/version-0.3.2--alpha.1-6d5dfc)](https://github.com/nya-a-cat/arknights-oni/releases/tag/v0.3.2-alpha.1)
![ONI 实测](https://img.shields.io/badge/ONI_tested-740622-ea6b35)
![C#](https://img.shields.io/badge/C%23-Unity-512BD4?logo=csharp&logoColor=white)
[![仓库](https://img.shields.io/badge/GitHub-arknights--oni-181717?logo=github)](https://github.com/nya-a-cat/arknights-oni)

</div>

![Arknights Operators Alpha 实机拼图](./docs/images/arknights-oni-alpha-v0.3.2-workshop.png)

> [!IMPORTANT]
> 当前 `0.3.2-alpha.1` 实现的是 **Arknights Operators（明日方舟干员）** 模块：把可选择的干员 Spine 外观覆盖到复制人上，并将移动、工作、休息、睡眠、压力和死亡状态映射到对应动画。
>
> 当前外观选择是全局设置，会应用到所有复制人。
>
> 这是 Alpha 测试版本，部分游戏内集成场景仍在专项验证中。
>
> Steam 创意工坊标题为 **Arknights Operators / 明日方舟干员 [Alpha]**；游戏内“模组”菜单显示为 **Arknights Operators（明日方舟干员）**。

当前版本已经在 ONI build 740622、四个复制人的隔离测试存档中完成实机冒烟验证，并分别用 `Surtr`、`阿米娅`、`テキサス` 完成英文、中文、日文搜索与热切换。

## 项目特色

- 在游戏内按中文名、英文名、日文名、PRTS 重定向别名或 `char_id` 搜索 449 个干员。
- 设置界面自动选择中文或英文，干员显示名会结合当前游戏语言和 PRTS 已提供的中文、日文、英文元数据。
- 联动选择干员、皮肤和模型。
- 从模组设置页或已加载存档中的 `Ctrl+F8` 打开同一选择界面，保存后实时切换外观。
- 使用 C# 实时渲染 Spine 3.8 Region/Mesh、clipping、多 atlas page 和常用 blend mode。
- 将 ONI 的移动、工作、休息、睡眠、压力和死亡状态映射到可用的干员动画。
- 提供 512 MiB 按需 LRU 缓存与永久保留已下载资源两种策略。
- 合并相同资源的并发请求，同时允许每个复制人独立取消自己的等待。
- 使用 HTTPS 来源限制、临时文件、SHA-256 索引校验和 64 MiB 单文件上限保护下载过程。
- PRTS 元数据或文件下载失败时，通过版本化清单和按干员拆分的 GitHub Release 包恢复目录内全部 449 个干员。
- 外观加载失败时依次回退到原始复制人外观、可选内置 Spine 资源和旧帧路径。

## 安装

### 前提

- 通过 Steam 安装的 Windows 版《缺氧》。
- WSL 中可运行 Mono `mcs`。

仓库不会自动安装编译器、浏览器或大型依赖。

### 构建并安装

```bash
cd arknights_oni_mod_work/ArknightsOperatorsMod
./build.sh
./install_local.sh
```

默认本地 Mod 目录：

```text
C:\Users\<你的用户名>\Documents\Klei\OxygenNotIncluded\mods\Local\ArknightsOperatorsMod
```

使用 `ONI_GAME_ROOT` 指定游戏目录，使用 `ONI_LOCAL_MOD_DIR` 指定安装目标。

早期本地原型使用 `AmiyaDuplicantMod` 目录。默认安装脚本会在新目标尚未存在时迁移旧本地目录及其配置/缓存。隐藏的旧 `staticID` 继续作为兼容键，使现有存档能够识别改名后的 Mod。

> [!TIP]
> 从 Steam 启动游戏，在“模组”中启用 **Arknights Operators（明日方舟干员）**，并按提示重启。部分 Steam 环境中直接启动游戏 EXE 会触发 Klei 的 Mod Safe Mode。

Git 源码仓库不包含明日方舟图片、Spine 骨骼、atlas 或复制的 PRTS 网页构建产物。PRTS 仍是按需获取的主来源；其元数据合同或文件下载失败时，Mod 会读取固定版本的备用清单，并从项目 GitHub Release 获取当前选择干员的资源包。完整 449 干员快照由 GitHub Actions 生成，贡献者的电脑无需全量下载。

现有 64 MiB 限制只用于单个 Spine 源文件的下载安全检查。Release 干员包只在选中该干员且主来源失败时获取，另有 512 MiB 的异常响应技术上限。开发时提到的 100 MB 是本机磁盘空间偏好；云端生成器只报告超过该体积的包并继续处理。

## 资源策略

| 模式 | 行为 | 适用场景 |
| --- | --- | --- |
| 按需缓存（推荐） | 只获取当前选择的外观；缓存超过 512 MiB 时清理最久未使用且未被引用的文件 | 控制磁盘占用 |
| 永久保留已下载资源 | 只获取当前选择的外观；成功缓存后不执行容量清理 | 希望已访问外观长期离线可用 |

两种模式都不会预下载完整干员目录。

备用干员包沿用同一保留策略：按需模式会在 512 MiB LRU 预算内清理未使用的包，永久模式会保留成功下载的包。

清单合同、云端生成流程、信任边界和发布检查清单见 [GitHub Release 备用资源设计](./docs/github_release_asset_fallback.md)。

## 当前进度与 Roadmap

### 干员

- [x] 支持中文、英文、日文、重定向别名和 `char_id` 的 449 干员目录
- [x] 干员、皮肤和模型联动选择
- [x] 从 Options 和 `Ctrl+F8` 实时切换
- [x] 运行时动画映射与地面对齐
- [ ] 基建/战斗语义动画档案，以及让单个复制人手动表演的动作转盘
- [ ] 每个复制人独立设置外观与语音
- [ ] 干员语音、语种选择、试听、冷却和优先级
- [ ] 外观预览、收藏、预设和打印舱分配池

### 明日方舟内容

- [ ] 基建家具、房间主题和可动装饰
- [ ] 敌人及生物外观内容包
- [ ] 技能、战斗、工作和环境特效
- [ ] `operator`、`voice`、`furniture`、`enemy`、`effect` 类型化内容包

### 基础质量

- [x] 干员设置界面自动使用中文或英文
- [x] 基于 PRTS 百科元数据的中文、英文、日文干员名搜索
- [x] 全干员版本化备用清单、带校验的 Release 包加载器和 GitHub Actions 分片生成器
- [ ] 生成、检查并发布首个 449 干员 `assets-v1.0.0` 快照
- [ ] 把其余运行时错误与诊断迁移到 ONI `STRINGS`，并增加更多界面语种
- [ ] 缓存管理器、下载状态和诊断导出
- [ ] 版本化配置迁移与目录更新
- [ ] 与其他外观 Mod 的兼容控制

### 长期框架方向

- [ ] 明日方舟内容管线成熟后，将 `arknights-oni` 作为通用 ONI 内容框架进行一次正式重估
- [ ] 把稳定的内容生命周期、缓存、选择、事件映射和内容包合同抽取为可复用核心
- [ ] 保留明日方舟作为第一套参考内容包和兼容性测试集
- [ ] 评估其他游戏的内容包，**BanG Dream!** 作为示例候选方向之一

详细优先级、验收标准、性能限制和资源使用边界见[完整代码审查与路线图](./docs/code_review_and_roadmap_20260715.md)。

## 开发

```bash
cd arknights_oni_mod_work/ArknightsOperatorsMod
./build.sh
./tests/run_operator_animation_mapper_tests.sh
./tests/run_operator_appearance_catalog_tests.sh
./tests/run_mod_localization_tests.sh
./tests/run_resource_index_tests.sh
python3 ./tests/test_fallback_release_builder.py
./tests/run_operator_fallback_tests.sh
./tests/run_operator_asset_resolver_integration.sh
```

最后一项集成测试会下载 PRTS 的真实小型 fixture；备用源测试使用内存中的 Release 包并模拟主来源失败；其余测试只使用本地代码和 fixture。

## 仓库结构

- `arknights_oni_mod_work/ArknightsOperatorsMod/src`：Mod 入口、设置、缓存、资源解析、渲染和动画映射。
- `arknights_oni_mod_work/ArknightsOperatorsMod/tests`：逻辑测试和真实小资源集成测试。
- `arknights_oni_mod_work/ArknightsOperatorsMod/lib`：PLib、固定版本 Spine C# runtime 源码及来源说明。
- `docs`：PRTS 资产研究、架构验收记录和详细路线图。
- `PROGRESS.md`：只追加的实现与验证日志。

## 项目边界与第三方组件

这是一个非商业同人项目，与 Klei、鹰角网络及 PRTS Wiki 没有隶属或背书关系。游戏及角色相关权利归各自权利人所有。公开 Git 仓库包含原创 Mod 源码、测试、开发文档、轻量目录元数据、单独许可的第三方代码，以及由真实游戏截图排版而成的宣传图；运行时美术和动画资源由用户按需从 PRTS 获取，主来源失败后可从项目的版本化 Release 快照获取。

原创代码当前没有授予额外的开源许可证。PLib、Spine runtime 和目录元数据分别适用各自的许可与来源说明，详见 [THIRD_PARTY_NOTICES.md](./THIRD_PARTY_NOTICES.md) 和 [DATA_NOTICE.md](./DATA_NOTICE.md)。
