using OpenTK.Windowing.Common;
using Serilog;
using Sundex.Engine.AssetManagement;
using Sundex.Engine.Input;
using Sundex.Engine.Scenes.Arguments;
using Sundex.Engine.Threading;

namespace Sundex.Engine.Scenes;

public abstract class Scene(GameRuntime runtime)
{
    public GameRuntime Runtime { get; } = runtime;
    public AssetProvider AssetProvider => Runtime.AssetProvider;
    public SceneManager SceneManager => Runtime.SceneManager;
    public ThreadRunner ThreadRunner => Runtime.ThreadRunner;
    public ILogger Logger => Runtime.Logger;


    /// <summary>
    ///     Method called during the OnLoad procedure.
    /// </summary>
    public abstract void Initialize(InitArguments initArguments);

    /// <summary>
    ///     Method called by the manager after all scenes are loaded.
    /// </summary>
    public abstract void Start();

    /// <summary>
    ///     Method called by the render loop.
    /// </summary>
    public abstract void Render(RenderArguments renderArgs);

    /// <summary>
    ///     Method called when the scene is transitioned to.
    /// </summary>
    public abstract void TransitionedTo();

    /// <summary>
    ///     Method called by the update loop.
    /// </summary>
    public abstract void Update(UpdateArguments updateArgs);

    /// <summary>
    ///     Method called when the window is resized.
    /// </summary>
    /// <param name="width">The width of the resized window.</param>
    /// <param name="height">The height of the resized window.</param>
    public abstract void Resize(int width, int height);

    /// <summary>
    ///     Method that should be called when releasing scene resources.
    /// </summary>
    public abstract void Shutdown();

    /// <summary>
    ///     Triggered when a file is dropped on the window.
    /// </summary>
    /// <param name="locations">The location of the dropped files.</param>
    public abstract void FileDrop(string[] locations);

    /// <summary>
    ///     Rebuilds this scene's UI from its markup, for hot reload. Virtual and empty:
    ///     a scene overrides it only if its interface can be thrown away and built again,
    ///     which one holding live playback state cannot.
    ///     <para>
    ///         Build the new interface into a fresh <c>UIContext</c> and only then assign both
    ///         fields, so that a markup or stylesheet saved mid-edit - which throws while it is
    ///         being built - leaves the running UI untouched; <see cref="SceneManager.Reload" />
    ///         logs the failure and moves on.
    ///     </para>
    /// </summary>
    public virtual void ReloadUI()
    {
    }

    /// <summary>
    ///     Re-reads this scene's stylesheets and applies them to the tree already on screen,
    ///     keeping everything the live UI holds. Used for a stylesheet-only edit, where the
    ///     full <see cref="ReloadUI" /> is not needed.
    /// </summary>
    public virtual void ReloadStyles()
    {
    }

    /// <summary>
    ///     Event triggered when a keyboard button is pressed.
    /// </summary>
    /// <param name="state">The keys this update.</param>
    public abstract void Keyboard(KeyboardInput state);

    /// <summary>
    ///     Event triggered when a pointing device is interacted with.
    /// </summary>
    /// <param name="mouseState">The pointer this update.</param>
    /// <param name="keyboardState">The keys this update.</param>
    public abstract void Mouse(PointerInput mouseState, KeyboardInput keyboardState);

    /// <summary>
    ///     Event triggered when the window receives a unicode text input.
    ///     Virtual so scenes without text entry can ignore it.
    /// </summary>
    /// <param name="args">The text input event, carrying the unicode codepoint.</param>
    public virtual void TextInput(TextInputEventArgs args)
    {
    }

    /// <summary>
    ///     Event triggered when a key is pressed (including OS key repeats).
    ///     Virtual so scenes using only the polled <see cref="Keyboard" /> path can ignore it.
    /// </summary>
    /// <param name="args">The key event, including <see cref="KeyboardKeyEventArgs.IsRepeat" />.</param>
    public virtual void KeyDown(KeyboardKeyEventArgs args)
    {
    }
}
