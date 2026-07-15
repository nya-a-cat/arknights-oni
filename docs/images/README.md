# Documentation image production

This directory keeps the editable inputs, deterministic renderer, and production notes for README and release images.

## Current README image

- Output: [`arknights-oni-alpha-v0.3.2-workshop.png`](./arknights-oni-alpha-v0.3.2-workshop.png)
- Production record: [`arknights-oni-alpha-v0.3.2-workshop.layout.md`](./arknights-oni-alpha-v0.3.2-workshop.layout.md)
- Source captures: [`source/alpha-v0.3.2`](./source/alpha-v0.3.2)
- Renderer: [`tools/render_alpha_promo.ps1`](../../tools/render_alpha_promo.ps1)

The current image uses real Oxygen Not Included screenshots. The script applies fixed crops, scaling, borders, colour blocks, and text. GPT ImageGen was not used.

## Current gameplay GIF

- Output: [`arknights-operators-demo-v0.3.2.gif`](./arknights-operators-demo-v0.3.2.gif)
- Production record: [`arknights-operators-demo-v0.3.2.layout.md`](./arknights-operators-demo-v0.3.2.layout.md)
- Source captures: [`source/alpha-v0.3.2/gif`](./source/alpha-v0.3.2/gif)
- Renderer: [`tools/render_operator_demo_gif.ps1`](../../tools/render_operator_demo_gif.ps1)

The GIF is a deterministic sequence of five real Steam gameplay captures. It shows different operators on four duplicants, the individual appearance picker, the action wheel, Amiya's Sleep visual performance, and automatic mapping restoration. GPT ImageGen was not used.

## Editing workflow

1. Keep the original screenshot files under a versioned `source/<release>` directory.
2. Record each input filename, byte length, SHA-256, label, and crop rectangle in the image's production record.
3. Change layout or copy in the renderer instead of editing the final PNG by hand.
4. Run the renderer from the repository root and visually inspect the complete output at its native output resolution.
5. Update the production record when any input, crop, label, colour, or output dimension changes.

## ImageGen prompt retention

When a future documentation image uses GPT ImageGen, save a sibling `<output-name>.prompt.md` with:

- the exact prompt sent to ImageGen;
- every referenced input image and its SHA-256;
- requested dimensions and transparency requirements;
- generation date and the exposed tool/model identifier, when available;
- subsequent deterministic crop, resize, text, or colour-processing steps;
- a clear distinction between generated artwork and real game screenshots.

Keep the original generated bitmap alongside the final edited output when post-processing changes its pixels. README gameplay claims should continue to use real game captures.
