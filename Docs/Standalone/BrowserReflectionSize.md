# Browser reflection experiment

This follows [Browser Code Size](BrowserCodeSize.md). All download figures are
compressed response bodies measured in fresh en-US Chromium contexts, with the
same Motion Room scene, assets and deterministic capture. KB and MB are decimal.

## What the experiment found

| Candidate | Initial download |
| --- | ---: |
| Previous shipped stripping pass | 4,173,091 B |
| Direct scene construction and typed built-in font access | 4,171,793 B |
| Generated ScriptableObject factories/lifecycle callbacks | 4,172,147 B |
| Generated lifecycle and runtime reset callbacks | 4,171,706 B |
| Above, with unused scenes stripped from the application assembly | 4,158,632 B |

Replacing both discovery mechanisms saved only **87 bytes** against the first
simple candidate. Its NowUI managed payload grew by 2,127 bytes, offset by small
savings elsewhere. Linked WebCIL inspection verified that the old lifecycle
discovery methods really were removed. The generated tables, factories and
callback wrappers replaced much of their cost. The experiment was working; it
did not produce a useful overall size reduction.

The generated registries, runtime feature switches and reset bridge were removed
from production. Native and browser object lifecycle/reset behavior retain the
existing implementation. Prototype source, patches, measurements and validation
remain in `artifacts/local/browser-reflection-size`.

The useful change was removing the whole-assembly root from the selected static
application. Motion Room shares an assembly with several other demo scenes;
their code and additional NowUI calls had remained reachable. Removing that
root saved a further **13,074 bytes** in the controlled experiment.

The retained publisher uses direct scene construction, typed built-in font field
access and a conservative static-scene eligibility check. Applications needing
dynamic preservation retain the previous behavior automatically. No asset export,
authoring API or new CLI option is required.

## Assets and maintenance

The generic Unity asset loader is intentionally retained. Its linked `Populate`
and `ConvertValue` implementations contain only 206 and 781 IL bytes. Replacing
them requires hundreds of field assignments/accessors, including nested structs,
private font fields, default-value handling and cyclic GUID references. That
would need to eliminate enough shared framework code to offset the generated
reader. This pass does not claim that a fully generated asset reader was built
or measured.

Fonts and the native runtime remain the main size costs. These experiments
preserved font faces, glyphs, cultures, supported assets, Lottie and input.

## Validation and measurement scope

Every experimental browser candidate passed full Playground interaction,
resize and fixed-time capture. Generated-reset candidates additionally passed
explicit shutdown/reset checks with no browser or managed errors. All fixed
captures match SHA256
`2497f50c63f15e7afbe77b2391219fd70c1c6eadc91f8f5d141dbbfe180ad3f4`.

Development-runner full-site totals include 19,779 bytes of additional NuGet
notice files compared with the prior packaged tool. Those files are not loaded
at startup; the table uses actual browser response bodies rather than mixing
those inventories. The final packaged validation is recorded below.

## Retained release build

The final installed package is
`dfd74811d7874c0622b308110a86b1fea5b52e89157892e93f42b34766ad9269`
(11,806,115 bytes). Its Motion Room cold response bodies total **4,158,093 bytes**,
down **14,998 bytes / 0.36%** from the previous shipped build. The complete site
is 14,852,666 original bytes / 4,646,937 Brotli-selected bytes; stored originals
and both encodings total 25,299,323 bytes, excluding the size report itself.

Linked application code fell from 12,911 to 4,426 Brotli bytes. Its selected
`PlaygroundScene` remains and the other eight demo scene types are absent.
NowUI managed code fell from 319,388 to 316,687 bytes. The original lifecycle and
reset implementations are present, with no experimental registry or reset bridge.

The exact installed tool passed Motion Room startup, text input, themes, Lottie,
keyboard binding, resize, fixed browser capture and native capture. Both captures
remain byte-identical to the prior build. Static validation round-tripped all
162 encoded sidecars; 3,978,150 original asset bytes are unchanged. Sixty focused
publishing tests and sixteen browser-host tests passed. Detailed evidence is in
`artifacts/local/browser-reflection-size/final-summary.json` and
`final-linked-check.json`.

A fresh scaffold created by the same installed bundle outside the checkout also
passed native rendering, ordinary publishing and AOT publishing. Its source and
all eleven built-in asset files match the prior scaffold. Both browser variants
passed persistent button clicks and resize; their initial PNGs and the native
capture are byte-identical to the preceding build.

| Fresh scaffold cold bodies | Previous | Retained build |
| --- | ---: | ---: |
| Normal | 3,670,707 B | 3,667,756 B |
| AOT | 5,553,753 B | 5,546,754 B |

All 132 installed payload files match their package entries, including all
twenty native libraries. The four built-in font faces load correctly in ordinary
and AOT browser builds. Evidence is in
`artifacts/local/browser-reflection-size/final-scaffold/comparison.json`.
