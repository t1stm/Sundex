using OpenTK.Mathematics;
using Sundex.Core;
using Sundex.Core.Renderer.Planes;
using Sundex.Engine.AssetManagement;
using Sundex.Engine.AssetManagement.Types.Asset;
using Sundex.Engine.AssetManagement.Types.Texture;
using Sundex.Engine.Renderer;
using Sundex.Engine.Renderer.Cameras;
using Sundex.Engine.Renderer.Textures;
using Sundex.Engine.Text;
using Sundex.Engine.Text.Fonts;

namespace FlyingMoai;

/// <summary>
///     The island at dusk and the rules of a run: everything drawn under the UI, with Sundex.Core's
///     planes and Slug text. All of it is written in <see cref="Update" />, so a frame only reads.
///     The world is 720 units tall at any window size; its width follows the window.
/// </summary>
public sealed class World : IDisposable
{
    private const float Height = 720;
    private const float Horizon = 548;
    private const float Ground = 640;

    private const float Gravity = 2100;
    private const float ClapSpeed = -640;
    private const float MaxFall = 950;
    private const float MoaiSize = 76;
    private const float HitRadius = 24;
    private const float PillarWidth = 92;
    private const float CapOverhang = 9;
    private const float CapHeight = 26;
    private const float PillarSpacing = 290;

    private static readonly Vector4[] DuskSky = [Hex(0x2b1b4aff), Hex(0x6b2f6bff), Hex(0xd9576bff), Hex(0xffb36bff)];
    private static readonly Vector4[] NightSky = [Hex(0x0b0a1fff), Hex(0x1d1840ff), Hex(0x3a2b5eff), Hex(0x6a4a7aff)];

    private readonly WorldCamera _camera = new();
    private readonly Random _random = new();
    private readonly List<IDisposable> _owned = [];
    private readonly TextBuffer _text;

    private readonly GradientPlane _sky = new() { GradientStops = [0, 0.45f, 0.8f, 1] };
    private readonly GradientPlane _sunGlow = new() { GradientType = GradientPlane.Type.Radial };
    private readonly TexturedPlane _sun;
    private readonly Vector2 _sunSize;
    private readonly ColoredPlane[] _clouds = Make(15, () => new ColoredPlane());
    private readonly ColoredPlane[] _islands = Make(4, () => new ColoredPlane());
    private readonly ColoredPlane _sea = new();
    private readonly ColoredPlane[] _glints = Make(7, () => new ColoredPlane { BorderRadius = 2 });
    private readonly ColoredPlane _sand = new();
    private readonly ColoredPlane _shore = new();
    private readonly ColoredPlane[] _pebbles = Make(9, () => new ColoredPlane());
    private readonly TexturedPlane _moai;
    private readonly ColoredPlane _flash = new() { Color = Vector4.Zero };

    private readonly List<Pillar> _pillars = [];
    private readonly Stack<Pillar> _spare = [];
    private readonly Ring[] _rings;
    private readonly Spark[] _sparks = Make(48, () => new Spark());
    private readonly Popup[] _popups;

    private float _width = 480;
    private float _time;
    private float _distance;
    private float _y;
    private float _velocity;
    private float _tilt;
    private float _squash;
    private float _shake;
    private int _passed;
    private bool _crashed;

    public World(AssetProvider assets)
    {
        // The statue looks left; the island scrolls right to left, so mirror it to face the pillars.
        _moai = new TexturedPlane
        {
            Texture = LoadTexture(assets, "Assets/Textures/moai.png"),
            UvRect = new Vector4(1, 0, -1, 1),
            Scale = (MoaiSize, MoaiSize, 1)
        };
        var sun = LoadTexture(assets, "Assets/Textures/sun.png");
        _sun = new TexturedPlane { Texture = sun };
        _sunSize = new Vector2(170, 170f * sun.Height / sun.Width);

        _text = new TextBuffer(new TextProvider(assets, new FontProvider(assets), "Lato Bold"));
        _popups = Make(4, () => new Popup(_text.GetTextSlice("+1")));
        _rings = Make(8, () => new Ring());
        Reset();
    }

