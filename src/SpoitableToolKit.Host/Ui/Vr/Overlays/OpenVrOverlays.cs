using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using SpoitableToolKit.Host.Ui.Vr.Overlays.Models;
using Valve.VR;

namespace SpoitableToolKit.Host.Ui.Vr.Overlays;

/// <summary>
/// OpenVR の実物。判断は持たず、呼ばれたことをそのまま OpenVR へ渡すだけ（試験に出せないので薄くしてある。何をいつやるかは <see cref="VrHost"/> にある）。
/// 使っているのは <c>VolcanicArts.Libs.OpenVR</c>（<c>Valve.VR</c> の生成バインディングと native の <c>openvr_api.dll</c> が同梱）。
/// </summary>
internal sealed class OpenVrOverlays(ILogger logger) : IVrOverlays
{
    private static readonly uint EventSize = (uint)Marshal.SizeOf<VREvent_t>();

    /// <summary>
    /// 繋いでよい状態か。OpenVR には触らずにプロセスを見る: <c>OpenVR.Init()</c> はもちろん <c>IsHmdPresent()</c> でさえ SteamVR を勝手に起動しうるので、2 秒ごとに呼ぶここで触ると、VR を使う気が無い人の機械で SteamVR が立ち上がる。
    /// vrserver だけでなく vrmonitor も居ることを見る。SteamVR が閉じるときは vrmonitor が先に居なくなり（vrserver のログ: <c>Lost master process ... Quitting all immediately</c>）、vrserver はそのあと数秒残る。
    /// vrserver だけを見ていると、その数秒に繋ぎに行って、口の閉じた vrserver を <c>OpenVR.Init</c> が起動し直す。
    /// </summary>
    public bool IsAvailable() => IsSteamVrRunning();

    /// <summary>vrserver だけが居て vrmonitor が居ない状態か。ログは入ったときの 1 回だけ。閉じる途中なら数秒で抜けるが、抜けないなら vrmonitor 無しで動く SteamVR で、こちらは繋ぎに行かない — その理由が 1 行も残らないと、繋がらない理由を追えない。</summary>
    private bool _serverWithoutMonitor;

    private bool IsSteamVrRunning()
    {
        try
        {
            var server = IsRunning("vrserver");
            var monitor = server && IsRunning("vrmonitor");

            var withoutMonitor = server && !monitor;

            if (withoutMonitor && !_serverWithoutMonitor)
            {
                logger.LogInformation("vrserver is running without vrmonitor (SteamVR is closing, or was started without it); not connecting until both are there");
            }

            _serverWithoutMonitor = withoutMonitor;

            return server && monitor;
        }
        catch
        {
            // 見に行けないだけ。次の周でまた試す。
            return false;
        }
    }

    private static bool IsRunning(string name) => App.ProcessCheck.IsRunning(name);

    public bool Start(out string? error)
    {
        var status = EVRInitError.None;

        // Overlay として繋ぐ。Scene だと自分が描画アプリになってしまう。
        OpenVR.Init(ref status, EVRApplicationType.VRApplication_Overlay);

        error = status == EVRInitError.None ? null : status.ToString();

        if (status != EVRInitError.None) return false;

        // 繋いだらすぐ渡す。UpdateActionState と PollNextEvent（QuitRequested）より前でないといけない。
        // 繋ぎ直すたびに渡す（Shutdown で手放している）。入力が使えなくても板は出す。
        StartInput();
        return true;
    }

    public void Stop()
    {
        _input = null;

        // 繋ぎを返せば SteamVR が板もサムネイルも消す。次に繋いだときの handle とは別物。
        _thumbnails.Clear();
        OpenVR.Shutdown();
    }

    /// <summary>action manifest の置き場。絶対パスで渡す（openvr.h: 相対だと断られる）。</summary>
    private static string ManifestPath => Path.Combine(AppContext.BaseDirectory, "vr", "actions.json");

    private static readonly uint ActiveSetSize = (uint)Marshal.SizeOf<VRActiveActionSet_t>();

    private static readonly uint DigitalSize = (uint)Marshal.SizeOf<InputDigitalActionData_t>();

    private static readonly uint PoseSize = (uint)Marshal.SizeOf<InputPoseActionData_t>();

    /// <summary>action の handle 一式。繋いでいて、manifest が通ったときだけある。</summary>
    private sealed record Actions(ulong Set, ulong Aim, ulong Click, ulong Grab, ulong Haptic, ulong Left, ulong Right)
    {
        public ulong Hand(VrHand hand) => hand == VrHand.Left ? Left : Right;
    }

