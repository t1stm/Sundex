# Render backend plan

Status: phases 0 and 1 implemented on 2026-11-19 and phase 2 on 2026-11-20, not yet committed. The
app ports are in the sibling worktrees `ThirtyDollarTools-render-backend` and `AudioAPI-render-backend`,
on the local prerelease `1.0.0-render.6`. Phase 1's stable release is still to do. Covers Sundex and
the two apps built on it, ThirtyDollarTools and musicrain.

## Goal

Sundex draws through OpenGL 3.3, and GL types appear in its public API. This plan has three phases:

1. **Phase 1:** put a backend-neutral API between the engine and OpenGL. Shape it like Vulkan, so a
   Vulkan backend later adds to the API instead of reshaping it. OpenGL stays the only backend.
2. **Phase 2:** remove the behavior that works only because of how GL drivers behave, while GL is
   still the only backend and the apps can be checked against it.
3. **Phase 3, later:** add a Vulkan backend when 3D work starts. Build it for 3D and for pushing the
   hardware, not as a port of the 2D path.

OpenGL stays a backend after phase 3. It is the fallback, the macOS path unless MoltenVK covers what
the 3D work needs, and what CI renders with.

Out of scope: batching or restructuring UI rendering, and new GL or 3D features before phase 3.

## Current state

Measured on 2026-11-19.

- Sundex makes about 120 `GL.` calls in 17 files, and 27 files import `OpenTK.Graphics.OpenGL`. Most
  are in `Shader.cs` (25), `Game.cs` (20), `GPUTexture.cs` (19), `BoxPlane.cs` (13) and
  `GLBuffer.cs` (10).
- Above the engine, only `UIContext.Render` calls GL, for the scissor. Markup, Style.DSL, Reactive
  and Slug have no GL.
- The public API carries GL: `BufferTarget` in buffer constructors, `ShaderType`, `InternalFormat`,
  `VertexAttribPointerType`, `int` handles on `IBindable`, `IGPUReflection.SelfReflectToGL`,
  `DeleteQueue(DeleteType, int)`, `GLInfo` in `InitArguments` and `AssetProvider`,
  `Shader.SetUniform(name, …)`, and GL's `DebugSource`/`DebugType` on `RenderMarker.Debug`.
- ThirtyDollarTools uses that layer directly: `GLBufferList`, `VertexArrayObject`,
  `VertexBufferLayout`, `SelfReflectToGL` (5 types), `SetUniform` (`RenderStack`, `FramedAtlas`),
  `GL.BindBufferBase` (`StaticSoundAtlas`), and `DeleteQueue` in 22 places. Its GPU voice mixer and
  `GpuMixBenchScene` are raw GL 4.3: compute, timer queries and fences.
- Shaders: 14 files (7 programs) in Sundex, 8 render and 2 compute shaders in ThirtyDollarTools.
- musicrain was not inspected; it wasn't found at the initial scan; located at "~/RiderProjects/AudioAPI/Native".

Behavior that holds only because the backend is GL:

- **Per-draw uniforms.** Every plane writes one shared static uniform buffer and then draws
  (`ColoredPlane.cs:127`, `BoxPlane.cs:115`, `GradientPlane.cs:159`, `TexturedPlane.cs:125`). Each
  draw sees its own data only because `glBufferData` orphans the old storage (`GLBuffer.cs:49`).
- **Uploads during drawing.** Buffers and textures upload inside `Bind()` and `Update()`, between
  draws.
- **Backdrop blur.** `BoxPlane.CopyBackdrop` copies the window framebuffer mid-frame and builds
  mipmaps from it.
- **Loose uniforms and texture units.** `uVPMatrix`/`u_VPMatrix` and the text samplers are set by
  name, and textures bind to whichever unit is active.
- **Global state.** Blend is enabled for the whole frame in `Game.OnRenderFrame`, and scissor is
  toggled per renderable.

Already in good shape:

- Uniform structs are std140 with explicit offsets.
- Each renderable class owns one static shader and one vertex layout, which is what a pipeline object
  needs.
- Every vertex buffer is either all per-vertex or all per-instance, which Vulkan requires.
- Creation, uploads and deletes are already deferred: `BufferState.PendingCreation`, the texture
  upload queues, `DeleteQueue`.
- Slug text reads float textures with `texelFetch` and clips per glyph in the shader. Nothing about it
  is GL-specific.
