# Native rendering

`DesktopRenderBackend` renders the shared C# NowUI draw data through OpenGL 3.3.
The shader files in `Shaders` belong to this backend; no browser, JavaScript,
or WebAssembly runtime is involved. The same `Now` and extension APIs are used
by Unity and native scenes.

The supported stock material paths cover rectangles, images, text, gradients,
ripples, Bezier lines, color pickers, glass, and SDF scenes. Glass uses NowUI's
capture and blur plans, including the Gaussian blur kernel. SDF scenes retain
their analytic operations, rotations, graphs, morphs, text, effects, masks, and
sprite silhouettes. Sprite distance fields use all five stock GPU bake passes
and floating-point targets.

Choose `ColorSpace.Gamma` or `ColorSpace.Linear` in the backend constructor and
set `NowRuntime.colorSpace` to the same value before rendering. The CLI does
this for `--color-space gamma|linear`. In Linear mode, authored vertex colors
use the stock Unity shader conversion, sRGB images decode on sampling, data
textures keep their numeric values, and an sRGB output attachment encodes the
linear composited result for display. PNG output removes premultiplication in
the working space before returning straight-alpha display pixels.

The backend supports single-sample 2D RGBA8, R8, and one-, two-, or
four-channel 16/32-bit float render targets and data textures. Shader passes
restore their blend mode before each draw, and material blits preserve the
caller's viewport. Graphics resources must be created, used, and disposed on
the context's owning thread.

Custom Unity HLSL shaders, world/camera content, XR arrays, MSAA textures,
depth targets, render-target mip chains, and procedural/instanced custom draws
remain Unity features. Unsupported formats and shaders fail with a named
error. `MirrorOnce` wrapping is not available on the OpenGL 3.3 baseline.
Rendering requires a desktop graphics driver; the SDF program also needs
enough fragment uniforms for NowUI's full scene arrays. The current GPU
regression coverage runs on Windows with NVIDIA OpenGL.

Run the focused GPU checks in a desktop graphics session:

```powershell
$env:NOWUI_TEST_NATIVE_GRAPHICS = '1'
dotnet test Standalone/NowUI.Native.Tests -c Release --filter FullyQualifiedName~RendererParityTests
```

The tests compare actual pixels for Gamma/Linear color, image/data texture
sampling, blending, PNG alpha, true glass blur, SDF effects and masks, image
distance fields, and all advertised data formats. Set
`NOWUI_RENDER_ARTIFACT_DIR` to save those PNGs for inspection.