    public Camera Camera => _camera;

    /// <summary>A pillar went by. Counted only while <see cref="Phase.Playing" />.</summary>
    public Action Scored { get; set; } = () => { };

    /// <summary>The moai hit a pillar or the beach.</summary>
    public Action Crashed { get; set; } = () => { };

    private float MoaiX => _width * 0.28f;

    public void Reset()
    {
        foreach (var pillar in _pillars) _spare.Push(pillar);
        _pillars.Clear();
        _y = Height * 0.42f;
        _velocity = 0;
        _tilt = 0;
        _passed = 0;
        _crashed = false;
    }

    public void Clap()
    {
        if (_crashed) return;
        _velocity = ClapSpeed;
        _squash = 1;
        Burst(new Vector2(MoaiX - 6, _y + 8), 0.9f, 4);
    }

    public void Resize(int framebufferWidth, int framebufferHeight)
    {
        _width = MathF.Round(Height * framebufferWidth / Math.Max(1, framebufferHeight));
        _camera.Viewport = new Vector2i((int)_width, (int)Height);
        _camera.UpdateMatrix();
    }

    public void Update(float delta, Phase phase)
    {
        delta = Math.Min(delta, 1 / 30f);
        _time += delta;
        if (phase == Phase.Title) Autopilot();

        var speed = 190 + 4 * Math.Min(_passed, 25);
        if (!_crashed)
        {
            _distance += speed * delta;
            MovePillars(speed * delta, phase);
        }

        Fall(delta, phase);
        UpdateEffects(delta);
        Paint();
    }

    public void Render(in RenderContext context)
    {
        _sky.Render(context);
        _sunGlow.Render(context);
        _sun.Render(context);
        foreach (var cloud in _clouds) cloud.Render(context);
        foreach (var island in _islands) island.Render(context);
        _sea.Render(context);
        foreach (var glint in _glints) glint.Render(context);

        foreach (var pillar in _pillars) pillar.Render(context);

        _shore.Render(context);
        _sand.Render(context);
        foreach (var pebble in _pebbles) pebble.Render(context);

        _moai.Render(context);
        foreach (var ring in _rings)
            if (ring.Life > 0) ring.Plane.Render(context);
        foreach (var spark in _sparks)
            if (spark.Life > 0) spark.Plane.Render(context);

        _text.Render(context);
        if (_flash.IsVisible) _flash.Render(context);
    }

    // The title screen plays itself: clap whenever the moai sinks below the next gap's middle.
    private void Autopilot()
    {
        var next = _pillars.FirstOrDefault(pillar => pillar.X + PillarWidth + CapOverhang > MoaiX - HitRadius);
        var target = next == null ? Height * 0.42f : next.GapY + next.Gap * 0.18f;
        if (_y > target && _velocity > 0) Clap();
    }

    private void MovePillars(float step, Phase phase)
    {
        foreach (var pillar in _pillars) pillar.X -= step;

        while (_pillars.Count > 0 && _pillars[0].X + PillarWidth + CapOverhang < 0)
        {
            _spare.Push(_pillars[0]);
            _pillars.RemoveAt(0);
        }

        var nextX = _pillars.Count == 0 ? _width + 140 : _pillars[^1].X + PillarSpacing;
        if (nextX < _width + PillarSpacing) Spawn(nextX);

        foreach (var pillar in _pillars)
        {
            if (pillar.Scored || pillar.X + PillarWidth > MoaiX - HitRadius) continue;
            pillar.Scored = true;
            _passed++;
            if (phase != Phase.Playing) continue;
            Scored();
            ShowPopup(new Vector2(MoaiX + 20, _y - 96));
        }
    }