- One pass, no depth test, no framebuffer objects, one GL context.
- OpenTK `5.0.0-pre.16` already ships `OpenTK.Graphics.Vulkan`, and its GLFW binding has
  `CreateWindowSurface`, `GetRequiredInstanceExtensions` and `VulkanSupported`.

## Principles

1. **Vulkan-shaped, GL-implemented.** Explicit render passes, immutable pipeline objects, numbered
   bindings, and a command list passed down to renderables instead of global state. Implement only
   what Sundex uses today. The device and command list are interfaces from the start, because
   turning a concrete GL type into an interface in phase 3 would break the apps a second time.
2. **Contracts, not mechanisms.** Each call states what it guarantees, and each backend meets that
   its own way. `PushUniforms` guarantees the draw sees the data as it was at the call. GL keeps
   orphaning a buffer, which is today's behavior and what drivers optimize for. Vulkan will use a
   per-frame ring with dynamic offsets. GL performance doesn't change.
3. **Keep lazy realization.** Resources stay plain objects that any thread or test can create and
   write. The backend creates and uploads the GPU object on the render thread the first time a
   command list uses it, as now.
4. **One breaking release.** Phase 1 ships as the next major version. The apps move to it once,
   against a prerelease.
5. **GL lives in one folder.** After phase 1, only `Sundex.Engine/Renderer/OpenGL/` imports
   `OpenTK.Graphics.OpenGL`, and a check enforces it.

## Phase 0: baseline

Before changing anything, record on current master of all three repositories:

- GPU time p50/p95 (`GpuTimer`) and CPU frame time on the Visualizer playing the sequence "/home/kris/tdw/flower castle/Flower Castle TDW Cover - Final.tdw",
  the Editor piano roll on "/home/kris/tdw/flower castle/flower castle.tdwproj", and the musicrain home page and full player.
- Screenshots of the same views.
- A passing run of `build/smoke-core.sh`.

These are the gates for every later step: no visible difference, frame times within noise.

## Phase 1: backend-neutral API on OpenGL

### API

A sketch: names can change, the shape shouldn't.

```csharp
namespace Sundex.Engine.Renderer;

public interface IGraphicsDevice : IDisposable
{
    GraphicsBackend Backend { get; }
    DeviceCapabilities Capabilities { get; }

    /// Frees resources whose last use has finished and returns this frame's command list.
    ICommandList BeginFrame();

    /// Submits the frame and presents it.
    void EndFrame();

    void Resize(int framebufferWidth, int framebufferHeight);

    /// Tests and diagnostics only: waits for the GPU. Valid between EndPass and EndFrame.
    void ReadPixels(Box2i rectangle, Span<byte> rgba8);
}

public interface ICommandList
{
    void BeginPass(in PassDescription pass); // target (null = window), load or clear, clear color
    void EndPass();
    Vector2i TargetSize { get; }             // physical pixels of the active target

    void SetPipeline(Pipeline pipeline);
    void SetView(in Matrix4 viewProjection);                // binding 0
    void PushUniforms(int binding, ReadOnlySpan<byte> data); // copied now
    void SetUniformBuffer(int binding, GpuBuffer buffer);
    void SetTexture(int binding, GPUTexture texture, Sampler sampler);
    void SetVertexBuffer(int slot, GpuBuffer buffer);
    void SetIndexBuffer(GpuBuffer<int> buffer);
    void SetScissor(Box2i? physicalPixels); // top-left origin, null = whole target

    void DrawIndexed(int indexCount, int instanceCount = 1, int firstIndex = 0);

    void CopyFromTarget(Box2i physicalPixels, GPUTexture destination); // ends and resumes the pass
    void CopyToTarget(GPUTexture source);                              // a render target onto the active one
    void GenerateMipmaps(GPUTexture texture);

    void PushLabel(ReadOnlySpan<char> name);
    void PopLabel();
}

public static class CommandListExtensions
{
    // Generic here rather than on the interface: a generic virtual call over a struct is a
    // runtime lookup, and this runs once per plane.
    public static void PushUniforms<T>(this ICommandList commands, int binding, in T data)
        where T : unmanaged =>
        commands.PushUniforms(binding, MemoryMarshal.AsBytes(new ReadOnlySpan<T>(in data)));

    /// Sets binding 0 from the camera and returns the context renderables draw with.
    public static RenderContext WithCamera(this ICommandList commands, Camera camera)
    {
        commands.SetView(camera.GetVPMatrix());
        return new RenderContext(commands, camera);
    }
}

public readonly record struct PipelineDescription(
    Shader Shader,
    VertexBufferLayout[] VertexBuffers,
    BlendMode Blend = BlendMode.Alpha,
    PrimitiveTopology Topology = PrimitiveTopology.Triangles);

public readonly struct RenderContext(ICommandList commands, Camera camera)
{
    public ICommandList Commands { get; } = commands;
    public Camera Camera { get; } = camera;
}

public interface IRenderable
{
    void Render(in RenderContext context);
}
```