    private Actions? _input;

    /// <summary>普段の周に渡す set。pointer だけ・優先度 0（読むだけで、VRChat からは取り上げない）。</summary>
    private readonly VRActiveActionSet_t[] _activeSets = new VRActiveActionSet_t[1];

    /// <summary>Layout Mode の周に渡す set。pointer と layout を、オーバーレイが使える優先度の下端で（<c>k_nActionSetOverlayGlobalPriorityMin</c>）。SteamVR は優先度の高い set が割り当てた入力元を、下のアプリ（VRChat）から外す。layout の set が引けなかったら null（Layout Mode でも預からない）。</summary>
    private VRActiveActionSet_t[]? _captureSets;

    /// <summary>高い優先度を SteamVR に断られた（繋いでいる間ずっと）。断られたら普段の set で読む。SteamVR の設定 <c>globalActionSetPriority</c> が切ってあると断られうる。こちらから書き換えない（設定はユーザーのもの）。</summary>
    private bool _priorityRefused;

    /// <summary>今の周で預かっているか。変わったときだけログに出す。</summary>
    private bool _capturing;

    /// <summary>UpdateActionState の最後の答え。変わったときだけログに出す（毎周呼ぶので）。</summary>
    private EVRInputError _lastUpdate = EVRInputError.None;

    /// <summary>手ごとに「狙いが取れているか」。変わったときだけ出す。</summary>
    private readonly bool[] _aiming = new bool[2];

    /// <summary>手ごとに「繋いでから押す（トリガー）を 1 度でも見たか」。1 回だけ出す。</summary>
    private readonly bool[] _clickSeen = new bool[2];

    private void StartInput()
    {
        Array.Clear(_aiming);
        Array.Clear(_clickSeen);
        _lastUpdate = EVRInputError.None;
        _captureSets = null;
        _priorityRefused = false;
        _capturing = false;

        if (OpenVR.Input is not { } input) return;

        var path = ManifestPath;
        var given = input.SetActionManifestPath(path);

        if (given != EVRInputError.None)
        {
            logger.LogWarning("SteamVR did not take the action manifest ({Error}, {Path}); the wrist cannot be pointed at", given, path);
            return;
        }

        ulong set = 0, aim = 0, click = 0, grab = 0, haptic = 0, left = 0, right = 0;

        var failed = new[]
        {
            ("/actions/pointer", input.GetActionSetHandle("/actions/pointer", ref set)),
            ("aim", input.GetActionHandle("/actions/pointer/in/aim", ref aim)),
            ("click", input.GetActionHandle("/actions/pointer/in/click", ref click)),
            ("grab", input.GetActionHandle("/actions/pointer/in/grab", ref grab)),
            ("haptic", input.GetActionHandle("/actions/pointer/out/haptic", ref haptic)),
            ("left hand", input.GetInputSourceHandle("/user/hand/left", ref left)),
            ("right hand", input.GetInputSourceHandle("/user/hand/right", ref right)),
        }.FirstOrDefault(handle => handle.Item2 != EVRInputError.None);

        if (failed.Item1 is not null)
        {
            logger.LogWarning("Could not look up the {Name} input handle ({Error}); the wrist cannot be pointed at", failed.Item1, failed.Item2);
            return;
        }

        _input = new Actions(set, aim, click, grab, haptic, left, right);
        _activeSets[0] = new VRActiveActionSet_t { ulActionSet = set };
        logger.LogInformation("SteamVR took the action manifest ({Path})", path);

        // layout の set は別に引く（上の並びに入れない）。引けなくても板は指せる — Layout Mode で入力を預からないだけ。
        ulong layout = 0;
        var found = input.GetActionSetHandle("/actions/layout", ref layout);

        if (found != EVRInputError.None)
        {
            logger.LogWarning("Could not look up the layout action set ({Error}); Layout Mode will not hold the controller inputs", found);
            return;
        }

        _captureSets =
        [
            new VRActiveActionSet_t { ulActionSet = set, nPriority = OpenVR.k_nActionSetOverlayGlobalPriorityMin },
            new VRActiveActionSet_t { ulActionSet = layout, nPriority = OpenVR.k_nActionSetOverlayGlobalPriorityMin },
        ];
    }

