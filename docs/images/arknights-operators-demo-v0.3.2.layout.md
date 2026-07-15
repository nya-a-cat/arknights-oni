# Alpha v0.3.2 gameplay GIF production record

`arknights-operators-demo-v0.3.2.gif` is a deterministic slideshow made from five real Steam gameplay captures. GPT ImageGen was not used. The renderer only scales the captured pixels, quantizes a GIF palette, assigns frame durations, and loops the result. It creates no character, environment, animation, or game-effect pixels.

## Captured flow

1. Four duplicants display Texas, Amiya, Kal'tsit, and Exusiai simultaneously.
2. `Ctrl+F8` opens Amiya's individual operator, skin, and model picker.
3. `Ctrl+F9` opens the selected duplicant's action wheel.
4. The Sleep visual performance is applied to Amiya.
5. The centre button restores automatic ONI state mapping.

## Source frames

| Source | Duration | Bytes | SHA-256 |
| --- | ---: | ---: | --- |
| `01-four-operators.png` | 1.6 s | 238,718 | `E29DA93A5BF06EB03D977FF96EB9525B81501B7658530CA32C544BB9C2489A12` |
| `02-individual-picker.png` | 2.0 s | 244,987 | `10CC16D82DD64B4C9BAE05BA1EA88DA3962B7C3397A1832B0A7878BC3BFAB3D1` |
| `03-action-wheel.png` | 2.0 s | 234,561 | `EF3EBC043A600AEC4456B04D044EEE0980A23821CBEFA157512A394AC072F9F8` |
| `04-amiya-sleep.png` | 2.0 s | 253,220 | `0A3CDA480AF927F63D85CBF2DED1283C51C13AFE3FA9CBE7CF916687782A7E8B` |
| `05-automatic-restored.png` | 1.6 s | 239,501 | `7DB56A1D15B4E69D271BC587F8600FBCCE9E24BA4270ECEDE86504FE14CDA658` |

All source frames are 1536×864 PNG captures under `docs/images/source/alpha-v0.3.2/gif`. The deterministic renderer scales them to 960×540, samples at 8 fps, builds a 192-colour differential palette, applies Sierra 2-4A dithering, limits the output to the 9.2-second storyboard duration, and writes a looping GIF.

## Reproduce

Run from the repository root in Windows PowerShell with the existing `ffmpeg` installation:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\render_operator_demo_gif.ps1
```

Reproduction environment: `ffmpeg version 7.1-full_build-www.gyan.dev` on Windows PowerShell 5.1.

Current output: 960×540, 74 frames, 9.26 seconds, 794,751 bytes; SHA-256 `CD7C76CEFBDA7E2BC77DFDF54C8073BF42B848851A59437E8F3BAB3B60BF8C20`. GIF timing granularity accounts for the 0.06-second difference from the 9.2-second storyboard sum.
