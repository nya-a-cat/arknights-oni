# Alpha v0.3.2 workshop image layout

The workshop image is a deterministic montage of four crops from one post-fix Steam screenshot. The capture shows four different operators at the same time, with the vanilla duplicants hidden and all feet aligned to the same floor. The renderer only crops and scales screenshot pixels, draws borders and colour blocks, and adds text. It creates no new character, environment, animation, or game-effect pixels. The header explicitly calls out the automatically localized UI and Chinese, English, and Japanese operator search.

GPT ImageGen was not used for this image, so there is no generation prompt. The complete inputs are stored under [`source/alpha-v0.3.2`](./source/alpha-v0.3.2), and the renderer is [`tools/render_alpha_promo.ps1`](../../tools/render_alpha_promo.ps1).

## Sources and labels

| Source | Label | Crop (`x, y, width, height`) | Bytes | SHA-256 |
| --- | --- | --- | ---: | --- |
| `20260716021045_1.jpg` | `TEXAS / 德克萨斯` | `248, 190, 175, 277` | 457,411 | `4F209411C98F439B6856D898259DAF5ADB3CEFC76ED04C45A8F10E78B70E97BF` |
| `20260716021045_1.jpg` | `AMIYA / 阿米娅` | `503, 190, 175, 277` | 457,411 | `4F209411C98F439B6856D898259DAF5ADB3CEFC76ED04C45A8F10E78B70E97BF` |
| `20260716021045_1.jpg` | `KAL'TSIT / 凯尔希` | `720, 190, 175, 277` | 457,411 | `4F209411C98F439B6856D898259DAF5ADB3CEFC76ED04C45A8F10E78B70E97BF` |
| `20260716021045_1.jpg` | `EXUSIAI / 能天使` | `908, 190, 175, 277` | 457,411 | `4F209411C98F439B6856D898259DAF5ADB3CEFC76ED04C45A8F10E78B70E97BF` |

The 1920×1080 source uses the four crop rectangles listed above, each scaled into a 430×680 image area. The separate 430×70 label bar begins below the image at `y=925`, so it does not cover the characters' feet. The exact title, subtitle, colours, dimensions and output path are recorded in [`tools/render_alpha_promo.ps1`](../../tools/render_alpha_promo.ps1). Rendering requires the Windows `Microsoft YaHei UI` font.

## Reproduce

Run from the repository root on Windows PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\render_alpha_promo.ps1
```

The default command reads the archived source capture and replaces `docs/images/arknights-oni-alpha-v0.3.2-workshop.png`. Use `-ScreenshotRoot` only when intentionally testing a different capture directory.

Current output: 2,373,321 bytes; SHA-256 `DE04B4EF5AB1ECC4B475EF15B5EBAF91CDE0FC0121ABBEE10DB12A64501D761B`.
