using System;
using Hacknet;
using HarmonyLib;

namespace KernelFix
{
    /// <summary>
    /// [EN] Restore localization for content paths built by Pathfinder.
    ///
    /// Pathfinder replaces several vanilla loaders with its own implementations.
    /// One of them (Replacements/ActionsLoader, replacing
    /// RunnableConditionalActions.LoadIntoOS) resolves the target file through
    /// Pathfinder.Util.StringExtensions.ContentFilePath() instead of vanilla's
    /// LocalizedFileLoader.GetLocalizedFilepath(), and ContentFilePath() only
    /// concatenates a path prefix -- it never looks for a localized copy.
    ///
    /// Result: every ActionScript (and anything else resolved through
    /// ContentFilePath, e.g. mission goal files and custom themes) is read from
    /// the English original even when a localized file exists, so on a
    /// non-English install parts of the Labyrinths DLC story show up in English
    /// while the rest is localized. Vanilla does not have this problem.
    ///
    /// This fix appends vanilla's lookup to ContentFilePath(): if the produced
    /// path is under "Content/", ask LocalizedFileLoader for a localized
    /// variant. No localized file -> the original path is returned unchanged,
    /// so nothing else changes. Paths returned in extension mode live inside
    /// the extension folder (they never start with "Content/"), so extension
    /// loading is untouched.
    ///
    /// [CN] 恢复 Pathfinder 构造的内容路径的本地化。
    ///
    /// Pathfinder 用自己的实现替换了若干原版加载器。其中之一
    /// （Replacements/ActionsLoader，替换 RunnableConditionalActions.LoadIntoOS）
    /// 通过 Pathfinder.Util.StringExtensions.ContentFilePath() 解析目标文件，
    /// 而不是原版的 LocalizedFileLoader.GetLocalizedFilepath()；而
    /// ContentFilePath() 只拼接路径前缀，从不查找本地化副本。
    ///
    /// 后果：所有经由 ContentFilePath 解析的文件（ActionScript、任务 goals、
    /// 自定义主题等）即使存在本地化版本也会读取英文原文件 —— 于是在非英文
    /// 环境下，Labyrinths DLC 的部分剧情会显示英文而其余部分是本地化的。
    /// 原版没有这个问题。
    ///
    /// 本修复为 ContentFilePath() 补上原版的查找：若产出的路径位于 "Content/"
    /// 之下，则向 LocalizedFileLoader 询问其本地化版本。没有本地化文件时原样
    /// 返回，不影响其他行为。扩展模式下返回的路径位于扩展目录内（不以
    /// "Content/" 开头），因此扩展加载完全不受影响。
    /// </summary>
    internal static class ContentFilePathLocaleFix
    {
        private static bool _retryHooked;

        public static void Apply()
        {
            var type = AccessTools.TypeByName("Pathfinder.Util.StringExtensions");
            if (type == null)
            {
                KernelFix.Instance.Log.LogWarning(
                    "[KF] ContentLocale: Pathfinder.Util.StringExtensions not found (Pathfinder not installed?), skipping.");
                return;
            }

            var target = AccessTools.Method(type, "ContentFilePath", new[] { typeof(string) });
            if (target == null)
            {
                KernelFix.Instance.Log.LogWarning(
                    "[KF] ContentLocale: StringExtensions.ContentFilePath not found, skipping.");
                return;
            }

            KernelFix.Instance.HarmonyInstance.Patch(target,
                postfix: new HarmonyMethod(typeof(ContentFilePathLocaleFix), nameof(Postfix)));

            KernelFix.Instance.Log.LogInfo(
                "[KF] ContentLocale: patched Pathfinder.Util.StringExtensions.ContentFilePath");
        }

        /// <summary>
        /// [EN] Fallback: if PathfinderAPI happens to load after KernelFix, hook
        /// assembly loads and apply as soon as the type appears. The BepInDependency
        /// above normally makes this unnecessary.
        /// [CN] 兜底：若 PathfinderAPI 恰好晚于 KernelFix 加载，则挂程序集加载事件，
        /// 一旦出现该类型立即应用。上面的 BepInDependency 通常已使此路径不会走到。
        /// </summary>
        public static void ApplyWithRetry()
        {
            if (AccessTools.TypeByName("Pathfinder.Util.StringExtensions") != null)
            {
                Apply();
                return;
            }

            if (_retryHooked)
                return;
            _retryHooked = true;
            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
            KernelFix.Instance.Log.LogWarning(
                "[KF] ContentLocale: PathfinderAPI not loaded yet, will apply when it loads.");
        }

        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            bool found;
            try
            {
                found = args.LoadedAssembly.GetType("Pathfinder.Util.StringExtensions") != null;
            }
            catch
            {
                return; // reflection on some assemblies can throw / 某些程序集反射可能抛异常
            }

            if (!found)
                return;

            AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
            Apply();
        }

        /// <summary>
        /// [EN] Ask vanilla's LocalizedFileLoader for a localized variant of the
        /// produced content path. ContentFilePath() mixes separators
        /// (Path.Combine on Windows yields '\'), so normalize before testing.
        /// [CN] 为产出的内容路径向原版 LocalizedFileLoader 询问本地化版本。
        /// ContentFilePath() 的路径分隔符不统一（Windows 上 Path.Combine 产出
        /// '\'），因此先归一化再判断。
        /// </summary>
        private static void Postfix(ref string __result)
        {
            if (KernelFix.EnableContentFilePathLocaleFix?.Value != true)
                return;

            var normalized = __result?.Replace('\\', '/');
            if (normalized == null || !normalized.StartsWith("Content/", StringComparison.Ordinal))
                return;

            // No localized file -> GetLocalizedFilepath returns the input unchanged.
            // 没有本地化文件时 GetLocalizedFilepath 原样返回输入。
            __result = LocalizedFileLoader.GetLocalizedFilepath(normalized);
        }
    }
}
