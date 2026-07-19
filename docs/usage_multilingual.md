# Arknights Operators usage / 使用说明 / 使い方

QQ Alpha testing group / Alpha 测试交流群 / Alpha テスト交流グループ：`785437890`

Steam Workshop / Steam 创意工坊 / Steam ワークショップ：[Arknights Operators / 明日方舟干员 [Alpha]](https://steamcommunity.com/sharedfiles/filedetails/?id=3765340857)

Version scope / 版本范围 / バージョン範囲：the published Stable package is `0.3.2-alpha.2`; the current local development version is `0.3.3-dev` and will reach the Workshop after Dev, RC, and Stable validation. / 已发布 Stable 包为 `0.3.2-alpha.2`；当前本地开发版本为 `0.3.3-dev`，完成 Dev、RC 与 Stable 验证后才会进入创意工坊。/ 公開済み Stable パッケージは `0.3.2-alpha.2`、現在のローカル開発版は `0.3.3-dev` です。Dev、RC、Stable の検証後に Workshop へ反映されます。

## 中文

### 安装与启用

1. 在 Steam 创意工坊订阅本 Mod。
2. 从 Steam 启动《缺氧》，进入“模组”并启用 `Arknights Operators（明日方舟干员）`。
3. 按游戏提示重启。首次选择某个外观时需要联网获取该干员所需的小型资源文件。

### 游戏内操作

- 选中一个复制人，按 `Ctrl+F8`：通过每页 20 张的 96px 干员头像卡选择干员；只按需获取当前页头像，右侧显示放大头像，缺图或离线时显示名称。头像用于识别干员，不随皮肤变化；切换皮肤或模型会在场景复制人上自动加载 Spine 预览，确认后再应用，并可微调该外观的显示比例。
- 按 `Ctrl+Shift+F8`：打开资源策略、缓存容量、自动模型切换和默认外观大小设置。
- 选中一个复制人，按 `Ctrl+F9`：打开动作转盘；中心按钮恢复 ONI 自动状态映射。
- 搜索支持中文名、英文名、日文名、PRTS 重定向别名和 `char_id`。
- `0.3.3-dev` 的轻量源目录记录 449 个干员，选择界面只显示 420 个具备基建移动模型的干员；29 个纯战斗角色和 30 个缺少基建走路模型的皮肤会被隐藏。
- “恢复全局默认”会清除当前复制人的单独外观覆盖。

### 资源策略

- `按需缓存`：仅获取使用到的外观。`0.3.3` 中可填写 `128–2000 MiB` 的整数容量，默认 `512 MiB`。保存更小容量后立即执行 LRU 清理；当前使用、正在下载和持有租约的资源受到保护。
- `永久保留已下载资源`：仅获取使用到的外观，并长期保留成功缓存的文件。容量输入值会保留，切回按需缓存时继续使用。
- `默认外观大小`：`100%` 对应旧版大小，`0.3.3` 默认 `125%`，可填写 `75–200%`。`Ctrl+F8` 中的比例按实际 `char_id + 皮肤 + 模型` 保存，因此基建与战斗模型可分别校准；同一外观由多个复制人共享该比例。“恢复默认比例”会重新继承全局默认。修改只更新视觉渲染、脚底基线、水平居中和翻转，不改变 ONI 碰撞体，也不会重新下载资源。
- 两种模式都不会预下载完整的 449 干员资源库。
- 用户容量不会放宽 `64 MiB` 单个 Spine 源文件限制或 `512 MiB` 备用包安全上限。

## English

### Install and enable

1. Subscribe to the Mod in Steam Workshop.
2. Launch Oxygen Not Included through Steam, open Mods, and enable `Arknights Operators（明日方舟干员）`.
3. Restart when prompted. The first use of an appearance requires a network connection for its small resource files.

### In-game controls

- Select a duplicant and press `Ctrl+F8`: choose from pages of 20 cached 96px operator-avatar cards. Only the visible page is fetched on demand; the selected avatar is enlarged on the right and offline or missing images use name cards. Avatars identify operators and do not change with skins. Changing skin or model automatically loads the in-world Spine preview before applying, and the appearance size can be adjusted live.
- Press `Ctrl+Shift+F8`: open resource policy, cache capacity, automatic model switching, and default appearance size settings.
- Select a duplicant and press `Ctrl+F9`: open the action wheel; use the centre button to restore automatic ONI state mapping.
- Search accepts Chinese, English, or Japanese names, PRTS redirect aliases, and `char_id`.
- The `0.3.3-dev` metadata snapshot contains 449 operators. The picker exposes 420 operators with a base movement model and hides 29 combat-only characters plus 30 skins without a base walking model.
- `Use global default` removes the selected duplicant's individual appearance override.

### Resource strategies

- `On-demand cache`: fetch only used appearances. In `0.3.3`, enter an integer capacity from `128` to `2000 MiB`; the default is `512 MiB`. Saving a smaller capacity runs LRU maintenance immediately. Active, downloading, and leased resources stay protected.
- `Keep downloaded resources`: fetch used appearances and retain successfully cached files. The capacity value stays saved and is reused after switching back to on-demand caching.
- `Default appearance size`: `100%` is the previous size. `0.3.3` defaults to `125%` and accepts `75–200%`. The `Ctrl+F8` value is saved by actual `char_id + skin + model`, so base and combat models can be calibrated separately and duplicants using the same appearance share one value. `Restore default size` resumes the global default. Changes update visual scale, foot baseline, horizontal centring, and facing only; ONI collision stays unchanged and no asset is downloaded again.
- Neither strategy pre-downloads the complete 449-operator resource library.
- The user capacity does not relax the `64 MiB` per-Spine-source-file limit or the `512 MiB` fallback-package safety ceiling.

## 日本語

### インストールと有効化

1. Steam Workshop で本 Mod をサブスクライブします。
2. Steam から Oxygen Not Included を起動し、Mods で `Arknights Operators（明日方舟干员）` を有効にします。
3. 表示された案内に従って再起動します。外観を初めて使用するときは、小容量の必要ファイルを取得するためネット接続が必要です。

### ゲーム内操作

- 複製人間を選択して `Ctrl+F8`：1 ページ 20 枚のキャッシュ対応 96px オペレーターアイコンから選択します。表示中のページだけを必要時に取得し、右側で選択中のアイコンを拡大表示します。オフラインまたは画像がない場合は名前カードを表示します。アイコンはオペレーター識別用で、コーデでは変化しません。コーデまたはモデルを切り替えると、適用前にゲーム画面上の Spine プレビューが自動で読み込まれ、表示倍率も調整できます。
- `Ctrl+Shift+F8`：リソース方式、キャッシュ容量、自動モデル切替、既定の外観サイズを開きます。
- 複製人間を選択して `Ctrl+F9`：アクションホイールを開きます。中央ボタンで ONI の自動状態マッピングに戻ります。
- 中国語名、英語名、日本語名、PRTS リダイレクト別名、`char_id` で検索できます。
- `0.3.3-dev` のメタデータには 449 人が含まれます。選択画面には基地移動モデルを持つ 420 人だけを表示し、戦闘専用 29 人と基地歩行モデルのない 30 コーデを非表示にします。
- `グローバル既定値を使用` は、その複製人間の個別外観設定を解除します。

### リソース保存方式

- `オンデマンドキャッシュ`：使用した外観だけを取得します。`0.3.3` では `128–2000 MiB` の整数を設定でき、既定値は `512 MiB` です。小さい容量を保存すると LRU 整理が直ちに実行され、使用中、ダウンロード中、リース中のリソースは保護されます。
- `ダウンロード済みリソースを保持`：使用した外観だけを取得し、正常にキャッシュしたファイルを保持します。容量値は保存され、オンデマンド方式へ戻したときに再利用されます。
- `既定の外観サイズ`：`100%` は従来のサイズです。`0.3.3` の既定値は `125%` で、`75–200%` を設定できます。`Ctrl+F8` の倍率は実際の `char_id + コーデ + モデル` ごとに保存されるため、基地モデルと戦闘モデルを別々に調整でき、同じ外観を使う複製人間で共有されます。既定値へ戻すとグローバル設定を再び継承します。表示倍率、足元基準、水平中央、向きだけを更新し、ONI の当たり判定とダウンロード済みリソースは変更しません。
- どちらの方式も 449 オペレーターの全リソースを事前ダウンロードしません。
- ユーザー設定容量は、Spine ソース 1 ファイルあたり `64 MiB` の制限とフォールバックパッケージの `512 MiB` 安全上限を変更しません。

## Alpha notes / Alpha 说明 / Alpha 注意事項

- 中文：现有四干员实机证据来自 `0.3.2-alpha.1` 候选包和 Oxygen Not Included `740622`。动作转盘仅控制视觉表演，不改变工作、生命、压力、碰撞或模拟状态。发布包包含代码、轻量目录与第三方声明，外观资源按需获取。问题反馈请加入 QQ 群 `785437890` 或使用 GitHub Issues。
- English: The recorded four-operator game test used the `0.3.2-alpha.1` candidate on Oxygen Not Included build `740622`. Action-wheel entries only control visual performances; jobs, health, stress, collision, and simulation state remain unchanged. The package contains code, lightweight catalog metadata, and third-party notices; appearance resources are retrieved on demand. Send feedback through QQ group `785437890` or GitHub Issues.
- 日本語：4 人のオペレーターに関する実機記録は、`0.3.2-alpha.1` 候補版と Oxygen Not Included `740622` を使用しています。アクションホイールは表示上の演技だけを制御し、作業、生命、ストレス、当たり判定、シミュレーション状態は変更しません。配布パッケージにはコード、軽量カタログ、第三者表記が含まれ、外観リソースは必要時に取得します。フィードバックは QQ グループ `785437890` または GitHub Issues へお寄せください。