Notes:

- A `Pipeline` is created from a `PipelineDescription` once per renderable class and kept in a static,
  as the VAO is now. No cache keyed on descriptions is needed yet.
- A `Pipeline` notices when hot reload relinks its `Shader` (a version counter on the shader) and
  rebuilds its backend objects.
- Base instance is left out: GL 3.3 has no `glDrawElementsInstancedBaseInstance`.
- Whoever owns a camera (`UIContext`, scenes) calls `WithCamera`. Renderables never set the
  view-projection matrix themselves.
- The GL command list runs each call immediately on the render thread. GL doesn't need recording and
  replay.

### Binding convention

All Sundex and app shaders follow one numbering:

- Binding 0: the `View` uniform block, holding `mat4 ViewProjection`. Later fields are only appended,
  so std140 offsets stay put.
- Binding 1: the `Draw` uniform block, filled by `PushUniforms`.
- Binding 2 and up: shader-specific textures and uniform buffers.

Binding numbers are unique within a shader across blocks and textures, so each shader's bindings fit
one Vulkan descriptor set later.

GLSL 330 can't declare bindings in source without `GL_ARB_shading_language_420pack`, which macOS's GL
4.1 lacks. So each shader declares its layout in C# (`ShaderLayout`, mapping names to bindings).
After linking, the GL backend applies it once, with `glUniformBlockBinding` for blocks and
`glUniform1i` for samplers. A declared name the linked program doesn't have is logged rather than
thrown, because compilers drop unused uniforms. This replaces `Shader.IsPedantic`.

### Old to new

| Today | After phase 1 |
|---|---|
| `GLBuffer<T>(deleteQueue, BufferTarget.X)` | `GpuBuffer<T>(BufferUsage.X)` |
| `GLBuffer<T>.WithCPUCache` | `GpuBuffer<T>.WithCPUCache` |
| `GLBufferList<T>(deleteQueue, capacity)` | `GpuBufferList<T>(capacity)` |
| `Dangerous_SetBufferData(span)` | `SetData(span)`: any thread, uploaded at next use |
| `GLQuad.VBOWithoutUV`, `VBOWithUV`, `EBO` | `UnitQuad.Positions`, `PositionsWithUV`, `Indices` |
| `GLQuad.DrawInstanced(n)` | `cmd.DrawIndexed(UnitQuad.IndexCount, n)` |
| `VertexArrayObject` | gone from the API: `PipelineDescription.VertexBuffers` plus `cmd.SetVertexBuffer` |
| `PushFloat(n, perInstance: true)` | `new VertexBufferLayout(VertexStepRate.Instance).PushFloat(n)` |
| `VertexAttribPointerType` | `VertexFormat` |
| `IGPUReflection.SelfReflectToGL(layout)` | `IVertexData.DescribeLayout(layout)` |
| `ShaderType.VertexShader` | `ShaderStage.Vertex` |
| `Shader.Use()` | `cmd.SetPipeline(pipeline)` |
| `Shader.SetUniform(name, value)` | `WithCamera`, or a field in the shader's `Draw` block |
| `GL.BindBufferBase(UniformBuffer, n, buffer.Handle)` | `cmd.SetUniformBuffer(n, buffer)` |
| `GL.ActiveTexture(unit)` then `texture.Bind()` | `cmd.SetTexture(binding, texture, Sampler.X)` |
| `InternalFormat.Rgba8`, `Rgba32f` | `TextureFormat.Rgba8Unorm`, `Rgba32Float` |
| `IBindable.Handle` (`int`) | removed: backends keep native handles |
| `DeleteQueue`, `DeleteType` | removed: `Dispose()` hands the native object to its device |
| `GLInfo` | `DeviceCapabilities` (`Game.Device.Capabilities`, `InitArguments.Capabilities`) |
| `IRenderable.Render(Camera)` | `IRenderable.Render(in RenderContext)` |
| `RenderMarker.Debug(text, marker, DebugSource, DebugType)` | `RenderMarker.Debug(text, marker)` |
| `Context.SwapBuffers()` in `Game` | `Device.EndFrame()` |

