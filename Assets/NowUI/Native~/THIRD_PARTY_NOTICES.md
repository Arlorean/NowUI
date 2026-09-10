# Native CLI third-party notices

This file accompanies NowUI's native CLI distribution. Full license texts and
the package metadata used for this inventory are retained in the adjacent
`ThirdPartyLicenses` directory. The inventory covers the libraries included
in the CLI tool package, including dependencies used by the renderer and
direct Unity asset reader.

## Bundled NuGet libraries

| Library | Version | Attribution | License text |
| --- | --- | --- | --- |
| AssetsTools.NET | 3.0.5 | nesrak1 | [MIT](ThirdPartyLicenses/AssetsTools.NET-LICENSE.txt) |
| OpenTK.Core, OpenTK.Graphics, OpenTK.Mathematics, OpenTK.Windowing.Common, OpenTK.Windowing.Desktop, OpenTK.Windowing.GraphicsLibraryFramework | 4.9.4 | Team OpenTK; Stefanos Apostolopoulos and the Open Toolkit project | [MIT](ThirdPartyLicenses/OpenTK/LICENSE.md), including the [OpenEXR conversion notice](ThirdPartyLicenses/OpenTK/THIRD_PARTIES.md) |
| OpenTK.redist.glfw | 3.4.0.44 | Marcus Geelnard; Camilla Löwy | [GLFW zlib-style license](ThirdPartyLicenses/GLFW-COPYING.md) |
| StbImageSharp | 2.30.16 | StbImageSharpTeam; stb by Sean Barrett | [MIT / public-domain alternatives](ThirdPartyLicenses/StbImageSharp-stb-LICENSE.txt) |
| YamlDotNet | 18.1.0 | Antoine Aubry and contributors | [MIT](ThirdPartyLicenses/YamlDotNet-LICENSE.txt) |

The native GLFW binaries are included by the OpenTK redistributable package.
StbImageSharp is a managed port of stb_image; it does not add a native image
decoder binary. Its NuGet metadata declares `Unlicense OR MIT`; both
alternatives from its included stb source are preserved here.

## NowUI native font libraries and font resources

The native font compiler's existing third-party notices for msdf-atlas-gen,
msdfgen, FreeType, HarfBuzz and their linked dependencies are retained in
[NowUI library notices](ThirdPartyLicenses/NowUI-LIBRARY-NOTICES.md). That file
also preserves the signed-distance-function attribution used by NowUI's
shaders. The bundled Noto Sans font files are covered by the
[Noto Sans SIL Open Font License](ThirdPartyLicenses/NotoSans-LICENSE.txt).
NowUI's own license is retained in [NowUI-LICENSE.md](ThirdPartyLicenses/NowUI-LICENSE.md).

## Optional browser deployment kit

The prepared browser kit carries WebAssembly objects for NowUI's vector
tessellator and font compiler, plus the complete HarfBuzz 10.1.0 implementation.
Its `THIRD_PARTY_NOTICES.md`, license texts and build provenance accompany
those objects in `browser/` in the Unity package, or `BrowserKit/` in an
installed CLI. Native rendering remains independent of these files.

## Provenance

Package identities and versions come from the CLI restore graph and the
runtime files included in the tool package. The original `.nuspec` files for
the ten bundled NuGet packages are retained in [NuGet](ThirdPartyLicenses/NuGet).
The tool uses an installed .NET runtime; this distribution does not include
the .NET runtime itself or a separate System.Runtime.CompilerServices.Unsafe
binary. Build and test packages are not included in this inventory.

License texts were retained from these inputs:

- `OpenTK.redist.glfw/3.4.0.44/COPYING.md`, copied directly from the restored NuGet package.
- OpenTK's [LICENSE.md](https://github.com/opentk/opentk/blob/4.9.4/LICENSE.md) and [THIRD_PARTIES.md](https://github.com/opentk/opentk/blob/4.9.4/THIRD_PARTIES.md), from tag `4.9.4`.
- YamlDotNet's [LICENSE.txt](https://github.com/aaubry/YamlDotNet/blob/748334a8fa7c227740018b284b71ad95cc6b7fc7/LICENSE.txt), from the repository commit recorded in its NuGet package.
- StbImageSharp's [stb_image.h license footer](https://github.com/StbSharp/StbImageSharp/blob/125af70cb557033f2c46aec8e82eaaf72ac49817/generation/StbImageSharp.Generator/stb_image.h), from the repository commit recorded in its NuGet package. The complete license footer is copied without changing its text.
- AssetsTools.NET's [LICENSE](https://github.com/nesrak1/AssetsTools.NET/blob/2d66992b60d583d6d82dd6e278d3946cf5827286/LICENSE). Its NuGet package declares MIT but contains no license file or repository commit, so this notice records the upstream revision used for the retained text separately.
- The existing NowUI `THIRD_PARTY_LICENSES.md`, `LICENSE.md`, and `Assets/Fonts/NotoSans/LICENSE.txt`, copied from this source package.
