# Optional browser kit notices

This kit contains the NowUI browser host, low-level WebGL interop, and prepared
WebAssembly objects for the same native font and vector implementations used
by NowUI. The application and scene API remain C#.

- `native/harfbuzz.o` is HarfBuzz 10.1.0, upstream revision
  `9ef44a2d67ac870c1f7f671f6dc98d08a2579865`, built with .NET wasm-tools
  Emscripten 3.1.56. Its complete [license](native/HarfBuzz-LICENSE.txt) and
  [build provenance](native/harfbuzz.json) accompany the object.
- `native/nowui-msdf.o` is the package's existing WebGL font compiler object,
  including msdfgen, msdf-atlas-gen and FreeType. Their retained notices are in
  [NowUI library notices](NowUI-LIBRARY-NOTICES.md); the complete
  [FreeType License](native/FreeType-LICENSE.txt) is also included.
- `native/nowui-vg.o` is NowUI's own vector tessellator. NowUI's
  [license](NowUI-LICENSE.md) accompanies this kit.
- `native/nowui-browser.o` is NowUI's compact native-call bridge for the .NET
  browser interpreter. It forwards to the stock font and vector implementations;
  its [build provenance](native/nowui-browser.json) accompanies the object.

The prepared objects have wasm object format despite the `.bc` extension of
their source-package counterparts. They are renamed `.o` for Emscripten linking.
The font object links the complete HarfBuzz implementation; no shaping stubs
are used. `kit.json` records the hashes of the distributed files.

This kit does not contain the .NET WebAssembly runtime. An optional browser
publish uses the separately installed .NET SDK and wasm-tools workload to
produce the application's runtime files. Managed libraries and font assets
copied from the native CLI retain its accompanying third-party notices.