### Steps

Each step builds and passes `dotnet test` and `build/smoke-core.sh`. The apps move in step 7, once,
so until then the extended smoke sample from step 1 is what catches visual regressions.

**Step 1: device, command list, render context.**

- Extend the core sample first: draw one of each plane type, a line of text and a scissored plane,
  and probe a pixel inside each. It keeps using `GL.ReadPixels` until step 5.
- Add `IGraphicsDevice`, `ICommandList`, `RenderContext`, `DeviceCapabilities`, and
  `GLGraphicsDevice` in `Sundex.Engine/Renderer/OpenGL/`.
- Move every GL call in `Game.cs` into `GLGraphicsDevice`: the debug callback, the capability queries
  (`GetGLInfo`), blend setup, viewport, clear and swap. `Game` gets a `Device` property, and
  `OnRenderFrame` becomes `BeginFrame`, `BeginPass` (clear to transparent black),
  `SceneManager.Render`, `EndPass`, `EndFrame`.
- Delete on the way: `GL.ClearColor` called after `GL.Clear` (it applies to the next frame), the
  depth clear (there is no depth test), and the polygon and line smoothing hints (smoothing is never
  enabled, so they do nothing).
- `RenderArguments` gets `ICommandList Commands`. `InitArguments.GLInfo` becomes `Capabilities`.
- Change `IRenderable.Render(Camera)` to `Render(in RenderContext)`. That is about 17 implementations
  across Sundex and ThirtyDollarTools, plus `UIContext.Render`, which now takes the command list.
  Renderables still call GL directly at this point.
- `Sundex.Testing` gets a recording `ICommandList` for tests that call `UIContext.Render`
  (`UIElementTests`).

**Step 2: binding model and shaders.**

- Add `ShaderLayout`, `SetView`, `PushUniforms`, `SetUniformBuffer` and `SetTexture`. Samplers become
  GL sampler objects (core in 3.3) instead of per-texture parameters, with presets
  `Sampler.LinearClamp`, `Sampler.LinearMipmapClamp` and `Sampler.NearestClamp`.
- Rewrite the 14 Sundex shader files to the binding convention:
  - `Projection` leaves the plane uniform structs (`ColoredUniform`, `GradientUniform`,
    `TexturedUniform`, `BoxPlane.Uniform`) and is read from `View`. Each struct shrinks by 64 bytes.
  - `uVPMatrix` in `Batched.vert` and `u_VPMatrix` in the instanced shaders move to `View`.
  - The three text samplers get bindings 2 to 4.
- Port the planes and `TextProvider.BindAndSetUniforms` to the command list.
- Delete `Shader.SetUniform`, `Shader.Use` and `IsPedantic`. For raw GL code, such as the app's compute
  mixer, the GL backend exposes `GLProgram`: a plain program wrapper with `Use` and `SetUniform`.

**Step 3: pipelines and vertex layouts.**

- Add `Pipeline`, `PipelineDescription`, `VertexFormat` and a per-buffer `VertexStepRate`.
  `VertexBufferLayout` keeps its builder methods and rejects a layout that mixes per-vertex and
  per-instance elements. `PushMatrix4` stays per-instance.
- Rename `IGPUReflection.SelfReflectToGL` to `IVertexData.DescribeLayout`.
- `VertexArrayObject` becomes internal to the GL backend: one VAO cached per pipeline and set of bound
  vertex and index buffers. It uses direct state access when the driver has it, as now.
- Rename `GLQuad` to `UnitQuad`. Port the planes, `TextBuffer` and every `GLQuad.DrawInstanced` caller
  to `SetPipeline`, `SetVertexBuffer`, `SetIndexBuffer` and `DrawIndexed`. The pipeline's `BlendMode`
  replaces the frame-wide `GL.Enable(Blend)`.

**Step 4: resources.**

- `GLBuffer<T>` becomes `GpuBuffer<T>`, with a non-generic base `GpuBuffer` and a `BufferUsage`
  (vertex, index, uniform) instead of `BufferTarget`. `GLBufferList<T>` becomes `GpuBufferList<T>`.
- A realized resource holds its backend object. `Dispose()` passes that object to its device: GL
  deletes it at the next `BeginFrame`, and Vulkan will wait for the frame's fence. Constructors lose
  their `DeleteQueue` parameter, and `DeleteQueue`, `DeleteType` and `IBindable` go.
