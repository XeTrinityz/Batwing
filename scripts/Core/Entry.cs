namespace Trinity.Batwing;

[Script]
public sealed class BatwingMod : Script
{
    public override void Main() => Start();

    public override void OnLoad() => Start();

    public override void OnEnterGame() => Safe.Run("enter game", App.EnterWorld);

    public override void OnTick() => Safe.Run("tick", App.SdkTick);

    public override void OnDraw(BmSDK.Engine.Canvas canvas) => Safe.Run("frame", () => App.Frame(canvas));

    public override void OnKeyDown(Keys key) => Safe.Run("key", () => App.Key(key));

    public override void OnUnload() => Safe.Run("unload", App.Stop);

    private void Start()
    {
        Shared.Bind(Mod);
        string dir = Mod.DirectoryPath;
        Safe.Run("start", () => App.Start(dir));
    }
}
