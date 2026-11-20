using OpenTK.Mathematics;
using Sundex.Components.Abstractions;
using Sundex.Core;
using Sundex.Core.Animations;
using Sundex.Engine.Renderer;

namespace Sundex.Components.Tests;

public class UIElementTests
{
    [Fact]
    public void TestAnimations_RegistersInContext()
    {
        var context = new TestUIContext();
        var element = new TestElement(context);
        var animation = new KeyframedAnimation([new Keyframe { Position = Vector3.Zero, LengthMs = 100 }]);

        element.AddAnimation(animation);
        Assert.Single(element.Animations);

        // UIElement.Update should be called during Render
        var updateCalled = false;
        element.OnUpdate = () => updateCalled = true;

        context.Render(new RecordingCommandList());
        Assert.True(updateCalled);

        element.RemoveAnimation(animation);
        Assert.Empty(element.Animations);

        updateCalled = false;
        context.Render(new RecordingCommandList());
        Assert.False(updateCalled);
    }

    [Fact]
    public void RenderPassesTopLeftPhysicalScissorsAndClearsTheLastClip()
    {
        var context = new TestUIContext { PixelScale = (2, 1.5f) };
        var commands = new RecordingCommandList();
        context.QueueRender(new DummyRenderable { Scale = Vector3.One, ClipRect = (3, 4, 13, 12) }, 0);

        context.Render(commands);

        Assert.Collection(commands.Calls,
            call => Assert.Equal(nameof(ICommandList.SetView), call.Name),
            call => Assert.Equal(new Box2i(6, 6, 26, 18), Assert.IsType<Box2i>(call.Data)),
            call => Assert.Equal((nameof(ICommandList.SetScissor), (object?)null), call));
    }

    [Fact]
    public void TestAbsoluteCoordinates_NoParent()
    {
        var context = new TestUIContext();
        var element = new TestElement(context, 10, 20);

        element.Layout();

        Assert.Equal(10, element.Computed.AbsoluteX);
        Assert.Equal(20, element.Computed.AbsoluteY);
    }

    [Fact]
    public void TestAbsoluteCoordinates_WithParent()
    {
        var context = new TestUIContext();
        var parent = new TestElement(context, 100, 200);
        var child = new TestElement(context, 10, 20) { Parent = parent };

        parent.Layout();
        child.Layout();

        Assert.Equal(110, child.Computed.AbsoluteX);
        Assert.Equal(220, child.Computed.AbsoluteY);
    }

    [Fact]
    public void TestAbsoluteCoordinates_Nested()
    {
        var context = new TestUIContext();
        var root = new TestElement(context, 100, 100);
        var middle = new TestElement(context, 50, 50) { Parent = root };
        var leaf = new TestElement(context, 10, 10) { Parent = middle };

        root.Layout();
        middle.Layout();
        leaf.Layout();

        Assert.Equal(160, leaf.Computed.AbsoluteX);
        Assert.Equal(160, leaf.Computed.AbsoluteY);
    }

    [Fact]
    public void TestInvalidateCoordinates_PropagatesToChildren()
    {
        // A bare UIElement has no Children, so this covers the leaf case: setting X/Y
        // invalidates the cached coordinates.
        var context = new TestUIContext();
        var element = new TestElement(context, 10, 10);

        element.Layout();
        // Access Computed.AbsoluteX to clear dirty flag
        _ = element.Computed.AbsoluteX;

        element.X = 20;
        element.Layout();
        Assert.Equal(20, element.Computed.AbsoluteX);
    }

    private sealed class TestElement : UIElement
    {
        public TestElement(UIContext context, float x = 0, float y = 0, float width = 0, float height = 0)
            : base(context)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public Action? OnUpdate { get; set; }

        public override string Tag => "test";

        public override void Update(UIContext uiContext)
        {
            base.Update(uiContext);
            OnUpdate?.Invoke();
        }

        protected override void DrawSelf(UIContext context)
        {
        }
    }
}