- The base `GpuBuffer<T>` keeps only `SetData`. Indexed access stays on `WithCPUCache`, which every
  indexed user already goes through (text, instance lists). This deletes the base class's
  `MapBuffer` readback and sparse updates, along with two latent bugs in them (see the last section).
  The compiler finds any caller still using the base indexer.
- `GPUTexture` takes `TextureFormat` instead of `InternalFormat` and keeps its upload queue.
  `GPUTextureAtlas` follows.
- `AssetProvider` stops taking GL information. In Sundex only the VAO read it; the app's uses move to
  `Device.Capabilities`.

**Step 5: remaining GL call sites.**

- `UIContext.Render`: scissor goes through `cmd.SetScissor` in physical pixels with a top-left
  origin. The GL backend flips to GL's bottom-left origin using the target height, so `UIContext`
  stops doing the flip.
- `BoxPlane.CopyBackdrop`: `CopyFromTarget` then `GenerateMipmaps`, into a box-sized `GPUTexture`
  that is replaced when the size changes. That allocates no more often than today's
  `glCopyTexImage2D`; phase 2 removes the per-copy allocation.
- `GpuTimer`: created from the device, and `Begin` and `End` take the command list. The GL
  implementation keeps its `GL_TIME_ELAPSED` queries.
- `RenderMarker`: drop the GL enum parameters and route through a sink the device installs. The GL
  sink keeps `glDebugMessageInsert`.
- Core sample: `GL.ReadPixels` becomes `Device.ReadPixels`, and the sample stops importing GL.

**Step 6: guards.**

- A unit test reflects over every public type in the Sundex assemblies. It fails if a public or
  protected member's signature uses a type from `OpenTK.Graphics`. That is about 20 lines, and it
  fails the moment GL leaks back into the API.
- A CI step fails if any `.cs` file outside `Sundex.Engine/Renderer/OpenGL/` imports
  `OpenTK.Graphics.OpenGL`.

**Step 7: release and apps.**

- Publish a prerelease of the next major version.
- Port ThirtyDollarTools:
  - `RenderStack`: a static `Pipeline` per vertex type and shader, `SetVertexBuffer` for the quad and
    the instance list, and `DrawIndexed`. Delete `SetUniform("u_VPMatrix")`.
  - `SoundData`, `StaticSound`, `BackgroundBlip`, `QuadUV` and `ShadedRect`: `DescribeLayout`, with
    the step rate on the layout.
  - `FramedAtlas.SetUniforms`: `u_UV` moves into the `Draw` block of the shaders that use it, called
    from `DollarStoreLoaderBackground` and `StackCollection`.
  - `StaticSoundAtlas`: `GpuBuffer<Vector4>(BufferUsage.Uniform)` and `cmd.SetUniformBuffer`. Its UV
    block moves from binding 0, which is now `View`, to binding 2.
  - The 8 render shaders: `View` and `Draw` blocks.
  - `LineBatch`, `RenderableFactory` and `GpuVoiceKernels`: `ShaderStage`.
  - All 22 `DeleteQueue` references go.
  - The GPU mixer and `GpuMixBenchScene` stay raw GL. They run inside `glDevice.RunNative(...)`,
    which resets the backend's cached GL state afterwards, because they change the bound program and
    buffers behind its back. Their compute programs use `GLProgram`. `ThirtyDollarWorkflow.cs:86`
    checks `Device is GLGraphicsDevice` and `Capabilities.SupportsCompute`.
- Port musicrain. If it only uses Components and Markup, only the `UIContext.Render` signature
  changes. Any custom renderables follow the table above.
- Compare both apps against the phase 0 baseline, then release stable.

### Done when

- Only `Sundex.Engine/Renderer/OpenGL/` imports `OpenTK.Graphics.OpenGL`, and the step 6 checks pass.
- Tests and the extended smoke sample pass on Mesa.
- Both apps look the same as the baseline screenshots, with frame times within noise.

## Phase 2: remove GL-only behavior, still on GL

Optional before phase 3, but cheap, and every problem it finds is easier to debug on GL than on a new
backend.

**2.1 Frame hazard check, in development builds.**

- Each realized resource records the frame it was last bound for reading. A CPU write to a resource
  already read in the same frame logs an error naming the resource and the label of its first read.
  CPU writes are `SetData`, an indexed write that reaches the GPU, and a texture upload.
  `PushUniforms` is exempt.
