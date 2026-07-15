# Arknights Operators for Oxygen Not Included

`Arknights Operators（明日方舟干员）` 是一个《缺氧》本地 Mod。它把可选择的明日方舟 Spine 外观覆盖到复制人上，并根据复制人的移动、工作、休息、睡眠、压力和死亡状态切换动画。

当前版本：`0.3.1`。已在 ONI build 722606、四个复制人的隔离测试存档中完成实机冒烟验证。

## 已实现

- 游戏内按中文名称或 `char_id` 搜索 449 个干员。
- 联动选择干员、皮肤和模型。
- 模组设置页与存档内 `Ctrl+F8` 共用同一设置界面，保存后实时切换当前复制人。
- 两种资源策略：按需缓存（512 MiB LRU）与永久保留已下载资源。
- 同一资源的并发请求合并；单个调用者取消不会中止其他复制人的共享下载。
- 下载采用 HTTPS 来源限制、临时文件、SHA-256 索引校验和 64 MiB 单文件上限。
- Spine 3.8 Region/Mesh、clipping、多 atlas page 和常用 blend mode 的实时 C# 渲染。
- 原始复制人外观、可选内置 Spine 和旧帧路径的分级失败保护。

当前外观选择是全局设置，会应用到所有复制人。每个复制人独立选择、正式多语言资源、自动按行为切换基建/正面模型和复杂双色视觉等价性列在[代码审查与路线图](docs/code_review_and_roadmap_20260715.md)中。

## 安装

前提：Windows 版《缺氧》已通过 Steam 安装；WSL 中可运行 Mono `mcs`。仓库不会自动下载编译器、浏览器或大型依赖。

```bash
cd arknights_oni_mod_work/AmiyaDuplicantMod
./build.sh
./install_local.sh
```

默认安装到：

```text
C:\Users\element\Documents\Klei\OxygenNotIncluded\mods\Local\AmiyaDuplicantMod
```

可用 `ONI_GAME_ROOT` 指定游戏目录，用 `ONI_LOCAL_MOD_DIR` 指定安装目录。进入 Steam 启动的游戏后，在“模组”中启用 `Arknights Operators（明日方舟干员）` 并按提示重启。直接双击游戏 EXE 在部分 Steam 环境会触发 Klei 的 Mod Safe Mode，因此测试和日常使用均建议从 Steam 启动。

仓库不分发明日方舟图片、Spine 骨骼、atlas 或 PRTS 网页构建产物。首次选择外观时，Mod 从 PRTS 资源域按需获取当前外观需要的小文件。单文件硬上限为 64 MiB，不会下载 100 MiB 以上的单项依赖。

## 资源策略

| 设置 | 行为 | 适用场景 |
| --- | --- | --- |
| 按需缓存（推荐） | 只获取当前选择资源；缓存超过 512 MiB 时清理最久未使用且未被引用的文件 | 控制磁盘占用 |
| 永久保留已下载资源 | 只获取当前选择资源；成功缓存后不执行容量清理 | 希望已访问外观长期离线可用 |

两种设置都不会预下载全量干员资源。

## 验证

```bash
cd arknights_oni_mod_work/AmiyaDuplicantMod
./build.sh
./tests/run_operator_animation_mapper_tests.sh
./tests/run_operator_appearance_catalog_tests.sh
./tests/run_resource_index_tests.sh
./tests/run_operator_asset_resolver_integration.sh
```

最后一项会访问 PRTS 的真实小型测试资源。其余测试只使用本地代码和 fixture。

## 目录

- `arknights_oni_mod_work/AmiyaDuplicantMod/src`：Mod、设置、缓存、资源解析、渲染与动画映射。
- `arknights_oni_mod_work/AmiyaDuplicantMod/tests`：纯逻辑测试和真实小资源集成测试。
- `arknights_oni_mod_work/AmiyaDuplicantMod/lib`：PLib 以及固定版本的 Spine C# runtime 源码和来源说明。
- `docs`：PRTS 资产审计、架构验收规范和代码审查路线图。
- `PROGRESS.md`：按任务追加的开发与验证记录。

## 权利与第三方组件

本仓库与 Klei、Hypergryph/鹰角网络及 PRTS Wiki 没有隶属或背书关系。游戏及角色相关权利归各自权利人所有。公开仓库只包含原创 Mod 源码、测试、开发文档、轻量目录元数据和单独许可的第三方代码；美术和动画资源由用户运行时按需获取。

原创代码当前没有授予额外的开源许可证。PLib、Spine runtime 和目录元数据适用各自的许可与来源说明，详见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) 和 [DATA_NOTICE.md](DATA_NOTICE.md)。使用或分发前请确认自己满足 Spine Runtimes/Spine Editor 的许可条件。