    private void Spawn(float x)
    {
        var gap = Math.Max(168f, 232 - 3 * _passed);
        var lowest = Ground - 60 - gap / 2;
        var highest = 120 + gap / 2;
        var y = highest + (float)_random.NextDouble() * (lowest - highest);
        // A gap far from the last one can't be reached in time; keep the climb fair.
        if (_pillars.Count > 0) y = Math.Clamp(y, _pillars[^1].GapY - 200, _pillars[^1].GapY + 200);

        var pillar = _spare.Count > 0 ? _spare.Pop() : new Pillar();
        pillar.X = x;
        pillar.GapY = y;
        pillar.Gap = gap;
        pillar.Scored = false;
        _pillars.Add(pillar);
    }

    private void Fall(float delta, Phase phase)
    {
        _velocity = Math.Min(_velocity + Gravity * delta, MaxFall);
        _y += _velocity * delta;

        if (_y < MoaiSize / 2)
        {
            _y = MoaiSize / 2;
            _velocity = 0;
        }

        const float floor = Ground - MoaiSize * 0.42f;
        if (_y >= floor)
        {
            _y = floor;
            _velocity = 0;
            switch (phase)
            {
                case Phase.Playing:
                    Crash();
                    break;
                case Phase.Title:
                    Clap();
                    break;
            }
        }
        else if (phase == Phase.Playing && HitsPillar())
        {
            Crash();
        }

        var lean = _crashed && _y >= floor ? 1.35f : Math.Clamp(_velocity / 900 * 1.1f, -0.45f, 1.2f);
        _tilt += (lean - _tilt) * Math.Min(1, delta * 12);
        _squash = Math.Max(0, _squash - delta * 6);
    }

    private bool HitsPillar()
    {
        var centre = new Vector2(MoaiX, _y);
        foreach (var pillar in _pillars)
        {
            var top = pillar.GapY - pillar.Gap / 2;
            var bottom = pillar.GapY + pillar.Gap / 2;
            if (Touches(centre, pillar.X, -100, pillar.X + PillarWidth, top) ||
                Touches(centre, pillar.X, bottom, pillar.X + PillarWidth, Ground) ||
                Touches(centre, pillar.X - CapOverhang, top - CapHeight, pillar.X + PillarWidth + CapOverhang, top) ||
                Touches(centre, pillar.X - CapOverhang, bottom, pillar.X + PillarWidth + CapOverhang, bottom + CapHeight))
                return true;
        }

        return false;

        static bool Touches(Vector2 centre, float left, float top, float right, float bottom)
        {
            var nearest = new Vector2(Math.Clamp(centre.X, left, right), Math.Clamp(centre.Y, top, bottom));
            return (centre - nearest).LengthSquared < HitRadius * HitRadius;
        }
    }

    private void Crash()
    {
        if (_crashed) return;
        _crashed = true;
        _shake = 1;
        _flash.Color = new Vector4(1, 0.95f, 0.85f, 0.75f);
        Burst(new Vector2(MoaiX, _y), 2.2f, 14);
        Crashed();
    }

    /// <summary>A clap: a ring spreading from the moai's hands and sparks thrown out of it.</summary>
    private void Burst(Vector2 at, float strength, int sparks)
    {
        var ring = _rings.MinBy(candidate => candidate.Life)!;
        ring.At = at;
        ring.Life = 1;
        ring.Strength = strength;

        for (var index = 0; index < sparks; index++)
        {
            var spark = _sparks.MinBy(candidate => candidate.Life)!;
            var angle = (float)(_random.NextDouble() * Math.Tau);
            var speed = (120 + (float)_random.NextDouble() * 160) * strength;
            spark.At = at;
            spark.Velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed - Vector2.UnitX * 120;
            spark.Life = 1;
            spark.Size = 4 + (float)_random.NextDouble() * 4;
        }
    }

    private void ShowPopup(Vector2 at)
    {
        var popup = _popups.MinBy(candidate => candidate.Life)!;
        popup.At = at;
        popup.Life = 1;
    }