- Run both apps through their usual flows (the Visualizer's headless driver, the Editor, musicrain's
  pages) and move any offending writes from `Render` into `Update`.
- With this rule in place, the Vulkan backend can put every upload it finds while recording into a
  separate command buffer, submitted before the frame. Uploads don't need to move to the start of the
  frame, which the first assessment suggested.

**2.2 Offscreen color target and backdrop.**

- `GPUTexture` gets a render-target usage, and `PassDescription.Target` accepts one. The GL backend
  backs it with a framebuffer object.
- `UIContext` draws into a window-sized color texture, which is blitted to the window at the end of
  the frame.
- `CopyFromTarget` then reads that texture instead of the window. Vulkan can do this without
  requiring transfer usage on swapchain images.
- Backdrop textures come from a small pool keyed by size rounded up to 64 px. Copy a region that much
  larger, with its real neighboring pixels, and pass the used part through `BoxPlane`'s existing
  `UvRect`, which today is always the full texture. Nothing is allocated per frame.
- Measure the extra full-screen blit against the baseline. If it shows, draw offscreen only on frames
  that queue a backdrop box.
- 3D needs offscreen targets anyway, for HDR color, depth and post-processing.

### Done when

- The hazard check reports nothing during normal use of either app.
- Backdrop blur looks the same, frame times are within noise, and no texture is allocated per frame.

### Outcome, 2026-11-20

Both conditions hold. Measurements and screenshots are in `docs/render-backend-baseline/README.md`.

2.1:

- `ResourceBacking` records the first read of each frame, and `FrameHazards` reports a later CPU write
  once per resource, with the write's stack trace. It runs when `SundexDevelopment` is on.
- Every report in the apps came from another thread writing while the render thread recorded, not from
  `Render`. All were in ThirtyDollarTools:
  - The loader thread added images to the atlases and the loading background. The image is now
    decoded on that thread, and the add is posted to the UI thread with `Frame.Post`. The loader
    waits for the posted adds before it reports the assets as loaded.
  - The `SequencePlayer` thread rewrote `!loopmany` and `!stop` value text. The handlers now post
    `SetValue`.
- musicrain reported nothing.

2.2, with three changes from the plan above:

- `Game` owns the window-sized target, not `UIContext`. Scenes draw outside `UIContext` in the same
  pass, and a backdrop has to see those draws.
- The new target costs a full-screen copy: 4 to 7 µs of GPU time per frame at p50. So `Game` draws
  offscreen only while the previous frame copied from its target. The first frame that copies reads
  the window, which GL allows. ThirtyDollarTools has no backdrops and keeps drawing to the window.
- The API gained `ICommandList.TargetSize`, which the backdrop needs to clamp its copy, and
  `CopyToTarget`, which puts the frame on the window.

`CopyFromTarget` now copies into a region of an existing texture with `glCopyTexSubImage2D`.
Texel row 0 receives the bottom row of the copied region. A Vulkan backend meets that by flipping
the offsets of `vkCmdBlitImage`.

For phase 3:

- Swapchain images may lack transfer-source usage. The Vulkan backend then has to draw offscreen
  every frame, because the first copying frame reads the window.
- A render target sampled in a later pass comes out upside down on Vulkan compared with GL. Decide
  the convention before post-processing (3.7).

## Phase 3: Vulkan backend, with 3D

This starts when the 3D work does. Reach 2D parity first, then build 3D on top.

GL stays the 2D and compatibility backend, and 3D features are behind `DeviceCapabilities` that
provide the needed things. Don't stretch GL 3.3 into a high-performance 3D path: it has no compute,
no storage buffers and no indirect multi-draw. If the target device supports GL 4.3 or later, these
can be enabled.

### 3.0 Decisions to re-check at the start

The ecosystem will have moved by then.

- **Vulkan version.** Target 1.3 core: dynamic rendering, synchronization2 and maintenance4, plus
  descriptor indexing, timeline semaphores and buffer device address from 1.2. Check MoltenVK's
  coverage if macOS should run Vulkan.
- **Bindings.** `OpenTK.Graphics.Vulkan` ships in `5.0.0-pre.16`. Confirm it is in OpenTK 5 stable
  and check its shape.
- **Memory allocator.** Either write a sub-allocator (large blocks per memory type, a free list for
  long-lived resources, a linear ring per frame) or bind VMA. A simple allocator is a few hundred
  lines. VMA is proven but needs a native library on every platform.