    public VrHands ReadHands(bool captureAll)
    {
        if (_input is not { } actions || OpenVR.Input is not { } input) return default;

        var capture = captureAll && _captureSets is not null && !_priorityRefused;
        var updated = input.UpdateActionState(capture ? _captureSets! : _activeSets, ActiveSetSize);

        if (capture && updated is EVRInputError.InvalidPriority or EVRInputError.PermissionDenied)
        {
            // 1 回だけ言って、普段の set に戻す（この周も読み直す）。断られたまま毎周頼むと、手が読めない周が続く。
            _priorityRefused = true;
            capture = false;
            logger.LogWarning(
                "SteamVR refused the high priority for Layout Mode ({Error}); the controller inputs still reach other apps while moving the overlays (SteamVR setting {Setting})",
                updated, OpenVR.k_pch_SteamVR_AllowGlobalActionSetPriority);

            updated = input.UpdateActionState(_activeSets, ActiveSetSize);
        }

        if (updated == EVRInputError.None && capture != _capturing)
        {
            _capturing = capture;
            logger.LogInformation(
                capture ? "Holding the controller inputs from other apps (Layout Mode)" : "The controller inputs reach other apps again");
        }

        if (updated != _lastUpdate)
        {
            _lastUpdate = updated;

            if (updated != EVRInputError.None) logger.LogWarning("UpdateActionState said {Error}", updated);
        }

        if (updated != EVRInputError.None) return default;

        return new VrHands(ReadHand(input, actions, VrHand.Left), ReadHand(input, actions, VrHand.Right));
    }

    private VrHandInput ReadHand(CVRInput input, Actions actions, VrHand hand)
    {
        var source = actions.Hand(hand);

        // 予測 0 秒。Poses() と同じ時点にしないと、手首の板と狙いが 1 枚ぶんずれる。
        var pose = default(InputPoseActionData_t);
        VrMatrix? aim = null;

        if (input.GetPoseActionDataRelativeToNow(actions.Aim, ETrackingUniverseOrigin.TrackingUniverseStanding, 0f, ref pose, PoseSize, source) == EVRInputError.None
            && pose.bActive && pose.pose.bPoseIsValid)
        {
            aim = ToVr(pose.pose.mDeviceToAbsoluteTracking);
        }

        var click = Pressed(input, actions.Click, source);
        var grab = Pressed(input, actions.Grab, source);

        NoteAiming(hand, aim is not null);

        if (click && !_clickSeen[(int)hand])
        {
            _clickSeen[(int)hand] = true;
            logger.LogInformation("The {Hand} hand pressed (the first press since connecting)", hand);
        }

        return new VrHandInput(aim, click, grab);
    }

    private static bool Pressed(CVRInput input, ulong action, ulong source)
    {
        var data = default(InputDigitalActionData_t);

        return input.GetDigitalActionData(action, ref data, DigitalSize, source) == EVRInputError.None && data.bActive && data.bState;
    }

    private void NoteAiming(VrHand hand, bool aiming)
    {
        if (_aiming[(int)hand] == aiming) return;

        _aiming[(int)hand] = aiming;
        logger.LogInformation(
            aiming ? "The {Hand} hand can point now" : "The {Hand} hand cannot point (not bound, or not tracked)", hand);
    }

    public void Pulse(VrHand hand, float seconds, float amplitude)
    {
        if (_input is not { } actions || OpenVR.Input is not { } input) return;

        input.TriggerHapticVibrationAction(actions.Haptic, 0f, seconds, 160f, amplitude, actions.Hand(hand));
    }

    public ulong CreateDashboard(string key, string name, float widthInMeters)
    {
        ulong main = 0;
        ulong thumbnail = 0;

        // 2 つ返る。板の方だけ使う（サムネイルはタブのアイコンで、今は既定のまま）。サムネイルは板を消すときに一緒に消す（Destroy）。
        if (Refused(OpenVR.Overlay.CreateDashboardOverlay(key, name, ref main, ref thumbnail), key)) return 0;

        if (thumbnail != 0) _thumbnails[main] = thumbnail;

        OpenVR.Overlay.SetOverlayWidthInMeters(main, widthInMeters);

        // 画素が何なのかを先に伝える。宣言しないと SteamVR が推測することになり、上げるたびに解釈し直される形になりうる。CEF が出すのはガンマ側。
        OpenVR.Overlay.SetOverlayTextureColorSpace(main, EColorSpace.Gamma);

        // 使う範囲も明示する。既定に頼ると、上げるたびに UV を引き直される形になりうる。
        var bounds = new VRTextureBounds_t { uMin = 0, vMin = 0, uMax = 1, vMax = 1 };
        OpenVR.Overlay.SetOverlayTextureBounds(main, ref bounds);

        // ShowOverlay は呼ばない。ダッシュボードの板は SteamVR 側が出し入れする（呼ぶと、絵を上げるたびにちらついた）。
        // ダッシュボードの外の板（手首）は逆で、呼ばないと出ない（SetShown）。

        return main;
    }

