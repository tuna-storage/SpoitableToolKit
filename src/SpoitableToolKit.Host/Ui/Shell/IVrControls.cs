namespace SpoitableToolKit.Host.Ui.Shell;

/// <summary>
/// 画面（手首の板・空間の窓）から VR へ頼む口。どれも周の外（CEF のスレッド）から呼ばれるので、実物は頼みを預かるだけにして周の中で片付ける。
/// </summary>
internal interface IVrControls
{
    /// <summary>空間の窓をその画面で開く。その画面を出しているなら閉じ、別の画面なら切り替える。</summary>
    void ToggleWindow(string view);

    void CloseWindow();

    /// <summary>空間の窓のページが自分で画面を切り替えた（サイドバー）。ショートカットの閉じる / 切り替えの判断に使う。</summary>
    void WindowViewing(string view);

    /// <summary>空間の窓がいま出している画面（閉じていれば null）。周の外から読んでよい。</summary>
    string? WindowView { get; }

    /// <summary>Layout Mode の外では効かない。</summary>
    void ResetWristPosition();

    void SetWristLayout(bool on);

    /// <summary>Layout Mode か（手首と空間の窓で共通）。周の外から読んでよい。</summary>
    bool InLayout { get; }

    /// <summary>負で小さく。Layout Mode の外では効かない。</summary>
    void ResizeWrist(int steps);
}