- **Shader toolchain.** Vulkan needs SPIR-V. Two options:
  - GLSL 450 through glslang or shaderc, with SPIRV-Cross generating the GLSL the GL backend keeps
    using, so there is one source.
  - Slang, which targets SPIR-V, GLSL and Metal and has modules, which helps a large 3D shader base.

  Either way, reflection data replaces the hand-written `ShaderLayout`. Hot reload needs the compiler
  at runtime in development builds, so prefer a library over a command-line tool.

### 3.1 Projects and backend choice

- Split into `Sundex.Graphics` (the API), `Sundex.Graphics.OpenGL` and `Sundex.Graphics.Vulkan`. The
  engine depends on the API only.
- The app picks a backend in its settings: `Auto`, `Vulkan` or `OpenGL`. `Auto` uses Vulkan when
  `GLFW.VulkanSupported()` is true and the device has the required features, and GL otherwise.

### 3.2 Window and swapchain

- Create the window with `ContextAPI.NoAPI`. Check that OpenTK 5's `GameWindow` runs without a
  context. If it doesn't, derive `Game` from `NativeWindow` and own the loop. `Game.OnUpdateFrame`
  calls `MakeCurrent()` today; that call must become GL-only.
- Create the surface with `GLFW.CreateWindowSurface`. Rebuild the swapchain on resize,
  `VK_ERROR_OUT_OF_DATE_KHR` and `VK_SUBOPTIMAL_KHR`.
- Take the present mode from `Vsync`: FIFO when on. When off, MAILBOX if available, otherwise
  IMMEDIATE.

### 3.3 Frame structure

- 2 frames in flight. Each has its own command pool, upload command buffer, uniform ring, descriptor
  allocator, and list of resources waiting to be freed. One timeline semaphore counts frames.
- The upload command buffer takes every copy, layout transition and mipmap blit found while
  recording. It is submitted before the frame's main command buffer.
- `PushUniforms` writes into the frame's ring and binds with a dynamic offset, aligned to
  `minUniformBufferOffsetAlignment`. The ring is host-visible memory, device-local as well on
  resizable-BAR systems.
- Push constants are not the general path for per-draw data. Even without `Projection`,
  `GradientUniform` is 352 bytes and `BoxPlane`'s block 176, both above the 128 bytes Vulkan
  guarantees for push constants.

### 3.4 Resources

- Static buffers and textures live in device-local memory and are filled through staging.
- `WithCPUCache` buffers: copy the dirty range into the ring, then from there to the device buffer in
  the upload command buffer.
- Mipmaps are a `vkCmdBlitImage` chain. Check that the format supports linear-filtered blits. Slug's
  `Rgba32Float` textures have no mipmaps, so that case doesn't come up today.

### 3.5 Pipelines and descriptors

- A `PipelineDescription` becomes a `VkPipeline` built for dynamic rendering, so there are no render
  pass objects. Viewport and scissor are dynamic state.
- Persist `VkPipelineCache` data to disk through `CacheProvider`.
- Each shader gets one descriptor set, filled with push descriptors (`VK_KHR_push_descriptor`) or from
  a per-frame pool. `View` and `Draw` use dynamic offsets.
- For 3D, go bindless: one large array of sampled images and samplers through descriptor indexing,
  indexed from instance or draw data.

### 3.6 Synchronization

- Use synchronization2 barriers. The command list tracks each resource's last access, which is enough
  for the 2D path's single pass plus copies.
- 3D wants a render graph that derives barriers and aliases transient targets. Build it during the 3D
  work, not before.

### 3.7 Clip space and scissor

- Keep the cameras as they are. The backend sets a negative viewport height (core since 1.1), so Y
  stays up. `SetView` multiplies the view-projection matrix by a fixed matrix that maps depth from
  -1..1 to 0..1.
- For 3D, use reversed-Z with a 32-bit float depth buffer. 3D cameras ask the device for its depth
  convention instead of relying on the `SetView` correction. GL can match with `glClipControl` where
  `GL_ARB_clip_control` exists.
- The scissor needs no flip, because Vulkan's origin is top-left. `null` becomes the full target,
  since Vulkan can't turn scissoring off.

### 3.8 Diagnostics

- Enable validation layers and `VK_EXT_debug_utils` in development builds. The messenger logs through
  Serilog like the GL callback does, and `RenderMarker` becomes debug-utils labels.