    /// <summary>
    /// ダッシュボードの板ごとのサムネイル。繋ぎを返せば SteamVR が全部消すが、繋いだまま板だけ消して作り直すこと（設定でダッシュボードを切って入れ直す）があるので、板と一緒に消す。
    /// 残すと、作り直すときに同じ key が使われていると断られうる。
    /// </summary>
    private readonly Dictionary<ulong, ulong> _thumbnails = [];

    /// <summary>板を作るのを断られたら、その理由を残す（呼ぶ側の「作れなかった」には理由が無い）。同じ key で作り直したときの <c>KeyInUse</c> と、SteamVR がまだ受けられないのとを、あとから見分けるため。</summary>
    private bool Refused(EVROverlayError result, string key)
    {
        if (result == EVROverlayError.None) return false;

        logger.LogWarning("SteamVR refused to create the overlay {Key} ({Result})", key, result);
        return true;
    }

    public void UploadTexture(ulong handle, nint texture)
    {
        if (texture == 0) return;

        var frame = new Texture_t
        {
            handle = texture,
            eType = ETextureType.DirectX,

            // CEF が出すのはガンマ側。Auto に任せると推測されることになる。
            eColorSpace = EColorSpace.Gamma,
        };

        var result = OpenVR.Overlay.SetOverlayTexture(handle, ref frame);

        // 断られた理由を残す。握りつぶすと「渡したのに見えない」で調べる先が無くなる。
        // 変わったときだけ出す — 毎周出すと、その行がアプリログの欄へ流れて再描画を起こし、それがまた次の行を生む。
        if (result == _lastResult) return;

        _lastResult = result;

        if (result != EVROverlayError.None) logger.LogWarning("SetOverlayTexture said {Result}", result);
    }

    private EVROverlayError _lastResult = EVROverlayError.None;

    public bool QuitRequested()
    {
        var received = default(VREvent_t);
        var (loaded, missing) = (0, 0);

        try
        {
            while (OpenVR.System is { } system && system.PollNextEvent(ref received, EventSize))
            {
                var type = (EVREventType)received.eventType;

                // 割り当ての読み込みの成否を残す。残さないと、manifest が壊れているのと「トリガーを引いていない」の見分けが付かない。
                switch (type)
                {
                    case EVREventType.VREvent_Input_ActionManifestLoadFailed:
                        logger.LogWarning("SteamVR could not load the action manifest");
                        break;

                    case EVREventType.VREvent_Input_BindingLoadFailed:
                        missing++;
                        break;

                    case EVREventType.VREvent_Input_BindingLoadSuccessful:
                        loaded++;
                        break;
                }

                if (type != EVREventType.VREvent_Quit) continue;

                // 返事をする。これをしないと SteamVR が「アプリを待っています」で止まる。
                OpenVR.System.AcknowledgeQuit_Exiting();
                return true;
            }

            return false;
        }
        finally
        {
            NoteBindings(loaded, missing);
        }
    }

    /// <summary>
    /// 割り当ての読み込みを 1 回ぶん 1 行にまとめて残す。
    /// 失敗は割り当てを書いていない種類の分が毎回まとまって来る（<c>rift</c> と Vive Tracker の各部位で 26 件。vrserver のログ:「(vive_tracker_waist) has no configured binding」）。
    /// 1 件ずつ警告にすると、肝心の「読めたか」が埋もれるので、1 つも読めなかったときだけ警告にする。指せるかどうかは手ごとの「can point now」の行で分かる。
    /// </summary>
    private void NoteBindings(int loaded, int missing)
    {
        if (loaded == 0 && missing == 0) return;

        if (loaded == 0)
        {
            logger.LogWarning("SteamVR loaded no bindings ({Missing} controller types have none)", missing);
            return;
        }

        logger.LogInformation(
            "SteamVR loaded {Loaded} bindings ({Missing} controller types have none — trackers and others we do not bind)",
            loaded, missing);
    }