    private void UpdateEffects(float delta)
    {
        foreach (var ring in _rings) ring.Life = Math.Max(0, ring.Life - delta / 0.45f);
        foreach (var spark in _sparks)
        {
            if (spark.Life <= 0) continue;
            spark.Life = Math.Max(0, spark.Life - delta / 0.6f);
            spark.Velocity += new Vector2(0, 700) * delta;
            spark.At += spark.Velocity * delta;
        }

        foreach (var popup in _popups)
        {
            popup.Life = Math.Max(0, popup.Life - delta / 0.8f);
            popup.At -= new Vector2(0, 60) * delta;
        }

        _shake = Math.Max(0, _shake - delta * 2.5f);
        var flash = _flash.Color;
        _flash.Color = flash with { W = Math.Max(0, flash.W - delta * 2.2f) };
    }

    /// <summary>Writes the frame's state into the planes and the text buffer.</summary>
    private void Paint()
    {
        // Dusk turns to night and back every 80 seconds; the sun sets into the sea and rises again.
        var night = 0.5f - 0.5f * MathF.Cos(_time * MathF.Tau / 80);
        var shake = _shake * _shake * 9;
        _camera.Position = new Vector3(((float)_random.NextDouble() - 0.5f) * shake, ((float)_random.NextDouble() - 0.5f) * shake, 0);

        _sky.GradientColors = [.. DuskSky.Zip(NightSky, (dusk, dark) => Vector4.Lerp(dusk, dark, night))];
        Vertical(_sky, 0, 0, _width, Horizon + 4);

        var sun = new Vector2(_width * 0.7f, Horizon - 110 + 170 * night);
        _sunGlow.GradientColors = [Hex(0xffd08aff) with { W = 0.55f * (1 - night) }, Hex(0xff9a6aff) with { W = 0.25f * (1 - night) }, Vector4.Zero];
        _sunGlow.GradientStops = [0, 0.35f, 1];
        Place(_sunGlow, sun.X - 260, sun.Y - 260, 520, 520);
        // Redder as it sets.
        _sun.Color = Vector4.Lerp(Vector4.One, Hex(0xff9a70ff), night);
        Place(_sun, sun.X - _sunSize.X / 2, sun.Y - _sunSize.Y / 2, _sunSize.X, _sunSize.Y);

        // A cloud is a flat base with two puffs on it. Opaque, so the overlaps don't show.
        var cloudColor = Vector4.Lerp(Hex(0x8a4a7eff), Hex(0x2c2550ff), night);
        for (var cloud = 0; cloud < _clouds.Length / 3; cloud++)
        {
            var width = 120 + cloud * 37 % 90;
            var x = Wrap(cloud * 233 - _distance * 0.06f, width);
            var y = 90 + cloud * 61 % 220;
            var big = width * 0.42f;
            var small = width * 0.3f;
            Puff(_clouds[cloud * 3], x, y, width, 28);
            Puff(_clouds[cloud * 3 + 1], x + width * 0.16f, y + 14 - big, big, big);
            Puff(_clouds[cloud * 3 + 2], x + width * 0.5f, y + 18 - small, small, small);
        }

        var islandColor = Vector4.Lerp(Hex(0x4a2a55ff), Hex(0x1a1433ff), night);
        for (var index = 0; index < _islands.Length; index++)
        {
            var width = 180 + index * 71 % 140;
            var height = 30 + index * 23 % 40;
            var x = Wrap(index * 310 + 60 - _distance * 0.18f, width);
            _islands[index].Color = islandColor;
            _islands[index].BorderRadius = height;
            Place(_islands[index], x, Horizon - height * 0.6f, width, height * 1.2f);
        }

        _sea.Color = Vector4.Lerp(Hex(0x3b2f6eff), Hex(0x141230ff), night);
        Place(_sea, 0, Horizon, _width, Ground - Horizon + 4);
        for (var index = 0; index < _glints.Length; index++)
        {
            var row = index % 4;
            var width = 30 - row * 4;
            var x = sun.X - 70 + Wrap(index * 47 - _distance * (0.3f + row * 0.1f), 0) % 140;
            _glints[index].Color = Hex(0xffd9a0ff) with { W = (0.55f - row * 0.1f) * (1 - night) };
            Place(_glints[index], x, Horizon + 10 + row * 19, width, 3);
        }

        _shore.Color = Vector4.Lerp(Hex(0xf2c38bff), Hex(0x8a6a7aff), night);
        Place(_shore, 0, Ground, _width, 8);
        _sand.Color = Vector4.Lerp(Hex(0xc98a5eff), Hex(0x4a3550ff), night);
        Place(_sand, 0, Ground + 8, _width, Height - Ground - 8);
        var pebbleColor = Vector4.Lerp(Hex(0x9a6446ff), Hex(0x33243dff), night);
        for (var index = 0; index < _pebbles.Length; index++)
        {
            var width = 10 + index * 7 % 18;
            _pebbles[index].Color = pebbleColor;
            _pebbles[index].BorderRadius = 4;
            Place(_pebbles[index], Wrap(index * 97 - _distance, width), Ground + 22 + index * 29 % 50, width, 7);
        }

        foreach (var pillar in _pillars) pillar.Paint();

        // Centred on the moai, squashed by a clap and leaning with its speed.
        var stretch = new Vector2(1 + 0.16f * _squash, 1 - 0.14f * _squash);
        _moai.Model = Matrix4.CreateTranslation(-0.5f, -0.5f, 0) *
                      Matrix4.CreateScale(MoaiSize * stretch.X, MoaiSize * stretch.Y, 1) *
                      Matrix4.CreateRotationZ(_tilt * 0.6f) *
                      Matrix4.CreateTranslation(MoaiX, _y, 0);

        foreach (var ring in _rings)
        {
            if (ring.Life <= 0) continue;
            var size = (1 - ring.Life * ring.Life) * 90 * ring.Strength + 20;
            ring.Plane.BorderColor = Hex(0xfff1c8ff) with { W = ring.Life };
            ring.Plane.BorderWidths = new Vector4(2 + 3 * ring.Life);
            ring.Plane.BorderRadius = size / 2;
            Place(ring.Plane, ring.At.X - size / 2, ring.At.Y - size / 2, size, size);
        }

        foreach (var spark in _sparks)
        {
            if (spark.Life <= 0) continue;
            spark.Plane.Color = Vector4.Lerp(Hex(0xff8a5c00), Hex(0xffe9a8ff), spark.Life);
            spark.Plane.BorderRadius = spark.Size / 2;
            Place(spark.Plane, spark.At.X - spark.Size / 2, spark.At.Y - spark.Size / 2, spark.Size, spark.Size);
        }

        foreach (var popup in _popups)
        {
            popup.Slice.Position = new Vector3(popup.At.X, popup.At.Y, 0);
            popup.Slice.Color = Hex(0xffe08aff) with { W = popup.Life };
            popup.Slice.FontSize = 30 + 10 * (1 - popup.Life);
            popup.Slice.UpdateCharacters();
        }

        Place(_flash, -20, -20, _width + 40, Height + 40);
        return;

        void Puff(ColoredPlane puff, float x, float y, float width, float height)
        {
            puff.Color = cloudColor;
            puff.BorderRadius = height / 2;
            Place(puff, x, y, width, height);
        }
    }