- `GpuTimer` uses timestamp queries, scaled by `timestampPeriod`.
- `ReadPixels` copies the phase 2 offscreen target into a host buffer and waits.

### 3.9 GPU voice mixer

- Move the mixer onto the API once compute is there: `Dispatch`, storage buffers, readback buffers and
  fences. Design those for 3D culling and simulation at the same time.
- Implement compute on Vulkan and on GL 4.3 or later, both behind `Capabilities.SupportsCompute`. The
  mixer then runs on either backend through the API, and its `RunNative` scope goes.
- On Vulkan, run it on an async compute queue if the device has one, and the GL fences become
  timeline semaphore waits. On GL they stay fences.
- Until compute is in the API, the mixer runs only on the GL backend. On Vulkan, the CPU path at
  `ThirtyDollarWorkflow.cs:86` takes over.

### 3.10 Testing

- CI installs lavapipe, Mesa's software Vulkan driver (`mesa-vulkan-drivers`), and the validation
  layers (`vulkan-validationlayers`). It runs the core sample on both backends. Any validation
  message fails the run.
- The same pixel probes run on both backends, with a small tolerance.
- Compare GL and Vulkan frame times on the phase 0 scenes.

### Milestone: 2D parity

Both apps run on Vulkan with no validation messages, look the same as on GL, and are no slower.

### After parity: 3D foundations

These are not planned here. They are listed to show that phase 1 leaves room for them:

- Depth and multisampled targets, HDR, post-processing.
- A render graph.
- Multi-threaded recording: one command list per thread, each with its own command pool.
- GPU-driven rendering: indirect and multi-draw indirect, compute culling.
- Bindless materials.
- Async compute.
- Mesh shaders, optionally.

Each is gated by a `DeviceCapabilities` flag. The GL backend implements the ones GL 4.3 or later
has (compute, storage buffers, multi-draw indirect) when the device supports them. It reports the
rest (async compute, multi-threaded recording, bindless) as unsupported.

Each is an addition to the phase 1 shape, not a change to it:

- Command lists are objects, so there can be several.
- Passes and pipelines are explicit.
- Bindings are numbered.
- Resources declare their usage.
- Nothing reads back synchronously except `ReadPixels`.

## Risks

- **Per-draw uniforms on Vulkan.** The UI issues one draw per plane. With thousands of planes,
  per-draw ring allocation and descriptor binding cost CPU time. Mitigation: dynamic offsets into one
  buffer per frame. If that still shows, instance the planes the way text and `RenderStack` already
  instance their draws, as separate work.
- **Raw GL in the apps.** The mixer changes GL state the backend caches. `RunNative` resets the cache.
  A missing reset shows up as wrong blending or bindings on the next draw.
- **Two apps break at once.** Mitigation: one prerelease, with both apps ported before stable.
- **OpenTK 5 is a prerelease.** Its Vulkan bindings can change before stable. The backend keeps them
  out of the rest of the engine.
- **Native dependencies.** The shader compiler, and VMA if chosen, need native libraries on Linux,
  Windows and macOS. Decide in 3.0.

## Rough effort

Solo and focused. These are estimates only.

- Phase 0: an afternoon.
- Phase 1: 1.5 to 2.5 weeks, including the app ports. This is more than the first assessment's week
  because pipelines, a command list and the binding convention land now, instead of a thin GL wrapper.
  In exchange, the Vulkan work adds to the API instead of breaking it again.
- Phase 2: 3 to 5 days.
- Phase 3, to 2D parity: 3 to 5 weeks. The 3D foundations are open-ended.

## Issues found during the survey

The plan's deletions fix these. They are listed so they aren't lost.

- `GLBuffer<T>.SetMemory` in the base class grows the buffer only when `Capacity < index`, so a write
  at `index == Capacity` overruns the mapped span. Latent: nothing seems to write through the base
  indexer. Deleted in step 4.
- `GLBuffer<T>.DangerousGLThread_Resize` reallocates with `glBufferData(…, null)`, which drops the
  existing contents. Only the pending updates are written back. Latent for the same reason. Deleted in
  step 4.
- `Game.OnRenderFrame` sets the clear color after clearing, so it applies to the next frame. Harmless
  while the color is constant. Deleted in step 1.
- `BoxPlane.CopyBackdrop` allocates an `int[4]` and queries the viewport from GL on every call, and
  reallocates the backdrop texture on every copy. Replaced in step 5 and phase 2: the copies now come
  from up to eight pooled textures, rounded up to 64 px.
