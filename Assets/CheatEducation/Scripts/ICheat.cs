// 各チートが実装する最小インターフェイス。CheatConsole がこれを集めてトグル UI を出す。
namespace CheatEducation
{
    public interface ICheat
    {
        string DisplayName { get; }
        bool CheatEnabled { get; set; }
        void DrawCheatOptions();   // IMGUI（OnGUI 文脈）から呼ばれる。不要なら空実装
        string StatusLine();       // 1 行の状態表示。不要なら null
    }
}