    public void EnableInput(ulong handle, int width, int height)
    {
        OpenVR.Overlay.SetOverlayInputMethod(handle, VROverlayInputMethod.Mouse);

        // 板の画素数を単位にする。これを渡さないと 0〜1 の割合で返ってくる。
        var scale = new HmdVector2_t { v0 = width, v1 = height };
        OpenVR.Overlay.SetOverlayMouseScale(handle, ref scale);

        // 旗を立てないとスクロールは 1 度も飛んで来ない。
        OpenVR.Overlay.SetOverlayFlag(handle, VROverlayFlags.SendVRDiscreteScrollEvents, true);
    }

    public IReadOnlyList<VrPointer> PollPointer(ulong handle)
    {
        List<VrPointer> pointers = [];
        var received = default(VREvent_t);

        while (OpenVR.Overlay.PollNextOverlayEvent(handle, ref received, EventSize))
        {
            var mouse = received.data.mouse;
            var scroll = received.data.scroll;

            var pointer = (EVREventType)received.eventType switch
            {
                EVREventType.VREvent_MouseMove => Pointer(VrPointerKind.Move, mouse),
                EVREventType.VREvent_MouseButtonDown => Pointer(VrPointerKind.Down, mouse),
                EVREventType.VREvent_MouseButtonUp => Pointer(VrPointerKind.Up, mouse),

                EVREventType.VREvent_ScrollDiscrete => new VrPointer
                {
                    Kind = VrPointerKind.Scroll, ScrollX = scroll.xdelta, ScrollY = scroll.ydelta,
                },

                _ => null,
            };

            if (pointer is not null) pointers.Add(pointer);
        }

        return pointers;
    }

    private static VrPointer Pointer(VrPointerKind kind, VREvent_Mouse_t mouse) => new()
    {
        Kind = kind,
        X = mouse.x,
        Y = mouse.y,

        // 押されていないボタンは 0 で来る。Left として扱って構わない（Move では見ない）。
        Button = (EVRMouseButton)mouse.button switch
        {
            EVRMouseButton.Right => VrPointerButton.Right,
            EVRMouseButton.Middle => VrPointerButton.Middle,
            _ => VrPointerButton.Left,
        },
    };

    public void Destroy(ulong handle)
    {
        OpenVR.Overlay.DestroyOverlay(handle);

        // ダッシュボードの板ならサムネイルも消す（板と一緒にもう消えていれば、断られるだけ）。
        if (_thumbnails.Remove(handle, out var thumbnail)) OpenVR.Overlay.DestroyOverlay(thumbnail);
    }

    public ulong CreateOverlay(string key, string name, float widthInMeters)
    {
        ulong handle = 0;

        if (Refused(OpenVR.Overlay.CreateOverlay(key, name, ref handle), key)) return 0;

        OpenVR.Overlay.SetOverlayWidthInMeters(handle, widthInMeters);

        // ダッシュボードの板と同じ理由で、画素の解釈と使う範囲を先に伝える。
        OpenVR.Overlay.SetOverlayTextureColorSpace(handle, EColorSpace.Gamma);

        var bounds = new VRTextureBounds_t { uMin = 0, vMin = 0, uMax = 1, vMax = 1 };
        OpenVR.Overlay.SetOverlayTextureBounds(handle, ref bounds);

        return handle;
    }

    public void Attach(ulong handle, uint device, VrMatrix offset)
    {
        var matrix = ToOpenVr(offset);
        OpenVR.Overlay.SetOverlayTransformTrackedDeviceRelative(handle, device, ref matrix);
    }

    public void PlaceAbsolute(ulong handle, VrMatrix pose)
    {
        var matrix = ToOpenVr(pose);
        OpenVR.Overlay.SetOverlayTransformAbsolute(handle, ETrackingUniverseOrigin.TrackingUniverseStanding, ref matrix);
    }

    /// <summary>ここでは ShowOverlay を呼ぶ。<see cref="CreateDashboard"/> の「ShowOverlay は呼ばない」はダッシュボードの板だけの話で、ダッシュボードの外の板は呼ばないと 1 度も出ない（openvr.h:「Not applicable for Dashboard Overlays」）。</summary>
    public void SetShown(ulong handle, bool shown)
    {
        if (shown) OpenVR.Overlay.ShowOverlay(handle);
        else OpenVR.Overlay.HideOverlay(handle);
    }