    /// <summary>Queues an embedded image for upload. The pixels must outlive the upload, so both live as long as the world.</summary>
    private GPUTexture LoadTexture(AssetProvider assets, string location)
    {
        var pixels = assets.Load<TextureHolder, TextureInfo>(new TextureInfo
        {
            AssetInfo = new AssetInfo { Location = location, Storage = StorageLocation.Assembly }
        }).Texture;
        var texture = new GPUTexture { Width = pixels.Width, Height = pixels.Height };
        texture.QueueUploadToGPU(pixels.Frames.RootFrame);
        _owned.Add(texture);
        _owned.Add(pixels);
        return texture;
    }

    private float Wrap(float x, float width)
    {
        var span = _width + width + 40;
        return ((x % span) + span) % span - width - 20;
    }

    private static void Place(Renderable plane, float x, float y, float width, float height)
    {
        plane.Scale = new Vector3(width, height, 1);
        plane.Position = new Vector3(x, y, 0);
    }

    /// <summary>A linear gradient runs left to right; turned a quarter, it runs top to bottom.</summary>
    private static void Vertical(GradientPlane plane, float x, float y, float width, float height)
    {
        plane.Rotation = new Vector3(0, 0, MathF.PI / 2);
        plane.Scale = new Vector3(height, width, 1);
        plane.Position = new Vector3(x + width, y, 0);
    }

