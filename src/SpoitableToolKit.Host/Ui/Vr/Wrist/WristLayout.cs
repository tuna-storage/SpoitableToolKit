namespace SpoitableToolKit.Host.Ui.Vr.Wrist;

/// <summary>Layout Mode の今。固定かどうかを決めるのは C# で、手首のページは写すだけ。</summary>
internal readonly record struct WristLayout(bool On, float Width);