    public void SetWidth(ulong handle, float widthInMeters) =>
        OpenVR.Overlay.SetOverlayWidthInMeters(handle, widthInMeters);

    public void SetAlpha(ulong handle, float alpha) =>
        OpenVR.Overlay.SetOverlayAlpha(handle, alpha);

    public bool DashboardOpen() => OpenVR.Overlay?.IsDashboardVisible() ?? false;

    public float? DisplayFrequency()
    {
        if (OpenVR.System is not { } system) return null;

        var error = ETrackedPropertyError.TrackedProp_Success;
        var hz = system.GetFloatTrackedDeviceProperty(
            OpenVR.k_unTrackedDeviceIndex_Hmd, ETrackedDeviceProperty.Prop_DisplayFrequency_Float, ref error);

        return error == ETrackedPropertyError.TrackedProp_Success ? hz : null;
    }

    public ulong CreateSprite(string key, string name, VrSprite sprite, float widthInMeters)
    {
        ulong handle = 0;

        if (Refused(OpenVR.Overlay.CreateOverlay(key, name, ref handle), key)) return 0;

        OpenVR.Overlay.SetOverlayWidthInMeters(handle, widthInMeters);

        // 板より手前に描く。板は既定の 0。
        OpenVR.Overlay.SetOverlaySortOrder(handle, 1u);

        var (pixels, size) = VrSprites.Draw(sprite);
        var pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);

        try
        {
            var result = OpenVR.Overlay.SetOverlayRaw(handle, pinned.AddrOfPinnedObject(), (uint)size, (uint)size, 4);

            if (result != EVROverlayError.None) logger.LogWarning("SetOverlayRaw said {Result} for the {Sprite}", result, sprite);
        }
        finally
        {
            pinned.Free();
        }

        return handle;
    }

    public (float U, float V)? Intersect(ulong handle, VrMatrix aim)
    {
        var (x, y, z) = aim.Position;
        var (dx, dy, dz) = aim.AxisZ;

        var given = new VROverlayIntersectionParams_t
        {
            vSource = new HmdVector3_t { v0 = x, v1 = y, v2 = z },
            vDirection = new HmdVector3_t { v0 = -dx, v1 = -dy, v2 = -dz },
            eOrigin = ETrackingUniverseOrigin.TrackingUniverseStanding,
        };

        var results = default(VROverlayIntersectionResults_t);

        return OpenVR.Overlay.ComputeOverlayIntersection(handle, ref given, ref results)
            ? (results.vUVs.v0, results.vUVs.v1)
            : null;
    }

    public uint? ControllerFor(VrHand hand)
    {
        if (OpenVR.System is not { } system) return null;

        var index = system.GetTrackedDeviceIndexForControllerRole(
            hand == VrHand.Left ? ETrackedControllerRole.LeftHand : ETrackedControllerRole.RightHand);

        return index == OpenVR.k_unTrackedDeviceIndexInvalid ? null : index;
    }

    /// <summary>毎周 1 回呼ぶ。配列は使い回す（周ごとに 64 個を作り直さない）。</summary>
    private readonly TrackedDevicePose_t[] _poses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];

    public IReadOnlyList<VrMatrix?> Poses()
    {
        var result = new VrMatrix?[_poses.Length];

        if (OpenVR.System is not { } system) return result;

        // 予測 0 秒。手首の板を出すかどうかの判断に使うだけなので、先読みは要らない。
        system.GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin.TrackingUniverseStanding, 0f, _poses);

        for (var i = 0; i < _poses.Length; i++)
        {
            if (!_poses[i].bPoseIsValid) continue;

            result[i] = ToVr(_poses[i].mDeviceToAbsoluteTracking);
        }

        return result;
    }

    private static VrMatrix ToVr(HmdMatrix34_t m) =>
        new(m.m0, m.m1, m.m2, m.m3, m.m4, m.m5, m.m6, m.m7, m.m8, m.m9, m.m10, m.m11);

    private static HmdMatrix34_t ToOpenVr(VrMatrix m) => new()
    {
        m0 = m.M0, m1 = m.M1, m2 = m.M2, m3 = m.M3,
        m4 = m.M4, m5 = m.M5, m6 = m.M6, m7 = m.M7,
        m8 = m.M8, m9 = m.M9, m10 = m.M10, m11 = m.M11,
    };
}
