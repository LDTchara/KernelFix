using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace KernelFix
{
    /// <summary>
    /// [EN] EXPERIMENTAL display tweaks - **for testing only**. Both features default
    /// to off and are deliberately kept out of KernelFix's regular fix list; they
    /// exist so frame-rate-dependent behaviour can be probed on demand.
    ///
    /// 1) DisableVSync - force swapInterval = 0 after every graphics device change.
    ///    Vanilla never touches VSync, so it always inherits FNA's default:
    ///    SynchronizeWithVerticalRetrace = true with PresentInterval.Default, which
    ///    on Windows asks for adaptive VSync (swapInterval = -1, logged as
    ///    "Using EXT_swap_control_tear VSync!"). Disabling it removes the frame-rate
    ///    ceiling, which also speeds up every frame-based timer in the game (same
    ///    root cause the RAM truncation fix compensates for) and causes tearing.
    ///
    /// 2) EnableFpsLimit - cap the main loop at FpsLimit frames per second by waiting
    ///    at the end of FNA's Game.Tick. Hacknet runs with IsFixedTimeStep = false, so
    ///    FNA never limits anything itself; without a cap, a VSync-less loop runs flat
    ///    out. Without disabling VSync the effective rate stays display-bound.
    ///
    /// Both hooks attach to FNA entry points that every path goes through:
    /// GraphicsDeviceManager.ApplyChanges (reached from device creation,
    /// ToggleFullScreen, Game1.setNewGraphics and window resizes) and Game.Tick.
    ///
    /// Note: a driver-side VSync override (NVIDIA control panel etc.) may still win
    /// over the swapInterval request - that is out of this plugin's hands.
    ///
    /// [CN] 实验性显示调整 —— **仅供测试**。两项功能默认关闭，刻意不列入 KernelFix 的
    /// 常规修复清单，只在需要时用于探查与帧率相关的行为。
    ///
    /// 1) DisableVSync —— 每次图形设备变更后强制 swapInterval = 0。原版从不触碰
    ///    VSync，因此始终继承 FNA 默认值：SynchronizeWithVerticalRetrace = true +
    ///    PresentInterval.Default，在 Windows 上会请求自适应垂直同步
    ///    （swapInterval = -1，日志 `Using EXT_swap_control_tear VSync!`）。取消后帧率
    ///    不再有上限，游戏里所有按帧推进的计时都会随之加快（RAM 截断修复所补偿的
    ///    现象即源于此），并会出现画面撕裂。
    ///
    /// 2) EnableFpsLimit —— 在 FNA 的 Game.Tick 末尾等待，把主循环限制在 FpsLimit
    ///    帧/秒。Hacknet 的 IsFixedTimeStep = false，FNA 自身不作任何限制；若不限帧，
    ///    取消 VSync 后主循环会跑满。未取消 VSync 时实际帧率仍受显示器约束。
    ///
    /// 两个挂钩都加在 FNA 的必经入口上：GraphicsDeviceManager.ApplyChanges（设备创建、
    /// ToggleFullScreen、Game1.setNewGraphics、窗口尺寸变化都会到达）与 Game.Tick。
    ///
    /// 注意：驱动面板层面的垂直同步覆盖（NVIDIA 控制面板等）可能仍然优先于
    /// swapInterval 请求 —— 那在本插件能力范围之外。
    /// </summary>
    internal static class DisplayTweaks
    {
        // [EN] Cached FNA entry points, resolved lazily so a failed lookup only
        //      costs one attempt.
        // [CN] 缓存的 FNA 入口，惰性解析，查找失败只尝试一次
        private static MethodInfo _setPresentationInterval;
        private static MethodInfo _sdlSetSwapInterval;
        private static bool _presentIntervalLookupDone;
        private static bool _presentIntervalUnavailableLogged;

        // [EN] One-shot log flags (ApplyChanges runs on every resolution change).
        // [CN] 一次性日志标志（ApplyChanges 在每次改分辨率时都会执行）
        private static bool _vsyncOverrideLogged;
        private static bool _vsyncOverrideFailedLogged;
        private static bool _limiterDisabledLogged;
        private static bool _limiterRealignLogged;
        private static int _limiterLoggedLimit = -1;

        // [EN] Frame limiter clock; _nextFrameTicks is the absolute target for the
        //      *next* Tick. Zero means "not scheduled yet".
        // [CN] 限帧时钟；_nextFrameTicks 是*下一次* Tick 的绝对目标时刻，0 表示尚未排期
        private static readonly Stopwatch _frameClock = Stopwatch.StartNew();
        private static long _nextFrameTicks;

        /// <summary>
        /// [EN] FpsLimit value meaning "disabled" (kept in sync with the config text).
        /// [CN] FpsLimit 表示"禁用"的取值（与配置说明一致）
        /// </summary>
        private const int FpsLimitDisabled = 0;

        // ===================================================================
        //  Registration / 注册入口
        // ===================================================================

        /// <summary>
        /// [EN] Patch the two FNA entry points. Called only while at least one of the
        ///      experimental switches is on, so a default install carries no extra
        ///      patches at all.
        /// [CN] 修补两个 FNA 入口。仅在至少一个实验开关打开时调用，因此默认配置
        ///      不会多出任何补丁。
        /// </summary>
        public static void Apply()
        {
            var harmony = KernelFix.Instance.HarmonyInstance;

            // --- VSync override hook / 取消 VSync 的挂钩 ---
            if (KernelFix.DisableVSync.Value)
            {
                try
                {
                    var applyChanges = AccessTools.Method(typeof(GraphicsDeviceManager), "ApplyChanges");
                    if (applyChanges == null)
                    {
                        KernelFix.Instance.Log.LogWarning(
                            "[KF] Display: GraphicsDeviceManager.ApplyChanges not found; VSync override unavailable.");
                    }
                    else
                    {
                        harmony.Patch(applyChanges,
                            postfix: new HarmonyMethod(typeof(DisplayTweaks), nameof(ApplyChangesPostfix)));
                        KernelFix.Instance.Log.LogInfo(
                            "[KF] Display: patch hook on GraphicsDeviceManager.ApplyChanges (VSync override).");
                    }
                }
                catch (Exception ex)
                {
                    KernelFix.Instance.Log.LogWarning(
                        $"[KF] Display: VSync override hook failed: {ex.Message}");
                }
            }

            // --- Frame limiter hook / 限帧的挂钩 ---
            if (KernelFix.EnableFpsLimit.Value)
            {
                try
                {
                    var tick = AccessTools.Method(typeof(Game), "Tick");
                    if (tick == null)
                    {
                        KernelFix.Instance.Log.LogWarning(
                            "[KF] Display: Game.Tick not found; frame limiter unavailable.");
                    }
                    else
                    {
                        harmony.Patch(tick,
                            postfix: new HarmonyMethod(typeof(DisplayTweaks), nameof(TickPostfix)));
                        KernelFix.Instance.Log.LogInfo(
                            "[KF] Display: patch hook on Game.Tick (frame limiter).");
                    }
                }
                catch (Exception ex)
                {
                    KernelFix.Instance.Log.LogWarning(
                        $"[KF] Display: frame limiter hook failed: {ex.Message}");
                }
            }
        }

        // ===================================================================
        //  VSync override / 取消垂直同步
        // ===================================================================

        /// <summary>
        /// [EN] Runs after ApplyChanges() has applied the requested PresentInterval.
        ///      ApplyChanges() is the single place where FNA groups its presentation
        ///      interval, so overriding here wins on every path (startup, resolution
        ///      change, full screen toggle).
        /// [CN] 在 ApplyChanges() 已应用请求的 PresentInterval 之后运行。ApplyChanges()
        ///      是 FNA 应用交换间隔的唯一位置，因此在这里覆盖可覆盖所有路径（启动、
        ///      改分辨率、切换全屏）。
        /// </summary>
        public static void ApplyChangesPostfix(GraphicsDeviceManager __instance)
        {
            if (KernelFix.DisableVSync?.Value != true)
                return;

            // [EN] ApplyChanges() returns early when no device exists yet - there is no
            //      GL context to talk to, so skip (a later call covers it).
            // [CN] 设备尚未创建时 ApplyChanges() 会提前返回，此时没有 GL 上下文可操作，
            //      直接跳过（后续调用会覆盖）。
            try
            {
                if (__instance == null || __instance.GraphicsDevice == null)
                    return;
            }
            catch
            {
                return;
            }

            if (ForceImmediatePresent())
            {
                if (!_vsyncOverrideLogged)
                {
                    _vsyncOverrideLogged = true;
                    KernelFix.Instance.Log.LogWarning(
                        "[KF] Display: VSync disabled (swapInterval = 0) - experimental, test-only setting.");
                }
                else
                {
                    KernelFix.Instance.Log.LogDebug(
                        "[KF] Display: VSync re-disabled after a graphics device change.");
                }
                return;
            }

            if (!_vsyncOverrideFailedLogged)
            {
                _vsyncOverrideFailedLogged = true;
                KernelFix.Instance.Log.LogWarning(
                    "[KF] Display: could not reach FNA's presentation-interval entry point; " +
                    "DisableVSync has no effect.");
            }
        }

        /// <summary>
        /// [EN] Ask FNA/SDL for PresentInterval.Immediate (swapInterval = 0).
        ///      Prefers FNA's own platform entry point (SDL2_FNAPlatform is internal,
        ///      hence reflection) and falls back to the raw SDL binding that FNA
        ///      bundles, so a renamed internal type still leaves a working path.
        /// [CN] 请求 PresentInterval.Immediate（swapInterval = 0）。优先走 FNA 自己的平台
        ///      入口（SDL2_FNAPlatform 是 internal，故用反射），失败时回退到 FNA 内嵌的
        ///      原始 SDL 绑定 —— 即使 internal 类型被改名也仍有可用路径。
        /// </summary>
        private static bool ForceImmediatePresent()
        {
            if (!_presentIntervalLookupDone)
            {
                _presentIntervalLookupDone = true;
                try
                {
                    var platformType = AccessTools.TypeByName("Microsoft.Xna.Framework.SDL2_FNAPlatform");
                    _setPresentationInterval = platformType == null
                        ? null
                        : AccessTools.Method(platformType, "SetPresentationInterval");
                    if (_setPresentationInterval == null)
                    {
                        var sdlType = AccessTools.TypeByName("SDL2.SDL");
                        _sdlSetSwapInterval = sdlType == null
                            ? null
                            : AccessTools.Method(sdlType, "SDL_GL_SetSwapInterval");
                    }
                }
                catch (Exception ex)
                {
                    KernelFix.Instance.Log.LogDebug(
                        $"[KF] Display: presentation-interval lookup failed: {ex.Message}");
                }
            }

            try
            {
                if (_setPresentationInterval != null)
                {
                    _setPresentationInterval.Invoke(null, new object[] { PresentInterval.Immediate });
                    return true;
                }
                if (_sdlSetSwapInterval != null)
                {
                    object result = _sdlSetSwapInterval.Invoke(null, new object[] { 0 });
                    return result is int rc && rc == 0;
                }
            }
            catch (Exception ex)
            {
                if (!_presentIntervalUnavailableLogged)
                {
                    _presentIntervalUnavailableLogged = true;
                    KernelFix.Instance.Log.LogDebug(
                        $"[KF] Display: forcing swapInterval failed: {ex.Message}");
                }
            }

            return false;
        }

        // ===================================================================
        //  Frame limiter / 帧率限制
        // ===================================================================

        /// <summary>
        /// [EN] Runs at the end of every Game.Tick. FNA's variable-timestep loop
        ///      (Hacknet sets IsFixedTimeStep = false) never sleeps, so waiting here
        ///      is what turns "as fast as possible" into "at most FpsLimit".
        ///      Sleep handles whole milliseconds, the last fraction is spun off with
        ///      Thread.Yield to keep the average close to the requested rate.
        /// [CN] 在每次 Game.Tick 末尾运行。FNA 的可变时间步循环（Hacknet 设了
        ///      IsFixedTimeStep = false）从不休眠，因此在这里等待才能把"能跑多快"
        ///      变成"至多 FpsLimit"。Sleep 只能按整毫秒等待，最后不足 1ms 的部分用
        ///      Thread.Yield 空转补齐，使平均帧率贴近目标值。
        /// </summary>
        public static void TickPostfix()
        {
            if (KernelFix.EnableFpsLimit?.Value != true)
            {
                _nextFrameTicks = 0;
                return;
            }

            int limit = KernelFix.FpsLimit?.Value ?? FpsLimitDisabled;
            if (limit <= FpsLimitDisabled)
            {
                if (!_limiterDisabledLogged)
                {
                    _limiterDisabledLogged = true;
                    KernelFix.Instance.Log.LogWarning(
                        $"[KF] Display: EnableFpsLimit is on but FpsLimit = {limit} (<= 0), limiter inactive.");
                }
                _nextFrameTicks = 0;
                return;
            }

            if (limit != _limiterLoggedLimit)
            {
                _limiterLoggedLimit = limit;
                KernelFix.Instance.Log.LogWarning(
                    $"[KF] Display: frame rate limited to {limit} fps - experimental, test-only setting.");
            }

            long frameTicks = (long)(Stopwatch.Frequency / (double)limit);
            if (frameTicks <= 0)
                return;

            long now = _frameClock.ElapsedTicks;
            if (_nextFrameTicks == 0)
                _nextFrameTicks = now;

            WaitUntil(_nextFrameTicks);

            // [EN] Advance the schedule from the previous target instead of "now" so
            //      the limiter does not drift; if a stall (window drag, breakpoint)
            //      pushed us a full frame behind, re-align rather than catching up.
            // [CN] 从上一个目标时刻推进而非"现在"，避免限帧持续漂移；若因卡顿（拖动
            //      窗口、断点）落后了一整个周期，则重新对齐而不是追赶。
            long after = _frameClock.ElapsedTicks;
            _nextFrameTicks += frameTicks;
            if (_nextFrameTicks < after)
            {
                _nextFrameTicks = after + frameTicks;
                if (!_limiterRealignLogged)
                {
                    _limiterRealignLogged = true;
                    KernelFix.Instance.Log.LogDebug(
                        "[KF] Display: frame limiter re-aligned after a long frame.");
                }
            }
        }

        /// <summary>
        /// [EN] Block until the stopwatch passes targetTicks. The loop always
        ///      terminates because the clock only moves forward.
        /// [CN] 阻塞到秒表走过 targetTicks。时钟只前进，因此循环必然结束。
        /// </summary>
        private static void WaitUntil(long targetTicks)
        {
            while (true)
            {
                long remaining = targetTicks - _frameClock.ElapsedTicks;
                if (remaining <= 0)
                    return;

                double remainingMs = remaining * 1000.0 / Stopwatch.Frequency;
                if (remainingMs > 1.5)
                    Thread.Sleep((int)(remainingMs - 1.0));
                else
                    Thread.Yield();
            }
        }
    }
}
