using OpenTK.Mathematics;
using Sundex.Core.Animations;
using Sundex.Engine.Renderer.Abstract;
using Sundex.Engine.Renderer.Cameras;
using Sundex.Engine.Renderer.Shaders;

namespace Sundex.Core;

public abstract class Renderable : IRenderable, IPositionable, IClippable
{
    public readonly List<Renderable> Children = [];

    /// <summary>
    ///     A boolean made for external use.
    /// </summary>
    public bool IsBeingUpdated = false;

    public bool IsChild;

    /// <summary>
    ///     Sets whether this renderable calls it's render method. Honored by the UI's
    ///     render pass (<c>UIContext.Render</c>); a renderable that knows it would draw
    ///     nothing (a fully transparent fill, say) clears this instead of being dequeued.
    /// </summary>
    public bool IsVisible = true;

    public virtual Shader Shader { get; set; } = Shader.Dummy;

    /// <summary>
    ///     The rotation of the current renderable.
    /// </summary>
    public virtual Vector3 Rotation { get; set; }

    /// <summary>
    ///     The offset position of the current renderable. Intended for dynamic positioning (eg. animations)
    /// </summary>
    public virtual Vector3 Translation { get; set; }

    /// <summary>
    ///     The model matrix of the current renderable.
    /// </summary>
    public virtual Matrix4 Model { get; set; }

    /// <summary>
    ///     The color of the current renderable.
    /// </summary>
    public virtual Vector4 Color { get; set; }

    /// <summary>
    ///     Represents the inverse alpha value used in renderable objects to influence transparency or animation.
    /// </summary>
    protected float InverseAlpha { get; set; }

    /// <inheritdoc />
    public Vector4i? ClipRect { get; set; }

    /// <summary>
    ///     The position of the current renderable.
    /// </summary>
    public virtual Vector3 Position { get; set; }

    /// <summary>
    ///     The scale of the current renderable.
    /// </summary>
    public virtual Vector3 Scale { get; set; }

    /// <summary>
    ///     Renders a given renderable using the projection matrix from the camera.
    /// </summary>
    /// <param name="camera">The camera you want to use.</param>
    public virtual void Render(Camera camera)
    {
        foreach (var child in Children) child.Render(camera);
    }

    /// <summary>
    ///     Updates the current renderable's model for the MVP rendering method.
    /// </summary>
    /// <param name="isChild">Whether the current renderable is a child of another renderable.</param>
    /// <param name="animations">The animations the current renderable will use.</param>
    public virtual void UpdateModel(bool isChild, Span<Animation> animations = default)
    {
        IsChild = isChild;
        var model = Matrix4.Identity;

        var position = Position;
        var scale = Scale;
        var translation = Translation;

        var tempTranslation = Vector3.Zero;
        var tempScale = Vector3.One;
        var tempRotation = Vector3.Zero;

        foreach (var animation in animations)
            if ((IsChild && animation.AffectsChildren) || !IsChild)
                ComputeAnimation(animation, ref tempTranslation, ref tempScale, ref tempRotation);

        var finalTranslation = position + translation + tempTranslation;
        var finalScale = scale * tempScale;
        var finalRotation = Rotation + tempRotation;

        model *= Matrix4.CreateScale(finalScale);

        model *= Matrix4.CreateRotationX(finalRotation.X) *
                 Matrix4.CreateRotationY(finalRotation.Y) *
                 Matrix4.CreateRotationZ(finalRotation.Z);

        model *= Matrix4.CreateTranslation(finalTranslation);

        foreach (var renderable in Children) renderable.UpdateModel(true, animations);

        Model = model;
    }

    /// <summary>
    ///     Computes a given animation.
    /// </summary>
    /// <param name="animation">The given animation.</param>
    /// <param name="finalTranslation">Reference to the final translation.</param>
    /// <param name="finalScale">Reference to the final scale.</param>
    /// <param name="finalRotation">Reference to the final rotation.</param>
    private void ComputeAnimation(Animation animation, ref Vector3 finalTranslation, ref Vector3 finalScale,
        ref Vector3 finalRotation)
    {
        var bitStack = animation.Features;

        if (bitStack.IsEnabled(AnimationFeature.TransformMultiply))
        {
            var transformMultiply = animation.GetTransform_Multiply(this);
            if (transformMultiply != Vector3.One)
                finalTranslation *= transformMultiply;
        }

        if (bitStack.IsEnabled(AnimationFeature.TransformAdd))
        {
            var transformAdd = animation.GetTransform_Add(this);
            if (transformAdd != Vector3.One)
                finalTranslation += transformAdd;
        }

        if (bitStack.IsEnabled(AnimationFeature.ScaleMultiply))
        {
            var scale = animation.GetScale_Multiply(this);

            if (scale != Vector3.One)
                finalScale *= scale;
        }

        if (bitStack.IsEnabled(AnimationFeature.ScaleAdd))
        {
            var scale = animation.GetScale_Add(this);

            if (scale != Vector3.One)
                finalScale += scale;
        }

        if (bitStack.IsEnabled(AnimationFeature.RotationAdd))
        {
            var rotation = animation.GetRotation_XYZ(this);

            if (rotation != Vector3.Zero)
                finalRotation += rotation;
        }

        if (bitStack.IsEnabled(AnimationFeature.ColorValue))
        {
            var colorChange = animation.GetColor_Value(this);
            if (colorChange != Vector4.Zero)
                Color = colorChange;
        }

        if (!bitStack.IsEnabled(AnimationFeature.DeltaAlpha)) return;
        InverseAlpha = animation.GetAlphaDelta_Value(this);
        Color = new Vector4(Color.X, Color.Y, Color.Z, 1 - InverseAlpha);
    }

    public virtual void Update()
    {
        // Override if needed.
    }

    /// <summary>
    ///     Method that sets the shader's uniforms if overriden.
    /// </summary>
    /// <param name="camera">The camera that contains main projection matrix.</param>
    public virtual void SetShaderUniforms(Camera camera)
    {
        // Implement if needed.
    }

    /// <summary>
    ///     Sets the renderable's translation.
    /// </summary>
    /// <param name="translation">The translation.</param>
    public virtual void SetTranslation(Vector3 translation)
    {
        Translation = translation;
        UpdateModel(IsChild);
        foreach (var renderable in Children) renderable.SetTranslation(translation);
    }

    ~Renderable()
    {
        Children.Clear();
    }
}