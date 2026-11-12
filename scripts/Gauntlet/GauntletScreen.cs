using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.Engine;

namespace Trinity.Batwing;

// The deck drawn into a texture that the game's own hologram screen shows in place of a call's
// video. The screen is a real model in the world, so Batman's head and hand pass in front of it,
// and the game's hologram look and its fade in and out apply to it.
internal static class GauntletScreen
{
    // The screen shows a centred window of its video, not all of it, and fades out at the
    // window's edges. Measured in game with a grid drawn into the texture.
    private const float WindowLeft = 0.13f;
    private const float WindowRight = 0.87f;
    private const float WindowTop = 0.05f;
    private const float WindowBottom = 0.95f;

    // The window is stretched to the screen's shape, 44.4 by 25. The texture is made wide enough
    // that the window already has that shape, so nothing is stretched.
    private const float ScreenAspect = 44.4f / 25f;
    private const int TextureHeight = 1024;
    private const int TextureWidth = (int)(TextureHeight * ScreenAspect * (WindowBottom - WindowTop) / (WindowRight - WindowLeft));

    // The slot of the screen's material that shows the video.
    private static readonly FName Slot = new("MovieTexture");

    private static ScriptedTexture? _texture;
    private static MaterialInstanceConstant? _material;
    private static Texture? _before;
    private static bool _drawing;
    private static bool _warned;
    // The screen's models are made bigger while the deck is on them, and put back after.
    private static Model? _screen;
    private static Model? _frame;

    // How much bigger the screen is right now. The deck is laid out over the extra room rather
    // than drawn larger.
    public static float Size { get; private set; } = 1f;

    // Our texture is on the game's screen.
    public static bool Active => _material != null;

    // Puts our texture on the screen the projector just turned on. False leaves the screen as it is.
    public static bool Take(RGauntletProjector projector)
    {
        if (Active)
            return true;

        if (projector.HologramMovieMaterial is not { IsValid: true } material || OurTexture() is not { } texture)
            return false;

        // A changed game would show its video some other way. Another mod's deck still fading
        // out on the screen also lands here.
        if (!material.GetTextureParameterValue(Slot, out Texture before) || before == null || projector.PrimaryRenderTarget is not { } video || before.Ptr != video.Ptr)
        {
            Warn("The gauntlet screen doesn't show its video where expected.");
            return false;
        }

        material.SetTextureParameterValue(Slot, texture);
        _material = material;
        _before = before;

        _screen = Model.Of(projector.ScreenHologramMovieMesh);
        _frame = Model.Of(projector.ScreenHologramMesh);
        Grow();
        return true;
    }

    private static void Grow()
    {
        Size = Opt.GauntletSize.Number;
        _screen?.Fit(Size);
        _frame?.Fit(Size);
    }

    // Puts the game's own video back on the screen.
    public static void Give()
    {
        if (_material is { IsValid: true } material && _before is { IsValid: true } before)
            material.SetTextureParameterValue(Slot, before);

        _screen?.Fit(1f);
        _frame?.Fit(1f);
        _screen = null;
        _frame = null;
        _material = null;
        _before = null;
        Size = 1f;
    }

    // One of the screen's models and how the game had it.
    private sealed class Model
    {
        private readonly StaticMeshComponent _mesh;
        private readonly float _scale;
        private readonly Vector3 _translation;
        private readonly float _middle;

        private Model(StaticMeshComponent mesh, float middle)
        {
            _mesh = mesh;
            _scale = mesh.Scale;
            _translation = mesh.Translation;
            _middle = middle;
        }

        public static Model? Of(StaticMeshComponent? mesh)
        {
            if (mesh is not { IsValid: true })
                return null;

            // How far the middle of the screen is from the model's pivot, which sits in the middle
            // of its top edge. Without its size the screen just grows downwards.
            float middle = 0f;
            if (mesh.StaticMesh is { IsValid: true } model && World.Player is { } pawn)
                middle = pawn.GetStaticMeshBounds(model).Origin.Y;

            return new Model(mesh, middle);
        }

        // The model is moved up as it grows, so its middle stays where it was and it grows evenly
        // up and down. Growing only upwards ran into the compass, and only downwards went behind
        // the forearm.
        public void Fit(float size)
        {
            if (!_mesh.IsValid)
                return;

            float scale = _scale * size;
            if (MathF.Abs(_mesh.Scale - scale) > 0.001f)
                _mesh.SetScale(scale);

            Vector3 want = _translation with { Y = _translation.Y - (size - 1f) * _middle * _scale };
            if (Vector3.DistanceSquared(_mesh.Translation, want) > 0.0001f)
                _mesh.SetTranslation(want);
        }
    }

    public static void Release()
    {
        Give();
        if (_texture is { IsValid: true } texture)
            texture.RemoveFromRoot();

        _texture = null;
    }

    // The texture is only drawn again when asked, so it is asked every frame it is on show.
    public static void Refresh()
    {
        if (!Active)
            return;

        if (_texture is { IsValid: true } texture)
            texture.bNeedsUpdate = true;

        // Also follows the size setting while the deck is open.
        Grow();
    }

    private static ScriptedTexture? OurTexture()
    {
        if (_texture is { IsValid: true })
            return _texture;

        // Create makes an object of the class it runs on, so running it on the scripted texture's
        // default object makes a scripted texture. It is kept from the garbage collector while
        // it is off the screen.
        TextureRenderTarget2D made = TextureRenderTarget2DStatics.Create(ScriptedTexture.DefaultObject, TextureWidth, TextureHeight, Texture.EPixelFormat.PF_A8R8G8B8);
        if (made is not ScriptedTexture { IsValid: true } texture)
        {
            Warn("A texture for the gauntlet screen couldn't be made.");
            return null;
        }

        texture.AddToRoot();
        _texture = texture;
        return texture;
    }

    private static void Warn(string text)
    {
        if (_warned)
            return;
        _warned = true;
        Log.Info(text);
    }

    // The engine asks a scripted texture to draw itself through this when it needs drawing.
    [Redirect(typeof(ScriptedTexture), nameof(ScriptedTexture.Render))]
    private static void Render(ScriptedTexture self, Canvas canvas)
    {
        if (_texture == null || self.Ptr != _texture.Ptr)
        {
            self.Render(canvas);
            return;
        }

        if (_drawing)
            return;

        // Gfx is shared with the HUD, so whatever it was drawing on is put back after.
        _drawing = true;
        Canvas was = Gfx.C;
        float scale = Gfx.Scale, alpha = Gfx.Alpha;
        try
        {
            if (!Gfx.Begin(canvas, out Font? font))
                return;

            try
            {
                // The deck fills the window the screen shows.
                float left = WindowLeft * canvas.ClipX;
                float top = WindowTop * canvas.ClipY;
                float width = (WindowRight - WindowLeft) * canvas.ClipX;
                float height = (WindowBottom - WindowTop) * canvas.ClipY;
                // A bigger screen spreads the texture wider, so the units shrink with the size once
                // to stay the same in game and again to get smaller as the screen grows.
                float per = Size / Opt.GauntletText.Number;
                float zoom = width / (GauntletView.BaseWidth * Size * per);
                GauntletView.Draw(left, top, zoom, width / zoom, height / zoom, Size, per, Overlay.DeckAnim);
            }
            finally
            {
                canvas.Font = font;
            }
        }
        catch (Exception ex)
        {
            Log.Error("gauntlet screen", "Gauntlet screen", ex);
        }
        finally
        {
            Gfx.C = was;
            Gfx.Scale = scale;
            Gfx.Alpha = alpha;
            _drawing = false;
        }
    }
}