    private static Vector4 Hex(uint rgba) => new(
        (rgba >> 24 & 0xff) / 255f, (rgba >> 16 & 0xff) / 255f, (rgba >> 8 & 0xff) / 255f, (rgba & 0xff) / 255f);

    private static T[] Make<T>(int count, Func<T> create)
    {
        var items = new T[count];
        for (var index = 0; index < count; index++) items[index] = create();
        return items;
    }

    public void Dispose()
    {
        _text.Dispose();
        foreach (var owned in _owned) owned.Dispose();
    }

    /// <summary>A basalt column above the gap and one below, each capped with a carved band.</summary>
    private sealed class Pillar
    {
        private static readonly Vector4 Stone = Hex(0x3f3556ff);
        private static readonly Vector4 Edge = Hex(0x5d4f7dff);
        private static readonly Vector4 Cap = Hex(0x54476fff);
        private static readonly Vector4 Band = Hex(0xffcf86ff);
        private static readonly Vector4 Light = Hex(0xffb07047);

        private readonly BoxPlane _top = Column();
        private readonly BoxPlane _bottom = Column();
        private readonly BoxPlane _topCap = Capital();
        private readonly BoxPlane _bottomCap = Capital();

        public float X;
        public float GapY;
        public float Gap;
        public bool Scored;

        public void Paint()
        {
            var top = GapY - Gap / 2;
            var bottom = GapY + Gap / 2;
            // The low sun lights the stone nearest the gap.
            _top.Glow = new Vector4(0.5f, 1, 1.2f, 0.5f);
            _bottom.Glow = new Vector4(0.5f, 0, 1.2f, 0.5f);
            Place(_top, X, -20, PillarWidth, top + 20);
            Place(_bottom, X, bottom, PillarWidth, Ground - bottom + 12);
            Place(_topCap, X - CapOverhang, top - CapHeight, PillarWidth + CapOverhang * 2, CapHeight);
            Place(_bottomCap, X - CapOverhang, bottom, PillarWidth + CapOverhang * 2, CapHeight);
        }

        public void Render(in RenderContext context)
        {
            _top.Render(context);
            _bottom.Render(context);
            _topCap.Render(context);
            _bottomCap.Render(context);
        }

        private static BoxPlane Column() => new()
        {
            Color = Stone, BorderColor = Edge, BorderWidths = new Vector4(0, 5, 0, 5), BorderRadius = 6, GlowColor = Light
        };

        private static BoxPlane Capital() => new()
        {
            Color = Cap, BorderColor = Band, BorderWidths = new Vector4(3, 0, 3, 0), BorderRadius = 6
        };
    }

    private sealed class Ring
    {
        public readonly BoxPlane Plane = new() { Color = Vector4.Zero };
        public Vector2 At;
        public float Life;
        public float Strength;
    }

    private sealed class Spark
    {
        public readonly ColoredPlane Plane = new();
        public Vector2 At;
        public Vector2 Velocity;
        public float Life;
        public float Size;
    }

    private sealed class Popup(TextSlice slice)
    {
        public readonly TextSlice Slice = Init(slice);
        public Vector2 At;
        public float Life;

        private static TextSlice Init(TextSlice slice)
        {
            slice.UpdateManually = true;
            slice.Color = Vector4.Zero;
            return slice;
        }
    }

    private sealed class WorldCamera() : Camera(Vector3.Zero, new Vector2i(480, (int)World.Height));
}
