using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AiGameStudio.ArcadeHub.Editor
{
    /// <summary>
    /// Собирает автомат в один исполняемый билд — на компьютере стойки Unity нет,
    /// туда едет готовая папка с .exe (founder, 2026-08-08).
    ///
    /// Хаб и все игры живут в ОДНОМ Unity-проекте (игры подключены пакетами), поэтому
    /// один билд содержит и лаунчер, и все игры: переходы между ними — это загрузки сцен
    /// внутри процесса, никаких отдельных .exe на игру.
    ///
    /// Запуск из терминала (см. bin/build-cabinet.sh — он и передаёт эти аргументы):
    ///   Unity -quit -batchmode -projectPath &lt;проект&gt; \
    ///         -executeMethod AiGameStudio.ArcadeHub.Editor.BuildCabinet.Windows \
    ///         -buildOutput &lt;папка&gt;
    /// Код выхода ненулевой, если билд не сложился — скрипт на это опирается.
    /// </summary>
    public static class BuildCabinet
    {
        private const string ExecutableName = "ArcadeCabinet.exe";
        private const string ProductName = "Аркадный автомат — 6 режимов суеты";
        private const string CompanyName = "AI Game Studio";

        /// <summary>Сборка под Windows-стойку (обычный ПК оператора).</summary>
        public static void Windows()
        {
            Run(BuildTarget.StandaloneWindows64, ExecutableName, "windows");
        }

        /// <summary>Сборка под macOS — чтобы проверить билд, не имея Windows под рукой.</summary>
        public static void Mac()
        {
            Run(BuildTarget.StandaloneOSX, "ArcadeCabinet.app", "mac");
        }

        private static void Run(BuildTarget target, string executable, string defaultFolder)
        {
            var output = ArgValue("-buildOutput")
                         ?? Path.Combine(Directory.GetParent(Application.dataPath)!.FullName,
                                         "Builds", defaultFolder);

            // Стойка — это витрина: имя окна и компании видит оператор в диспетчере задач
            // и в заголовке, «unity-game»/«DefaultCompany» там смотрятся браком.
            PlayerSettings.companyName = CompanyName;
            PlayerSettings.productName = ProductName;

            // Экран стойки — 1920x1080 (замер основательницы), полноэкранно и без рамки:
            // у автомата нет ни мыши, ни оконного менеджера в руках игрока.
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.resizableWindow = false;
            PlayerSettings.runInBackground = true;

            var scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

            if (scenes.Length == 0)
                throw new InvalidOperationException("В списке сцен билда пусто — собирать нечего.");

            // Первой обязана идти сцена лаунчера: билд стартует с неё, а не с чьей-то игры.
            var menuIndex = Array.FindIndex(scenes, p => p.EndsWith("/HubMenu.unity", StringComparison.Ordinal));
            if (menuIndex < 0)
                throw new InvalidOperationException("HubMenu.unity нет среди включённых сцен билда.");
            if (menuIndex > 0)
            {
                var menu = scenes[menuIndex];
                Array.Copy(scenes, 0, scenes, 1, menuIndex);
                scenes[0] = menu;
            }

            Directory.CreateDirectory(output);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(output, executable),
                target = target,
                options = BuildOptions.None,
            };

            Debug.Log($"[BuildCabinet] {target} → {options.locationPathName}\n" +
                      string.Join("\n", scenes.Select((p, i) => $"  {i}: {p}")));

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            if (summary.result != BuildResult.Succeeded)
            {
                // Батч-режим не «падает» сам по себе от неудачного билда — гасим руками,
                // иначе скрипт сборки отрапортует успех на пустой папке.
                Debug.LogError($"[BuildCabinet] БИЛД НЕ СОБРАН: {summary.result}, ошибок {summary.totalErrors}");
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log($"[BuildCabinet] ГОТОВО: {summary.outputPath}, " +
                      $"{summary.totalSize / (1024 * 1024)} МБ, {summary.totalTime}");
        }

        private static string ArgValue(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
                if (args[i] == name)
                    return args[i + 1];
            return null;
        }
    }
}
